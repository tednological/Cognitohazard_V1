# Cognitohazard — Godot 4 Port Specification

**Handoff target:** an agent (Claude Code) working in a Godot 4.x 2D project.
**Source of truth for behaviour:** `cognitohazard_combat_testbed.html` — a validated browser prototype.
**Status of this document:** implementation spec, not a design doc. Every number here was measured in the prototype. Nothing here is aspirational.

---

## 0. Read this first

### What you are porting
A top-down, one-hit-kill stealth-action combat slice with three interlocking systems:
1. **Time dilation paid for with knowledge** (the "parasite")
2. **A record economy** where information is fuel, score, and loss condition simultaneously
3. **Graded stealth AI** driven by an awareness accumulator, not a detection boolean

### What you are NOT porting
- Dialogue, verb resolution, factions, the fact/proposition model, zone authority, capture and coercion. Out of scope. Do not stub them, do not design them, do not add hooks "for later" beyond what §2 specifies.
- Art. Sprite assets follow `cognitohazard_art_pipeline.md` (adopted for scripted Blender production). Procedural drawing remains the fallback; integration follows human review.
- Audio design. Port the synthesis parameters as given (§7); do not substitute sample-based audio.

### Constants are load-bearing and are NOT yours to tune
Every number in §5–§8 was arrived at by hand-feel iteration or by measured headless test. They are placeholders in the sense that they were tuned against **browser input latency**, which is worse than Godot's — but retuning is a human-in-the-loop task.

**Rule:** port the numbers exactly. Put them in one exported resource per system (§3.4). If a number feels wrong, say so in your report; do not change it silently. A drifted feel constant produces no test failure and no visible artifact, which makes it the single most dangerous class of silent regression in this codebase.

---

## 1. Architecture

### 1.1 Hard split
```
sim/          pure RefCounted. ZERO Node dependencies. Runs headless.
              No get_node, no signals to scene tree, no Input, no OS time,
              no randf(), no Time.get_ticks_*, no print to game UI.
game/         Node2D presentation + input. READS sim state. NEVER writes
              game logic. Owns rendering, audio, camera, particles, HUD.
tests/        headless harness. Drives sim/ only. No game/ imports.
```

This split is the project's compoundable surface for agent assistance and is non-negotiable. If a task appears to require `sim/` touching a Node, the task is wrong — report it instead of working around it.

### 1.2 Language
- `sim/` — **C#**. The reason is Roslyn: symbol resolution, find-all-references, type errors, and analyzer output arrive as structured pre-runtime feedback you can act on. That is a verification surface the scripting side does not provide, and the sim layer is where correctness is mechanically checkable.
- `game/` — **GDScript**. Faster iteration on feel, direct access to the editor idioms, and nothing in it needs compiler-grade verification because its correctness is established by a human looking at it.
- Boundary: sim exposes a plain data snapshot per frame. No C# → GDScript callbacks in the hot path.

If the C# build is unavailable in the target environment, stop and report. Do not silently implement `sim/` in GDScript — that decision changes the project's verification story and is not yours to make.

### 1.3 Frame model
```
_physics_process(delta):        # Godot, 60 Hz fixed
    accumulate real delta
    while accumulator >= 1/60:
        sim.step(TICK)          # TICK is the integer constant 1
        accumulator -= 1/60
game/_process(delta):           # render, interpolate, audio, input sampling
```

- `sim.step()` takes **no delta**. One call = one tick = 1/60 s of world time. Time dilation is expressed as *which* subsystems advance on a given tick (§5.3), never as a variable delta.
- This is a change from the prototype, which used variable `dt`. It is required for determinism. Expect small behavioural differences and flag any that feel significant.

---

## 2. Data model

### 2.1 Level format (already designed — port the parser as-is)
Plain text, 48 × 28 grid, 20 px cells → 960 × 560 px playfield.

```
name: Substation 4
# glyphs  # wall  . floor  @ spawn  X exit  $ records  a-h guard start
grid:
################################################
#..............#...........#...................#
... 28 rows total ...
################################################
> a 10,5 10,22
> b 19,9 28,9 28,21 19,21
```

| Glyph | Meaning |
|---|---|
| `#` | wall |
| `.` | floor |
| `@` | player spawn (last one wins) |
| `X` | exit — any contiguous block; exit rect is the bounding box of all `X` cells |
| `$` | record cache |
| `a`–`h` | guard start; letter is the route id |

Route lines start with `>`, then the route id, then `col,row` waypoints in order. Loop closes implicitly. **A guard with no matching route line is a stationary sentry** — this is a feature, not a fallback.

Requirements:
- Round-trip must be lossless: `to_text(from_text(s)) == s` for any well-formed input. This is an existing passing test; keep it passing.
- Parser must be **total**: malformed input yields a playable level with defaults, never an exception. Missing `@` → spawn at cell (1,1). Missing `X` → exit at (GW-3, GH-3), 2×2.
- The format is the source of truth for level content. Levels live in `levels/*.txt`, version-controlled as text. Do not introduce a binary or `.tres` level format.

### 2.2 Grid → collision geometry
Walls are merged into rectangles with a greedy horizontal-run-then-vertical-extend pass before use. On the reference level this collapses 222 wall cells to 18 rects. Raycast cost is linear in rect count and every actor casts 24 cone rays per frame plus the player's 400-ray visibility polygon, so the merge is a hard requirement, not an optimisation.

Port the merge exactly; there is a passing test asserting the 222→18 result on the default level.

### 2.3 Records
```
Record {
    name:    StringName
    tier:    int        # 1..3, contributes to score
    charge:  int        # FRAMES remaining in current stage. 240 == 4.0s
    state:   enum { Intact, Degraded, Gone }
}
```
- `charge` is an **integer frame count**, not a float second count. This is a deliberate change from the prototype and is required for exact replay hashing.
- Records are appended to `held[]` in acquisition order. Order is semantically meaningful (§6.2) — never sort or compact this array.
- `Gone` records **stay in the array** as spent husks. They are excluded from scoring and fuel selection but remain visible in the HUD so the run's damage stays legible.

### 2.4 Acquisition
| Source | Yield | Cost |
|---|---|---|
| Subdue a guard from behind, then search | that guard's records, intact | none |
| Walk onto a `$` cache, hold 0.7 s | that cache's records, intact | none |
| Shoot a guard | nothing | guard's records permanently destroyed |

Player starts holding exactly one tier-1 record named `briefing note`. This exists to solve the empty-wallet problem — without it the first room has no dilation available, which is the moment the player is least equipped. Treat it as a known fiction debt, not a bug.

---

## 3. Sim layer surface

### 3.1 Classes
```
SimWorld        owns the tick, owns every subsystem, the only public entry point
Level           grid, merged rects, spawn, exit, cache defs, guard defs
Geometry        static. ray-vs-AABB, clear_line, circle-vs-rect, visibility polygon
Actor           player + guards. position, facing, radius, state
Perception      awareness accumulation, LOS/cone queries, stimulus routing
RecordStore     held[], fuel selection, burn ladder, scoring
TimeAuthority   THE single owner of every time-scale effect (§5.3)
Projectiles     bullets, substepped sweep, hit resolution
AlarmState      floor alarm level, decay, awareness floors
EventLog        append-only record of everything that happened
DetRng          seeded deterministic RNG — the only randomness source
```

### 3.2 Public API
```
SimWorld.new(level: Level, seed: ulong)
SimWorld.step(input: InputFrame)          # exactly one tick
SimWorld.snapshot() -> SimSnapshot        # read-only, for game/ and tests/
SimWorld.state_hash() -> ulong            # see §4
```
```
InputFrame {
    move_x, move_y: int    # -1, 0, +1 each. Already normalised by game/
    aim_angle:      int    # BRAD: 0..65535 mapped to 0..2pi. NOT a float.
    fire:           bool
    sneak:          bool
    dilate:         bool   # Space held
    subdue:         bool   # F pressed this frame (edge, not level)
    reload:         bool   # R pressed this frame (edge)
}
```
`aim_angle` as a 16-bit integer is deliberate. Mouse aim is the only continuous input in the game; quantising it at the boundary is what makes a recorded input stream replay bit-exactly.

### 3.3 Forbidden in `sim/`
`randf`, `randi`, `Time.*`, `OS.*`, `Engine.get_frames_*`, `Input.*`, any `Node`, any `print` to a game surface, any iteration over a hash-ordered collection whose order affects state, and any use of `delta`. Treat each as a build-breaking error, not a style issue.

### 3.4 Tuning resources
One `Resource` per system, exported so a human can adjust in-editor: `PerceptionTuning`, `CombatTuning`, `DilationTuning`, `FeelTuning`. Defaults exactly as specified in §5–§8. `sim/` reads these as plain structs at construction; it does not hold `Resource` references at runtime.

---

## 4. Determinism and the test harness

This is milestone 0 and gates everything else.

### 4.1 Rules
- Fixed tick only. No wall-clock reads in `sim/`.
- **Integers for all state that feeds the hash**: positions in sub-pixel fixed point (1/256 px), angles in BRAD, awareness in tenths (0..1200), record charge in frames.
- One `DetRng`, seeded from the run seed, all draws through it, draw order fixed by iteration order over stable arrays. Guards iterate by index, always.
- Float math may appear in `game/` freely and in `sim/` only where the result is immediately quantised back to integer before it touches state.

### 4.2 Replay hash test — build this on day one
```
Replay { seed: ulong, level_text: string, inputs: InputFrame[] }
```
Test: load replay → construct SimWorld → step through every input frame → assert `state_hash()` matches a golden value at frames 60, 300, 900, and final.

This is the externalised verification surface that stops agent-assisted sim work from drifting. Without it, this codebase is not safe to let an agent iterate on. Commit at least three recorded replays: a clean stealth run, a loud run, and a run that exhausts the record supply.

### 4.3 Headless harness
`tests/` must run with no window and no audio, driven from the command line, reporting pass/fail with a non-zero exit code on failure. Port these existing passing assertions:

| Test | Assertion |
|---|---|
| Wall merge | default level → 18 rects from 222 cells |
| Level round-trip | `to_text(from_text(s)) == s` |
| Marker sanity | no `@ X $ a-h` glyph coincides with a `#` cell |
| Idle stability | 30 s with the player at spawn → all guards remain in Patrol, alarm 0 |
| Detection curve | the table in §8.5, ±10% |
| Gunshot propagation | one shot → every guard within 640 px enters Hunt, alarm = 2 |
| Body discovery | guard with LOS to a corpse within 300 px → alarm 2, finder in Hunt |
| Burn ladder | one record yields 8.0 s of dilation across exactly two stage transitions |
| Stress | 60 s of randomised input with rendering → no exception, no NaN in snapshot |

---

## 5. Time dilation

### 5.1 Modes
Ship all three behind a debug switch; **Parasite is the shipping mode.**

| Mode | Rule |
|---|---|
| **Parasite** | Hold `dilate` → world slows, consumes the selected record |
| Meter | Hold `dilate` → world slows, drains a free 100-unit meter at 27/s, refills 8/s when released |
| Gated | World runs at 0.045× when the player holds no movement key, 1.0× when moving |
| Live | No dilation |

### 5.2 Constants
```
WORLD_SLOW        = 0.18      # world tick rate while dilating
PLAYER_CLOCK      = 0.62      # player's own tick rate while dilating
SCALE_LERP        = exp(-dt * 20)   # smoothing toward target, game/ visual only
GATED_STILL       = 0.045
RECORD_STAGE      = 240 frames (4.0 s) per stage
JOLT              = 18 frames (0.30 s) hard snap to real time at each transition
```
The relative advantage while dilating is 0.62 / 0.18 ≈ 3.4×. Player movement, fire rate, and reload run on the player clock. **Aim rotation is free and unscaled** — this is a known exploit (§10.2), preserved intentionally so the port matches the prototype.

### 5.3 TimeAuthority — resolves a real conflict
Three systems want to manipulate time: dilation, hitstop on kills, and the degradation jolt. In the prototype hitstop is crudely gated off whenever `timeScale <= 0.55`. That is a patch, not a design, and it will compound badly as more effects are added.

`TimeAuthority` is the sole owner. Every effect registers a request; the authority resolves them by priority and returns, per tick, which of `{world, player, presentation}` clocks advance:

```
priority 0  Jolt       — overrides everything, forces world+player to real time
priority 1  Hitstop    — freezes world+player, presentation continues
priority 2  Dilation   — world at WORLD_SLOW, player at PLAYER_CLOCK
priority 3  Normal
```

No subsystem may read or write a time scale directly. This is the one place where you should improve on the prototype rather than port it faithfully.

---

## 6. The parasite (record burning)

### 6.1 Ladder
Holding `dilate` drains the selected record's `charge` by 1 per real frame.

```
Intact,   charge hits 0  →  state = Degraded, charge = 240, JOLT fires
Degraded, charge hits 0  →  state = Gone,     charge = 0,   JOLT fires, reselect fuel
```
8.0 s of dilation per record, split into two 4.0 s stages by a mandatory 0.30 s interruption. **The jolt must interrupt** — the player cannot hold through a degradation. This is what makes the cost felt rather than merely counted.

### 6.2 Fuel selection
Ship **Newest first.** Keep the others behind the debug switch for comparison.

| Order | Selection | Observed effect |
|---|---|---|
| **Newest** | last non-`Gone` entry in `held[]` | cost escalates naturally; late-run looting becomes self-punishing |
| Cheapest | lowest tier, ties break newest | degenerate — tier-1 chaff becomes a fuel tank |
| Random | uniform over non-`Gone` | reads as unfair within two minutes |

Reselect only when the current selection becomes `Gone`, never per-frame — the selection must be stable enough to display.

### 6.3 Scoring
```
provable   = sum(tier) where state == Intact
unprovable = sum(tier) where state == Degraded
destroyed  = count(Gone) + sum(tier of records destroyed by gunfire)
```
Reaching the exit scores. Dying scores zero across all three.

**Known design gap:** `Degraded` is currently only a smaller number. The intent is that it fails *at the filing desk* — you present the record, admissibility is checked, the claim dies. That system does not exist yet and is out of scope. Do not invent it. Leave the scoring readout as three separate figures so the eventual system has somewhere to attach.

---

## 7. Combat and feel

### 7.1 Player
```
radius            11 px
speed             196 px/s walking, 98 px/s sneaking
magazine          8, no reserve limit
fire cooldown     0.17 s (player clock)
reload            1.4 s (player clock)
muzzle offset     22 px along facing
spread            0.014 rad + heat * 0.085
heat              +0.34 per shot, decay 1.5/s, clamped [0,1]
recoil            1.0 on fire, decay 7/s — sprite offset only, no positional push
```
One hit kills the player. One hit kills a guard. No health, no armour.

### 7.2 Projectiles
```
player bullet     840 px/s, life 2.0 s
guard bullet      640 px/s, life 2.2 s, spread ±0.045 rad
substeps          3 per tick — required, or bullets tunnel walls at dilated speeds
hit radius        actor radius + 2
```
Bullets are simulated objects, not raycasts. At `WORLD_SLOW` they are visibly in flight, which is the entire point of the dilation mechanic.

### 7.3 Feel budget — port all of it, it is not decoration
| Effect | Value |
|---|---|
| Hitstop, guard killed | 0.055 s, world+player frozen, presentation runs |
| Hitstop, player killed | 0.10 s |
| Shake on fire | magnitude 3.2, directed opposite to aim |
| Shake on kill | 7, directed along bullet travel |
| Shake on player death | 11 |
| Shake decay | `exp(-dt * 11)` |
| Muzzle flash | additive cone + bloom, ~4 frames, lingers under dilation |
| Shell ejection | perpendicular ±0.35 rad, 110–190 px/s, drag 2.6, settles to a permanent decal |
| Wall impact | 7 sparks back along incidence, pockmark decal |
| Kill | 16 blood particles in a ±0.7 rad cone along bullet travel, 4 pool decals |
| Decal cap | 150, FIFO |
| Dry fire | click, 0.22 s cooldown, shake 1.2, no shot |
| Reticle | expands with heat, `(0.014 + heat*0.085) * 260 + 6` px |

Hitstop is the single largest contributor to how the guns feel. If you cut one thing, do not cut this.

### 7.4 Audio — synthesized, no samples
Filtered noise burst plus a low sine thump per event. Every sound's frequencies scale by the current world time scale and durations scale by its inverse, so dilation pitches the whole mix down.

```
shot        noise 0.11 s, 3200→140 Hz lowpass, gain 0.62
            + sine 0.10 s, 200→58 Hz, gain 0.42
guard shot  noise 0.09 s, 2300→180 Hz, gain 0.30 + sine 0.08 s, 160→55 Hz, 0.20
wall hit    noise 0.05 s, 5200→1100 Hz bandpass Q 2.4, gain 0.17
flesh       noise 0.20 s, 820→90 Hz, 0.45 + sine 0.30 s, 96→40 Hz, 0.30
dry click   noise 0.03 s, 3400→2100 Hz bandpass Q 3, gain 0.28
reload      three bandpass clicks at 0 ms, 260 ms, 980 ms
pickup      triangle 680→1020 Hz then 1020→1360 Hz
degrade     saw 0.55 s, 320→132 Hz, 0.24 + noise 0.35 s, 900→180 Hz
destroy     saw 0.85 s, 210→44 Hz, 0.32 + noise 0.55 s, 700→90 Hz
notice      square 0.14 s, 520→760 Hz, 0.16
alert       square 420→300 Hz then 300→220 Hz
```
**Master lowpass:** `cutoff = 400 + 17600 * pow(world_scale, 0.7)` Hz, updated per frame. This closing filter under dilation sells the effect more than any visual does. Implement it first.

---

## 8. Stealth AI

### 8.1 States
| State | Entry | Behaviour | HUD glyph |
|---|---|---|---|
| Patrol | default with a route | walks route, 96 px/s (118 if alarm ≥ 1) | — |
| Sentry | default without a route | stands, sways ±0.16 rad/s | — |
| Curious | awareness ≥ 30 | stops, turns toward the stimulus at 3.2 rad/s | `?` |
| Hunt | awareness ≥ 66 | moves to LKP at 158 px/s; **holds on the player while LOS persists** | `!` |
| Search | reached LKP without LOS | sweeps random points for 9 s, 104 px/s, then stands down | `○` |
| Engage | awareness ≥ 100 **and** LOS | fires | `!` |
| Down | subdued from behind | prone, searchable, discoverable as a body | — |
| Dead | shot | prone, records destroyed, discoverable as a body | — |

### 8.2 Perception tuning
```
range         Patrol 400  Curious 430  Hunt 450  Search 450  Engage 470   (+60 if alarm >= 2)
half-angle    Patrol 0.85 Curious 1.00 Hunt 1.10 Search 1.05 Engage 1.15  (radians)
gain          78 /s at point-blank, dead centre
decay         14 /s, only after the grace window
grace         0.8 s — suspicion lingers after the sightline breaks
thresholds    curious 30, hunt 66, engage 100, cap 120
```
Stimulus strength:
```
q      = clamp(1 - offset/half, 0.30, 1) * clamp(1 - dist/range, 0.15, 1)
mul    = (player moved this tick ? 1.35 : 0.50)
       * (sneaking ? 0.60 : 1.0)
       * (state is Curious/Hunt/Search ? 1.9 : 1.0)
stim   = gain * q * mul
```
The 1.9× alerted multiplier matters: a guard already looking for you confirms far faster than one on routine patrol.

Awareness update per tick: if `stim > 0`, add and reset grace to 0.8 s. Else if grace remains, decrement grace and hold awareness. Else decay toward the alarm floor.

### 8.3 Non-visual stimuli
| Stimulus | Radius | Effect |
|---|---|---|
| Footsteps (walking, not sneaking) | 170 px | +30/s awareness, **no LOS required**, capped at 62 so noise alone never reaches Engage |
| Gunshot | 640 px | awareness → 92, LKP = shot origin, alarm → 2 |
| Body or downed guard seen | 300 px, within cone, LOS | finder awareness → 100, alarm → 2, broadcast to 520 px → 74 |
| Subdue | 120 px | nearby guards → 34 |
| Callout from a Hunt/Engage guard | 300 px, requires LOS between guards, every 1.3 s | recipient → 72, LKP shared |

### 8.4 Floor alarm
Three levels. Level 1 sets an awareness floor of 16, level 2 sets 38 and grants +60 cone range and faster patrols. Decays one level after 22 s with no guard in Curious, Hunt, Search or Engage.

This replaces the global detection boolean and is the seed of the eventual propagating alarm subsystem. Keep it in `AlarmState` as its own class so that work has somewhere to land.

### 8.5 Engage behaviour
Turn toward the player at 9 rad/s. Strafe out at 76 px/s if beyond 210 px, in if inside 115 px, hold otherwise. Initial aim delay 0.45 s, then fire and set a 0.8 s cooldown with a 0.16 s re-aim. Only fire with live LOS.

### 8.6 Measured detection curve — this is the acceptance test
Time from entering an unobstructed sightline to the first guard shot, sentry facing the player:

| Range | Walking | Sneaking | Standing still |
|---|---|---|---|
| 120 px | 0.95 s | 1.57 s | 2.55 s |
| 250 px | 1.63 s | 2.73 s | 4.40 s |
| 380 px | 4.12 s | 6.63 s | 10.53 s |

Monotonic in both distance and stance, with standing still the stealthiest stance. If your port deviates by more than 10% in any cell, something is wrong in §8.2 — do not adjust the tuning to make the test pass.

### 8.7 The bug that was already fixed — do not reintroduce it
A guard reaching its LKP would flip to Search, turn away to pick a sweep point, and lose a player standing directly in front of it. The fix has two parts, both required:
- **Hunt** does not transition to Search while LOS to the player persists; it turns to face and closes to 150 px.
- **Search** interrupted by LOS abandons its sweep point and holds facing.

---

## 9. Presentation layer

- Procedural human sprite: ground shadow, two legs on a walk phase driven by distance travelled, two arms wrapping the weapon, torso ellipse 8.4 × 11.2 px (wider across the shoulders than front-to-back), head circle r 5.7 offset 2.2 px forward, hard dark outline on torso and head.
- Death and subdual states are visually distinct: corpses flattened at a random roll in a desaturated blood palette, weapon gone; subdued guards the same sprawl in cold blue. Reading a room's history at a glance is load-bearing for the record economy.
- Player concealment comes from walls only. The visibility polygon is 400 rays at 430 px, filled with `even-odd` against the screen rect. Guards without LOS are **not drawn at all**, not dimmed.
- Awareness bar 26 × 4 px above each visible guard, plus the state glyph. Colour: cyan below 30, amber 30–65, red above.
- HUD record strip: newest on the right, tier as dots, white border on the selected fuel, charge bar beneath it, spent husks as empty outlines.
- Debug overlay (toggleable): state name, awareness value, LKP marker and a line to it.

---

## 10. Known limitations — inherited, not bugs to fix silently

### 10.1 Steering is not pathfinding
Guards move straight at their target and slide along walls, with an unstick nudge (±1.1 rad) after 0.45 s of insufficient progress. On simple floors this is fine. On any level with real maze structure they will grind into corners.

The grid format already gives you a nav grid for free. **Report this when it first bites; do not build A\* unprompted.**

### 10.2 Aim rotation is free under dilation
You can enter dilation, rotate 180° at no cost, and leave. Preserved deliberately. The candidate fix is charging tolerance per radian turned rather than per second held, which changes the feel substantially and is a design decision, not a port task.

### 10.3 Search is random, not semantic
Guards sweep uniformly random points 40–150 px from the LKP. Real stealth AI searches plausible hiding places. The intended fix is a new level glyph marking concealment spots, which the text format can absorb trivially. Out of scope here.

### 10.4 Records have no readable content
A record is a name and a tier. The intent is that it is a readable document in clinical register, where degradation blacks out the load-bearing clause so the player can see exactly what was spent. This is the highest-value gap for the target audience. Out of scope; do not invent a document system.

### 10.5 The feel constants are browser-tuned
See §0. Report, do not retune.

---

## 11. Milestones and acceptance

| # | Milestone | Done when |
|---|---|---|
| 0 | Determinism spine | `sim/` skeleton with fixed tick, `DetRng`, `state_hash()`, headless runner, replay test green on one recorded replay |
| 1 | Level pipeline | Text parser + serialiser round-trip test green, wall merge test green (222→18), level renders |
| 2 | Movement and geometry | Player moves and slides, sneak works, visibility polygon renders, fog hides guards without LOS |
| 3 | Combat and feel | Bullets, one-hit kills both ways, full §7.3 feel budget, §7.4 audio including the master lowpass |
| 4 | Time authority and parasite | All four modes, `TimeAuthority` priority resolution, burn ladder test green, HUD record strip |
| 5 | Stealth AI | All eight states, §8.2 tuning, detection-curve test green within 10%, gunshot/body/callout propagation tests green, §8.7 regression covered by a test |
| 6 | Editor | In-editor grid painting with tools, route authoring, text import/export, `.txt` save/load |

Each milestone ends with: headless tests green, a one-paragraph report of anything that felt different from the prototype, and an explicit list of any constant you were tempted to change.

---

## 12. Reporting contract

At every milestone, state plainly:
1. Which tests pass and which fail, by name.
2. Any constant that felt wrong, with the value you would have chosen — **and confirmation you did not change it.**
3. Anything in this spec that was ambiguous or internally contradictory. Spec bugs are expected; silently resolving them is not.
4. Anything you added that this spec did not ask for.

Reference behaviour is `cognitohazard_combat_testbed.html`. When this document and the prototype disagree, the prototype is correct about *feel* and this document is correct about *architecture*. Report the conflict either way.
