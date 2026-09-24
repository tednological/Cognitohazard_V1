# Campaign plan: the between-runs stash, and mission select with stakes

Status: **§2 BUILT** (mission select, derived threat, the payout multiplier,
per-mission history; §2.1 was decided in favour of deriving from the level).
**NOT built:** §1 (selling from the stash, stash capacity as a purchase) and
§2.4's carrying-too-little warning. Written against the tree as of the
meta-mechanics pass (money, chests, record scarcity, the shop); §0's table
predates the title/stash screen split, so read file names there against
CLAUDE.md.

---

## 0. What already exists

Do not rebuild any of this — both features below are extensions of it.

| Thing | Where | State |
|---|---|---|
| Persistent gear | `game/stash.gd` → `user://stash.txt` | 12x10 grid + 8 worn slots + 6 sub-slots |
| Money | `game/campaign.gd` → `user://campaign.txt` | balance, runs, extractions, last payout breakdown |
| Shop | `game/shop_screen.gd` | B from the start screen; stock is `GearCatalog` filtered to priced items |
| Settlement | `main.gd:_settle_run` | extract → fee + records + fenced overflow, pack into stash; die → nothing |
| Mission select | `game/start_screen.gd` | left/right over `levels.gd:list()`, summary from `SimBridge.LevelSummary` |
| Levels | `levels/` | 48x28, 96x56, 144x84 — all 8 guards, differing in size and container count |

Two facts that constrain everything below:

- **The sim has never heard of money**, and must not. Nothing in either feature
  may reach `sim/` or the state hash. A mission's payout is a property of the
  campaign, not of the world.
- **A level file has no metadata beyond `name:`**. Difficulty and reward have to
  come from somewhere, and §2.1 is the decision about where.

---

## 1. The stash between levels

The stash already persists. What it does not yet do is any of the things that
make it a *place* rather than a container.

### 1.1 What is actually missing

1. **You cannot sell.** The shop buys only. Overflow is auto-fenced at
   extraction (`SALVAGE_RATE_Q8`), but a rifle you have outgrown sits in the
   grid forever. Selling is the release valve that makes a 12x10 grid a
   decision instead of an eventual wall.
2. **It is one undifferentiated grid.** A found scope and a spare pair of boots
   compete for the same cells.
3. **Nothing tells you what you own is worth.** The shop shows prices; the stash
   does not.
4. **No capacity progression.** 12x10 from the first run to the last.

### 1.2 Recommended shape

**Sell from the equipment screen, not a new one.** The stash grid is already
drawn there with drag-and-drop; selling is one more drop target. Add a SELL
panel beside the worn slots — drop an item on it, it becomes
`salvage_value(price)` in the ledger. Reuse `campaign.salvage_value`, so buying
at 100% and selling at ~35% is one number in one place and the spread is
visible without explanation.

Guard rails:
- **Never sell a worn item.** Drop on SELL from a worn slot is refused; unequip
  first. The one-keypress path to being unarmed is not worth the convenience.
- **Confirm above a threshold.** A misdrop that costs 1400 needs a beat; one
  that costs 40 does not.

**Capacity as a purchase, not a level-up.** A `stash locker` line in the shop
that adds rows (12x10 → 12x14 → 12x18). It is already a grid that can `resize`,
and buying space competes with buying gear, which is the interesting decision.

Do NOT add tabs or categories. The packing grid is the game — the moment gear
sorts itself into bins, the 12x10 stops being a constraint and starts being a
list.

### 1.3 Where it goes

- `game/equipment_screen.gd` — the SELL target and its drop handling.
- `game/campaign.gd` — `sell(item_id, price)`; already has `earn`.
- `game/stash.gd` — `resize(w, h)` preserving placements (the grid has `resize`
  but the stash never calls it); `total_value(bridge)` for the header.
- `sim/GearCatalog.cs` — a `stash locker` item, or a non-item shop line.

### 1.4 Test gates

- Selling a worn item is refused and the money does not move.
- Sell price is exactly `salvage_value(price)` — pinned, so the buy/sell spread
  cannot drift.
- Money arrives only after the item leaves the grid (the same ordering the shop
  already tests: no charge for an item you did not receive, no payment for one
  you still hold).
- Growing the stash preserves every placement; shrinking refuses rather than
  dropping items.
- Round-trip: sell, buy, resize, save, reload, identical.

---

## 2. Mission select with stakes

The start screen already lists levels. What it cannot do is say that one of them
is harder than another, or worth more.

### 2.1 Where difficulty comes from — the real decision

Three options, and the choice matters more than anything else here.

**(a) Derive it from the level.** `LevelSummary` already returns cols, rows,
guards, caches, walls. A difficulty score falls out: guards, area, container
count. Zero new format, zero authoring, and it is automatically right for a
level you just built in the editor.
*Against:* it cannot know that a level is hard because of its SHAPE. Eight
guards in a corridor is not eight guards in an atrium.

**(b) Author it.** A `difficulty: 3` / `payout: 450` line in the level header.
Exact control.
*Against:* a new field in a format whose parser is deliberately total, another
thing to keep in sync, and every level in `levels/` needs editing.

**(c) Derive, with an optional authored override.** Compute from the summary;
let a header line win when present.

**Recommendation: (a) now, (c) if it turns out to be wrong.** Ship the derived
score. It is free, it is honest about what it measures, and the moment a level
is misrated you will know exactly which number to override. Adding the override
later costs one optional header line; starting with authoring costs edits to
every level and a format change you may not need.

Concretely, in `game/missions.gd`:

```
threat = guards * 10
       + floor(area_in_cells / 400)          # room to be seen crossing
       - min(chests * 2, 20)                 # supply makes a floor survivable
```

Then `payout_multiplier = 1.0 + threat / 100.0`, applied to the extraction fee
and the record rates — NOT to salvage, which is already the item's own worth.

### 2.2 What the player sees

The mission strip has room for one more line. It should answer, in order:
"how bad is this", "what does it pay", "have I done it".

```
MISSION
‹  Terminal Twelve  ›                              3 / 3
terminal_twelve.txt   144x84   8 guards   13 caches
THREAT ▮▮▮▮▯   pays 2.4x   ·   best: 1,840   ·   3 runs
```

- **Threat as pips, not a number.** A 0-100 score invites arithmetic; five pips
  invite a decision.
- **The multiplier, not the expected payout.** What a run pays depends mostly on
  what you carry out, which the screen cannot know. Promising a figure it cannot
  honour is worse than showing the rate.

### 2.3 Per-mission history

`campaign.gd` currently tracks totals. Missions need per-level records, keyed by
**file name** (titles are not unique and can be renamed):

```
mission terminal_twelve.txt runs=3 extractions=1 best=1840
```

Same total-parser rules: an unreadable line is skipped, an unknown level is
ignored rather than fatal — a mission file may be deleted between sessions.

### 2.4 Gating, and why not much of it

The obvious move is to lock hard missions behind money or completions. **Do not,
beyond a single soft gate.** The stealth game already gates itself: walking into
Terminal Twelve with a Glock and a satchel fails on its own, informatively, and
that failure teaches more than a locked row. One gate is worth having — a
**warning**, not a lock, when threat far exceeds the value of what you are
carrying:

```
you are carrying 320 of kit into a 2.4x floor
```

State the mismatch, let them deploy anyway.

### 2.5 Where it goes

- `game/missions.gd` — NEW. Threat scoring, payout multiplier, per-level
  history. Pure and static, like `levels.gd`, so it is testable headlessly.
- `game/campaign.gd` — per-mission records; `settle` takes a multiplier.
- `game/start_screen.gd` — the threat/pays/best line.
- `main.gd:_settle_run` — pass the multiplier and the level name.

### 2.6 Test gates

- Threat is monotonic in guards and in area; more chests lower it.
- Every level on disk scores inside the pip range (no level rates 0 or off the
  end of the scale).
- The multiplier applies to fee and records and **not** to salvage — pinned,
  because double-counting salvage is exactly the mistake the first payout
  version made.
- Per-mission history round-trips; an unknown or deleted level is skipped.
- Settling twice for one run is impossible (it is called on the over-transition
  only — worth an explicit test now that money depends on it).

---

## 3. Order

| # | Step | Touches | Gate |
|---|---|---|---|
| 1 | Selling from the equipment screen | `equipment_screen`, `campaign`, `stash` | inventory_check |
| 2 | Per-mission history in the ledger | `campaign` | inventory_check |
| 3 | `missions.gd` threat + multiplier | NEW, `start_screen` | new tests |
| 4 | Wire the multiplier into settlement | `main.gd` | inventory_check |
| 5 | Stash capacity as a purchase | `stash`, `GearCatalog` | inventory_check |
| 6 | The carrying-too-little warning | `start_screen` | play it |

Steps 1–2 are independently useful and do not depend on the rest. Step 3 is the
one with a real decision in it (§2.1); do not start it until that call is made.

---

## 4. Risks

- **Silent, worst:** a payout that double-counts. Salvage is already the item's
  full worth; multiplying it by mission difficulty pays twice for one thing.
  The multiplier touches fee and records only, and that is a test, not a note.
- **Silent:** selling a worn item, leaving you deployed with an empty hand that
  `apply_to` quietly resolves to the default weapon.
- **Design:** a derived threat score that disagrees with how a level actually
  plays. Expected, and the reason §2.1 recommends leaving room for an override
  rather than building one now.
- **Scope:** every part of this is `game/`-only. If a change starts reaching
  into `sim/`, it has gone wrong — the sim does not know what a mission is
  worth, and must not learn.
