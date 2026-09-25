extends Node2D

## Presentation and input (spec §1.1, §9).
##
## This layer READS sim state and never writes game logic. Every position it
## receives is sim fixed-point (1/256 px); FX is the divisor back to pixels.
## Nothing here decides an outcome — it draws one.

const FX: float = 256.0
const FIELD_W: float = 960.0
const FIELD_H: float = 560.0
const HUD_H: float = 60.0

const VISION_RAYS: int = 400
const VISION_RADIUS: int = 430

# Guard POSTURES, mirroring Sim.GuardState ordinals (Guard_AI.md §2).
const ST_RELAXED: int = 0
const ST_CURIOUS: int = 1
const ST_COMBAT: int = 2
const ST_HUNTING: int = 3
const ST_DOWN: int = 4
const ST_DEAD: int = 5

# Guard TASKS, mirroring Sim.GuardTask ordinals -- only the ones drawn.
const TASK_ENGAGE: int = 7
const TASK_RADIO: int = 10

# Event kinds, mirroring Sim.SimEventKind ordinals.
const EV_PLAYER_SHOT: int = 0
const EV_GUARD_SHOT: int = 1
const EV_DRY_FIRE: int = 2
const EV_RELOAD: int = 3
const EV_WALL_HIT: int = 4
const EV_GUARD_KILLED: int = 5
const EV_PLAYER_KILLED: int = 6
const EV_SUBDUE: int = 7
const EV_PICKUP: int = 8
const EV_DEGRADE: int = 9
const EV_DESTROY: int = 10
const EV_NOTICE: int = 11
const EV_ALERT: int = 12
const EV_BODY_FOUND: int = 13
const EV_EXIT: int = 14
# Appended at milestone 8. New kinds go on the END of Sim.SimEventKind; the
# startup guard below asserts the count still matches.
const EV_PLAYER_HURT: int = 15
const EV_ARMOUR_BROKEN: int = 16
const EV_GUARD_HURT: int = 17
const EV_AIM_LOCKED: int = 18
const EV_AIM_LOST: int = 19
const EV_HEADSHOT: int = 20

## APPENDED at the inventory pass. sim/EventLog.cs appends new kinds to the END
## for exactly this reason: these ordinals are mirrored by hand, so inserting a
## kind mid-enum would silently remap every sound and effect.
const EV_LOOTED: int = 21
const EV_PACK_FULL: int = 22
const EV_WEAPON_SWAPPED: int = 23

## Appended with armoured guards. New kinds go on the END, always.
const EV_GUARD_ARMOUR_HIT: int = 24
const EV_GUARD_ARMOUR_BROKEN: int = 25

## Appended when gear could be dropped. New kinds go on the END, always.
const EV_DROPPED: int = 26

## Appended with the developer menu's mid-run spawn. New kinds go on the END.
const EV_SPAWNED: int = 27

## Appended when gear could be put on mid-mission. New kinds go on the END.
const EV_EQUIPPED: int = 28

## Appended with glass and doors. New kinds go on the END. Value is the panel
## index; for the door kinds, heading 1 means a GUARD moved it.
const EV_GLASS_BROKEN: int = 29
const EV_DOOR_OPENED: int = 30
const EV_DOOR_CLOSED: int = 31
const EV_DOOR_BLOCKED: int = 32

## Appended with the specialist weapons.
const EV_WALL_PIERCED: int = 33
const EV_ARC_JUMP: int = 34
const EV_GRENADE_THROWN: int = 35
const EV_GRENADE_BOUNCE: int = 36
const EV_BLAST: int = 37

## Appended with Guard AI v2's radio. Value is the purpose: 1 backup, 2 report.
const EV_RADIO_START: int = 38
const EV_RADIO_SENT: int = 39
const EV_RADIO_CUT: int = 40
const EV_COMPROMISED: int = 41

## Appended with fear (Guard_AI.md §4.1). Value: 1 gunfire, 2 an ally's death.
const EV_AFRAID: int = 42
## Appended with lighting. LampBroken's value is the lamp; LightsOn/Off are at
## the switch, heading 1 when a guard threw it.
const EV_LAMP_BROKEN: int = 43
const EV_LIGHTS_ON: int = 44
const EV_LIGHTS_OFF: int = 45

## The last mirrored kind. The startup and per-tick ordinal checks compare the
## sim's count against this, so a new kind moves ONE line, not two.
const EV_LAST: int = EV_LIGHTS_OFF

## Ints per round in SimBridge.GetBullets(): x, y, heading, fromPlayer, speed,
## kind, lifeTicks. Kind mirrors sim BulletKind by ordinal.
const BULLET_STRIDE: int = 7
const BK_ARC: int = 1
const BK_GRENADE: int = 2
const BK_FRAG: int = 3
const BK_PIERCE: int = 4

## Ints per panel in SimBridge.GetPanels(): kind, x, y, w, h, flags.
const PANEL_STRIDE: int = 6
const PANEL_GLASS: int = 0
const PANEL_DOOR: int = 1

## A guard's hand on a door is heard only this close. On a floor of twenty-six
## guards, every door on the level creaking at full volume is noise, not news.
const DOOR_HEAR_PX: float = 420.0

## A radio is heard this close, like a door: the squelch of a call being made
## across the level is not something the player can hear. That a call WENT
## THROUGH is always told, as a notice -- that is the fairness line
## (Guard_AI.md §0, "glyphs + radio cues").
const RADIO_HEAR_PX: float = 420.0

## Alarm level names, by AlarmState level (Guard_AI.md §9.2). 3 is sticky.
const ALARM_NAMES := ["CALM", "SUSPICIOUS", "COMBAT", "COMPROMISED"]

## F3: the AI debug overlay. Draws every guard's state, task, path, squad and
## sweep group, and the sweep map's staleness -- including guards the player
## cannot see, which is why it is off by default and never saved.
var _ai_debug: bool = false
var _task_names: PackedStringArray = PackedStringArray()
var _state_names: PackedStringArray = PackedStringArray()
var _dbg_intel_age: int = -1
var _dbg_compromised: bool = false

## Ints per guard in SimBridge.GetGuards(). Named rather than written out at
## each call site: it was spelt 8 in two places and widening it silently turned
## the startup guard count into a fiction.
const GUARD_STRIDE: int = 13

@export var level_path: String = "res://levels/substation_4.txt"
@export var seed_value: int = 20260917

## §7.3 feel budget. Exported so they can be tuned in the inspector, but the
## defaults are the spec's and are not to be changed silently.
@export var shake_fire: float = 3.2
@export var shake_kill: float = 7.0
@export var shake_death: float = 11.0
@export var shake_decay: float = 11.0
@export var decal_cap: int = 150

## The C# bridge is resolved at RUNTIME, not with preload().
##
## Two reasons, both learned the hard way:
##   1. Its [GlobalClass] name only exists after an editor filesystem scan, so a
##      bare-name reference fails to parse in headless and CI runs.
##   2. preload() of a .cs file is a PARSE-TIME dependency. Opened in a Godot
##      build without C# support, the entire script fails to parse with
##      "has no resource loaders" and the game is a black window with a cryptic
##      console error. load() lets the script parse and report the real cause.
const BRIDGE_PATH := "res://game/SimBridge.cs"
const AUDIO := preload("res://game/audio.gd")
const EDITOR := preload("res://game/editor.gd")
const LOADOUT_MENU := preload("res://game/loadout_menu.gd")
const STASH_SCREEN := preload("res://game/stash_screen.gd")
const TITLE_SCREEN := preload("res://game/title_screen.gd")
const OPTIONS_SCREEN := preload("res://game/options_screen.gd")
const LOOT_PANEL := preload("res://game/loot_panel.gd")
const CAMPAIGN := preload("res://game/campaign.gd")
const MISSIONS := preload("res://game/missions.gd")
const LEVELS := preload("res://game/levels.gd")
const AI_DEBUG_OVERLAY := preload("res://game/ai_debug_overlay.gd")
const SHOP_SCREEN := preload("res://game/shop_screen.gd")
const DEV_MENU := preload("res://game/dev_menu.gd")
const HUD_LAYOUT := preload("res://game/hud_layout.gd")
const HUD_EDITOR := preload("res://game/hud_editor.gd")
const STASH := preload("res://game/stash.gd")
const FOOTSTEPS := preload("res://game/footsteps.gd")
const LEVEL_ART := preload("res://game/level_art.gd")

var _bridge: RefCounted

## The level dressed in its map kit (game/level_art.gd). Built per level in
## _refresh_level_cache; `ready` false (no kit, or headless) means the
## procedural floor and walls below draw instead.
var _art: RefCounted = LEVEL_ART.new()

## Set when the project cannot run at all. Currently only one cause: a Godot
## build with no C# support.
var _fatal: String = ""

var _walls: PackedInt32Array = PackedInt32Array()
var _exit_rects: Array[Rect2] = []    # every exit; reaching any ends the run

## Glass and doors AS THEY STAND, re-read after every tick. The walls above are
## cached once per level because they never change; these are the cells that do.
var _panels: PackedInt32Array = PackedInt32Array()

## LIGHTING (cognitohazard_lighting_plan.md §7). The darkness is the sim's own
## light map, drawn as a tinted overlay whose alpha rises as the light falls:
## at alpha a an ordinary blend multiplies what is under it by (1 - a), so it IS
## the plan's multiply layer, without a material or a node. Re-uploaded only
## when SimBridge.LightVersion moves (a door, a lamp, a switch), never per frame.
## Filtered LINEAR through a CanvasTexture, so 20 px cells read as pools of
## light rather than a chessboard, whatever filter the rest of the canvas uses.
var _dark_tex: CanvasTexture = null
var _dark_ver: int = -1
var dark_uploads: int = 0        # counted, like draws: the harness asserts it

## Night blue, not black: a dark room should read as "dark", not "missing".
const C_NIGHT := Color(0.012, 0.020, 0.045)
## Display floor (plan §7.1): the overlay never goes past this, so pitch black
## to the sim is still a readable floor to the player. PRESENTATION ONLY.
const DARK_MAX: int = 200        # of 255, ~0.78
const C_LAMP := Color(1.0, 0.88, 0.62)
const C_TORCH := Color(1.0, 0.93, 0.72, 0.085)
## How the player sees a guard in the dark (SimBridge.GetGuardSight, Q8):
## below SEE_OUTLINE he is an outline and nothing more, below SEE_CLEAR he is
## dimmed toward the floor, and at 0 he is not drawn at all.
const SEE_CLEAR: int = 200
const SEE_OUTLINE: int = 110

## G was pressed on a DOOR, and is still held. While it is, G does not also
## raise the loot panel over a body lying in the doorway: one press, one thing.
var _g_on_door: bool = false

var _shake: Vector2 = Vector2.ZERO

# ------------------------------------------------------------------ camera
#
# The camera is PRESENTATION and nothing else. It never reaches an InputFrame
# and never touches the state hash: a replay recorded here must verify on a
# machine with a different window, a different zoom and the camera parked
# somewhere else entirely. `camera_check.gd` asserts exactly that.
#
# Deliberately NOT a Camera2D. main.gd already draws the world and the HUD as
# two passes separated by a transform reset, so the camera is a transform on the
# first pass and nothing at all on the second. A Camera2D would move the HUD
# too, which would mean splitting this script across a CanvasLayer for no gain.
# The one thing a Camera2D would have given free -- a mouse position already in
# world space -- is `world_mouse()` below, and every site that needs it says so.

## What the game plays at. The camera used to back off until the whole floor
## fitted the screen, which on a one-screen level meant 1.0 and on a large one
## meant 0.6 -- correct for reading a map, too far away for reading a firefight.
## It now holds a fixed magnification and SCROLLS to cover the level instead.
## 0.7 ON REQUEST: zoomed out from 1.35 to 1.0, then 30% further, for a wider
## look round the player. The reference level (48x28, one screen) is now
## smaller than the view and is shown whole, centred; every larger level still
## scrolls. Close to MIN_ZOOM -- there is not much further out to go.
const PLAY_ZOOM: float = 0.7

## Floors and ceilings on that. MIN_ZOOM is where the 22px actors and the 26px
## bars stop being readable; MAX_ZOOM stops a level smaller than the view from
## being blown up past the point the art holds together.
const MIN_ZOOM: float = 0.6
const MAX_ZOOM: float = 2.0

## How much of the way to the cursor the view leans, and the hard cap on it.
## Capped so the camera can never be used to scout: the cursor may leave the
## screen, the view may not follow it there.
const LOOK_AHEAD_FRAC: float = 0.35
const LOOK_AHEAD_MAX: float = 90.0

## Exponential follow, the same 1-exp(-dt*k) form as _visual_scale, so it is
## frame-rate independent.
const CAM_FOLLOW: float = 8.0

## Dilation pulls the view back a little. Under WORLD_SLOW the player is
## watching rounds cross the room -- that is the whole mechanic -- and at these
## muzzle velocities a tight camera would have them enter and leave the view.
const DILATE_ZOOM: float = 0.90

## World point at the centre of the view, the live zoom, and the world-space
## rectangle currently visible. The last is what culling tests against.
var _cam: Vector2 = Vector2.ZERO
var _zoom: float = 1.0
var _view_origin: Vector2 = Vector2.ZERO
var _view_rect: Rect2 = Rect2(0, 0, FIELD_W, FIELD_H)
var _cam_ready: bool = false
var _particles: Array[Dictionary] = []
var _decals: Array[Dictionary] = []
var _flashes: Array[Dictionary] = []
var _pops: Array[Dictionary] = []
## Lightning segments, {a, b, t}: one per jump of a Tesla discharge. Redrawn
## with fresh jitter every frame so they crackle rather than sit there.
var _arcs: Array[Dictionary] = []
## Hidden guards' footsteps, heard through walls (game/footsteps.gd).
## Presentation only: read from GetGuards() after a tick, never fed back.
var _steps: RefCounted = FOOTSTEPS.new()

var _vision: PackedVector2Array = PackedVector2Array()
var _font: Font

var _over_code: int = 0

## This run WON THE GAME: the final mission (missions.gd FINAL_LEVEL) was
## completed. Set once, in _settle_run; the debrief becomes the victory screen.
var _victory: bool = false
## The per-mission table the victory screen draws, [title, runs, completions,
## best] per shipped level. Built once when the game is won rather than per
## frame, since it reads every level file.
var _victory_rows: Array = []
## The run in progress came from the editor's ENTER. A playtest leaves
## level_path alone, so without this an edited level playtested while the Black
## Site was selected would win the game.
var _playtest_run: bool = false

## The sim's worn kit on the tick this run BEGAN (stash.sim_kit). Extraction
## compares the kit it ends with against this, and writes back only what the
## field changed -- see stash.reconcile_worn.
var _deployed_kit: Array = []
## The stash: inventory on the left, mission select on the right. It replaced
## both the equipment screen and the start menu, which used to answer "what am
## I carrying" and "where am I taking it" on separate screens.
var _stash_screen: Node2D
var _title: Node2D
var _options: Node2D
var _loot: Node2D

## Where the HUD's elements sit, and the screen that lets you rearrange them.
var _hud: RefCounted
var _hud_ed: Node2D

## The campaign ledger and the screen that spends it.
var _campaign: RefCounted
var _shop: Node2D

## F8. Sits above every other screen and is openable from any of them, so it is
## declared apart from the flow they belong to.
var _dev: Node2D

## Which movement tier the scroll wheel has selected: 0 stealth, 1 walk,
## 2 fast walk, 3 sprint. Held in game/ because it is INPUT -- the sim is told
## it every tick like any other intent, and a replay carries it.
var _move_tier: int = 1

## True when the equipment screen was opened FROM the start screen, so closing
## it returns there instead of deploying. The mission has not begun yet.

## Whether the stash came off disk, which decides whether the start screen
## offers "keep what I had".
var _stash_loaded: bool = false
var _stash: RefCounted

## Where the worn kit and the stash live between runs. The equipment screen
## writes the same path.
const EQUIP_SAVE: String = "user://stash.txt"
var _draw_count: int = 0

var _audio: Node
var _editor: Node2D
var _menu: Node2D

## Replay. Every live run is recorded from its first tick — a run you have to
## remember to start recording is a run you will not have recorded when
## something interesting happens.
const REPLAY_DIR: String = "user://replays"
var _playback: bool = false
var _pb_playing: bool = true
var _pb_speed: float = 1.0
var _pb_accum: float = 0.0
var _saved_path: String = ""
var _save_notice: float = 0.0
## True once the current run's replay is on disk, so the safety-net save in
## _exit_tree() does not write a second copy of a run that already saved.
var _saved_this_run: bool = false
var _notice: String = ""
var _notice_t: float = 0.0

## SCALE_LERP (spec §5.2): exp(-dt * 20) toward the sim's world scale. This is
## explicitly game/-visual-only smoothing — sim/ steps in exact rational scales
## and never sees this value. It drives the master lowpass and audio pitching.
var _visual_scale: float = 1.0
var _rng := RandomNumberGenerator.new()

# Palette. Art per cognitohazard_art_pipeline.md; procedural fallback retained (spec §0).
const C_VOID := Color(0.04, 0.047, 0.059)
const C_FLOOR := Color(0.051, 0.067, 0.086)
const C_FLOOR_LIT := Color(0.086, 0.11, 0.137)
const C_WALL := Color(0.16, 0.18, 0.22)
const C_WALL_EDGE := Color(0.24, 0.27, 0.33)
const C_PLAYER := Color(0.85, 0.87, 0.92)
const C_GUARD := Color(0.72, 0.56, 0.36)
const C_DEAD := Color(0.38, 0.22, 0.20)

## Armour reads as cold steel against the warm tan of a guard, so a plate is
## legible on the silhouette without another hue competing for meaning.
## Outside the level's own bounds. Darker than the floor, so the edge of the
## world reads as an edge rather than as more room.
const C_BEYOND := Color(0.02, 0.025, 0.032)
## Behind the between-runs screens, where no world is drawn. title_screen's C_BG.
const C_BASE := Color(0.030, 0.036, 0.045)

## One label colour and one trough colour across the whole HUD. Each readout
## used to pick its own, which is why the bar never quite looked like one thing.
const C_HUD_LABEL := Color(0.50, 0.55, 0.63)
const C_HUD_TROUGH := Color(0.12, 0.13, 0.16)

## A chest reads warmer than a record cache, because it holds a different kind
## of thing and the two must never be confused at a glance.
## The objective reads apart from both a supply chest and a record cache: it is
## the one thing on the floor the mission is actually about.
const C_OBJECTIVE := Color(0.55, 0.85, 0.95)

## Your own gear on the floor. Cool and small: it must never be mistaken for a
## chest you have not opened yet.
const C_GROUND := Color(0.58, 0.64, 0.72)

const C_CHEST := Color(0.85, 0.66, 0.36)
const C_CHEST_EMPTY := Color(0.34, 0.31, 0.26)

const C_PLATE := Color(0.62, 0.70, 0.80)
const C_PLATE_GONE := Color(0.34, 0.37, 0.42)
const C_DOWN := Color(0.30, 0.42, 0.56)
const C_EXIT := Color(0.25, 0.72, 0.48)
## Glass is COLD and see-through; a door is WARM and solid. Neither may read as
## a wall (grey) or as the chest amber, which is gear.
const C_GLASS := Color(0.62, 0.84, 0.95)
const C_DOOR := Color(0.36, 0.27, 0.19)
const C_DOOR_EDGE := Color(0.62, 0.48, 0.32)
const C_CACHE := Color(0.90, 0.74, 0.30)
const C_COLD := Color(0.40, 0.76, 0.85)
const C_AMBER := Color(0.92, 0.70, 0.25)
const C_SIGNAL := Color(0.90, 0.32, 0.28)
const C_FEAR := Color(0.95, 0.95, 0.70)
## Heard, not seen: a pale cold tone apart from every guard and state colour.
const C_STEP := Color(0.72, 0.84, 0.98)


func _ready() -> void:
	_font = ThemeDB.fallback_font
	_rng.seed = seed_value

	var bridge_script: Script = load(BRIDGE_PATH)
	if bridge_script == null:
		_fatal = "This project needs the .NET build of Godot."
		push_error("%s  '%s' could not be loaded, which means this Godot has no C# support. Open the project with /Applications/Godot-4.6-dotnet.app instead."
			% [_fatal, BRIDGE_PATH])
		queue_redraw()
		return

	_audio = AUDIO.new()
	_audio.name = "Audio"
	add_child(_audio)

	_bridge = bridge_script.new()

	# What was worn last session, resolved before Load so the first mission
	# starts with it. Without this the pack would be zero-sized on launch and
	# nothing could be looted until the player found the equipment screen.
	_stash = STASH.new(_bridge)
	_campaign = CAMPAIGN.new()
	if _campaign.load_saved():
		print("cognitohazard: campaign loaded — %d on hand, %d run(s), %d extraction(s)"
			% [_campaign.money, _campaign.runs, _campaign.extractions])

	if FileAccess.file_exists(EQUIP_SAVE):
		if _stash.from_text(FileAccess.get_file_as_string(EQUIP_SAVE)) > 0:
			push_warning("parts of %s were unreadable and were skipped" % EQUIP_SAVE)
		_stash_loaded = true
	else:
		_stash.stock_default()
		_stash.equip_starting_kit()
	_stash.apply_to(_bridge)

	# `-- --level res://levels/foo.txt` overrides the export, the same way
	# --replay does. Command-line rather than a menu so a large level can be
	# smoke-tested from a shell loop without touching the scene.
	var chosen: String = _level_override()
	if not chosen.is_empty():
		level_path = chosen

	if not _load_level(level_path):
		return

	# Positive confirmation that the C# bridge loaded and the level parsed. A
	# silent scene with no script looks identical to a working one at a glance.
	# The EV_* constants above mirror Sim.SimEventKind by ordinal. If a kind is
	# ever inserted mid-enum, every sound and effect silently remaps to the
	# wrong event. Fail loudly instead.
	var kinds: int = _bridge.EventKindCount
	if kinds != EV_LAST + 1:
		push_error("event kind mismatch: sim has %d, game/ mirrors %d — re-check the EV_* constants"
			% [kinds, EV_LAST + 1])

	_editor = EDITOR.new()
	_editor.name = "Editor"
	_editor.bridge = _bridge
	_editor.playtest_requested.connect(_start_playtest)
	add_child(_editor)

	_menu = LOADOUT_MENU.new()
	_menu.name = "LoadoutMenu"
	_menu.bridge = _bridge
	_menu.closed.connect(_on_loadout_closed)
	add_child(_menu)

	_stash_screen = STASH_SCREEN.new()
	_stash_screen.name = "StashScreen"
	_stash_screen.bridge = _bridge
	_stash_screen.stash = _stash
	_stash_screen.campaign = _campaign
	_stash_screen.closed.connect(_on_stash_closed)
	_stash_screen.deploy_requested.connect(_on_deploy)
	_stash_screen.equipped.connect(_on_stash_equip)
	_stash_screen.dropped.connect(_on_stash_equip)
	_stash_screen.shop_requested.connect(_open_shop)
	add_child(_stash_screen)

	_title = TITLE_SCREEN.new()
	_title.name = "TitleScreen"
	_title.campaign = _campaign
	_title.continue_requested.connect(_on_continue)
	_title.new_game_requested.connect(_on_new_game)
	_title.options_requested.connect(_on_options)
	_title.builder_requested.connect(_on_builder)
	add_child(_title)

	_options = OPTIONS_SCREEN.new()
	_options.name = "OptionsScreen"
	_options.audio = _audio
	_options.campaign = _campaign
	_options.closed.connect(_on_options_closed)
	_options.hud_editor_requested.connect(_on_hud_from_options)
	_options.campaign_wiped.connect(_on_wipe)
	add_child(_options)

	# The layout has to exist before the first _draw, and its saved arrangement
	# is loaded here so a player never sees one frame of the default first.
	_hud = HUD_LAYOUT.new()
	var restored: int = _hud.load_saved()
	if restored > 0:
		print("cognitohazard: hud layout restored (%d elements)" % restored)

	_hud_ed = HUD_EDITOR.new()
	_hud_ed.name = "HudEditor"
	_hud_ed.layout = _hud
	_hud_ed.closed.connect(_on_hud_closed)
	add_child(_hud_ed)

	_shop = SHOP_SCREEN.new()
	_shop.name = "ShopScreen"
	_shop.bridge = _bridge
	_shop.stash = _stash
	_shop.campaign = _campaign
	_shop.closed.connect(_on_shop_closed)
	add_child(_shop)

	_dev = DEV_MENU.new()
	_dev.name = "DevMenu"
	_dev.bridge = _bridge
	_dev.stash = _stash
	_dev.spawned.connect(_on_dev_spawn)
	_dev.pack_spawn_requested.connect(_on_dev_pack_spawn)
	add_child(_dev)

	_loot = LOOT_PANEL.new()
	_loot.name = "LootPanel"
	_loot.bridge = _bridge
	add_child(_loot)

	_maybe_enter_playback()

	# Pick a loadout before the first tick.
	#
	# Skipped when watching a replay, whose kit is whatever was recorded and is
	# not the player's to change. Skipped headless too: there is nobody there to
	# choose, and the smoke test and the harnesses exist to exercise the SIM --
	# holding the world on a menu would have quietly reduced every one of them to
	# measuring a screen. The kit is already resolved above, before Load, so a
	# headless run deploys with the saved or starting loadout as it stands.
	if not _playback and DisplayServer.get_name() != "headless":
		_title.open_screen()

	_vision = _bridge.GetVisionPolygon(VISION_RAYS, VISION_RADIUS)

	# A fixed-timestep game has no reason to render faster than it simulates.
	# Left uncapped this ran at ~550 fps on an Intel iGPU in GL compatibility,
	# where vsync does not reliably engage.
	Engine.max_fps = 60

	# Proof the stretch transform is live: at the design size the scale is 1.0,
	# and it tracks the window from there. A silently disabled stretch mode looks
	# identical to a working one until someone resizes.
	var cx: Transform2D = get_viewport().get_screen_transform()
	print("cognitohazard: viewport %dx%d, canvas scale %0.3f" % [
		get_viewport_rect().size.x, get_viewport_rect().size.y, cx.get_scale().x])

	print("cognitohazard: %d wall rects, %d guards, %d caches" % [
		_walls.size() / 4, _bridge.GetGuards().size() / GUARD_STRIDE, _bridge.GetCaches().size() / 3])

	# The worn kit, and the pack it gives. A zero-sized pack means nothing can be
	# looted, which is worth saying out loud rather than discovering in play.
	print("cognitohazard: %s / %s  pack %dx%d" % [
		_bridge.CurrentWeaponName, _bridge.CurrentSecondaryName,
		_bridge.PackWidth, _bridge.PackHeight])


## Fullscreen is handled in _input rather than _physics_process so it works in
## every mode — the editor and the loadout menu both consume unhandled input,
## and _physics_process early-returns while either is open.
func _input(event: InputEvent) -> void:
	if event.is_action_pressed("fullscreen"):
		_toggle_fullscreen()
		get_viewport().set_input_as_handled()
		return

	# Same reasoning for the HUD editor: it has to be reachable from a running
	# mission, which _physics_process does not early-return out of, but its own
	# keys must not fall through to the game underneath.
	if event is InputEventKey and event.pressed and not event.echo:
		# F8 first: the dev menu opens OVER whatever is up, so it has to win the
		# key before a screen underneath can claim it.
		if _dev != null and event.keycode == KEY_F8:
			_toggle_dev_menu()
			get_viewport().set_input_as_handled()
			return
		# F3: the AI debug overlay. Presentation only -- it never reaches an
		# InputFrame, so it cannot touch a replay or the hash.
		if event.keycode == KEY_F3 and not (_hud_ed != null and _hud_ed.active):
			_ai_debug = not _ai_debug
			_notice = "AI debug overlay on  ·  F3 to hide" if _ai_debug else "AI debug overlay off"
			_notice_t = 2.0
			get_viewport().set_input_as_handled()
			return
		if _hud_ed != null and _handle_hud_key(event):
			get_viewport().set_input_as_handled()
		return

	# The scroll wheel picks the movement tier. Clamped rather than wrapping:
	# wheeling off the end of the range and landing back on stealth mid-firefight
	# would be the worst possible moment for it.
	if event is InputEventMouseButton and event.pressed and _dev != null and _dev.active:
		if event.button_index == MOUSE_BUTTON_WHEEL_UP:
			_dev.move(-1)
			get_viewport().set_input_as_handled()
			return
		if event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			_dev.move(1)
			get_viewport().set_input_as_handled()
			return

	if event is InputEventMouseButton and event.pressed and not _any_screen_open():
		if event.button_index == MOUSE_BUTTON_WHEEL_UP:
			_set_move_tier(_move_tier + 1)
			get_viewport().set_input_as_handled()
			return
		if event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			_set_move_tier(_move_tier - 1)
			get_viewport().set_input_as_handled()
			return

	if _hud_ed == null or not _hud_ed.active:
		return

	# Dragging. The mouse is read through get_local_mouse_position rather than
	# event.position for the reason CLAUDE.md gives: the raw event is in window
	# pixels and lands on the wrong element the moment the canvas is scaled.
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed:
			_hud_ed.press(get_local_mouse_position())
		else:
			_hud_ed.release()
		get_viewport().set_input_as_handled()
	elif event is InputEventMouseMotion:
		_hud_ed.drag(get_local_mouse_position())
		get_viewport().set_input_as_handled()


## F4, handled by keycode rather than through an input action: adding an action
## means editing project.godot, and the editor rewrites that file whenever it
## feels like it. The same reason editor.gd reads TAB this way.
func _handle_hud_key(event: InputEventKey) -> bool:
	if event.keycode == KEY_F4:
		if _hud_ed.active:
			_hud_ed.close_editor(true)
		elif _can_open_hud_editor():
			_hud_ed.open_editor()
		return true

	if not _hud_ed.active:
		return false

	match event.keycode:
		KEY_ESCAPE:
			_hud_ed.close_editor(false)
		KEY_ENTER, KEY_KP_ENTER:
			_hud_ed.close_editor(true)
		KEY_R:
			_hud_ed.reset_all()
		KEY_LEFT:
			_hud_ed.nudge(Vector2.LEFT)
		KEY_RIGHT:
			_hud_ed.nudge(Vector2.RIGHT)
		KEY_UP:
			_hud_ed.nudge(Vector2.UP)
		KEY_DOWN:
			_hud_ed.nudge(Vector2.DOWN)
		_:
			return false
	return true


## The shop is reachable from the start screen, which is the only place
## "between runs" actually exists. It hides the start screen rather than drawing
## over it, so only one thing is ever taking keys.
var _shop_from_start: bool = false

## Whether the HUD editor was opened from Options, so closing it returns there
## rather than leaving the player wherever the editor happened to be over.
var _hud_from_options: bool = false

## Item ids the developer menu has asked to conjure into the pack, oldest first.
## A QUEUE rather than a single slot because the menu pauses the sim: nothing is
## stepped while it is open, so several spawns can pile up before the next tick
## can carry one.
var _spawn_queue: Array[int] = []

## E DURING A RUN: the field view. Only what is being carried -- the pack, the
## four slots the sim reads, and the floor. No stash, because a stash is at base
## and cannot be reached from a corridor, and no mission select, because the
## mission has already been chosen.
##
## Falls back to the full stash when there is no run to be inside of, which is
## what E means on the debrief.
func _open_mission_inventory() -> void:
	if _stash_screen == null:
		return
	if not _bridge.RunLive:
		_open_start_screen()
		return
	_loot.hide_panel()
	_stash_screen.open_screen(level_path, true)


## E is a TOGGLE: the key that opens the inventory is the key that closes it.
##
## ONE reader, which is the whole point of routing it through here. main.gd
## POLLS -- Input.is_action_just_pressed stays true for the rest of the frame
## once it is true at all -- so two readers see one physical press twice. That
## is exactly what E did: _handle_stash_keys closed the field view and the open
## below reopened it before the frame was out, so E could raise the inventory
## and never put it down.
##
## Silently, because the player has not backed out of anything: `closed` means
## the title screen, and the run is still going on behind this.
func _toggle_mission_inventory() -> void:
	if _stash_screen == null:
		return
	if _stash_screen.active:
		_stash_screen.close_screen_silent()
		return
	_open_mission_inventory()


## Something was staged — put on, or put down. The field view STAYS OPEN: the
## world runs behind it now, so the next tick applies the action and the screen
## redraws with it done. It used to close on every action because the sim was
## paused, and a staged action would otherwise have sat unapplied behind a
## picture of the kit it was about to change.
func _on_stash_equip() -> void:
	if _stash_screen == null or not _stash_screen.active:
		return
	# Nothing can be staged outside a run any more -- there is no floor to drop
	# onto at base and the pack cannot arm you there -- so reaching here with
	# the base stash up means a new staging path forgot that. Closing the screen
	# was the old answer and it is the wrong one: it left the player looking at
	# a mission that has not begun, with no screen and nothing to press.
	if not _stash_screen.mission_mode:
		push_error("the base stash staged a sim action; nothing there may")


## Back to the stash. Reopened mid-session rather than only at launch, so the
## debrief has somewhere to send the player.
func _open_start_screen() -> void:
	if _stash_screen == null:
		return
	_loot.hide_panel()
	_stash_screen.open_screen(level_path)


## Continuing: the campaign and the stash are already loaded from disk in
## _ready, so this is only a matter of showing the stash.
func _on_continue() -> void:
	_title.close_screen()
	_open_start_screen()


## A new game wipes what is on disk and starts from the issued kit. Destructive,
## and the title screen is where a player expects that to be possible -- but it
## is still worth doing in ONE place so nothing is half-reset.
func _on_new_game() -> void:
	_title.close_screen()
	_wipe_campaign()
	_open_start_screen()


func _wipe_campaign() -> void:
	_campaign.from_text("")
	_campaign.save()
	_stash = STASH.new(_bridge)
	_stash.stock_default()
	_stash.equip_starting_kit()
	_stash.apply_to(_bridge)
	_stash_loaded = false
	_save_stash()

	# Every screen that holds the stash has to be handed the new one, or one of
	# them keeps drawing the old campaign's gear.
	_stash_screen.stash = _stash
	_shop.stash = _stash
	_dev.stash = _stash
	_restart()


func _on_options() -> void:
	_title.close_screen()
	_options.open_screen()


func _on_options_closed() -> void:
	_title.open_screen()


## The HUD editor is reachable from Options, and has to come BACK to Options.
## Closing options LOUDLY here emitted `closed`, whose handler opens the title --
## and the title is z 300 against the editor's z 200, so the title drew over an
## editor that was still holding the keyboard.
func _on_hud_from_options() -> void:
	_hud_from_options = true
	_options.close_screen_silent()
	_hud_ed.open_editor()


func _on_hud_closed() -> void:
	if not _hud_from_options:
		return
	_hud_from_options = false
	_options.open_screen()


func _on_wipe() -> void:
	_wipe_campaign()


func _on_builder() -> void:
	_title.close_screen()
	_editor.open_editor()


## Leaving the stash goes back to the title rather than into a mission: the
## only way into a mission is DEPLOY.
func _on_stash_closed() -> void:
	_stash.apply_to(_bridge)
	_save_stash()
	_title.open_screen()


func _open_shop() -> void:
	_shop_from_start = _stash_screen != null and _stash_screen.active
	if _stash_screen != null:
		_stash_screen.close_screen_silent()
	_shop.open_screen()


## F8. Opens OVER whatever is already up rather than replacing it, which is why
## it is a toggle and not part of any screen's flow. Two exceptions, both cases
## where something else already owns the whole input surface: the level editor
## takes the keyboard entire and has modal tools of its own, and the HUD editor
## is a drag surface whose mouse this list would steal.
func _toggle_dev_menu() -> void:
	if _dev.active:
		_dev.close_screen()
		return
	if _editor != null and _editor.active:
		return
	if _hud_ed != null and _hud_ed.active:
		return
	# Asked fresh on every open, because it decides where ENTER puts things and
	# the answer changes the moment the player dies or extracts.
	_dev.live = _in_mission()
	_dev.open_screen()


## A mission is being PLAYED, not merely loaded. RunLive alone is not that: the
## world is built at launch (and rebuilt by a new campaign) behind the title
## and the base stash, so RunLive is true before anything has been deployed. The
## dev menu asked it alone, queued its first spawns for a pack that deploying
## then threw away in _restart(), and only "worked" once a death or extraction
## had ended that phantom run. The screens below are reachable only between
## runs; the field view (stash in mission_mode) and the loadout menu are not
## among them, because both are opened FROM a mission. A replay is never one.
func _in_mission() -> bool:
	return _bridge.RunLive and not _playback and not _between_runs_screen_up()


## A screen that exists only BETWEEN runs is up: the title, options, the shop,
## or the stash at BASE. Not the field view (the stash in mission_mode), which
## is opened from inside a mission and is meant to sit over it.
func _between_runs_screen_up() -> bool:
	return (_title != null and _title.active) \
		or (_options != null and _options.active) \
		or (_shop != null and _shop.active) \
		or (_stash_screen != null and _stash_screen.active and not _stash_screen.mission_mode)


## A spawn changes the stash, and the stash is only durable once written, so it
## is saved on the spot -- a dev menu whose gear vanishes on quit is worse than
## no dev menu.
func _on_dev_spawn(_item_id: int) -> void:
	_save_stash()


## A spawn into the MISSION PACK. Queued, never performed here: the pack feeds
## the state hash and rides in the replay, so the item has to be conjured INSIDE
## a tick via InputFrame.SpawnItem, exactly as a drop is. The queue drains one
## per tick in _physics_sim, because an InputFrame carries one.
func _on_dev_pack_spawn(item_id: int) -> void:
	_spawn_queue.append(item_id)


## Keys while the dev menu is up. It is checked before every other screen in
## _physics_process, so these are the only keys the frame sees.
func _handle_dev_keys() -> void:
	if Input.is_action_just_pressed("move_up") or Input.is_action_just_pressed("ui_up"):
		_dev.move(-1)
	elif Input.is_action_just_pressed("move_down") or Input.is_action_just_pressed("ui_down"):
		_dev.move(1)
	elif Input.is_action_just_pressed("move_left") or Input.is_action_just_pressed("ui_left"):
		_dev.cycle_tab(-1)
	elif Input.is_action_just_pressed("move_right") or Input.is_action_just_pressed("ui_right"):
		_dev.cycle_tab(1)
	elif Input.is_action_just_pressed("ui_accept"):
		_dev.spawn()
	elif Input.is_action_just_pressed("ui_cancel"):
		_dev.close_screen()


func _on_shop_closed() -> void:
	_campaign.save()
	if _shop_from_start:
		_shop_from_start = false
		_stash_screen.open_screen(level_path)


## Not over another screen. Arranging the HUD on top of the equipment grid or
## the start menu would be arranging something that is not being drawn.
func _can_open_hud_editor() -> bool:
	return not _any_screen_open()


## Any full-screen interface is up. The scroll wheel belongs to those while they
## are open -- the editor zooms with it, the equipment screen may yet -- so the
## movement tier must not also move underneath them.
func _any_screen_open() -> bool:
	return (_editor != null and _editor.active) \
		or (_menu != null and _menu.active) \
		or (_stash_screen != null and _stash_screen.active) \
		or (_title != null and _title.active) \
		or (_options != null and _options.active) \
		or (_shop != null and _shop.active) \
		or (_hud_ed != null and _hud_ed.active) \
		or (_dev != null and _dev.active)


func _set_move_tier(tier: int) -> void:
	var next: int = clampi(tier, 0, _bridge.MoveTierCount - 1)
	if next == _move_tier:
		return
	_move_tier = next
	_notice = "%s  ·  %d px/s" % [_bridge.MoveTierName(next), _bridge.MoveTierSpeedPx(next)]
	_notice_t = 1.4


## Keys while the stash is up. The MOUSE works the inventory half; the keyboard
## works the mission half, which is why the two can share one screen without a
## focus concept.
func _handle_stash_keys() -> void:
	# The field view has no mission half: the mission is already under way, so
	# up/down and ENTER have nothing to pick and deploying again would restart
	# the run the player is standing in.
	#
	# E is deliberately NOT read here -- see _toggle_mission_inventory. The
	# field view is the one screen the tick loop does not early-return out of,
	# so a press consumed here was still sitting there to be read by the open
	# further down, which put the screen straight back up inside the same frame.
	# ESC is safe to read here because nothing below reads it.
	if _stash_screen.mission_mode:
		if Input.is_action_just_pressed("ui_cancel"):
			_stash_screen.close_screen_silent()
		return

	if Input.is_action_just_pressed("move_up") or Input.is_action_just_pressed("ui_up"):
		_stash_screen.cycle_level(-1)
	elif Input.is_action_just_pressed("move_down") or Input.is_action_just_pressed("ui_down"):
		_stash_screen.cycle_level(1)
	elif Input.is_action_just_pressed("ui_accept"):
		_on_deploy()
	elif Input.is_action_just_pressed("shop"):
		_open_shop()
	elif Input.is_action_just_pressed("equipment") or Input.is_action_just_pressed("ui_cancel"):
		_stash_screen.close_screen()


func _toggle_fullscreen() -> void:
	var mode: int = DisplayServer.window_get_mode()
	var full: bool = mode == DisplayServer.WINDOW_MODE_FULLSCREEN \
		or mode == DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN
	# Borderless fullscreen rather than exclusive: on macOS exclusive mode grabs
	# the display and makes alt-tabbing out unpleasant.
	DisplayServer.window_set_mode(
		DisplayServer.WINDOW_MODE_WINDOWED if full else DisplayServer.WINDOW_MODE_FULLSCREEN)
	_notice = "windowed" if full else "fullscreen"
	_notice_t = 2.0


func _exit_tree() -> void:
	if _bridge == null:
		return

	# Safety net. The auto-save in _physics_process only fires on the frame a
	# run's outcome flips, so any run that never reaches that frame -- closing
	# the window mid-run, or quitting on the very frame it ends -- left nothing
	# on disk at all. Quitting is the one exit all of those paths share.
	if not _playback and not _saved_this_run and _bridge.RecordedTicks > 0:
		_save_replay()

	var w: PackedInt32Array = _bridge.GetWorld()
	print("cognitohazard: ran %d ticks, outcome %s, %d draws, %d sounds, %d editor, %d menu, %d stash, %d title, %d loot, %d hud, %d dev" % [
		w[0], ["running", "escaped", "dead"][w[6]], _draw_count, _audio.played,
		_editor.draws, _menu.draws, _stash_screen.draws, _title.draws,
		_loot.draws, _hud_ed.draws, _dev.draws])


## `Godot --path . -- --replay <path|last>` watches a recorded run instead of
## Load a level file into the bridge and re-cache everything derived from its
## geometry. One function, because the walls and the exit rect are cached at
## load: a second load path that forgot to refresh them would draw the previous
## level's walls over the new one's floor, and the player would walk through
## them.
func _load_level(path: String) -> bool:
	var text: String = FileAccess.get_file_as_string(path)
	if text.is_empty():
		push_error("could not read level: %s" % path)
		return false

	_bridge.Load(text, seed_value)
	level_path = path
	_refresh_level_cache()
	return true


func _refresh_level_cache() -> void:
	_walls = _bridge.GetWalls()
	_panels = _bridge.GetPanels()
	var ex: PackedInt32Array = _bridge.GetExits()
	_exit_rects.clear()
	for i in range(0, ex.size() - 3, 4):
		_exit_rects.append(Rect2(ex[i] / FX, ex[i + 1] / FX, ex[i + 2] / FX, ex[i + 3] / FX))
	# Dressed from the LIVE level's grid -- the one being played, which after a
	# replay load or a playtest is not the file on disk.
	_art.build(_bridge.GetGrid(), _bridge.GridCols, _bridge.GridRows, _bridge.LevelTheme)
	# A new level means a new world size, so the camera must not keep easing
	# toward a point that belonged to the last one.
	_cam_ready = false


## The seed a new run starts from. FRESH every deploy, so the same floor is not
## the same floor twice: chest loot and luck are rolled from it (sim/LootTable),
## and with a fixed seed every run of a level found the same things. The replay
## records whichever seed was used, so determinism is untouched.
##
## `-- --seed N` pins it, for chasing a bug through the same run twice.
var _seed_rng := RandomNumberGenerator.new()
var _seed_rng_ready: bool = false


## "luck 132% -- a good day". Luck is rolled per run (Tune.LuckMin..150,
## mean 100) and tilts chest rarity; the words are the part a player reads.
static func luck_text(luck: int) -> String:
	var mood: String = "an ordinary day"
	if luck >= 130:
		mood = "a very good day"
	elif luck >= 112:
		mood = "a good day"
	elif luck <= 70:
		mood = "a very bad day"
	elif luck <= 88:
		mood = "a bad day"
	return "luck %d%%  —  %s" % [luck, mood]


func run_seed() -> int:
	var args: PackedStringArray = OS.get_cmdline_user_args()
	for i in range(args.size()):
		if args[i] == "--seed" and i + 1 < args.size() and args[i + 1].is_valid_int():
			return args[i + 1].to_int()
	if not _seed_rng_ready:
		_seed_rng.randomize()
		_seed_rng_ready = true
	# Positive and inside 31 bits: it crosses the bridge as an int and is
	# written into the replay as text.
	return _seed_rng.randi() & 0x7FFFFFFF


## The level named on the command line, or "" for the exported default.
func _level_override() -> String:
	var args: PackedStringArray = OS.get_cmdline_user_args()
	for i in range(args.size()):
		if args[i] == "--level" and i + 1 < args.size():
			return args[i + 1]
	return ""


## playing. Command-line rather than a menu so it composes with a debugger and
## a shell loop.
func _maybe_enter_playback() -> void:
	var args: PackedStringArray = OS.get_cmdline_user_args()
	var want: String = ""
	for i in range(args.size()):
		if args[i] == "--replay" and i + 1 < args.size():
			want = args[i + 1]
			break
	if want.is_empty():
		return

	var path: String = _resolve_replay(want)
	if path.is_empty():
		push_error("no replay found for '%s'" % want)
		return

	var text: String = FileAccess.get_file_as_string(path)
	if text.is_empty() or not _bridge.LoadReplay(text):
		push_error("could not load replay: %s" % path)
		return

	_playback = true
	_pb_playing = true
	print("cognitohazard: replaying %s (%d ticks)" % [path, _bridge.ReplayLength])


func _resolve_replay(want: String) -> String:
	if want != "last":
		return want if FileAccess.file_exists(want) else ""
	var dir := DirAccess.open(REPLAY_DIR)
	if dir == null:
		return ""
	var best: String = ""
	for f in dir.get_files():
		if f.ends_with(".txt") and f > best:
			best = f
	return "" if best.is_empty() else REPLAY_DIR + "/" + best


## One tick of held keys and buttons, as the sim's flag word.
##
## Static and side-effect free for the same reason the camera functions below
## are: a rule that exists only inside _physics_process cannot be asserted in
## the headless harness, and this rule has now been got wrong twice.
##
## `hands_on_weapon` is the whole of it. BOTH mouse buttons are weapon controls
## -- left fires, right aims -- so anything that puts a full-screen panel under
## the cursor has to take them away, or a click meant for that panel goes off
## down the corridor instead. The loot panel and the HUD editor were on that
## list; the FIELD VIEW was not, which is how sorting your bag mid-mission fired
## your rifle. Nothing else is withheld: the keyboard is not ambiguous, and
## reload, dilate and swap are what they say they are wherever they are pressed.
static func sim_flags(fire: bool, dilate: bool, subdue: bool, reload: bool,
		aim: bool, swap: bool, loot: bool, hands_on_weapon: bool) -> int:
	var flags: int = 0
	if fire and hands_on_weapon:
		flags |= 1
	# Bit 1 was the sneak flag. The movement tier replaced it and is passed as
	# its own argument to Step, so the bit is now FREE -- the first spare one
	# since looting had to become a field for want of a bit.
	if dilate:
		flags |= 4
	if subdue:
		flags |= 8
	if reload:
		flags |= 16
	if aim and hands_on_weapon:
		flags |= 32
	# Edge, like subdue and reload: one swap per press, not one per tick.
	if swap:
		flags |= 64
	# Level, like fire: the dwell fills only while the key is held down.
	if loot:
		flags |= 128
	return flags


## The DoorPick for this tick: 0, or the door's panel index + 1. Static and
## pure for the reason sim_flags is -- it is a rule, and the harness has to be
## able to ask it. `door` is GetDoorTarget (index, x, y, open, dist), `loot`
## GetLootTarget, `loot_dist` that target's distance, all fixed-point.
static func door_pick_for(pressed: bool, sorting: bool, door: PackedInt32Array,
		loot: PackedInt32Array, loot_dist: int) -> int:
	if not pressed or sorting or door.size() < 5:
		return 0
	if loot.size() >= 5 and loot_dist >= 0 and loot_dist < door[4]:
		return 0
	return door[0] + 1


func _physics_process(_delta: float) -> void:
	if _bridge == null:
		return

	# The dev menu sits above every screen, so it is checked before any of them.
	# The sim is not stepped while it is up -- the same bargain the loadout menu
	# makes, and the reason a spawn can never land mid-tick.
	if _dev != null and _dev.active:
		_handle_dev_keys()
		return

	# The start screen holds the world before the mission begins. Nothing is
	# stepped and nothing is recorded, so quitting from here leaves no replay.
	if _loot != null and ((_shop != null and _shop.active)
			or (_stash_screen != null and _stash_screen.active)
			or (_title != null and _title.active)
			or (_editor != null and _editor.active)
			or (_menu != null and _menu.active)):
		_loot.hide_panel()

	if _shop != null and _shop.active:
		if Input.is_action_just_pressed("move_up") or Input.is_action_just_pressed("ui_up"):
			_shop.move(-1)
		elif Input.is_action_just_pressed("move_down") or Input.is_action_just_pressed("ui_down"):
			_shop.move(1)
		elif Input.is_action_just_pressed("ui_accept"):
			if _shop.buy():
				# BOTH halves of the trade, now. The ledger used to be written
				# only when the shop closed, so quitting with it open kept the
				# item (the stash is saved here) and never charged for it.
				_save_stash()
				_campaign.save()
		elif Input.is_action_just_pressed("shop") or Input.is_action_just_pressed("ui_cancel"):
			_shop.close_screen()
		return

	# The title and options screens sit above everything and take the keyboard
	# whole, so they are checked before anything else that could consume a key.
	if _title != null and _title.active:
		if Input.is_action_just_pressed("move_up") or Input.is_action_just_pressed("ui_up"):
			_title.move(-1)
		elif Input.is_action_just_pressed("move_down") or Input.is_action_just_pressed("ui_down"):
			_title.move(1)
		elif Input.is_action_just_pressed("ui_accept"):
			_title.confirm()
		return

	if _options != null and _options.active:
		if Input.is_action_just_pressed("move_up") or Input.is_action_just_pressed("ui_up"):
			_options.move(-1)
		elif Input.is_action_just_pressed("move_down") or Input.is_action_just_pressed("ui_down"):
			_options.move(1)
		elif Input.is_action_just_pressed("ui_accept"):
			_options.confirm()
		elif Input.is_action_just_pressed("ui_cancel"):
			_options.close_screen()
		return

	# main.gd owns every key. A screen never toggles itself, because polling here
	# and handling the event there would see one physical press twice and open
	# and close in the same frame.
	if _stash_screen != null and _stash_screen.active:
		_handle_stash_keys()
		# The FIELD view does not pause. Rummaging in your own bag is not meant
		# to be free any more than rummaging in a corpse is -- the world keeps
		# running behind it, which is what makes "or you take damage" mean
		# something. The base stash still holds the world, because there is no
		# world to hold: the mission has not begun.
		if not _stash_screen.mission_mode:
			return

	# The editor owns the screen and the keyboard while it is open; the sim is
	# simply not advanced, so a level can be authored without the world running
	# underneath it.
	if _editor != null and _editor.active:
		return
	if _menu != null and _menu.active:
		return

	if _playback:
		_physics_playback()
		return

	if Input.is_action_just_pressed("save_replay"):
		_save_replay()

	# The loadout is fixed at mission start, so opening the menu and closing it
	# stages a kit for the next run rather than restarting into it.
	if Input.is_action_just_pressed("cycle_weapon"):
		_menu.open_menu()
		return

	# The only place the equipment key is read on the field-view path. See
	# _toggle_mission_inventory for why there may not be a second one.
	if Input.is_action_just_pressed("equipment"):
		_toggle_mission_inventory()
		return

	# The debrief is up. ENTER goes back to the stash, which is where a run is
	# planned, bought for and equipped -- the only screen "between runs" means.
	# F5 still runs the same mission again without the detour.
	if _over_code != 0 and Input.is_action_just_pressed("ui_accept"):
		_open_start_screen()
		return

	# F5 restarts, whether the run is over or still going. ENTER no longer does:
	# the only control that restarts a run is this one. A function key on
	# purpose -- it sits away from WASD, so a run cannot be wiped by a stray
	# press in the middle of a firefight.
	if Input.is_action_just_pressed("restart"):
		_restart()
		return

	# game/ owns input sampling; sim/ never touches Input (spec §3.3).
	var mx: int = int(Input.is_action_pressed("move_right")) - int(Input.is_action_pressed("move_left"))
	var my: int = int(Input.is_action_pressed("move_down")) - int(Input.is_action_pressed("move_up"))

	# A drag in the HUD editor is a mouse button, and both mouse buttons are
	# bound to fire and aim -- so they are withheld while it is up, exactly as
	# they are while the loot panel is open. Both stay withheld while rummaging
	# even though only the right one loots now: a left click over an open panel
	# is far more likely to be a misaimed pick than a considered shot.
	var arranging: bool = _hud_ed != null and _hud_ed.active

	# Sorting your kit holds you still and takes your hands off the weapon.
	# Movement too, not just the buttons: the screen covers the view, and
	# walking blind into a room you cannot see is not a decision, it is an
	# accident.
	#
	# Read BEFORE the loot panel and the flags, because both of them are gated
	# on it. It used to be read after, and the flags were never gated on it at
	# all: the field view zeroed movement, said in this very comment that it
	# took your hands off the weapon, and then passed the trigger straight
	# through -- so every left click meant for the grid emptied a magazine into
	# whatever the player happened to be facing.
	var sorting: bool = _stash_screen != null and _stash_screen.active \
		and _stash_screen.mission_mode
	if sorting:
		mx = 0
		my = 0

	# world_mouse(), not the raw cursor: once the view scrolls, a screen-space
	# read aims at wherever that pixel USED to be in the world.
	var to_mouse: Vector2 = world_mouse() - _player_pos()
	var aim: int = int(to_mouse.angle() / TAU * 65536.0) & 65535

	# Read the body BEFORE stepping, so the panel shows the world the player is
	# currently looking at rather than the one after this tick.
	#
	# Never behind the field view, though: that screen covers the panel and
	# _open_mission_inventory hides it on the way in, so raising it again here
	# would put a live take-item click somewhere the player cannot see it. The
	# pick is driven by the raw right button rather than by the aim FLAG, so
	# withholding the flag alone would not have stopped it.
	var lt: PackedInt32Array = _bridge.GetLootTarget()

	# G is ONE key for two verbs: a press on a door swings it, a hold over a
	# body or chest rummages. Whichever is NEARER wins the press, so standing
	# in a doorway over a corpse still does what you are looking at.
	var door_pick: int = door_pick_for(Input.is_action_just_pressed("loot"), sorting,
		_bridge.GetDoorTarget(), lt, _bridge.LootTargetDist(lt[4]) if lt.size() >= 5 else -1)
	if door_pick > 0:
		_g_on_door = true
	elif not Input.is_action_pressed("loot"):
		_g_on_door = false

	var rummaging: bool = Input.is_action_pressed("loot") and lt.size() >= 5 \
		and not sorting and not _g_on_door

	# Which item the player clicked, as the sim's LootPick: 0 for none. Nothing
	# is taken by standing still any more, so this is the only way gear moves.
	var pick: int = 0

	if rummaging:
		_loot.show_for(lt[4], _bridge.GetLootKit(lt[4]))
		# Identifying is the cursor RESTING on a row -- the panel runs that dwell
		# itself, off frame delta. Taking is a RIGHT-CLICK, and only on something
		# already identified.
		#
		# Right specifically, not either button. Taking the LAST item empties the
		# kit, which ends the rummage on that same tick -- and the left button
		# still being held down then read as fire, so grabbing the last thing out
		# of a chest put a round into it. The loot button is now the one that is
		# NOT the trigger.
		if Input.is_action_just_pressed("aim"):
			pick = _loot.click_at(get_local_mouse_position())
	else:
		_loot.hide_panel()

	var flags: int = sim_flags(
		Input.is_action_pressed("fire"),
		Input.is_action_pressed("dilate"),
		Input.is_action_just_pressed("subdue"),
		Input.is_action_just_pressed("reload"),
		Input.is_action_pressed("aim"),
		Input.is_action_just_pressed("swap_weapon"),
		Input.is_action_pressed("loot"),
		not rummaging and not arranging and not sorting)

	# A drop the equipment screen staged this tick. Recorded intent like a loot
	# pick, so it survives a replay; consumed here so it fires exactly once.
	var drop: int = 0
	var dropping_item: int = -1
	if _stash_screen != null and _stash_screen.pending_drop >= 0:
		drop = _stash_screen.pending_drop + 1
		dropping_item = _stash_screen.pending_drop_item
		_stash_screen.pending_drop = -1

	# What the player is about to put down is something they plainly recognise,
	# so it needs no identify dwell when they pick it back up.
	if drop > 0 and _loot != null:
		_loot.mark_known(dropping_item)

	# One spawn per tick, because InputFrame carries ONE. The dev menu queues
	# them while it is open (the sim is not stepping then) and the queue drains
	# here, a tick each, so every conjured item is recorded exactly like a drop.
	var spawn: int = 0
	if not _spawn_queue.is_empty():
		spawn = _spawn_queue.pop_front()

	# An equip the field view staged. Recorded intent like a drop: the sim puts
	# it on inside the tick, because Loadout decides damage, spread, speed and
	# magazine size and every one of those feeds the hash.
	var equip: int = 0
	if _stash_screen != null and _stash_screen.pending_equip != 0:
		equip = _stash_screen.pending_equip
		_stash_screen.pending_equip = 0

	_bridge.Step(mx, my, aim, flags, pick, _move_tier, drop, spawn, equip, door_pick)
	_panels = _bridge.GetPanels()
	if drop > 0 or spawn > 0 or equip != 0:
		_stash_screen.refresh_pack()
	if spawn > 0 and _loot != null:
		# Same reasoning as a drop: you know perfectly well what you just
		# conjured, so it needs no identify dwell if you put it down again.
		_loot.mark_known(spawn)
	_consume_events()
	_observe_footsteps()

	# The visibility polygon is 400 raycasts against every wall rect. It depends
	# only on the player's position, so it belongs on the sim clock — computing
	# it in _process meant recomputing it every rendered frame for no benefit.
	# The EV_* constants above mirror Sim.SimEventKind by ordinal. If a kind is
	# ever inserted mid-enum, every sound and effect silently remaps to the
	# wrong event. Fail loudly instead.
	var kinds: int = _bridge.EventKindCount
	if kinds != EV_LAST + 1:
		push_error("event kind mismatch: sim has %d, game/ mirrors %d — re-check the EV_* constants"
			% [kinds, EV_LAST + 1])

	# The rail can extend how far the player sees (flashlight).
	_vision = _bridge.GetVisionPolygon(VISION_RAYS, _bridge.VisionRadiusPx(VISION_RADIUS))

	var world: PackedInt32Array = _bridge.GetWorld()
	var was: int = _over_code
	_over_code = world[6]
	# Auto-save the moment a run ends: the interesting runs are the ones that
	# ended badly, and nobody remembers to press a key at that moment.
	if was == 0 and _over_code != 0:
		_save_replay()
		_settle_run(world)


## Playback ignores live input for the SIM and repurposes those keys as
## transport controls: the recorded stream is the only thing driving the world.
func _physics_playback() -> void:
	if Input.is_action_just_pressed("dilate"):
		_pb_playing = not _pb_playing
	if Input.is_action_just_pressed("replay_faster"):
		_pb_speed = minf(_pb_speed * 2.0, 8.0)
	if Input.is_action_just_pressed("replay_slower"):
		_pb_speed = maxf(_pb_speed * 0.5, 0.125)
	if Input.is_action_just_pressed("restart"):
		_bridge.SeekTo(0)
		_consume_events()
		_steps.reset()
		return

	# Single-stepping while paused is the actual debugging affordance.
	if Input.is_action_just_pressed("move_right"):
		_pb_playing = false
		_bridge.StepPlayback()
		_consume_events()
		_observe_footsteps()
		return
	if Input.is_action_just_pressed("move_left"):
		_pb_playing = false
		_bridge.SeekTo(maxi(0, _bridge.ReplayIndex - 1))
		return

	if not _pb_playing or _bridge.ReplayFinished:
		return

	_pb_accum += _pb_speed
	while _pb_accum >= 1.0:
		_pb_accum -= 1.0
		_bridge.StepPlayback()
		if _bridge.ReplayFinished:
			break
	_consume_events()
	_observe_footsteps()


func _save_replay() -> void:
	DirAccess.make_dir_recursive_absolute(REPLAY_DIR)
	# The stamp only resolves to the second, so two saves inside one second --
	# F9 twice, or F9 on the tick a run ends -- built the same name and the
	# second write silently destroyed the first. Suffix until the name is free.
	var stamp: String = Time.get_datetime_string_from_system().replace(":", "-")
	var path: String = "%s/%s.txt" % [REPLAY_DIR, stamp]
	var n: int = 2
	while FileAccess.file_exists(path):
		path = "%s/%s-%d.txt" % [REPLAY_DIR, stamp, n]
		n += 1
	var f := FileAccess.open(path, FileAccess.WRITE)
	if f == null:
		push_error("could not write replay: %s" % path)
		return
	f.store_string(_bridge.GetReplayText())
	f.close()
	_saved_this_run = true
	_saved_path = ProjectSettings.globalize_path(path)
	_save_notice = 4.0
	print("cognitohazard: saved replay (%d ticks) -> %s" % [_bridge.RecordedTicks, _saved_path])


# ---- the camera, as pure functions -------------------------------------
#
# Static and side-effect free on purpose: a camera baked into _process cannot be
# tested in this harness, and the clamping is exactly the part that is easy to
# get subtly wrong at three of the four edges.

## The magnification to play `level` at.
##
## PLAY_ZOOM unless the level is smaller than ONE SCREEN at 1:1, in which case
## it is pulled in further so a small test level fills the screen rather than
## sitting in a letterbox. Never backs off below PLAY_ZOOM to fit a large floor
## -- that was the old behaviour and it kept the camera too far out to read a
## fight.
##
## One screen, not "the view at PLAY_ZOOM": with PLAY_ZOOM under 1 the
## reference level is smaller than the view, and pulling it in would play it
## at 1.0 while every larger level plays at PLAY_ZOOM -- moving between
## missions would change how big everything looks. At PLAY_ZOOM 1 or above the
## two rules are the same rule.
static func zoom_for(level: Vector2, view: Vector2) -> float:
	var fit: float = minf(view.x / maxf(1.0, level.x), view.y / maxf(1.0, level.y))
	var want: float = maxf(PLAY_ZOOM, fit) if fit > 1.0 else PLAY_ZOOM
	return clampf(want, MIN_ZOOM, MAX_ZOOM)


## Where the view wants to be: on the player, leaning toward what they are
## aiming at, by a capped amount.
static func camera_target(player: Vector2, cursor: Vector2) -> Vector2:
	var off: Vector2 = (cursor - player) * LOOK_AHEAD_FRAC
	if off.length() > LOOK_AHEAD_MAX:
		off = off.normalized() * LOOK_AHEAD_MAX
	return player + off


## Keep the visible rectangle inside the level. On an axis where the level is
## SMALLER than the view, centre instead of clamping -- otherwise a narrow level
## jams against one edge and leaves all the empty space on the other.
static func clamp_camera(cam: Vector2, level: Vector2, visible: Vector2) -> Vector2:
	var out: Vector2 = cam
	if level.x <= visible.x:
		out.x = level.x * 0.5
	else:
		out.x = clampf(cam.x, visible.x * 0.5, level.x - visible.x * 0.5)
	if level.y <= visible.y:
		out.y = level.y * 0.5
	else:
		out.y = clampf(cam.y, visible.y * 0.5, level.y - visible.y * 0.5)
	return out


## World size of the level currently loaded.
func level_size() -> Vector2:
	return Vector2(_bridge.GridWidthPx, _bridge.GridHeightPx)


## Screen point for a world point, and back. The world pass is drawn with
## `draw_set_transform(_view_origin, 0, _zoom)`, so these are that transform and
## its inverse, written out once.
func world_to_screen(w: Vector2) -> Vector2:
	return _view_origin + w * _zoom


func screen_to_world(s: Vector2) -> Vector2:
	return (s - _view_origin) / maxf(0.001, _zoom)


## The cursor in WORLD space. Every world-space mouse read goes through this;
## screen-space ones (the panels, which are pinned to the viewport) keep using
## get_local_mouse_position directly and say so.
func world_mouse() -> Vector2:
	return screen_to_world(get_local_mouse_position())


func _update_camera(delta: float, dilating: bool) -> void:
	var level: Vector2 = level_size()
	var view := Vector2(FIELD_W, FIELD_H)

	var want_zoom: float = zoom_for(level, view)
	if dilating:
		want_zoom *= DILATE_ZOOM
	# Ease the zoom too, or entering dilation snaps the whole world a step
	# smaller on one frame.
	_zoom = lerpf(_zoom, want_zoom, 1.0 - exp(-delta * CAM_FOLLOW)) if _cam_ready else want_zoom

	var visible: Vector2 = view / maxf(0.001, _zoom)
	var ppos: Vector2 = _player_pos()

	# The look-ahead needs the cursor in world space, which needs a transform
	# that is computed from the camera -- so use LAST frame's. One frame of lag
	# on a look-ahead offset is invisible, and it keeps the dependency acyclic.
	var target: Vector2 = camera_target(ppos, world_mouse())

	if _cam_ready:
		_cam = _cam.lerp(target, 1.0 - exp(-delta * CAM_FOLLOW))
	else:
		# First frame of a run: snap, so a restart does not sweep the camera
		# across the level from wherever the last run ended.
		_cam = target
		_cam_ready = true

	_cam = clamp_camera(_cam, level, visible)
	_recompute_view()


func _recompute_view() -> void:
	_view_origin = Vector2(FIELD_W, FIELD_H) * 0.5 - _cam * _zoom + _shake
	var visible: Vector2 = Vector2(FIELD_W, FIELD_H) / maxf(0.001, _zoom)
	_view_rect = Rect2(_cam - visible * 0.5, visible)


func _process(delta: float) -> void:
	if _bridge == null:
		return
	if _save_notice > 0.0:
		_save_notice -= delta
	if _notice_t > 0.0:
		_notice_t -= delta
	if _editor != null and _editor.active:
		return
	var world: PackedInt32Array = _bridge.GetWorld()
	var target: float = float(world[1]) / 200.0
	_visual_scale += (target - _visual_scale) * (1.0 - exp(-delta * 20.0))
	_audio.update_world_scale(_visual_scale)

	# Particles, shake and decals stay on the render clock: they are delta-based
	# and frame-rate independent, and they should stay smooth above 60 fps.
	_step_effects(delta)
	_update_camera(delta, world[3] == 1)
	queue_redraw()


func _restart() -> void:
	_bridge.Restart(run_seed())
	_deployed_kit = STASH.sim_kit(_bridge)
	_particles.clear()
	_decals.clear()
	_flashes.clear()
	_pops.clear()
	_arcs.clear()
	_steps.reset()
	_shake = Vector2.ZERO
	_cam_ready = false
	_over_code = 0
	_victory = false
	_playtest_run = false
	_saved_this_run = false
	# Anything the dev menu queued belonged to the run that just ended. Carrying
	# it over would conjure items into a run the player never asked for them in,
	# and would do it on a tick they cannot predict. A staged drop or equip is
	# worse still: both name a PACK PLACEMENT, and the new run's pack is a
	# different pack.
	_spawn_queue.clear()
	if _stash_screen != null:
		_stash_screen.pending_drop = -1
		_stash_screen.pending_drop_item = -1
		_stash_screen.pending_equip = 0
		_stash_screen.pending_equip_item = -1
	if _loot != null:
		_loot.hide_panel()
		_loot.forget_all()
	# Say the day's luck as the run begins. It decides what the chests hold,
	# so it is worth knowing before choosing which ones to walk to.
	_notice = luck_text(_bridge.Luck)
	_notice_t = 3.5


## Load whatever is in the editor's buffer and play it immediately. This is the
## authoring loop that matters: paint, ENTER, play, TAB, fix.
## Closing the equipment screen resolves what is worn into a Loadout and starts
## the mission again. A mission's kit is fixed at its start, which is the same
## bargain the loadout menu makes -- and the only honest one while the pack is
## sim state recorded in the replay.
## Deploying: resolve the chosen kit and begin. The sim has not stepped yet, so
## the restart here is what BEGINS the mission rather than restarting one — it
## is the only _restart() left besides F5, and removing it would mean deploying
## never built the world with the chosen level and kit.
func _on_deploy() -> void:
	# A bag taken off at base goes back on: no run starts without one.
	if _stash.ensure_pack():
		_save_stash()
	_stash.apply_to(_bridge)

	# The mission the screen is showing, loaded only if it is not already the
	# one in the bridge -- reloading the same file would be a pointless reparse
	# on every deploy.
	var chosen: String = _stash_screen.selected_level()
	if not chosen.is_empty() and chosen != level_path:
		if not _load_level(chosen):
			# Refuse to deploy into a level that would not read, rather than
			# dropping the player into the previous one without saying so.
			_notice = "could not read %s" % chosen.get_file()
			_notice_t = 4.0
			return

	_restart()
	# SILENT: `closed` means "the player backed out", and its handler goes to the
	# title. Deploying is not backing out -- closing loudly here deployed and then
	# immediately threw you to the main menu.
	_stash_screen.close_screen_silent()
	print("cognitohazard: deployed -> %s  on %s  pack %dx%d"
		% [_bridge.LoadoutText(), level_path.get_file(),
			_bridge.PackWidth, _bridge.PackHeight])


## Arranging the kit in detail before deploying. Coming back from the equipment
## screen returns to the start screen rather than dropping into the mission.
## The body in reach, the dwell filling, and the swap window. All three are
## things the player has to react to in the moment, so they belong on the field
## rather than behind a screen.
func _draw_loot_prompt() -> void:
	var swap: int = _bridge.SwapProgressQ8
	if swap > 0:
		var at: Vector2 = _hud.pos_of("swap")
		var w: float = _hud.size_of("swap").x
		_hud_label(at + Vector2(0, 8), "SWAPPING", w)
		draw_rect(Rect2(at.x, at.y + 12, w, 6), C_HUD_TROUGH)
		draw_rect(Rect2(at.x, at.y + 12, w * (swap / 256.0), 6), C_COLD)

	var t: PackedInt32Array = _bridge.GetLootTarget()

	# A door in reach, when it is what G would move. Asked through the same
	# rule the press uses, so the prompt can never name the other thing.
	var door: PackedInt32Array = _bridge.GetDoorTarget()
	if door_pick_for(true, false, door, t,
			_bridge.LootTargetDist(t[4]) if t.size() >= 5 else -1) > 0:
		var dpos := Vector2(door[1] / FX, door[2] / FX)
		var is_switch: bool = door.size() >= 6 and door[5] == 1
		var verb: String
		if is_switch:
			verb = "G  lights off" if door[3] == 1 else "G  lights on"
		else:
			verb = "G  close" if door[3] == 1 else "G  open"
		_world_pass()
		draw_string(_font, dpos + Vector2(-26, -18), verb,
			HORIZONTAL_ALIGNMENT_LEFT, -1, 11, C_LAMP if is_switch else C_DOOR_EDGE)
		_screen_pass()
		return

	if t.size() < 6:
		return

	var pos: Vector2 = Vector2(t[0] / FX, t[1] / FX)
	var fits: bool = t[3] == 1
	var col: Color = C_EXIT if fits else C_SIGNAL

	# The swap bar above is screen furniture; the ring below is ON the body, so
	# it is drawn in world space and the label with it.
	_world_pass()

	# A ring on the body itself, so the prompt is where the player is looking.
	# There is no dwell arc any more: nothing fills, because nothing is taken
	# until the player picks it out of the panel.
	draw_arc(pos, 16.0, 0.0, TAU, 20, col, 1.0)

	var label: String = "hold G  ·  %d item(s)" % t[2] if fits else "pack full"
	draw_string(_font, pos + Vector2(-34, -22), label,
		HORIZONTAL_ALIGNMENT_LEFT, -1, 11, col)

	_screen_pass()


## Staged, not applied. See _on_equipment_closed.
func _on_loadout_closed() -> void:
	_notice = "%s · %s  —  F5 to deploy with it" % [
		_bridge.CurrentWeaponName, _bridge.CurrentArmourName]
	_notice_t = 5.0
	print("cognitohazard: staged -> %s  (F5 to deploy)" % _bridge.LoadoutText())


func _start_playtest() -> void:
	_editor.close_editor()
	_bridge.EditorPlaytest(seed_value)
	_deployed_kit = STASH.sim_kit(_bridge)
	# Through the shared refresh, so playtesting a level of a different size
	# resets the camera along with the wall cache.
	_refresh_level_cache()
	_playback = false
	_over_code = 0
	_victory = false
	_playtest_run = true
	_saved_this_run = false
	_particles.clear()
	_decals.clear()
	_flashes.clear()
	_pops.clear()
	_arcs.clear()
	_steps.reset()
	_shake = Vector2.ZERO
	_vision = _bridge.GetVisionPolygon(VISION_RAYS, VISION_RADIUS)
	print("cognitohazard: playtesting '%s' (%d rects)" % [_bridge.EditorName, _walls.size() / 4])


## Pay for a finished run, once, on the tick it finishes.
##
## Extracting banks BOTH the fee-plus-evidence payout and everything in the
## mission pack, which lands in the stash as real items. Dying banks nothing and
## loses the pack — that asymmetry is the entire reason to walk to the exit
## rather than keep looting, and it is why the pack is worth filling at all.
func _settle_run(world: PackedInt32Array) -> void:
	if _playback:
		return      # watching a recording is not earning

	var file: String = level_path.get_file()

	if _over_code != 1:
		# DYING TAKES EVERYTHING ON YOU: the pack, what you were wearing, and
		# what was fitted to it. The stash GRID survives untouched, which is
		# the whole shape of the decision -- what you leave at base is safe,
		# and what you carry is not. Before this, a death cost only the loot
		# you had found, so walking in with everything you owned was free.
		_stash.lose_kit()
		# ...except the bag: every run starts with at least a satchel, so a
		# death never leaves the next one unable to carry anything home.
		_stash.ensure_pack()
		_stash.apply_to(_bridge)
		_campaign.settle_loss(file)
		# Dying scores zero across all three record tiers (spec §6.3); the
		# guards you dropped on the way down still count.
		_campaign.note_run(world[11], world[12], world[13], world[0], 0, 0, 0)
		_campaign.save()
		_save_stash()
		_notice = "killed in action — everything you carried is gone"
		_notice_t = 7.0
		return

	# Everything carried out goes into the stash. KEPT GEAR PAYS NOTHING — the
	# item is the reward, and paying cash for it as well meant one good chest
	# bought a rifle outright. Only what will not fit is sold, at the fence's
	# rate, so a full stash costs you value without costing you the find.
	# Where the recovered gear goes, and what the objective does, is
	# stash.bank_recovered -- which a harness can reach and this function
	# cannot (cognitohazard_loot_flow.md §7).
	# WHAT YOU ARE WEARING comes home too. Anything equipped in the field left
	# the pack, so reading the pack alone destroyed it -- and duplicated the
	# item it displaced, which went into the pack and was banked beside the
	# copy the stash's worn slot still held.
	if not _deployed_kit.is_empty():
		_stash.reconcile_worn(_deployed_kit, STASH.sim_kit(_bridge))
	var carried: PackedInt32Array = _bridge.GetPackItems()
	var banked: Array = _stash.bank_recovered(carried)
	# The staged Loadout is still the one you deployed in; F5 from the debrief
	# must deploy what you now wear.
	_stash.apply_to(_bridge)
	var kept: int = banked[0]
	var fenced_n: int = banked[1]
	var fenced_price: int = banked[2]
	var handed: int = banked[3]

	var fenced: int = _campaign.salvage_value(fenced_price)

	# Did the job get done? An extraction WITHOUT the objective is still an
	# extraction -- you keep every item -- but it pays nothing at all.
	var completed: bool = _bridge.ObjectivesMet
	var summary: PackedInt32Array = PackedInt32Array(
		_bridge.LevelSummary(FileAccess.get_file_as_string(level_path)))
	var mission_pay: int = MISSIONS.mission_payout(summary)

	var paid: int = _campaign.settle(file, completed, mission_pay,
		world[7], world[8], fenced, kept + fenced_n)
	_campaign.note_run(world[11], world[12], world[13], world[0],
		world[7], world[8], world[9])
	# THE WIN CONDITION: out of the Black Site with the objective. Recorded
	# before the save, so the ledger knows the game was won even if the window
	# closes on the victory screen.
	if MISSIONS.is_victory(level_path, completed) and not _playtest_run:
		_campaign.win()
		_victory = true
		_victory_rows = _mission_table()
	_campaign.save()
	_save_stash()

	if _victory:
		_notice = "THE BLACK SITE IS BROKEN — %d earned" % paid
	elif completed:
		_notice = "MISSION COMPLETE — %s%d earned, %d item(s) recovered%s" % [
			("case handed in  ·  " if handed > 0 else ""),
			paid, kept, ("  ·  %d fenced for %d" % [fenced_n, fenced]) if fenced_n > 0 else ""]
	else:
		# No fence either (the fence is part of the pay), so what the stash had
		# no room for is gone -- say so, rather than a count that hides it.
		_notice = "extracted without the objective — no pay, but %d item(s) kept%s" % [
			kept, ("  ·  %d lost, no room in the stash" % fenced_n) if fenced_n > 0 else ""]
	_notice_t = 9.0
	print("cognitohazard: %s — mission %d, records %d, salvage %d = %d  (balance %d)"
		% ["COMPLETE" if completed else "no objective",
			_campaign.last_mission, _campaign.last_records, _campaign.last_salvage,
			paid, _campaign.money])


func _save_stash() -> void:
	var f := FileAccess.open(EQUIP_SAVE, FileAccess.WRITE)
	if f != null:
		f.store_string(_stash.to_text())


func _player_pos() -> Vector2:
	var p: PackedInt32Array = _bridge.GetPlayer()
	return Vector2(p[0] / FX, p[1] / FX)


# ---------------------------------------------------------------- effects

func _consume_events() -> void:
	var ev: PackedInt32Array = _bridge.GetEvents()
	# A discharge arrives as its nodes in chain order (hop 0 the strike, each
	# later one joined to the last), so the chain is drawn from order alone.
	var arc_prev := Vector2.INF
	var i: int = 0
	while i < ev.size():
		var kind: int = ev[i]
		var pos := Vector2(ev[i + 1] / FX, ev[i + 2] / FX)
		var ang: float = ev[i + 3] / 65536.0 * TAU
		match kind:
			EV_PLAYER_SHOT:
				_audio.play(AUDIO.SHOT, _visual_scale)
				_flash(pos, ang, 1.0)
				_sparks(pos, ang, 5, Color(1.0, 0.85, 0.63))
				_eject_shell(pos, ang)
				_add_shake(shake_fire, ang + PI)
			EV_GUARD_SHOT:
				_audio.play(AUDIO.ESHOT, _visual_scale)
				_flash(pos, ang, 0.8)
				_sparks(pos, ang, 3, Color(1.0, 0.77, 0.54))
			EV_DRY_FIRE:
				_audio.play(AUDIO.DRY, _visual_scale)
				_add_shake(1.2, ang + PI)
			EV_WALL_HIT:
				_audio.play(AUDIO.WALL, _visual_scale)
				_sparks(pos, ang + PI, 7, Color(1.0, 0.88, 0.69))
				_decal(pos, _rng.randf_range(1.6, 3.0), Color(0, 0, 0, 0.5))
			EV_GUARD_KILLED:
				_audio.play(AUDIO.FLESH, _visual_scale)
				_blood(pos, ang)
				_add_shake(shake_kill, ang)
				if ev[i + 4] > 0:
					_pop(pos, "burned with him — %d" % ev[i + 4], C_SIGNAL)
			EV_PLAYER_KILLED:
				_audio.play(AUDIO.DEATH, _visual_scale)
				_blood(pos, ang)
				_add_shake(shake_death, ang)
			EV_PLAYER_HURT:
				# Being shot ends any sorting of the kit. The screen covers the
				# view and holds the player still, so leaving it up through a
				# firefight would be a way to be killed without ever seeing it.
				if _stash_screen != null and _stash_screen.active \
						and _stash_screen.mission_mode:
					_stash_screen.close_screen_silent()
					_notice = "hit — kit closed"
					_notice_t = 2.5
				_audio.play(AUDIO.FLESH, _visual_scale)
				_blood(pos, ang)
				_add_shake(shake_kill, ang)
			EV_ARMOUR_BROKEN:
				_audio.play(AUDIO.DESTROY, _visual_scale)
				_add_shake(shake_death * 0.8, ang)
				_pop(pos, "armour gone", C_SIGNAL)
			EV_GUARD_HURT:
				_audio.play(AUDIO.FLESH, _visual_scale)
				_blood(pos, ang)
			EV_GUARD_ARMOUR_HIT:
				# Sparks, not blood. A round a plate ate has to look different
				# from one that landed, or the player cannot tell a weapon that
				# is working from one that is not. Cold sparks, so they do not
				# read as the warm ones a wall throws.
				_audio.play(AUDIO.WALL, _visual_scale)
				_sparks(pos, ang + PI, 6, Color(0.74, 0.86, 1.0))
			EV_DROPPED:
				_audio.play(AUDIO.PICKUP, _visual_scale)
				_pop(pos, "dropped", C_WALL_EDGE)
			EV_EQUIPPED:
				_audio.play(AUDIO.PICKUP, _visual_scale)
				_pop(pos, "equipped %s" % _bridge.GearName(ev[i + 4]), C_COLD)
			EV_SPAWNED:
				# Deliberately not a pickup sound. A conjured item is not a
				# thing that happened in the fiction, and it should not read
				# like one.
				_pop(pos, "spawned %s" % _bridge.GearName(ev[i + 4]), C_AMBER)
			EV_GUARD_ARMOUR_BROKEN:
				_audio.play(AUDIO.DESTROY, _visual_scale)
				_sparks(pos, ang + PI, 12, Color(0.95, 0.80, 0.55))
				_pop(pos, "plate cracked", C_AMBER)
			EV_SUBDUE:
				_audio.play(AUDIO.SUBDUE, _visual_scale)
				_add_shake(3.0, ang)
				_pop(pos, "subdued", C_COLD)
			EV_PICKUP:
				_audio.play(AUDIO.PICKUP, _visual_scale)
				if ev[i + 4] > 0:
					_pop(pos, "+%d record%s" % [ev[i + 4], "s" if ev[i + 4] > 1 else ""], C_COLD)
			EV_DEGRADE:
				_audio.play(AUDIO.DEGRADE, _visual_scale)
				_add_shake(5.0)
				_pop(pos, "degraded", C_AMBER)
			EV_DESTROY:
				_audio.play(AUDIO.DESTROY, _visual_scale)
				_add_shake(7.0)
				_pop(pos, "destroyed", C_SIGNAL)
			EV_AIM_LOCKED:
				_audio.play(AUDIO.NOTICE, _visual_scale)
			EV_HEADSHOT:
				_audio.play(AUDIO.DESTROY, _visual_scale)
				_add_shake(shake_kill, ang)
				_pop(pos, "headshot", C_SIGNAL)
			EV_BODY_FOUND:
				_audio.play(AUDIO.BODYFOUND, _visual_scale)
				_pop(pos, "body found", C_SIGNAL)
			EV_RADIO_START:
				if pos.distance_to(_player_pos()) < RADIO_HEAR_PX or _in_view(pos, 0.0):
					_audio.play(AUDIO.RADIO_KEY, _visual_scale)
			EV_RADIO_SENT:
				if pos.distance_to(_player_pos()) < RADIO_HEAR_PX:
					_audio.play(AUDIO.RADIO_SENT, _visual_scale)
				# Heard or not, the player is TOLD a call went through: backup
				# on its way is the one thing a stealth player must not learn
				# by being flanked.
				_notice = ("radio chatter  ·  backup inbound" if ev[i + 4] == 1
					else "radio chatter  ·  something has been reported")
				_notice_t = 4.0
			EV_RADIO_CUT:
				if pos.distance_to(_player_pos()) < RADIO_HEAR_PX or _in_view(pos, 0.0):
					_audio.play(AUDIO.RADIO_CUT, _visual_scale)
				if _in_view(pos, 0.0):
					_pop(pos, "radio cut off", C_COLD)
			EV_AFRAID:
				if _in_view(pos, 0.0):
					_pop(pos, "!?", C_FEAR)
			EV_COMPROMISED:
				_audio.play(AUDIO.COMPROMISED, _visual_scale)
				_notice = "the floor is compromised  ·  they are hunting you"
				_notice_t = 6.0
			EV_RELOAD:
				_audio.play(AUDIO.RELOAD, _visual_scale)
			EV_NOTICE:
				_audio.play(AUDIO.NOTICE, _visual_scale)
			EV_ALERT:
				_audio.play(AUDIO.ALERT, _visual_scale)
			EV_EXIT:
				_pop(pos, "out", C_EXIT)
			EV_GLASS_BROKEN:
				# Loud on purpose, and heard from anywhere: the whole floor
				# heard it too.
				_audio.play(AUDIO.GLASS, _visual_scale)
				_shatter(pos, ang)
				_add_shake(2.5, ang)
			EV_DOOR_OPENED, EV_DOOR_CLOSED:
				var by_guard: bool = ev[i + 3] == 1
				if not by_guard or pos.distance_to(_player_pos()) < DOOR_HEAR_PX:
					_audio.play(AUDIO.DOOR_OPEN if kind == EV_DOOR_OPENED else AUDIO.DOOR_CLOSE,
						_visual_scale)
				# A door you did not touch swinging open is news; say so if it
				# is in view, because it means someone is on the other side.
				if by_guard and _in_view(pos, 0.0):
					_pop(pos, "a door opens", C_AMBER)
			EV_DOOR_BLOCKED:
				_audio.play(AUDIO.DRY, _visual_scale)
				_pop(pos, "doorway blocked", C_SIGNAL)
			EV_LAMP_BROKEN:
				# Glass's voice, higher and shorter; the floor heard it too.
				_audio.play(AUDIO.LAMP, _visual_scale)
				_shatter(pos, ang)
				_sparks(pos, _rng.randf_range(0.0, TAU), 7, C_LAMP)
			EV_LIGHTS_ON, EV_LIGHTS_OFF:
				var by_guard2: bool = ev[i + 3] == 1
				if not by_guard2 or pos.distance_to(_player_pos()) < DOOR_HEAR_PX:
					_audio.play(AUDIO.SWITCH, _visual_scale)
				# Someone else putting the lights back on is news.
				if by_guard2 and _in_view(pos, 0.0):
					_pop(pos, "the lights come on" if kind == EV_LIGHTS_ON else "the lights go out",
						C_LAMP)
			EV_WALL_PIERCED:
				# In one side and out the other: dust back toward the shooter
				# AND on through, so a wall the round went through never reads
				# like one that stopped it.
				_audio.play(AUDIO.WALL, _visual_scale)
				_sparks(pos, ang + PI, 6, Color(1.0, 0.88, 0.69))
				_sparks(pos + Vector2(cos(ang), sin(ang)) * 20.0, ang, 8, Color(0.62, 0.60, 0.56))
				_decal(pos, 2.4, Color(0, 0, 0, 0.6))
			EV_ARC_JUMP:
				var hop: int = ev[i + 4]
				if hop == 0 or is_inf(arc_prev.x):
					arc_prev = pos
					_audio.play(AUDIO.ZAP, _visual_scale)
				else:
					_arcs.append({"a": arc_prev, "b": pos, "t": 1.0})
					arc_prev = pos
				_sparks(pos, _rng.randf_range(0.0, TAU), 9, Color(0.70, 0.95, 1.0))
				_decal(pos, _rng.randf_range(3.0, 5.0), Color(0.02, 0.03, 0.05, 0.55))
				_add_shake(3.0)
				if ev[i + 3] == 1:
					_pop(pos, "the arc came back", C_SIGNAL)
			EV_GRENADE_THROWN:
				_audio.play(AUDIO.THROW, _visual_scale)
			EV_GRENADE_BOUNCE:
				if _in_view(pos, 0.0):
					_audio.play(AUDIO.TINK, _visual_scale)
			EV_BLAST:
				_audio.play(AUDIO.BOOM, _visual_scale)
				_blast(pos)
				var near: float = clampf(1.0 - pos.distance_to(_player_pos()) / 500.0, 0.25, 1.0)
				_add_shake(shake_death * 1.6 * near)
		i += 5


## A grenade going off. A white core, a fireball that blooms and dies in a
## fifth of a second, grit thrown well past it, smoke that hangs, and a scorch
## that stays -- the shrapnel itself is drawn as the rounds it is.
func _blast(pos: Vector2) -> void:
	for k in range(4):
		_flash(pos, _rng.randf_range(0.0, TAU), 2.2)
	for n in range(26):
		var a: float = _rng.randf_range(0.0, TAU)
		var sp: float = _rng.randf_range(40.0, 220.0)
		_particle(pos, Vector2(cos(a), sin(a)) * sp, _rng.randf_range(0.12, 0.32),
			_rng.randf_range(4.0, 9.0), Color(1.0, _rng.randf_range(0.45, 0.8), 0.2), "fire")
	for n in range(14):
		var a2: float = _rng.randf_range(0.0, TAU)
		_particle(pos, Vector2(cos(a2), sin(a2)) * _rng.randf_range(20.0, 70.0),
			_rng.randf_range(0.8, 1.6), _rng.randf_range(6.0, 12.0),
			Color(0.22, 0.22, 0.24, 0.55), "smoke")
	for n in range(18):
		var a3: float = _rng.randf_range(0.0, TAU)
		_particle(pos, Vector2(cos(a3), sin(a3)) * _rng.randf_range(260.0, 520.0),
			_rng.randf_range(0.10, 0.25), 1.4, Color(1.0, 0.85, 0.55), "spark")
	_decal(pos, 16.0, Color(0.0, 0.0, 0.0, 0.45))
	_decal(pos, 9.0, Color(0.02, 0.02, 0.02, 0.6))


func _add_shake(mag: float, ang: float = INF) -> void:
	var a: float = _rng.randf_range(0.0, TAU) if is_inf(ang) else ang
	_shake += Vector2(cos(a), sin(a)) * mag


func _flash(pos: Vector2, ang: float, scale: float) -> void:
	_flashes.append({"pos": pos, "ang": ang, "t": 1.0, "s": scale})


func _pop(pos: Vector2, text: String, col: Color) -> void:
	_pops.append({"pos": pos + Vector2(0, -22), "t": 1.9, "text": text, "col": col})


func _decal(pos: Vector2, radius: float, col: Color, rot: float = 0.0) -> void:
	_decals.append({"pos": pos, "r": radius, "col": col, "rot": rot})
	while _decals.size() > decal_cap:
		_decals.pop_front()


func _particle(pos: Vector2, vel: Vector2, life: float, size: float, col: Color, kind: String) -> void:
	_particles.append({
		"pos": pos, "vel": vel, "life": life, "max_life": life,
		"size": size, "col": col, "kind": kind,
		"rot": _rng.randf_range(0.0, TAU), "spin": _rng.randf_range(-12.0, 12.0),
	})


func _sparks(pos: Vector2, ang: float, count: int, col: Color) -> void:
	for i in range(count):
		var a: float = ang + _rng.randf_range(-0.5, 0.5)
		var sp: float = _rng.randf_range(90.0, 260.0)
		_particle(pos, Vector2(cos(a), sin(a)) * sp, _rng.randf_range(0.08, 0.22), 1.6, col, "spark")


## A pane going. Shards fly ALONG the round's heading and out of both faces,
## glitter as they tumble, and settle as a scatter that stays: a broken window
## you walk past later should still look like one.
func _shatter(pos: Vector2, ang: float) -> void:
	for i in range(22):
		var through: bool = _rng.randf() < 0.7
		var a: float = (ang if through else ang + PI) + _rng.randf_range(-0.9, 0.9)
		var sp: float = _rng.randf_range(60.0, 280.0)
		_particle(pos + Vector2(_rng.randf_range(-8, 8), _rng.randf_range(-8, 8)),
			Vector2(cos(a), sin(a)) * sp, _rng.randf_range(0.25, 0.7),
			_rng.randf_range(1.5, 3.2), Color(0.78, 0.92, 1.0, 0.9), "shard")


func _blood(pos: Vector2, ang: float) -> void:
	for i in range(16):
		var a: float = ang + _rng.randf_range(-0.7, 0.7)
		var sp: float = _rng.randf_range(60.0, 240.0)
		_particle(pos, Vector2(cos(a), sin(a)) * sp, _rng.randf_range(0.25, 0.6),
			_rng.randf_range(1.4, 3.0), Color(0.55, 0.10, 0.08), "blood")
	for i in range(4):
		var a2: float = _rng.randf_range(0.0, TAU)
		var d: float = _rng.randf_range(2.0, 12.0)
		_decal(pos + Vector2(cos(a2), sin(a2)) * d, _rng.randf_range(2.0, 4.5),
			Color(0.38, 0.08, 0.06, 0.5))


func _eject_shell(pos: Vector2, ang: float) -> void:
	var a: float = ang + (PI * 0.5) * (1.0 if _rng.randf() < 0.5 else -1.0) + _rng.randf_range(-0.35, 0.35)
	var sp: float = _rng.randf_range(110.0, 190.0)
	_particle(pos, Vector2(cos(a), sin(a)) * sp, 0.55, 1.7, Color(0.59, 0.45, 0.19), "shell")


func _step_effects(delta: float) -> void:
	_shake *= exp(-delta * shake_decay)

	for i in range(_particles.size() - 1, -1, -1):
		var q: Dictionary = _particles[i]
		q["pos"] += q["vel"] * delta
		q["rot"] += q["spin"] * delta
		var drag: float = 2.6 if q["kind"] == "shell" else (4.2 if q["kind"] == "blood" \
			else (3.4 if q["kind"] == "shard" else (2.0 if q["kind"] == "smoke" else 7.0)))
		q["vel"] -= q["vel"] * drag * delta
		q["life"] -= delta
		if q["life"] <= 0.0:
			if q["kind"] == "shard":
				_decal(q["pos"], _rng.randf_range(0.8, 1.6), Color(0.70, 0.86, 0.95, 0.45))
			elif q["kind"] == "shell":
				# Shells settle into a permanent decal rather than vanishing.
				_decal(q["pos"], 1.7, Color(0.59, 0.45, 0.19, 0.6), q["rot"])
			elif q["kind"] == "blood":
				_decal(q["pos"], _rng.randf_range(1.4, 3.2), Color(0.38, 0.08, 0.05, 0.5))
			_particles.remove_at(i)

	for i in range(_flashes.size() - 1, -1, -1):
		_flashes[i]["t"] -= delta * 13.0
		if _flashes[i]["t"] <= 0.0:
			_flashes.remove_at(i)

	for i in range(_arcs.size() - 1, -1, -1):
		_arcs[i]["t"] -= delta * 5.0
		if _arcs[i]["t"] <= 0.0:
			_arcs.remove_at(i)

	_steps.step(delta)

	for i in range(_pops.size() - 1, -1, -1):
		_pops[i]["t"] -= delta
		_pops[i]["pos"] += Vector2(0, -12.0 * delta)
		if _pops[i]["t"] <= 0.0:
			_pops.remove_at(i)


# ----------------------------------------------------------------- drawing

## The world pass runs under one transform: world units in, screen pixels out.
## Anything that needs its OWN rotation (a body, a spinning shell) composes on
## top of it through _world_xform rather than replacing it, which is the bug
## that would put actors back at 1:1 while the floor scrolled beneath them.
func _world_pass() -> void:
	draw_set_transform(_view_origin, 0.0, Vector2(_zoom, _zoom))


func _world_xform(pos: Vector2, ang: float) -> void:
	draw_set_transform(world_to_screen(pos), ang, Vector2(_zoom, _zoom))


func _screen_pass() -> void:
	draw_set_transform(Vector2.ZERO, 0.0, Vector2.ONE)


## Is this world-space point worth drawing? The margin covers actors whose
## centre is off screen but whose body, bar or label is not.
func _in_view(pos: Vector2, margin: float = 40.0) -> bool:
	return _view_rect.grow(margin).has_point(pos)


func _draw() -> void:
	if _fatal != "":
		_draw_fatal()
		return
	if _bridge == null:
		return
	if _editor != null and _editor.active:
		return
	if _menu != null and _menu.active:
		return
	_draw_count += 1

	# Between runs there is no world to show. The base stash and the shop are
	# translucent -- the same panels serve the field view, which sits over the
	# mission -- so drawing the world under them put the stash over wherever the
	# last run ended: the body, the blood, the debrief. The backdrop is the
	# title's, so the stash after a death is the stash you reach from Continue.
	if _between_runs_screen_up():
		_screen_pass()
		draw_rect(Rect2(0, 0, FIELD_W, FIELD_H + 60.0), C_BASE)
		return

	var player: PackedInt32Array = _bridge.GetPlayer()
	var world: PackedInt32Array = _bridge.GetWorld()
	var ppos := Vector2(player[0] / FX, player[1] / FX)

	# Beyond the level's own edge there is nothing, and it should look like
	# nothing rather than like more floor. Painted in SCREEN space so it covers
	# the whole viewport whatever the camera is doing.
	_screen_pass()
	draw_rect(Rect2(0, 0, FIELD_W, FIELD_H), C_BEYOND)

	_world_pass()

	# The lit region. Everything outside it stays at the darker floor tone, so
	# concealment reads as "the walls are hiding you", which is the only source
	# of concealment in the game (spec §9).
	if _art.ready:
		# The kit's floors are drawn once, LIT, and everything outside the
		# polygon is dimmed -- so each room keeps its own floor in the dark.
		_art.draw_floor(self, _view_rect)
		_art.draw_unlit(self, _vision, ppos, level_size(), C_BEYOND)
	else:
		# The floor is the LEVEL, not the viewport. These were the same
		# rectangle while a level was exactly one screen; they are not any more.
		draw_rect(Rect2(Vector2.ZERO, level_size()), C_FLOOR)
		if _vision.size() >= 3:
			draw_colored_polygon(_vision, C_FLOOR_LIT)

	_draw_exit()
	_draw_caches()

	for d in _decals:
		if _in_view(d["pos"], 8.0):
			draw_circle(d["pos"], d["r"], d["col"])

	_draw_walls()
	_draw_panels()
	_draw_ground()
	_draw_chests()
	# Everything above is the level and what lies in it, and is darkened by
	# the light. Everything below is lit its own way: lamps and beams glow,
	# guards are dimmed by how well the player can make them out, and the
	# player is always themselves.
	_draw_darkness()
	_draw_lamps()
	_draw_torch_beams()
	_draw_guards()
	_draw_footsteps()
	_draw_ai_debug()
	_draw_bullets()
	_draw_arcs()

	for q in _particles:
		var fade: float = clampf(q["life"] / q["max_life"], 0.0, 1.0)
		var c: Color = q["col"]
		c.a = fade
		if not _in_view(q["pos"], 16.0):
			continue
		if q["kind"] == "shell":
			_world_xform(q["pos"], q["rot"])
			draw_rect(Rect2(-2.0, -1.0, 4.0, 2.0), c)
			_world_pass()
		elif q["kind"] == "shard":
			_world_xform(q["pos"], q["rot"])
			draw_colored_polygon(PackedVector2Array([Vector2(-q["size"], -0.6),
				Vector2(q["size"], 0.0), Vector2(-q["size"] * 0.4, 1.2)]), c)
			_world_pass()
		elif q["kind"] == "smoke":
			# Smoke grows as it fades rather than shrinking like a spark.
			draw_circle(q["pos"], q["size"] * (2.0 - fade), c)
		else:
			draw_circle(q["pos"], q["size"] * fade, c)

	if player[3] == 1:
		_draw_human(ppos, player[2] / 65536.0 * TAU, C_PLAYER, player[8] / 256.0,
			player[10], player[11])

	for f in _flashes:
		if _in_view(f["pos"], 40.0):
			_draw_flash(f)

	for p in _pops:
		var col: Color = p["col"]
		col.a = clampf(p["t"], 0.0, 1.0)
		draw_string(_font, p["pos"], p["text"], HORIZONTAL_ALIGNMENT_CENTER, -1, 11, col)

	_screen_pass()
	_draw_offscreen_markers()
	_draw_ai_debug_header()
	_draw_hud(player, world)
	_draw_loot_prompt()


## The F3 AI debug overlay, drawn by game/ai_debug_overlay.gd (a pure function
## of the snapshot, so the harness can draw it too). WORLD pass.
func _draw_ai_debug() -> void:
	if not _ai_debug:
		return
	if _task_names.is_empty():
		_task_names = _bridge.GuardTaskNames()
		_state_names = _bridge.GuardStateNames()
	var got: Dictionary = AI_DEBUG_OVERLAY.draw(self, _bridge.GetAiDebug(), _font,
		_state_names, _task_names)
	_dbg_intel_age = got["intel_age"]
	_dbg_compromised = got["compromised"]


## The overlay's own line, in the SCREEN pass, so it stays put and readable.
func _draw_ai_debug_header() -> void:
	if not _ai_debug:
		return
	var line: String = "AI DEBUG (F3)"
	if _dbg_intel_age >= 0:
		line += "  ·  intel %.1f s old" % (_dbg_intel_age / 60.0)
	if _dbg_compromised:
		line += "  ·  COMPROMISED"
	draw_string(_font, Vector2(12, 18), line, HORIZONTAL_ALIGNMENT_LEFT, -1, 11, C_AMBER)


## A black window with an error buried in the console is the worst possible
## failure here, because the cause is "you launched the wrong application" and
## nothing on screen says so.
func _draw_fatal() -> void:
	draw_rect(Rect2(0, 0, FIELD_W, FIELD_H + HUD_H), Color(0.06, 0.03, 0.03))
	draw_string(_font, Vector2(0, FIELD_H * 0.40), _fatal,
		HORIZONTAL_ALIGNMENT_CENTER, FIELD_W, 22, Color(0.95, 0.55, 0.50))
	draw_string(_font, Vector2(0, FIELD_H * 0.40 + 34),
		"Cognitohazard's sim layer is C#. This Godot build has no C# support,",
		HORIZONTAL_ALIGNMENT_CENTER, FIELD_W, 13, Color(0.78, 0.74, 0.74))
	draw_string(_font, Vector2(0, FIELD_H * 0.40 + 52),
		"so it cannot load res://game/SimBridge.cs.",
		HORIZONTAL_ALIGNMENT_CENTER, FIELD_W, 13, Color(0.78, 0.74, 0.74))
	draw_string(_font, Vector2(0, FIELD_H * 0.40 + 90),
		"Open the project with:   /Applications/Godot-4.6-dotnet.app",
		HORIZONTAL_ALIGNMENT_CENTER, FIELD_W, 15, Color(0.55, 0.85, 0.70))
	draw_string(_font, Vector2(0, FIELD_H * 0.40 + 116),
		"(Godot-4.6-standard.app on this machine is the non-.NET build)",
		HORIZONTAL_ALIGNMENT_CENTER, FIELD_W, 11, Color(0.52, 0.50, 0.50))


func _draw_walls() -> void:
	# The kit draws walls cell by cell from the grid, props on the free-standing
	# blocks; the merged rects below are the fallback, and stay what the sim uses.
	if _art.ready:
		_art.draw_structure(self, _view_rect)
		return
	var view: Rect2 = _view_rect.grow(4.0)
	var i: int = 0
	while i < _walls.size():
		var r := Rect2(_walls[i] / FX, _walls[i + 1] / FX, _walls[i + 2] / FX, _walls[i + 3] / FX)
		if view.intersects(r):
			draw_rect(r, C_WALL)
			draw_rect(r, C_WALL_EDGE, false, 1.0)
		i += 4


## Glass and doors, drawn over the floor where the wall merge left a gap for
## them. They are NOT in _walls: those are cached per level, and these change.
func _draw_panels() -> void:
	var view: Rect2 = _view_rect.grow(24.0)
	var i: int = 0
	while i + PANEL_STRIDE <= _panels.size():
		var r := Rect2(_panels[i + 1] / FX, _panels[i + 2] / FX,
			_panels[i + 3] / FX, _panels[i + 4] / FX)
		var open: bool = (_panels[i + 5] & 1) != 0
		var vertical: bool = (_panels[i + 5] & 2) != 0
		if view.intersects(r):
			if _panels[i] == PANEL_GLASS:
				_draw_glass(r, open, vertical)
			else:
				_draw_door(r, open, vertical)
		i += PANEL_STRIDE


## A pane: a cold, see-through fill with a hard bright line down its length and
## a glint, so it reads as GLASS from across a room and never as floor. A
## broken one is its frame and a few teeth left in it, and is walkable.
func _draw_glass(r: Rect2, broken: bool, vertical: bool) -> void:
	var mid_a: Vector2
	var mid_b: Vector2
	if vertical:
		mid_a = Vector2(r.get_center().x, r.position.y)
		mid_b = Vector2(r.get_center().x, r.end.y)
	else:
		mid_a = Vector2(r.position.x, r.get_center().y)
		mid_b = Vector2(r.end.x, r.get_center().y)

	if broken:
		# The frame's ends, and jagged teeth pointing inward from them.
		var along: Vector2 = (mid_b - mid_a).normalized()
		var across := Vector2(-along.y, along.x)
		for end in [[mid_a, along], [mid_b, -along]]:
			var at: Vector2 = end[0]
			var dir: Vector2 = end[1]
			draw_line(at - across * 5.0, at + across * 5.0, C_WALL_EDGE, 2.0)
			draw_colored_polygon(PackedVector2Array([
				at - across * 3.0, at + across * 3.0, at + dir * 7.0 + across * 1.0]),
				Color(C_GLASS.r, C_GLASS.g, C_GLASS.b, 0.55))
		return

	draw_rect(r, Color(C_GLASS.r, C_GLASS.g, C_GLASS.b, 0.16))
	draw_line(mid_a, mid_b, C_GLASS, 2.0)
	# Two short diagonal glints, fixed per pane so they do not shimmer.
	var c: Vector2 = r.get_center()
	var g: float = 4.0
	draw_line(c + Vector2(-g, g), c + Vector2(g, -g), Color(1, 1, 1, 0.35), 1.0)
	draw_line(c + Vector2(-g + 5, g + 3), c + Vector2(g + 3, -g + 5), Color(1, 1, 1, 0.20), 1.0)


## A door: a solid warm leaf in its frame when shut -- it hides what is behind
## it, so it must read as solid as a wall -- and when open, the leaf swung
## square to the wall on its hinge with the swing arc ghosted in, so an open
## door is recognisable as one, and not mistaken for a gap in the wall.
func _draw_door(r: Rect2, open: bool, vertical: bool) -> void:
	# The kit's sliding pair when there is a kit; this swing door otherwise.
	if _art.draw_door(self, r, open, vertical):
		return
	# Jambs at both ends, the part of the frame that is always there.
	var jamb: float = 3.0
	if vertical:
		draw_rect(Rect2(r.position.x, r.position.y, r.size.x, jamb), C_WALL_EDGE)
		draw_rect(Rect2(r.position.x, r.end.y - jamb, r.size.x, jamb), C_WALL_EDGE)
	else:
		draw_rect(Rect2(r.position.x, r.position.y, jamb, r.size.y), C_WALL_EDGE)
		draw_rect(Rect2(r.end.x - jamb, r.position.y, jamb, r.size.y), C_WALL_EDGE)

	if not open:
		var leaf: Rect2 = r.grow_individual(
			0.0 if vertical else -jamb, -jamb if vertical else 0.0,
			0.0 if vertical else -jamb, -jamb if vertical else 0.0)
		leaf = leaf.grow_individual(
			-5.0 if vertical else 0.0, 0.0 if vertical else -5.0,
			-5.0 if vertical else 0.0, 0.0 if vertical else -5.0)
		draw_rect(leaf, C_DOOR)
		draw_rect(leaf, C_DOOR_EDGE, false, 1.0)
		# Where a double door meets, and a handle beside it.
		var seam_a: Vector2
		var seam_b: Vector2
		if vertical:
			seam_a = Vector2(leaf.position.x, leaf.get_center().y)
			seam_b = Vector2(leaf.end.x, leaf.get_center().y)
		else:
			seam_a = Vector2(leaf.get_center().x, leaf.position.y)
			seam_b = Vector2(leaf.get_center().x, leaf.end.y)
		draw_line(seam_a, seam_b, C_DOOR_EDGE, 1.0)
		draw_circle((seam_a + seam_b) * 0.5 + (Vector2(0, 4) if vertical else Vector2(4, 0)),
			1.4, C_DOOR_EDGE)
		return

	# Open: hinged at the top (vertical) or left (horizontal) jamb, swung 90
	# degrees into the room below / to the right.
	var hinge: Vector2
	var tip: Vector2
	var length: float
	if vertical:
		length = r.size.y - jamb * 2.0
		hinge = Vector2(r.get_center().x, r.position.y + jamb)
		tip = hinge + Vector2(length, 0)
		draw_arc(hinge, length, 0.0, PI * 0.5, 12, Color(C_DOOR.r, C_DOOR.g, C_DOOR.b, 0.25), 1.0)
	else:
		length = r.size.x - jamb * 2.0
		hinge = Vector2(r.position.x + jamb, r.get_center().y)
		tip = hinge + Vector2(0, length)
		draw_arc(hinge, length, 0.0, PI * 0.5, 12, Color(C_DOOR.r, C_DOOR.g, C_DOOR.b, 0.25), 1.0)
	draw_line(hinge, tip, C_DOOR, 4.0)
	draw_line(hinge, tip, C_DOOR_EDGE, 1.0)


func _draw_exit() -> void:
	for rect: Rect2 in _exit_rects:
		draw_rect(rect, Color(C_EXIT.r, C_EXIT.g, C_EXIT.b, 0.16))
		draw_rect(rect, C_EXIT, false, 1.5)
		draw_string(_font, rect.position + Vector2(4, 14), "EXIT",
			HORIZONTAL_ALIGNMENT_LEFT, -1, 10, C_EXIT)


## Gear chests. An emptied one still draws, as an open outline, so a floor you
## have already searched reads as searched from across the room.
## Gear the player put down. Drawn small and cool, so a pile of your own kit
## never reads as a chest you have not opened.
## Dropped gear, drawn as a BAG rather than the 10x8 smudge it used to be.
##
## A pile is a real loot target -- same G, same panel, same index space as a
## body or a chest -- so it has to read as one from across a room. At the old
## size, on a dark floor, dropping something looked like nothing happening,
## which is half of why dropping seemed not to work at all.
##
## Sized like a chest and given the same flap line, because that is the shape
## the player has already learnt means "there is gear in this".
func _draw_ground() -> void:
	var g: PackedInt32Array = _bridge.GetGround()
	var i: int = 0
	while i < g.size():
		var pos := Vector2(g[i] / FX, g[i + 1] / FX)
		var count: int = g[i + 2]
		if count > 0 and _in_view(pos, 16.0):
			var body := Rect2(pos - Vector2(9, 7), Vector2(18, 14))
			draw_rect(body, Color(C_GROUND.r, C_GROUND.g, C_GROUND.b, 0.30))
			draw_rect(body, C_GROUND, false, 1.5)
			# The flap, and the two straps that make it a bag rather than a
			# crate -- a chest is authored into a level, a bag is something the
			# player put there, and they should not be confused.
			draw_line(pos + Vector2(-9, -3), pos + Vector2(9, -3), C_GROUND, 1.0)
			draw_line(pos + Vector2(-4, -7), pos + Vector2(-4, -3), C_GROUND, 1.0)
			draw_line(pos + Vector2(4, -7), pos + Vector2(4, -3), C_GROUND, 1.0)
			if count > 1:
				draw_string(_font, pos + Vector2(11, -3), str(count),
					HORIZONTAL_ALIGNMENT_LEFT, -1, 10, C_GROUND)
		i += 3


func _draw_chests() -> void:
	var c: PackedInt32Array = _bridge.GetChests()
	var i: int = 0
	while i < c.size():
		var pos := Vector2(c[i] / FX, c[i + 1] / FX)
		if _in_view(pos, 14.0):
			# A negative count marks an objective site: -1 empty, -2 holding one.
			var raw: int = c[i + 2]
			var objective: bool = raw < 0
			var items: int = (-raw - 1) if objective else raw
			var col: Color = C_OBJECTIVE if objective else C_CHEST
			var box := Rect2(pos - Vector2(8, 6), Vector2(16, 12))
			if items > 0:
				draw_rect(box, Color(col.r, col.g, col.b, 0.25))
				draw_rect(box, col, false, 1.5)
				draw_line(pos + Vector2(-8, -1), pos + Vector2(8, -1), col, 1.0)
				if objective:
					# A ring, so the one thing the mission is about is findable
					# across a room rather than only by walking onto it.
					draw_arc(pos, 15.0, 0.0, TAU, 24, Color(col.r, col.g, col.b, 0.7), 1.0)
			else:
				draw_rect(box, C_CHEST_EMPTY, false, 1.0)
		i += 3


func _draw_caches() -> void:
	var c: PackedInt32Array = _bridge.GetCaches()
	var i: int = 0
	while i < c.size():
		if c[i + 2] == 0:
			var pos := Vector2(c[i] / FX, c[i + 1] / FX)
			if _in_view(pos, 12.0):
				draw_rect(Rect2(pos - Vector2(6, 6), Vector2(12, 12)), C_CACHE, false, 1.5)
				draw_rect(Rect2(pos - Vector2(2.5, 2.5), Vector2(5, 5)), C_CACHE)
		i += 3


## Tracers are drawn as a streak the length of roughly one tick of flight, off
## the round's own speed. At the retuned muzzle velocities a fixed 7px tail read
## as a scatter of dots; this keeps the shot legible AND keeps a subsonic round
## visibly slower than a rifle round, which is a stat the player paid for.
func _draw_bullets() -> void:
	var b: PackedInt32Array = _bridge.GetBullets()
	var i: int = 0
	while i < b.size():
		var pos := Vector2(b[i] / FX, b[i + 1] / FX)
		if not _in_view(pos, 50.0):
			i += BULLET_STRIDE
			continue
		var kind: int = b[i + 5]
		if kind == BK_GRENADE:
			_draw_grenade(pos, b[i + 6])
			i += BULLET_STRIDE
			continue
		var ang: float = b[i + 2] / 65536.0 * TAU
		var streak: float = clampf(b[i + 4] / FX / 60.0, 6.0, 46.0)
		var tail: Vector2 = pos - Vector2(cos(ang), sin(ang)) * streak
		if kind == BK_FRAG:
			# Shrapnel: short, hot and many. It is the pattern that reads.
			draw_line(pos - Vector2(cos(ang), sin(ang)) * 7.0, pos, Color(1.0, 0.66, 0.30), 1.4)
			i += BULLET_STRIDE
			continue
		if kind == BK_ARC:
			# A bolt, not a tracer: white-hot core in a cyan sheath.
			draw_line(tail, pos, Color(0.45, 0.85, 1.0, 0.5), 5.0)
			draw_line(tail, pos, Color(0.92, 0.98, 1.0), 2.0)
			i += BULLET_STRIDE
			continue
		# Energy bolts travel at twice anything ballistic, so speed is an honest
		# discriminator and needs no extra column in the snapshot.
		var energy: bool = b[i + 4] > 4000 * FX
		var col: Color
		if energy:
			col = Color(0.55, 0.95, 1.0)
		else:
			col = Color(1.0, 0.93, 0.72) if b[i + 3] == 1 else Color(1.0, 0.72, 0.52)
		var width: float = 2.2 if energy else 1.6
		if kind == BK_PIERCE:
			width = 2.4   # the heaviest round in the game should look it
		draw_line(tail, pos, col, width)
		i += BULLET_STRIDE


## A grenade on the floor: a dark body and a fuse light that blinks faster as
## the fuse runs down, so how long you have is readable at a glance.
func _draw_grenade(pos: Vector2, fuse_ticks: int) -> void:
	draw_circle(pos, 4.2, Color(0.12, 0.14, 0.10))
	draw_circle(pos, 3.4, Color(0.30, 0.36, 0.22))
	var rate: float = lerpf(18.0, 4.0, clampf(fuse_ticks / 96.0, 0.0, 1.0))
	if fmod(Time.get_ticks_msec() / 1000.0 * rate, 1.0) < 0.5:
		draw_circle(pos + Vector2(1.6, -1.6), 1.3, Color(1.0, 0.25, 0.2))


## Lightning between the nodes of a discharge. Jagged, re-rolled every frame,
## a wide faint sheath under a thin bright core, fading over a fifth of a
## second -- long enough to see the chain the shot just took.
func _draw_arcs() -> void:
	for arc in _arcs:
		var a: Vector2 = arc["a"]
		var bb: Vector2 = arc["b"]
		var t: float = clampf(arc["t"], 0.0, 1.0)
		var d: Vector2 = bb - a
		var n := Vector2(-d.y, d.x).normalized()
		var segs: int = maxi(4, int(d.length() / 14.0))
		var pts := PackedVector2Array()
		for k in range(segs + 1):
			var f: float = float(k) / segs
			var off: float = 0.0 if k == 0 or k == segs else _rng.randf_range(-9.0, 9.0)
			pts.append(a + d * f + n * off)
		draw_polyline(pts, Color(0.45, 0.80, 1.0, 0.35 * t), 6.0)
		draw_polyline(pts, Color(0.90, 0.97, 1.0, t), 1.8)


func _draw_guards() -> void:
	var g: PackedInt32Array = _bridge.GetGuards()
	# Lighting: how well the player makes each guard out. Empty on a lit
	# level, where every guard in line of sight is drawn plainly, as before.
	var sight: PackedInt32Array = _bridge.GetGuardSight() if _bridge.HasLight \
		else PackedInt32Array()
	var i: int = 0
	while i < g.size():
		var pos := Vector2(g[i] / FX, g[i + 1] / FX)
		var state: int = g[i + 3]
		var visible: bool = g[i + 7] == 1
		var armour: int = g[i + 8]
		var armour_max: int = g[i + 9]
		var task: int = g[i + 10]
		var radio_q8: int = g[i + 11]
		var afraid: bool = g[i + 12] == 1

		if not _in_view(pos, 32.0):
			i += GUARD_STRIDE
			continue

		if state == ST_DEAD or state == ST_DOWN:
			# Corpses and subdued guards read differently at a glance: reading a
			# room's history is load-bearing for the record economy (spec §9).
			var col: Color = C_DEAD if state == ST_DEAD else C_DOWN
			var roll: float = g[i + 6] / 65536.0 * TAU
			_draw_prone(pos, g[i + 5] / 65536.0 * TAU + roll, col)
		elif visible:
			var k: int = i / GUARD_STRIDE
			var seen: int = sight[k * 2] if k * 2 < sight.size() else 256
			if seen >= SEE_CLEAR:
				_draw_human(pos, g[i + 2] / 65536.0 * TAU, C_GUARD, 0.0,
					armour, armour_max)
				_draw_armour_bar(pos, armour, armour_max)
				_draw_awareness(pos, g[i + 4], state, afraid)
				if task == TASK_RADIO:
					_draw_radio(pos, radio_q8)
			elif seen >= SEE_OUTLINE:
				# A shape in the gloom: dimmed toward the floor, the meter
				# still readable, the plate not -- you cannot see what he wears.
				var dim: float = float(seen - SEE_OUTLINE) / float(SEE_CLEAR - SEE_OUTLINE)
				_draw_human(pos, g[i + 2] / 65536.0 * TAU,
					C_GUARD.lerp(C_NIGHT, 0.65 - 0.35 * dim), 0.0, 0, 0)
				_draw_awareness(pos, g[i + 4], state, afraid)
			elif seen > 0:
				# Pitch dark and close: an outline, nothing more.
				draw_arc(pos, 11.0, 0.0, TAU, 18, Color(C_GUARD, 0.35), 1.0)
		i += GUARD_STRIDE


## The darkness overlay: the sim's light map, re-uploaded only when it changed.
## WORLD pass; a no-op on a lit level.
func _draw_darkness() -> void:
	if not _bridge.HasLight:
		return
	var ver: int = _bridge.LightVersion
	if ver != _dark_ver or _dark_tex == null:
		_dark_ver = ver
		var cols: int = _bridge.GridCols * 2
		var rows: int = _bridge.GridRows * 2
		var img := Image.create_from_data(cols, rows, false, Image.FORMAT_RGBA8,
			_bridge.GetDarkness(int(C_NIGHT.r8), int(C_NIGHT.g8), int(C_NIGHT.b8), DARK_MAX))
		if _dark_tex == null:
			_dark_tex = CanvasTexture.new()
			_dark_tex.texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR
		_dark_tex.diffuse_texture = ImageTexture.create_from_image(img)
		dark_uploads += 1
	draw_texture_rect(_dark_tex, Rect2(Vector2.ZERO, level_size()), false)


## Lamps and switches over the darkness: the kit's fixture if there is one
## (level_art.draw_lamp, with its own emission), a drawn one otherwise.
## Cosmetic only -- the light that MEANS something is already in the overlay.
func _draw_lamps() -> void:
	var lamps: PackedInt32Array = _bridge.GetLamps()
	var i: int = 0
	while i + 3 <= lamps.size():
		var pos := Vector2(lamps[i] / FX, lamps[i + 1] / FX)
		var lit: bool = (lamps[i + 2] & 1) != 0
		var broken: bool = (lamps[i + 2] & 2) != 0
		i += 3
		if not _in_view(pos, 60.0):
			continue
		# The pool of light itself is the overlay's; this is only the fitting.
		if _art.draw_lamp(self, pos, lit, broken, false):
			continue
		if broken:
			draw_arc(pos, 4.0, 0.0, TAU, 10, Color(0.45, 0.45, 0.48), 1.0)
			draw_line(pos + Vector2(-3, -2), pos + Vector2(2, 3), Color(0.55, 0.55, 0.6), 1.0)
		elif lit:
			draw_circle(pos, 7.0, Color(C_LAMP, 0.18))
			draw_circle(pos, 4.0, C_LAMP)
			draw_circle(pos, 2.0, Color(1, 1, 0.95))
		else:
			draw_circle(pos, 4.0, Color(0.30, 0.30, 0.34))
			draw_arc(pos, 4.0, 0.0, TAU, 10, Color(0.5, 0.5, 0.55), 1.0)
	var sw: PackedInt32Array = _bridge.GetSwitches()
	i = 0
	while i + 3 <= sw.size():
		var spos := Vector2(sw[i] / FX, sw[i + 1] / FX)
		var on: bool = sw[i + 2] == 1
		i += 3
		if not _in_view(spos, 20.0):
			continue
		draw_rect(Rect2(spos - Vector2(3.5, 5), Vector2(7, 10)), Color(0.20, 0.21, 0.24))
		draw_rect(Rect2(spos - Vector2(3.5, 5), Vector2(7, 10)), Color(0.55, 0.55, 0.6), false, 1.0)
		draw_rect(Rect2(spos + Vector2(-1.5, -3.5 if on else 0.5), Vector2(3, 3)),
			C_LAMP if on else Color(0.35, 0.36, 0.40))


## Guard torches as soft fans, clipped by walls, drawn WHETHER OR NOT the guard
## can be seen: a beam on a far wall is how you learn a search is coming.
func _draw_torch_beams() -> void:
	if not _bridge.HasLight:
		return
	const RAYS: int = 9
	var pts: PackedVector2Array = _bridge.GetTorchBeams(RAYS)
	var i: int = 0
	while i + RAYS + 1 <= pts.size():
		if _in_view(pts[i], 320.0):
			draw_colored_polygon(pts.slice(i, i + RAYS + 1), C_TORCH)
			draw_circle(pts[i], 2.0, C_LAMP)
		i += RAYS + 1


## Feed the post-tick guard snapshot to the footstep listener. Only while the
## run is live: after a death or an escape there is nobody left to listen.
func _observe_footsteps() -> void:
	if _over_code != 0:
		return
	var player: PackedInt32Array = _bridge.GetPlayer()
	if player[3] != 1:
		return
	_steps.observe(_bridge.GetGuards(), GUARD_STRIDE,
		Vector2(player[0] / FX, player[1] / FX), player[19])


## Footsteps heard through walls: two thin rings per footfall, the second a
## beat behind the first, easing out and fading. Drawn AFTER the walls, since
## the whole point is that the wall does not hide them, and deliberately faint
## -- a sound, not a sighting. No guard figure is ever drawn: where he is is
## yours to infer, and which way he is going is in the zig-zag of his feet.
func _draw_footsteps() -> void:
	var life: float = FOOTSTEPS.LIFE
	var lag: float = 0.18
	for r in _steps.ripples:
		var pos: Vector2 = r["pos"]
		if not _in_view(pos, 30.0):
			continue
		var t: float = r["t"]
		var size: float = r["size"]
		var peak: float = r["alpha"]
		for k in range(2):
			var local: float = (t - lag * k) / (life - lag)
			if local <= 0.0 or local >= 1.0:
				continue
			var rad: float = size * (1.0 - pow(1.0 - local, 3.0)) * (1.0 - 0.3 * k)
			var col := C_STEP
			col.a = peak * pow(1.0 - local, 1.6) * (1.0 - 0.35 * k)
			draw_arc(pos, maxf(rad, 0.5), 0.0, TAU, 28, col, 1.4 - 0.4 * k)
		# The footfall itself, a moment long.
		if t < 0.25:
			var dot := C_STEP
			dot.a = peak * (1.0 - t / 0.25)
			draw_circle(pos, 1.8, dot)


## Guards who are actively coming for you, but are off screen.
##
## The fairness problem a scrolling camera creates: before it, the player could
## see the whole floor and every threat on it, and the stealth game was planning
## under full information. Being shot from off screen is not tension, it is
## noise.
##
## Gated on STATE, deliberately. A patrolling guard you have not noticed gets
## nothing -- that information asymmetry is the game. One who is hunting or
## engaging you already knows where you are, so telling the player costs no
## secret and removes the unfair deaths.
func _draw_offscreen_markers() -> void:
	if _view_rect.encloses(Rect2(Vector2.ZERO, level_size())):
		return   # whole level on screen; there is no "off screen" to warn about

	var g: PackedInt32Array = _bridge.GetGuards()
	var centre := Vector2(FIELD_W, FIELD_H) * 0.5
	var i: int = 0
	while i < g.size():
		var state: int = g[i + 3]
		if state != ST_COMBAT:
			i += GUARD_STRIDE
			continue

		var pos := Vector2(g[i] / FX, g[i + 1] / FX)
		if _in_view(pos, 0.0):
			i += GUARD_STRIDE
			continue

		# Project onto the screen edge along the line from the view centre.
		var scr: Vector2 = world_to_screen(pos)
		var dir: Vector2 = scr - centre
		if dir.length() < 0.001:
			i += GUARD_STRIDE
			continue
		dir = dir.normalized()

		var inset: float = 18.0
		var half := Vector2(FIELD_W, FIELD_H) * 0.5 - Vector2(inset, inset)
		# Distance along `dir` to the nearer of the two bounding planes.
		var tx: float = half.x / maxf(0.001, absf(dir.x))
		var ty: float = half.y / maxf(0.001, absf(dir.y))
		var edge: Vector2 = centre + dir * minf(tx, ty)

		var col: Color = C_SIGNAL if g[i + 10] == TASK_ENGAGE else C_AMBER
		var perp := Vector2(-dir.y, dir.x)
		draw_colored_polygon(PackedVector2Array([
			edge + dir * 7.0, edge - dir * 4.0 + perp * 5.0, edge - dir * 4.0 - perp * 5.0,
		]), col)
		i += GUARD_STRIDE


## A plate bar above the awareness bar, drawn only for guards who have one.
##
## Two separate jobs, deliberately split: the SILHOUETTE says "this one is
## armoured" from across the room, before you commit, and the bar says "you are
## getting through it" once you have. A bar alone would only answer the second
## question, by which point you have already spent the magazine.
func _draw_armour_bar(pos: Vector2, armour: int, armour_max: int) -> void:
	if armour_max <= 0:
		return

	var bar := Rect2(pos.x - 13.0, pos.y - 25.0, 26.0, 3.0)
	draw_rect(bar, Color(0, 0, 0, 0.55))

	var frac: float = clampf(float(armour) / float(armour_max), 0.0, 1.0)
	if frac > 0.0:
		draw_rect(Rect2(bar.position, Vector2(bar.size.x * frac, bar.size.y)), C_PLATE)
	else:
		# Spent, not absent: an empty socket reads as "the plate is gone", which
		# is the moment the player has been shooting toward.
		draw_rect(bar, C_PLATE_GONE, false, 1.0)


## A guard keying his radio (Guard_AI.md §5.3): a ring that fills as the call
## goes through, beside the state glyph. It is the whole of the counterplay --
## the player has to SEE there is a call to stop before it can be stopped.
func _draw_radio(pos: Vector2, radio_q8: int) -> void:
	var at: Vector2 = pos + Vector2(11.0, -27.0)
	var frac: float = clampf(radio_q8 / 256.0, 0.0, 1.0)
	draw_circle(at, 4.5, Color(0, 0, 0, 0.55))
	draw_arc(at, 4.5, -PI * 0.5, -PI * 0.5 + TAU * frac, 16, C_AMBER, 2.0)
	draw_circle(at, 1.4, C_AMBER)


func _draw_awareness(pos: Vector2, awareness_tenths: int, state: int, afraid: bool = false) -> void:
	var frac: float = clampf(awareness_tenths / 1000.0, 0.0, 1.0)
	var bar := Rect2(pos.x - 13.0, pos.y - 20.0, 26.0, 4.0)
	draw_rect(bar, Color(0, 0, 0, 0.55))

	var col: Color = C_COLD
	if awareness_tenths > 650:
		col = C_SIGNAL
	elif awareness_tenths >= 300:
		col = C_AMBER
	draw_rect(Rect2(bar.position, Vector2(bar.size.x * frac, bar.size.y)), col)

	var glyph: String = ""
	match state:
		ST_CURIOUS: glyph = "?"
		ST_COMBAT: glyph = "!"
		ST_HUNTING: glyph = "○"
	# Frozen in fear, whatever his posture underneath (Guard_AI.md §4.1): the
	# moment to act, so it must read differently from every posture's glyph.
	if afraid:
		glyph = "!?"
		col = C_FEAR
	if glyph != "":
		draw_string(_font, pos + Vector2(-3, -24), glyph, HORIZONTAL_ALIGNMENT_LEFT, -1, 12, col)


## Procedural human: shadow, two legs, torso, two arms wrapping the weapon, head.
## Wider across the shoulders than front-to-back, so facing reads at a glance.
##
## An armoured figure is BULKIER, not merely recoloured: a plate across the
## chest and two shoulder pads widen the silhouette, so the shape alone answers
## "is this one going to take four rounds" from across a room. Colour-only
## encoding would be invisible at this scale and in the middle of a firefight.
func _draw_human(pos: Vector2, ang: float, col: Color, recoil: float,
		armour: int = 0, armour_max: int = 0) -> void:
	var fwd := Vector2(cos(ang), sin(ang))
	var side := Vector2(-fwd.y, fwd.x)
	var dark := Color(col.r * 0.35, col.g * 0.35, col.b * 0.35)

	draw_circle(pos + Vector2(1.5, 2.0), 10.0, Color(0, 0, 0, 0.32))

	for s in [-1.0, 1.0]:
		draw_line(pos + side * (3.4 * s), pos + side * (4.4 * s) - fwd * 1.0, dark, 3.0)

	# Torso: 8.4 x 11.2, long axis across the shoulders.
	_world_xform(pos, ang)
	draw_circle(Vector2.ZERO, 5.6, col)
	draw_rect(Rect2(-4.2, -5.6, 8.4, 11.2), col)
	draw_rect(Rect2(-4.2, -5.6, 8.4, 11.2), dark, false, 1.0)

	if armour_max > 0:
		# Heavier plate is physically bigger, so the three armour tiers are
		# distinguishable from each other and not just from bare.
		var bulk: float = 1.0 + clampf(float(armour_max) / 150.0, 0.0, 1.0) * 0.9
		var spent: bool = armour <= 0
		var plate: Color = C_PLATE_GONE if spent else C_PLATE

		# Shoulder pads, one each side, sitting PROUD of the torso rather than
		# inset on it -- the silhouette has to actually get wider, or this is
		# just a recolour under another name.
		var pad: float = 1.4 * bulk
		for sp in [-1.0, 1.0]:
			var y0: float = 5.6 if sp > 0.0 else -5.6 - pad
			draw_rect(Rect2(-4.6, y0, 9.2, pad), plate)
			draw_rect(Rect2(-4.6, y0, 9.2, pad), dark, false, 1.0)

		# Chest plate, inset on the torso and facing forward.
		draw_rect(Rect2(-2.6, -3.4, 5.2, 6.8), plate)
		draw_rect(Rect2(-2.6, -3.4, 5.2, 6.8), dark, false, 1.0)

	_world_pass()

	var muzzle: Vector2 = pos + fwd * (13.0 - recoil * 2.5)
	for s2 in [-1.0, 1.0]:
		draw_line(pos + side * (3.0 * s2), muzzle, col, 2.2)
	draw_line(muzzle, muzzle + fwd * 7.0, dark, 2.6)

	draw_circle(pos + fwd * 2.2, 5.7, col)
	draw_circle(pos + fwd * 2.2, 5.7, dark)
	draw_circle(pos + fwd * 2.2, 4.4, col)


func _draw_prone(pos: Vector2, ang: float, col: Color) -> void:
	_world_xform(pos, ang)
	draw_rect(Rect2(-7.0, -4.5, 14.0, 9.0), col)
	draw_rect(Rect2(-7.0, -4.5, 14.0, 9.0), Color(col.r * 0.4, col.g * 0.4, col.b * 0.4), false, 1.0)
	draw_circle(Vector2(6.0, 0.0), 4.2, col)
	_world_pass()


func _draw_flash(f: Dictionary) -> void:
	var t: float = clampf(f["t"], 0.0, 1.0)
	var pos: Vector2 = f["pos"]
	var ang: float = f["ang"]
	var s: float = f["s"]
	# At t or s of zero every point below collapses onto pos, and a polygon with
	# no area fails triangulation -- which spammed the log with "Invalid polygon
	# data" for every flash that reached the end of its fade.
	var extent: float = 24.0 * s * t
	if extent < 0.5:
		return

	var fwd := Vector2(cos(ang), sin(ang))
	var side := Vector2(-fwd.y, fwd.x)
	var col := Color(1.0, 0.92, 0.70, t * 0.9)
	var pts := PackedVector2Array([
		pos,
		pos + fwd * (18.0 * s * t) + side * (6.0 * s * t),
		pos + fwd * (24.0 * s * t),
		pos + fwd * (18.0 * s * t) - side * (6.0 * s * t),
	])
	draw_colored_polygon(pts, col)
	draw_circle(pos, 4.0 * s * t, Color(1.0, 0.95, 0.80, t * 0.7))


# --------------------------------------------------------------------- HUD

## Every element is drawn at a corner the layout owns, never at an offset
## computed here. That is what makes the HUD editor possible at all, and it is
## also why the old `right - 170` arithmetic had to go: an element whose
## position is an expression cannot be moved.
func _draw_hud(player: PackedInt32Array, world: PackedInt32Array) -> void:
	draw_rect(Rect2(0, FIELD_H, FIELD_W, HUD_H), C_VOID)
	draw_line(Vector2(0, FIELD_H), Vector2(FIELD_W, FIELD_H), Color(0.16, 0.18, 0.22), 1.0)

	_hud_records(_hud.pos_of("records"), world[10])
	_hud_vitals(_hud.pos_of("vitals"), player)
	_hud_weapon(_hud.pos_of("weapon"), player)
	_hud_alarm(_hud.pos_of("alarm"), world[4])
	_hud_exposure(_hud.pos_of("exposure"), world[5])
	_hud_score(_hud.pos_of("score"), world)
	_hud_movement(_hud.pos_of("movement"), player)
	if _bridge.HasLight:
		_hud_light(_hud.pos_of("light"), _bridge.PlayerVisQ8)

	# A kit was equipped but the run it belongs to has not started. Persistent
	# rather than a fading notice: the whole failure this fixes is a player
	# changing weapons, seeing nothing change, and concluding it is broken.
	if _bridge.LoadoutStaged:
		_hud_pending(_hud.pos_of("pending"))
	if _bridge.ObjectivesTotal > 0:
		_hud_objective(_hud.pos_of("objective"))

	if world[3] == 1:
		_hud_dilating(_hud.pos_of("dilating"))
	if _notice_t > 0.0:
		_hud_notice(_hud.pos_of("notice"))
	if _save_notice > 0.0:
		_hud_replay_notice(_hud.pos_of("replay"))

	if not _playback:
		_draw_reticle(player)

	if _playback:
		_draw_playback_bar()

	# Not behind the stash or the title: going back replaces the debrief rather
	# than stacking a second panel under it.
	if _over_code != 0 and not _playback and not _any_screen_open():
		if _victory:
			_draw_victory_card(world)
		else:
			_draw_end_card(world)


## A small caps label above a value, which is the shape every readout in the bar
## now shares. Consistency here is most of what "tidying the HUD" amounted to:
## before, three elements each invented their own label size and colour.
func _hud_label(at: Vector2, text: String, width: float,
		align: int = HORIZONTAL_ALIGNMENT_LEFT) -> void:
	draw_string(_font, at, text, align, width, 9, C_HUD_LABEL)


## The weapon IN YOUR HANDS, not the one staged for the next run.
##
## This read CurrentWeaponName and MagazineSize -- both of which describe the
## STAGED kit. While every equip restarted, the two could not differ. They can
## now, in two ordinary cases: after an X swap, and after equipping without
## redeploying. The HUD was naming a weapon the player was not holding and
## counting a magazine they did not have.
func _hud_weapon(at: Vector2, player: PackedInt32Array) -> void:
	var w: float = _hud.size_of("weapon").x
	_hud_label(at + Vector2(0, 9), _bridge.HeldWeaponName.to_upper(), w,
		HORIZONTAL_ALIGNMENT_RIGHT)
	var mag: int = player[4]
	var text: String = "RELOADING" if player[6] == 1 else "%d / %d" % [mag, _bridge.HeldMagazineSize]
	draw_string(_font, at + Vector2(0, 24), text, HORIZONTAL_ALIGNMENT_RIGHT, w, 14,
		C_PLAYER if mag > 0 else C_SIGNAL)

	# A rotary barrel spooling up. Shown under the ammo because that is where
	# the eye already is when the trigger is down and nothing is coming out.
	var spin: float = clampf(player[21] / 256.0, 0.0, 1.0)
	if spin > 0.0:
		var bar := Rect2(at.x, at.y + 28.0, w, 3.0)
		draw_rect(bar, C_HUD_TROUGH)
		draw_rect(Rect2(bar.position, Vector2(bar.size.x * spin, bar.size.y)),
			C_EXIT if spin >= 1.0 else C_AMBER)


## Four pips and a name. The pips matter more than the word: the tier is
## changed with the wheel, which gives no positional feedback of its own, so
## where you are in the range has to be visible at a glance.
##
## The bar underneath is the post-sprint recovery -- while it is draining the
## weapon is not back on target, no aim lock will build, and the reticle is
## wide. That is worth showing rather than leaving the player to infer it from
## missing.
func _hud_movement(at: Vector2, player: PackedInt32Array) -> void:
	var w: float = _hud.size_of("movement").x
	# 19 and 20, not 18 and 19: index 18 is sway. Getting this wrong drew the
	# sway value as the movement tier, which looked almost plausible.
	var tier: int = player[19]
	var ready: float = clampf(player[20] / 256.0, 0.0, 1.0)

	_hud_label(at + Vector2(0, 9), _bridge.MoveTierName(tier), w,
		HORIZONTAL_ALIGNMENT_RIGHT)

	var pip_w: float = 14.0
	var gap: float = 4.0
	var n: int = _bridge.MoveTierCount
	var total: float = n * pip_w + (n - 1) * gap
	var x: float = at.x + w - total
	for i in range(n):
		var box := Rect2(x + i * (pip_w + gap), at.y + 13.0, pip_w, 5.0)
		if i <= tier:
			# Cooler as you slow down, hotter as you speed up: the colour says
			# which way along the trade you are without reading the word.
			draw_rect(box, C_COLD if i == 0 else (C_EXIT if i == 1 else
				(C_AMBER if i == 2 else C_SIGNAL)))
		else:
			draw_rect(box, C_HUD_TROUGH)

	if ready > 0.0:
		var bar := Rect2(at.x, at.y + 21.0, w, 3.0)
		draw_rect(bar, C_HUD_TROUGH)
		draw_rect(Rect2(bar.position, Vector2(bar.size.x * ready, bar.size.y)), C_SIGNAL)


## What the mission is for, and whether you have it. Permanent while a mission
## has an objective: walking to the exit without it is allowed and pays nothing,
## so the one thing the player must not be able to do is forget.
func _hud_objective(at: Vector2) -> void:
	var w: float = _hud.size_of("objective").x
	var have: int = _bridge.ObjectivesCarried
	var want: int = _bridge.ObjectivesTotal
	var met: bool = have >= want

	_hud_label(at + Vector2(0, 9), "OBJECTIVE", w)
	draw_string(_font, at + Vector2(0, 22),
		"IN THE PACK" if met else "%d / %d" % [have, want],
		HORIZONTAL_ALIGNMENT_LEFT, w, 13, C_EXIT if met else C_SIGNAL)


func _hud_pending(at: Vector2) -> void:
	var w: float = _hud.size_of("pending").x
	draw_string(_font, at + Vector2(0, 11),
		"F5 → %s" % _bridge.StagedWeaponName.to_upper(),
		HORIZONTAL_ALIGNMENT_RIGHT, w, 11, C_AMBER)


func _hud_alarm(at: Vector2, alarm: int) -> void:
	var w: float = _hud.size_of("alarm").x
	var col: Color = C_COLD if alarm == 0 else (C_AMBER if alarm == 1 else C_SIGNAL)
	var alarm_name: String = ALARM_NAMES[clampi(alarm, 0, ALARM_NAMES.size() - 1)]
	draw_string(_font, at + Vector2(0, 11), "ALARM  %s" % alarm_name,
		HORIZONTAL_ALIGNMENT_RIGHT, w, 11, col)


func _hud_exposure(at: Vector2, exposure_tenths: int) -> void:
	var w: float = _hud.size_of("exposure").x
	var frac: float = clampf(exposure_tenths / 1000.0, 0.0, 1.0)
	# draw_string positions the BASELINE, so the label has to be dropped into
	# the box rather than sat on its top edge -- otherwise it paints above the
	# element and the editor's box does not contain what it claims to.
	_hud_label(at + Vector2(0, 9), "EXPOSURE", w, HORIZONTAL_ALIGNMENT_RIGHT)
	var bar := Rect2(at + Vector2(0, 12), Vector2(w, 5))
	draw_rect(bar, C_HUD_TROUGH)
	draw_rect(Rect2(bar.position, Vector2(bar.size.x * frac, bar.size.y)),
		C_SIGNAL if frac > 0.65 else (C_AMBER if frac > 0.3 else C_COLD))


## How visible you are RIGHT NOW (lighting plan §7.3): the sim's own VisQ8 at
## the player, which is what every guard's eye is scaled by this tick, and
## nothing else -- the meter cannot disagree with perception. A jewel that goes
## from dim to bright, a word, and a bar.
func _hud_light(at: Vector2, vis_q8: int) -> void:
	var w: float = _hud.size_of("light").x
	var f: float = clampf(vis_q8 / 256.0, 0.0, 1.0)
	var word: String = "HIDDEN" if vis_q8 < 110 else ("IN SHADOW" if vis_q8 < 200 else "LIT")
	var jewel: Color = Color(0.20, 0.24, 0.32).lerp(C_LAMP, f)
	draw_circle(at + Vector2(8, 11), 7.0, Color(jewel, 0.25))
	draw_circle(at + Vector2(8, 11), 4.5, jewel)
	_hud_label(at + Vector2(22, 9), "LIGHT", w - 22, HORIZONTAL_ALIGNMENT_RIGHT)
	draw_string(_font, at + Vector2(22, 10), word, HORIZONTAL_ALIGNMENT_LEFT, -1, 11, jewel)
	var bar := Rect2(at + Vector2(22, 17), Vector2(w - 22, 5))
	draw_rect(bar, C_HUD_TROUGH)
	draw_rect(Rect2(bar.position, Vector2(bar.size.x * f, bar.size.y)), jewel)


func _hud_score(at: Vector2, world: PackedInt32Array) -> void:
	var w: float = _hud.size_of("score").x
	draw_string(_font, at + Vector2(0, 12),
		"provable %d   unprovable %d   destroyed %d" % [world[7], world[8], world[9]],
		HORIZONTAL_ALIGNMENT_LEFT, w, 11, Color(0.6, 0.64, 0.7))


func _hud_dilating(at: Vector2) -> void:
	draw_string(_font, at + Vector2(0, 12), "DILATING",
		HORIZONTAL_ALIGNMENT_LEFT, _hud.size_of("dilating").x, 11, C_AMBER)


func _hud_notice(at: Vector2) -> void:
	draw_string(_font, at + Vector2(0, 11), _notice, HORIZONTAL_ALIGNMENT_LEFT,
		_hud.size_of("notice").x, 11,
		Color(0.55, 0.85, 0.70, clampf(_notice_t, 0.0, 1.0)))


func _hud_replay_notice(at: Vector2) -> void:
	draw_string(_font, at + Vector2(0, 10), "replay saved -> %s" % _saved_path,
		HORIZONTAL_ALIGNMENT_LEFT, _hud.size_of("replay").x, 10,
		Color(0.55, 0.85, 0.70, clampf(_save_notice, 0.0, 1.0)))


## Transport bar. The divergence banner is the reason this screen exists: it
## means the sim no longer reproduces the recorded run, and names the tick.
func _draw_playback_bar() -> void:
	var total: int = maxi(1, _bridge.ReplayLength)
	var idx: int = _bridge.ReplayIndex
	var bar := Rect2(14, 8, FIELD_W - 28, 4)

	draw_rect(Rect2(0, 0, FIELD_W, 34), Color(0.02, 0.03, 0.04, 0.78))
	draw_rect(bar, Color(0.14, 0.16, 0.20))
	draw_rect(Rect2(bar.position, Vector2(bar.size.x * float(idx) / float(total), bar.size.y)),
		C_COLD)

	var state: String = "PLAYING" if _pb_playing else "PAUSED"
	if _bridge.ReplayFinished:
		state = "END"
	draw_string(_font, Vector2(14, 28),
		"REPLAY  %s  tick %d / %d  x%s" % [state, idx, total, _speed_text()],
		HORIZONTAL_ALIGNMENT_LEFT, 420, 11, Color(0.78, 0.82, 0.88))

	draw_string(_font, Vector2(FIELD_W - 434, 28),
		"space pause   left/right step   up/down speed   enter rewind",
		HORIZONTAL_ALIGNMENT_RIGHT, 420, 10, Color(0.45, 0.48, 0.54))

	var diverged: int = _bridge.DivergedTick
	if diverged >= 0:
		var msg: String = "DIVERGED AT TICK %d  —  %s" % [diverged, _bridge.DivergedDetail]
		draw_rect(Rect2(0, 34, FIELD_W, 22), Color(0.35, 0.06, 0.06, 0.88))
		draw_string(_font, Vector2(0, 49), msg, HORIZONTAL_ALIGNMENT_CENTER,
			FIELD_W, 11, Color(1.0, 0.82, 0.78))


## GDScript's % formatting has no %g, so trim a fixed-point string instead.
## 1.0 -> "1", 0.125 -> "0.125".
## Health and armour as two stacked bars. Armour reads as a separate pool
## because it is one: it absorbs first and never comes back mid-mission.
## Health over armour, with a 36px label gutter on the left. The gutter is part
## of the element's box, so dragging it grabs the labels too.
func _hud_vitals(at: Vector2, player: PackedInt32Array) -> void:
	const GUTTER: float = 36.0
	var bar_w: float = _hud.size_of("vitals").x - GUTTER
	var x: float = at.x + GUTTER
	var health: int = player[9]
	var armour: int = player[10]
	var armour_max: int = player[11]
	var health_max: int = maxi(1, player[12])

	var hp_bar := Rect2(x, at.y + 2.0, bar_w, 8.0)
	draw_rect(hp_bar, C_HUD_TROUGH)
	var hp_frac: float = clampf(float(health) / float(health_max), 0.0, 1.0)
	var hp_col := C_EXIT if hp_frac > 0.5 else (C_AMBER if hp_frac > 0.25 else C_SIGNAL)
	draw_rect(Rect2(hp_bar.position, Vector2(hp_bar.size.x * hp_frac, hp_bar.size.y)), hp_col)
	_hud_label(Vector2(at.x, at.y + 10.0), "HP", GUTTER - 6.0, HORIZONTAL_ALIGNMENT_RIGHT)

	if armour_max > 0:
		var ar_bar := Rect2(x, at.y + 13.0, bar_w, 5.0)
		draw_rect(ar_bar, C_HUD_TROUGH)
		var ar_frac: float = clampf(float(armour) / float(armour_max), 0.0, 1.0)
		draw_rect(Rect2(ar_bar.position, Vector2(ar_bar.size.x * ar_frac, ar_bar.size.y)),
			C_COLD if ar_frac > 0.0 else Color(0.3, 0.3, 0.3))
		_hud_label(Vector2(at.x, at.y + 20.0), "ARM", GUTTER - 6.0, HORIZONTAL_ALIGNMENT_RIGHT)


func _speed_text() -> String:
	var t: String = "%0.3f" % _pb_speed
	if t.contains("."):
		t = t.rstrip("0").rstrip(".")
	return t


func _hud_records(at: Vector2, fuel_index: int) -> void:
	var recs: PackedInt32Array = _bridge.GetRecords()
	var count: int = recs.size() / 4
	var pw: float = 92.0
	var ph: float = 17.0
	var x: float = at.x
	var y: float = at.y

	# Newest on the right: draw oldest-first from the left, capped to what the
	# strip's own box has room for rather than to a magic nine.
	var first: int = maxi(0, count - _hud.record_slots())
	for idx in range(first, count):
		var base: int = idx * 4
		var tier: int = recs[base + 1]
		var charge: int = recs[base + 2]
		var state: int = recs[base + 3]

		var body := Rect2(x, y, pw - 4.0, ph)
		if state == 2:
			# Spent husks stay visible as empty outlines so the run's damage
			# stays legible (spec §2.3).
			draw_rect(body, Color(0.22, 0.24, 0.28), false, 1.0)
		else:
			var fill: Color = C_AMBER if state == 1 else Color(0.18, 0.22, 0.28)
			if state == 1:
				fill.a = 0.22
			draw_rect(body, fill)
			draw_rect(body, Color(0.30, 0.34, 0.40), false, 1.0)

		if idx == fuel_index and state != 2:
			draw_rect(body.grow(1.5), Color(1, 1, 1, 0.85), false, 1.0)

		var name_col: Color = Color(0.32, 0.34, 0.38) if state == 2 else Color(0.78, 0.82, 0.88)
		var nm: String = _bridge.RecordName(recs[base])
		if nm.length() > 13:
			nm = nm.substr(0, 12) + "…"
		draw_string(_font, Vector2(x + 4, y + 12), nm, HORIZONTAL_ALIGNMENT_LEFT, pw - 26, 9, name_col)

		for t in range(tier):
			draw_circle(Vector2(x + pw - 10.0 - t * 5.0, y + 8.0), 1.8, name_col)

		if state != 2:
			var frac: float = clampf(float(charge) / float(_bridge.RecordStageTicks), 0.0, 1.0)
			draw_rect(Rect2(x, y + ph + 1.0, (pw - 4.0) * frac, 2.0),
				C_AMBER if state == 1 else C_COLD)
		x += pw


## The reticle is the whole aiming UI. Two things it must be honest about:
##
##  - It sits on the MUZZLE line, not under the cursor. A heavy weapon swings
##    onto target at its own rate, so the gun genuinely lags the mouse; drawing
##    the bracket under the pointer would make those misses look like the sim
##    cheating. The cursor keeps a faint mark of its own and a thread joins the
##    two while they disagree.
##  - Its size is the sim's OWN cone half-angle, off the snapshot, rather than
##    the prototype's hardcoded pistol formula. Heat, sway and the aiming stance
##    are already folded in, so what the bracket shows is what the round does.
func _draw_reticle(player: PackedInt32Array) -> void:
	if player[3] != 1:
		return

	# Anchored to the muzzle and the cursor, both of which are world positions,
	# so this is a world-space overlay despite being called from the HUD pass.
	_world_pass()

	var aiming: bool = player[13] == 1
	var lock_mt: int = player[15]
	var lock_full: int = maxi(1, player[16])
	var lock: float = clampf(float(lock_mt) / float(lock_full), 0.0, 1.0)
	var ready: bool = lock >= 1.0

	var ppos := Vector2(player[0] / FX, player[1] / FX)
	var cursor: Vector2 = world_mouse()
	var reach: float = maxf(40.0, ppos.distance_to(cursor))

	var ang: float = player[2] / 65536.0 * TAU
	var m: Vector2 = ppos + Vector2(cos(ang), sin(ang)) * reach

	var half: float = player[17] / 65536.0 * TAU
	var radius: float = maxf(4.0, tan(half) * reach + 4.0)

	var col := Color(0.85, 0.88, 0.94, 0.5)
	if aiming:
		col = Color(0.55, 0.85, 0.95, 0.75)
	if ready:
		col = C_SIGNAL

	# The gun has not caught up with the pointer yet: show both, and the gap.
	var lag: float = m.distance_to(cursor)
	if lag > 5.0:
		var faint := Color(col.r, col.g, col.b, 0.22)
		draw_line(m, cursor, faint, 1.0)
		draw_line(cursor - Vector2(3, 0), cursor + Vector2(3, 0), faint, 1.0)
		draw_line(cursor - Vector2(0, 3), cursor + Vector2(0, 3), faint, 1.0)

	for i in range(4):
		var a: float = i * PI * 0.5 + PI * 0.25
		var d := Vector2(cos(a), sin(a))
		draw_line(m + d * (radius - 4.0), m + d * radius, col, 1.5 if aiming else 1.0)

	if aiming and lock > 0.0:
		# An arc rather than a bar: it reads at a glance without leaving the
		# spot the eye is already on.
		var ring: float = radius + 7.0
		var steps: int = maxi(2, int(lock * 40.0))
		var pts := PackedVector2Array()
		for i in range(steps + 1):
			var a2: float = -PI * 0.5 + TAU * lock * (float(i) / float(steps))
			pts.append(m + Vector2(cos(a2), sin(a2)) * ring)
		if pts.size() >= 2:
			draw_polyline(pts, C_SIGNAL if ready else Color(0.55, 0.85, 0.95, 0.9), 2.0)

	if ready:
		draw_string(_font, m + Vector2(-40, -radius - 16), "HEADSHOT",
			HORIZONTAL_ALIGNMENT_CENTER, 80, 10, C_SIGNAL)

	_screen_pass()


## The debrief. Three outcomes, said plainly, because "OUT" did not distinguish
## the run that paid from the one that did not — and those are the only two
## extractions there are.
func _draw_end_card(world: PackedInt32Array) -> void:
	draw_rect(Rect2(0, 0, FIELD_W, FIELD_H + HUD_H), Color(0.02, 0.03, 0.04, 0.82))

	var died: bool = _over_code != 1
	var completed: bool = _campaign.last_completed

	var title: String
	var col: Color
	if died:
		title = "You died!"
		col = C_SIGNAL
	elif completed:
		title = "You escaped with the mission objective"
		col = C_EXIT
	else:
		title = "You escaped without the mission objective"
		col = C_AMBER

	var panel := Rect2(FIELD_W * 0.5 - 300, FIELD_H * 0.5 - 130, 600, 260)
	draw_rect(panel, Color(0.043, 0.051, 0.063, 0.96))
	draw_rect(panel, col, false, 1.5)

	var y: float = panel.position.y + 48
	draw_string(_font, Vector2(panel.position.x, y), title,
		HORIZONTAL_ALIGNMENT_CENTER, panel.size.x, 22, col)

	y += 34
	if died:
		draw_string(_font, Vector2(panel.position.x, y),
			"the pack is gone, and with it everything in it",
			HORIZONTAL_ALIGNMENT_CENTER, panel.size.x, 13, Color(0.72, 0.76, 0.82))
	elif not completed:
		draw_string(_font, Vector2(panel.position.x, y),
			"no pay without it — but the gear you carried is yours",
			HORIZONTAL_ALIGNMENT_CENTER, panel.size.x, 13, Color(0.72, 0.76, 0.82))
	else:
		draw_string(_font, Vector2(panel.position.x, y),
			"mission %d    records %d    salvage %d" % [
				_campaign.last_mission, _campaign.last_records, _campaign.last_salvage],
			HORIZONTAL_ALIGNMENT_CENTER, panel.size.x, 13, Color(0.72, 0.76, 0.82))

	y += 30
	if not died:
		draw_string(_font, Vector2(panel.position.x, y),
			"EARNED %d" % _campaign.total_of_last(),
			HORIZONTAL_ALIGNMENT_CENTER, panel.size.x, 26,
			C_EXIT if completed else Color(0.5, 0.54, 0.6))
		y += 26
		draw_string(_font, Vector2(panel.position.x, y),
			"%d item(s) recovered  ·  balance %d" % [
				_campaign.last_items, _campaign.money],
			HORIZONTAL_ALIGNMENT_CENTER, panel.size.x, 12, Color(0.5, 0.54, 0.6))

	y += 34
	# Dying scores zero across all three (spec §6.3).
	var line: String = "provable 0    unprovable 0    destroyed 0"
	if not died:
		line = "provable %d    unprovable %d    destroyed %d" % [world[7], world[8], world[9]]
	draw_string(_font, Vector2(panel.position.x, y), line,
		HORIZONTAL_ALIGNMENT_CENTER, panel.size.x, 12, Color(0.5, 0.54, 0.6))

	y += 18
	draw_string(_font, Vector2(panel.position.x, y),
		"kills %d    subdued %d    shots %d    %s" % [world[11], world[12], world[13],
			luck_text(_bridge.Luck)],
		HORIZONTAL_ALIGNMENT_CENTER, panel.size.x, 11, Color(0.42, 0.46, 0.52))

	draw_string(_font, Vector2(panel.position.x, panel.end.y - 18),
		"ENTER  back to the stash          F5  run it again",
		HORIZONTAL_ALIGNMENT_CENTER, panel.size.x, 13, Color(0.82, 0.86, 0.92))


## Every shipped mission as [title, runs, completions, best payout], in mission
## select's order, for the victory screen's table.
func _mission_table() -> Array:
	var rows: Array = []
	for path in LEVELS.list():
		var rec: Dictionary = _campaign.mission_record(path.get_file())
		rows.append([_bridge.LevelTitle(LEVELS.read(path)), int(rec["runs"]),
			int(rec["completions"]), int(rec["best"])])
	return rows


## Seconds of play as h:mm:ss, or m:ss under an hour. `ticks` is the sim's
## tick count, which runs on the 60 Hz physics clock whatever dilation does to
## the world.
static func play_time_text(ticks: int) -> String:
	var secs: int = maxi(0, ticks) / 60
	if secs >= 3600:
		return "%d:%02d:%02d" % [secs / 3600, (secs / 60) % 60, secs % 60]
	return "%d:%02d" % [secs / 60, secs % 60]


## One label/value line of the victory screen: label left, value right, in a
## column `w` wide.
func _stat_line(x: float, y: float, w: float, label: String, value: String,
		col: Color = Color(0.82, 0.86, 0.92)) -> void:
	draw_string(_font, Vector2(x, y), label, HORIZONTAL_ALIGNMENT_LEFT, w, 12,
		Color(0.50, 0.55, 0.63))
	draw_string(_font, Vector2(x, y), value, HORIZONTAL_ALIGNMENT_RIGHT, w, 12, col)


## The debrief for the run that WON: the final mission, completed. This run on
## the left, the whole campaign on the right, every mission beneath. ENTER and
## F5 do what they do on the ordinary debrief -- winning does not end the
## campaign, it only says that it has been won.
func _draw_victory_card(world: PackedInt32Array) -> void:
	draw_rect(Rect2(0, 0, FIELD_W, FIELD_H + HUD_H), Color(0.02, 0.03, 0.04, 0.9))

	var panel := Rect2(FIELD_W * 0.5 - 360, 28, 720, FIELD_H + HUD_H - 56)
	draw_rect(panel, Color(0.043, 0.051, 0.063, 0.97))
	draw_rect(panel, C_OBJECTIVE, false, 1.5)

	var x0: float = panel.position.x
	var y: float = panel.position.y + 50
	draw_string(_font, Vector2(x0, y), "VICTORY",
		HORIZONTAL_ALIGNMENT_CENTER, panel.size.x, 34, C_OBJECTIVE)
	y += 26
	draw_string(_font, Vector2(x0, y),
		"you walked out of the Black Site with the objective — the game is won",
		HORIZONTAL_ALIGNMENT_CENTER, panel.size.x, 13, Color(0.72, 0.76, 0.82))
	y += 18
	var c: RefCounted = _campaign
	var won: String = "won on run %d" % c.won_on_run
	if c.victories > 1:
		won += "  ·  victory #%d" % c.victories
	draw_string(_font, Vector2(x0, y), won,
		HORIZONTAL_ALIGNMENT_CENTER, panel.size.x, 11, Color(0.5, 0.54, 0.6))

	# Two columns of figures.
	var colw: float = 300.0
	var lx: float = x0 + 40.0
	var rx: float = panel.end.x - 40.0 - colw
	y += 34
	var head := Color(0.72, 0.76, 0.82)
	draw_string(_font, Vector2(lx, y), "THIS RUN", HORIZONTAL_ALIGNMENT_LEFT, colw, 12, head)
	draw_string(_font, Vector2(rx, y), "THE CAMPAIGN", HORIZONTAL_ALIGNMENT_LEFT, colw, 12, head)
	draw_line(Vector2(lx, y + 6), Vector2(lx + colw, y + 6), C_HUD_TROUGH, 1.0)
	draw_line(Vector2(rx, y + 6), Vector2(rx + colw, y + 6), C_HUD_TROUGH, 1.0)

	var run: Array = [
		["time in the field", play_time_text(world[0])],
		["guards killed", str(world[11])],
		["guards subdued", str(world[12])],
		["shots fired", str(world[13])],
		["records provable", str(world[7])],
		["records unprovable", str(world[8])],
		["records destroyed", str(world[9])],
		["items recovered", str(c.last_items)],
		["luck", luck_text(_bridge.Luck).trim_prefix("luck ")],
		["earned", "%d" % c.total_of_last()],
	]
	var camp: Array = [
		["time in the field", play_time_text(c.ticks)],
		["runs", str(c.runs)],
		["extractions  ·  deaths", "%d  ·  %d" % [c.extractions, c.deaths()]],
		["missions completed", str(c.completions())],
		["guards killed", str(c.kills)],
		["guards subdued", str(c.subdues)],
		["shots fired", str(c.shots)],
		["records out  (prov · unprov)", "%d · %d" % [c.provable, c.unprovable]],
		["items recovered", str(c.recovered)],
		["earned  ·  on hand", "%d  ·  %d" % [c.earned, c.money]],
	]
	var line_h: float = 17.0
	for i in range(maxi(run.size(), camp.size())):
		var ly: float = y + 24 + i * line_h
		if i < run.size():
			var v: Color = C_EXIT if i == run.size() - 1 else Color(0.82, 0.86, 0.92)
			_stat_line(lx, ly, colw, run[i][0], run[i][1], v)
		if i < camp.size():
			_stat_line(rx, ly, colw, camp[i][0], camp[i][1])
	y += 24 + maxi(run.size(), camp.size()) * line_h + 14

	# Every mission, in mission select's order.
	var tx: float = lx
	var tw: float = panel.end.x - 40.0 - lx
	draw_string(_font, Vector2(tx, y), "MISSIONS", HORIZONTAL_ALIGNMENT_LEFT, 200, 12, head)
	draw_string(_font, Vector2(tx + tw - 300, y), "runs", HORIZONTAL_ALIGNMENT_RIGHT, 80, 11,
		C_HUD_LABEL)
	draw_string(_font, Vector2(tx + tw - 200, y), "completed", HORIZONTAL_ALIGNMENT_RIGHT, 90, 11,
		C_HUD_LABEL)
	draw_string(_font, Vector2(tx + tw - 100, y), "best", HORIZONTAL_ALIGNMENT_RIGHT, 100, 11,
		C_HUD_LABEL)
	draw_line(Vector2(tx, y + 6), Vector2(tx + tw, y + 6), C_HUD_TROUGH, 1.0)
	# Room for what fits above the footer; mission select is the full list.
	var room: int = int((panel.end.y - 44 - (y + 22)) / 15.0) + 1
	for i in range(mini(_victory_rows.size(), room)):
		var r: Array = _victory_rows[i]
		var ry: float = y + 22 + i * 15.0
		var done: bool = int(r[2]) > 0
		var col: Color = Color(0.82, 0.86, 0.92) if done else Color(0.5, 0.54, 0.6)
		draw_string(_font, Vector2(tx, ry), r[0], HORIZONTAL_ALIGNMENT_LEFT, tw - 310, 12, col)
		draw_string(_font, Vector2(tx + tw - 300, ry), str(r[1]),
			HORIZONTAL_ALIGNMENT_RIGHT, 80, 12, col)
		draw_string(_font, Vector2(tx + tw - 200, ry), str(r[2]),
			HORIZONTAL_ALIGNMENT_RIGHT, 90, 12, C_EXIT if done else col)
		draw_string(_font, Vector2(tx + tw - 100, ry), str(r[3]),
			HORIZONTAL_ALIGNMENT_RIGHT, 100, 12, col)

	draw_string(_font, Vector2(x0, panel.end.y - 18),
		"ENTER  back to the stash          F5  run it again",
		HORIZONTAL_ALIGNMENT_CENTER, panel.size.x, 13, Color(0.82, 0.86, 0.92))
