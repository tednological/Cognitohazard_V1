# Lighting — Plan

Goal: make **light a stealth axis**. Today the only thing that hides you is a
wall (spec §9: "Player concealment comes from walls only"). After this, a dark
corner hides you too, a lit corridor exposes you, and the player can change
which is which: shoot out a lamp, throw a switch, shut a door on a lit room,
turn a torch on or off. Guards can use light against you as well.

Status: **PLAN ONLY — nothing built.** Every number marked *(proposal)* is a
starting value to tune in play, not a spec constant. This is a deliberate
deviation from spec §9, and CLAUDE.md must record it when it lands.

Depends on: **glass and doors** (built — `=` and `+`, `SimWorld.Panels`,
`Solid`/`Opaque`). Light travels exactly where sight travels, so it reuses the
opaque set those introduced: glass passes light, a shut door stops it.
Sits alongside **Guard AI v2** (`Guard_AI.md`). The two meet in §8 below.

---

## 0. Decisions to take (recommendations first)

| Question | Recommendation | Why |
|---|---|---|
| Where is the light map computed? | **In `sim/`, as an integer cell grid.** Presentation draws what the sim computed. | A guard must see you by the SAME light you see yourself in. A Godot `PointLight2D` shading that disagrees with the sim's number is a lie the player will catch in the first dark corner. |
| Godot 2D lights at all? | **Cosmetic only.** Additive glow sprites at lamps, never the shading that means something. | The camera is a draw transform, not a Camera2D (CLAUDE.md "Levels and the camera"), and walls are merged rects drawn in `_draw`, not nodes. `LightOccluder2D` would mean a node per wall, for a result the sim already knows. |
| What does darkness do to detection? | **It scales the sight stimulus `q` (spec §8.2) by a light factor with a FLOOR**, and a guard always sees you when you are very close. | Darkness must slow detection, never make you invisible. Otherwise a dark level has no stealth game at all, only a walk. |
| Does it change existing levels? | **No.** A level with no `ambient:` line and no lamps is fully lit, and the maths reduces to today's exactly. | The same rule glass and doors kept: Substation 4's golden hashes and the §8.6 detection curve do not move. |
| Is darkness symmetric? | **Yes.** A guard standing in the dark is harder for YOU to see as well: he is drawn dimmed, then as an outline, then not at all. | Otherwise darkness is a pure player buff and the dark rooms are where you should always walk. Symmetry makes them a gamble. |
| The flashlight | **A real cone you toggle**, replacing today's flat `DetectionMul`. It lights what you point it at, so you see further, and it lights YOU for every guard facing the beam. | The rail slot promised "vision radius and enemy detection of you" (rpg plan §5). A cone makes that trade spatial instead of a number. |
| How does the torch toggle reach the sim? | **A new InputFrame field, `Toggles` (byte, bit 0 = torch)**, written as a `tN` token. It is an 11th `SimBridge.Step` argument. | Flags bit 1 (the old sneak bit) is free, but replays without an `m` token still READ it as stealth. Reusing it would flip the torch in every old stealth frame. |
| Shooting out lights | **Lamps are breakables, and they break through the glass machinery.** Same impact path, the same shatter, a smaller noise radius. | We already have "a round that touches this breaks it, flies on, and is heard" (`Projectiles` `HitKind.Glass`, `SimWorld.BreakGlass`). A lamp is glass with a bulb in it. |

---

## 1. What exists today (the baseline)

- **Vision polygon**: `SimBridge.GetVisionPolygon`, 400 rays at 430 px (plus
  the flashlight's `VisionRadiusBonus`), cast against `SimWorld.Opaque`: walls
  and shut doors, not glass. game/ fills it with `C_FLOOR_LIT` over a darker
  `C_FLOOR`. That is not light. It is line of sight drawn as light.
- **Guard concealment**: `GetGuards` reports `visible = ClearLine(Opaque)`, and
  a guard with no line to the player is not drawn at all.
- **Sight stimulus** (`Perception.SeesPoint`, spec §8.2):
  `q = clamp(1 - offset/half, 0.30, 1) * clamp(1 - dist/range, 0.15, 1)`,
  then scaled by movement, stance and alert multipliers. The §8.6 curve
  (`Systems.DetectionCurve`) is an acceptance test measured against it.
- **The flashlight** (rail attachment): `+150 px` vision radius and
  `DetectionMul` (`+38/256`) on EVERY guard's sight stimulus, wherever the
  player is pointing. A lit torch is a beacon in all directions at once.
- **Glass and doors**: `SimWorld.Panels` with `Solid` (movement) and `Opaque`
  (sight and rounds) rebuilt on change. Glass is transparent, a shut door is
  opaque. A panel changes state only through a tick (`DoorPick`, a round, a
  guard walking into a door).
- **Muzzle flash** is presentation only (`_flash`), and so is the player's
  gunshot. What guards hear is the weapon's `GunshotRadius`, with no line of
  sight involved.

---

## 2. The model

### 2.1 The light map

One byte-per-cell grid over the level (20 px cells), `Light[c, r]` in Q8,
0..256:

```
Light = clamp( Ambient + Σ lamps + Σ transient pulses , 0, 256 )
```

- **Ambient**: the level's base light, from a new header line
  `ambient: <0..100>` (percent). **Missing means 100**, which is fully lit and
  exactly today's game.
- **Lamps**: a glyph, `L`, is a ceiling lamp over that cell (floor for every
  other purpose). Each lamp lights a disc of `LampRadius` *(proposal: 7 cells,
  140 px)* with a linear falloff to zero, **shadowcast on the cell grid
  against the opaque set**: walls and shut doors block it, glass does not.
  A lit office with a glass front spills light into the corridor; the same
  office behind a shut door keeps its light to itself.
- **Transient pulses** (§4): muzzle flashes, and any other short burst, added
  for a few ticks.

Shadowcasting is the integer, deterministic, symmetric recursive kind on the
cell grid, run once per lamp. Cost is proportional to the lit area, not to the
wall count, so Terminal Twelve's 284 rects do not matter.

**Derived, not stored.** The map is a pure function of hashed state (lamp
states, panel states, ambient, the pulse list) and is rebuilt when one of those
changes, exactly as `Solid`/`Opaque` are. Only its inputs are hashed.

### 2.2 When it is rebuilt

Event-driven, never per tick:

| Event | Rebuild |
|---|---|
| Level load | everything |
| A door opens or shuts | only the lamps whose radius covers that door |
| A lamp breaks, or a switch flips (§5) | that lamp or that circuit |
| A pulse starts or ends | that pulse's own disc (pulses are small) |

`SimWorld.RebuildBlockers` already runs on exactly the door events, so lighting
hooks the same place.

### 2.3 The light at a POINT

Sim code reads light at a fixed-point position through **bilinear
interpolation of the four nearest cell centres**, in integers. A guard standing
on a light-pool boundary should not flicker between two values from one pixel
of movement. That is a fairness bug, not polish.

---

## 3. Light and perception

### 3.1 Guards seeing the player

`SeesPoint` keeps its geometry. The QUALITY gains one factor, the light **at
the player**:

```
lightQ8  = LightAt(player)                           // 0..256
visQ8    = VisFloor + (256 - VisFloor) * lightQ8 / 256
q'       = q * visQ8 / 256
```

- `VisFloor` *(proposal: 72, i.e. 0.28)*: pitch dark still leaves you at
  ~28% visibility, so darkness slows detection by a factor of three or four
  and never stops it.
- **Close-range override**: inside `DarkSeeRange` *(proposal: 60 px)* a guard
  sees you at full quality whatever the light. You cannot hide in the dark in
  front of someone's face.
- **Fully lit (256) gives visQ8 = 256**, so `q' = q` exactly: lit levels and
  the §8.6 curve are untouched, and a test pins that (§9, L1).
- Applied BEFORE the movement, stance and alert multipliers, so creeping
  through a dark room compounds with stealth tier the way a player expects.

### 3.2 Guards seeing BODIES

The same factor at the body, with its own floor *(proposal: 40)*: a body in a
dark corner is found late. Hiding bodies in the dark is a real reason to carry
them there, and to kill a lamp before a takedown.

### 3.3 The player seeing guards

Presentation, but it follows the same number, so it is symmetric by
construction:

| Light at the guard | Drawn as |
|---|---|
| ≥ `SeeClear` *(proposal: 0.45)* | as today |
| between | dimmed toward the floor colour, awareness bar still shown |
| ≤ `SeeOutline` *(proposal: 0.15)* | a faint outline only, no bar, no plate silhouette |
| in the dark AND beyond `DarkSeeRange` | not drawn, as if out of line of sight |

A guard in your torch beam is lit and drawn normally (§6). Edge markers
(Hunt/Engage, off-screen) are unchanged. They are knowledge, not sight.

---

## 4. Light as a tell: muzzle flash

Firing lights the shooter. Each shot adds a pulse centred on the muzzle
*(proposal: radius 5 cells, intensity 200, 6 ticks)*. In a lit room nothing
changes. In a dark one the pulse does two things:

1. **You are lit for a few ticks.** Anyone facing you sees you at up to full
   quality, so shooting from the dark gives the dark away.
2. **The flash itself is seen.** A guard with a clear line to a muzzle within
   `FlashSeenRange` *(proposal: 700 px, beyond sight range)* gets an LKP at
   the flash, even when he could not see YOU there. The same Notice path a
   gunshot uses, gated on line of sight rather than on sound.

Subsonic ammunition quiets the report. A **flash hider** (a future muzzle
attachment, `SimB` = pulse intensity) would quiet the light. The two stay
separate axes, which is why the pulse is its own number.

Guard shots pulse too. A firefight in a dark room is lit in stutters, which is
also most of what makes it look good.

---

## 5. Lamps, switches and the player's hands

### 5.1 Lamps

- Glyph `L`. Floor for walking, sight and rounds. It is a point object with a
  hit circle *(proposal: 5 px)* for rounds.
- **Breakable through the glass path.** `Projectiles` already reports a Glass
  impact and flies on. A lamp is added to the same breakable list, and
  `BreakGlass` generalises to `Shatter(breakable)`, with its own noise radius
  *(proposal: 220 px, smaller than a window's 340)*. The shatter effect and
  sound are the glass ones, pitched up.
- A broken lamp never relights, as a broken pane never mends.

### 5.2 Switches (a later phase)

- Glyph `S` on a wall-adjacent floor cell. **G toggles it**, through the same
  `DoorPick`-style recorded intent. Recommend generalising `DoorPick` to
  `UsePick`, which names any panel, switch or later interactable by index. The
  `u` token is already spelled "use".
- **Circuits are derived, not authored**: a switch controls every lamp in its
  ROOM, meaning the flood-filled region bounded by walls, glass and door
  cells. No wiring syntax to author or get wrong, and a room built in the
  editor is wired the moment it is saved.
- Guards notice. A room going dark that was lit is a stimulus, like hearing a
  door: guards inside it or with a line into it gain awareness at the switch
  and may come and turn it back on (Guard AI v2 Curious/Investigate task,
  "restore light").

---

## 6. The torch (flashlight rework)

Today the torch is `+150 px` of vision and a flat `+15%` detection everywhere.
Reworked:

- **Toggle** with a key *(proposal: `T`; free today)* through
  `InputFrame.Toggles` bit 0. Off by default. Only a weapon with the
  flashlight fitted can turn it on.
- **On**, it is a CONE: half-angle *(proposal: 0.35 rad)*, reach
  *(proposal: 300 px)*, on the muzzle line. It is computed analytically per
  query (angle and distance), not written into the grid, because it moves
  every tick.
  - What is **in the cone** counts as lit (at least `TorchLight`, *proposal*
    200) for **the player's** view and for **perception of guards** (§3.3).
  - **The player counts as lit** to any guard who is himself inside the cone
    or has the torch lens in view (the player facing within 90° of him). A torch
    pointed at a guard is the brightest thing in his world. A torch pointed
    away from him is only a glow.
- The vision polygon's radius bonus stays, but only while the torch is ON.
- `DetectionMul` as a global multiplier is **retired**. The cone replaces it.
  This moves hashes on any loadout with the torch fitted, and those replays
  diverge. Record it.

### Guard torches

Guards gain a torch in **Hunting/Search** (and, on levels whose ambient is
under `GuardTorchAmbient`, *proposal 40%*, always). The same cone, on their
facing. It does two things:

- A player caught in a guard's beam is lit (§3.1 picks it up for free).
- The player SEES the beams: presentation draws them as soft cones even when
  the guard himself is out of line of sight, clipped by the opaque set. A
  torch sweeping a far wall tells you a search is coming before the searcher
  does. This is knowledge the player earns by looking, and it pairs with
  Guard AI v2's rule of no edge markers for Hunting pairs.

---

## 7. Presentation

### 7.1 The darkness layer

- A child `Node2D` (built in code, like the editor and screens are, so no
  scene change) with a `CanvasItemMaterial` in **multiply** blend. It draws one
  texture, the light map as an `Image` of W×H luminance, uploaded only when the
  sim reports the map changed, and stretched over the level with **linear
  filtering**. Twenty-pixel cells then read as smooth pools, not a chessboard.
- It sits above the floor and below actors in the world pass. Actors are
  dimmed separately by §3.3, so a guard in the dark is not double-darkened.
- The existing vision polygon stays and still means LINE OF SIGHT. Light and
  sight are different questions: a lit room behind a wall is lit but not seen,
  and a dark room in front of you is seen but not lit.
- **Readability floor**: the player's own screen never goes below
  `DisplayFloor` *(proposal: 0.18)* within the vision polygon, even at light 0.
  The floor has to stay readable where the player is looking, or the level
  cannot be played. This is presentation only. The sim still uses 0.

### 7.2 Lamps and glow

Lamps draw as a small fitting, with an additive radial glow *(cosmetic,
never read by anything)*. A broken lamp draws as a dead fitting with glass
shards on the floor below (the glass `_shatter` effect).

### 7.3 The HUD light meter

A new HUD element `light` (`hud_layout.gd` `ELEMENTS` + `DEFAULTS` +
`_hud_light` in main.gd, with the harness rules: a grid-aligned default, a
declared size that matches what it paints, no overlap). It shows how visible
you are RIGHT NOW: the sim's `visQ8` at the player, as a jewel that goes from
dim to bright. This is the one readout lighting cannot do without. Players have
to know when they are hidden. The snapshot carries `PlayerVisQ8`, so the meter
cannot disagree with perception.

---

## 8. With Guard AI v2

- **Sweep nodes** (`Guard_AI.md` §6.3) score DARK cells higher: a searcher
  checks shadows first, which is where a player would hide.
- **Hunting** guards carry torches (§6). A compromised level becomes a level
  of moving lights, which the player can read.
- **A broken lamp, or a room that went dark,** is evidence: Curious ("go
  look"), not Combat.
- **Radio calls** are unaffected by light.
- **Snap sight** (`Guard_AI.md` §2.1, ~150 px in the cone snaps to Combat)
  must respect light, or it undoes §3.1. Recommend the snap range scales with
  `visQ8` down to `DarkSeeRange`.

---

## 9. Data model changes

### 9.1 `sim/`

| Where | Change |
|---|---|
| `Level.cs` | `ambient:` header (default 100, clamped 0..100, round-trips). `L` glyph → `Level.Lamps` (row-major, like panels). `S` glyph → `Level.Switches` (later phase). Header comment gains `L lamp` (`HeaderComment`). |
| `sim/LightMap.cs` (new) | Q8 cell grid; per-lamp integer shadowcast against the opaque CELL mask; `LightAt(x, y)` bilinear; incremental rebuild by lamp. Derived, NOT hashed. |
| `SimWorld.cs` | `Lamps` runtime list (`Lit`, `Broken`), **hashed only when the level has any** (the rule panels follow). Pulses list (hashed when non-empty). Rebuild hooks in `RebuildBlockers`, `Shatter`, the switch step. `PlayerVisQ8` computed once per tick. |
| `Projectiles.cs` | The glass list generalises to breakables (panes and lamps), same index discipline. |
| `Perception.cs` | `SeesPoint` gains a `visQ8` argument (256 = today). `DarkSeeRange` override. |
| `InputFrame.cs` / `Replay.cs` | `Toggles` byte (bit 0 torch), `tN` token, omitted at 0. |
| `Actor.cs` | `TorchOn` (player and guards), hashed. |
| `Loadout.cs` | The flashlight attachment keeps its id and slot. `DetectionBonus` goes, and `TorchHalf`/`TorchReach` come in. Every `With*` carries them (`Exhaustive.LoadoutMutators`). |
| `EventLog.cs` | Append `LampBroken`, `LightsToggled`, `TorchToggled`. |
| `SimSnapshot.cs` | `LightDirty` flag + the grid for the bridge; `PlayerVisQ8`; per-guard `LitQ8`; per-lamp view. |
| `Tuning.cs` | A "lighting" block, every constant marked NEW NUMBERS, to be tuned. |

### 9.2 `game/`

- `SimBridge`: `GetLightMap()` (byte[] W×H, only when dirty), `LightDirty`,
  `PlayerVisQ8`, guard stride +1 (`LitQ8`), `GetLamps()`, torch cones for
  drawing. `Step` gains its 11th argument (`toggles`). The arity lint in
  `editor_check.gd` reads the count, so only the call sites change.
- `main.gd`: the darkness layer (§7.1), guard dimming (§3.3), torch cones,
  lamp drawing, pulses, `_hud_light`, the `T` key.
- `editor.gd`: a Lamp tool, an `ambient` control (shown in the header, changed
  with a key pair), and **V to preview the light map** in the editor through
  the same `LightMap` on the edit buffer, so a designer sees what the sim will
  compute. `EditorValidate` warns about a level whose objective or exit
  sits in pitch dark with no lamp (legal, but probably unintended).
- `audio.gd`: `LAMP` (glass, pitched up with a fizz), `SWITCH` (a clack),
  `TORCH` (a click).
- `missions.gd`: threat does not know about light. Darkness makes a floor
  EASIER to sneak and harder to fight. Leave threat alone until play says
  otherwise, and note it in the mission select line ("dark").

### 9.3 Not touched

Loot, records, stash, campaign, economy, the camera.

---

## 10. Phases and acceptance tests

Each phase lands green: `dotnet build`, the sim harness, every GDScript
harness, and **no golden move except where stated**.

### L0 — The light map
`LightMap.cs`, `ambient:`, `L` lamps, the shadowcast, incremental rebuild on
door changes.
- A level with no ambient line and no lamps is 256 everywhere, and a test pins
  it on every shipped level.
- A lamp lights its own cell at full, falls off linearly, and lights nothing
  behind a wall; through a pane it does, through a shut door it does not,
  through an open one it does.
- Incremental rebuild equals a from-scratch rebuild after every door toggle
  (fuzzed).
- `ambient:` round-trips; out-of-range values clamp; the parser stays total
  (`Robustness` feeds `L` and `ambient: 99999999999` through it).
- Determinism: same inputs, same map, bit for bit.

### L1 — Perception
`visQ8` in `SeesPoint`, `DarkSeeRange`, bodies.
- **§8.6 detection curve unchanged on a lit level** (the existing test must
  pass untouched).
- **Goldens unchanged** (Substation 4 is lit).
- In the dark, time-to-notice at a fixed distance is ≥ 3× the lit time, and
  inside `DarkSeeRange` it equals the lit time.
- A body in the dark is found later than the same body lit.

### L2 — Presentation
The darkness layer, guard dimming, lamps, the HUD meter, editor preview.
- `hud_layout` harness rules pass for `light`.
- The meter reads `PlayerVisQ8` and nothing else (asserted in
  `inventory_check.gd`-style static tests of the drawing inputs).
- The light image uploads only on `LightDirty` (counted, like `draws`).

### L3 — Breakables and pulses
Shooting out lamps, the muzzle flash.
- A round shatters a lamp, flies on, is heard at `LampNoiseRadius`, and the map
  loses exactly that lamp's contribution.
- A shot fired in the dark makes the shooter visible for the pulse's ticks,
  and a guard with a line to the flash gets an LKP at it.
- Fuzz: the light map always equals a from-scratch rebuild of the current
  state.

### L4 — The torch
`Toggles`, the cone, `DetectionMul` retired.
- The `tN` token round-trips and old replays parse with the torch off.
- A torch pointed at a guard in the dark lets him see you at lit quality; one
  pointed away does not.
- `Exhaustive.LoadoutMutators` still passes with the new fields.
- **Goldens move** only if the golden run carries a torch (it does not). Any
  replay with a torch fitted diverges, and that is recorded in CLAUDE.md.

### L5 — Guard torches
Hunting/Search guards carry torches; beams are drawn.
- A searching guard's beam lights the player; beams are clipped by the opaque
  set; a beam is drawn even when the guard is out of line of sight.

### L6 — Switches
`S`, derived circuits, `UsePick`.
- A switch controls exactly the lamps its room's flood fill reaches.
- Guards notice a room going dark and turn it back on.

### L7 — Levels and bug-finding
- `tools/levelkit.py` learns `L`/`S`/ambient and checks that no objective,
  exit or chest is pitch dark unless the level asks for it.
- **A dark level**: rework Vault Row (the doors level) with `ambient: 25` and
  lamps in the hall and offices. It is already the floor where you cannot see
  round corners, so darkness makes it a second, harder floor from the same
  geometry.
- **A night Meridian**: `ambient: 35`, the glass offices lit and the atrium
  dark. Lit rooms behind glass become the stage and the atrium the wings. The
  intended picture is guards silhouetted against the glass.
- `Fuzz` invariants: the map equals a rebuild; `visQ8` is in range; a lit
  level's perception equals the unlit code path.

---

## 11. Proposed tuning (all *(proposal)*)

| Constant | Value | Note |
|---|---|---|
| `LampRadius` | 7 cells (140 px) | linear falloff to 0 |
| `LampIntensity` | 256 | at the lamp's own cell |
| `VisFloor` | 72 / 256 (0.28) | darkness never makes you invisible |
| `BodyVisFloor` | 40 / 256 | bodies hide better than people |
| `DarkSeeRange` | 60 px | inside it, light does not matter |
| `FlashPulse` | r 5 cells, 200, 6 ticks | muzzle flash |
| `FlashSeenRange` | 700 px | a flash is seen further than a man |
| `LampNoiseRadius` | 220 px | a window is 340 |
| `LampHitRadius` | 5 px | |
| `TorchHalf` / `TorchReach` | 0.35 rad / 300 px | |
| `TorchLight` | 200 / 256 | light inside the beam |
| `GuardTorchAmbient` | 40% | at or below it, guards always carry torches |
| `DisplayFloor` | 0.18 | presentation only |
| `SeeClear` / `SeeOutline` | 0.45 / 0.15 | how the player sees guards |

---

## 12. Risks and open points

- **Dark is frustrating before it is tense.** Hence the floors: `VisFloor`
  in the sim and `DisplayFloor` on screen. Tune the second first. If players
  cannot read the floor they will not care what the guards can see.
- **The detection curve is an acceptance test.** Lighting must reduce to
  identity when lit, and L1 must prove it before any dark level ships.
- **Replays with a torch fitted diverge at L4.** Unavoidable once the global
  multiplier becomes a cone. Say so in CLAUDE.md in the same change.
- **Cost.** Shadowcasting is per lamp and per event. A door toggle near five
  lamps recasts five discs of ~150 cells: trivial. The worry is pulses in a
  firefight (a SAW at 13 rounds a second). Pulses do not shadowcast: they are
  an unoccluded disc with a single line-of-sight test to each query point.
  Cheap, and slightly wrong through thin walls, which the flash's short life
  hides.
- **Two sources of "lit" on screen.** The vision polygon (sight) and the
  darkness layer (light) must not read as the same thing. §7.1 keeps them
  visually distinct: the polygon brightens the floor slightly, the darkness
  multiplies everything. Play-test that a dark room in plain sight reads as
  "I can see it is dark" and not as "I cannot see it".
- **Glyph budget.** `L` and `S` join `C` and `X` as capitals. `*` is reserved
  for sweep nodes. Lower-case is guards. Nothing collides.
- **Kit revamp.** The torch is an attachment today. When the Kit revamp lands
  (CLAUDE.md), `TorchOn` stays on the Actor and the ability to turn it on
  comes from the Kit's fitted rail. There is no conflict, but sequence them.
