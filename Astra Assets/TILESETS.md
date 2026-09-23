# Industrial and scientific map kits

110 modular assets, authored and rendered in Blender 4.5.3: **55 per theme,
330 RGBA PNGs** including color, normal, and emission companions. These are
an environment asset delivery; the live map renderer/editor has not been
switched to them. No simulation, levels, collision, saves, or lighting rules
were changed.

Open [the two-room preview](previews/tilesets/two_tilesets.png) or
[the interactive lighting preview](previews/tilesets/lighting_preview.html).
The HTML is self-contained and works from disk without a server. Move the
pointer to move its point light; choose the theme, ambient level, emission,
and individual texture passes. It illustrates material response, with no
shadow occlusion or gameplay visibility simulation.

## Contents

| Category | Each theme | Details |
|---|---:|---|
| Floor | 8 | Plain, plate, grate, drain, conduit, access hatch, worn, raised tread |
| Wall | 16 | Every cardinal connection mask, including isolated piers, end caps, corners, T and cross junctions |
| Pipe | 5 | Straight, elbow, T, cross, valve |
| Door | 3 | Closed, open/retracted, sealed |
| Light | 5 | Strip on/off/broken, caged beacon, floodlight |
| Marking | 4 | Lane, corner, chevrons, dashed boundary |
| Prop | 14 | Different equipment for each theme |

Industrial props: generator, compressor, transformer, control console,
electrical cabinet, barrel cluster, pallet, cargo crate, workbench, vent fan,
cable reel, pump, tool cart, pressure tank. Warm steel and subdued ochre
accents make maintenance and utility spaces.

Scientific props: lab bench, microscope, centrifuge, cryopod, containment
chamber, server rack, analysis console, sample rack, medical bed, fume hood,
sterilizer, gas cylinders, robot arm, specimen freezer. Pale ceramic, blue-grey
housings, and cool indicators make laboratories and secure containment spaces.

## Files and dimensions

- Sources: `scripts/render_tilesets.py`, plus the additive tileset rig functions
  in `scripts/cog_common.py`. Existing sprite-render paths are unchanged.
- Editable libraries: `blend/industrial_tileset.blend` and
  `blend/scientific_tileset.blend`. Each asset is an asset-marked collection
  with a centered instance offset. Assets are arranged in an eight-column
  browsing grid. The script is the source of truth.
- Game-ready texture files: `../cognitohazard-v-1/assets/tilesets/`.
  Theme subfolders contain `albedo/`, `normal/`, and `emission/`.
- Separate `industrial.json` and `scientific.json` manifests describe each
  asset's canvas, pivot, map paths, connectivity, light states and anchors.
  The original 115-sprite manifest is preserved. The existing asset validator
  delegates to the companion-kit checks when this folder is present.
- Review sheets: `previews/tilesets/industrial_catalog.png` and
  `scientific_catalog.png`; corresponding `*_world_1.00x.png` and
  `*_world_1.35x.png` show actual game scale when viewed at 100%.

One Blender unit is one world pixel. A map cell is **20 world pixels**.
Textures are **4×** world size. Walls/pipes occupy 20×20 canvases; doors
40×20; props 40×40 with transparent padding; seamless floor textures 80×80
(four cells in each direction). Pivots are centered. Floor fills should be
clipped by the map footprint; their repeat is independent of collision cells.

Wall and pipe masks use **N=1, E=2, S=4, W=8**, with north at the top of the
PNG. Choose the sum of connected neighbors; rotate marked pieces in 90° steps.
World coordinates in metadata originate at the canvas's top-left (+Y down).
Source-library geometry uses Blender +Y up. Horizontal doors span two cells;
rotate 90° for vertical placement. Door states are a matched sliding-door set.
They are not a drop-in replacement for the existing swinging-door animation.

## Lighting contract

The request for lighting-capable themed map kits extends the earlier sprite
brief: these packs use colored dark floors, emissive equipment, and normal
companions. The existing actor/item palette, textures and renderer remain
unchanged. Manufactured geometry supplies borders on these map assets instead
of Freestyle strokes, so color and data maps have matching silhouettes.

- **Albedo:** neutral overhead color plus local material occlusion on props;
  no directional cast shadow or baked light pool. Floors use flat material
  color to meet the original ±4% relative contrast budget in every channel.
- **Normal:** camera-aligned OpenGL **+Y up**, linear RGB data rendered using
  Raw color management. A flat face is approximately `(128,128,255)`.
  Decode `2 * rgb - 1` and normalize after filtering. The blue channel faces
  the viewer. Do not apply an sRGB transfer function to vector data. Rotate
  vectors as well as sprites when implementing a custom renderer.
- **Emission:** sRGB color, black where inactive, same straight-alpha coverage
  as the other maps. Multiply RGB by alpha when compositing it over a
  background; add it after diffuse lighting. A separate pass/material should
  keep it from being darkened with the base color. It contains luminous lens
  and indicator geometry, **not** the surrounding light spill.
- **Dynamic lights:** manifests provide centered fixture anchors and suggested
  color/radius (140 world pixels; 80 for beacons). These are art suggestions,
  not simulation tuning. On/off/broken strips have identical placement and
  canvas. Off and broken emission is black.
- **Occlusion:** manifests suggest wall and door occluder rectangles as
  `[x,y,width,height]`. They are visual guides; collision and light blocking
  must be derived from authoritative map/simulation geometry when integrated.
  Open doors have no leaf occluder. Props require deliberate collision placement.

RGBA PNGs come directly from Blender, without repainting, resizing or
postprocessing. Only contact sheets and illustrative room previews are resized,
composited and relit. Those live outside the game and are not production assets.
No Godot import, tile resource wiring, or in-game dynamic-light test is claimed.

## Rebuild and verify

From `Astra Assets/`:

```sh
BLENDER=/tmp/cognitohazard-runtime/blender-4.5.3-linux-x64/blender
PYTHON=/tmp/cognitohazard-runtime/venv/bin/python
"$BLENDER" -b -t 4 --python-exit-code 1 -P scripts/render_tilesets.py
"$PYTHON" scripts/check_tilesets.py
```

Python validation/review generation needs Pillow and NumPy. `COG_TILESET_THEME`
selects one theme; `COG_TILESET_ONLY` selects one asset ID. A partial run merges
its manifest entry and does not overwrite the complete Blender libraries.
`COG_TILESET_OUTPUT` redirects PNGs/manifests and leaves production libraries
untouched. A reproducibility check is:

```sh
COG_TILESET_OUTPUT=/tmp/cog-tileset-repro "$BLENDER" -b -t 4 --python-exit-code 1 -P scripts/render_tilesets.py
"$PYTHON" scripts/check_tilesets.py --compare /tmp/cog-tileset-repro
```

The validator checks count, format, 4× dimensions, matching alpha, normal-vector
sanity, emission states, transparent prop margins, floor seams/contrast and
complete wall connectivity with matching port edges in all three passes. It records SHA-256 for all 330 PNGs in
`previews/tilesets/validation.json`, and compares manifests and every PNG when
`--compare` is supplied. Library caches are reproducible from scripts but are
not asserted byte-identical (Blender files carry session metadata).
