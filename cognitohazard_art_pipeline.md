# Cognitohazard — Art Pipeline (ChatGPT Astra × Blender)

PLAN. Nothing here is built yet. This document is the brief an Astra session
works from, and the contract its output has to meet before the game draws it.
Every number below is taken from the code as it stands (`game/main.gd`,
`game/stash_screen.gd`, `sim/Tuning.cs`, `sim/Level.cs`, `sim/GearCatalog.cs`);
if the code moves, this document moves with it.

---

## 0. Read this first

### 0.1 This amends spec §0, and that has to be decided out loud

`cognitohazard_port_spec.md` §0 says: *"Sprites are procedural primitives. Keep
them procedural. Do not source or generate assets."* CLAUDE.md repeats it under
Rules. Adopting this pipeline is a deliberate reversal of that rule, so it is
recorded in three places at once or not at all:
- spec §0 gets a line pointing here,
- CLAUDE.md's "Placeholder art only" rule is replaced by "Art per
  `cognitohazard_art_pipeline.md`",
- the `# Palette. Placeholder art only` comment in `main.gd` is updated.

Until then every sprite in this document is an OPTION the procedural drawing
still backs up. The procedural path is never deleted (see §9): it is the
fallback when a texture is missing, and what the headless harnesses draw.

### 0.2 What stays procedural no matter what

These are information, not decoration. They are drawn from live sim values
every frame, and a texture would either lie or lag:

| stays procedural | why |
|---|---|
| tracers, energy bolts, arcs, frag shrapnel | length is computed from the round's speed; colour encodes ballistic vs energy |
| muzzle flashes, sparks, smoke, shells, glass shards | driven by per-frame fade and scale |
| the vision polygon (lit floor) | 400 rays against `Opaque` each frame |
| awareness bar, armour bar, radio ring, posture glyphs `? ! ○ !?` | literal gauges of sim state |
| off-screen markers, reticle, HUD, every UI panel frame | HUD layout editor owns their boxes |
| glass panes (v1) | a translucent fill and a line; a texture adds nothing at 20 px |
| the EXIT rectangle | authored at arbitrary size |
| rarity colour on item names and frames | one palette, `item_catalog.gd RARITY_COLOURS` |

The pipeline makes: **actors, held weapons, armour overlays, bodies, floor
props, doors, floor and wall surfaces, and item icons.**

### 0.3 What Astra is doing here

GPT-6 Astra drives Blender directly (computer use + the `bpy` Python API). We
use it as a **script author**, not as a hand modeller: every asset is produced
by a `bpy` script checked into `Astra Assets/scripts/`, and the `.blend` files
are a cache of those scripts. Same rule as `tools/gen_icon.py` and the level
generators: *deterministic, regenerate rather than edit*. An asset that exists
only as a hand-tweaked `.blend` is an asset nobody can change in six months.

---

## 1. Visual style

### 1.1 One sentence

**Clean, flat-lit, top-down industrial-espionage miniatures on a near-black
floor: readable silhouettes first, surface detail a distant second.** Think
tabletop figures photographed straight down under a single overhead lamp, not
pixel art and not painterly.

### 1.2 Why not pixel art

The camera zooms by NON-INTEGER factors (play zoom 1.35, range 0.6-2.0,
dilation 0.90) and every actor ROTATES freely to any angle, 65,536 steps a
turn. Pixel art survives neither: rotation shreds it and fractional scale
shimmers it. The look is smooth, anti-aliased, filtered — like the procedural
art it replaces.

### 1.3 Rules of the style

1. **Silhouette is the message.** At 1.35x a guard is ~30 screen pixels
   across. Anything that must be read in a firefight is read by SHAPE: facing
   (wider across the shoulders than front-to-back), armour (the outline gets
   physically wider), weapon length. Colour is the second channel; surface
   detail is the third and must never compete.
2. **No baked directional light.** Actors, weapons and bodies ROTATE at
   runtime; a highlight baked on the left becomes a highlight on the right when
   the guard turns round. Light straight down (-Z) plus ambient occlusion only.
   Form comes from AO and a soft top-down falloff, never from a key light.
3. **No baked drop shadows.** The game draws the ground shadow (a 10 px
   radius disc, offset (+1.5, +2.0), 32% black). A baked one would rotate with
   the actor. Ship shadow-free sprites.
4. **Hard dark outline**, 1 world px (4 texture px at the render scale, §2),
   at 35% of the body's value — the current `dark = col * 0.35`. Silhouette
   and material boundaries only; no interior hatching.
5. **Low floor contrast, high actor contrast.** The world is almost black
   (floor ~7% luminance, lit floor ~11%). Actors and props are the bright
   things in it. Floor and wall textures stay within a few percent of their
   base value, or their noise competes with 1.6 px tracers.
6. **Tint, don't paint, anything that changes meaning.** Player and guard are
   the same rig in different colours; dead and subdued are the same body in
   different colours; armour intact and spent are the same plate in different
   colours. Those parts are rendered GREYSCALE and coloured by the game at
   draw time (`modulate`), so the palette in §3 stays in `main.gd` where it is
   already one place.
7. **Materials read as material at a glance, not up close:** matte fabric,
   satin gunmetal, scuffed painted steel, dull wood. No specular glints (they
   would rotate), no emissive except where the game already glows (energy
   weapons' cells, the objective).

---

## 2. Scale and pixel sizes

### 2.1 The unit

- **World pixel (wpx):** the game's unit. Design resolution 960x620.
- **Cell:** 20 wpx (`Level.CellPx`). The level grid.
- **Actor body:** 22 wpx across (`Tune.ActorRadius` = 11). This is the
  collision circle; the body must fit inside it. Only the weapon may leave it.
- **Muzzle:** 22 wpx forward of the actor's centre (`Tune.MuzzleOffset`).
  Rounds spawn there, so every held weapon's barrel ends there — exactly.

### 2.2 Render scale: 4 texture pixels per world pixel ("@4x")

The largest on-screen magnification the game reaches in normal play is
`PLAY_ZOOM` 1.35 × window stretch (1.5 at the default 1440x930 window, 1.74
fullscreen at 1080p) ≈ **2.35**, and ~4.6 at the 2.0 zoom cap on a 1440p
screen. 4x covers ordinary play with room to spare and costs little: the whole
actor set is well under 10 MB. Everything below is given in wpx **and** in
texture px.

**Do not render at 1x and upscale, and do not render at 8x "to be safe":** 4x
with mipmaps is the contract, and the validator (§7) rejects other sizes.

### 2.3 The size table

All canvases are square-or-rectangular PNGs with the PIVOT at the exact centre
unless stated. Facing is **+X (right)** for everything that rotates.

| asset | world size (wpx) | texture (px) | pivot | notes |
|---|---|---|---|---|
| actor canvas (every actor layer) | 56 × 56 | 224 × 224 | centre = body centre | ±28 wpx holds the 22 wpx muzzle plus a 6 wpx flash margin |
| body (torso + head) | fits in Ø22 | fits in Ø88 | centre | torso 8.4 deep × 11.2 wide; head Ø11.4 at +2.2 forward |
| legs (walk cycle) | fits in Ø22 | — | centre | 8 frames, same canvas |
| held weapon + arms | barrel tip at x = +22 | tip at x = +88 from centre | centre | one per weapon (13) |
| armour overlay | ≤ Ø26 | ≤ Ø104 | centre | 3 tiers; heavy is the widest silhouette in the game |
| prone body (dead/subdued) | 28 × 28 | 112 × 112 | centre | body 14 × 9, head Ø8.4 at +6 |
| supply chest | 16 × 12 (canvas 20 × 16) | 80 × 64 | centre | + an OPENED/empty state |
| objective case (on floor) | 16 × 12 (canvas 20 × 16) | 80 × 64 | centre | the game still draws its 15 wpx ring |
| record cache `$` | 12 × 12 (canvas 16 × 16) | 64 × 64 | centre | + a TAKEN state |
| dropped bag (ground pile) | 18 × 14 (canvas 22 × 18) | 88 × 72 | centre | count label stays procedural |
| grenade on the floor | Ø8.4 (canvas 12 × 12) | 48 × 48 | centre | fuse light stays procedural |
| door leaf, 2-cell | 34 × 10 | 136 × 40 | centre | 40 wpx opening − two 3 wpx jambs; 5 wpx inset each face |
| door leaf, 3-cell | 54 × 10 | 216 × 40 | centre | — |
| door jamb | 3 × 20 | 12 × 80 | — | shared by both leaf lengths |
| floor surface (tiling) | 80 × 80 (4 × 4 cells) | 320 × 320 | — | seamless on all four edges |
| wall surface (tiling) | 40 × 40 (2 × 2 cells) | 160 × 160 | — | seamless; walls are merged rects of ANY size |
| item icon | 20 wpx per grid cell | 80 px per cell | — | footprint W × H from `GearCatalog` (§5) |

### 2.4 Orientation, Blender to Godot

- Blender camera: **orthographic, looking straight down −Z**, rotation (0,0,0).
- Model faces **+X** in Blender. Image right = +X.
- Blender +Y is image UP; Godot +Y is DOWN. So the actor's LEFT side
  (Blender +Y) is on screen top when it faces right. A right-handed grip
  (weapon on the actor's right, Blender −Y) therefore lands on screen BOTTOM
  when facing right. Keep weapons on the centre line unless asymmetry is the
  point, as the procedural art does.
- **Model in Blender units where 1 BU = 1 wpx**, set `ortho_scale` to the
  canvas's world width (56 for actors), and render at 4 × that in pixels.

---

## 3. Palette

The game's palette is `main.gd` `C_*` plus `item_catalog.gd RARITY_COLOURS`.
Textures do not USE these as paint so much as RESPECT them: the hues are
spoken for, each with a meaning, and an asset that borrows one says something
it does not mean.

### 3.1 World values (for matching tone, and for greyscale tint targets)

| name | hex | meaning |
|---|---|---|
| C_BEYOND | `#050608` | outside the level |
| C_VOID | `#0A0C0F` | — |
| C_FLOOR | `#0D1116` | floor in shadow |
| C_FLOOR_LIT | `#161C23` | floor in the player's sight |
| C_WALL | `#292E38` | wall fill |
| C_WALL_EDGE | `#3D4554` | wall edge, door jambs, broken glass frame |
| C_PLAYER | `#D9DEEB` | player tint (pale, cold) |
| C_GUARD | `#B88F5C` | guard tint (tan, warm) |
| C_DEAD | `#613833` | corpse tint (desaturated blood) |
| C_DOWN | `#4C6B8F` | subdued tint (cold blue) |
| C_PLATE | `#9EB2CC` | armour intact (cold steel) |
| C_PLATE_GONE | `#575E6B` | armour spent |
| C_CHEST | `#D9A85C` | supply chest (warm: gear) |
| C_CHEST_EMPTY | `#574F42` | searched chest |
| C_OBJECTIVE | `#8CD9F2` | the objective (cold cyan) |
| C_GROUND | `#94A3B8` | your own dropped gear (cool grey-blue) |
| C_CACHE | `#E6BD4C` | record cache |
| C_EXIT | `#40B87A` | exit |
| C_GLASS | `#9ED6F2` | glass |
| C_DOOR | `#5C4530` | door leaf |
| C_DOOR_EDGE | `#9E7A52` | door edge, handle |

### 3.2 Reserved hues — do not use saturated in any texture

| hue | reserved for |
|---|---|
| red `#E65247` | SIGNAL: engaging guard, danger, awareness past 65% |
| amber `#EBB240` | AMBER: suspicion, radio, hunting markers |
| cyan `#66C2D9` / `#8CD9F2` | cold awareness, glass, the objective, energy rounds |
| green `#40B87A` / `#5CD166` | the exit; uncommon rarity |
| blue `#5799FF` | rare rarity |
| purple `#B86BFA` | epic rarity |
| orange `#FF9929` | legendary rarity |

Consequences:
- **Item icons are NEUTRAL.** The game frames and names them in the rarity
  colour; an icon painted purple would read as epic whatever it is. Guns are
  gunmetal and polymer, fabric is olive/khaki/grey, packs are canvas.
- **Energy weapons** (Photon, Arc Lance, Tesla) may show a SMALL cyan cell or
  coil — cyan already means "energy" on their rounds. Nothing else glows.
- **The objective case** is the one prop allowed a clean cold cyan accent.
- **Nothing is red** except as a tiny detail (a fuse light is procedural and
  already red).

### 3.3 Value targets for greyscale (tinted) layers

Tinted layers are rendered as greyscale where **R = G = B** (the validator
checks within ±2). The game multiplies by the tint, so:
- main body mass: **~0.90-1.00** (so the tint reads at full strength),
- shaded folds / AO: **0.60-0.80**,
- the outline: **0.35** (reproduces today's `col * 0.35`),
- fixed-colour details (boots, gloves, straps, the gun) go on a SEPARATE
  untinted layer (§4.1), never in the tinted one.

---

## 4. Asset classes

### 4.1 Actors (player and every guard share one rig)

The procedural human is drawn back to front as: shadow, legs, torso, armour,
arms + weapon, head. The sprite version keeps that order as separate LAYERS,
because the game moves some independently (recoil pulls arms and gun back
2.5 wpx per unit of recoil; legs animate; armour appears or not):

| layer | files | tinted? | frames |
|---|---|---|---|
| legs | `actor_legs_00..07.png` | yes | 8-frame walk cycle |
| torso | `actor_torso.png` + `actor_torso_detail.png` | base yes, detail no | 1 |
| armour | `armour_light.png`, `armour_medium.png`, `armour_heavy.png` | yes (C_PLATE / C_PLATE_GONE) | 1 each |
| arms + weapon | `held_<weapon>.png` + `held_<weapon>_arms.png` | gun no, arms yes | 1 each, 13 weapons |
| head | `actor_head.png` + `actor_head_detail.png` | base yes, detail no | 1 |

Requirements:
- **Facing reads from the silhouette**: shoulders 11.2 wpx wide, torso 8.4
  deep. The head sits 2.2 wpx FORWARD of centre.
- **Armour widens the silhouette.** Light, medium, heavy must be tellable
  apart by OUTLINE alone: pads sit proud of the shoulders, not inset on them;
  heavy is the widest thing an actor can be (`bulk` 1.0 → 1.9 in code). A
  recolour is not an armour tier.
- **Walk cycle is driven by DISTANCE, not time** (spec §5): the game advances
  a frame per ~3.5 wpx travelled (one cycle ≈ 28 wpx, tunable in game/). A
  frozen guard stands on frame 0, so frame 0 is the neutral stance.
- **Every held weapon ends at x = +22 wpx.** Length is expressed backward (a
  longer gun sits further into the shoulder), never by moving the muzzle.
  Relative lengths by class: pistols (Glock, Welrod) short in two hands; MP7
  compact; AK, Remington, VSS, Photon, Tesla rifle-length; SAW, Vulcan, Arc
  Lance, AWM long. The SAW and Vulcan are visibly BULKY (they cost movement).
  Frag is a grenade in the right hand, not a gun.
- Arms are drawn as two limbs from the shoulders (±3 wpx) converging on the
  weapon, as now.

### 4.2 Bodies

`body_prone_0..3.png` — four sprawls, greyscale, one canvas. The game picks
one by the guard's index and rotates by his fall roll; it tints C_DEAD or
C_DOWN. **No weapon on the body** (spec §9: corpses are unarmed; the gun is in
the loot). No blood pool baked in: decals are procedural.

### 4.3 Floor props

| prop | states | must read as |
|---|---|---|
| supply chest `C` | `chest_full`, `chest_open` | WARM, a crate with a lid line; the open one visibly searched (lid off / flaps open) from across a room |
| objective case `!` | `objective_case` (+ `objective_site_empty`) | a hard-shell case, cold cyan accent; never mistakable for a chest |
| record cache `$` | `cache_full`, `cache_taken` | small, a filing box or card holder; distinct from the chest in SIZE and shape |
| ground pile | `ground_bag` | a soft BAG with a flap and two straps — something the PLAYER put down. Must never read as a chest |
| grenade | `grenade` | small, olive, round; the blinking fuse is procedural |

These are NOT tinted — they carry their own colour — but that colour stays in
the family the palette gives them (chest warm amber-brown, bag cool grey-blue,
objective cyan-accented, cache gold-brown).

### 4.4 Doors

- `door_leaf_2.png`, `door_leaf_3.png`: the SHUT leaf, horizontal, with the
  seam of a double door across its centre and a small handle beside it. Warm,
  solid — a shut door hides what is behind it and must look as solid as a wall
  while never reading AS a wall.
- `door_jamb.png`: the frame piece, wall-edge grey.
- The OPEN state reuses the leaf, rotated 90° on its hinge by the game; the
  ghosted swing arc stays procedural.
- Vertical doors are the same sprites rotated. Author horizontal only.

### 4.5 Floor and walls

- `floor_tile.png` (320 × 320, 4 × 4 cells, seamless). Dark concrete / worn
  vinyl, **greyscale**, mean value tuned so that `modulate(C_FLOOR)` and
  `modulate(C_FLOOR_LIT)` reproduce today's two floor tones. The lit/unlit
  split is the vision polygon, textured with the same tile — so the tile must
  look right at BOTH tints.
- `wall_tile.png` (160 × 160, seamless). Painted block or concrete, value
  around C_WALL. The 1 wpx C_WALL_EDGE outline stays procedural, so do not
  paint an edge into the tile.
- **No visible cell grid** in either. Cells are a simulation fact, not a
  visual one; the game has never drawn them. Four-cell repeat is chosen so
  the tile does not beat against the 20 wpx grid.
- Contrast budget: detail within **±4% value** of the mean on the floor,
  **±6%** on walls.

### 4.6 Item icons (inventory, loot panel, shop, dev menu, tooltip)

- One icon per `GearCatalog` item: **48 items** plus `item_unknown.png` for
  the loot panel's unidentified rows.
- Named by **item id, never by name**: `item_102.png` (AK-47). Ids are fixed
  forever; names are not.
- Size = footprint × 80 px per cell, from the catalogue (§5). The stash draws
  items in 20 px UI cells; the icon is drawn into that box.
- **Laid flat on a table, camera straight down** — the same orthographic
  rig as the world, so a gun seen in the stash is the gun seen in a hand, in
  profile. Long axis horizontal, muzzle RIGHT. The stash rotates an item 90°
  itself when the player turns it; never ship a rotated variant.
- **Inset 8 px (2 UI px) on every side** so the rarity frame the game draws
  is never covered.
- Neutral colour (§3.2). Transparent background. No text, no labels (the game
  labels items; baked text would not localise and would collide).
- Attachments are shown on their own, not mounted.
- The mission objective (`item_900`) is the same case as the floor prop,
  larger.

---

## 5. Item icon manifest

Texture size = W × 80 by H × 80.

| id | name | W×H | texture |
|---|---|---|---|
| 100 | Glock | 2×2 | 160 × 160 |
| 101 | MP7 | 3×2 | 240 × 160 |
| 102 | AK-47 | 4×2 | 320 × 160 |
| 103 | Remington | 4×2 | 320 × 160 |
| 104 | SAW | 5×3 | 400 × 240 |
| 105 | Welrod | 2×2 | 160 × 160 |
| 106 | VSS | 4×2 | 320 × 160 |
| 107 | Photon | 3×2 | 240 × 160 |
| 108 | Arc Lance | 5×2 | 400 × 160 |
| 109 | Vulcan | 5×3 | 400 × 240 |
| 110 | Tesla | 4×2 | 320 × 160 |
| 111 | Frag | 2×1 | 160 × 80 |
| 112 | AWM | 5×2 | 400 × 160 |
| 201 | light weave | 2×2 | 160 × 160 |
| 202 | medium carrier | 3×2 | 240 × 160 |
| 203 | heavy plate | 3×3 | 240 × 240 |
| 301 | red dot | 1×1 | 80 × 80 |
| 302 | holographic | 1×1 | 80 × 80 |
| 303 | scope | 2×1 | 160 × 80 |
| 311 | rubber grip | 1×1 | 80 × 80 |
| 312 | tactical grip | 1×1 | 80 × 80 |
| 313 | angled grip | 1×1 | 80 × 80 |
| 321 | laser | 1×1 | 80 × 80 |
| 322 | flashlight | 2×1 | 160 × 80 |
| 323 | foregrip | 1×1 | 80 × 80 |
| 331 | extended mag | 1×2 | 80 × 160 |
| 332 | drum mag | 2×2 | 160 × 160 |
| 333 | quick-release | 1×1 | 80 × 80 |
| 341 | subsonic | 1×1 | 80 × 80 |
| 342 | hollow point | 1×1 | 80 × 80 |
| 343 | armour piercing | 1×1 | 80 × 80 |
| 351 | light stock | 2×1 | 160 × 80 |
| 352 | heavy stock | 3×1 | 240 × 80 |
| 401 | field cap | 2×1 | 160 × 80 |
| 402 | combat helmet | 2×2 | 160 × 160 |
| 501 | satchel | 2×2 | 160 × 160 |
| 502 | field pack | 3×2 | 240 × 160 |
| 503 | large pack | 3×3 | 240 × 240 |
| 601 | canvas shoes | 2×1 | 160 × 80 |
| 602 | patrol boots | 2×2 | 160 × 160 |
| 701 | fatigues | 2×2 | 160 × 160 |
| 702 | work shirt | 2×1 | 160 × 80 |
| 801 | work gloves | 1×1 | 80 × 80 |
| 802 | armguards | 2×1 | 160 × 80 |
| 900 | sealed case | 2×2 | 160 × 160 |
| 901 | work trousers | 2×2 | 160 × 160 |
| 902 | cargo trousers | 2×2 | 160 × 160 |
| 903 | padded greaves | 2×3 | 160 × 240 |
| — | unknown item | 1×1 | 80 × 80 (`item_unknown.png`) |

The catalogue is the source of truth, not this table: the render script READS
footprints out of `sim/GearCatalog.cs` (a regex over the `new GearItem(` lines)
rather than copying them, so a resized item re-renders at its new size and a
new item fails the validator until it has an icon.

Ammunition (341-343) is drawn as a few loose rounds / a stripper clip: the
three must differ in SHAPE (subsonic long and heavy, hollow point with the
cup, AP with the penetrator tip), not in coloured tips alone.

---

## 6. Blender setup (what every script establishes)

A shared module, `Astra Assets/scripts/cog_common.py`, sets all of this up and
every asset script imports it. No asset script sets render state by itself.

| setting | value | why |
|---|---|---|
| Blender | 4.2 LTS or later; pin the version in `cog_common.py` and assert it | renders must be reproducible |
| units | 1 Blender unit = 1 wpx | sizes in §2 transfer without arithmetic |
| engine | EEVEE | fast, deterministic enough, good AO |
| camera | orthographic, straight down, `ortho_scale` = canvas world width | §2.4 |
| resolution | canvas world size × 4, 100% | §2.2 |
| film | transparent | sprites composite over the game's floor |
| colour management | view transform **Standard**, look None, exposure 0, gamma 1 | AgX/Filmic would shift every palette value |
| lighting | one Sun straight down (−Z), strength tuned so a 1.0 albedo renders ≈ 1.0; world ambient low; AO on | no directional light (§1.3.2) |
| shadows | off (sun casts none) | the game draws the drop shadow |
| outline | Freestyle, absolute thickness 4 px, silhouette + material border, colour = 0.35 × body | §1.3.4 |
| anti-aliasing | filter size 1.50 px, 64 samples | smooth edges without softening the outline |
| output | PNG, RGBA, 8-bit, **straight (unassociated) alpha**, compression 15 | what Godot imports cleanly |
| seeds | any noise texture or scatter takes a fixed seed from the script | regenerate byte-stable |

Scripts render headless: `blender -b -P "Astra Assets/scripts/render_actors.py"`.
Astra may use the GUI to LOOK, but the final render is the headless run.

---

## 7. Folders, naming, validation

### 7.1 Where things live

```
Attempt_1/
  Astra Assets/                 SOURCE, outside the Godot project on purpose
    scripts/                    bpy scripts: THE source of truth, committed
      cog_common.py
      render_actors.py  render_weapons.py  render_props.py
      render_surfaces.py  render_items.py
    blend/                      generated .blend caches, safe to delete
    previews/                   contact sheets (§7.3), not shipped
  cognitohazard-v-1/
    assets/                     OUTPUT, imported by Godot
      actors/    weapons/    props/    surfaces/    items/
      manifest.json
```

The source stays OUTSIDE `cognitohazard-v-1/`: Godot 4 imports any `.blend` it
finds in the project as a 3D scene (and tries to run Blender to do it). If a
source folder ever has to live inside the project, put a `.gdignore` in it.

Names: snake_case, lower case, no spaces (the source folder's name is the one
exception and must be QUOTED in every command). Weapons by their `WeaponKind`
name in lower case: `held_glock.png`, `held_arc_lance.png`, `held_awm.png`.

### 7.2 `assets/manifest.json`

Written by the render scripts, never by hand. One entry per PNG:

```json
{
  "path": "weapons/held_ak47.png",
  "world_size": [56, 56],
  "pivot": [28, 28],
  "anchors": { "muzzle": [50, 28] },
  "tinted": false,
  "layer": "held",
  "source": "render_weapons.py"
}
```

Anchors are in wpx from the canvas's top-left. The game reads the muzzle
anchor to ASSERT it (it must be 22 wpx ahead of the pivot), never to move the
shot; the sim decides where rounds start.

### 7.3 Validation (a script, `tools/check_assets.py`, to be written)

Refuses the batch, like `levelkit.py` refuses a level, if any of:
- a PNG's size is not its manifest `world_size × 4`;
- a PNG is not RGBA 8-bit, or its alpha is premultiplied;
- a `tinted` layer is not greyscale (R, G, B within ±2 on every opaque pixel);
- an opaque pixel of a body layer falls outside the Ø22 wpx circle;
- a held weapon's muzzle anchor is not exactly (+22, 0) from the pivot;
- a floor or wall tile does not tile (left/right and top/bottom edge columns
  differ by more than a small tolerance);
- a floor tile's value range exceeds the §4.5 budget;
- an item in `GearCatalog.cs` has no `items/item_<id>.png`, or an icon's size
  disagrees with the item's footprint;
- an icon has an opaque pixel inside the 8 px inset;
- any opaque pixel is close to a reserved hue (§3.2) at high saturation,
  outside the allowed exceptions (energy weapons, objective).

It also writes `Astra Assets/previews/contact_*.png`: every sprite composited
on `C_FLOOR` and on `C_FLOOR_LIT` at **1.0x and 1.35x game scale**, tinted as
the game will tint it. **That sheet is the review surface**, not the 4x
render: an asset that looks good at 4x and dissolves at 1.35x has failed.

---

## 8. The Astra loop

1. **Brief.** Paste §10 into the Astra session, and attach this file.
2. **Script, don't sculpt.** Astra writes or extends a script in
   `Astra Assets/scripts/`, importing `cog_common.py`. Primitives, modifiers
   and geometry nodes are all fine; hand edits that are not in the script are
   not.
3. **Render headless** into `cognitohazard-v-1/assets/…` and write the
   manifest.
4. **Validate:** `python3 tools/check_assets.py`. Fix and re-render until clean.
5. **Review the contact sheet** at game scale (§7.3), against the readability
   tests in §8.1. A human signs off here; Astra does not self-approve art.
6. **Import:** `$G --headless --path . --import` (from `cognitohazard-v-1/`).
7. **Commit scripts and outputs together**, the same way golden hashes are
   re-baked in the same commit as the sim change.

### 8.1 Readability tests (at 1.35x, on the dark floor, squinting)

Each comes from a comment in the code that says why the procedural art looks
the way it does. A sprite that fails one has lost information the game relies
on:
- Which way is this actor facing? (shoulders wide, head forward)
- Is this guard armoured, and how heavily? (outline, not colour)
- Player or guard? (tint)
- Corpse or subdued? (tint and sprawl)
- Chest, dropped bag, objective, or cache? (four different shapes)
- Has this chest been searched? (open state)
- Is that door shut, and is it a door or a wall? (warm leaf, jambs)
- Which gun is he carrying? (length and bulk; at least pistol / rifle / heavy)
- Does a tracer stay visible over the floor texture? (floor contrast budget)

Production order, cheapest proof first: actor rig and one weapon (proves
tinting, layering, the muzzle rule and readability) → the other 12 weapons →
armour → bodies → props → doors → surfaces → item icons.

---

## 9. Integration (game/ work, not Astra's)

Listed so the art is made to fit it; built by us, after the art exists.

- **Presentation only.** Sprites never reach an InputFrame, the state hash or
  `sim/`. Hitboxes stay `Tune.ActorRadius`; a sprite does not change what can
  be hit.
- **Draw in world units through the camera helpers.** A sprite is drawn with a
  destination rect in WPX (texture ÷ 4), under `_world_pass()`, or under
  `_world_xform(pos, ang)` for anything rotated. Never
  `draw_set_transform` directly (CLAUDE.md, "Levels and the camera").
- **Procedural fallback stays.** Each `_draw_*` checks for its texture and
  falls back to today's primitives if it is missing. The headless harnesses
  keep passing with no assets imported, and a half-finished set is playable.
- **Filtering.** Textures import lossless with mipmaps and
  `process/fix_alpha_border` on; the world Node2D draws with
  `TEXTURE_FILTER_LINEAR_WITH_MIPMAPS`, since zoom goes as low as 0.6.
- **Floor under the vision polygon.** Both floor passes use the one tile: the
  full-level rect modulated `C_FLOOR`, then the vision polygon with UVs in
  world space modulated `C_FLOOR_LIT`.
- **Lighting plan compatibility.** `cognitohazard_lighting_plan.md` makes
  light a runtime quantity. Flat-lit, shadow-free sprites (§1.3) are what that
  plan needs; baked lighting would contradict it.
- **Export.** `assets/` is ordinary Godot resources and ships with no filter
  change. The source folder is outside the project, so nothing to exclude.
  Watch the build size (176 MB today): the whole set here is a few MB.
- **Harness.** `inventory_check.gd` gains one assertion once icons exist: every
  catalogue id has an icon whose size matches its footprint.

---

## 10. Master brief (paste into Astra)

> You are producing 2D sprite assets for **Cognitohazard**, a top-down
> stealth-action game in Godot 4. You work in Blender through `bpy` scripts;
> every asset must be produced by a script in `Astra Assets/scripts/` that
> imports `cog_common.py`, renders headless, and is deterministic. Do not hand
> edit renders.
>
> **Style:** clean, flat-lit, top-down industrial-espionage miniatures on a
> near-black floor. Silhouette first, surface detail a distant second. Smooth
> and anti-aliased, NOT pixel art (the game rotates and zooms freely).
>
> **Camera:** orthographic, straight down −Z. 1 Blender unit = 1 world pixel.
> Render at 4 texture pixels per world pixel. Everything that rotates faces
> +X (right).
>
> **Light:** one sun straight down, ambient occlusion, NO directional key, NO
> cast shadows, NO specular glints. Colour management Standard, not AgX.
>
> **Outline:** Freestyle, 4 px, silhouette and material borders, at 35% of
> the body's value.
>
> **Scale:** a cell is 20 wpx. An actor's body fits in a 22 wpx circle. Every
> held weapon's barrel tip is exactly 22 wpx ahead of the actor's centre. Actor
> canvases are 56 × 56 wpx (224 × 224 px), pivot at centre.
>
> **Tinting:** player/guard bodies, armour, and corpses are rendered GREYSCALE
> (R=G=B), body ≈ 0.95, folds 0.6-0.8, outline 0.35; the game colours them.
> Guns, boots, straps and props carry their own colour on separate layers.
>
> **Palette:** the world is very dark (floor `#0D1116`, walls `#292E38`). Do
> not use saturated red, amber, cyan, green, blue, purple or orange: those hues
> are reserved for game signals and item rarity. Item icons are neutral
> (gunmetal, polymer, olive, khaki, canvas). Exceptions: a small cyan cell on
> energy weapons, and a cyan accent on the objective case.
>
> **Item icons:** laid flat on a table, seen from above, muzzle right, 80 px
> per inventory cell, 8 px transparent inset on every side, no text. Footprints
> come from `sim/GearCatalog.cs`; name files `item_<id>.png`.
>
> Write `cognitohazard-v-1/assets/manifest.json` with size, pivot and anchors
> for every PNG, run `python3 tools/check_assets.py`, and show me the contact
> sheet at 1.0x and 1.35x game scale. The full contract is
> `cognitohazard_art_pipeline.md`; where this brief and that file disagree,
> the file wins.

---

## 11. Open decisions

1. **Spec §0.** Adopt this and amend §0/CLAUDE.md (§0.1), or keep it as a plan.
2. **Walk-cycle stride** (28 wpx per cycle) is a guess; it is tuned in game/,
   so the 8 frames do not depend on it.
3. **Guard variety.** One rig for twenty guards reads as a uniformed force,
   which fits. Per-guard apparel sprites (helmet, cap) are possible because
   guards already wear apparel from their loot roll — but apparel is cosmetic
   and not yet in the guard snapshot, so it is a game/ + bridge change first.
4. **Glass** stays procedural in v1 (§0.2). Revisit only if the lighting plan
   lands and panes need to catch light.
5. **UI art** (title screen, paper doll figure) is out of scope here; the doll
   is drawn from primitives behind translucent boxes and works.
