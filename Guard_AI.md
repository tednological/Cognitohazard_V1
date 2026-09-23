# Guard AI v2 — Plan

Goal: guards that are **smarter and more realistic**, with stealth still the
core of the game. They move from a single-guard state machine to a posture
model with pathfinding, radio, squads and a level that stays compromised once
a fight has happened.

Status: **P0-P6 BUILT, 2026-09-22.** P7 (cover) not started. Every number below marked *(proposal)*
is a starting value to tune, not a spec constant.

---

## 0. Decisions already taken

| Question | Decision |
|---|---|
| What "sees a threat" means | **Faster meter.** The graded awareness meter stays, but a clear sighting within close range (~150 px, inside the cone) snaps to Combat. |
| Radio | **Timed and interruptible.** Level-wide reach. The call takes ~1.5 s of the guard standing still, visibly and audibly. Kill or subdue him mid-call and nobody comes. |
| Group assault | **Flank.** Members take different routes to the last known position and arrive from different doorways, timed to arrive together. |
| Body found | **Radio + sweep.** There is no known player position, so the finder radios it in and the level goes straight to the post-combat sweep. |
| Who answers the radio | **Scales with threat.** 2 responders, +1 per guard the player has killed in this incident, capped at 5. |
| Who sweeps after combat | **Mobile guards sweep, some hold.** Patrollers and combat survivors pair up and sweep. Sentries hold their posts at high alert. One pair covers the exit. |
| Cover | **A later phase.** A lone guard waiting for backup only holds a spot out of the last known position's line of sight (simple version). |
| Telegraphing | **Glyphs + radio cues.** A glyph per posture, a visible and audible radio call, and a HUD floor-alert line. |

---

## 1. What exists today (the baseline)

`sim/SimWorld.cs:1377-1726`, `sim/Perception.cs`, `sim/AlarmState.cs`, spec §8.

- **Six live states**: Patrol, Sentry, Curious, Hunt, Search, Engage (+ Down,
  Dead). Every guard decides alone. The only coordination is a 300 px
  LOS-gated voice callout every 1.3 s, and body/gunshot broadcasts.
- **Graded awareness** in tenths: curious 30, hunt 66, engage 100, cap 120.
  The spec §8.6 detection curve is an acceptance test (`tests/Systems.cs:
  DetectionCurve`).
- **Movement is steering, not pathfinding** (spec §10.1): guards drive straight
  at a target, slide along walls and nudge themselves when stuck. On Relay Nine
  (96x56) and Terminal Twelve (144x84) this grinds into corners.
- **Facing is movement direction.** `Steer` turns the guard toward where he is
  walking. A guard cannot walk one way and look another, which the "one watches
  back" sweep needs.
- **Search is random** (spec §10.3): uniformly random points 40–150 px from the
  last known position, for 9 s, then the guard stands down to Patrol/Sentry.
- **Floor alarm** has three levels (0/1/2) and decays one level after 22 s of
  quiet.

Kept as-is: the awareness accumulator and its multipliers, the grace window,
cones and ranges per posture, footstep noise and its cap below combat, §8.5
engage behaviour (turn rate, strafe bands, aim delay, cooldown, the firing-error
model), subdue from behind, and the §8.7 fix, carried into the new states.

---

## 2. The model: POSTURE and TASK

Two fields instead of one flat state.

- **Posture** = how alert the guard is. This is what the user described, what
  the HUD glyph shows and what drives cone and multiplier choices.
- **Task** = what he is doing about it right now. This is a sub-state that the
  posture owns.

```
GuardState (posture)   Relaxed | Curious | Combat | Hunting | Down | Dead
GuardTask              Patrol, Post,                          <- Relaxed
                       Look, Investigate, LookAround, Return, <- Curious
                       Engage, Converge, Radio, HoldForBackup,
                       Rally, Assault, SearchLkp,             <- Combat
                       Regroup, Sweep, HoldPost, WatchExit    <- Hunting
```

The old values map across: Patrol → Relaxed/Patrol, Sentry → Relaxed/Post,
Curious → Curious/Look, Hunt → Combat/Converge, Search → Combat/SearchLkp,
Engage → Combat/Engage.

### 2.1 Posture transitions

```
                 meter >= 30 (sight, footsteps, subdue nearby)
   ┌─────────┐ ─────────────────────────────────────────────▶ ┌─────────┐
   │ RELAXED │                                                 │ CURIOUS │
   └─────────┘ ◀───────────── investigation finds nothing ──── └─────────┘
        │                                                           │
        │  meter >= 100 + LOS · snap sight · gunshot heard ·        │
        │  shot at · voice callout · radio assignment               │
        └──────────────────────────┬────────────────────────────────┘
                                   ▼
                              ┌─────────┐   re-sighted (alerted ×1.9)
                              │ COMBAT  │ ◀──────────────────────────┐
                              └─────────┘                            │
                                   │ contact lost: nobody on the     │
                                   │ net has seen the player for     │
                                   │ ContactLostTicks AND the LKP    │
                                   │ has been searched               │
                                   ▼                                 │
                              ┌─────────┐ ───────────────────────────┘
      body found ───radio───▶ │ HUNTING │   never returns to Relaxed.
                              └─────────┘   Ends only with the run:
                                            player dead or extracted.
```

Rules that hold everywhere:

1. **Hunting is sticky.** Once the level is compromised, no guard returns to
   Relaxed, and no guard's posture drops below Hunting except to go Down or
   Dead. `Fuzz` asserts it every tick.
2. **Combat with LOS never drops a posture** (the §8.7 fix, generalised). A
   guard who can see the player is either Engaging or Converging on him, never
   searching.
3. **Posture only rises through perception.** It falls only by an explicit,
   timed rule: investigation complete, or contact lost.

### 2.2 Perception per posture

| Posture | Cone range / half-angle | Alerted ×1.9 multiplier |
|---|---|---|
| Relaxed | Patrol (400 / 0.85) | no |
| Curious | Curious (430 / 1.00) | yes |
| Combat | Engage (470 / 1.15), or Hunt (450 / 1.10) while Converging | yes |
| Hunting | Search (450 / 1.05) | yes |

The spec's own numbers, reused with no new constants. The +60 alarm range bonus
applies at alarm ≥ 2 as today.

---

## 3. RELAXED

- **Patrol**: walks the authored route. Legs are now **pathed** (§8), so a
  route drawn across a wall still works. Speed is `Alarm.PatrolSpeed` as today.
- **Post**: stationary sentry sway, exactly as today.
- Non-responders who hear a radio call **stay Relaxed** but take the raised
  alarm floor (38). They start 38 points up the meter, a "heads up" rather
  than a behaviour change.

## 4. CURIOUS — "something's off, go look"

Entered when the meter crosses 30: a glimpse at range, footsteps, or a subdue
within 120 px.

| Task | Behaviour | Ends |
|---|---|---|
| **Look** | Stop and turn toward the stimulus at 3.2 rad/s (today's Curious). | After `CuriousLookTicks` *(0.8 s)*, → Investigate. If the meter keeps climbing it may reach Combat first. |
| **Investigate** | Path to the stimulus point at `SpeedInvestigate` *(96 px/s, patrol pace)*, looking where he walks. A newer stimulus retargets him. | Arrives within `LkpReach` → LookAround. |
| **LookAround** | Stand and scan three headings: ±`LookAroundArc` *(1.2 rad)* then back, over `LookAroundTicks` *(4 s)*. | Scan done → Return. |
| **Return** | Path back to the nearest waypoint on his route, or to his post and original facing. | Arrives → **Relaxed**. |

Realism rule: **a curious guard finishes checking even if the meter decays.**
Posture does not drop mid-walk, as it does today, where Curious reverts the
moment awareness dips below 30. The meter still decays underneath, so a guard
who sees nothing more arrives cool and returns to Relaxed.

## 4.1 AFRAID — a freeze, layered over the posture ✅ BUILT (2026-09-23)

Requested after P6. When bullets start flying, a guard at ease may freeze;
seeing an ally die may freeze anyone, and a guard already fighting mostly keeps
his head.

| Trigger | Who rolls | Chance *(proposal)* |
|---|---|---|
| Hears the player's shot or a blast (`GunshotHeard`), or is hit (`ShotAt`) | a guard who was RELAXED when it happened | `FearGunfireQ8` 90 (~35%) |
| SEES an ally die (in his cone, in range, a clear line) | anyone not in Combat | `FearAllyDeathQ8` 128 (50%) |
| the same | a guard in Combat | `FearAllyDeathCombatQ8` 26 (~10%) |

A failed roll freezes him for `FearTicks` (60, 1 s, world clock). As built:
- AFRAID IS NOT A POSTURE. It is `Actor.FearMt`, a timer laid over whatever
  posture the trigger put him in, which is usually Combat. Squads, sweep
  groups, radio calls and the compromised-level rules all key off posture, and
  swapping it out for a second would have dropped a frightened guard out of
  all of them. Frozen, he still SEES and HEARS: the meter fills and his LKP
  updates. He does not move, turn, shout, key a radio, shoot or find bodies,
  and nothing changes his posture until it passes. Then he carries on with the
  fight he was already in, so a lone guard's radio call is delayed a second.
- The rolls use the sim's RNG, and only when one is owed. Fear does not stack:
  a frozen guard does not roll again.
- Who was at ease is decided BEFORE anyone reacts to the shot, so a guard
  shouted into the fight by the first to hear it still rolls as the relaxed
  man he was when it rang out.
- A frozen guard can be subdued from behind even if he was engaging, and is
  never picked as a backup responder.
- Only the player's shots and grenade blasts are HEARD by guards (a guard's own
  fire never was), so a gunfight between guards and nobody else frightens
  nobody at ease. Bullets flying past without hitting do not count either.
- Presentation: `!?` over a frozen guard (pale), a `!?` pop when he freezes in
  view, `AFRAID` on his debug-overlay tag. Event `Afraid` (Value 1 gunfire,
  2 death); `GetGuards` stride 13 (slot 12 afraid); `GetAiDebug` guard block
  14 ints.
- Tests (`GuardAI.FearOfGunfire/FearOfDeath`): rates over 400 seeds measured
  37.8% (35.2% target), 53.7% (50%) and 9.8% (10.2%). Frozen means no move,
  turn or radio progress for FearTicks. A guard not at ease never freezes at a
  shot, and a witness looking away never freezes. `Fuzz`: a guard frozen
  through a tick never moves. Goldens re-baked.

## 5. COMBAT

### 5.1 Entry

Any of the following puts a Relaxed or Curious guard into Combat:

| Trigger | LKP set to | Replaces |
|---|---|---|
| Meter ≥ 100 with LOS | player | same as today |
| **Snap sight**: player inside `SnapSightRange` *(150 px)*, inside the cone, with LOS, for `SnapReactTicks` *(0.2 s)* | player | new ("faster meter") |
| **Gunshot heard** (weapon's `GunshotRadius`) | shot origin | was awareness → 92 (Hunt) |
| **Shot at**: a round hits him, even a silenced one nobody heard | shooter's position at the moment of firing | new |
| Voice callout from a Combat guard (300 px, LOS between guards) | caller's LKP | was → 72 (Hunt) |
| Radio assignment as a responder | the incident's LKP | new |

`SnapReactTicks` is a human reaction time. It also leaves a sprinting takedown
a sliver of a chance, but only from behind, because the cone is still required.

### 5.2 The first decision: am I alone?

On entering Combat, the guard counts **allies**: non-prone guards within
`AllyPathRange` *(350 px of PATH distance, not straight-line; a guard behind a
wall 50 px away but 600 px round is not nearby)* that are not already in
another squad.

- **Allies present** → he shouts (the existing voice callout; allies with LOS
  join Combat) and forms a squad with them on the spot. → **Converge / Engage**.
- **Alone** → **Radio**.

### 5.3 Radio (timed, interruptible)

- The guard stops, faces his LKP and keys the radio for `RadioTicks` *(90 =
  1.5 s, WORLD clock, so dilation lets the player close the gap)*.
- **Interrupted** by death or subdual, which cancels it: nobody is dispatched.
  Taking damage restarts the timer.
- **Self-defence first**: if the player is in LOS during the call, the guard
  Engages and re-keys once contact breaks. A player who is visible and shooting
  at him gets shot, not ignored.
- On completion the call goes **level-wide** (every living guard is on the
  net):
  - The alarm rises to 2 and the incident's LKP becomes **net intel**.
  - **Responders** = `2 + kills this incident`, capped at 5. They are chosen by
    PATH distance from the caller (one Dijkstra flood from the caller's cell
    gives every guard's distance at once), **patrollers before sentries**, with
    ties broken by guard index. Guards already in Combat or Curious-investigating
    are eligible. Down, Dead and already-squadded guards are not.
  - Non-responders take the raised floor and stay Relaxed (§3).

### 5.4 HoldForBackup (simple cover)

While waiting, the caller does not advance. He picks the nearest reachable cell
(by path, within `HoldSearchRadius`, *6 cells*) that has **no LOS to the LKP**,
preferring cells adjacent to a wall. He moves there and faces the most likely
approach, the first segment of the nav path from the LKP toward him. He fires
if the player appears.

- If the pool had **zero** eligible responders, he holds until contact is lost
  and then goes Hunting alone.
- `BackupWaitMaxTicks` *(20 s)*: if the responders never arrive (killed on the
  way, say), he assaults with whoever has arrived, or holds if nobody has.

### 5.5 Rally → Assault (flank)

1. **Rally**: responders path to the caller at `SpeedHunt` (158 px/s). The
   squad is formed when `min(responders, 2)` are within `RallyRadius`
   *(80 px)*. A responder who sees the player on the way Engages; a squad does
   not wait while one of it is shooting.
2. **Plan the flank**: each member in index order runs A* from his cell to the
   LKP with a **penalty** `FlankPenalty` *(+60 per cell)* on cells within 2 of
   any earlier member's path. Where the geometry offers a second doorway the
   members take it. Where it does not (a single-door room) they stack through
   the one door, which is honest: flanking needs a second way in.
3. **Synchronise arrival**: each member's ETA = path length / `SpeedHunt`.
   Shorter paths wait `maxETA − ETA` before moving (capped at `EtaSyncMaxTicks`,
   *3 s*), so the squad breaks in from several sides at once instead of
   trickling through a chokepoint.
4. **Assault**: move on the LKP. LOS → Engage (today's §8.5 engage behaviour:
   turn 9 rad/s, strafe bands, 0.45 s aim delay, 0.8 s cooldown).
5. **Intel updates**: any member's sighting moves the net LKP. The squad
   re-plans, throttled by the repath budget (§8.4).

### 5.6 Losing the player

On reaching the LKP without LOS, the squad runs **SearchLkp**: today's Search
behaviour, 9 s (`Tune.SearchTicks`), but with sweep points drawn from
**reachable nav cells** within 40–150 px rather than raw random points (§10.3
lifted a little). Squad members take different points.

**Contact lost** = no guard on the net has seen the player for
`ContactLostTicks` *(8 s)* AND SearchLkp has completed. Then the level becomes
**compromised** and every surviving combatant goes → **Hunting / Regroup**.

## 6. HUNTING — the compromised level

Entered on contact lost, or by a body report (§7). **Permanent for the rest of
the run.** `GuardNet.Compromised` is monotonic.

### 6.1 Who does what

On the tick the level becomes compromised, `GuardNet` assigns roles once, by
guard index:

| Who | Task |
|---|---|
| **Sentries** (no route) | **HoldPost**: return to post, alert cone, ×1.9 multiplier. Their sway widens to ±`HoldSwayAmp` *(0.5 rad)*. |
| **One pair** of mobile guards, nearest the exit by path | **WatchExit**: go to the exit, then run a short sweep circuit within `ExitWatchRadius` *(8 cells)*. Assigned only if ≥ 4 mobile guards remain. |
| **All other mobile guards** (patrollers + combat survivors) | **Regroup** into pairs, then **Sweep**. |

**Pairing** is greedy by path distance, lowest index first. With an odd count
the last guard joins the nearest pair as a trio. A guard whose partner goes
Down or Dead joins the nearest pair (becoming a trio) or, with none reachable,
sweeps alone.

### 6.2 Formation: one watches front, one watches back

- **Leader** follows the nav path to the pair's sweep target and faces his
  direction of travel, with a gentle ±`WatchSwayArc` *(0.4 rad)* sway.
- **Follower** trails the leader's breadcrumb trail at `PairSpacing` *(36 px)*
  and **faces backward**: his watch sector is centred on the reverse of travel.
- **Third member** (a trio) takes the flank. His sector alternates left and
  right of travel every `FlankSwapTicks` *(2 s)*.
- Walking with facing more than 90° off travel costs `BackpedalQ8` *(0.7x)*
  speed, so the leader is capped to what the follower can keep up with:
  `SpeedSearch × 0.7` ≈ 73 px/s. Slow and deliberate, as a real sweep is.
- This needs **facing decoupled from movement** (§8.3).

What this means for the player: **a sweeping pair has no blind rear arc.**
Subdue-from-behind on a pair member requires beating the follower's cone.
Sentries holding posts and lone guards are still takedown targets.

### 6.3 Where they sweep (semantic, not random)

`sim/SweepMap.cs`, built once per level:

- The floor is divided into **sweep nodes** of `SweepNodeCells` *(6x6 cells,
  120 px)* blocks that are mostly passable. The node centre snaps to the most
  open reachable cell.
- Each node carries a **staleness** counter that climbs per tick and resets to
  0 whenever any guard has LOS to its centre within 200 px.
- A pair picks the unclaimed node with the highest score:
  `staleness − distance_penalty(path from pair) + proximity_bonus(to net LKP)`.
  It claims the node, walks there, pauses `NodeDwellTicks` *(1.5 s)* while the
  watchers sweep their sectors, and repeats.
- Result: pairs spread out rather than bunching, re-check where the player was
  last seen first, and eventually cover the whole level.

### 6.3.1 Authored sweep nodes — REQUIRED: the level editor must place them

Auto-derived nodes are the floor, not the ceiling. A designer knows where a
player hides, and the level editor **must be able to add sweep nodes by hand**.

- **Level format**: grid glyph `*` (free today: the grid uses `# . @ X $ C !`
  and `a-z`). It is floor for every other purpose: walls, nav grid, movement,
  LOS. `Level.Build()` collects the glyphs into `Level.SweepNodes`, in
  row-major order, so ordering is stable (spec §4.1). Older levels simply have
  none. Remember to add it to the glyph comment line the shipped levels carry.
- **Editor**: a new tool "Sweep node" in `game/editor.gd:TOOLS`, as
  **EditorPaint code 11** (0-10 are taken: 8 glass, 9 door, 10 objective)
  on a **letter** key, since 0-9 are all bound. Free letters today: B D E G
  I J K O P Q T U W (ask the editor's owner before taking one). LMB paints
  `*` on floor, RMB erases back to `.`, and undo/redo come for free since it
  is a grid glyph; add `*` to `EditorStampable` so brushes and paste carry
  it. It draws as a distinct marker (a hollow eye/ring) so it cannot be
  mistaken for a record cache or a guard.
- **Merging with auto nodes**: an authored node suppresses any auto-derived
  node whose block it falls in, and carries a `AuthoredNodeBonus` *(proposal)*
  on its sweep score, so pairs check designer-marked hiding spots first.
- **Validation**: an authored node on a cell the nav grid cannot reach is
  reported by the editor (as an unreachable exit is today) and ignored by the
  sim, never thrown on. `Robustness` feeds `*` through the parser like every
  other glyph.
- **Lands in P4** with the sweep map. The editor tool can come earlier (it is
  only a glyph) if you want to start marking levels before the sim reads them.

### 6.4 Leaving Hunting

- **Re-sighted** → Combat (alerted gain ×1.9, so it is quick). The pair is
  already a squad: no radio needed, the pair's second member counts as an ally.
- **Gunshot or shot at** → Combat, as §5.1.
- Otherwise never. The run ends it.

## 7. Bodies

A guard who sees a Down or Dead body (existing check: 300 px, in cone, LOS):

1. The body is marked Found (once, as today). The finder's posture becomes
   Combat with task **Radio**, and his LKP is the body. He radios
   **regardless of allies**, because this is a report, not a request for backup.
2. **Interruptible**, like any call: kill the finder mid-call and the body
   stays unreported. The finder stays Combat and searches the body's
   surroundings (SearchLkp), then goes Hunting alone.
3. On completion the level is **compromised immediately** (no fight to
   lose). Every guard is assigned per §6.1 and the first sweep node goes to the
   one nearest the body.

The existing 520 px broadcast (→ 74) is removed. The radio replaces it.

---

## 8. Pathfinding (lifts spec §10.1, now requested)

New files: `sim/NavGrid.cs`, `sim/PathFinder.cs`.

### 8.1 Nav grid

- Built once in `Level.Build()` from the 20 px cell grid.
- A cell is **passable** iff `Geometry.HitsWall(walls, centre, ActorRadius)` is
  false: the SAME test the movement code uses, so the nav grid cannot claim a
  route that `MoveSlide` then refuses. A 1-cell corridor is 20 px wide and a
  guard is 22, so it is impassable in both.
- 8-connected. A diagonal is legal only if both orthogonal neighbours are
  passable (no corner cutting).

### 8.2 A*

- Integer costs 10 / 14, octile heuristic.
- **Deterministic**: a binary heap ordered by (f, h, cell index). No
  `Dictionary` iteration. Scratch arrays are sized to the level once and reused.
- Optional per-cell **penalty layer** (for flanking).
- Output is **smoothed** by string-pulling: drop a waypoint when a swept circle
  of `ActorRadius` from the previous kept point to the next is clear (three
  parallel `ClearLine`s at −r, 0, +r). Stored as fixed-point waypoint arrays on
  the actor.
- **Dijkstra flood** variant (no goal) for "path distance from X to everyone":
  responder selection, ally counting, pairing, exit pair. One flood per event,
  not per guard.

### 8.3 Movement: `MoveTo` and `LookAt`

`Steer` splits in two:

- `MoveTo(e, tx, ty, speed, w)` advances along the current path without
  touching facing. It applies the backpedal penalty from the angle between
  travel and facing, keeps wall-slide, and keeps the unstick nudge as a
  fallback. Stuck for more than 1 s → repath.
- `LookAt(e, angle, turnRate, w)` turns facing only.
- `Steer` stays as `MoveTo` + `LookAt(heading)`, so behaviours that want the
  old "look where you walk" feel keep it in one call.
- Sway still measures what the guard actually did (turn + distance), so a
  backpedalling watcher is appropriately less accurate.

### 8.4 Budget

Guard counts are 10, 10 and 26 on the shipped levels, with up to 12,096 cells
on Terminal Twelve.

- At most `RepathPerTick` *(4)* A* searches per tick, served round-robin by
  guard index (deterministic). A guard waiting its turn keeps following its old
  path.
- A repath is triggered only by a goal-cell change, a stuck timeout, or
  `RepathCooldownTicks` *(0.5 s)* expiring while the goal moves.
- A test pins the budget: Relay Nine with all 26 guards hunting runs at under
  `X` ms/tick on the harness machine (X set from the first measurement, then
  held).

---

## 9. Data model changes

### 9.1 `sim/`

| Where | Change |
|---|---|
| `Actor.cs` | `GuardState` rewritten to the posture enum. New `GuardTask Task`, `TaskMt`, `PostX/PostY/PostFacing`, nav path arrays + index + goal cell, `RepathMt`, `SquadId`, `Role` (Leader/Follower/Flank/None), `WatchCentre` (BRAD), `RadioMt`, `ContactMt`, `SnapMt`. **All hashed.** |
| `sim/GuardNet.cs` (new) | Net LKP + age, `Compromised` (monotonic), incident kill count, pending radio call, the squad list (a `List<Squad>`, index-ordered), exit-pair ids. `HashInto`. |
| `sim/NavGrid.cs`, `sim/PathFinder.cs` (new) | §8. The grid is derived from the level, not hashed. Paths live on actors and are hashed. |
| `sim/SweepMap.cs` (new) | Nodes (derived) + staleness and claims (hashed). |
| `AlarmState.cs` | Levels 0 calm, 1 suspicious, 2 combat, **3 compromised** (sticky, no decay). Floors 16 / 38 / 38. Keeps its role as "the seed of the propagating alarm". |
| `Perception.cs` | `RangeFor`/`HalfAngleFor`/`IsAlerted` keyed on posture + task (§2.2). Snap-sight helper. |
| `SimWorld.cs` | `StepGuard` split into perception / posture transitions / task behaviours. Behaviours move to `sim/GuardBrain.cs` (SimWorld is already 1,958 lines). `GunshotHeard`, `BodyFound`, `Notice`, `HurtGuard` rerouted per §5.1 and §7. |
| `EventLog.cs` | New events: `RadioStart`, `RadioSent`, `RadioCut`, `SquadAssault`, `Compromised`, `NodeCleared`. These feed audio and HUD. |
| `SimSnapshot.cs` | `ActorView` gains `Task`, `SquadId`, `Role`, `RadioQ8` (call progress). The snapshot gains `AlarmLevel` and `Compromised`. |
| `Tuning.cs` | A "Guard AI v2" block. Every constant is documented with a *(proposal)* note until play-tested. |

**No InputFrame or replay-format change**: the AI is not input. `Step`'s nine
arguments are untouched.

### 9.2 `game/`

- `SimBridge.GetGuards()` stride 10 → 13 (task, squad, radio progress).
  `main.gd` readers updated. `ST_*` constants re-mirrored to the new ordinals,
  with a `TASK_*` table added.
- **Glyphs**: Relaxed none, Curious `?`, Combat `!`, Hunting `◎`.
- **Radio cue**: a small radio icon with a progress arc over a visible calling
  guard, and a synthesized squelch/chirp in `audio.gd` (spec §7.4: no samples),
  attenuated by distance. On `RadioSent`: a short HUD line, "radio chatter —
  backup inbound". The player learns the call went out, not where the
  responders are.
- **HUD element `floor_alert`** in `hud_layout.gd` (`ELEMENTS` + `DEFAULTS` + a
  `_hud_floor_alert` draw fn). It shows CALM / SUSPICIOUS / COMBAT /
  COMPROMISED. Obeys the harness rules: a grid-aligned default, a declared size
  that matches the paint, no overlap.
- **Off-screen edge markers**: Combat only. Hunting pairs get NONE, keeping
  the stealth asymmetry the design notes call out ("that asymmetry is the
  stealth game").
- **Debug overlay** (spec §9, toggleable, dev only): nav path, squad links,
  watch sectors, sweep-node staleness heat.

- **Level editor**: tool 8 "Sweep node" (glyph `*`), see §6.3.1.

### 9.3 Not touched

Loot, records, stash, campaign, economy. The level text format gains ONE glyph
(`*`, §6.3.1) and nothing else.

---

## 10. Deliberate deviations from the spec (to record in CLAUDE.md)

| Spec | Deviation | Why |
|---|---|---|
| §8.1 | State table replaced by posture + task. | Requested redesign. |
| §8.3 gunshot | Gunshot → Combat, not awareness 92. | Requested ("hear a gunshot → combat"). |
| §8.3 body | The 520 px → 74 broadcast is replaced by a radio report + compromised level. | Requested (radio + sweep). |
| §8.3 callout | Voice callout recipients enter Combat, not Hunt at 72. | Combat is now the one "fighting" posture. |
| §8.6 | **The 120 px row moves** (snap sight at 150 px: every stance there ≈ 0.2 s react + 0.45 s aim delay ≈ 0.65 s). The **250 and 380 px rows must still pass within 10%**, untouched. | Requested ("faster meter"). The test is re-baselined for that row only, in the same commit. |
| §8.7 | Preserved in the new form: Combat with LOS never searches or drops posture. | The regression test is ported, not deleted. |
| §10.1 | A* pathfinding built. | Explicitly requested ("pathfind to the gunshot"). |
| §10.3 | Sweep is staleness-driven, not random. SearchLkp points are reachable nav cells. | Needed for pairs to cover a level. |

Golden hashes move at every phase below, re-baked with `-- --record` in the
same commit. Existing replays will diverge (`--verify` names the tick), which
is expected for an intentional sim change.

---

## 11. Phases and acceptance tests

Each phase builds, passes every harness and re-bakes goldens before the next
starts. New tests go in `tests/GuardAI.cs` (sim harness) unless noted.

**Sequencing vs. the Kit revamp**: do NOT interleave. AI v2 touches `Actor` and
`StepGuard` but not the replay format or the stash, so it can land cleanly
**before** Kit revamp steps 1–4, which then touch `Actor` once more.
Recommended order: AI v2 first.

### P0 — Nav grid, A*, decoupled facing ✅ BUILT
Guards keep today's state machine but move by path. Landed as
`sim/NavGrid.cs`, `sim/PathFinder.cs`, the Movement section of `SimWorld`
(`MoveTo` / `LookAt` / `Steer` / `ServePaths`) and `tests/Navigation.cs`, plus
three `Fuzz` invariants. Goldens re-baked. As built, differing from §8:
- Node points are pushed 2 px off an adjacent wall, not taken at the cell
  centre: centres sit 10 px from a wall and a guard is 11 wide, so without the
  push every two-cell corridor was sealed.
- Glass (`=`) is nav-wall and doors (`+`) are walkable, for the glass/door
  work landing alongside.
- Found and fixed: Relay Nine's guard `f` spawned wedged in a one-cell gap and
  had never moved. Moved one cell east, and pinned by a test that every guard
  and the spawn start on a standable cell.
- Measured: 26 guards all hunting across Relay Nine cost ~0.1 ms/tick.
- The nav grid agrees with `HitsWall` for every cell of all three levels.
- A* returns optimal lengths on hand-built fixtures and is identical across 2
  runs (determinism).
- A guard reaches an LKP behind a U-shaped wall. Today's `Steer` provably fails
  the same fixture (asserted, so the test cannot pass by accident).
- Every patrol route on every shipped level is fully pathable.
- `MoveTo` with facing held fixed moves at the backpedal speed.
- Performance budget on Relay Nine.

### P1 — Posture/task machine, Curious investigate, snap sight, gunshot → Combat ✅ BUILT
- Detection curve: 250/380 px rows unchanged within 10%, 120 px row
  re-baselined.
- §8.7 ported.
- A Curious guard walks to the stimulus, scans, returns to his post, and ends
  Relaxed at his original facing.
- A gunshot inside the radius → Combat. Outside it → nothing (keeps the
  subsonic test meaningful: subsonic still alerts fewer guards).
- A round hitting a guard who heard nothing → Combat with LKP at the shooter.

### P2 — GuardNet and radio ✅ BUILT

P1 and P2 landed together as `sim/SimWorld.Guards.cs` (guard AI moved out of
SimWorld.cs as a partial class), `sim/GuardNet.cs`, the posture/task enums in
`Actor.cs`, `PathFinder.Flood`, and `tests/GuardAI.cs` plus five `Fuzz`
invariants. As built, differing from or sharpening the plan:
- **The §8.6 curve holds exactly at 250/380 px.** A curious guard past AwHunt
  watches with the old Hunt cone and, while he still sees the player, closes
  in as old Hunt-with-LOS did. He only walks over to investigate once the
  stimulus has been gone CuriousLookTicks. The 120 px row is snap sight: 0.20 s
  to Combat in every stance, 0.63 s to the first shot.
- **Combat means the meter is full.** Entering it sets awareness to at least
  100, and a Combat guard with LOS engages outright.
- **A noise holds for the grace window** (0.8 s), exactly as a glimpse does.
  **Hurry** (investigating at SpeedHunt) is decided once, when he sets off.
- **The body report does not raise the alarm up front.** It used to go to 2 at
  once; now the floor learns nothing until the report is through, which is
  the window to stop it. The old 520 px broadcast is gone.
- **A shout never freshens intel**, and guards only shout while intel is less
  than ContactLostTicks old. Found by `GuardAI.RallyAndGo`: a searching
  guard's stale shouts pulled hunting guards back into Combat, reset the age,
  and kept a lost fight alive forever.
- **Responders are never Engaging or keying a radio**; everyone else is
  eligible, patrollers before sentries.
- **Hunting is SOLO until P4**: patrollers walk their route at search pace
  with alerted eyes; sentries hold their post and sweep ±0.5 rad. A noise
  still sends a hunting guard to look, and he goes back to hunting after.
- **Presentation (P5 early, minimal)**: glyphs ? ! ○ for Curious, Combat,
  Hunting; a filling ring beside a guard keying his radio; off-screen markers
  for Combat only. No audio yet, and no HUD floor-alert element yet.
- A lone guard radios. An allied guard does not.
- Killing or subduing mid-call → zero responders dispatched, `RadioCut`
  logged.
- Responder count = 2 + kills, capped at 5, on a fixture with enough guards.
- Responders are the nearest by PATH, with a wall fixture where the
  straight-line-nearest guard is not chosen. Patrollers are chosen before
  sentries.
- HoldForBackup picks a cell with no LOS to the LKP.

### P3 — Squads, flank, synchronised assault ✅ BUILT

`SimWorld.PlanAssault` / `Behave_Assault` (new task `Assault`, appended),
per-guard `RouteX/RouteY/RouteIndex/WaitMt`, per-squad `PlannedX/Y` and
`ReplanMt`, all hashed; tests `GuardAI.Flank/OneDoor/Replan` plus a `Fuzz`
invariant. As built:
- The flank applies to BACKUP squads (caller + responders). A group pulled
  together by a shout still converges straight in; it never gathered, so
  there is nothing to synchronise.
- ETA is the WALKED length of the A* cell path at SpeedHunt, not the
  penalised cost; the route is smoothed after measuring.
- No penalty within FlankGoalFreeCells (3) of the LKP: every route ends
  there. A member already Engaging or keying a radio is not planned for.
- The squad re-plans when the net's LKP moves more than 80 px, at most once
  a second, and with no sync wait: the fight is already on.
- On the three-door fixture the squad of three takes three different doors,
  and no two routes share 30% of their cells. The man on the short west route
  holds back, and all three arrive within EtaSyncMaxTicks. On one door they
  stack through it and all arrive.
- On a two-door room fixture, two members' paths share under 30% of cells.
- Arrivals at the LKP fall within `EtaSyncMaxTicks` of each other.
- On a one-door fixture, the plan degrades to a stack without failing.

### P4 — Compromised level and paired sweep ✅ BUILT

`sim/SweepMap.cs`, `SweepGroup` and `Net.Groups/Focus` in `GuardNet.cs`, the
sweep section of `SimWorld.Guards.cs`, `Level.SweepNodes`, the editor tool
(key O, code 11) and its validator warning; tests `GuardAI.Groups/OddCounts/
Formation/MemberLost/LateJoiner/Coverage/AuthoredNodes`, three `Fuzz`
invariants, a sweep performance budget, and `editor_check.gd`. As built:
- NO SEPARATE REGROUP TASK. The leader waits whenever a member is more than
  `GroupWaitDist` behind or off investigating a noise, and a man that far
  back faces where he is going to catch up. Gathering at formation is the same
  rule, so it needed no state of its own. `WatchExit` was appended; `Regroup`
  was not.
- MOBILE means a patroller or anyone who comes out of a fight, so a sentry
  sent as backup sweeps afterwards rather than walking back to his post.
- Staleness is tracked only once the level is compromised, and every node
  starts equal; first picks go by distance, focus and authored bonus. A node
  is SEEN when a hunting or fighting guard has it in his CONE within 200 px,
  so the watchers' sectors are what clears the map.
- The exit group takes nodes within ExitWatchCells of the exit; with none
  there it stands on the exit itself (Target -2).
- The '*' tool is one-per-click (not `EditorStampable`) and does not paint
  over walls, glass or doors.
- Measured: every Substation 4 node seen in 23 s; the sweep costs 0.30
  ms/tick on Relay Nine (158 nodes, 12 groups) and 0.18 on Terminal Twelve.
- After contact lost, no guard is Relaxed or Curious again for 10,000 ticks
  (also a `Fuzz` invariant).
- Pairs form. Each follower's facing is within its sector of reverse-travel.
- An exit pair is assigned iff ≥ 4 mobile guards remain. Sentries hold posts.
- A body found → radio → compromised. Killing the finder mid-call keeps the
  level uncompromised.
- Sweep coverage: every node on Substation 4 is visited within T seconds.
- Authored sweep nodes (`*`): parsed, round-trip through `ToText`, suppress
  the auto node in their block, and are visited before plain auto nodes.
  The editor paints and erases them (`editor_check.gd`).

### P5 — Presentation ✅ BUILT

As built, differing from §9.2:
- NO NEW `floor_alert` HUD ELEMENT. The existing `alarm` element (same box,
  no layout churn, no saved layouts invalidated) now names the level: CALM,
  SUSPICIOUS, COMBAT, COMPROMISED.
- Sounds (`audio.gd`, appended ids): RADIO_KEY on a call starting, RADIO_SENT
  on one going through, RADIO_CUT on one cut off, COMPROMISED (a two-swell
  klaxon). Radio sounds are heard within 420 px, like doors, and none scale
  with the world clock. The NOTICE line always says a call went through
  ("radio chatter · backup inbound" / "· something has been reported") and
  when the floor is compromised, heard or not.
- F3 toggles the AI debug overlay (`game/ai_debug_overlay.gd`, fed by
  `SimBridge.GetAiDebug`): facing, posture/task/awareness, nav paths, assault
  routes, LKPs, squad links and rally rings, sweep groups and their targets,
  sweep-node staleness heat, the net's intel and focus.
- Harness: `editor_check.gd` checks the ST_*/TASK_* mirrors against the
  sim's enum names, parses the live snapshot to its exact length, and draws
  the overlay inside a real `_draw` with every section populated;
  `audio_check.gd` renders the four new sounds.
Glyphs, radio icon and arc, squelch audio (`audio_check.gd`), the `floor_alert`
HUD element (`inventory_check.gd` / the HUD layout rules), Combat-only edge
markers, and the debug overlay. The `--check-only` pass on every touched `.gd`.

### P6 — Bug-finding suites ✅ BUILT

- `SimWorld.NormaliseGuard` + `TaskFits`: the sim is total over guard state,
  repairing an impossible posture/task pair, dangling squad/group ids, a radio
  purpose off a fighting guard, or an out-of-range index, to the posture's own
  default. `SimWorld.Normalised` counts repairs (not hashed); `Fuzz` asserts
  it stays 0, so the repair can never hide a real bug.
- `Fuzz`: "a guard's task fits his posture" and "normal play never needs a
  guard repaired", on top of P1-P4's invariants (posture sticky, radio,
  squads, groups, claims, routes, nav).
- `Exhaustive.GuardStates`: 14,896 injected states. Checked for teeth: with
  `NormaliseGuard` disabled the rules check fails at once, and corrupt indices
  throw `IndexOutOfRangeException`.
- `Robustness`: the AI ladder (fight, compromise, sweep) on every adversarial
  level (one byte flip in four), plus `GuardAi()` edge cases: floods from
  nowhere, a sweep map with no floor, a floor of '*' and no guards, a double
  compromise, a walled-in guard.
- Measured: the cross product runs in ~5 s, and the AI ladder adds ~3 s to
  Robustness. (Robustness's `Replays` section takes ~59 s in the full
  harness, which is the bulk of its time. Not investigated further; its
  individual cases run in well under a second each in isolation.)
`Fuzz` invariants every tick:
- a squad's members are alive, non-prone, and in exactly one squad;
- every path waypoint is on a passable cell;
- `Compromised` is monotonic;
- no Hunting → Relaxed/Curious transition;
- radio progress is in range;
- guards are neither created nor destroyed (existing).

`Robustness`: A* on degenerate levels (no floor, a single cell, disconnected
islands, `MaxDim`) returns "no path" and never throws.

### P7 — Cover (later)
Cover nodes derived from the nav grid (floor cells adjacent to walls, with a
peek cell), used for Engage (fire from a peek cell, reload in cover) and a
better HoldForBackup. Separate plan when P0–P6 have been played.

---

## 12. Proposed tuning (all *(proposal)*, to play-test)

| Constant | Value | Note |
|---|---|---|
| `SnapSightRange` | 150 px | inside the cone, LOS |
| `SnapReactTicks` | 12 (0.2 s) | reaction time |
| `CuriousLookTicks` | 48 (0.8 s) | stop-and-stare before walking |
| `SpeedInvestigate` | 96 px/s | = patrol |
| `LookAroundTicks` / `LookAroundArc` | 240 (4 s) / 1.2 rad | |
| `AllyPathRange` | 350 px | path distance |
| `RadioTicks` | 90 (1.5 s) | world clock |
| `ResponderBase` / `PerKill` / `Cap` | 2 / 1 / 5 | |
| `HoldSearchRadius` | 6 cells | |
| `BackupWaitMaxTicks` | 1200 (20 s) | |
| `RallyRadius` | 80 px | |
| `FlankPenalty` | +60 per cell, within 2 cells | |
| `EtaSyncMaxTicks` | 180 (3 s) | |
| `ContactLostTicks` | 480 (8 s) | plus a completed SearchLkp |
| `PairSpacing` | 36 px | |
| `BackpedalQ8` | 179 (0.7) | |
| `WatchSwayArc` / `FlankSwapTicks` | 0.4 rad / 120 (2 s) | |
| `HoldSwayAmp` | 0.5 rad | sentries when compromised |
| `ExitWatchRadius` | 8 cells | |
| `SweepNodeCells` / `NodeDwellTicks` | 6 / 90 (1.5 s) | |
| `RepathPerTick` / `RepathCooldownTicks` | 4 / 30 (0.5 s) | |

---

## 13. Risks and open points

- **Permanent compromise may feel unwinnable** on big levels. Levers: the
  sweep's slow backpedal pace, the exit pair only at ≥ 4 mobile guards, and
  the sweep ignoring the player's true position (intel only). If play-testing
  says it is too harsh, the first knob is `ExitWatchRadius`, then the pair
  pace. The rule "never relaxes" itself stays.
- **Snap sight is stance-blind.** Standing still 140 px in front of a guard
  now gets you shot in ~0.65 s. That is intended by the choice, but it makes
  the stealth tier irrelevant inside 150 px. If that reads badly, a cheap
  option is to require the inner half of the cone for the snap.
- **Relay Nine has 26 guards.** The responder cap keeps a single call sane,
  but a compromised Relay Nine is ~10 roaming pairs. Watch the difficulty
  derivation in `missions.gd`: threat does not know about AI changes.
- **Omniscience creep.** Squads act only on net intel (LKP + age), never on
  the player's true position. The one exception is a guard who can see the
  player. A test should pin it: move the player unseen and the squad's target
  does not follow.
- **Sweep vs. the "Kit revamp" guard-kit change**: both edit `Actor`. Land
  them in sequence, as §11 says.
