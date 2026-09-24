# The looting flow, end to end

Written to answer a specific failure: *"I extracted with a full kit of two
weapons and full armour, and went back to my stash with none of it."*

That report is accurate, the cause is identified, and it is reproducible in
five lines. §6 has it. The rest of this document is the map you need to read
that diagnosis, and to see the other places the same class of mistake can hide.

Scope: everything that moves an ITEM, from the moment the sim invents it to the
moment it lands in the stash between runs.

---

## 1. Where an item can be

At any instant, an item id is in exactly one of these places. Everything below
is about moving between them.

| Place | Lives in | Sim state? | Survives the run? |
|---|---|---|---|
| A guard's kit | `SimWorld.Guards[i].Kit` | yes, hashed | no |
| A chest | `SimWorld.Chests[i].Kit` | yes, hashed | no |
| A ground pile | `SimWorld.Ground[i].Kit` | yes, hashed | no |
| The mission pack | `SimWorld.Pack` (`PackGrid`) | yes, hashed | **yes, on extraction** |
| Worn, in the sim | `SimWorld.Loadout` | yes, hashed | **no — see §6.1** |
| The stash grid | `stash.gd` `grid` | no, meta | yes, on disk |
| A stash worn slot | `stash.gd` `_slots` | no, meta | yes, on disk |

The line between rows 1–6 and rows 7–8 is the whole architecture. Everything
above it is the SIM: integer, hashed, recorded in replays, thrown away when the
run ends. Everything below it is the META layer: GDScript, saved to
`user://stash.txt`, and invisible to the sim.

**An item crossing that line is the dangerous moment, and it happens in exactly
one place: `main.gd:_settle_run`.** Every bug in §6 is a variation on that
crossing being incomplete.

---

## 2. Where items come from

`SimWorld` mints every item during construction, from the loot stream:

- `RollGuardKits()` — each guard gets a kit; the vest in it is the vest he is
  actually wearing (`Actor.Armour` is set from the same entry).
- `RollChests()` — runs AFTER the guard kits on purpose, so adding a chest to a
  level does not re-roll every body on it.
- Objective sites (`!`) hold exactly one `GearCatalog.ObjectiveId` (900) and
  draw NOTHING from the loot stream, so adding one does not disturb the supply.

Two later sources exist:
- `StepSpawn` — the F8 developer menu, conjuring an item into the pack.
- `StepDrop` — moving an item from the pack to a ground pile.

---

## 3. Taking: input → intent → tick

This is the part that is carefully built, and it is not where the bug is.

### 3.1 Reaching (game/, per frame)

`main.gd:_physics_sim` each frame:

1. `_bridge.NearestLootTarget()` → a target index, or -1.
2. If the `loot` action (G) is held and a target is in reach, `rummaging` is
   true and `_loot.show_for(index, _bridge.GetLootKit(index))` raises the panel.
3. Identifying is the cursor RESTING on a row — `loot_panel.gd` runs that dwell
   itself, off frame delta. It feeds no hash and is not recorded.
4. Taking is a RIGHT-CLICK: `_loot.click_at(mouse)` returns `row + 1`, or 0.
   It returns 0 for a row that has not been identified yet.

The right button specifically, because taking the LAST item empties the kit,
which ends the rummage on that same tick — and the left button still being held
then read as fire, so grabbing the last thing out of a chest shot it.

### 3.2 The intent

`pick` is passed as `InputFrame.LootPick` (0 for none, else kit index + 1). It
is hashed and written to the replay as a `pN` token. This is why per-item
looting survives a replay at all: the sim does not choose, the player does, and
the choice is recorded.

`main.gd` also WITHHOLDS the fire and aim flags for as long as the panel is up,
so a click cannot also empty a magazine into the body.

### 3.3 The tick

`SimWorld.StepLoot`:

```
if LootPick == 0            -> nothing
target = NearestLootTarget()          // recomputed, NOT the panel's index
TryLootTarget(target, out x, y, kit)
index = LootPick - 1
if index out of range       -> ignored (a stale pick, kit shrank)
if Pack.AutoPlace(item) == None -> Log PackFull, item STAYS on the body
else                        -> Log Looted, kit.RemoveAt(index)
```

`LootTargetCount` is `Guards.Count + Chests.Count + Ground.Count` — one index
space, so the panel, the intent and the tick have no idea which kind of thing
they are working on.

---

## 4. Holding: the pack

`PackGrid` is a spatial grid sized by the worn backpack (`Loadout.PackW/PackH`).
No backpack means a 0x0 grid, which means nothing can be carried at all.

It is sim state: hashed, and carried in replays. That is why the pack panel is
READ-ONLY except for the two operations below — a drag cannot be recorded into
an `InputFrame`, so rearranging the pack mid-mission would desync every replay.

---

## 5. Moving it again, mid-mission

Both of these are RECORDED INTENT, staged by the screen and performed by the
tick. Neither is done by the menu.

### 5.1 Dropping — `InputFrame.DropPick`

`stash_screen._drop_on_floor()` sets `pending_drop` and emits `dropped`;
`main.gd` hands it to the sim on the next tick as `DropPick` (placement + 1,
replay token `dN`). `StepDrop` removes it from the pack and puts it in a ground
pile, merging into an existing pile within `Tune.DropMergeDist`.

A ground pile is an ordinary loot target — same G, same panel, same index space.

### 5.2 Equipping — `InputFrame.EquipPick`

`stash_screen._equip_from_pack(slot)` sets `pending_equip` and emits `equipped`.
The pick is PACKED: low byte the placement + 1, high byte the `GearSlot`.
Replay token `eN`.

`SimWorld.StepEquip` makes it a TRADE: the item leaves the pack, and whatever
comes off goes INTO the pack. If the displaced item will not fit, the whole
equip is refused. The backpack is a special case (`EquipBackpack`) — it is the
container the others live in, so the new bag is test-fitted into a scratch grid
and committed only if everything still fits.

**This is the step that created the bug.** It moves an item from the pack into
`SimWorld.Loadout`, and §6.1 is about what happens to it there.

---

## 6. Leaving the mission — and the bugs

`main.gd:_settle_run(world)` is the ONLY place an item crosses from sim to meta.

```gdscript
if _over_code != 1:                      # 1 == escaped; 2 == dead
    _campaign.settle_loss(file); return  # the pack is gone

var carried = _bridge.GetPackItems()     # <-- the PACK, and only the pack
for item_id in carried:
    if _stash.add(item_id) == _stash.NONE:
        sold += 1; unsold += _bridge.GearPrice(item_id)
    else:
        kept += 1
```

### 6.1 FIXED — worn gear was destroyed, and the gear it replaced duplicated

Fixed by `stash.reconcile_worn`: the sim's worn kit is snapshotted when the run
begins and compared at extraction, and every slot or rail the field changed is
written back into the stash before the pack is banked. Pinned by
`inventory_check.gd:_check_field_equip_comes_home`. The original diagnosis:

**`_settle_run` reads `GetPackItems()` and nothing else. `SimWorld.Loadout` is
never reconciled back into the stash.**

Anything equipped in the field left the pack (§5.2) and is therefore in neither
place the settle looks. It is destroyed. Worse, the item it displaced went INTO
the pack, settles into the stash grid — while `stash._slots` still holds that
same item in its worn slot, because the stash was never told anything changed.

Reproduced exactly:

```
deployed with: primary=Glock
in the field:  held=AK-47  pack=[100]        # looted an AK, equipped it
after settle:  stash primary=Glock  grid holds=[Glock]
RESULT: AK-47 in stash = 0   Glock in stash = 2
```

One rifle destroyed, one pistol duplicated, per field equip. With two weapons
and a vest equipped in the field, all three are destroyed — which is the
reported symptom exactly.

Note this is not a looting bug at all. Looting works; the bug is that the
mid-mission EQUIP feature was built with no persistence path, and `_settle_run`
was never taught that `Loadout` is now a place items can be.

### 6.2 FIXED — the objective ends up in the stash

`GetPackItems()` returns every live placement, including
`GearCatalog.ObjectiveId` (900). `_settle_run` added all of them, and nothing
filtered it — so a 2x2 sealed case accumulated in the stash after every
completed mission, permanently eating grid space, and the item count reported
one more than the player had actually recovered.

The banking loop now lives in `stash.bank_recovered`, which HANDS THE CASE IN
rather than keeping it, and returns `[kept, fenced, fencedValue, handedIn]` so
the debrief can say so. It moved out of `main.gd` precisely because of §7: a
harness can reach a method on the stash and cannot reach `_settle_run`.
`inventory_check.gd` now asserts the case is neither stashed nor owned after a
completed run.

### 6.3 FIXED — the panel's target and the tick's target could differ

Reproduced (two chests equidistant from the player, a pick sent while
stepping toward the second took from the second) and fixed: `SimWorld.Step`
resolves the loot target at the TOP of the tick, from the state `GetLootTarget`
read, before the player's move. Pinned by `Economy` "the loot pick takes from
the kit on screen". What follows is the original analysis.

`_loot.show_for(lt[4], ...)` draws the kit of the target that was nearest LAST
FRAME. `StepLoot` calls `NearestLootTarget()` again on the tick. A frame of
movement between them — or a body being looted empty, which removes it from
consideration entirely — means the row the player clicked can index a different
kit than the one they were reading.

`StepLoot` guards the crash (`index >= kit.Count` is ignored) but not the
mistake: if the other kit is long enough, the player takes an item they did not
choose. Not reproduced; flagged because the two selections are independent
computations of the same thing, which is precisely the shape of §6.1.

### 6.4 BY DESIGN, worth confirming — ground piles are abandoned

Anything dropped on the floor stays there. `_settle_run` reads only the pack,
so extraction never collects ground piles. This is intended (dropping is how you
make room), but it means "drop it and pick it up on the way out" silently fails
if the player never returns for it.

### 6.5 CHANGED — death now destroys everything on you

It used to settle a loss and return, leaving the stash's worn slots untouched:
the kit you DEPLOYED with survived death, so walking in wearing everything you
owned was free.

`_settle_run` now calls `stash.lose_kit()` on any outcome but extraction, which
clears every worn slot, both attachment sets and the carry list. The stash GRID
is untouched — what you leave at base is safe, and what you carry is not.

This is also what makes §4's new carry list a decision: you choose what to
risk, and the bag you packed at base is lost with the rest of it.

---

## 7. Why 1971 tests did not catch this

Worth stating plainly, because it is the actionable part.

Two independent gaps, and both are precise:

**1. `main.gd:_settle_run` is not exercised by anything.** `campaign.settle()`
— the ledger ARITHMETIC — is well covered (`inventory_check.gd`,
`fuzz_check.gd`). What is not covered is the ITEM HANDOVER that wraps it: the
loop in `_settle_run` that walks `GetPackItems()` into `_stash.add()`. The sim
harness covers `sim/` and stops at the bridge; `inventory_check.gd` covers the
stash, the screens and the campaign as isolated objects. Nothing drives the
JOIN, which is the one place items cross the architecture's central boundary.
The smoke test does load `main.gd`, but the player never moves, so no run ever
ends and `_settle_run` never executes.

**2. `Fuzz.Conservation` deliberately skips equips.** It strips `SpawnItem` and
`EquipPick` out of every frame before stepping, with the comment "both
legitimately change the count". That was true of spawning and wrong about
equipping: an equip is a TRADE and should conserve exactly. And `TotalItems`
counts pack + bodies + chests + ground, not `Loadout` — so even with equips
enabled it would have called the AK "gone" rather than "worn", and reported a
conservation failure for the wrong reason.

Both halves have to change together: count `Loadout` as a place items live, and
then stop excluding equips. Done in that order, the fuzzer would have caught
§6.1 on its first run.

DONE, and it found two more ways to lose an item (trousers into the unwired
legs slot, and an attachment fitted over a masked rail). It also found that the
conservation streams had never moved an item at all: they began with an empty
pack and the player never reached a body or a chest. They now start with
random gear in the bag, and assert the streams really did loot, drop and
equip.

---

## 8. Suggested order of work

All four are DONE (§6.1 and §6.2 first; §7 and §6.3 in the review sweep that
also fixed the two item losses §7 turned up).

1. **§6.1** — reconcile `SimWorld.Loadout` into the stash at settle, so worn
   gear comes home and the displaced item is not duplicated. Data loss on every
   field equip; this is the reported bug.
2. **§6.2** — filter the objective in `_settle_run` (or in `GetPackItems`).
   One line; contradicts documented behaviour today.
3. **§7** — teach `Fuzz.TotalItems` to count `Loadout`, and add a harness that
   drives the sim → stash handover. Without this, the next feature that moves
   items will lose them the same way.
4. **§6.3** — have the tick honour the target the panel showed, or have the
   panel re-read the target the tick would choose.
