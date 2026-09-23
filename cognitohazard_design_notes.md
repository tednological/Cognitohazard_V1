# Project
Godot 4.x top-down 2D game, ported from `cognitohazard_combat_testbed.html`
per `cognitohazard_port_spec.md`. The spec's architecture section is binding.

# Language split (spec §1.1–1.2)
- `sim/`   C#. Pure RefCounted, zero Node deps, runs headless. Roslyn is the
           verification surface; that is the whole reason for C# here.
- `game/`  GDScript. Node2D presentation + input. Reads sim state, never writes
           game logic.
- `tests/` Headless harness. Drives `sim/` only, never imports `game/`.
Boundary: sim exposes a plain data snapshot per frame. No C# → GDScript
callbacks in the hot path.

# Godot binary — the .NET build ONLY
`$G` below = `/Applications/Godot-4.6-dotnet.app/Contents/MacOS/Godot`
(4.6.2.stable.mono). Quote any path containing a space.
`Godot-4.6-standard.app` cannot open this project — no C# support, so
`res://game/SimBridge.cs` has no resource loader. Symptoms: a red "This project
needs the .NET build" screen, or `Preload file ... has no resource loaders`.
Both apps share one project manager, so it is easy to launch the wrong one.
Editor: `$G --editor --path .`

# Commands
- GDScript syntax check: `$G --headless --check-only --script res://path/f.gd`
- C# build (must pass before claiming done): `dotnet build`
- Smoke test (300 frames then exit): `$G --headless --path . --quit-after 300`
- Import assets after adding files: `$G --headless --path . --import`
- Sim harness (non-zero exit on failure, spec §4.3):
  `dotnet run --project tests/Cognitohazard.Tests.csproj`
  A plain .NET console app, not a Godot script: sim/ has zero Node deps, so it
  runs windowless. Re-bake golden hashes with `-- --record` after an
  INTENTIONAL sim change, in the same commit.
- Audio harness: `$G --headless --path . --script res://tests/audio_check.gd`
- Game-bridge harness — level editor, replay record/playback, and a
  project-wide lint for unsupported GDScript `%%` conversions (all on SimBridge
  in game/, unreachable from the sim harness):
  `$G --headless --path . --script res://tests/editor_check.gd`
- Inventory harness — the campaign stash (grid packing, item catalog, gear
  slots, save format; meta layer per rpg_extension_plan §1). Also asserts
  `item_catalog.gd` still mirrors `sim/Loadout.cs` names and ids:
  `$G --headless --path . --script res://tests/inventory_check.gd`
  It opens REAL screens, and stash_screen's close paths `_save()` over the
  PLAYER'S `user://stash.txt`. `_restore_player_stash` backs that file up for
  the whole run and asserts it is put back BYTE FOR BYTE; any new test that
  opens a stash screen is covered by that one guard.
- Game-layer fuzz: `$G --headless --path . --script res://tests/fuzz_check.gd`

# Bug-finding suites (vs. the regression suites everywhere else in tests/)
- `tests/Fuzz.cs` — thousands of RANDOM input streams, invariants checked EVERY
  tick: player in the level, health/magazines in range, guards neither created
  nor destroyed, pack always a valid packing, hash pure. Plus determinism,
  replay round-trip, and item conservation (loot/drop only MOVE items).
- `tests/Robustness.cs` — adversarial text: every parser truncated at 120
  offsets, byte-flipped, fed every printable char as a grid, handed numbers at
  every integer boundary. The bar is not "does not throw" but "the result can
  still be PLAYED".
- `tests/Exhaustive.cs` — the cross product: all 30,720 weapon × attachment
  loadouts (zero magazines, zero cooldowns, negative cones); every gear item;
  every item × every slot run THROUGH the sim, so `CanEquipMidRun` and
  `StepEquip` cannot drift apart.
- `tests/fuzz_check.gd` — the same for game/: random operation sequences
  against the packing grid, stash, campaign parser, HUD layout, mission math.

All four report ONE assertion per invariant, naming the first offending seed,
tick or combination. A fuzzer that prints a line per case buries the failure.

# Replays
Every live run is recorded to `user://replays`: on the tick it ends, and again
on quit, so a run abandoned by closing the window survives. F9 saves mid-run.
Two saves in one second get a -2, -3 suffix.
- Verify against the CURRENT sim (names the first diverging tick):
  `dotnet run --project tests/Cognitohazard.Tests.csproj -- --verify <r.txt>`
- Watch (space pause, left/right step, up/down speed, enter rewind):
  `$G --path . -- --replay last`

# Level editor
TAB toggles. 1-7 tools, LMB paint, RMB erase, ctrl+Z undo, S save to levels/,
L load next, N new, F2 rename, ENTER playtest.
Arrows/MMB pan, -/= or wheel zoom, F fit, ctrl+arrows resize (see Editor view).

# Screens and the flow between them
Boot goes to the TITLE (`title_screen.gd`), not into a mission:

    title ── Continue ──┐
          ── New Game ──┴─> STASH ── ENTER ──> mission ── F5/debrief ──> stash
          ── Options ────>  options              │
          ── Level Builder ─> editor             └── ESC ──> title

- TITLE: Continue is DISABLED, not hidden, without `user://campaign.txt` — a
  menu whose rows move with state cannot be learned. New Game wipes money,
  stash and mission history in ONE place (`main.gd:_wipe_campaign`).
- STASH (`stash_screen.gd`): INVENTORY left, MISSION SELECT right — what you
  carry and where you take it are one decision. The MOUSE works the inventory
  half and the KEYBOARD the mission half, which is what lets them share a
  screen with no focus concept.
- OPTIONS (`options_screen.gd`): sound, fullscreen, HUD editor, erase campaign.
  The erase is two presses (the row arms, next ENTER does it) and disarms if
  you move off it.

`start_screen.gd` and `equipment_screen.gd` are GONE; the latter became
`stash_screen.gd`. Loadout PRESETS went with it — free kits stopped making
sense once gear had to be bought.

A screen's `closed` signal means ONE thing: THE PLAYER BACKED OUT, and its
handler returns to the title. A screen left because something ELSE is taking
over must use `close_screen_silent()` — so `_on_deploy` does, and so does
`_on_hud_from_options` (with `_on_hud_closed` returning to Options). Both were
bugs; both are pinned by `inventory_check.gd:_check_screen_exits`.

# Developer menu (F8)
`dev_menu.gd`. Spawns ANY item in `sim/GearCatalog.cs` — the table UNFILTERED,
which is the point: it reaches what the shop hides (the price-0 starter Glock
and the mission objective, both excluded by `GetShopStock`). Left/right filter
by kind, up/down or WHEEL choose, ENTER spawns, F8/ESC closes. Where it lands,
which the screen states rather than making you infer:
- MID-RUN → the MISSION PACK, on the next tick.
- OTHERWISE (title, stash, after a death) → the STASH, at once.

The mid-run half is RECORDED INTENT. `InputFrame.SpawnItem` is 0 for none, else
a GearCatalog ITEM ID (an id, not a catalogue index — ids are fixed forever),
written to replays as an `sN` token. It is a `ushort`, not a byte like LootPick
and DropPick, because ids run past 255 (the objective is 900). The menu only
QUEUES the id; `main.gd:_spawn_queue` hands one per tick to the sim and
`SimWorld.StepSpawn` conjures it INSIDE the tick through the same
`Pack.AutoPlace` as looting. Gear may be conjured, ROOM may not — a full pack
refuses and logs `PackFull`. A QUEUE because the menu pauses the sim, so spawns
pile up before a tick can carry one; `_restart()` clears it.

Does NOT move the golden hashes: `InputFrame.HashInto` is not part of
`SimWorld.StateHash`, and the `s` token is prefixed and omitted at 0.

Sits ABOVE every screen (z 320): F8 is read in `_input` BEFORE the HUD editor's
keys, and `_handle_dev_keys` runs BEFORE every other screen in
`_physics_process`. Two refusals, where something else owns the whole input
surface: the level editor (TAB) and the HUD editor (F4).

A spawn saves the stash on the spot. `KIND_SINGULAR` is indexed by sim
`GearKind` and is NOT parallel to `KINDS`/`KIND_NAMES`, which carry an extra
"all" tab; `inventory_check.gd` asserts every sim kind has a tab AND a row tag.

# Equipment, the stash and looting
Eight worn slots: helmet, vest, backpack, footware, shirt/chest, arms, primary
weapon, secondary weapon (supersedes rpg_extension_plan §4's seven). Only FOUR
reach the sim, because only four have a reader there: the two weapons, the vest
(armour) and the backpack (pack size). Helmet, footware, shirt and arms are
worn, saved and drawn, change no stat, and are labelled "cosmetic". Do not give
them effects until sim/ can read them.

E SHOWS A DIFFERENT SCREEN IN THE FIELD. `stash_screen.gd` has two modes;
`main.gd:_open_mission_inventory` picks on `SimBridge.RunLive`:
- IN A MISSION → the FIELD VIEW (`mission_mode`): only what is carried — ALL
  EIGHT worn slots, the mission pack at nearly twice the size, the floor, and a
  read-only list of what is fitted to the weapon. No stash grid, no mission
  select. The worn kit comes from `bridge.GetWornSim()`, NOT the stash object:
  mid-run the sim's Loadout is the truth, and the two deliberately disagree the
  moment anything is equipped in the field.
- OTHERWISE → the full stash, below.

EQUIPPING IN THE FIELD is recorded intent. `InputFrame.EquipPick` is PACKED —
low byte the pack placement + 1, high byte the GearSlot — because Step's
argument list is already the widest thing crossing into GDScript. Build with
`InputFrame.PackEquip`, read with `EquipPlacement`/`EquipSlot`; rides in
replays as an `eN` token. The screen only STAGES (`pending_equip`); main.gd
hands it to the sim on the next tick.

`SimWorld.StepEquip` is a TRADE, never a gain: what comes off goes into the
pack, and if it will not fit NOTHING moves. The new item is lifted out before
the old one is put back, or a nearly-full pack would refuse trades that fit. A
newly equipped weapon arrives with an EMPTY magazine — there is no ammo pool,
so handing one back would make re-equipping better than reloading. `Mag` is the
weapon in hand and `MagStowed` the other, so which to clear depends on whether
the replaced slot is the held one.

EACH WEAPON HAS ITS OWN ATTACHMENTS. `Loadout` carries TWO `AttachSet`s — the
primary's and the holster's — each masked by ITS weapon's own rails. One shared
set meant the drum mag bought for the rifle also fed the pistol the moment it
was drawn, and the two guns could never differ. Consequences:
- `AttachmentAt(hand, slot)` / `WithAttachmentAt(hand, slot, id)` name the hand.
  `Attachment`/`WithAttachment` act on the weapon IN HAND, which is what the
  field means by fitting something; the stash has both guns in front of it and
  must say which. `SpecAt(hand)` is what the sim fires with, and `StowedSpec`
  replaced `SpecFor(Stowed)` — the stowed gun's magazine is the one fitted to
  THAT gun.
- The rails stay with the HAND, not the gun that left it. Clearing them on a
  weapon swap would DESTROY the attachments: the pack holds a weapon as one
  item id with nothing fitted, so a scope taken off would have nowhere to go.
- `ToText` appends `sight2..stock2`; a kit written before this parses them as 0,
  which is what those runs were. `stash.txt` appends `attach2 <sub> <item>`
  lines beside the existing `attach` ones for the same reason.
- The stash screen shows ONE gun's six rails at a time and names it; clicking a
  weapon slot switches. Six boxes is what the layout has room for, and a rail
  block that did not say which gun it was about would be worse than one page.

EVERY worn slot can be changed in the field EXCEPT the weapon sub-slots:
attachments stay fixed at deploy, since fitting one mid-run is not the local
change it looks like.

The BACKPACK is special (`SimWorld.EquipBackpack`): it is the container the
pack lives in, so changing it re-grids everything. The new bag is TEST-FITTED
into a scratch PackGrid and committed only if every item — including the bag
coming off — still fits, so the pack is untouched or wholly valid, never half
re-gridded. `SimBridge.BackpackWouldHold` exposes that same test, so the screen
can say "your kit will not fit that bag" rather than letting the tick refuse in
silence.

APPAREL (helmet, footware, shirt, arms) lives in `Loadout` as four item ids and
is HASHED. Inert is not absent: an item moving onto the player is sim state
whether or not it does anything, so it must be recorded or the replay diverges.
`Loadout.ToText` carries them; older kits parse them as 0.

EVERY `Loadout.With*` must carry EVERY field through. It is a readonly struct,
so each mutator rebuilds the whole thing; a field added without visiting all
nine is silently dropped (that is how `SetAttachment` took a helmet back off).
`Exhaustive.LoadoutMutators` asserts every mutator preserves every field it
does not name, comparing through `ToText` because `Attachment()` masks by the
weapon in hand.

Worn gear cannot be taken OFF in the field: the sim models no unarmed state.
`SimBridge.CanEquipMidRun` is StepEquip's own rule read back;
`inventory_check.gd` asserts the two agree across every item and every slot.

- E opens THE inventory — there is only one. Worn slots and sub-slots left,
  STASH grid below them, MISSION PACK below that. Drag gear between the stash
  and a slot, R turns the carried item, E or ESC closes. Closing resolves the
  worn kit into a Loadout and STAGES it — the sim's Loadout is fixed at
  Restart, so it takes effect next run and the screen says so. Saved to
  `user://stash.txt`, reloaded on launch.
- FOUR destinations, and every one of them is a box the screen draws: a WORN
  SLOT, a WEAPON SUB-SLOT, a STASH CELL, and (in the field only) the FLOOR.
  `release_at` must name every one. It did not name the sub-slot rail, so a
  scope dragged onto its own rail was neither slot nor cell, fell through to
  the cancel, and evaporated with nothing said — the drag had not failed, it
  had ended nowhere. An attachment goes only in ITS OWN sub-slot: the item
  names it via SimA, and the destination is checked against that.
- Dragging one WORN SLOT onto another EXCHANGES them (`stash.swap_slots`), and
  nothing passes through the grid. It used to unequip into the grid and equip
  back out, which displaced the target's item into the grid as well — so
  swapping hands left one hand empty and a gun in the stash, and failed
  outright when the stash was full.
- THE MOUSE HIT TESTS TAKE A POINT. `press_at(m)`/`release_at(m)` and every
  `*_at(m)` below them read the cursor ONCE, in `_unhandled_input`, and are
  handed the point. `get_local_mouse_position()` has no cursor to read
  headlessly, so a screen that read it for itself was a screen no harness could
  drive: `inventory_check.gd:_check_drag_paths` covers every drag above at the
  CENTRE of the box the screen draws, which is how four of them were found.
- A drag cannot outlive the button. Releasing outside the window delivers no
  button-up, and `press_at` refuses while something is carried — so one stuck
  drag made the screen ignore every click after it. `_process` drops a drag
  with no button behind it, and `open_screen` cancels one outright.
- HOVERING AN ITEM SAYS WHAT IT DOES. `game/item_tooltip.gd` builds the rows —
  `[kind, left, right]`, pure and static, so the whole of it is assertable
  instead of read off a screenshot — and `stash_screen._draw_tooltip` is only
  geometry and colour. `hovered_item(m)` resolves the four panels that can be
  under the cursor (stash grid, worn slot, weapon rail, pack, plus the field
  view's fitted chips) in the same order `release_at` resolves destinations.
  Never while dragging: the panel would cover the cell being aimed at.
- A WEAPON LISTS EVERY RAIL, fitted or empty or absent — the same reason the
  loadout menu greys a slot rather than hiding it. Its FIGURES are the FITTED
  ones, with the bare weapon's shown as a colour on anything an attachment has
  moved; asking what a rifle does and being told what it would do with nothing
  on it answers a different question. ONE attachment set applies to whichever
  weapon is held, so a rail the weapon lacks reads "no rail" and a count says
  how many fitted items it cannot take.
- AN ATTACHMENT IS A DELTA, so it is shown as one, against the weapon IN YOUR
  HANDS when that weapon has the rail and the first one in the catalogue that
  does otherwise. Only the figures it actually moves are printed.
  `ATTACH_ROWS` is the FULL stat table, not the weapon tooltip's headline one:
  a rubber grip touches only the cone under fire and when swung, and against
  the short list it said "changes nothing".
- Every figure comes from `SimBridge`, never computed in GDScript. The tooltip
  asks through the ITEM PROBE — a scratch `Loadout` built by `ProbeWeapon` /
  `ProbeAttach` and read by `ProbeStats`/`ProbeBareStats` — which never touches
  `_loadout`, never reaches the sim and feeds no hash. Built call by call
  rather than from an array argument, for the reason `Step`'s arity lint
  exists. `inventory_check.gd` asserts that looking at every item in the
  catalogue leaves the staged weapon, armour and attachments untouched: a
  tooltip that re-armed you by being looked at would be the worst bug in here.
- `SimBridge.GetFittedSim()` is `GetWornSim`'s counterpart — what is on the
  rails IN THE SIM, masked by the weapon in hand. The field view reads it
  rather than the stash, because attachments can be fitted mid-run and the two
  then disagree, exactly as the worn slots do.
- YOU PACK THE BAG AT BASE. Dragging out of the stash grid onto the pack panel
  moves the item INTO the bag and OUT of the stash — `stash.carried`, saved as
  `carry <item>` lines, pushed to `Loadout.Carried`, and auto-placed into the
  pack by `SimWorld` at Restart. It is hashed and it rides in the replay,
  because the pack is sim state: a run that began with a plate in the bag and
  one that did not are different runs from tick zero.
  - The BAG decides how much fits. `SimBridge.CarriedWouldFit` re-packs the
    whole list into a scratch grid rather than counting cells — a pack is a
    PACKING, and four 1x1s and one 2x2 are not the same four cells.
  - `CarriedPlacements()` lays the staged kit out through the SAME `AutoPlace`
    that will place it for real, so what the panel shows at base is where it
    actually lands.
  - `stash.gd` otherwise touches the sim only in `apply_to`; `_sync_carry` is
    the stated exception, because "will this fit" is a packing question, the
    sim owns the packing, and the answer depends on the bag.
- IN THE FIELD the pack panel is read-only except for DROPPING: the pack is sim
  state that rides in replays, and a drag cannot be recorded into an
  InputFrame. The sim auto-places what you take; spatial decisions happen in
  the stash.
- AT BASE THE PACK DRAWS EMPTY, and is sized by the bag you will WEAR
  (`pack_dims`), not by `PackWidth`/`PackHeight`. The sim rebuilds Pack at
  Restart, so between runs those describe the pack of the run that just ENDED —
  whose contents `_settle_run` has already banked into the stash. Drawing them
  showed every recovered item twice, in a panel that would not let go of it.
  Nothing can be lifted out of it there and THE FLOOR IS FIELD-ONLY: the bin is
  neither drawn nor hittable at base, because a drop staged there was still
  staged when the next mission began and the sim performed it on that run's
  first tick. `_restart()` clears both staged fields for the same reason.
- DROPPING is recorded intent: `InputFrame.DropPick` is 0 for none, else the
  pack PLACEMENT index + 1, written as a `dN` token. The screen stages
  `pending_drop`; main.gd hands it to the sim next tick and connects BOTH
  `dropped` and `equipped` to one handler.
- Dropped gear becomes a GROUND PILE at your feet: an ordinary loot target,
  same panel, same G, same index space (guards, then chests, then ground).
  Drops within `Tune.DropMergeDist` JOIN an existing pile. It draws as a BAG
  (`main.gd:_draw_ground`), chest-sized with flap and straps, because a small
  rectangle on a dark floor read as nothing having happened. The loot panel
  skips the identify dwell for anything you dropped yourself
  (`loot_panel.mark_known`).
- G held over a fallen guard, a chest or a ground pile raises the loot panel:
  what is still on it, nearest only, every item UNIDENTIFIED. IDENTIFYING IS A
  DWELL — rest the cursor on a row and a bar fills (loot_panel.gd
  `IDENTIFY_SECONDS`, 0.7 s), then it resolves into the name; partial progress
  survives the cursor moving away. Keyed by item id, so duplicates reveal
  together. TAKING IS A RIGHT-CLICK, and only on something already identified;
  a click on a mystery does nothing, not even start the dwell. RIGHT
  specifically because taking the LAST item ends the rummage that same tick and
  the held left button then read as fire. Nothing leaves a body unless the
  player named it. Anything too big for the room left is refused and reported.
  No backpack worn means nothing can be carried at all.
- The choice is recorded: `InputFrame.LootPick` is 0 for none, else the kit
  index + 1, hashed and written as a `pN` token (prefixed so it cannot be
  confused with the `xN` run length; absent from older replays, which still
  parse). All eight Flags bits were spoken for, hence a field.
- What has been IDENTIFIED, and how far each dwell has run, is PRESENTATION
  only: no hash, not recorded, so a replay reproduces what was taken, not what
  was looked at. That is also why the dwell may run on frame delta.
- The world keeps running while the panel is up — rummaging a corpse is not
  free — so main.gd withholds the fire and aim flags for its duration; both are
  mouse buttons and a click would empty a magazine into the body.
- `SimBridge.Step` takes NINE arguments (…, lootPick, moveTier, dropPick,
  spawnItem, equipPick). C# defaults do NOT reach GDScript — Godot registers
  every parameter as required — so a short call fails at RUNTIME with
  "Nonexistent function 'Step'" while `--check-only` passes and the build is
  clean. It has broken call sites FOUR times, once silently aborting a whole
  test function while still reporting zero failures.
  `editor_check.gd:_check_step_arity` now reads the expected count out of
  SimBridge.cs and counts arguments at every `.Step(` in every .gd file; a
  tenth argument needs no lint change, only the call sites it names.
- Presets ISSUE gear: with no room to stage an item in the grid it is equipped
  directly (`stash.gd:_issue`), or a crowded stash deploys without its vest.
- `sim/GearCatalog.cs` is the ONE item table, PRICES INCLUDED. game/ reads it
  through the bridge; never re-copy it into GDScript. Price 0 means "not for
  sale" — only the starting Glock.

# The campaign (meta layer)
A new campaign starts with a GLOCK AND A SATCHEL and nothing else; everything
else is bought or found.

Money lives in `game/campaign.gd` (`user://campaign.txt`), beside the stash,
and feeds no hash: nothing you buy changes a rule, only which Loadout you walk
in with.

NOTHING is charged to enter a mission and nothing is paid for merely coming
back. You are paid for DOING THE JOB. A run settles ONCE, on the tick it ends
(`main.gd:_settle_run`):
- COMPLETED (extracted WITH the objective): mission payout
  (`missions.gd:mission_payout`, scaled by threat) + provable/unprovable record
  tiers + whatever the fence paid for overflow. Everything that fits goes into
  the stash as real gear.
- EXTRACTED WITHOUT THE OBJECTIVE: **no cash at all** — no pay, no records, no
  fence. You keep every item you carried; gear is not money.
- DIED: **everything on you is destroyed** — the pack, what you were wearing,
  and what was fitted to it (`stash.lose_kit`). The stash GRID survives
  untouched, which is the whole shape of the decision: what you leave at base
  is safe, and what you carry is not. A death used to cost only the loot you
  had found, so walking in wearing everything you owned was free.

THE OBJECTIVE IS HANDED IN, not kept. `stash.bank_recovered` skips it on the
way into the stash: it is the job, the job pays the mission rate, and keeping
the case as well would be paid for it twice and leave a 2x2 lump of nothing in
the stash forever. That function is also where the fence rule lives, because
`_settle_run` itself is reachable by no test at all (loot flow §7) and this is
the half of it that decides where gear ends up. The bag is emptied on the way
out: what was in it has just been banked, and a carry list left standing would
deploy the next run with a copy of all of it.

KEPT GEAR PAYS NO CASH. Paying shop value for salvage AND keeping the item made
one chest buy a rifle and ended the economy in a run. The item IS the reward;
only overflow is fenced, at `SALVAGE_RATE_Q8` (~35%).

B opens the shop. Stock is `GearCatalog` filtered to things with a `Price`, so
an item added to the sim is purchasable the moment it exists. Buying puts it in
the STASH, not on your body. Money leaves the ledger only after the stash has
taken the item.

# Mission objectives
Glyph `!`. An objective site is a CHEST holding one `sealed case`
(`GearCatalog.ObjectiveId`, 2x2, `GearKind.Objective`), so it loots through the
machinery a chest already has, and it draws NOTHING from the loot stream — so
adding one does not re-roll a level's supply chests.

Carry it out in the pack and the mission is complete; `SimWorld.ObjectivesMet`
is the whole condition, and a level with no `!` is met by default so older
levels stay payable. Priced at zero, excluded from the shop by KIND as well as
price, never reaches the stash. 2x2 deliberately: it costs real room, so the
smallest pack makes "objective or loot" a decision.

# Mission select
Any mission, any time — no gating, no entry cost. `game/missions.gd` DERIVES
threat from the level summary (guards, area, chests) rather than an authored
field (campaign plan §2.1), so a level built in the editor rates correctly the
moment it is saved.

Area is weighted heavily (`CELLS_PER_POINT` 150) because every shipped level
carries the same twenty guards, so size is what differentiates them.
`THREAT_MAX` is the pip scale and must keep the levels that EXIST in its middle
— at 200 they all pinned at five pips.

The multiplier applies to mission payout and record rates ONLY. It must never
touch salvage: an item's fence value is already its own worth.

# Guards
Glyph range 'a'-'z' (`Level.GuardFirst`/`GuardLast`, `MaxGuards` 26). Eight
capped how dangerous ANY level could be and made the guard term constant on the
threat scale.

Every level carries TEN. A guard with no route line is a stationary sentry by
design: patrollers set the rhythm on corridors, sentries hold the rooms worth
crossing. `Systems.Playable()` asserts routed guards walk and sentries do not.

`Tune.GuardRecordEvery` tracks the guard count (3 at eight, 4 at ten, 5 at
twenty), so record scarcity stays a decision rather than drifting with a
difficulty change.

# Chests
Glyph `C`. Containers of GEAR, as against `$` record caches — they must never
read the same at a glance, hence the warm colour. Contents are rolled by the
sim from the loot stream, not authored: a level says WHERE, not what. Rolled
AFTER the guard kits, so adding a chest does not re-roll every body
(`Economy.Chests()`).

Bodies and chests share ONE loot index space — guards, then chests — so the
loot panel, `InputFrame.LootPick` and `StepLoot` never know which they work on.
`SimBridge.GetLootKit(index)` replaced `GetGuardKit(guardIndex)`.

# Record scarcity
Records are the dilation fuel, the score AND the loss condition (spec §2.3), so
plenty undercuts all three. One guard in `Tune.GuardRecordEvery` carries a
SINGLE record; a cache holds one. About seven on the reference level, against
~23 before. The starting briefing survives — it stops the first room from being
the one place dilation is unavailable.

# Weapons
TEN, in five families. Ordinals are in the replay format and the state hash, so
they are fixed forever — append, never reorder.

| id | weapon | class | the trade |
|----|--------|-------|-----------|
| 0 | Glock | pistol | free, quiet-ish, the default |
| 1 | MP7 | submachine gun | cadence |
| 2 | AK-47 | assault rifle | damage |
| 3 | Remington | shotgun | a choked pattern at close range |
| 4 | SAW | machine gun | volume, at the cost of moving |
| 5 | Welrod | silenced pistol | 130px report, six rounds, bolt-action slow |
| 6 | VSS | silenced marksman rifle | 260px, one-shots a bare guard |
| 7 | Photon | laser carbine | 52 dmg, 50% pierce, overheats |
| 8 | Arc Lance | heavy laser | 75% pierce, eight shots |
| 9 | Vulcan | minigun | 58 dmg, 300 rounds at 30/s, SPIN-UP |

Photon and Vulcan were RETUNED UP after play: half of 38 through a heavy plate
was four bolts from a barrel that will not take four, and the loudest weapon in
the game has to be worth having told everyone where you are.

Three axes the new families move on, none of them "a bigger number":
- SILENCED trades damage and cadence for a report that does not wake the floor.
  `GunshotRadius` is the stealth axis (spec §8.3). Subsonic ammo must stay the
  quietest option in the game — there is a test, since a silenced weapon that
  beat it would make the attachment pointless.
- ENERGY trades magazine and cooling for armour PIERCE and a bolt at 5500+
  px/s. Bullets above 4000 px/s draw cyan; nothing ballistic comes close.
- ROTARY is the Vulcan's `SpinUpTicks` (45, 0.75s): ticks of HELD trigger
  before the first round. Spooling costs no ammunition and starts no cooldown,
  or holding the trigger would empty the magazine without a round leaving. It
  winds down 1.5x faster than up (`Tune.SpinDownQ8`), so tapping is punished.
  `Actor.SpinMt` is hashed. No attachment shortens it.

Exotics are RARE in chests (under a tenth between them): a Vulcan out of the
first chest would end the economy the shop exists to create.

# Loadout menu
Q opens it. Up/down pick a row, left/right change it, E or ENTER stages the kit
and closes — it does NOT restart; F5 deploys with it. Weapon, armour and six
attachment slots; slots the equipped weapon lacks are greyed, not hidden.

# Guards, health and armour
Guards spawn on `Tune.GuardHealth` (60), NOT `Tune.BaseHealth` (the player's
100): the interesting question about a guard is whether he wears a plate, not
how deep his pool is.

The vest in a guard's rolled kit is the vest he is WEARING — `RollGuardKits`
sets `Actor.Armour`/`ArmourMax` from the same catalogue entry it adds to the
body, so the plate stopping your rounds is the plate you strip off the corpse.
~60% have one (30% light / 20% medium / 10% heavy). An AK drops a bare guard in
one round and takes three or four through heavy plate.

The answers to a plate are AP ammo and a locked headshot (headshots ignore
armour entirely). Both tested in tests/Health.cs.

Armour is drawn two ways deliberately: the SILHOUETTE (shoulder pads and chest
plate, scaled by tier) says "armoured" from across the room before you commit;
a steel BAR above the awareness bar says "you are getting through it" once you
have. `GuardArmourHit` sparks cold instead of spraying blood.

# Levels and the camera
Levels carry their OWN size. `Level.GW`/`GH` (48x28) are the default for a
BLANK level, not a world size: `Level.FromText` infers W and H from the grid
text. Clamped to `Level.MinDim`/`MaxDim`/`MaxCells`; the parser stays total.
- `substation_4.txt` 48x28 — the reference. KEEP IT AT 48x28: golden-hash
  fixture, and the level-parser tests read it.
- `relay_nine.txt` 96x56 — 4x area, first scrolling level.
- `terminal_twelve.txt` 144x84 — 9x area, 20 rooms off a corridor lattice, 284
  wall rects. Regenerate with `python3 tools/gen_terminal_twelve.py`; it is
  deterministic and asserts its own connectivity before writing.

`-- --level res://levels/foo.txt` overrides the exported level, like --replay.

The camera is PRESENTATION and must stay that way: it never reaches an
InputFrame and never touches the state hash (editor_check.gd asserts this). It
is a draw transform, NOT a Camera2D — main.gd already draws world and HUD as
two passes, so the camera is a transform on the first and nothing on the
second. Consequences:
- The world pass runs under `_world_pass()`; anything needing its own rotation
  composes via `_world_xform()`. NEVER call `draw_set_transform` directly in
  world drawing — it pins that thing at 1:1 while the floor scrolls.
- The HUD pass runs under `_screen_pass()`.
- ANY world-space mouse read goes through `world_mouse()`. Screen-space panels
  (loot, menus) keep `get_local_mouse_position()`. Getting this wrong aims at
  where the cursor used to be, and only once you scroll.
- Fixed `PLAY_ZOOM` (1.35), scrolling to cover the level; it does not back off
  to fit the floor, which kept it too far out to read a fight. A level SMALLER
  than the view is pulled in up to `MAX_ZOOM` 2.0; `MIN_ZOOM` 0.6 is a floor
  nothing reaches.
- Dilation pulls the view back (`DILATE_ZOOM`), or rounds at the retuned muzzle
  velocities fly off screen during the one mechanic built around watching them.
- Guards in Hunt/Engage get an off-screen edge marker. Patrolling guards get
  nothing — that asymmetry is the stealth game.

# Editor view
Levels no longer fit one screen, so the editor has its own view: arrows or
middle-drag pan, -/= or wheel zoom, F fit (automatic on open, load, new, undo).
ctrl+arrows RESIZE in 4-cell steps; shrinking is allowed and undoable, and
reports how many non-floor cells it dropped rather than silently eating a room.

# HUD
Every element has an id, a size and a top-left corner owned by
`game/hud_layout.gd`. main.gd draws each at `_hud.pos_of(id)` and NEVER at a
computed offset — an element whose position is an expression cannot be moved.

**F4 opens the HUD layout editor**: drag any box, arrows nudge whatever is
under the cursor, R resets, ENTER or F4 saves and closes, ESC discards. Saved
to `user://hud_layout.txt`, restored on launch. The world keeps running behind
it and the mouse buttons are withheld from the sim, as for the loot panel.

The HUD reads what is IN YOUR HANDS (`HeldWeaponName`, `HeldMagazineSize`), not
the STAGED loadout (`CurrentWeaponName`, `MagazineSize`); they diverge after an
X swap and after equipping without redeploying. A `pending` element shows
`F5 → <weapon>` whenever `SimBridge.LoadoutStaged` is true.

Adding an element means adding it to `ELEMENTS` and `DEFAULTS` in hud_layout.gd
plus a `_hud_*` draw function in main.gd. The harness enforces:
- Defaults must be multiples of `GRID` (4); `set_pos` snaps and load goes
  through it, so an off-grid default would shift on reload.
- Declared sizes must match what the element paints. `draw_string` positions
  the BASELINE, so labels drop INTO the box rather than sit on its top edge.
- No two elements may overlap in the shipped layout. The record strip is capped
  to the slots its own box has room for, not a magic nine.

# Display
Design resolution 960x620; everything draws in those coordinates.
project.godot uses stretch mode canvas_items with aspect keep, so the window
scales and letterboxes untouched by drawing code. F11 toggles fullscreen. Mouse
input must go through `get_local_mouse_position()` or a Node2D's
`get_global_mouse_position()` — a raw `InputEvent.position` is in window pixels
and lands wrong once the canvas is scaled.

# Movement tiers
FOUR speeds on the SCROLL WHEEL, not a held key
(`InputFrame.TierStealth..TierSprint`), trading on three axes at once:

| tier    | px/s | noise | detection | turn |
|---------|------|-------|-----------|------|
| stealth |   98 |  none |       81% | 100% |
| walk    |  196 | 170px |      135% | 100% |
| fast    |  270 | 240px |      189% |  72% |
| sprint  |  360 | 320px |      250% |  45% |

Tiers 0 and 1 reproduce the OLD sneak and walk EXACTLY — same speed, noise and
detection multiplier — so spec §8.6's measured detection curve still holds.
`Handling.MoveTiers()` asserts that parity; do not break it casually.

Sprinting floors SWAY at `Tune.SprintSwayQ8` and keeps it floored for
`Tune.SprintRecoverTicks` (0.45s) after you stop — the re-aim cost; no aim lock
can build while it runs. Expressed through the existing sway term so it reads
on the reticle.

The tier is INPUT: `InputFrame.MoveTier` (its own field — all 8 flag bits were
spoken for), written as an `mN` token, omitted at walk. A replay with no `m`
token derives the tier from the old FSneak bit, so older recordings verify.
That bit is now FREE.

# Keys
WASD move, mouse aim, click fire, SCROLL WHEEL move speed, space dilate,
F subdue, R reload, F5 restart, F9 save replay, F11 fullscreen, Q loadout,
TAB editor, G hold to loot a body/chest/floor gear, X swap weapon, E inventory
(FIELD VIEW during a mission, full stash + mission select outside one), B shop,
F4 HUD layout editor, F8 dev menu. `I` is unbound.
Function keys: F2 rename (editor only), F4, F5, F9, F11. The `sneak` action was
deleted (the wheel replaced it), so SHIFT is free.

When a run ends the DEBRIEF names the outcome — "You died!", "You escaped with
the mission objective", "You escaped without the mission objective" — shows
what it paid, and offers ENTER for the stash or F5 to rerun the mission.

F5 RESTARTS THE RUN, at any time — the only control that does. Closing the
equipment screen or the loadout menu STAGES a kit and leaves the run alone; the
only other `_restart()` in main.gd is the stash's deploy, which BEGINS a
mission. The editor's ENTER playtest builds a fresh run from the edit buffer.
ENTER no longer restarts (it still deploys, via ui_accept). A FUNCTION key
deliberately: restart is destructive and unconfirmed, so it sits away from WASD.

# Never drive the live game against the real save directory
`user://` is `~/Library/Application Support/Godot/app_userdata/Cognitohazard_v1`
and holds the PLAYER'S campaign, stash and HUD layout. Launching the game WILL
write those files, and the title menu includes New Game, which wipes the
campaign in one keypress with no confirmation — a scripted input sequence has
already destroyed a real save this way. Before running the game (not the
harnesses — those guard themselves), copy `user://*.txt` somewhere and put it
back afterwards.

# Conventions
- Tabs, not spaces (Godot's parser and editor both assume tabs)
- Do NOT run `unexpand` over a whole generated file: BSD unexpand also converts
  runs of spaces INSIDE string literals, putting tabs into on-screen text like
  "%d item(s)  ·  %s". Convert leading whitespace only, or write tabs directly.
- Static typing everywhere: `var speed: float = 300.0`
- snake_case for files, nodes, functions; PascalCase for classes
- Signals over polling; `@export` for anything I should tune in the inspector

# Rules
- Never hand-edit the `[ext_resource]` or `uid://` lines in a .tscn
- If a scene needs structural changes, tell me and I'll do it in the editor
- After any script change, run --check-only before telling me you're done
- After any C# change, run `dotnet build` before telling me you're done
- Placeholder art only: procedural primitives. No asset hunting. (spec §0)

# Sim-layer rules (spec §3.3) — build-breaking, not style
Forbidden anywhere in `sim/`: randf/randi, Time.*, OS.*, Engine.get_frames_*,
Input.*, any Node, any delta, and any iteration over a hash-ordered collection
whose order affects state. Guards iterate by index, always.
All hash-feeding state is integer: positions 1/256 px fixed point, angles BRAD,
awareness in tenths, record charge in frames.

# Tuning constants (spec §0, §12.2)
Every number in spec §5–§8 is load-bearing and browser-tuned. Port them
exactly. If one feels wrong, say so in the milestone report — never change it
silently.

Bugs the bug-finding suites found, each fixed and each pinned by a named
regression test as well as by the suite that caught it:
- `SimWorld` filled the STOWED magazine from `Loadout.Secondary` — the
  HOLSTERED weapon, which is the one in HAND once the secondary is drawn — and
  read the raw catalogue entry instead of the attachment-modified spec. Fixed
  with `Loadout.Stowed` and `Loadout.SpecFor`. (`Fuzz`.)
- `Level.MaxGuards` had NO consumer, so a malformed grid parsed into thousands
  of guards the sim walks every tick. `FromText` enforces it. (`Robustness`.)
- `campaign.gd` guarded with `is_valid_int()`, which only answers "all digits",
  so a 20-digit number passed and then errored per line. (`fuzz_check.gd`.)
- `hud_layout.gd:set_pos` SNAPPED then CLAMPED, so any element of non-grid
  width landed off-grid from the one function meant to keep it on. Now clamps,
  then floors. (`fuzz_check.gd`.)

Deliberate deviations, each on request and each commented where it lives:
- `Tune.AimLockTicks` is 30 (0.5 s), not the browser's 90, and is now exact —
  it used to take one tick longer than the constant said.
- Muzzle velocities ~3x the browser's (840→2520 px/s player, 640→2200 guard),
  with round LIFETIMES cut in proportion so REACH is unchanged and only time of
  flight moved. `Tune.BulletSubsteps` is a floor, not the count: substeps are
  derived per round from distance covered, or samples 16 px apart would put
  rounds through one-cell walls.
- The player's aim no longer snaps to the cursor. `WeaponSpec.TurnNum` swings
  it per-weapon (Glock 165/300 per tick, SAW 51/300), so heavy weapons are
  harder to bring on target. This CLOSES the spec §10.2 free-spin exploit,
  since the turn is paid on the player clock. The reticle is drawn on the
  muzzle line, not under the mouse.
- Firing error runs on BOTH sides. A third cone term, sway (`Actor.SwayQ8`),
  rises with how hard the weapon is swung and with movement and decays when the
  shooter settles; guards carry it AND a sustained-fire heat term instead of a
  flat ±0.045 rad. A cold guard's cone still equals that old flat draw.
- Guards spawn on `Tune.GuardHealth` (60) and wear the vest their loot roll put
  on their body; previously a flat 100 pool, and the vest protected nobody.
- The Remington is choked: 3129 → 1565 base, pellets laid evenly across the
  cone with per-slice jitter rather than seven independent draws, which
  clumped. Heat re-tuned (90 per shell, 64/s decay), since at the shared 1.5/s
  decay a 54-tick pump cleared between shells.
- `StepPlayer` reads `Loadout.WalkSpeed`/`SneakSpeed`/`AimMoveQ8`; it had moved
  at the flat `Tune.SpeedWalk`, so weapon weight, armour bulk and the cost of
  aiming were computed and never applied. A repair, but it moves the hashes.

# MCP
The godot MCP tools point at the .NET build (GODOT_PATH in ~/.claude.json), so
run_project/get_debug_output/stop_project work — but only after the MCP server
restarts; until then launch directly with `$G --path .`
Do not use create_scene / add_node — I build scenes in the editor.

# The Kit revamp — PART DONE, read before continuing
Target: a body (player OR guard) is ONE structure of nine worn entries — head,
two weapons, arms, shoes, vest, chest, legs, pack — where the pack is itself a
container holding its own grid. Looting reads another body's Kit; items go into
a slot or the pack; a dropped pack keeps its loot for the level; the kit
persists between runs until unloaded at the stash or lost on death.

LANDED:
- `GearSlot.Legs` appended (ordinals are mirrored and in save files, so it goes
  on the END). `GearCatalog.SlotCount` and `item_catalog.gd SLOT_COUNT` are 9.
  Both slot grids re-laid as two columns of five; the worn-item baseline comes
  from the BOX, because the two views give slots different heights.
- `sim/Kit.cs` — nine slots, its own `PackGrid`, the attachment set, the active
  hand, hashing, `PackFullPercent()`, and `ToLoadout()`. Keep the layering:
  **Kit = what is worn, as item ids. Loadout = what that means, as specs.**
  NOT WIRED IN YET — nothing constructs one.
- ATTACHMENTS CAN BE FITTED IN THE FIELD (`SimWorld.EquipAttachment`). The item
  names its own sub-slot via SimA/SimB but must still be dropped on a WEAPON
  slot — the sim read the item and ignored the destination, so a red dot
  dropped on the helmet fitted itself to your rifle.
- THE FIELD VIEW NO LONGER PAUSES THE WORLD and no longer closes on every
  action. It closes on E, ESC, or TAKING DAMAGE (`EV_PLAYER_HURT`). While up,
  the player is held still and both mouse buttons are withheld, as for the loot
  panel — the screen covers the view, so walking blind would be an accident.
- The pack shows a FULLNESS PERCENTAGE, coloured past 70% and 90%.

STILL TO DO, in dependency order:
1. Guards hold a `Kit` instead of `Actor.Kit` (a flat `List<int>`).
2. A richer loot address. `InputFrame.LootPick` is a byte index into a flat
   list; it must name a SOURCE (one of nine slots, or a pack placement) and a
   DESTINATION (a slot, or the pack). Pack it like `EquipPick` — low byte the
   entry + 1, high byte the destination — and keep the `pN` token.
3. Packs as world objects, so a dropped bag keeps its contents. `GroundPile` is
   a flat list today; a dropped pack needs its own grid beside it.
4. The player's Kit replaces the `Loadout` + `Pack` pair in `SimWorld` and
   PERSISTS between runs. Also the fix for the settle bug below.

Steps 1-4 are one interlocking change: ~200 call sites, the state hash, the
replay format and the stash save format move together. Do not start it
half-way — `sim/Kit.cs` exists so the shape is agreed before the wiring.

# Loot flow
`cognitohazard_loot_flow.md` traces an item from the loot stream to the stash
and names where that chain is broken. Read it before touching anything that
moves items.

THE OPEN BUG: `main.gd:_settle_run` reads `GetPackItems()` and ONLY that.
`SimWorld.Loadout` is never reconciled back into the stash, so anything
equipped in the FIELD is destroyed on extraction, while the item it displaced —
which went into the pack — settles into the stash where the stash's own worn
slot still holds it, duplicating it. One rifle lost and one pistol duplicated
per field equip. `_settle_run` is exercised by no test at all; see §7 of that
document for why 1971 assertions missed it.

# Campaign plan
`cognitohazard_campaign_plan.md`. §2 (mission select, threat, payout
multiplier, per-mission history) is BUILT; §2.1 was decided in favour of
deriving from the level. UNBUILT: §1's selling from the stash and stash
capacity as a purchase, and §2.4's carrying-too-little warning.

# Camera plan
`cognitohazard_camera_plan.md` — BUILT, phases 1-8, except phase 2 was
implemented as a draw transform rather than a Camera2D + CanvasLayer split, so
no scene change was needed. See "Levels and the camera" for the contract.
