# Cognitohazard — 3090 cluster handoff

## Map-kit extension — 2026-09-23

Industrial and scientific map kits are now built: 55 assets each, with 330
color/normal/emission PNGs and two editable Blender libraries. See
[TILESETS.md](TILESETS.md) for files, rebuild commands and lighting conventions.
All 330 PNGs and both manifests reproduce byte-for-byte. The original 115
sprites are unchanged. The map kits are now drawn in game by
`game/level_art.gd`, selected per level by `theme:` (industrial or scientific). Review the assembled rooms and
interactive lighting viewer in `previews/tilesets/`.

## Current status — Linux continuation, 2026-09-23

**All 115 production PNGs are rendered, validated, and visually approved by the user on 2026-09-23.**
The historical Mac handoff below and the transfer archive describe the earlier
two-PNG state; they are retained as history, not as the current delivery.

- Blender 4.5.3 LTS ran headless EEVEE on the RTX 3090 with driver 595.84.
- Fixed wall contrast by retaining the procedural image in floating-point
  linear storage. The final output remains RGBA 8-bit PNG.
- Disabled Blender stamp metadata, whose date and render-duration fields
  made otherwise identical PNGs differ between runs.
- Full batch validation passes; all 11 validator fault-injection cases pass.
- Two final full batches have identical manifests and byte-identical PNGs.
  `previews/reproducibility.json` records all 115 SHA-256 hashes;
  `scripts/check_reproducibility.py` repeats the comparison.
- Four contact sheets now exist at the paths listed below. The user reviewed
  the art and approved publishing the assets: "I like it, push those assets".
- Game integration, Godot import, and game build remain pending. The approved
  asset set is ready for the integration step under art-contract §9.

Runtime paths (temporary, may disappear on reboot):

```sh
export BLENDER=/tmp/cognitohazard-runtime/blender-4.5.3-linux-x64/blender
export PYTHON=/tmp/cognitohazard-runtime/venv/bin/python
bash cluster_render.sh
```

Final evidence: `previews/cluster_preflight.log`, `cluster_render.log`,
`cluster_validation.log`, `cluster_validator_tests.log`, and
`cluster_repro_render.log`. Freestyle reports corrected degenerate triangles
from some primitive meshes; both batches completed and passed the checks.

Review at 100% image size:

- `previews/contact_world_1.00x.png`
- `previews/contact_world_1.35x.png`
- `previews/contact_all_1.00x.png`
- `previews/contact_all_1.35x.png`

## Historical Mac transfer handoff

Saved 2026-09-23. The user moved production from the Intel Mac to a cluster
with an RTX 3090. **The asset batch is not complete or visually approved.**

## Resume

1. Extract `cognitohazard-render-handoff.tar.gz` into a fresh directory. Keep
   `Astra Assets/` and `cognitohazard-v-1/` as siblings; scripts derive these
   locations from their own paths, not the current directory.
2. Use **Blender 4.5.3 LTS** on the allocated GPU node. The version assertion
   is intentional. EEVEE needs a working graphics-driver context on the node;
   configure that using your cluster's graphics/headless setup. This is an
   EEVEE job, not a Cycles/CUDA job. No scheduler or partition is assumed.
3. Install Pillow and NumPy in the Python environment used for validation:
   `python3 -m pip install Pillow numpy`.
4. From the extracted root:

```sh
cd 'Astra Assets'
export BLENDER=/absolute/path/to/blender-4.5.3/blender
export PYTHON=/absolute/path/to/python3
bash cluster_render.sh
```

The wrapper performs a configuration check, renders **all 115 PNGs**, installs
and runs `python3 tools/check_assets.py` from the game directory, then runs
11 validator fault-injection cases on temporary copies. Logs are in
`Astra Assets/previews/cluster_*.log`. Run the full batch once; do not use a
skip-existing resume for the current two partial PNGs, because the source
changed during renderer troubleshooting.

For an isolated asset retry after that full run:

```sh
COG_ONLY=items/item_102.png "$BLENDER" -b -t 4 --python-exit-code 1 \
  -P scripts/render_items.py
```

The five authoring scripts can run separately. `scripts/run_batch.py` is an
optional sequential one-process-per-asset fallback if a driver has trouble
with repeated renders; it is slower. Do not run concurrent processes against
the same manifest. `COG_OUTPUT` redirects an entire batch for comparisons.

## What is saved

- `scripts/cog_common.py`: pinned renderer, camera, material/AO rig, primitive
  geometry helpers, catalogue parser, generated manifest writer.
- `render_actors.py`: 8 leg frames plus 8 untinted boot-detail frames, torso and
  head with separate detail layers, 3 armour tiers, 4 unarmed prone poses.
- `render_weapons.py`: 13 held weapons plus separate greyscale arm layers.
- `render_props.py`: 8 prop/state sprites and 3 door/frame sprites.
- `render_surfaces.py`: bounded periodic floor/wall material fields.
- `render_items.py`: all 48 catalogue icons plus unknown. Footprints are read
  from `sim/GearCatalog.cs`, which is included in the transfer bundle.
- `check_assets.py`: canvas/format/alpha evidence, greyscale, body circle,
  muzzle anchors and barrel geometry, surface seams/contrast, palette,
  catalogue completeness and icon inset checks; creates four review sheets.
- `test_validator.py`: 11 fault-injection cases, **not yet run** because the
  production set is incomplete.
- Scripts, README, full art contract, partial manifest/PNGs and diagnostic logs.
- The original contract-required policy-note amendments to spec §0, CLAUDE.md
  and game/main.gd are included as context snapshots in the bundle. Only the
  palette comment was changed in main.gd; sprite integration is still pending.

## Exact completion status

Only these **two production PNGs** exist:

- `assets/actors/actor_legs_00.png`
- `assets/actors/actor_legs_00_detail.png`

They and the incomplete manifest were generated with Blender 4.5.3. Separate
proof renders are under `previews/proof/`. They are not production delivery.
No contact sheets exist yet. The final batch validator has not passed; checks
on the two available PNGs passed while expected missing-file errors were
excluded. Godot `--check-only --script res://game/main.gd` passed after the
comment change. Python source compilation passed before handoff preparation.
No game sprite integration, Godot import, git commit or art sign-off was done.

## Local issue and last fix

Blender 4.5.3 rendered the first frame in about 10 seconds, then slowed severely
on subsequent renders on the Intel Mac. A process sample showed waits in the
Intel Metal driver's graphics-memory allocation. Retaining materials did not
resolve it. This is a local diagnosis, not a confirmed Blender-wide defect.
The macOS build rejected OpenGL; it exposes Metal only.

A Blender 4.2.3 compatibility experiment stopped before rendering because a
fresh Freestyle line set had no style. **That initialization is now fixed**
explicitly in `cog_common.py`. The version pin is restored to **4.5.3** for the
3090. Every setup now resets the scene with `read_factory_settings`, creates
its world as needed, and creates a missing Freestyle style. A full render
using this final setup still needs to be performed on the cluster.

The local runtime DMGs/apps are temporary Mac binaries and are intentionally
not included in the transfer bundle. Obtain the Linux Blender 4.5.3 build for
the cluster. Diagnostic logs preserve the attempted runtime versions.

## After rendering

Fix any validator errors in the bpy scripts, re-render the affected classes,
and run the validator again. Do not edit PNGs. Check deterministic output by
rendering a second batch to `COG_OUTPUT` and comparing PNG hashes. This has
not yet been performed. Inspect the actual game-scale sheets:

- `previews/contact_world_1.00x.png`
- `previews/contact_world_1.35x.png`
- `previews/contact_all_1.00x.png`
- `previews/contact_all_1.35x.png`

The world sheets composite layers and game tints; the all sheets include every
PNG on both floor colours. View at 100% for the stated scale. Human visual
approval comes next, before game integration under contract §9.
