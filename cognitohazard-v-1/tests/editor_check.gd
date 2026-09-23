extends SceneTree

## Headless verification for the level editor (spec §11, milestone 6).
##
##   Godot --headless --path . --script res://tests/editor_check.gd
##
## The C# sim harness cannot cover this: the editor and replay APIs live on
## SimBridge, which is part of game/ and needs the engine. So the game-side
## bridge gets its own runner, like the audio harness. It also carries a
## project-wide lint for unsupported GDScript format conversions.
##
## Checks run on the first processed frame, not in _initialize(): a node added
## to the tree during _initialize() does not get _ready() until the tree starts
## processing.

var _pass: int = 0
var _fail: int = 0
var _done: bool = false

const GW: int = 48
const GH: int = 28

# Tool codes, mirroring SimBridge.EditorPaint.
const T_WALL := 0
const T_FLOOR := 1
const T_SPAWN := 2
const T_EXIT := 3
const T_CACHE := 4
const T_GUARD := 5
const T_WAYPOINT := 6
const T_CHEST := 7
const T_GLASS := 8
const T_DOOR := 9
const T_OBJECTIVE := 10


func _check(name: String, ok: bool, detail: String = "") -> void:
	if ok:
		_pass += 1
		print("  PASS  %s" % name)
	else:
		_fail += 1
		print("  FAIL  %s\n          %s" % [name, detail])


func _eq(name: String, got, want) -> void:
	_check(name, got == want, "expected %s, got %s" % [str(want), str(got)])


## The overlay probe (Guard_AI.md §9.2) must draw inside a REAL _draw, which
## only happens once the tree has processed a frame, so the harness waits for
## it before reporting. Up to a few frames; a probe that never draws fails.
var _probe: Node2D = null
var _probe_frames: int = 0


func _process(_delta: float) -> bool:
	if _probe != null:
		_probe_frames += 1
		var results: Array = _probe.get("results")
		if results.size() < 2 and _probe_frames < 10:
			return false
		_report_overlay_probe(results)
		_probe.queue_free()
		_probe = null
		print()
		print("%d passed, %d failed" % [_pass, _fail])
		quit(0 if _fail == 0 else 1)
		return true
	if _done:
		return true
	_done = true

	print("editor harness - spec 11 milestone 6")
	print()

	var bridge_script: Script = load("res://game/SimBridge.cs")   # CSharpScript, not GDScript
	var b: RefCounted = bridge_script.new()
	b.Load(FileAccess.get_file_as_string("res://levels/substation_4.txt"), 1)

	_check_camera(bridge_script)
	_check_resize(bridge_script)
	_check_level_swap(bridge_script)
	_check_staged_kit(bridge_script)
	_check_painting(b)
	_check_panels(bridge_script, b)
	_check_loot_assignment(bridge_script)
	_check_guards_and_routes(b)
	_check_roundtrip(b)
	_check_validation(b)
	_check_editor_node(bridge_script)
	_check_editor_tools(bridge_script)
	_check_editor_routes(bridge_script)
	_check_recording(bridge_script)
	_check_input_gates()
	_check_step_arity()
	_check_format_strings()
	_check_guard_ai_mirror(bridge_script)
	_start_overlay_probe(bridge_script)
	return false


# ---------------------------------------------------------------- camera
#
# The camera is presentation. These assert the two things that would otherwise
# only show up as "it feels wrong at the edges" or, far worse, as a replay that
# verifies on one machine and not another.

const MAIN := preload("res://game/main.gd")
const LEVELS := preload("res://game/levels.gd")

func _check_camera(bridge_script: Script) -> void:
	print("  -- camera --")

	var view := Vector2(960, 560)

	# Zoom. The camera plays at a FIXED magnification and scrolls, rather than
	# backing off until the whole floor fits -- which is the change that made
	# the reference level scroll for the first time.
	_eq("a one-screen level plays at the play zoom",
		MAIN.zoom_for(view, view), MAIN.PLAY_ZOOM)
	_eq("so does a level nine times the size",
		MAIN.zoom_for(Vector2(2880, 1680), view), MAIN.PLAY_ZOOM)
	_eq("and one merely twice the size",
		MAIN.zoom_for(Vector2(1920, 1120), view), MAIN.PLAY_ZOOM)
	_check("the play zoom is a real magnification, not 1:1",
		MAIN.PLAY_ZOOM >= 1.25 and MAIN.PLAY_ZOOM <= 1.5, str(MAIN.PLAY_ZOOM))

	# A level SMALLER than the view is pulled in further rather than left
	# sitting in a letterbox, but only as far as the art holds up.
	_check("a small level is magnified to fill the screen",
		MAIN.zoom_for(view * 0.5, view) > MAIN.PLAY_ZOOM,
		str(MAIN.zoom_for(view * 0.5, view)))
	_eq("but never past the ceiling",
		MAIN.zoom_for(Vector2(60, 40), view), MAIN.MAX_ZOOM)
	_check("and never below the readable floor",
		MAIN.zoom_for(Vector2(99999, 99999), view) >= MAIN.MIN_ZOOM)

	# Every real level on disk plays at the same magnification, so moving
	# between them does not change how big anything looks.
	var zooms: Array = []
	for f in LEVELS.list():
		var lb: RefCounted = bridge_script.new()
		lb.Load(LEVELS.read(f), 1)
		zooms.append(MAIN.zoom_for(Vector2(lb.GridWidthPx, lb.GridHeightPx), view))
	var consistent: bool = true
	for z in zooms:
		if absf(z - zooms[0]) > 0.001:
			consistent = false
	_check("every level on disk plays at the same zoom", consistent, str(zooms))

	# Look-ahead leans toward the cursor but is capped, so the camera can never
	# be walked across the level to scout with the mouse.
	var p := Vector2(500, 300)
	_eq("with the cursor on the player the view is on the player",
		MAIN.camera_target(p, p), p)
	var near: Vector2 = MAIN.camera_target(p, p + Vector2(100, 0))
	_check("the view leans toward the cursor", near.x > p.x and near.x < p.x + 100.0,
		str(near))
	var far: Vector2 = MAIN.camera_target(p, p + Vector2(99999, 0))
	_check("but never further than the cap",
		far.distance_to(p) <= MAIN.LOOK_AHEAD_MAX + 0.001, str(far.distance_to(p)))

	# Clamping, at all four edges and in the centring case.
	var level := Vector2(1920, 1120)
	var vis := Vector2(960, 560)
	_eq("clamped at the left edge", MAIN.clamp_camera(Vector2(0, 560), level, vis).x, 480.0)
	_eq("clamped at the right edge", MAIN.clamp_camera(Vector2(9999, 560), level, vis).x, 1440.0)
	_eq("clamped at the top edge", MAIN.clamp_camera(Vector2(960, 0), level, vis).y, 280.0)
	_eq("clamped at the bottom edge", MAIN.clamp_camera(Vector2(960, 9999), level, vis).y, 840.0)
	_check("and left alone in the middle",
		MAIN.clamp_camera(Vector2(960, 560), level, vis).is_equal_approx(Vector2(960, 560)))

	# The view never leaves the level.
	var inside: bool = true
	for gx in [-5000.0, 0.0, 300.0, 960.0, 1800.0, 5000.0]:
		for gy in [-5000.0, 0.0, 200.0, 560.0, 1100.0, 5000.0]:
			var c: Vector2 = MAIN.clamp_camera(Vector2(gx, gy), level, vis)
			var r := Rect2(c - vis * 0.5, vis)
			if r.position.x < -0.001 or r.position.y < -0.001:
				inside = false
			if r.end.x > level.x + 0.001 or r.end.y > level.y + 0.001:
				inside = false
	_check("the visible rect never leaves the level", inside)

	# A level NARROWER than the view centres rather than jamming against a side.
	var narrow := Vector2(400, 300)
	_eq("a narrow level centres horizontally",
		MAIN.clamp_camera(Vector2(0, 0), narrow, vis).x, 200.0)
	_eq("and vertically", MAIN.clamp_camera(Vector2(0, 0), narrow, vis).y, 150.0)

	# Purity: same arguments, same answer, no hidden state.
	_check("clamp_camera is pure",
		MAIN.clamp_camera(Vector2(123, 456), level, vis)
			== MAIN.clamp_camera(Vector2(123, 456), level, vis))

	# THE assertion. The camera must not be able to reach the sim: two runs with
	# identical inputs must agree bit for bit however the view was moved.
	var a: RefCounted = bridge_script.new()
	var c2: RefCounted = bridge_script.new()
	var level_text: String = FileAccess.get_file_as_string("res://levels/substation_4.txt")
	a.Load(level_text, 31)
	c2.Load(level_text, 31)
	for i in range(240):
		a.Step(i % 3 - 1, (i / 3) % 3 - 1, i * 611, 1 if i % 5 == 0 else 0, 0, 1, 0, 0, 0, 0)
		c2.Step(i % 3 - 1, (i / 3) % 3 - 1, i * 611, 1 if i % 5 == 0 else 0, 0, 1, 0, 0, 0, 0)
	_eq("the camera cannot reach the sim", a.StateHash(), c2.StateHash())

	# The vision polygon pre-filters walls by distance before casting. It is a
	# conservative superset, so the polygon must come out IDENTICAL to the
	# unfiltered one -- an optimisation that quietly changes what the player can
	# see is a bug wearing a performance costume. Compared point for point
	# against the unfiltered path, from a walk around the level.
	var v: RefCounted = bridge_script.new()
	v.Load(level_text, 17)
	var same: bool = true
	var worst: float = 0.0
	for step in range(60):
		v.Step(1 if step % 2 == 0 else 0, 1, step * 331, 0, 0, 1, 0, 0, 0, 0)
		var filtered: PackedVector2Array = v.GetVisionPolygon(160, 430)
		var brute: PackedVector2Array = v.GetVisionPolygonUnfiltered(160, 430)
		if filtered.size() != brute.size():
			same = false
			break
		for k in range(filtered.size()):
			var d: float = filtered[k].distance_to(brute[k])
			if d > worst:
				worst = d
			if d > 0.0:
				same = false
	_check("filtering walls by range changes no ray", same,
		"worst divergence %f px" % worst)

	var probe: RefCounted = bridge_script.new()
	probe.Load(level_text, 17)
	probe.Step(0, 0, 0, 0, 0, 1, 0, 0, 0, 0)
	var poly: PackedVector2Array = probe.GetVisionPolygon(400, 430)
	_eq("the polygon has one point per ray", poly.size(), 400)
	var ppos := Vector2(probe.GetPlayer()[0] / 256.0, probe.GetPlayer()[1] / 256.0)
	var within: bool = true
	for pt in poly:
		if pt.distance_to(ppos) > 430.0 + 1.0:
			within = false
	_check("and no ray reaches past its radius", within)


# ----------------------------------------------------- swapping levels
#
# The start screen can deploy into a different level than the one loaded, so a
# live bridge has to swap geometry cleanly. The hazard is STALE CACHE: main.gd
# holds the wall array and the exit rect from load time, and a swap that left
# either behind would draw one level's walls over another's floor and let the
# player walk through them.

func _check_level_swap(bridge_script: Script) -> void:
	print("  -- swapping levels --")

	var files: PackedStringArray = LEVELS.list()
	_check("there are levels to swap between", files.size() >= 2,
		"%d found" % files.size())
	if files.size() < 2:
		return

	# Two files of genuinely different shape, so a stale cache cannot pass by
	# coincidence.
	var small: String = ""
	var big: String = ""
	for f in files:
		var probe: RefCounted = bridge_script.new()
		probe.Load(LEVELS.read(f), 1)
		if small.is_empty() or probe.GridCols < _cols_of(bridge_script, small):
			small = f
		if big.is_empty() or probe.GridCols > _cols_of(bridge_script, big):
			big = f
	_check("found two differently sized levels", _cols_of(bridge_script, small)
		< _cols_of(bridge_script, big),
		"%s vs %s" % [small.get_file(), big.get_file()])

	var b: RefCounted = bridge_script.new()
	b.Load(LEVELS.read(small), 5)
	var w1: int = b.GetWalls().size()
	var c1: int = b.GridCols
	var e1: PackedInt32Array = b.GetExit()

	# Swap, in place, the way _load_level does.
	b.Load(LEVELS.read(big), 5)
	_check("the swapped level is a different width", b.GridCols != c1,
		"%d then %d" % [c1, b.GridCols])
	_check("and carries different walls", b.GetWalls().size() != w1)
	_check("and a different exit", b.GetExit() != e1)

	# What a swap produces must equal what a fresh load produces, or the bridge
	# is carrying something over between levels.
	var fresh: RefCounted = bridge_script.new()
	fresh.Load(LEVELS.read(big), 5)
	_eq("a swapped load matches a fresh one: cols", b.GridCols, fresh.GridCols)
	_eq("rows", b.GridRows, fresh.GridRows)
	_eq("walls", b.GetWalls().size(), fresh.GetWalls().size())
	_eq("guards", b.GetGuards().size(), fresh.GetGuards().size())
	_eq("caches", b.GetCaches().size(), fresh.GetCaches().size())
	_eq("and the state hash", b.StateHash(), fresh.StateHash())

	# And it still simulates: a swapped-into level must be steppable and record.
	for i in range(120):
		b.Step(1, 1, i * 419, 0, 0, 1, 0, 0, 0, 0)
		fresh.Step(1, 1, i * 419, 0, 0, 1, 0, 0, 0, 0)
	_eq("a swapped level steps identically to a fresh one",
		b.StateHash(), fresh.StateHash())
	_eq("and records every tick", b.RecordedTicks, 120)

	# Swapping back restores the first level exactly.
	b.Load(LEVELS.read(small), 5)
	_eq("swapping back restores the width", b.GridCols, c1)
	_eq("and the walls", b.GetWalls().size(), w1)
	_eq("and the exit", b.GetExit(), e1)


func _cols_of(bridge_script: Script, path: String) -> int:
	var p: RefCounted = bridge_script.new()
	p.Load(LEVELS.read(path), 1)
	return p.GridCols


# --------------------------------------------------- staged vs held kit
#
# The equipment screen and the loadout menu STAGE a kit; only a restart puts it
# in the player's hands. That gap is new -- both used to restart on close -- and
# it broke the HUD silently: it named the staged weapon and counted the staged
# magazine, so equipping a rifle showed a rifle while a pistol did the firing.

func _check_staged_kit(bridge_script: Script) -> void:
	print("  -- staged vs held --")

	const STASH := preload("res://game/stash.gd")
	const CAT := preload("res://game/item_catalog.gd")

	var b: RefCounted = bridge_script.new()
	b.Load(LEVELS.read("res://levels/substation_4.txt"), 99)

	var st: RefCounted = STASH.new(b)
	st.stock_default()
	st.equip_starting_kit()
	st.apply_to(b)
	b.Restart(99)

	_eq("a fresh run holds what it staged", b.HeldWeaponName, b.StagedWeaponName)
	_check("and reports nothing pending", not b.LoadoutStaged)
	var start_mag: int = b.HeldMagazineSize

	# Equip a rifle WITHOUT restarting, the way closing the equipment screen
	# mid-run now does.
	var pi: int = st.add(102)
	_check("fixture: the rifle goes in the stash", pi != -1)
	_check("fixture: it equips", st.equip_from_grid(pi, CAT.SLOT_PRIMARY))
	st.apply_to(b)

	_eq("the staged weapon changes", b.StagedWeaponName, "AK-47")
	_eq("but the held one does not", b.HeldWeaponName, "Glock")
	_check("and the gap is reported", b.LoadoutStaged)
	_eq("the magazine still describes what is held", b.HeldMagazineSize, start_mag)
	_check("which is not the staged weapon's", b.HeldMagazineSize != b.MagazineSize)

	# Restarting is what actually arms you.
	b.Restart(99)
	_eq("restarting puts the staged weapon in hand", b.HeldWeaponName, "AK-47")
	_check("and clears the pending flag", not b.LoadoutStaged)
	_eq("with its own magazine", b.HeldMagazineSize, 30)
	_eq("which the sim agrees with", b.GetPlayer()[4], 30)

	# An X swap changes what is HELD without staging anything -- the indicator
	# must not fire for it, or it would be on permanently after every swap.
	var pg: int = st.add(100)
	st.equip_from_grid(pg, CAT.SLOT_SECONDARY)
	st.apply_to(b)
	b.Restart(99)
	_eq("fixture: primary is the rifle", b.HeldWeaponName, "AK-47")
	b.Step(0, 0, 0, 64, 0, 1, 0, 0, 0, 0)
	for i in range(40):
		b.Step(0, 0, 0, 0, 0, 1, 0, 0, 0, 0)
	_eq("swapping changes what is held", b.HeldWeaponName, "Glock")
	_eq("and the magazine follows it", b.HeldMagazineSize, 17)
	_check("but nothing is pending", not b.LoadoutStaged)


# ------------------------------------------------------- editor resizing

func _check_resize(bridge_script: Script) -> void:
	print("  -- editor resize --")

	var b: RefCounted = bridge_script.new()
	b.EditorBeginFrom(FileAccess.get_file_as_string("res://levels/substation_4.txt"))
	var c0: int = b.EditorCols
	var r0: int = b.EditorRows
	_eq("the reference level opens at 48 wide", c0, 48)
	_eq("and 28 tall", r0, 28)

	# Growing keeps everything and drops nothing.
	var lost: int = b.EditorResize(c0 + 16, r0 + 12)
	_eq("growing drops nothing", lost, 0)
	_eq("and the level is wider", b.EditorCols, c0 + 16)
	_eq("and taller", b.EditorRows, r0 + 12)

	# A grown level is still valid, still has its markers, and re-parses at the
	# new size -- the round-trip is what the playtest path depends on.
	var grown: String = b.EditorToText()
	var re: RefCounted = bridge_script.new()
	re.Load(grown, 1)
	_eq("a grown level loads at its new width", re.GridCols, c0 + 16)
	_eq("and its new height", re.GridRows, r0 + 12)
	_eq("its pixel width follows", re.GridWidthPx, (c0 + 16) * 20)

	var errors: int = 0
	for issue in b.EditorValidate():
		if issue.begins_with("error"):
			errors += 1
	_eq("and a grown level still validates", errors, 0)

	# The new border must be sealed, or the level leaks at the seam.
	var sealed: bool = true
	for c in range(b.EditorCols):
		if b.EditorGetCell(c, 0) != 35 or b.EditorGetCell(c, b.EditorRows - 1) != 35:
			sealed = false
	for r in range(b.EditorRows):
		if b.EditorGetCell(0, r) != 35 or b.EditorGetCell(b.EditorCols - 1, r) != 35:
			sealed = false
	_check("a resized level is sealed at its new border", sealed)

	# Shrinking past occupied cells REPORTS what it dropped rather than eating
	# it. That report is the whole contract: a silent shrink loses a room.
	var b2: RefCounted = bridge_script.new()
	b2.EditorBeginFrom(FileAccess.get_file_as_string("res://levels/substation_4.txt"))
	var dropped: int = b2.EditorResize(20, 14)
	_check("shrinking past content says how much it dropped", dropped > 0,
		"reported %d" % dropped)
	_eq("and the level really is smaller", b2.EditorCols, 20)

	# Resizing to the same size is a no-op.
	var b3: RefCounted = bridge_script.new()
	b3.EditorBeginBlank()
	var before: String = b3.EditorToText()
	_eq("resizing to the current size drops nothing",
		b3.EditorResize(b3.EditorCols, b3.EditorRows), 0)
	_check("and changes nothing", b3.EditorToText() == before)

	# Absurd sizes clamp rather than allocating or crashing.
	var b4: RefCounted = bridge_script.new()
	b4.EditorBeginBlank()
	b4.EditorResize(99999, 99999)
	_check("an absurd resize clamps", b4.EditorCols <= 512 and b4.EditorRows <= 512,
		"%dx%d" % [b4.EditorCols, b4.EditorRows])
	b4.EditorResize(1, 1)
	_check("and a tiny one clamps up", b4.EditorCols >= 12 and b4.EditorRows >= 12,
		"%dx%d" % [b4.EditorCols, b4.EditorRows])


# ------------------------------------------------------ replay recording

func _check_recording(bridge_script: Script) -> void:
	print("  -- replay recording --")

	var b: RefCounted = bridge_script.new()
	b.Load(FileAccess.get_file_as_string("res://levels/substation_4.txt"), 99)

	_eq("a fresh bridge has recorded nothing", b.RecordedTicks, 0)
	_check("a fresh run is not closed", not b.RecordingClosed)

	for i in range(120):
		b.Step(0, 0, 0, 0, 0, 1, 0, 0, 0, 0)
	_eq("every live tick is recorded", b.RecordedTicks, 120)

	# Checkpoints land every 60 ticks.
	var text: String = b.GetReplayText()
	var checkpoints: int = 0
	for line in text.split("\n"):
		if line.begins_with("hash:"):
			checkpoints += 1
	_eq("two checkpoints after 120 ticks", checkpoints, 2)
	_check("the level is embedded", text.contains("endlevel"))
	_check("the seed is recorded", text.contains("seed: 99"))

	# Walk into the exit to end the run, then confirm recording stops dead.
	#
	# On an OPEN room, not the reference level: the walk below is a greedy
	# sign-step with no wall avoidance, so any authored geometry between spawn
	# and exit -- a spawn room with its door in a corner, say -- wedges it and
	# the run never ends. What is under test is that ending a run closes the
	# recording, not that a naive walker can solve a level.
	var b2: RefCounted = bridge_script.new()
	b2.Load(_open_room(), 7)
	var exit_rect: PackedInt32Array = b2.GetExit()
	var target_x: int = exit_rect[0] + exit_rect[2] / 2
	var target_y: int = exit_rect[1] + exit_rect[3] / 2

	var ended_at: int = -1
	for i in range(60 * 90):
		var p: PackedInt32Array = b2.GetPlayer()
		var mx: int = signi(target_x - p[0])
		var my: int = signi(target_y - p[1])
		b2.Step(mx, my, 0, 0, 0, 1, 0, 0, 0, 0)
		if b2.GetWorld()[6] != 0:
			ended_at = b2.RecordedTicks
			break

	_check("the run can be ended", ended_at > 0, "never ended")
	if ended_at > 0:
		_check("recording is closed once the run ends", b2.RecordingClosed)
		# A death screen left open must not grow the buffer forever.
		for i in range(600):
			b2.Step(0, 0, 0, 0, 0, 1, 0, 0, 0, 0)
		_eq("ticks after the run are not recorded", b2.RecordedTicks, ended_at)

	# Restart clears the recording for the next run.
	b2.Restart(7)
	_eq("restart clears the recording", b2.RecordedTicks, 0)
	_check("restart reopens recording", not b2.RecordingClosed)
	b2.Step(0, 0, 0, 0, 0, 1, 0, 0, 0, 0)
	_eq("and records again", b2.RecordedTicks, 1)

	# Playback round-trip through the bridge.
	var b3: RefCounted = bridge_script.new()
	b3.Load(FileAccess.get_file_as_string("res://levels/substation_4.txt"), 5)
	for i in range(180):
		b3.Step(1, 0, i * 97, 0, 0, 1, 0, 0, 0, 0)
	var recorded: String = b3.GetReplayText()

	var b4: RefCounted = bridge_script.new()
	b4.Load(FileAccess.get_file_as_string("res://levels/substation_4.txt"), 1)
	_check("a replay loads", b4.LoadReplay(recorded))
	_eq("replay length matches", b4.ReplayLength, 180)
	_eq("replay starts at tick 0", b4.ReplayIndex, 0)
	_check("nothing has diverged yet", b4.DivergedTick < 0)

	while not b4.ReplayFinished:
		b4.StepPlayback()
	_eq("playback reaches the end", b4.ReplayIndex, 180)
	_check("an untouched replay does not diverge", b4.DivergedTick < 0,
		"diverged at %d" % b4.DivergedTick)

	# Playback must land on the same state the recording ended in.
	_eq("playback ends where the recording did", b4.GetPlayer()[0], b3.GetPlayer()[0])
	_eq("playback ends on the same row", b4.GetPlayer()[1], b3.GetPlayer()[1])

	# Seek re-simulates exactly.
	b4.SeekTo(90)
	_eq("seek sets the index", b4.ReplayIndex, 90)
	var mid_x: int = b4.GetPlayer()[0]
	b4.SeekTo(0)
	b4.SeekTo(90)
	_eq("seeking twice lands identically", b4.GetPlayer()[0], mid_x)

	_check("an empty replay is refused", not b4.LoadReplay(""))
	_check("a garbage replay is refused", not b4.LoadReplay("nonsense\nframes:\nzz"))


## A bare walled room with a spawn top-left and an exit bottom-right, and
## nothing whatsoever in between.
func _open_room() -> String:
	var rows := PackedStringArray()
	for r in range(GH):
		var line := ""
		for c in range(GW):
			if r == 0 or r == GH - 1 or c == 0 or c == GW - 1:
				line += "#"
			elif r == 2 and c == 2:
				line += "@"
			elif r == GH - 3 and c == GW - 3:
				line += "X"
			else:
				line += "."
		rows.append(line)
	return "name: open\ngrid:\n" + "\n".join(rows) + "\n"


# ------------------------------------------------------------ input gating
#
# BOTH mouse buttons are weapon controls -- left fires, right aims -- so every
# full-screen panel has to take them away while it is up, or a click meant for
# that panel goes off down the corridor instead. That rule lives in a static
# function precisely so it can be asserted here: _physics_process cannot be run
# headlessly, and the rule has been broken twice by adding a screen and
# forgetting the mouse. The second time was the FIELD VIEW, which held the
# player still, said it had taken his hands off the weapon, and fired anyway.

const F_FIRE: int = 1
const F_DILATE: int = 4
const F_SUBDUE: int = 8
const F_RELOAD: int = 16
const F_AIM: int = 32
const F_SWAP: int = 64
const F_LOOT: int = 128

func _check_input_gates() -> void:
	print("  -- input gating --")

	# Hands on the weapon: everything held reaches the sim.
	var open_handed: int = MAIN.sim_flags(true, true, true, true, true, true, true, true)
	_eq("every control reaches the sim with nothing in the way", open_handed,
		F_FIRE | F_DILATE | F_SUBDUE | F_RELOAD | F_AIM | F_SWAP | F_LOOT)

	# A panel under the cursor: the two mouse buttons and NOTHING else.
	var panelled: int = MAIN.sim_flags(true, true, true, true, true, true, true, false)
	_eq("a panel withholds fire", panelled & F_FIRE, 0)
	_eq("and aim", panelled & F_AIM, 0)
	_eq("and withholds nothing else", panelled, open_handed & ~(F_FIRE | F_AIM))

	# The mirror of the above: withholding is not the same as pressing nothing,
	# so the keyboard half has to still be there.
	_check("the keyboard still reaches the sim", panelled & F_RELOAD != 0
		and panelled & F_DILATE != 0 and panelled & F_SWAP != 0, str(panelled))

	# Nothing held is no flags, hands free or not -- the gate withholds, it does
	# not invent.
	_eq("nothing held is no flags", MAIN.sim_flags(false, false, false, false,
		false, false, false, true), 0)
	_eq("nor with a panel up", MAIN.sim_flags(false, false, false, false,
		false, false, false, false), 0)

	# The bits themselves are the replay format and the sim's own constants, so
	# they are fixed forever. One flag at a time, each landing on its own bit.
	_eq("fire is bit 0", MAIN.sim_flags(true, false, false, false, false, false, false, true), F_FIRE)
	_eq("dilate is bit 2", MAIN.sim_flags(false, true, false, false, false, false, false, true), F_DILATE)
	_eq("subdue is bit 3", MAIN.sim_flags(false, false, true, false, false, false, false, true), F_SUBDUE)
	_eq("reload is bit 4", MAIN.sim_flags(false, false, false, true, false, false, false, true), F_RELOAD)
	_eq("aim is bit 5", MAIN.sim_flags(false, false, false, false, true, false, false, true), F_AIM)
	_eq("swap is bit 6", MAIN.sim_flags(false, false, false, false, false, true, false, true), F_SWAP)
	_eq("loot is bit 7", MAIN.sim_flags(false, false, false, false, false, false, true, true), F_LOOT)

	# Bit 1 is the old sneak flag. The movement tier replaced it and it is now
	# free; nothing may quietly start setting it again, because a replay with it
	# set and no tier token is read as a STEALTH frame.
	_eq("bit 1 is free and stays free", open_handed & 2, 0)


# ------------------------------------------------- SimBridge.Step arity lint
#
## Godot registers EVERY C# parameter as required, defaults included, so adding
## one to SimBridge.Step breaks every .gd call site at RUNTIME -- "Nonexistent
## function 'Step'" -- while --check-only passes and the build is clean. That has
## now happened four times (lootPick, moveTier, dropPick, spawnItem/equipPick),
## and the last one silently aborted a whole test function, taking ten
## assertions with it and still reporting zero failures.
##
## So: count the arguments at every call site and compare against the ONE truth,
## SimBridge.cs itself. Nothing here needs updating when a ninth is added -- the
## expected count is read from the C# source.
func _check_step_arity() -> void:
	print("  -- SimBridge.Step arity lint --")

	var cs: String = FileAccess.get_file_as_string("res://game/SimBridge.cs")
	var sig := RegEx.new()
	sig.compile("public void Step\\(([^)]*)\\)")
	var m: RegExMatch = sig.search(cs)
	_check("found the Step signature in SimBridge.cs", m != null)
	if m == null:
		return

	var want: int = m.get_string(1).split(",").size()
	_check("Step takes a plausible number of parameters", want >= 4 and want <= 16,
		"%d" % want)

	var files: Array[String] = []
	_collect_gd("res://game", files)
	_collect_gd("res://tests", files)

	var bad: Array[String] = []
	var seen: int = 0
	for path in files:
		var line_no: int = 0
		for line in FileAccess.get_file_as_string(path).split("\n"):
			line_no += 1
			var at: int = line.find(".Step(")   # lint-ignore: this line IS the matcher
			if at < 0 or line.strip_edges().begins_with("#"):
				continue
			if line.contains("lint-ignore"):
				continue
			var args: int = _count_args(line.substr(at + 6))
			if args < 0:
				continue        # split across lines; not worth parsing
			seen += 1
			if args != want:
				bad.append("%s:%d has %d, wants %d"
					% [path.get_file(), line_no, args, want])

	_check("every .gd Step call site is found", seen > 0, "%d" % seen)
	_check("and every one passes %d arguments" % want, bad.is_empty(),
		" | ".join(bad))

	# Prove the counter works, or a clean pass means nothing.
	_eq("counts a flat call", _count_args("1, 2, 3)"), 3)
	_eq("counts nested parens as one argument", _count_args("a, f(b, c), d)"), 3)
	_eq("counts a single argument", _count_args("x)"), 1)
	_eq("reports -1 when the call does not close", _count_args("a, b"), -1)


## Arguments in `text`, which starts just after an opening paren and should
## contain its closing one. Commas inside nested parens or brackets do not
## separate arguments. -1 when the call never closes on this line.
func _count_args(text: String) -> int:
	var depth: int = 0
	var n: int = 1
	for i in range(text.length()):
		var c: String = text[i]
		if c == "(" or c == "[":
			depth += 1
		elif c == "]":
			depth -= 1
		elif c == ")":
			if depth == 0:
				return n
			depth -= 1
		elif c == "," and depth == 0:
			n += 1
	return -1


# ------------------------------------------------- gdscript format lint
#
# Lodging here rather than in its own runner to avoid a fourth test command.
# It scans every .gd file in the project, not just the editor's.

## GDScript's % operator supports a SMALLER set of conversions than C's printf.
## %e and %g in particular look right, compile fine, pass --check-only, and then
## spam "String formatting error" once per frame at runtime. This project has
## shipped that bug three times; it gets a lint.
const ALLOWED_CONVERSIONS := "sdfxXoc%v"

func _check_format_strings() -> void:
	print("  -- gdscript format lint --")

	var files: Array[String] = []
	_collect_gd("res://game", files)
	_collect_gd("res://tests", files)
	_check("found scripts to lint", files.size() > 0, "%d files" % files.size())

	# Only STRING LITERALS are scanned. A first attempt scanned whole lines and
	# allowed printf's space flag, which made the modulo operator itself match:
	# `(i + 1) % VOICES` looks exactly like `%V`. Comments mentioning %g matched
	# too. Both are noise that would have made the lint useless.
	var literals := RegEx.new()
	literals.compile('"(?:[^"\\\\]|\\\\.)*"')
	var conv := RegEx.new()
	conv.compile("%[-+0#]*[0-9]*(\\.[0-9]+)?([a-zA-Z])")

	var bad: Array[String] = []
	for path in files:
		var text: String = FileAccess.get_file_as_string(path)
		var line_no: int = 0
		for line in text.split("\n"):
			line_no += 1
			# A line may opt out: this lint's own fixtures are string literals
			# that contain bad conversions deliberately.
			if line.contains("lint-ignore"):
				continue
			for lit in literals.search_all(line):
				var body: String = lit.get_string(0).replace("%%", "")
				for m in conv.search_all(body):
					var c: String = m.get_string(2)
					if not ALLOWED_CONVERSIONS.contains(c):
						bad.append("%s:%d %s" % [path.get_file(), line_no, m.get_string(0)])

	_check("no unsupported format conversions in any .gd file", bad.is_empty(),
		" | ".join(bad))

	# Prove the matcher fires and does not over-fire, or a clean pass is
	# meaningless. These are the exact shapes that fooled the first version.
	_check("lint flags a bad conversion", _lint_line(conv, literals, 'print("%0.3e" % v)'))   # lint-ignore
	_check("lint accepts a good conversion", not _lint_line(conv, literals, 'print("%0.3f" % v)'))
	_check("lint ignores the modulo operator",
		not _lint_line(conv, literals, "_next = (_next + 1) % VOICES"))
	_check("lint ignores the format operator itself",
		not _lint_line(conv, literals, 'push_error("could not write %s" % path)'))
	_check("lint ignores a comment mentioning a conversion",
		not _lint_line(conv, literals, "## GDScript has no %g conversion"))   # lint-ignore
	_check("lint ignores a percentage in prose",
		not _lint_line(conv, literals, 'var s := "100% done"'))
	_check("lint ignores an escaped percent",
		not _lint_line(conv, literals, 'var s := "50%% off"'))


## True when this one line would be flagged.
func _lint_line(conv: RegEx, literals: RegEx, line: String) -> bool:
	for lit in literals.search_all(line):
		var body: String = lit.get_string(0).replace("%%", "")
		for m in conv.search_all(body):
			if not ALLOWED_CONVERSIONS.contains(m.get_string(2)):
				return true
	return false


func _collect_gd(dir_path: String, out: Array[String]) -> void:
	var dir := DirAccess.open(dir_path)
	if dir == null:
		return
	for f in dir.get_files():
		if f.ends_with(".gd"):
			out.append(dir_path + "/" + f)
	for d in dir.get_directories():
		_collect_gd(dir_path + "/" + d, out)


# ------------------------------------------------------- loot assignment

## Point buy is AUTHORED here: a guard's points and the floor's chest budget,
## both one undo step, both written into the level text, both what the mission
## select then shows.
func _check_loot_assignment(bridge_script: Script) -> void:
	print("  -- loot assignment --")
	var b: RefCounted = bridge_script.new()
	b.EditorBeginFrom(FileAccess.get_file_as_string("res://levels/substation_4.txt"))
	var default_pts: int = b.EditorGuardPoints("a".unicode_at(0))
	_check("an unassigned guard has the default points", default_pts > 0, str(default_pts))
	var total: int = b.EditorGuardLootTotal

	b.EditorSetGuardPoints("a".unicode_at(0), default_pts + 700)
	_eq("assigning points sets them", b.EditorGuardPoints("a".unicode_at(0)), default_pts + 700)
	_eq("and moves the floor's total by exactly that", b.EditorGuardLootTotal, total + 700)
	_check("and is written into the level", b.EditorToText().contains("kit: a %d" % (default_pts + 700)))

	b.EditorSetGuardPoints("a".unicode_at(0), default_pts)
	_check("setting him back to the default removes the line",
		not b.EditorToText().contains("kit: a"))

	var budget: int = b.EditorChestBudget
	b.EditorSetChestBudget(budget + 2500)
	_eq("the chest budget can be authored", b.EditorChestBudget, budget + 2500)
	_eq("and the mission select reads it", PackedInt32Array(b.LevelSummary(b.EditorToText()))[9],
		budget + 2500)
	b.EditorSetChestBudget(-1)
	_eq("and handed back to the chest count", b.EditorChestBudget, budget)

	# Through the editor node's own keys, with undo.
	var ed: Node = EDITOR.new()
	ed.bridge = b
	root.add_child(ed)
	b.EditorSelectGuard("c".unicode_at(0))
	var before: int = b.EditorGuardPoints("c".unicode_at(0))
	ed.adjust_guard_points("c".unicode_at(0), 1000)
	_eq("the editor's key raises the selected guard's points",
		b.EditorGuardPoints("c".unicode_at(0)), before + 1000)
	ed.adjust_guard_points("c".unicode_at(0), -999999)
	_eq("and never below zero", b.EditorGuardPoints("c".unicode_at(0)), 0)
	ed._undo_step()
	_eq("and undo gives the last assignment back", b.EditorGuardPoints("c".unicode_at(0)),
		before + 1000)
	ed.queue_free()


# ------------------------------------------------------- glass and doors

const EDITOR := preload("res://game/editor.gd")

func _check_panels(bridge_script: Script, b: RefCounted) -> void:
	print("  -- glass and doors --")

	# The palette: ten tools on ten number keys, the last three new, and
	# shift+chest the objective. Codes are palette order, so they must match
	# what EditorPaint switches on.
	_eq("the palette has eleven tools", EDITOR.TOOLS.size(), 11)
	_eq("tool 8 is the chest", EDITOR.TOOLS[T_CHEST]["glyph"], "C")
	_eq("tool 9 is glass", EDITOR.TOOLS[T_GLASS]["glyph"], "=")
	_eq("tool 0 is the door", EDITOR.TOOLS[T_DOOR]["glyph"], "+")
	_eq("shift with the chest tool paints an objective",
		EDITOR.paint_code(T_CHEST, true), T_OBJECTIVE)
	_eq("shift with anything else changes nothing", EDITOR.paint_code(T_GLASS, true), T_GLASS)

	b.EditorBeginBlank()
	b.EditorPaint(6, 6, T_GLASS, false)
	_eq("glass tool paints =", b.EditorGetCell(6, 6), "=".unicode_at(0))
	b.EditorPaint(0, 6, T_DOOR, false)
	_eq("the door tool paints INTO a wall", b.EditorGetCell(0, 6), "+".unicode_at(0))
	b.EditorPaint(8, 8, T_CHEST, false)
	_eq("chest tool paints C", b.EditorGetCell(8, 8), "C".unicode_at(0))
	b.EditorPaint(8, 8, T_CHEST, false)
	_eq("and toggles back off", b.EditorGetCell(8, 8), ".".unicode_at(0))
	b.EditorPaint(9, 8, T_OBJECTIVE, false)
	_eq("the objective code paints !", b.EditorGetCell(9, 8), "!".unicode_at(0))

	# The sweep node (Guard_AI.md §6.3.1): palette index 10, EditorPaint code 11.
	_eq("the eleventh tool is the sweep node", EDITOR.TOOLS[EDITOR.T_SWEEP]["glyph"], "*")
	_eq("and it sends code 11, not the objective's 10",
		EDITOR.paint_code(EDITOR.T_SWEEP, false), EDITOR.SWEEP_CODE)
	_eq("SWEEP_CODE is 11", EDITOR.SWEEP_CODE, 11)
	_check("a sweep node goes down one per click (not structure)",
		not b.EditorStampable("*".unicode_at(0)))
	b.EditorPaint(12, 12, EDITOR.SWEEP_CODE, false)
	_eq("the sweep tool paints *", b.EditorGetCell(12, 12), "*".unicode_at(0))
	b.EditorPaint(12, 12, EDITOR.SWEEP_CODE, false)
	_eq("and toggles back off", b.EditorGetCell(12, 12), ".".unicode_at(0))
	b.EditorPaint(12, 12, EDITOR.SWEEP_CODE, false)
	b.EditorPaint(12, 12, EDITOR.SWEEP_CODE, true)
	_eq("RMB erases it to floor", b.EditorGetCell(12, 12), ".".unicode_at(0))
	b.EditorPaint(13, 12, T_WALL, false)
	b.EditorPaint(13, 12, EDITOR.SWEEP_CODE, false)
	_eq("it does not knock through a wall", b.EditorGetCell(13, 12), "#".unicode_at(0))

	b.EditorBeginBlank()
	b.EditorPaint(5, 5, T_GUARD, false)
	for r in range(1, b.EditorRows - 1):
		b.EditorPaint(20, r, T_WALL, false)
	b.EditorPaint(30, 10, EDITOR.SWEEP_CODE, false)
	var sweep_issues: PackedStringArray = b.EditorValidate()
	_check("a sweep node no guard can reach is reported",
		" ".join(sweep_issues).contains("sweep node at (30,10)"), " | ".join(sweep_issues))

	# The validator: a one-cell door, a door in open floor, and an exit only
	# reachable through glass are all said out loud.
	b.EditorBeginBlank()
	b.EditorPaint(10, 10, T_DOOR, false)
	var issues: PackedStringArray = b.EditorValidate()
	_check("a one-cell door is too narrow, and says so",
		" ".join(issues).contains("one cell"), " | ".join(issues))
	_check("a door in open floor is not in a wall, and says so",
		" ".join(issues).contains("not set in a wall"), " | ".join(issues))

	b.EditorBeginBlank()
	b.EditorPaint(3, 3, T_SPAWN, false)
	for r in range(1, GH - 1):
		b.EditorPaint(24, r, T_GLASS if r == 10 or r == 11 else T_WALL, false)
	var sealed: PackedStringArray = b.EditorValidate()
	_check("an exit behind glass is a warning, not an error",
		" ".join(sealed).contains("through glass")
			and not " ".join(sealed).contains("not reachable"), " | ".join(sealed))

	# Round-trips through the one parser, and the summary counts them.
	var text: String = b.EditorToText()
	var back: RefCounted = bridge_script.new()
	back.EditorBeginFrom(text)
	_eq("glass survives a save/load", back.EditorGetCell(24, 10), "=".unicode_at(0))
	var sm: PackedInt32Array = b.LevelSummary(text)
	_eq("the summary counts the window as one pane", sm[7], 1)
	_eq("and no doors", sm[8], 0)

	# G is one key for two verbs. The nearer thing wins the press; a hold never
	# swings a door; and nothing happens behind the field view.
	var door := PackedInt32Array([3, 0, 0, 0, 20 * 256])
	var loot := PackedInt32Array([0, 0, 1, 1, 2, 0])
	_eq("a press with only a door in reach opens it",
		MAIN.door_pick_for(true, false, door, PackedInt32Array(), -1), 4)
	_eq("a body nearer than the door wins the press",
		MAIN.door_pick_for(true, false, door, loot, 10 * 256), 0)
	_eq("a door nearer than the body wins it",
		MAIN.door_pick_for(true, false, door, loot, 30 * 256), 4)
	_eq("holding G does not swing the door again",
		MAIN.door_pick_for(false, false, door, PackedInt32Array(), -1), 0)
	_eq("nor does a press behind the field view",
		MAIN.door_pick_for(true, true, door, PackedInt32Array(), -1), 0)
	_eq("no door, no pick", MAIN.door_pick_for(true, false, PackedInt32Array(),
		PackedInt32Array(), -1), 0)

	# The door target and panels reach game/ from a LIVE level.
	var live: RefCounted = bridge_script.new()
	live.Load(text, 1)
	var panels: PackedInt32Array = live.GetPanels()
	_eq("a live level reports its panels, stride 6", panels.size(), 6)
	_eq("and the pane is glass, whole", [panels[0], panels[5] & 1], [0, 0])


# --------------------------------------------------------------- painting

func _check_painting(b: RefCounted) -> void:
	print("  -- painting --")
	b.EditorBeginBlank()

	_eq("blank grid has a wall border", b.EditorGetCell(0, 0), "#".unicode_at(0))
	_eq("blank grid has floor inside", b.EditorGetCell(5, 5), ".".unicode_at(0))

	b.EditorPaint(5, 5, T_WALL, false)
	_eq("wall tool paints #", b.EditorGetCell(5, 5), "#".unicode_at(0))

	b.EditorPaint(5, 5, T_FLOOR, false)
	_eq("floor tool paints .", b.EditorGetCell(5, 5), ".".unicode_at(0))

	b.EditorPaint(7, 7, T_WALL, false)
	b.EditorPaint(7, 7, 0, true)
	_eq("right-click erases to floor", b.EditorGetCell(7, 7), ".".unicode_at(0))

	# Exit and cache toggle.
	b.EditorPaint(9, 9, T_EXIT, false)
	_eq("exit tool paints X", b.EditorGetCell(9, 9), "X".unicode_at(0))
	b.EditorPaint(9, 9, T_EXIT, false)
	_eq("exit tool toggles back off", b.EditorGetCell(9, 9), ".".unicode_at(0))

	b.EditorPaint(11, 9, T_CACHE, false)
	_eq("cache tool paints $", b.EditorGetCell(11, 9), "$".unicode_at(0))
	b.EditorPaint(11, 9, T_CACHE, false)
	_eq("cache tool toggles back off", b.EditorGetCell(11, 9), ".".unicode_at(0))

	# Spawn is unique: the parser takes the last '@' it scans, so a stray one
	# would make the level depend on scan order.
	b.EditorPaint(3, 3, T_SPAWN, false)
	b.EditorPaint(20, 12, T_SPAWN, false)
	_eq("the old spawn is cleared", b.EditorGetCell(3, 3), ".".unicode_at(0))
	_eq("the new spawn is set", b.EditorGetCell(20, 12), "@".unicode_at(0))

	var spawns: int = 0
	var grid: PackedByteArray = b.EditorGetGrid()
	for i in range(grid.size()):
		if grid[i] == "@".unicode_at(0):
			spawns += 1
	_eq("exactly one spawn exists", spawns, 1)

	# Painting out of bounds must be a no-op, not a crash.
	_eq("out-of-bounds paint is ignored", b.EditorPaint(-1, 5, T_WALL, false), 0)
	_eq("out-of-bounds paint past edge is ignored", b.EditorPaint(GW, 5, T_WALL, false), 0)
	_eq("out-of-bounds read is 0", b.EditorGetCell(999, 999), 0)

	# Merged rect count is live feedback while painting (spec §2.2).
	b.EditorBeginBlank()
	var border: int = b.EditorMergedRectCount()
	_check("a blank room merges to few rects", border > 0 and border <= 4, str(border))
	for c in range(4, 20):
		b.EditorPaint(c, 10, T_WALL, false)
	_check("a painted run merges into the count",
		b.EditorMergedRectCount() > border, "still %d" % b.EditorMergedRectCount())
	_eq("wall cells counted", b.EditorWallCellCount(), 2 * GW + 2 * (GH - 2) + 16)


# ------------------------------------------------------- guards and routes

func _check_guards_and_routes(b: RefCounted) -> void:
	print("  -- guards and routes --")
	b.EditorBeginBlank()

	b.EditorPaint(6, 6, T_GUARD, false)
	_eq("first guard is 'a'", b.EditorGetCell(6, 6), "a".unicode_at(0))
	_eq("placing a guard selects it", b.EditorSelectedGuard, "a".unicode_at(0))

	# A new guard's route starts at its own cell (prototype parity).
	var route: PackedInt32Array = b.EditorGetRoute("a".unicode_at(0))
	_eq("route starts with the guard's own cell", route.size(), 2)
	_eq("route point 0 col", route[0], 6)
	_eq("route point 0 row", route[1], 6)

	b.EditorPaint(10, 6, T_WAYPOINT, false)
	b.EditorPaint(10, 12, T_WAYPOINT, false)
	route = b.EditorGetRoute("a".unicode_at(0))
	_eq("waypoints append", route.size(), 6)
	_eq("last waypoint col", route[4], 10)
	_eq("last waypoint row", route[5], 12)

	b.EditorRouteUndoPoint("a".unicode_at(0))
	_eq("backspace drops the last waypoint", b.EditorGetRoute("a".unicode_at(0)).size(), 4)

	# Second guard gets the next free letter.
	b.EditorPaint(20, 6, T_GUARD, false)
	_eq("second guard is 'b'", b.EditorGetCell(20, 6), "b".unicode_at(0))
	_eq("selection follows the new guard", b.EditorSelectedGuard, "b".unicode_at(0))

	# Clicking an existing guard selects rather than replaces it.
	b.EditorPaint(6, 6, T_GUARD, false)
	_eq("clicking a guard selects it", b.EditorSelectedGuard, "a".unicode_at(0))
	_eq("and does not change the glyph", b.EditorGetCell(6, 6), "a".unicode_at(0))

	var ids: PackedByteArray = b.EditorGuardIds()
	_eq("two guards are listed", ids.size(), 2)

	# Erasing a guard takes its route with it, or the file keeps a route line
	# pointing at a guard that is not there.
	b.EditorPaint(20, 6, 0, true)
	_eq("erased guard is gone from the grid", b.EditorGetCell(20, 6), ".".unicode_at(0))
	_eq("erased guard's route is gone", b.EditorGetRoute("b".unicode_at(0)).size(), 0)
	_eq("one guard remains", b.EditorGuardIds().size(), 1)

	# The freed letter is reused.
	b.EditorPaint(30, 6, T_GUARD, false)
	_eq("the freed letter is reused", b.EditorGetCell(30, 6), "b".unicode_at(0))

	b.EditorRouteClear("a".unicode_at(0))
	_eq("clear route empties it", b.EditorGetRoute("a".unicode_at(0)).size(), 0)

	# Twenty-six guards is the cap ('a'..'z'). It was eight, which capped how
	# dangerous ANY level could be regardless of its size.
	b.EditorBeginBlank()
	var cap: int = "z".unicode_at(0) - "a".unicode_at(0) + 1
	_eq("the cap is the whole lowercase alphabet", cap, 26)
	for i in range(cap + 4):
		b.EditorPaint(2 + (i % 20) * 2, 4 + (i / 20) * 3, T_GUARD, false)
	_eq("at most twenty-six guards", b.EditorGuardIds().size(), cap)
	_eq("and the next placement is refused", b.EditorNextGuardId(), 0)

	# The letters really do run a..z, with no gaps.
	var letters: PackedByteArray = b.EditorGuardIds()
	var contiguous: bool = true
	for i in range(letters.size()):
		if letters[i] != "a".unicode_at(0) + i:
			contiguous = false
	_check("and they are a..z in order", contiguous)


# ------------------------------------------------------------- round-trip

func _check_roundtrip(b: RefCounted) -> void:
	print("  -- text round-trip --")

	# The editor must survive the same round-trip the format guarantees
	# (spec §2.1) — otherwise saving and reloading silently mutates the level.
	b.EditorBeginFrom(FileAccess.get_file_as_string("res://levels/substation_4.txt"))
	var once: String = b.EditorToText()
	b.EditorBeginFrom(once)
	var twice: String = b.EditorToText()
	_check("editing an existing level round-trips", once == twice)

	var original: String = FileAccess.get_file_as_string("res://levels/substation_4.txt")
	_check("loading the reference level reproduces its text exactly", once == original)

	# And after edits.
	b.EditorPaint(24, 14, T_WALL, false)
	b.EditorPaint(25, 14, T_GUARD, false)
	b.EditorPaint(26, 16, T_WAYPOINT, false)
	var edited: String = b.EditorToText()
	b.EditorBeginFrom(edited)
	_check("an edited level round-trips", b.EditorToText() == edited)

	b.EditorName = "Test Chamber"
	_check("name is stored", b.EditorName == "Test Chamber")
	_check("name appears in the text", b.EditorToText().begins_with("name: Test Chamber"))
	b.EditorName = "   "
	_check("a blank name falls back to untitled", b.EditorName == "untitled")


# -------------------------------------------------------------- validation

func _check_validation(b: RefCounted) -> void:
	print("  -- validation --")

	b.EditorBeginBlank()
	var issues: PackedStringArray = b.EditorValidate()
	_check("a blank level warns about the missing spawn", _has(issues, "no spawn"))
	_check("a blank level warns about the missing exit", _has(issues, "no exit"))
	_check("a blank level notes it has no guards", _has(issues, "no guards"))
	_check("the merge count is always reported", _has(issues, "merge to"))

	# Sealed exit.
	b.EditorBeginBlank()
	b.EditorPaint(3, 3, T_SPAWN, false)
	b.EditorPaint(40, 20, T_EXIT, false)
	for r in range(GH):
		b.EditorPaint(24, r, T_WALL, false)
	_check("a walled-off exit is an error", _has(b.EditorValidate(), "not reachable"))

	b.EditorPaint(24, 14, T_FLOOR, false)
	_check("opening one cell clears it", not _has(b.EditorValidate(), "not reachable"))

	# Waypoint buried in a wall: the guard would grind against it forever.
	b.EditorBeginBlank()
	b.EditorPaint(3, 3, T_SPAWN, false)
	b.EditorPaint(40, 20, T_EXIT, false)
	b.EditorPaint(10, 10, T_GUARD, false)
	b.EditorPaint(14, 10, T_WALL, false)
	b.EditorPaint(14, 10, T_WAYPOINT, false)
	_check("a waypoint inside a wall is an error", _has(b.EditorValidate(), "inside a wall"))

	# A routeless guard is a sentry by design, so it is info, not a warning.
	b.EditorBeginBlank()
	b.EditorPaint(3, 3, T_SPAWN, false)
	b.EditorPaint(40, 20, T_EXIT, false)
	b.EditorPaint(12, 12, T_GUARD, false)
	b.EditorRouteClear("a".unicode_at(0))
	var sentry: PackedStringArray = b.EditorValidate()
	_check("a routeless guard is reported as a sentry", _has(sentry, "stationary sentry"))
	_check("and is info, not an error", not _has(sentry, "error: guard a"))

	# A clean level should raise no errors at all.
	b.EditorBeginFrom(FileAccess.get_file_as_string("res://levels/substation_4.txt"))
	var clean: PackedStringArray = b.EditorValidate()
	var errors: int = 0
	for issue in clean:
		if issue.begins_with("error"):
			errors += 1
			print("          unexpected: %s" % issue)
	_eq("the reference level has no errors", errors, 0)
	# The SHAPE of the report, not a memorised cell count. The reference level is
	# authored in this very editor and gets re-saved; pinning "222 -> 18" made
	# every level edit look like a code regression.
	var merged: bool = false
	for issue in clean:
		if issue.contains("wall cells merge to") and issue.contains("rects"):
			merged = true
	_check("the reference level reports its wall merge", merged, _join(clean))
	_check("and reports fewer rects than cells",
		b.EditorMergedRectCount() < b.EditorWallCellCount(),
		"%d rects from %d cells" % [b.EditorMergedRectCount(), b.EditorWallCellCount()])


func _has(list: PackedStringArray, needle: String) -> bool:
	for s in list:
		if s.contains(needle):
			return true
	return false


func _join(list: PackedStringArray) -> String:
	return " | ".join(list)


# ------------------------------------------------------------ editor node

func _check_editor_node(bridge_script: Script) -> void:
	print("  -- editor node --")

	var host := Node.new()
	root.add_child(host)

	var b2: RefCounted = bridge_script.new()
	b2.Load(FileAccess.get_file_as_string("res://levels/substation_4.txt"), 1)

	var ed_script: GDScript = load("res://game/editor.gd")
	var ed: Node2D = ed_script.new()
	ed.bridge = b2
	host.add_child(ed)

	_check("editor starts closed", not ed.active)

	ed.open_editor()
	_check("open_editor activates", ed.active)
	_check("opening seeds the buffer from the current level",
		b2.EditorToText().begins_with("name: Substation 4"))

	# Undo restores the exact prior text, because snapshots are level texts.
	var before: String = b2.EditorToText()
	ed._push_undo()
	b2.EditorPaint(24, 14, T_WALL, false)
	_check("a paint changes the text", b2.EditorToText() != before)
	ed._undo_step()
	_check("undo restores the previous text exactly", b2.EditorToText() == before)

	ed._undo_step()
	_check("undo past the start is harmless", ed.active)

	# Saving writes real bytes that parse back.
	b2.EditorName = "Harness Fixture"
	ed._save()
	var path: String = "res://levels/harness_fixture.txt"
	if not FileAccess.file_exists(path):
		path = "user://levels/harness_fixture.txt"
	_check("save wrote a file", FileAccess.file_exists(path), path)
	if FileAccess.file_exists(path):
		var back: String = FileAccess.get_file_as_string(path)
		_check("the saved file round-trips", back == b2.EditorToText())
		DirAccess.remove_absolute(ProjectSettings.globalize_path(path))

	_eq("slug strips punctuation and case", ed._slug("Substation 4!"), "substation_4")
	_eq("slug of an empty name", ed._slug("***"), "untitled")

	ed.close_editor()
	_check("close_editor deactivates", not ed.active)

	host.queue_free()


# ------------------------------------------------------------ editor tools
#
# Brush size, shapes, fill, select/copy/paste, redo, eyedropper. Driven through
# press/drag_to/release with CELLS, which is what the mouse handlers hand them,
# so every tool is reachable here without a cursor.

func _count_glyph(b: RefCounted, glyph: String) -> int:
	var want: int = glyph.unicode_at(0)
	var n: int = 0
	for g in b.EditorGetGrid():
		if g == want:
			n += 1
	return n


func _check_editor_tools(bridge_script: Script) -> void:
	print("  -- editor tools --")
	var W: int = "#".unicode_at(0)
	var F: int = ".".unicode_at(0)

	# The bridge half: a stamp never toggles, refuses actors, and takes a
	# guard's route with him; a flood is four-connected and stops at walls.
	var b: RefCounted = bridge_script.new()
	b.EditorBeginBlank()
	b.EditorStamp(5, 5, "X".unicode_at(0))
	b.EditorStamp(5, 5, "X".unicode_at(0))
	_eq("stamping an exit twice leaves an exit (no toggle)", b.EditorGetCell(5, 5), "X".unicode_at(0))
	_eq("spawn is not stampable", b.EditorStamp(6, 6, "@".unicode_at(0)), 0)
	_eq("nor is a guard", b.EditorStamp(6, 6, "a".unicode_at(0)), 0)
	b.EditorPaint(8, 8, T_GUARD, false)
	b.EditorStamp(8, 8, W)
	_eq("stamping over a guard removes him", b.EditorGuardIds().size(), 0)
	_eq("and his route", b.EditorGetRoute("a".unicode_at(0)).size(), 0)

	b.EditorBeginBlank()
	var inside: int = (GW - 2) * (GH - 2)
	_eq("flooding the empty room fills exactly the interior",
		b.EditorFlood(3, 3, "=".unicode_at(0)), inside)
	b.EditorBeginBlank()
	for r in range(1, GH - 1):
		b.EditorStamp(20, r, W)
	_eq("a wall line stops the flood", b.EditorFlood(3, 3, "C".unicode_at(0)), 19 * (GH - 2))
	_eq("flooding with the glyph already there changes nothing", b.EditorFlood(3, 3, "C".unicode_at(0)), 0)
	b.EditorBeginBlank()
	b.EditorStamp(10, 10, W)
	b.EditorStamp(11, 11, W)
	_eq("a flood is four-connected: diagonal walls are not a path",
		b.EditorFlood(10, 10, F), 1)

	# The editor half.
	var host := Node.new()
	root.add_child(host)
	var b2: RefCounted = bridge_script.new()
	b2.Load(FileAccess.get_file_as_string("res://levels/substation_4.txt"), 1)
	var ed: Node2D = EDITOR.new()
	ed.bridge = b2
	host.add_child(ed)
	ed.open_editor()

	_eq("the editor fills the design height", ed.FIELD_H + ed.HUD_H, 620.0)
	var rects: Array[Rect2] = []
	for i in range(EDITOR.TOOLS.size()):
		rects.append(ed._tool_rect(i))
	for i in range(EDITOR.MODES.size()):
		rects.append(ed._mode_rect(i))
	rects.append(ed._brush_rect())
	var fits: bool = true
	var overlap: bool = false
	for i in range(rects.size()):
		if rects[i].position.x < 0 or rects[i].end.x > ed.FIELD_W or rects[i].position.y < ed.FIELD_H \
				or rects[i].end.y > ed.FIELD_H + ed.HUD_H:
			fits = false
		for j in range(i + 1, rects.size()):
			if rects[i].intersects(rects[j]):
				overlap = true
	_check("every toolbar button sits inside the strip", fits)
	_check("and no two overlap", not overlap)

	# Brush size.
	b2.EditorBeginBlank()
	ed.set_tool(T_WALL)
	ed.set_mode(EDITOR.M_BRUSH)
	ed.set_brush(3)
	var walls0: int = b2.EditorWallCellCount()
	ed.press(Vector2i(10, 10), false)
	ed.release()
	_eq("a 3x3 brush paints nine cells", b2.EditorWallCellCount() - walls0, 9)
	_eq("centred on the cell", b2.EditorGetCell(9, 9), W)
	ed.set_brush(99)
	_eq("brush size clamps high", ed._brush, EDITOR.BRUSH_MAX)
	ed.set_brush(0)
	_eq("and low", ed._brush, 1)

	# A fast stroke is joined up, and it is ONE undo step.
	b2.EditorBeginBlank()
	ed._undo.clear()
	ed.press(Vector2i(5, 5), false)
	ed.drag_to(Vector2i(15, 5))
	ed.release()
	var joined: bool = true
	for c in range(5, 16):
		if b2.EditorGetCell(c, 5) != W:
			joined = false
	_check("a stroke fills every cell between two motion events", joined)
	_eq("and is one undo step", ed._undo.size(), 1)

	# A stroke that changes nothing leaves no undo step, and costs no redo.
	ed._undo_step()
	_eq("undo takes the stroke back", b2.EditorGetCell(10, 5), F)
	ed._redo_step()
	_eq("redo puts it back", b2.EditorGetCell(10, 5), W)
	ed._undo_step()
	ed.set_tool(T_FLOOR)
	ed.press(Vector2i(10, 10), false)
	ed.release()
	_eq("a no-op stroke leaves no undo step", ed._undo.size(), 0)
	_eq("and keeps the redo", ed._redo.size(), 1)
	ed.set_tool(T_WALL)
	ed.press(Vector2i(10, 10), false)
	ed.release()
	_eq("a real edit clears the redo", ed._redo.size(), 0)

	# Structure is stamped: dragging back over an exit keeps it.
	b2.EditorBeginBlank()
	ed.set_tool(T_EXIT)
	ed.press(Vector2i(6, 6), false)
	ed.drag_to(Vector2i(7, 6))
	ed.drag_to(Vector2i(6, 6))
	ed.release()
	_eq("a brush dragged back over its own exit does not toggle it", b2.EditorGetCell(6, 6),
		"X".unicode_at(0))
	ed.set_tool(T_CHEST)
	ed.press(Vector2i(12, 6), false, true)
	ed.release()
	_eq("shift with the chest brush stamps an objective", b2.EditorGetCell(12, 6), "!".unicode_at(0))

	# ESC mid-stroke rolls it back.
	b2.EditorBeginBlank()
	ed._undo.clear()
	ed.set_tool(T_WALL)
	ed.press(Vector2i(4, 4), false)
	ed.drag_to(Vector2i(8, 4))
	ed._escape()
	_eq("ESC mid-stroke rolls the stroke back", b2.EditorGetCell(6, 4), F)
	_eq("and leaves no undo step", ed._undo.size(), 0)

	# Actor tools place one per press, whatever the mode.
	b2.EditorBeginBlank()
	ed.set_tool(T_GUARD)
	ed.press(Vector2i(10, 10), false)
	ed.release()
	ed.set_tool(T_WAYPOINT)
	ed.set_mode(EDITOR.M_RECT)
	ed.press(Vector2i(12, 10), false)
	ed.drag_to(Vector2i(20, 10))
	ed.release()
	_eq("a dragged waypoint tool adds ONE waypoint", b2.EditorGetRoute("a".unicode_at(0)).size(), 4)

	# Shapes.
	b2.EditorBeginBlank()
	ed.set_tool(T_WALL)
	ed.set_brush(1)
	ed.set_mode(EDITOR.M_RECT)
	walls0 = b2.EditorWallCellCount()
	ed.press(Vector2i(5, 5), false)
	ed.drag_to(Vector2i(10, 8))
	_eq("a shape paints nothing until released", b2.EditorWallCellCount(), walls0)
	ed.release()
	_eq("a 6x4 rect outline is 16 cells", b2.EditorWallCellCount() - walls0, 16)
	_eq("and hollow", b2.EditorGetCell(7, 6), F)
	b2.EditorBeginBlank()
	ed.set_brush(2)
	ed.press(Vector2i(5, 5), false)
	ed.drag_to(Vector2i(12, 12))
	ed.release()
	_eq("a brush-2 rect has 2-thick walls that grow inward", b2.EditorGetCell(6, 6), W)
	_eq("keeping the dragged corner as the outside", b2.EditorGetCell(4, 4), F)
	_eq("and still hollow", b2.EditorGetCell(8, 8), F)

	b2.EditorBeginBlank()
	ed.set_brush(1)
	ed.set_mode(EDITOR.M_BOX)
	ed.set_tool(T_GLASS)
	ed.press(Vector2i(10, 12), false)
	ed.drag_to(Vector2i(3, 10))
	ed.release()
	_eq("a filled rect dragged backwards fills 8x3", _count_glyph(b2, "="), 24)
	ed.set_mode(EDITOR.M_BOX)
	ed.press(Vector2i(4, 11), true)
	ed.drag_to(Vector2i(5, 11))
	ed.release()
	_eq("RMB with a shape erases it", _count_glyph(b2, "="), 22)

	b2.EditorBeginBlank()
	ed.set_mode(EDITOR.M_LINE)
	ed.set_tool(T_WALL)
	walls0 = b2.EditorWallCellCount()
	ed.press(Vector2i(5, 5), false)
	ed.drag_to(Vector2i(15, 10))
	ed.release()
	_eq("a line covers its longer axis, one cell per column", b2.EditorWallCellCount() - walls0, 11)
	_eq("from end", b2.EditorGetCell(5, 5), W)
	_eq("to end", b2.EditorGetCell(15, 10), W)
	_eq("line cells include both ends", EDITOR.line_cells(Vector2i(0, 0), Vector2i(3, 0)).size(), 4)

	# Fill.
	b2.EditorBeginBlank()
	ed.set_mode(EDITOR.M_FILL)
	ed.set_tool(T_DOOR)
	ed.press(Vector2i(3, 3), false)
	ed.release()
	_eq("fill mode floods the room", _count_glyph(b2, "+"), (GW - 2) * (GH - 2))
	ed._undo_step()
	_eq("and undoes in one step", _count_glyph(b2, "+"), 0)

	# Select, copy, paste, rotate, flip.
	b2.EditorBeginBlank()
	b2.EditorStamp(2, 2, W)
	b2.EditorStamp(3, 2, "C".unicode_at(0))
	b2.EditorPaint(2, 3, T_GUARD, false)
	ed.set_mode(EDITOR.M_SELECT)
	ed.press(Vector2i(2, 2), false)
	ed.drag_to(Vector2i(4, 3))
	ed.release()
	_eq("select drags a rect", ed._sel, Rect2i(2, 2, 3, 2))
	_check("copy succeeds with a selection", ed.copy_selection())
	_eq("the clipboard is the selection's size", ed._clip_size, Vector2i(3, 2))
	_eq("a guard copies as floor", ed._clip[3], F)
	ed.begin_paste()
	ed.press(Vector2i(20, 10), false)
	_eq("paste stamps the wall", b2.EditorGetCell(20, 10), W)
	_eq("and the chest", b2.EditorGetCell(21, 10), "C".unicode_at(0))
	_eq("but no second guard", b2.EditorGuardIds().size(), 1)
	ed.press(Vector2i(30, 10), false)
	_eq("paste mode stays up for another stamp", b2.EditorGetCell(30, 10), W)
	b2.EditorPaint(40, 10, T_GUARD, false)
	ed.press(Vector2i(39, 9), false)
	_eq("pasted floor does not bury a guard", b2.EditorGuardIds().size(), 2)
	ed.press(Vector2i(0, 0), true)
	_check("RMB leaves paste mode", not ed._pasting)

	ed.rotate_clip()
	_eq("rotating swaps the clipboard's sides", ed._clip_size, Vector2i(2, 3))
	_eq("a quarter turn clockwise puts the top-left top-right", ed._clip[1], W)
	ed.rotate_clip()
	ed.rotate_clip()
	ed.rotate_clip()
	ed.flip_clip()
	_eq("a flip mirrors left to right", ed._clip[2], W)

	ed.set_mode(EDITOR.M_SELECT)
	ed.press(Vector2i(20, 10), false)
	ed.drag_to(Vector2i(22, 11))
	ed.release()
	ed.cut_selection()
	_eq("cut clears the structure", b2.EditorGetCell(20, 10), F)
	ed._sel = Rect2i(39, 9, 3, 3)
	ed.cut_selection()
	_eq("cut leaves a guard standing", b2.EditorGuardIds().size(), 2)
	ed.delete_selection()
	_eq("DEL takes him too", b2.EditorGuardIds().size(), 1)
	ed._sel = Rect2i(-5, -5, 3, 3)
	_check("copying a selection off the level refuses", not ed.copy_selection())

	# Eyedropper.
	ed.pick(Vector2i(0, 0))
	_eq("alt+click a wall picks the wall tool", ed._tool, T_WALL)
	ed.pick(Vector2i(21, 10))
	ed.pick(Vector2i(2, 3))
	_eq("alt+click a guard picks the guard tool", ed._tool, T_GUARD)
	_eq("and selects him", b2.EditorSelectedGuard, "a".unicode_at(0))

	# Undo keeps the zoom when the size is unchanged.
	ed._zoom = 2.0
	ed.set_mode(EDITOR.M_BRUSH)
	ed.set_tool(T_WALL)
	ed.press(Vector2i(25, 20), false)
	ed.release()
	ed._undo_step()
	_eq("undo does not re-fit the view", ed._zoom, 2.0)

	ed.close_editor()
	host.queue_free()


# ------------------------------------------------ routes, mirror, issues

func _check_editor_routes(bridge_script: Script) -> void:
	print("  -- editor routes, mirror, issues --")
	var A: int = "a".unicode_at(0)
	var W: int = "#".unicode_at(0)

	# Route editing by index, on the bridge.
	var b: RefCounted = bridge_script.new()
	b.EditorBeginBlank()
	b.EditorPaint(10, 10, T_GUARD, false)
	_check("a point can be inserted", b.EditorRouteInsertPoint(A, 1, 20, 10))
	_eq("at the index named", b.EditorGetRoute(A), PackedInt32Array([10, 10, 20, 10]))
	_check("and moved", b.EditorRouteMovePoint(A, 1, 20, 12))
	_eq("to the cell named", b.EditorGetRoute(A), PackedInt32Array([10, 10, 20, 12]))
	_check("a move off the grid refuses", not b.EditorRouteMovePoint(A, 1, -1, 5))
	_check("an index past the end refuses", not b.EditorRouteMovePoint(A, 5, 3, 3))
	_check("an insert past Count refuses", not b.EditorRouteInsertPoint(A, 3, 3, 3))
	_check("a point can be removed", b.EditorRouteRemovePoint(A, 0))
	_eq("leaving the rest", b.EditorGetRoute(A), PackedInt32Array([20, 12]))
	_check("a guard with no route refuses", not b.EditorRouteInsertPoint("b".unicode_at(0), 0, 3, 3))

	var host := Node.new()
	root.add_child(host)
	var b2: RefCounted = bridge_script.new()
	b2.Load(FileAccess.get_file_as_string("res://levels/substation_4.txt"), 1)
	var ed: Node2D = EDITOR.new()
	ed.bridge = b2
	host.add_child(ed)
	ed.open_editor()

	# The waypoint tool: append-and-drag, grab-and-drag, insert on the line,
	# RMB deletes one point.
	b2.EditorBeginBlank()
	ed._undo.clear()
	ed.set_mode(EDITOR.M_BRUSH)
	ed.set_tool(T_WAYPOINT)
	ed.press(Vector2i(20, 10), false)
	_eq("with no guard selected the waypoint tool does nothing", b2.EditorToText().contains(">"), false)
	ed.set_tool(T_GUARD)
	ed.press(Vector2i(10, 10), false)
	ed.set_tool(T_WAYPOINT)
	var steps: int = ed._undo.size()
	ed.press(Vector2i(20, 10), false)
	ed.drag_to(Vector2i(20, 15))
	ed.release()
	_eq("a press off the route appends, and the drag places it",
		b2.EditorGetRoute(A), PackedInt32Array([10, 10, 20, 15]))
	_eq("as one undo step", ed._undo.size(), steps + 1)
	ed.press(Vector2i(20, 15), false)
	ed.drag_to(Vector2i(20, 10))
	ed.release()
	_eq("a press ON a waypoint drags it", b2.EditorGetRoute(A), PackedInt32Array([10, 10, 20, 10]))
	_eq("the route line is found between points", ed.segment_at(Vector2i(15, 10)), 1)
	_eq("but not off it", ed.segment_at(Vector2i(15, 12)), -1)
	ed.press(Vector2i(15, 10), false)
	ed.drag_to(Vector2i(15, 6))
	ed.release()
	_eq("a press on the line inserts there", b2.EditorGetRoute(A),
		PackedInt32Array([10, 10, 15, 6, 20, 10]))
	ed.press(Vector2i(15, 6), true)
	_eq("RMB on a waypoint deletes just it", b2.EditorGetRoute(A), PackedInt32Array([10, 10, 20, 10]))
	_eq("and leaves the guard", b2.EditorGuardIds().size(), 1)
	b2.EditorStamp(30, 20, W)
	ed.press(Vector2i(30, 20), true)
	ed.release()
	_eq("RMB off the route erases as usual", b2.EditorGetCell(30, 20), ".".unicode_at(0))
	ed.press(Vector2i(20, 10), false)
	ed.drag_to(Vector2i(30, 5))
	ed._escape()
	_eq("ESC mid-drag puts the waypoint back", b2.EditorGetRoute(A), PackedInt32Array([10, 10, 20, 10]))
	_eq("the drag cannot leave the grid", _drag_clamped(ed, b2, A), PackedInt32Array([10, 10, 0, 10]))

	# Guard cycling.
	b2.EditorBeginBlank()
	b2.EditorPaint(5, 5, T_GUARD, false)
	b2.EditorPaint(30, 20, T_GUARD, false)
	b2.EditorSelectGuard(0)
	ed.cycle_guard(1)
	_eq("G from nothing selects the first guard", b2.EditorSelectedGuard, A)
	_eq("and centres the view on him", ed._pan, Vector2(5 * 20 + 10, 5 * 20 + 10))
	ed.cycle_guard(1)
	_eq("G again selects the next", b2.EditorSelectedGuard, "b".unicode_at(0))
	ed.cycle_guard(1)
	_eq("and wraps", b2.EditorSelectedGuard, A)
	ed.cycle_guard(-1)
	_eq("shift+G goes back", b2.EditorSelectedGuard, "b".unicode_at(0))

	# Mirror.
	b2.EditorBeginBlank()
	var nc: int = b2.EditorCols
	var nr: int = b2.EditorRows
	ed.set_tool(T_WALL)
	ed.set_brush(1)
	ed.set_mirror(1)
	ed.press(Vector2i(3, 5), false)
	ed.release()
	_eq("mirror X repeats the stroke across the centre", b2.EditorGetCell(nc - 4, 5), W)
	ed.set_mirror(3)
	ed.press(Vector2i(6, 7), false)
	ed.release()
	var four: bool = b2.EditorGetCell(nc - 7, 7) == W and b2.EditorGetCell(6, nr - 8) == W \
		and b2.EditorGetCell(nc - 7, nr - 8) == W
	_check("mirror XY repeats it four ways", four)
	ed.set_mirror(4)
	_eq("the mirror setting wraps", ed._mirror, 0)
	ed.set_mirror(1)
	b2.EditorBeginBlank()
	b2.EditorStamp(2, 2, W)
	b2.EditorStamp(3, 2, "C".unicode_at(0))
	ed._sel = Rect2i(2, 2, 2, 1)
	ed.copy_selection()
	b2.EditorBeginBlank()
	ed.begin_paste()
	ed.press(Vector2i(2, 2), false)
	ed._pasting = false
	_eq("a mirrored paste lays the far copy down flipped", b2.EditorGetCell(nc - 3, 2), W)
	_eq("both ways round", b2.EditorGetCell(nc - 4, 2), "C".unicode_at(0))
	b2.EditorStamp(nc - 6, 9, W)
	ed._sel = Rect2i(4, 8, 3, 3)
	b2.EditorStamp(5, 9, W)
	ed.delete_selection()
	_eq("DEL is not mirrored: it clears only what is selected", b2.EditorGetCell(nc - 6, 9), W)
	ed.set_mirror(0)

	# Issues are links.
	b2.EditorBeginBlank()
	b2.EditorPaint(5, 5, T_GUARD, false)
	b2.EditorPaint(9, 9, T_GUARD, false)
	b2.EditorSelectGuard(0)
	_check("an issue naming a guard is a link", EDITOR.issue_links("info: guard b has no route"))
	_check("one naming a cell is too", EDITOR.issue_links("warn: door at (4,7) is one cell"))
	_check("one naming neither is not", not EDITOR.issue_links("info: no guards placed"))
	_check("following a guard link works", ed.goto_issue("info: guard b has no route - sentry"))
	_eq("and selects him", b2.EditorSelectedGuard, "b".unicode_at(0))
	_eq("and shows him", ed._flash, Vector2i(9, 9))
	ed.goto_issue("warn: door at (4,7) is one cell")
	_eq("a cell link shows the cell", ed._flash, Vector2i(4, 7))
	_check("a line with no place goes nowhere", not ed.goto_issue("info: no guards placed"))
	ed._revalidate()
	ed._issues_open = true
	var head: Rect2 = ed._issue_row(-1)
	_eq("the panel header is hit as the fold control", ed.issue_at(head.get_center()), -1)
	_eq("a point off the panel is not the panel", ed.issue_at(Vector2(10, 300)), -2)

	ed.close_editor()
	host.queue_free()


## Drag a waypoint far off the grid's left edge and report the route.
func _drag_clamped(ed: Node2D, b: RefCounted, id: int) -> PackedInt32Array:
	ed.press(Vector2i(20, 10), false)
	ed.drag_to(Vector2i(-40, 10))
	ed.release()
	return b.EditorGetRoute(id)


## Guard AI v2 (Guard_AI.md §9.2): main.gd mirrors Sim.GuardState and
## Sim.GuardTask by hand, and the event kinds too. A reorder in the sim would
## silently draw the wrong glyph, gate the wrong edge marker and hide the radio
## ring, so every mirrored ordinal is checked against the sim's own names here.
## The debug overlay's snapshot is exercised too: a malformed section would
## misparse every section after it.
func _check_guard_ai_mirror(bridge_script: Script) -> void:
	print("  -- guard AI mirror --")
	var main_script: GDScript = load("res://game/main.gd")
	var consts: Dictionary = main_script.get_script_constant_map()
	var b: RefCounted = bridge_script.new()
	var states: PackedStringArray = b.GuardStateNames()
	var tasks: PackedStringArray = b.GuardTaskNames()
	for pair in [["ST_RELAXED", "Relaxed"], ["ST_CURIOUS", "Curious"], ["ST_COMBAT", "Combat"],
			["ST_HUNTING", "Hunting"], ["ST_DOWN", "Down"], ["ST_DEAD", "Dead"]]:
		var ord: int = consts.get(pair[0], -1)
		_check("%s mirrors GuardState.%s" % pair,
			ord >= 0 and ord < states.size() and states[ord] == pair[1],
			"main.gd says %d, sim has %s" % [ord, str(states)])
	for pair in [["TASK_ENGAGE", "Engage"], ["TASK_RADIO", "Radio"]]:
		var ord2: int = consts.get(pair[0], -1)
		_check("%s mirrors GuardTask.%s" % pair,
			ord2 >= 0 and ord2 < tasks.size() and tasks[ord2] == pair[1],
			"main.gd says %d" % ord2)
	_eq("ALARM_NAMES covers alarm levels 0-3", (consts["ALARM_NAMES"] as Array).size(), 4)
	_eq("GUARD_STRIDE matches what GetGuards packs per guard",
		consts["GUARD_STRIDE"], 13)

	# The overlay parses GetAiDebug section by section; walk it the same way.
	# A fresh level leaves the squad, group and node sections EMPTY (the
	# bridge cannot compromise a level), so this pins the layout of the
	# guard and net sections and the section counts; the populated shapes
	# are the sim's, asserted in tests/GuardAI.cs.
	b.Load(FileAccess.get_file_as_string("res://levels/substation_4.txt"), 1)
	var d: PackedInt32Array = b.GetAiDebug()
	var i: int = 0
	var guards: int = d[i]
	i += 1
	for _g in range(guards):
		var nav_n: int = d[i + 11]
		var route_n: int = d[i + 12]
		i += 14 + 2 * nav_n + 2 * route_n
	i += 8
	var squads: int = d[i]
	i += 1
	for _s in range(squads):
		i += 5 + d[i + 4]
	var groups: int = d[i]
	i += 1
	for _gg in range(groups):
		i += 5 + d[i + 4]
	var nodes: int = d[i]
	i += 1 + 5 * nodes
	_eq("the AI debug snapshot parses to its exact length", i, d.size())
	_check("and carries every guard", guards > 0, "%d guards" % guards)


## A hand-built snapshot in GetAiDebug's layout with EVERY section populated:
## two guards (one with nav and route points, one dead), live intel and a
## focus, a gathering squad of three, an exit group of two, three nodes (one
## authored, one claimed). The live one from a fresh level has empty squad,
## group and node sections, so without this those draw paths never run.
static func _synthetic_ai_snapshot() -> PackedInt32Array:
	var d := PackedInt32Array()
	d.append(2)
	d.append_array([2560, 2560, 0, 2, 7, 1000, 1, 5120, 2560, 0, 1, 2, 1, 1,
		3000, 3000, 4000, 3000, 5000, 2600])
	d.append_array([7680, 5120, 16384, 5, 0, 0, 0, 0, 0, -1, -1, 0, 0, 0])
	d.append_array([1, 5120, 2560, 90, 1, 1, 6000, 6000])
	d.append_array([1, 0, 0, 2560, 2560, 3, 0, 1, 0])
	d.append_array([1, 1, 2, 12000, 0, 2, 0, 1])
	d.append_array([3, 2000, 2000, 0, -1, 0, 4000, 4000, 1800, 0, 1, 6000, 1000, 9999, -1, 0])
	return d


## The probe node is built from source here rather than shipped as a file:
## it exists only to give the overlay a real _draw to run in.
func _start_overlay_probe(bridge_script: Script) -> void:
	var b: RefCounted = bridge_script.new()
	b.Load(FileAccess.get_file_as_string("res://levels/substation_4.txt"), 1)
	var src := GDScript.new()
	src.source_code = "extends Node2D\nconst OVERLAY := preload(\"res://game/ai_debug_overlay.gd\")\nvar snaps: Array = []\nvar states := PackedStringArray()\nvar tasks := PackedStringArray()\nvar results: Array = []\nfunc _draw() -> void:\n\tif results.size() >= snaps.size():\n\t\treturn\n\tfor d in snaps:\n\t\tvar got: Dictionary = OVERLAY.draw(self, d, ThemeDB.fallback_font, states, tasks)\n\t\tgot[\"size\"] = d.size()\n\t\tresults.append(got)\n"
	src.reload()
	_probe = Node2D.new()
	_probe.set_script(src)
	_probe.set("snaps", [b.GetAiDebug(), _synthetic_ai_snapshot()])
	_probe.set("states", b.GuardStateNames())
	_probe.set("tasks", b.GuardTaskNames())
	root.add_child(_probe)


func _report_overlay_probe(results: Array) -> void:
	print("  -- AI debug overlay, drawn --")
	_check("the overlay drew inside a real _draw", results.size() == 2,
		"%d of 2 snapshots drawn" % results.size())
	if results.size() < 2:
		return
	var live: Dictionary = results[0]
	_eq("live snapshot: every int consumed", live["consumed"], live["size"])
	var syn: Dictionary = results[1]
	_eq("synthetic snapshot: every int consumed", syn["consumed"], syn["size"])
	_check("and every section was walked",
		syn["guards"] == 2 and syn["squads"] == 1 and syn["groups"] == 1 and syn["nodes"] == 3,
		str(syn))
	_eq("intel age comes back for the header", syn["intel_age"], 90)
	_eq("and the compromised flag", syn["compromised"], true)
