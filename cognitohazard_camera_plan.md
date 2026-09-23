# Scrolling camera / larger levels — implementation plan

Status: **BUILT.** Phases 1-8 are implemented and under test. One deliberate
departure, recorded in §4 below: the camera is a draw transform, not a
`Camera2D` + `CanvasLayer` split, so no scene change was needed after all.

Measured on `levels/relay_nine.txt` (96x56 cells, 1920x1120 px, 59 wall rects):
zoom settles at the 0.600 floor, the level scrolls, the camera never leaves
bounds over a full sweep of the level, and a sim tick plus a 400-ray visibility
polygon costs **0.282 ms/frame** — under 2% of a 60 Hz budget. A run recorded
on it verifies against the sim.

What is left undone is listed at the end under **Not done**.

---

## 0. The one rule

The camera is **presentation**. It never enters `sim/`, never reaches an
`InputFrame`, and never touches the state hash. A replay recorded on one
machine must verify on a machine with a different window size, a different
zoom, and the camera parked somewhere else entirely.

Corollary: **level SIZE is not presentation.** It changes `Level.Walls`, which
changes everything downstream. Phase 1 below is a sim change with a golden
re-bake; every phase after it is a `game/` change with none.

---

## 1. What is true today

| Thing | Where | Value |
|---|---|---|
| Grid | `sim/Level.cs:39` | `const GW = 48`, `const GH = 28`, `CellPx = 20` |
| World size | derived | 960 x 560 px — exactly one screen |
| Design resolution | `project.godot` | 960 x 620 (`FIELD_H` 560 + `HUD_H` 60) |
| Stretch | `project.godot` | `canvas_items` / `keep` |
| Drawing | `game/main.gd` | ONE `_draw()` on the `Main` Node2D draws world *and* HUD |
| Mouse | `game/main.gd` | `get_global_mouse_position()`, already design-space |
| Vision | `game/main.gd:15` | 400 rays x 430 px, vs **every** wall rect, every sim tick |
| Walls | `sim/Level.cs` | greedy-merged to ~20 rects on the reference level |

The world is exactly one screen because `GW * CellPx == 960` and
`GH * CellPx == 560`. That coincidence is load-bearing in more places than it
looks, which is the whole difficulty.

`SimBridge` already exposes `GridWidthPx` / `GridHeightPx`. Nothing in `game/`
reads them yet — everything reads `FIELD_W` / `FIELD_H`. That is the single
most useful fact in this document: the seam already exists, it is just unused.

---

## 2. Do zoom first, scroll second

**Recommendation: ship zoom-to-fit before building a scrolling camera.**

For levels up to roughly 2x area, `Camera2D.zoom` alone solves the problem: the
whole floor stays on screen, no culling work, no offscreen-guard fairness
problem, no change to how the stealth game reads. It is perhaps a day of work
and it is a strict subset of the scrolling work (you need the camera node and
the HUD split either way).

Where zoom runs out: actors are drawn at ~22 px across. Below about **0.6 zoom**
they stop reading as facing-directional shapes, and the awareness bar (26 px)
and the new armour bar (26 x 3 px) become mush. 0.6 zoom on a 960x560 viewport
covers 1600x933 of world — call that the **zoom ceiling: ~3x the current area**.

So:

- Levels up to ~1600x930 → zoom. Phases 1, 2, 3 only.
- Levels beyond that → scroll. All phases.

---

## 3. Phase 1 — variable level size (sim, breaks the hash)

Make `Level` carry its own dimensions instead of two compile-time constants.

**Change:** `GW` / `GH` stay as the *defaults for a new level*. Add instance
fields `W` and `H`, and infer them in `Level.FromText` from the grid text —
width is the longest row, height is the row count. `Grid` becomes
`new char[W * H]`. Every `GH` / `GW` inside `Level.cs` (31 sites) becomes
`H` / `W`; the bounds clamps (`if (row >= GH) continue;`) become the instance
check.

**Why infer rather than a `size:` header line:** an existing 48x28 file infers
48x28 and parses byte-identically, so every recorded replay still verifies and
the goldens do not move for unchanged levels. A header line would have to be
absent-tolerant anyway, and then the inference is still the real code path.
Optionally write `size: WxH` on save as a checksum the loader *validates*
rather than trusts.

**Blast radius:**

- `sim/Level.cs` — the real work.
- `game/SimBridge.cs` — 8 sites; `GridWidthPx`/`GridHeightPx` already exist and
  become instance-derived, which is where `game/` will read size from.
- `tests/` — ~90 sites, but almost all are the `Room()` fixture helpers that
  emit a 48x28 grid. They keep working unchanged. Worth leaving them at 48x28
  so the fixtures stay comparable, and adding *new* differently-sized fixtures
  rather than resizing the old ones.
- `game/editor.gd` — needs a resize affordance; see Phase 6.

**Guard against silent breakage:** cap `W * H`. A malformed file claiming
4000x4000 must clamp, not allocate — `Level.FromText` is a **total** parser
(`tests/Program.cs` asserts it never throws on garbage) and that contract must
survive. Suggest `MaxCells = 48 * 28 * 16` (~21k cells, 16x today).

**Test gates:**

- Round-trip lossless at 48x28, at a wide level, at a tall one, at 1x1.
- An existing 48x28 file parses to a byte-identical `ToText()`.
- Goldens **unchanged** — this phase must not move the hash for the reference
  level. If it does, the inference is wrong somewhere.
- Garbage/truncated/oversized input still yields a playable level.
- `tests/Program.cs:AssertPlayable` extended with `W > 0 && H > 0`.

---

## 4. Phase 2 — the camera (game, structural)

**Recommended a real `Camera2D`. Built as a manual draw transform instead.**

The reason for the reversal: this plan assumed the HUD split was cheap and the
mouse hazard was not. Both turned out the other way round. `main.gd` already
ends its world drawing with a `draw_set_transform` reset before the HUD — the
two passes were *already* separated, so the camera is a transform on the first
pass and nothing at all on the second, with no scene change, no reparenting and
no 600-line move. The mouse hazard is real but small: three world-space call
sites, all now behind one named `world_mouse()`, versus the panels which stay
screen-space and say so. The original argument follows, kept because it is the
right default for a project that has not already split its passes.

**Original recommendation: a real `Camera2D`, not a manual draw offset.**

`main.gd` already does manual transforms (`draw_set_transform(_shake + pos, …)`),
so folding a camera offset into `_shake` looks tempting and is about ten lines.
Do not. The reason is `get_global_mouse_position()`: with a `Camera2D` on the
world node it returns **world** coordinates for free, and every existing mouse
call site keeps working. With a manual offset, every mouse site has to remember
to subtract it, and CLAUDE.md already records that this exact class of bug
(window pixels vs design pixels) has bitten this project once. Do not
reintroduce it one layer deeper.

**This would need a scene change, which per CLAUDE.md is yours to make in the
editor, not mine — and is the cost that decided against it:**

```
Main (Node2D, main.gd)
├── World (Node2D)          <- all world drawing moves here
│   └── Camera2D
└── Hud (CanvasLayer)       <- HUD, menus, loot panel, editor chrome
    └── HudDraw (Node2D)
```

`main.gd`'s single `_draw()` splits in two: `World._draw()` (walls, vision
polygon, decals, bullets, guards, player, loot prompt, reticle) and
`HudDraw._draw()` (the bar at `FIELD_H`, notices, end card, playback bar). The
existing `_draw_*` helpers move essentially verbatim; the split is by call site,
not by rewrite.

**Camera behaviour:**

```
target = player_pos
       + clamp(cursor_pos - player_pos, LOOK_AHEAD_MAX)   # show what you aim at
pos    = lerp(pos, target, 1 - exp(-delta * FOLLOW_RATE)) # exponential, frame-rate safe
pos    = clamp_to_level_bounds(pos, level_size, view_size)
```

- `LOOK_AHEAD_MAX` ~ 90 px. Capped so the camera cannot be used to scout: the
  cursor can leave the screen, the camera must not follow it there.
- `FOLLOW_RATE` ~ 8.0. Same `1 - exp(-delta * k)` form already used for
  `_visual_scale` at `main.gd:603`, so the feel matches and it is frame-rate
  independent.
- Clamp to level bounds so there is never letterbox void at an edge. When the
  level is *smaller* than the view on an axis, centre on that axis instead of
  clamping — otherwise a narrow level jams against one side.
- **Dilation:** the camera follows the player's *position*, which already moves
  on the player clock, so it slows down naturally under `WORLD_SLOW`. Do not
  scale the camera's own lerp by the sim clock as well, or it double-dips and
  feels like treacle at exactly the moment the player needs to read the screen.

**Make it a pure function.** `camera_for(player, cursor, level_size, view_size)
-> Vector2`, no node state. That is what makes Phase 2 testable at all (see
gates below) — a camera baked into `_process` is untestable in this harness.

**Test gates (`tests/editor_check.gd`, which already drives `game/` headlessly):**

- Clamps at all four edges — camera rect never leaves level bounds.
- A level smaller than the viewport centres rather than clamping.
- Look-ahead is capped: a cursor 5000 px away moves the camera no further than
  `LOOK_AHEAD_MAX`.
- The camera is a pure function of its arguments — same inputs, same output.
- **The sim hash is unaffected by camera position.** Step a world twice with
  identical inputs and different camera state; assert identical hashes. This is
  the assertion that keeps the one rule in §0 true.

---

## 5. Phase 3 — HUD, screens and the design-space constants

`FIELD_W` / `FIELD_H` appear 82 times across `game/*.gd`. They split cleanly:

- **Stay screen-space** (no change): `loadout_menu.gd`, `equipment_screen.gd`,
  `inventory_screen.gd`, `start_screen.gd`, `loot_panel.gd`, the HUD bar, the
  end card, the playback bar. These are all *screens*, correctly sized to the
  viewport. They move to the `CanvasLayer` and keep reading 960x620.
- **Become world-space** (must change): anything that positions against the
  field — the loot prompt ring, the reticle, the vision polygon, decals, shake.
  These stop reading `FIELD_*` and start reading `bridge.GridWidthPx` /
  `GridHeightPx`, or nothing at all.

Mechanical way to find them: after the scene split, anything still referencing
`FIELD_*` inside `World._draw()` is a bug. Grep is the gate.

**The loot panel is a screen** (fixed at `PANEL_X = FIELD_W - PANEL_W - 16`), so
it goes on the `CanvasLayer` unchanged. But note `loot_panel.gd` uses
`get_local_mouse_position()` for row hit-testing and the identify dwell — on a
`CanvasLayer` that stays screen-space and keeps working. Do **not** move it to
`World`, or the dwell starts tracking the wrong rows.

---

## 6. Phase 4 — performance

Two things get worse superlinearly with level area. Both need doing before
scroll ships; neither is needed for zoom-only.

**6a. The visibility polygon.** `main.gd:15` — 400 rays against every wall rect,
every sim tick. At ~20 rects today that is 8,000 segment tests per tick. A 4x
level with ~200 rects is 80,000 per tick at 60 Hz, which will be the frame
budget.

Fix, in order of value:

1. **Broad-phase on the cell grid.** Walls are already cell-aligned, so build a
   `cell -> rect index` lookup once at load and walk each ray cell-by-cell
   (DDA), testing only rects in cells the ray actually enters. Exact, not
   approximate, and it makes cost proportional to ray length rather than to
   level size. This alone is sufficient.
2. **Cull rays by view.** Vision radius is 430 px; anything beyond the camera
   rect is invisible anyway. Clip the polygon to the view rect + margin.
3. Do not drop the ray count — 400 is a look, and lowering it is visible.

**6b. Draw culling.** Today every wall rect, decal and guard is drawn every
frame. Add a `view_rect` test in `World._draw()`. Decals especially: `DecalCap`
is 150 *globally*, which on a large level means a floor's worth of history
spread thin. Cull by view first, then consider raising the cap.

**Test gate:** the culled draw set must equal the unculled set intersected with
the view rect — assert set equality on a fixture, not a screenshot.

---

## 7. Phase 5 — the editor

`game/editor.gd` paints cells from the mouse position. Once the world scrolls it
needs:

- The same camera (it should share `camera_for`, not grow a second one).
- **Pan**: middle-drag, and arrow keys for keyboard-only work.
- **Zoom**: mouse wheel, plus a zoom-to-fit key so the whole level is reachable.
- **Resize**: `N` (new) should prompt for size; add a grow/shrink affordance.
  Shrinking must refuse to discard occupied cells silently — warn, or clamp to
  the occupied bounds.
- Its own hit-testing switches to world coordinates. With a `Camera2D` on the
  parent this is automatic; that is the payoff from §4.

`tests/editor_check.gd` already drives the editor headlessly and is the natural
home for the resize assertions (round-trip after a resize, markers preserved,
refuse-to-discard).

---

## 8. Phase 6 — what a scrolling screen does to the *game*

This is the part that is a design question rather than an engineering one, and
it should be decided before the work, not after.

Right now the player sees the entire floor. Every guard, every patrol, the exit,
all of it. The stealth game is **planning under full information**. A scrolling
camera deletes that, and replaces it with something meaningfully different.

Three things need an answer:

1. **Offscreen guards.** Being shot from off-screen is not tension, it is
   noise. Options: edge markers for guards in `Hunt`/`Engage`, a wider vision
   radius, or a minimap. Recommend **edge markers gated on guard state** —
   nothing for a patrolling guard you have not noticed, a marker for one that
   is actively hunting you. That preserves the stealth information asymmetry
   while removing the unfair deaths.
2. **Dilation.** Under `WORLD_SLOW` the player is watching bullets cross the
   room — that is the entire mechanic (`sim/Projectiles.cs` is explicit about
   it). At the retuned muzzle velocities a round crosses 960 px in 381 ms at
   normal speed. If the camera is tight, rounds now enter and leave the view.
   Recommend: **zoom out slightly while dilating**. It reads as the perceptual
   shift the mechanic is fiction-ing about, and it solves the problem.
3. **The alarm and callouts** have world-space radii (`CalloutRange` 300 px,
   `GunRange` 640 px) that were tuned against a one-screen floor. On a level
   four times the area they will feel local in a way they never did. They are
   spec §8.3 constants, so per CLAUDE.md they do not get changed silently —
   flag them for retuning as an explicit decision once a large level exists to
   play.

---

## 9. Suggested order

| # | Phase | Touches | Hash moves? | Gate |
|---|---|---|---|---|
| 1 | Variable level size | `sim/Level.cs` | **no** (that is the test) | sim harness + round-trip |
| 2 | Scene split + Camera2D + zoom-to-fit | scene (**yours**), `main.gd` | no | smoke + `editor_check` |
| 3 | HUD/world constant split | `game/*.gd` | no | grep gate + smoke |
| 4 | **Ship zoom-only here.** Levels to ~1600x930 work. | — | — | play it |
| 5 | Camera follow + clamp + look-ahead | `main.gd` | no | `camera_for` unit tests |
| 6 | Vision broad-phase + draw culling | `SimBridge.cs`, `main.gd` | no | set-equality + frame time |
| 7 | Editor pan/zoom/resize | `editor.gd` | no | `editor_check` |
| 8 | Offscreen markers, dilation zoom | `main.gd` | no | play it |

Phases 1–4 are the cheap 80%. Stop there and see whether the levels you
actually want to build need more.

---

## 10. Not done

- **Vision broad-phase by DDA (§6a.1).** Built the cheaper half instead: wall
  rects are pre-filtered against the vision radius once per frame, then the 400
  rays cast against the survivors. `editor_check.gd` asserts the filtered
  polygon is identical to the unfiltered one, ray for ray. At 0.282 ms/frame on
  a 4x level the DDA is not yet worth its bug surface; revisit past ~300 rects.
- **`ClearLine` broad-phase.** Untouched, and in the sim tick. It is
  O(guards x rects) — 8 x 59 on the large level, trivial. It will matter before
  the vision polygon does if guard counts rise.
- **Minimap.** Edge markers were the cheaper answer to the off-screen problem
  and may be enough.
- **Retuning `CalloutRange` / `GunRange` for large floors (§8.3).** Still a
  spec §8.3 decision, still flagged, still not taken — now with a large level
  to actually play it on.

## 11. Risks

- **Silent, worst:** a mouse site that keeps reading screen coordinates after
  the split. Aim, the loot panel dwell, the editor brush, the reticle. Symptom
  is "it works until you scroll". Mitigation: `Camera2D` (so the default is
  correct), then grep every `mouse_position` call and label it screen or world.
- **Silent:** `FIELD_W`/`FIELD_H` surviving inside world drawing. Grep gate.
- **Loud, fine:** the golden hashes, if Phase 1 gets the inference wrong. That
  is the drift detector doing its job.
- **Not silent but easy to miss:** `levels/substation_4.txt` is 48x28 and
  hand-authored. Resizing it invalidates the goldens *and* the level-shape
  assertions in `tests/Program.cs`. Build large levels as **new files** and
  leave the reference level at 48x28 as the fixture.
