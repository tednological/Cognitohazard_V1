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
- Level art harness (map kits; see "Level art"):
  `$G --headless --path . --script res://tests/level_art_check.gd`
- Render every level in its kit to PNG for a look (NOT headless, needs a
  renderer; touches nothing in user://):
  `$G --path . --script res://tools/render_level_art.gd -- --out /abs/dir [--level name]`

# Shipping a build
`tools/ship.sh` is the whole release: every harness, then the export, then the
zip. `tools/ship.sh "macOS"` / `"Linux"` for the others; no argument means
Windows. It REFUSES to export if anything fails, and the failure is taken from
the step itself -- `harness | tail -1` reports tail's status, which is always
0, so a gate written that way passes every time.

The Windows build is ONE self-contained `Cognitohazard.exe` (176 MB, 67 MB
zipped): `binary_format/embed_pck` puts the pack inside the executable and
`dotnet/embed_build_outputs` puts the .NET runtime inside the pack. Nothing to
install and nothing to lose beside it. `debug/export_console_wrapper=0` because
a second exe next to it is a second thing to send.

Export templates are NOT installed by default and must be the MONO ones at the
EXACT editor version (4.6.2.stable.mono), in
`~/Library/Application Support/Godot/export_templates/<version>/`. Editor >
Manage Export Templates, or unzip the `.tpz` there by hand. The standard
templates are a different download and will not do.

`export_presets.cfg` is committed, and two of its filters are load-bearing:
- `include_filter="levels/*.txt"`. Levels are NOT Godot resources, so without
  this line the game ships with no levels at all and dies on the first load.
- `exclude_filter` patterns are matched against the path WITH its `res://`
  prefix, so `tests/*` silently excludes nothing. Write `*tests/*`. Verify by
  listing the pack rather than by eye -- the pack directory stores paths
  without the prefix, so grepping the binary for `res://tests/` answers a
  different question (it finds `.godot/uid_cache.bin`, which names every file
  in the project and is harmless).

The version is `config/version` in project.godot, ONE place: the title screen
draws it in its corner (`inventory_check.gd` asserts the two agree, and that it
is not empty) and the Windows preset repeats it as the exe's file version. Bump
it there before a build you intend to hand out. `config/name` is NOT a place to
tidy: it is the `user://` directory name, so renaming it orphans every player's
campaign.

`icon.png`/`icon.ico` come from `tools/gen_icon.py` -- procedural, per spec §0,
deterministic, regenerate rather than edit.

Platform gotchas, each cost an hour once:
- WINDOWS: unsigned, so SmartScreen shows "Windows protected your PC" on first
  run -- More info, Run anyway. Only a code-signing certificate removes it.
- MACOS: Godot's own ad-hoc signing produced a bundle the kernel SIGKILLs with
  no output at all (even `--version`). `codesign --force --deep -s - Cog.app`
  fixes it. A universal build also requires Rendering > Textures > VRAM
  Compression > Import ETC2 ASTC, which is why that setting is on.
- The exported build cannot be smoke-tested by pointing the editor binary at
  its `.pck` (`--main-pack`): the editor loads C# from the PROJECT, so the
  bridge fails to bind and the failure is an artifact of the test, not a bug.
  Run the exported binary itself, and back up `user://` first -- it is the same
  save directory the real game uses.

# Bug-finding suites (vs. the regression suites everywhere else in tests/)
- `tests/Fuzz.cs` — thousands of RANDOM input streams, invariants checked EVERY
  tick: player in the level, health/magazines in range, guards neither created
  nor destroyed, pack always a valid packing, hash pure. Plus determinism,
  replay round-trip, and item conservation: loot, drop and EQUIP only MOVE
  items, counting what is worn (raw rails included) as a place items live.
  Those streams start with random gear in the bag and assert they really moved
  items: before that they began empty-handed and moved NOTHING, so the
  invariant held vacuously for as long as it existed.
- `tests/Robustness.cs` — adversarial text: every parser truncated at 120
  offsets, byte-flipped, fed every printable char as a grid, handed numbers at
  every integer boundary. The bar is not "does not throw" but "the result can
  still be PLAYED".
- `tests/Exhaustive.cs` — the cross product: all 39,936 weapon × attachment
  loadouts (zero magazines, zero cooldowns, negative cones); every gear item;
  every item × every slot run THROUGH the sim, so `CanEquipMidRun` and
  `StepEquip` cannot drift apart; and every guard posture × task × radio
  purpose × squad/group id, out-of-range values included, plus corrupt path
  indices (~15,000 injected states): none throws, each is back inside the
  rules after one tick. Disabling `NormaliseGuard` fails it, which is how it
  was checked for teeth.
- `tests/Robustness.cs` also runs the guard AI's whole ladder (a fight, a
  compromise, a sweep) on every adversarial level it builds (one byte flip in
  four), and feeds the nav grid, floods and sweep map their edge cases.
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
TAB toggles. 1-9,0 tools, O sweep node (shift+LMB with Chest = objective), LMB paint, RMB
erase, ctrl+Z undo / ctrl+Y redo, S save to levels/, L load next, N new, F2
rename, ENTER playtest, F1 key overlay. Both toolbar rows are clickable.
Arrows/MMB pan, -/= or wheel zoom, F fit, ctrl+arrows resize (see Editor view).

MODES (M cycles): brush, line, rect (outline), filled rect, fill (4-connected
flood), select. `[ ]` brush size 1-9 = brush width, line width, rect wall
thickness (grows INWARD, so the dragged corners stay the outside). alt+LMB is
the eyedropper. Select: ctrl+A/C/X/V, DEL; paste stays up for repeated stamps,
R rotates, H flips, ESC backs out one layer at a time.
- STRUCTURE vs ACTORS. Brushes, shapes, fill and paste STAMP structure
  (`SimBridge.EditorStamp`/`EditorFlood`, glyphs per `EditorStampable`) and
  never toggle, so a stroke that re-crosses an exit keeps it. Spawn, guard and
  waypoint are actors and place ONE per press whatever the mode (a dragged
  waypoint tool used to drop one per cell). `EditorPaint` keeps its old toggle
  semantics for single clicks through the bridge; the harness pins both.
- The clipboard is structure only: actors copy as floor, pasted floor never
  buries an actor, cut leaves actors standing, DEL removes them.
- Every stroke/shape/fill/paste is ONE undo step, and a no-op one leaves none
  (and keeps the redo). Undo no longer re-fits the view unless the size changed.
- `press(cell, erase, shift)`/`drag_to`/`release` take CELLS so
  `editor_check.gd:_check_editor_tools` drives every tool with no cursor.
- Tool codes 0-13 are taken. Index = EditorPaint code UP TO the sweep node:
  10 = objective (shift+chest, no palette entry), so the sweep node is palette
  index 10 (`T_SWEEP`) sending code 11 (`SWEEP_CODE`), and EVERY palette entry
  after it sits one code past its index (`paint_code`): Lamp (B) is index 11,
  code 12; Switch (P) index 12, code 13. A new tool appends code 14 and needs a
  letter key. Editor letters in use:
  A B C D F G H I K L M N O P R S T U V X Y Z. (T / shift+T cycles the level's
  map-kit THEME, one undo step; the header names it. B lamp, P switch,
  D / shift+D ambient, U light preview: see "Lighting".)
- SWEEP NODES (`*`, Guard_AI.md §6.3.1) are an ACTOR tool, one per click, not
  `EditorStampable`: they mark places, so brushes and paste must not spray
  them. Floor to everything but the sweep; they never knock through a wall.
  The validator warns about one no guard can reach.
- ROUTES are edited by INDEX (`EditorRouteMovePoint`/`InsertPoint`/
  `RemovePoint`). With a guard selected, the waypoint tool grabs and drags a
  point it is pressed on, inserts on the route LINE (the loop's closing
  segment counts), and otherwise appends and drags; RMB on a point deletes it.
  G/shift+G cycles guards and centres on him.
- MIRROR (K: off, X, Y, XY) repeats every structure stamp -- strokes, shapes,
  fills, pastes (which therefore land flipped) -- across the level's centre.
  Never actors, and never DEL, which clears only what is selected.
- The issues panel folds (I, or its header). A line naming `guard x` or
  `(c,r)` is a link: clicking it selects/centres there. Other lines paint
  through, since the panel sits over the level.

# Screens and the flow between them
Boot goes to the TITLE (`title_screen.gd`), not into a mission:

    title ── Continue ──┐
          ── New Game ──┴─> STASH ── ENTER ──> mission ── F5/debrief ──> stash
          ── Options ────>  options              │
          ── Level Builder ─> editor             └── ESC ──> title

- TITLE: Continue is DISABLED, not hidden, without `user://campaign.txt` — a
  menu whose rows move with state cannot be learned. New Game wipes money,
  stash and mission history in ONE place (`main.gd:_wipe_campaign`), and over
  an existing save it takes TWO presses (the row arms, moving off disarms), as
  the Options erase does.
- STASH (`stash_screen.gd`): THREE columns -- the CHARACTER (paper doll +
  the rails of one gun), the STASH (grid, and the bag you pack under it), and
  MISSION SELECT. The whole inventory is on one screen; what you carry and
  where you take it are one decision. The MOUSE works the inventory
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
Decided by `main.gd:_in_mission()`, NOT `RunLive` alone: a world is loaded
behind the title and base stash at launch, so RunLive is true before any
deploy, and the first spawns were queued for a pack `_restart()` then cleared.

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
Nine worn slots: helmet, vest, backpack, footware, shirt/chest, arms, primary
weapon, secondary weapon, legs (supersedes rpg_extension_plan §4's seven). Only
FOUR change a stat, because only four have a reader there: the two weapons, the
vest (armour) and the backpack (pack size). Helmet, footware, shirt, arms and
legs are worn, saved, drawn and carried by the sim, change no stat, and are
labelled "cosmetic". Do not give them effects until sim/ can read them.

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

APPAREL (helmet, footware, shirt, arms, legs) lives in `Loadout` as five item
ids and is HASHED. Inert is not absent: an item moving onto the player is sim
state whether or not it does anything, so it must be recorded or the replay
diverges. `Loadout.ToText` carries them; older kits parse them as 0. LEGS came
late: `GearSlot.Legs` was appended with no `Loadout` field, so trousers equipped
in the field left the pack and went nowhere. It is hashed and written (`legs=`)
ONLY WHEN WORN, so goldens and older kit texts did not move.

EVERY `Loadout.With*` must carry EVERY field through. It is a readonly struct,
so each mutator rebuilds the whole thing; a field added without visiting all
nine is silently dropped (that is how `SetAttachment` took a helmet back off).
`Exhaustive.LoadoutMutators` asserts every mutator preserves every field it
does not name, comparing through `ToText` because `Attachment()` masks by the
weapon in hand.

Worn gear cannot be taken OFF in the field: the sim models no unarmed state.
`SimBridge.CanEquipMidRun` is StepEquip's own rule read back;
`inventory_check.gd` asserts the two agree across every item and every slot.

- THE PAPER DOLL. Every worn slot is a box ON its body part (`DOLL_AT`,
  indexed by GearSlot ordinal; `DOLL_LABELS` are the short names): helmet on
  the head, shirt and vest on the torso, arms and backpack on the shoulders,
  secondary and primary in the left and right hands, legs, feet. ONE layout
  for both views -- only `doll_origin()` moves -- drawn by `_draw_doll()` from
  primitives, behind translucent boxes. A new GearSlot needs a DOLL_AT entry
  and a label or it has no box (`inventory_check` asserts the sizes agree).
- E opens THE inventory — there is only one. The doll and the rails left,
  STASH grid in the middle column, MISSION PACK below the grid. Drag gear between the stash
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
  - The bag cannot CHANGE under what is packed in it: `stash.bag_would_hold`
    refuses a smaller bag, or taking it off, while the list will not fit. It
    used to be accepted, and the next `apply_to` pushed what the new bag refused
    back into the grid -- or, with the grid full, nowhere at all.
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
- The pick names a row of the kit ON SCREEN, so the tick resolves the target
  (`NearestLootTarget`) at its TOP, from the state the panel read, before the
  player's move. Resolved after it, a step in the same tick could hand the
  click to the next body over (loot flow §6.3; `Economy` "the loot pick takes
  from the kit on screen").
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
- `SimBridge.Step` takes TEN arguments (…, lootPick, moveTier, dropPick,
  spawnItem, equipPick, doorPick). C# defaults do NOT reach GDScript — Godot registers
  every parameter as required — so a short call fails at RUNTIME with
  "Nonexistent function 'Step'" while `--check-only` passes and the build is
  clean. It has broken call sites FOUR times, once silently aborting a whole
  test function while still reporting zero failures.
  `editor_check.gd:_check_step_arity` now reads the expected count out of
  SimBridge.cs and counts arguments at every `.Step(` in every .gd file; an
  eleventh argument needs no lint change, only the call sites it names.
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

THE GAME IS WON by completing the final mission: extracting WITH the objective
from `missions.gd:FINAL_PATH` (the SHIPPED `res://levels/zz_black_site.txt`, so
an edited copy in user://levels does not count; nor does an editor playtest,
`main.gd:_playtest_run`). `_settle_run` calls `campaign.win()` and the debrief
becomes `_draw_victory_card`: this run, the lifetime campaign, every mission's
runs/completions/best. Winning ends nothing -- ENTER and F5 work as ever.
The lifetime figures (`campaign.STAT_KEYS`: kills, subdues, shots, ticks,
records, earned, recovered, victories, won_on_run) are added by `note_run` on
EVERY settled run, deaths included, one `key N` save line each; an older
ledger loads them as zero. New Game wipes them with the money.

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
The guard ALPHABET is `Level.GuardGlyphs`: 'a'-'z', then the 62 Latin-1 letters
'À'-'ÿ' (U+00C0..U+00FF without × and ÷), so `MaxGuards` is 88. Eight, then
twenty-six, capped how dangerous ANY level could be. It stays inside ONE BYTE
on purpose: the grid crosses to game/ as a byte per cell (`GetGrid`), so a
wider alphabet needs that boundary changed first. Level files are UTF-8
('Ä' is two bytes on disk, one char in the sim); `levelkit.py` writes UTF-8 and
has the same alphabet as `GUARD_GLYPHS`. Records key off `Level.GuardSlot`
(the place in the alphabet), not `Id - 'a'`. game/ asks
`SimBridge.IsGuardGlyph(int)` -- never a 97..122 range test -- and the editor's
issue links match `guard \S`. Loot index space: guards + chests + ground piles
must stay under 255 (`InputFrame.LootPick` is a byte); the Black Site uses 121.

Every level carries TEN. A guard with no route line is a stationary sentry by
design: patrollers set the rhythm on corridors, sentries hold the rooms worth
crossing. `Systems.Playable()` asserts routed guards walk and sentries do not.

`Tune.GuardRecordEvery` tracks the guard count (3 at eight, 4 at ten, 5 at
twenty), so record scarcity stays a decision rather than drifting with a
difficulty change.

# Guard AI v2 — `Guard_AI.md` is the plan, P0-P6 BUILT
The redesign (Relaxed / Curious / Combat / Hunting postures, radio, flanking
squads, paired sweeps) is phased in `Guard_AI.md` §11. Read it before touching
guard behaviour. Built: **P0 pathfinding, P1 postures, P2 radio and backup,
P3 flanking squads, P4 the compromised sweep, P5 presentation, P6 the
bug-finding suites.** P7 (cover) is not built.
Guard AI lives in `sim/SimWorld.Guards.cs` (a partial of SimWorld), the net
in `sim/GuardNet.cs`.
- POSTURE (`GuardState`: Relaxed, Curious, Combat, Hunting, Down, Dead) is how
  alert; TASK (`GuardTask`) is what he is doing. Both ordinals are hashed and
  mirrored in main.gd (`ST_*`, `TASK_*`): append, never reorder. Posture
  rises only through perception and falls only by a timed rule: Curious →
  Relaxed when an investigation finds nothing, Combat → Hunting on contact
  lost. HUNTING NEVER FALLS: a compromised level (`Net.Compromised`, alarm
  level 3, sticky) stays so until the run ends. `Fuzz` asserts all of it.
- Combat entry: meter 100 + LOS, SNAP SIGHT (≤150 px, in cone, 0.2 s), a
  heard gunshot, being hit, a shout, or being dispatched. A guard ALONE (no
  guard within `AllyPathCost` of PATH) radios; one with allies shouts them in.
- THE RADIO is timed (1.5 s, world clock) and interruptible: a guard shot or
  subdued mid-call cuts it (`RadioCut`) and nobody comes. A Combat guard
  keying a radio CAN be subdued from behind; one engaging cannot.
- A shout never freshens intel, and nobody shouts once intel is
  `ContactLostTicks` old. Break either and a lost fight never ends.
- A body is REPORTED by radio; the floor learns nothing until it completes.
- AFRAID (`Actor.FearMt`, Guard_AI.md §4.1) is a 1 s FREEZE LAYERED OVER the
  posture, not a posture: squads, groups, radio and compromise all key off
  posture and must not lose a frightened man. Rolls (sim RNG, only when owed):
  a RELAXED guard who hears the player's shot/blast or is hit
  (`FearGunfireQ8`); anyone who SEES an ally die (`FearAllyDeathQ8`, or
  `FearAllyDeathCombatQ8` if already fighting). Frozen: perceives, never acts.
  `!?` glyph; `GetGuards` stride 13.
- BACKUP SQUADS FLANK: `PlanAssault` gives each member his own A* route with
  `Tune.FlankPenalty` near earlier members' routes, and the shorter walks
  wait (≤ `EtaSyncMaxTicks`) so they break in together. One door means they
  stack; that is correct, not a failure.
- THE SWEEP (`sim/SweepMap.cs`, `Net.Groups`): on compromise, sentries hold
  their posts and MOBILE guards (patrollers, and anyone who was fighting) sweep
  in groups of 1-3: [0] leads facing the way they walk at `SpeedSweep`, [1]
  walks his trail `PairSpacing` back watching BEHIND, [2] watches a flank.
  With 4+ mobile guards the group nearest the exit keeps to it. Groups walk
  to the sweep node with the best staleness-less-distance score (plus the
  focus and authored bonuses), look round, pick again. A man down or pulled
  into a fight leaves his group; a lone survivor folds into the nearest group.
  `Fuzz` forces a compromise in every third stream so the sweep invariants
  are checked against random play, not vacuously.
- PRESENTATION: glyphs `?` `!` `○` (curious, combat, hunting); a filling ring
  over a guard keying a radio; off-screen markers for COMBAT only. The alarm
  HUD element names the level (CALM, SUSPICIOUS, COMBAT, COMPROMISED). Radio
  sounds are heard within `RADIO_HEAR_PX` (420); a call GOING THROUGH and the
  floor being compromised are always told as notices -- that is the fairness
  line. main.gd mirrors `ST_*`/`TASK_*` by hand, and `editor_check.gd`
  asserts them against `SimBridge.GuardStateNames/GuardTaskNames`.
- FOOTSTEPS THROUGH WALLS: `game/footsteps.gd`, PRESENTATION ONLY -- read
  from `GetGuards()` after each tick (`main.gd:_observe_footsteps`), never
  fed back, no hash, no replay token. A guard the player CANNOT see (`visible`
  0) who WALKS lands a ripple every `STRIDE_PX` of distance, alternating feet
  (the zig-zag gives his heading). Earshot `HEAR_PX` × posture loudness,
  shrunk by `TIER_MASK` only while the player is MOVING. A standing sentry is
  silent -- that surprise is the stealth game. Pinned by
  `editor_check.gd:_check_footsteps`, including a real run whose hash matches
  an unlistened one.
- F3 AI DEBUG OVERLAY: `game/ai_debug_overlay.gd`, a pure function of
  `SimBridge.GetAiDebug()` (a sectioned int snapshot). Shows hidden guards, so
  it is off by default and never saved. `editor_check.gd` draws it inside a
  REAL `_draw` against a hand-built snapshot with every section populated;
  that harness now waits a few frames before reporting, for that probe.
- `sim/NavGrid.cs`: derived from the grid, cached on `Level.Nav`, dropped by
  `Build()`, never hashed. Its circle test IS `Geometry.HitsWall` asked of
  nearby cells (`Navigation.MatchesPhysics` asserts they agree exactly), so it
  cannot promise a route `MoveSlide` refuses. Node points are pushed 2 px off
  an adjacent wall, or every two-cell corridor would be sealed (cell centres are
  10 px from a wall; a guard is 11 wide). A one-cell gap is shut, correctly.
  Glass `=` is nav-wall; a door `+` is walkable (guards open doors).
- `sim/PathFinder.cs`: A*, costs 10/14, open list ordered (f, h, cell), so
  ties always break the same way. A goal in another region is refused before
  any search.
- FACING AND TRAVEL ARE SEPARATE. `MoveTo` walks a path and leaves facing
  alone; `LookAt` turns; `Steer` is both, for a guard who looks where he
  walks. Moving more than 90° off facing costs `Tune.BackpedalQ8`.
- Plans are served at the END of the guard pass (`ServePaths`), round-robin,
  at most `Tune.NavSearchesPerTick` A* runs a tick; a straight-line shortcut is
  free. The path's LAST waypoint is always the live goal.
- A stuck guard re-plans round the obstacle; the old ±1.1 rad nudge and its
  RNG draw are gone.
- THE SIM IS TOTAL OVER GUARD STATE. `NormaliseGuard` runs at the top of every
  guard's step and repairs anything outside the rules (`SimWorld.TaskFits`):
  a posture/task pair that cannot occur, a squad or group id naming nothing,
  a radio purpose off a fighting guard, an index outside its list. It counts
  its repairs in `SimWorld.Normalised` (NOT hashed), and `Fuzz` asserts that
  stays ZERO in random play, so it can never quietly cover a real bug. A new
  task means a new line in `TaskFits`, or every guard given it is "repaired".

# Chests
Glyph `C`. Containers of GEAR, as against `$` record caches — they must never
read the same at a glance, hence the warm colour. Contents are bought by the
sim from the loot stream (see "Loot as money"), not authored: a level says
WHERE and HOW MUCH, not what. Stocked AFTER the guard kits, so adding a chest
does not re-roll every body (`Economy.Chests()`).

# Loot as money (`sim/LootTable.cs`)
Every item has a RARITY (`GearItem.Rarity`: common, uncommon, rare, epic,
legendary; each tier dearer on average than the last, and `Loot.Rarities`
asserts it). game/ draws names in ONE palette, `item_catalog.gd`
`RARITY_COLOURS` (grey, green, blue, purple, orange; the objective keeps its
cyan). That covers the stash grid, the pack, worn slots, fitted chips, the loot
panel, the shop, the dev menu and the tooltip, whose title line names the tier
in words too. Rarity is not a stat.

Loot is a POINT BUY in the shop's dollars (`GearCatalog.LootValue`: the price,
or `StarterLootValue` 250 for the not-for-sale Glock). `LootTable.Buy` picks a
tier by weight, then an affordable item in it, until the money or the slots
run out. One of each item per container, and one per body slot for apparel and
packs. PACING: each pick must cost at least half its fair share of what is
left, and the floor HALVES when nothing meets it, so a budget is SPENT rather
than frittered into commons (and a rich guard buys the best gun he can reach).
- CHESTS share the level's `ChestBudget` (`loot: N`, else
  `Tune.LootPerChest` × supply chests). Shares are uneven (a weight of 1-4
  each), the remainder carries on, and a final pass spends what the item cap
  left. Objective sites draw NOTHING. A chest is never empty. `Loot.Budgets`
  asserts every shipped floor holds its budget to within one item.
- LUCK is rolled once per run (`SimWorld.Luck`, 50-150, two draws so 100 is
  typical) AFTER the guards. It changes WHAT the chest money buys, never HOW
  MUCH: `TierWeight` tilts toward legendaries above 100 and toward commons
  below. `Loot.Luck` asserts that lucky runs find rarer things AND that the
  dollars match. The run announces it ("luck 132% — a good day") and the
  debrief repeats it.
- GUARDS each point-buy their own kit with `Level.PointsFor(id)`: a gun with up
  to `GuardGunSharePct` of it (a Glock if nothing else is affordable), then a
  vest `GuardArmourChancePct` of the time, which he WEARS, then apparel,
  packs and attachments. No luck for guards.
- The ASSIGNMENT: `guard_loot: N` sets every guard's points, and
  `kit: a N` sets one guard's (written after the routes, only when it differs
  from the default). In the level editor, `,`/`.` move the selected guard's
  points by $100 (shift $1,000) and `;`/`'` move the chest budget. Each is one
  undo step, and the header shows both totals. A level with none of these
  lines round-trips byte for byte.
- MISSION SELECT shows both figures on each row ("loot $7,200 in chests ·
  $18,000 on guards"), from `LevelSummary` fields [9] and [10]. They are the
  LEVEL's numbers, identical every run; luck changes the contents, not the sum.
- EVERY DEPLOY GETS A FRESH SEED (`main.gd:run_seed`). Every run used the one
  fixed `seed_value`, which made a floor hold the same things forever. The
  replay records the seed, and `-- --seed N` pins it.

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
THIRTEEN: five families plus three specialists. Ordinals are in the replay format and the state hash, so
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
| 10 | Tesla | lightning gun | kills 4 in a chain, 3 charges, 6s recharge, ARCS TO YOU |
| 11 | Frag | grenades | thrown, bounce, 28 fragments that hit you too |
| 12 | AWM | sniper rifle | 160 dmg through up to 3 walls, 1.4s bolt |

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

Exotics are RARE in chests: the legendary tier is 3 in 100 by base weight, and
at $1,800 a chest most shares cannot reach a Vulcan anyway. A Vulcan out of the
first chest would end the economy the shop exists to create.

THE SPECIALISTS each break one rule every other weapon keeps, via a trait on
`WeaponSpec` that `With()` carries through untouched (no attachment buys it
off), and a `BulletKind` on the projectile (hashed ONLY when not `Round`, so
runs without them hash as before). `WeaponCatalog.TraitOf` is the tooltip line.
Constants are `Tune.Arc*`, `Tune.Frag*`, `Tune.Grenade*`, `Tune.WallPierceLossQ8`.
- TESLA (`ArcTargets` 4): the bolt is a projectile; its impact runs
  `SimWorld.Discharge`. At EVERY node the PLAYER is checked first — within
  `ArcPlayerReach` (110px) with a clear line and the arc goes to them, lethal
  through any vest, and the chain ends. Otherwise the nearest standing guard
  within `ArcReach` (200px) over Opaque. A WALL strike grounds: it can still
  reach you, never a guard. Nodes log as `ArcJump` in order (Value = hop),
  which is how main.gd draws the chain.
- FRAG (`Grenade`): `BulletSpeed` is the throw, `BulletTicks` the fuse. Thrown
  from the player's CENTRE (a muzzle can be inside the wall). Bounces off
  Opaque, shatters glass, touches no body; aim held is a half-speed lob. On the
  fuse, `Blast` lays `FragCount` fragments evenly round the circle (the choke,
  all the way round); `BulletKind.Frag` hits guards AND the player. Heard at
  `BlastHeardRadius` AT THE BLAST POINT, so it doubles as a distraction.
- AWM (`WallPierce` 3): a `Pierce` round counts a wall on the way IN (`InWall`),
  loses 30% there, logs `WallPierced`; the fourth wall stops it. One wall
  leaves enough to kill anything but heavy plate. Hip-fired it is 0.11 rad
  wide; `AimSpreadQ8` 20 makes it a needle.
`tests/Specialists.cs` runs every one of those claims through the sim.

# Loadout menu
Q opens it. Up/down pick a row, left/right change it, E or ENTER stages the kit
and closes — it does NOT restart; F5 deploys with it. Weapon, armour and six
attachment slots; slots the equipped weapon lacks are greyed, not hidden.

# Guards fire their own guns
A DELIBERATE DEVIATION, on request: guards used to fire a flat rifle (50
damage, 2200 px/s, one aimed round per 0.8 s, `Tune.GuardDamage` et al, all
deleted). Now `Actor.Weapon` is the gun his loot roll bought
(`SimWorld.ArmGuard`, from the first weapon in his kit, Glock if none), and
`SimWorld.GuardFire` fires it through the player's machinery: damage, pierce,
pellets and choke, bullet speed and life (his shots still call no
GunshotHeard, as before), heat and its own decay, magazine and reload, the Vulcan's spin-up,
AWM wall pierce, Tesla bolts (lethal on a hit; a grounded strike arcs to a
player within `ArcPlayerReach`, never along guards), and FRAGS (thrown from
his centre, fragments hit everyone).
- BURSTS: as many rounds as the weapon cycles in `Tune.GuardBurstTicks` (0.4 s)
  -- AK 3, MP7 6, Vulcan 12, bolt/pump 1 -- then `EngageCooldownTicks` and a
  re-aim. `SimWorld.GuardBurst(spec)`.
- CONE: the weapon's own three terms plus `Tune.GuardSpreadPenalty` (625), set
  so a cold AK guard draws exactly the old flat 938-BRAD cone.
- A grenadier never throws inside `Tune.GuardGrenadeMinDist` (260 px, past the
  ~230 px fragment reach) and holds his engage band out beyond it.
- `StepGuardWeapon` runs every tick after behaviour: heat, reload, spin-down,
  burst reset, and a top-up reload out of combat. `NormaliseGuard` repairs an
  out-of-range weapon, magazine, burst, reload or spin.
- `GuardShot` carries the weapon ordinal in Value. Weapon and BurstShots are
  hashed (goldens re-baked). `tests/GuardWeapons.cs` pins all of it, every
  weapon through a real guard.
- Guard MOVEMENT ignores weapon weight (`SpeedDelta`), and guards get no
  headshots or aim lock. Tests that drive a round into the player directly use
  a local fixture round (Health.cs), not a guard constant.

# Guards, health and armour
Guards spawn on `Tune.GuardHealth` (60), NOT `Tune.BaseHealth` (the player's
200): the interesting question about a guard is whether he wears a plate, not
how deep his pool is.

The vest in a guard's rolled kit is the vest he is WEARING — `RollGuardKits`
sets `Actor.Armour`/`ArmourMax` from the same catalogue entry it adds to the
body, so the plate stopping your rounds is the plate you strip off the corpse.
~60% buy one (`Tune.GuardArmourChancePct`; which one depends on his points). An AK drops a bare guard in
one round and takes three or four through heavy plate.

The answers to a plate are AP ammo and a locked headshot (headshots ignore
armour entirely). Both tested in tests/Health.cs.

Armour is drawn two ways deliberately: the SILHOUETTE (shoulder pads and chest
plate, scaled by tier) says "armoured" from across the room before you commit;
a steel BAR above the awareness bar says "you are getting through it" once you
have. `GuardArmourHit` sparks cold instead of spraying blood.

# Glass and doors
Two glyphs for cells that CHANGE during a run: `=` glass and `+` door
(`PanelKind`). `Level.MergePanels` merges runs of one glyph into PANELS,
horizontal first, capped at `GlassPaneCells` 4 / `DoorLeafCells` 3: a long
window breaks a section at a time, and a two-cell doorway is ONE door. A
one-cell door is useless (22 px guard, 20 px cell), so the editor warns about
it. `Level.Panels` is glass then doors, each in row-major order, and that INDEX
is the replay format. Panel cells are never in `Level.Walls`.

| | walking | sight / light | rounds |
|---|---|---|---|
| glass, whole | blocks | passes | SHATTERS it and flies on |
| glass, broken | passes | passes | passes |
| door, shut | blocks | blocks | stops |
| door, open | passes | passes | passes |

- `SimWorld.Solid` (walls + whole panes + shut doors) is what MOVES use;
  `SimWorld.Opaque` (walls + shut doors) is what SEES and what stops rounds.
  NEVER reach for `Level.Walls` in sim code that runs mid-run. Both are rebuilt
  by `RebuildBlockers` when a panel changes, and with no panels they ARE
  `Level.Walls` (same array), which is why the feature moved no golden hash.
  `Panels.NoPanelsNoChange` pins that. The bridge's vision polygon and guard
  visibility read `Opaque` too.
- GLASS IS A SOUND CUE. A pane shattering (`BreakGlass`) raises every guard in
  `Tune.GlassNoiseRadius` (340 px) to `GlassAwareness` (70, past AwHunt, short
  of Engage) with the PANE as his last known position, not the shooter. It is
  wider than any silenced report, so a quiet gun through a window is still
  loud, and a window across the room is a lure. No floor alarm from glass
  alone. `Projectiles` clears the pane in the SAME substep, so a shotgun blast
  breaks it once. Hands do not reach through glass: subduing and looting ask
  `PanelBetween`.
- DOORS ARE RECORDED INTENT: `InputFrame.DoorPick` is 0 or the panel index + 1,
  a `uN` replay token, and `SimBridge.Step`'s TENTH argument. The sim refuses a
  pick that is not a door within `Tune.DoorReach` (30 px). A close is REFUSED,
  out loud (`DoorBlocked`), when anyone, standing or fallen, is in the doorway.
  A door is heard: `DoorHeard` ADDS awareness with falloff inside
  `DoorNoiseRadius` (halved at the stealth tier), capped at NoiseCap.
- GUARDS OPEN DOORS and leave them open (`OpenDoorAhead`, called in `Advance`).
  A door standing open that you left shut is the tell that someone has been
  through. The nav grid treats `+` as walkable and `=` as wall.
- G IS ONE KEY FOR TWO VERBS. `main.gd:door_pick_for` is the pure rule: a
  PRESS (not a hold) picks the door in reach unless a loot target is nearer.
  While that press is held, `_g_on_door` keeps the loot panel down. The prompt
  asks the same function, so it cannot name the other thing.
- Events APPENDED: `GlassBroken`, `DoorOpened`, `DoorClosed`, `DoorBlocked`
  (Value = panel index; Heading 1 = a guard moved it). game/ mirrors them
  (`EV_*`, `EV_LAST`). Audio: `GLASS` (scales with the world clock like the
  other impacts), `DOOR_OPEN`, `DOOR_CLOSE`. A guard's door is heard within
  `DOOR_HEAR_PX` and pops "a door opens" if in view.
- `SimBridge.GetPanels()` stride 6 (kind, x, y, w, h, flags: bit 0 open, bit 1
  vertical) is read EVERY tick, unlike `GetWalls`. `LevelSummary` gained glass
  and door counts at [7] and [8]. Readers check `size() >= 7`, so the first
  seven never move.
- `tests/Panels.cs` pins all of the above. `Fuzz` fires random door picks on
  panel levels (only there, so older streams are unchanged) and asserts nobody
  ever stands inside a shut door or a whole pane.

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
- `meridian_glasshouse.txt` 64x40: the GLASS level. Offices with glass fronts
  on an atrium, a glasshouse in the middle, a security office watching the
  atrium through a window. The objective is in a server hall reached only
  through doors. `python3 tools/gen_meridian_glasshouse.py`.
- `vault_row.txt` 72x44: the DOORS level, the glasshouse's opposite. Nearly
  every room is behind a door, stacks make two-cell aisles, and there is ONE
  window: the vault supervisor watching the antechamber. The objective is
  behind a three-cell vault door. `python3 tools/gen_vault_row.py`.
- `zz_black_site.txt` 168x128 (exactly `Level.MaxCells`): Cognitohazard Black
  Site, the FINAL level -- `zz_` because mission select sorts by filename. A
  complex dug into a mountain: the surface building is open to the west, south
  and east yards, and the vault sits at the back of the carved deep site with
  solid rock behind it. Three ways in (two service tunnels, the portal), all
  meeting in the gallery before the antechamber, vault hall and cage. FOUR
  exits, 78 guards (the 26 letters plus 52 past 'z', mostly mirrored pairs),
  `loot: 1000000` over 42 chests (~75% of chest items legendary) and
  `guard_loot: 7000` (every guard a legendary gun -- Tesla, AWM, Arc Lance,
  Vulcan -- which he FIRES; ~60% plated), officers more via `kit:`. `python3 tools/gen_black_site.py`.
- New generators build on `tools/levelkit.py`, which REFUSES to write a level
  that fails its checks: every door 2-3 cells and set in a wall with floor on
  both faces, every window set in a wall, every guard and waypoint on a clear
  3x3 (a guard is 22 px, a cell 20), and spawn, exit, objective, chests and
  caches reachable WITHOUT breaking glass. Both levels are in `Fuzz` and
  `Navigation`'s level lists.

SEVERAL EXITS: `Level.Exits` is one rect per 8-connected blob of 'X' (row-major
by first cell, capped at `MaxExits` 8; blobs past it are floor and the editor
warns). Reaching ANY ends the run. `Level.Exit` is the first, which on a
one-exit level is exactly the old bounding box, so no golden moved and the grid
(already hashed) is the only state. The sweep's exit group keeps to
`Level.WatchedExit`, the exit nearest the objective. `ExitReachable(which:)`
asks one exit; the validator warns per sealed exit. game/ reads
`SimBridge.GetExits()` (stride 4); `level_art.gd` frames each blob.
`levelkit.write` takes `loot=`, `guard_loot=`, `kits=` and writes them where
`Level.ToText` would. Tests: `Systems` "multiple exits", `level_art_check`.

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
- Fixed `PLAY_ZOOM` (1.0, the design resolution 1:1; it was 1.35 and was
  zoomed out on request), scrolling to cover the level; it does not back off
  to fit the floor, which at 0.6 was too far out to read a fight. The reference
  level is exactly one view, so it no longer scrolls; everything larger does.
  A level SMALLER than the view is pulled in up to `MAX_ZOOM` 2.0; `MIN_ZOOM`
  0.6 is a floor nothing reaches.
- Dilation pulls the view back (`DILATE_ZOOM`), or rounds at the retuned muzzle
  velocities fly off screen during the one mechanic built around watching them.
- Guards in COMBAT get an off-screen edge marker (red when engaging).
  Relaxed, curious and hunting guards get nothing — that asymmetry is the stealth game.

# Level art — the Astra map kits
`game/level_art.gd` dresses the level in a kit from `assets/tilesets/`
(`Astra Assets/TILESETS.md`): `industrial` or `scientific`, chosen by the
level's `theme:` line. PRESENTATION ONLY and DERIVED: built from the live grid
(`SimBridge.GetGrid`) in `main.gd:_refresh_level_cache`, it ADDS NO SHAPE.
`tests/level_art_check.gd` asserts the contract over every shipped level and
300 fuzzed grids:
- Whatever LOOKS solid is drawn only on '#', and every '#' looks solid. Walls
  are one tile per cell by N=1 E=2 S=4 W=8 connection mask (doors and panes
  count as connected), over a fill of the kit's rim tone -- the tiles are 14 px
  wide and a wall blocks 20 -- with the notch between arms patched inside solid
  masses (`CORNER_PATCH` 5: the notch AND its bevel, or masses show a grid).
- FREE-STANDING BLOCKS become equipment: a '#' component touching neither the
  border nor a door/pane, an exact rectangle with even sides (2x2 props), or a
  one-deep bench 2 or 4 long (`NARROW_PROPS`). On a plinth covering the whole
  footprint, so the collision is the box you see. Odd blocks stay wall.
- Walkable cells get only flat things: a floor per ROOM (cores >= 2 cells from
  any wall, so doorways split them; long thin rooms get corridor floors),
  corridor LANES (3-5 wide, straight runs of `LANE_MIN` 6+), exit brackets.
  A conduit runs the border ring.
- NO DECORATIVE LIGHT FIXTURES, ever: light is a stealth axis (lighting plan).
  `draw_lamp(canvas, pos, lit, broken, vertical)` draws the kit's strip light
  where the SIM has a lamp; the harness fails on any `light_*` command.
- The floor is drawn once LIT; `draw_unlit` then darkens everything outside the
  vision polygon with one triangle array (the quads between consecutive rays
  out to `UNLIT_FAR`, possible because the polygon is an even fan), and repaints
  C_BEYOND past the level edge. `UNLIT_ALPHA` 0.42 keeps the old ~0.59
  unlit/lit ratio. Walls, props and the conduit draw AFTER it, at constant
  tone, as walls always have.
- Textures: `get_image()` off the imported PNGs, turned/flipped variants built
  on first use, mipmapped, each wrapped in a CanvasTexture so filter and repeat
  are PER TEXTURE (main's canvas item also draws HUD text). Emission maps
  become GLOW textures (hue at full brightness, brightness as alpha, half res):
  alpha blending approximating TILESETS.md's "add after diffuse" on a canvas
  item with one blend mode. Normal maps are unused until something lights them.
- `ready` false (no kit, headless dummy renderer) -> main.gd's procedural floor,
  walls and swing doors draw instead. Glass and the EXIT stay procedural
  (art pipeline §0.2). The kit's doors are a SLIDING pair, so a 3-cell door
  is its 2-cell art stretched 1.5x.
- `theme:` is `Level.Theme`, cleaned by `Level.CleanTheme`, written after
  `name:` only when set (older levels round-trip byte for byte), and NOT
  HASHED. Unknown or missing -> `DEFAULT_THEME` industrial. Generators pass
  `theme=` to `levelkit.write`. Shipped: substation_4, terminal_twelve,
  vault_row industrial; relay_nine, meridian_glasshouse, zz_black_site
  scientific.
- The manifests are JSON, NOT resources: `export_presets.cfg` includes
  `*assets/tilesets/*.json`, or a shipped build silently draws primitives.

# Editor view
Levels no longer fit one screen, so the editor has its own view: arrows or
middle-drag pan, -/= or wheel zoom, F fit (automatic on open, load, new, and an
undo that changes the size). The editor's field is 540 tall, not main's 560:
its strip below holds three rows.
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
TAB editor, G hold to loot a body/chest/floor gear OR tap to open/close the
door or flip the light switch in reach (the nearer wins), X swap weapon, E inventory
(FIELD VIEW during a mission, full stash + mission select outside one), B shop,
F4 HUD layout editor, F8 dev menu, F3 AI debug overlay. `I` is unbound.
Function keys: F2 rename (editor only), F3, F4, F5, F8, F9, F11. The `sneak` action was
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
- Art per `cognitohazard_art_pipeline.md`: deterministic scripted assets, with procedural fallback retained. (spec §0)

# Sim-layer rules (spec §3.3) — build-breaking, not style
Forbidden anywhere in `sim/`: randf/randi, Time.*, OS.*, Engine.get_frames_*,
Input.*, any Node, any delta, and any iteration over a hash-ordered collection
whose order affects state. Guards iterate by index, always.
All hash-feeding state is integer: positions 1/256 px fixed point, angles BRAD,
awareness in tenths, record charge in frames.

The TEXT formats (level, replay, loadout) are CULTURE-INVARIANT: numbers parse
through `sim/Invariant.cs` and format through an invariant `Append`, strings
compare ordinally. The machine's culture writes a negative number without '-'
in sv-SE (U+2212), fa-IR and ar-SA, so a replay recorded there dropped every
left/up frame when read anywhere else. `.editorconfig` makes CA1304/1305/1310/
1311 errors for sim/ (the harness builds with warnings as errors), and
`SimLint` forbids `int.TryParse` & co. outside Invariant.cs, which CA1305 does
not see. `Robustness` "cultures" round-trips every format through a BUILT
hostile culture, so it has teeth on a machine with no locale data.
`Replay.FromText` caps the frames it will expand at `Replay.MaxFrames` (a day
at 60 Hz): one `x999999999` token used to allocate 16 GB.

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
- Relay Nine's guard `f` spawned in a one-cell gap, overlapping walls N and S,
  so `MoveSlide` refused every step and he stood frozen from the port on. Moved
  one cell east. (`Navigation`: "every guard and the spawn start where a body
  can stand", and "every patrolling guard completes a lap".)
- Trousers equipped in the field were DESTROYED: `GearSlot.Legs` had no
  `Loadout` field, and `Exhaustive.EquipAgreement` looped slots 0..7 so the one
  pair never ran. It loops `GearCatalog.SlotCount` and asserts no equip creates
  or destroys an item. (`Exhaustive`, `Fuzz` conservation, `Economy`.)
- `EquipAttachment` read the MASKED rail to decide what to hand back, so fitting
  over a rail the gun in hand lacks overwrote what the last gun left there.
  Reads the raw set. (`Economy` "fitting attachments in the field".)
- `Replay.FromText` allocated whatever a run length asked for (OutOfMemory on
  `x999999999` where the OS would not page it), and a walk-tier frame carrying
  the legacy FSneak bit came back as stealth. (`Robustness`.)

Deliberate deviations, each on request and each commented where it lives:
- `Tune.AimLockTicks` is 6 (0.1 s), not the browser's 90: 30 (0.5 s) first,
  then a fifth of that, both on request. It is exact -- it used to take one
  tick longer than the constant said. At 0.1 s nearly every AIMED round is a
  headshot, and a headshot ignores armour.
- `Tune.BaseHealth` (the player's pool) is 200, doubled on request from RPG
  plan §2's 100. That plan's lethality brief (1-2 rounds unarmoured, 4-5 in
  the best armour) is 3-4 and 5-7 now; `Health`'s shots-to-kill table is
  re-pinned to it, and a full shotgun blast no longer kills an unarmoured
  player outright (two do). The Tesla's arc to its shooter stays lethal
  (`ArcSelfDamage` 999). Goldens re-baked; older replays diverge.
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
- Guards PATHFIND (spec §10.1 said report, do not build; it was asked for, see
  `Guard_AI.md`). They travel toward a waypoint rather than along their
  facing, and a stuck guard re-plans instead of taking a random nudge. It moves
  the hashes, and replays recorded before it diverge. The §8.6 detection curve
  is untouched (the measured sentry never moves).
- Spec §8.1's state table is REPLACED by postures and tasks (`Guard_AI.md`).
  A heard gunshot is Combat, not awareness 92; a body is a radio report, not a
  520 px broadcast; callouts put listeners in Combat. The §8.6 curve holds
  EXACTLY at 250/380 px; the 120 px row is snap sight (0.20 s in every
  stance), re-baselined in `Systems.DetectionCurve` on request.

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
  Both views now lay the slots out on the paper doll (see "THE PAPER DOLL");
  the worn-item baseline still comes from the BOX.
- `sim/Kit.cs` — nine slots, its own `PackGrid`, the attachment set, the active
  hand, hashing, `PackFullPercent()`, and `ToLoadout()`. Keep the layering:
  **Kit = what is worn, as item ids. Loadout = what that means, as specs.**
  NOT WIRED IN YET — nothing constructs one. It predates per-weapon rails: it
  still has ONE attachment set, and needs the holster's second before wiring.
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

FIXED (§6.1): extraction now brings WORN gear home. `main.gd:_restart` (and
`_start_playtest`) snapshot the sim's worn kit with `stash.sim_kit()` on the
tick a run begins; `_settle_run` calls `stash.reconcile_worn(deployed, now)`
before banking the pack, then `apply_to` so F5 deploys what you now wear.
Only slots/rails that DIFFER are taken from the sim -- an untouched slot keeps
the stash's answer, because an empty primary deploys as the default Glock and
a rail the gun lacks deploys as nothing; copying the sim wholesale would mint
the one and destroy the other. Rails are read UNMASKED
(`SimBridge.GetRailsSim`), since they stay with the hand. Pinned by
`inventory_check.gd:_check_field_equip_comes_home`, which drives the real
bridge: deploy, equip in the field, reconcile, bank. Known edge: deploying with
an EMPTY primary and equipping over the phantom Glock banks that Glock -- a
free item, price 0, fence value 0.

# Campaign plan
`cognitohazard_campaign_plan.md`. §2 (mission select, threat, payout
multiplier, per-mission history) is BUILT; §2.1 was decided in favour of
deriving from the level. UNBUILT: §1's selling from the stash and stash
capacity as a purchase, and §2.4's carrying-too-little warning.

# Lighting — `cognitohazard_lighting_plan.md`, L0-L3, L5, L6 BUILT
Light is a STEALTH AXIS: a dark corner hides you, a lit corridor exposes you,
and the player can change which is which. A DELIBERATE DEVIATION from spec §9
("concealment comes from walls only"), on request. Every constant is in
`Tune`'s "lighting" block, marked NEW NUMBERS. NOT built: L4 (the player's own
torch as a toggled cone; the flashlight rail is still the old flat
`DetectionMul`) and the sweep scoring dark nodes higher (§8).
- THE IDENTITY RULE. A level with no `ambient:` line (or `ambient: 100`) has
  `SimWorld.Light == null`, and every lighting branch sits behind that null, so
  a lit level runs exactly the code it ran before: goldens and the §8.6 curve
  did not move. `Lighting.Identity` pins it. Hashing follows the panels' rule:
  lamps hashed only when there are any, the flash only when `Light != null`.
- LEVEL FORMAT: `ambient: N` (percent, clamped 0-100; garbage is "not
  authored"), recognised in any mode like `loot:`, and written only when
  authored. `L` lamp, `S` light switch: both FLOOR to everything (walls, nav,
  sight, rounds); `Level.Lamps`/`Switches` are row-major and that index is
  their identity. The HeaderComment gained "L lamp  S switch" and every level
  file's header line with it.
- `sim/LightMap.cs`: Q8 per cell = ambient + every lit lamp, each lamp's disc
  (`LampRadius` 140 px, linear falloff) CAST WITH `Geometry.ClearLine` AGAINST
  `Opaque` from the lamp to each cell centre, so light goes exactly where sight
  goes: glass passes it, a shut door keeps it in. DERIVED, never hashed. Opaque
  cells hold 0 and are left out of `LightAt`'s bilinear sample (walls must not
  bleed light through to their far side). Rebuilt on events only:
  `RebuildBlockers` -> `SyncLight` re-casts just the lamps near a cell whose
  opacity flipped; a lamp breaking or switching only re-sums.
  `SimWorld.FreshLight()` builds one from scratch; `Lighting` and `Fuzz` assert
  the incremental map always equals it.
- PERCEPTION (`Perception.InLight`/`DarkReach`/`VisQ8`, all the identity at
  light 256). `PlayerLightQ8` is computed ONCE a tick before the guards look
  (map, raised by the player's own flash and by any guard torch beam they stand
  in). Then: inside `DarkSeeRange` (60 px) light is ignored; beyond the range
  shortened toward `DarkSightRange` (180 px in pitch dark) he cannot make you
  out AT ALL (this is what lets a lost fight be broken off in the dark);
  between, q is scaled by `VisFloor` (0.28) up to 1 BEFORE the movement /
  stance / alert multipliers. Snap sight's range shrinks the same way to
  DarkSeeRange; a body is found within `BodyRange` shortened to `BodyDarkRange`.
- THE MUZZLE FLASH (`MuzzleFlash`): firing on a lighting level lights the
  player for `FlashTicks`, and every non-Combat guard facing it (±90°) with a
  line to the muzzle within min(2 x report, `FlashSeenRange` 700) gets a
  Notice at `FlashAwareness` there. Scaled by the report, so a suppressor hides
  the flash as it hides the bang (a Welrod's reaches 260 px).
- LAMPS ARE GLASS WITH A BULB IN IT: appended to the breakables array after the
  panes (`RebuildBreakables`; `_glassPanel` holds -(lamp+1)), so one `Glass`
  impact kind covers both. A grenade ROLLING is passed only the panes (`panes`
  argument) and goes under a lamp; its fragments do break them. `BreakLamp`:
  never relights, heard at `LampNoiseRadius` (220, a window is 340).
- SWITCHES share `InputFrame.DoorPick` and its `u` token: pick =
  Panels.Count + switch index + 1, so no new field, no 11th Step argument, and
  a level without switches draws the same streams. `NearestUse()` is doors
  and switches; `GetDoorTarget` gained field [5] (0 door, 1 switch). A switch
  controls every lamp in its ROOM (`Level.RoomOf`: 4-connected, bounded by
  walls, glass and doors), no wiring. It darkens the room if anything is lit,
  lights it otherwise, clicks (`SwitchNoiseRadius`), and anyone in the room or
  with a line to one of its lamps comes to look (`LightsNoticed`) -- and on
  arriving turns it back ON (`TryRestoreLights`, from `Behave_Investigate`).
  Only a shot-out lamp stays dark.
- GUARD TORCHES (`TorchOn`, derived from posture, so unhashed): lit in Combat
  and Hunting on any lighting level, and ALWAYS on a level at or below
  `GuardTorchAmbient` (40%). A 0.35 rad, 300 px cone; standing in any beam
  lights you (`TorchLightQ8`). Beams are drawn even for guards you cannot see.
- WHAT THE PLAYER SEES follows the guards' rule (`SimWorld.PlayerSeesQ8`, sight
  430 + flashlight): 0 is not drawn; below `SEE_OUTLINE` an outline; below
  `SEE_CLEAR` dimmed, meter shown, plate hidden. A torch or a shot gives a guard
  away. Read through `SimBridge.GetGuardSight()` (stride 2: seen, torch), a
  SEPARATE array: GetGuards' stride 13 is indexed by hand in footsteps and the
  AI overlay and did not move.
- PRESENTATION (main.gd). `_draw_darkness` draws `GetDarkness` (RGBA8 at TWO
  texels per cell: a wall's texels take the light of the floor they FACE, so
  a lit room does not glow through its walls) as a night-blue overlay whose
  alpha rises as light falls, capped at `DARK_MAX` (display floor: pitch dark to
  the sim is still a readable floor). At alpha a a normal blend IS a multiply
  by (1 - a), so no material and no node. LINEAR via a `CanvasTexture`, whatever
  the canvas filter. Uploaded only when `LightVersion` moves; that version is
  salted per world, since every restart's map starts again at 1. Order: level
  and items, DARKNESS, lamps (`_art.draw_lamp` or primitives), beams, guards,
  player. HUD element `light` (default 716,464): `PlayerVisQ8` and nothing else.
  Audio `LAMP` (scales with time) and `SWITCH`. Events APPENDED: `LampBroken`,
  `LightsOn`, `LightsOff` (Heading 1 = a guard threw it). Mission select says
  "dark, N% light" (`LevelSummary` [11] ambient, [12] lamps); threat ignores light.
- EDITOR: B lamp, P switch (fixtures, one per click, never stamped, never
  through a wall), D / shift+D ambient in tens (past 90 removes the line:
  fully lit), U toggles the preview -- the edit buffer through the sim's own
  `LightMap.AtRest`. The validator warns about lamps on a lit level and a
  switch with no lamp in its room, and says when the exit or objective is
  pitch dark.
- `vault_row_night.txt` (`tools/gen_vault_row_night.py`, which imports
  gen_vault_row's grid -- that script now writes only when run): Vault Row at
  25% with 20 lamps and 8 switches, so every guard carries a torch. It is in
  the Fuzz, Loot and Navigation level lists. `levelkit.write(..., ambient=N)`
  refuses lamps on a lit level and a switch that is not against a wall or
  wired to nothing.
- Tests: `tests/Lighting.cs` (every rule above), `Fuzz` (map equals a fresh
  build, broken lamps dark, light in range; asserts it reached a lit level),
  `editor_check.gd:_check_lighting`, `audio_check.gd`.

# Camera plan
`cognitohazard_camera_plan.md` — BUILT, phases 1-8, except phase 2 was
implemented as a draw transform rather than a Camera2D + CanvasLayer split, so
no scene change was needed. See "Levels and the camera" for the contract.
