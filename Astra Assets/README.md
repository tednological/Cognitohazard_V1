# Cognitohazard sprite sources

**Cluster handoff:** read [HANDOFF.md](HANDOFF.md) for current status and
run `bash cluster_render.sh` with Blender 4.5.3 on the allocated GPU node.

The contract is [cognitohazard_art_pipeline.md](cognitohazard_art_pipeline.md).
All shipped PNGs are headless Blender renders. Never paint or resize the shipped
images after rendering. Review sheets are generated composites, not assets.

## Rebuild

Pinned runtime: **Blender 4.5.3 LTS**, EEVEE, Standard colour management.
Set `BLENDER` to the executable on your machine, then run from this directory:

```sh
"$BLENDER" -b -t 4 --python-exit-code 1 -P scripts/render_all.py
python3 ../cognitohazard-v-1/tools/check_assets.py
```

The validator needs Pillow and NumPy (`python3 -m pip install Pillow numpy`).
Each of the five `render_*.py` class scripts is also a standalone Blender entry
point. `COG_OUTPUT` optionally redirects renders for reproducibility checks;
the normal destination is the sibling game's `assets/` directory. The scripts
merge their own entries into the generated manifest, preserving other classes.
Do not run two render processes against the same output manifest concurrently.

## Outputs and layers

- Actor canvas: 56 world pixels, 224 texture pixels, center pivot.
- Legs include **eight separate untinted boot layers** named
  `actor_legs_00_detail.png` through `actor_legs_07_detail.png`. Composite each
  immediately over its matching tinted leg frame.
- Torso and head detail layers are untinted; arms are separate from each gun.
- Frag uses a grenade held in the right hand. Its muzzle anchor denotes the
  simulation's throw origin at +22; a grenade is not elongated to that point.
- Every inventory footprint is parsed from `sim/GearCatalog.cs`. Adding an
  unmapped item fails rendering instead of silently producing a generic icon.
- Generated manifest fields include world and texture size, pivot, anchors,
  tinting, layer, source script, pinned Blender version and random seed.

`tools/check_assets.py` lives in the game. Its maintained source copy is
`scripts/check_assets.py`; copy it there when editing the validator.

## Review

`previews/contact_world_1.00x.png` and `contact_world_1.35x.png` show assembled
actors, armour, walk frames, weapon classes, prop states and corpse tints.
`contact_all_*.png` show **every shipped PNG** on both floor values, including
all inventory icons and isolated layers. Inspect at 100% image size for the
stated game scale. Viewer fit-to-window changes that scale.

PNG alpha association has no definitive header flag. The validator checks the
PNG IHDR, straight-alpha renderer provenance, and low-alpha fringe evidence;
it does not pretend this proves association for every possible arbitrary PNG.
The common rig writes Blender PNGs directly, with no image postprocessing.

Human visual approval was received on 2026-09-23. Game sprite drawing, procedural fallback
wiring, mipmap import configuration and integration harness changes belong to
the subsequent integration step described in contract §9.

## Reproducibility

The shared renderer disables stamp metadata (including timestamps) so that
PNG bytes can be compared across runs. The surface shader keeps its intermediate
linear-light image in floating-point storage to preserve the dark-wall contrast
budget. Neither fix postprocesses delivered PNGs.

After a full production batch, render a second output and compare it:

```sh
COG_OUTPUT=/tmp/cog-repro "$BLENDER" -b -t 4 --python-exit-code 1 -P scripts/render_all.py
python3 scripts/check_reproducibility.py ../cognitohazard-v-1/assets /tmp/cog-repro --report previews/reproducibility.json
```

On the Linux continuation host, the temporary runtime is
`/tmp/cognitohazard-runtime/blender-4.5.3-linux-x64/blender` and validation Python
is `/tmp/cognitohazard-runtime/venv/bin/python`. These may disappear on reboot.
