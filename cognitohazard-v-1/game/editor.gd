extends Node2D

## In-editor level authoring (spec §11, milestone 6).
##
## Grid painting, route authoring, text import/export, .txt save/load.
##
## All editing goes through SimBridge into a real sim Level, so the text format
## has exactly one implementation and the editor cannot drift from the parser.
## This script owns input and pixels; it never decides what a glyph means.

const CS: int = 20                     # cell size, px
const FIELD_W: float = 960.0
## The strip below the field holds three rows -- tools, modes, key hints -- so
## the field gave up 20 px of the 620 for it.
const FIELD_H: float = 540.0
const HUD_H: float = 80.0

## Levels are no longer one screen, so the editor has a view of its own: it
## fits whatever it loads, and the wheel and the arrow keys get you closer.
## Without this, everything past column 48 is simply unreachable.
const ED_MIN_ZOOM: float = 0.25
const ED_MAX_ZOOM: float = 2.5
const PAN_STEP: float = 80.0
const RESIZE_STEP: int = 4

const LEVELS := preload("res://game/levels.gd")
const CAT := preload("res://game/item_catalog.gd")
const LEVELS_DIR: String = LEVELS.LEVELS_DIR
const FALLBACK_DIR: String = LEVELS.FALLBACK_DIR
const UNDO_LIMIT: int = 64

# Tool codes must match SimBridge.EditorPaint. The palette ORDER is the code,
# so tools are only ever appended; the key is what the player presses.
# A tool whose glyph SimBridge.EditorStampable accepts is STRUCTURE and goes
# down by brush, shape, fill or paste; spawn, guard and waypoint are ACTORS
# and place one per click.
const TOOLS := [
	{"key": "1", "label": "Wall", "glyph": "#"},
	{"key": "2", "label": "Floor", "glyph": "."},
	{"key": "3", "label": "Spawn", "glyph": "@"},
	{"key": "4", "label": "Exit", "glyph": "X"},
	{"key": "5", "label": "Records", "glyph": "$"},
	{"key": "6", "label": "Guard", "glyph": "a"},
	{"key": "7", "label": "Waypoint", "glyph": ">"},
	{"key": "8", "label": "Chest", "glyph": "C"},
	{"key": "9", "label": "Glass", "glyph": "="},
	{"key": "0", "label": "Door", "glyph": "+"},
	{"key": "O", "label": "Sweep", "glyph": "*"},
]
const T_SPAWN: int = 2
const T_GUARD: int = 5

## Not a palette entry of its own: SHIFT with the chest tool paints an
## objective site instead. Ten number keys are all the palette has, and an
## objective IS a chest -- it loots through the same machinery.
const T_CHEST: int = 7
const T_OBJECTIVE: int = 10

## The sweep-node tool (Guard_AI.md §6.3.1): palette index 10, but EditorPaint
## CODE 11, because code 10 is already the objective. The one palette entry
## whose index is not its code; paint_code maps it. A letter key, since the
## number keys are all spoken for. An ACTOR tool (not EditorStampable): a
## sweep node marks one place, so it goes down one click at a time.
const T_SWEEP: int = 10
const SWEEP_CODE: int = 11

## How a press lays a structure tool down. M cycles them; each is also a
## clickable button on the second toolbar row.
const M_BRUSH: int = 0
const M_LINE: int = 1
const M_RECT: int = 2
const M_BOX: int = 3
const M_FILL: int = 4
const M_SELECT: int = 5
const MODES := [
	{"label": "Brush", "hint": "drag to paint  ·  [ ] brush size"},
	{"label": "Line", "hint": "drag a straight line, brush-thick"},
	{"label": "Rect", "hint": "drag a room outline, walls brush-thick"},
	{"label": "Filled", "hint": "drag a solid block"},
	{"label": "Fill", "hint": "click floods the connected area"},
	{"label": "Select", "hint": "drag to select  ·  ctrl+C/X copy/cut  ·  DEL clear"},
]
const BRUSH_MAX: int = 9

var active: bool = false
var bridge: RefCounted

var _tool: int = 0
var _mode: int = M_BRUSH
var _brush: int = 1
var _hover := Vector2i(-1, -1)
var _hover_raw := Vector2i(-1, -1)     # unclamped: shapes and pastes run off the edge
var _painting: int = 0                 # 0 none, 1 paint, 2 erase
var _stroke_button: int = 0
var _shift_paint: bool = false
## The previous cell of a brush stroke, so a fast flick is joined up rather
## than dotted. Structure is STAMPED, never toggled, so revisiting a cell in
## the same stroke is harmless -- the toggling tools used to flicker on every
## motion event inside one cell.
var _last := Vector2i.ZERO
var _anchor := Vector2i.ZERO           # where a shape or selection drag began
var _drag_end := Vector2i.ZERO
var _undo: Array[String] = []
var _redo: Array[String] = []
var _redo_held: Array[String] = []
var _issues: PackedStringArray = PackedStringArray()
var _notice: String = ""
var _notice_t: float = 0.0
var _renaming: bool = false
var _name_buf: String = ""
var _help: bool = false
var _font: Font

## Selection and clipboard. The clipboard is STRUCTURE only: spawn and guards
## copy as the floor they stand on, because a pasted guard would need a new
## letter and a route, and a pasted spawn would move the only one.
var _sel := Rect2i()
var _clip := PackedByteArray()
var _clip_size := Vector2i.ZERO
var _pasting: bool = false

const T_WAYPOINT: int = 6
## The waypoint a waypoint-tool drag is moving, or -1. Routes are edited by
## index through the bridge, so a point mid-patrol moves without re-clicking
## every point after it.
var _route_drag: int = -1

## Mirror painting: every structure stamp is repeated across the level's
## centre line(s). Actors are never mirrored -- spawn is unique and a guard
## owns a letter.
const MIRRORS := ["off", "X", "Y", "XY"]
var _mirror: int = 0

## The issues panel covers the level's top-right corner, so it folds (I).
## A line naming a cell or a guard is a link: click it and the view goes there.
var _issues_open: bool = true
var _flash := Vector2i(-1, -1)
var _flash_t: float = 0.0

## World point at the centre of the editor view, and the editor's own zoom.
## Independent of main.gd's camera: authoring wants the whole floor, playing
## wants to be close to it.
var _pan: Vector2 = Vector2(FIELD_W * 0.5, FIELD_H * 0.5)
var _zoom: float = 1.0
var _origin: Vector2 = Vector2.ZERO
var _panning: bool = false

## Reported at exit alongside main's counters. An editor that is "active" but
## never draws looks identical to a working one in the log otherwise — which is
## exactly how a scriptless scene slipped through earlier in this project.
var draws: int = 0

signal playtest_requested()

const C_BG := Color(0.04, 0.047, 0.059)
const C_FLOOR := Color(0.071, 0.090, 0.110)
const C_WALL := Color(0.19, 0.21, 0.26)
const C_GRID := Color(1, 1, 1, 0.045)
const C_GRID_MAJOR := Color(1, 1, 1, 0.10)
const C_SPAWN := Color(0.85, 0.87, 0.92)
const C_EXIT := Color(0.25, 0.72, 0.48)
const C_CACHE := Color(0.90, 0.74, 0.30)
const C_GUARD := Color(0.72, 0.56, 0.36)
const C_ROUTE := Color(0.40, 0.76, 0.85, 0.85)
const C_CHEST := Color(0.85, 0.66, 0.36)
const C_OBJECTIVE := Color(0.55, 0.85, 0.95)
const C_GLASS := Color(0.62, 0.84, 0.95)
const C_DOOR := Color(0.36, 0.27, 0.19)
const C_DOOR_EDGE := Color(0.62, 0.48, 0.32)
const C_SEL := Color(1.0, 0.95, 0.55)
const C_ERR := Color(0.95, 0.42, 0.38)
const C_WARN := Color(0.92, 0.70, 0.25)
const C_INFO := Color(0.50, 0.56, 0.64)


func _ready() -> void:
	_font = ThemeDB.fallback_font
	set_process_unhandled_input(true)


func open_editor() -> void:
	active = true
	bridge.EditorBeginFromCurrent()
	_undo.clear()
	_redo.clear()
	_sel = Rect2i()
	_pasting = false
	_painting = 0
	# Frame whatever was loaded. Opening onto a level larger than the field with
	# the view still at 1:1 would show its top-left corner and nothing else.
	fit_view()
	_revalidate()
	queue_redraw()


func close_editor() -> void:
	if _painting != 0:
		_cancel_stroke()
	active = false
	_renaming = false
	_help = false
	_pasting = false
	queue_redraw()


func _process(delta: float) -> void:
	if _notice_t > 0.0:
		_notice_t -= delta
	if _flash_t > 0.0:
		_flash_t -= delta
	if active:
		queue_redraw()


# ------------------------------------------------------------------- input

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_TAB:
			if active: close_editor()
			else: open_editor()
			get_viewport().set_input_as_handled()
			return

	if not active:
		return

	if _renaming:
		_handle_rename(event)
		return

	if event is InputEventKey and event.pressed:
		# Echo only for the keys where holding down means "more": pan, zoom,
		# brush size. A held S must not save thirty times.
		if not event.echo or _repeats(event.keycode):
			_handle_key(event)
		get_viewport().set_input_as_handled()
	elif event is InputEventMouseButton:
		_handle_mouse_button(event)
	elif event is InputEventMouseMotion:
		if _panning:
			_pan -= event.relative / maxf(0.001, _zoom)
			_clamp_pan()
			return
		var m: Vector2 = get_local_mouse_position()
		_hover = _cell_at(m)
		_hover_raw = _raw_cell(m)
		drag_to(_hover_raw)


func _repeats(keycode: int) -> bool:
	return keycode in [KEY_LEFT, KEY_RIGHT, KEY_UP, KEY_DOWN, KEY_MINUS, KEY_EQUAL,
		KEY_BRACKETLEFT, KEY_BRACKETRIGHT]


func _handle_rename(event: InputEvent) -> void:
	if not (event is InputEventKey and event.pressed):
		return
	if event.keycode == KEY_ENTER or event.keycode == KEY_KP_ENTER:
		bridge.EditorName = _name_buf
		_renaming = false
	elif event.keycode == KEY_ESCAPE:
		_renaming = false
	elif event.keycode == KEY_BACKSPACE:
		_name_buf = _name_buf.substr(0, maxi(0, _name_buf.length() - 1))
	elif event.unicode >= 32:
		_name_buf += char(event.unicode)
	get_viewport().set_input_as_handled()


func _handle_key(event: InputEventKey) -> void:
	var cmd: bool = event.ctrl_pressed or event.meta_pressed
	match event.keycode:
		KEY_1, KEY_2, KEY_3, KEY_4, KEY_5, KEY_6, KEY_7, KEY_8, KEY_9:
			set_tool(event.keycode - KEY_1)
		KEY_0:
			set_tool(9)
		KEY_O:
			set_tool(T_SWEEP)
		KEY_M:
			set_mode(posmod(_mode + (-1 if event.shift_pressed else 1), MODES.size()))
		KEY_G:
			cycle_guard(-1 if event.shift_pressed else 1)
		KEY_COMMA, KEY_PERIOD:
			# The selected guard's loot points: what he buys his kit with.
			var step: int = (1000 if event.shift_pressed else 100) \
				* (1 if event.keycode == KEY_PERIOD else -1)
			adjust_guard_points(bridge.EditorSelectedGuard, step)
		KEY_SEMICOLON, KEY_APOSTROPHE:
			# The floor's chest budget, spread across every supply chest.
			var step2: int = (1000 if event.shift_pressed else 100) \
				* (1 if event.keycode == KEY_APOSTROPHE else -1)
			adjust_chest_budget(step2)
		KEY_K:
			set_mirror(_mirror + (-1 if event.shift_pressed else 1))
		KEY_I:
			_issues_open = not _issues_open
		KEY_BRACKETLEFT:
			set_brush(_brush - 1)
		KEY_BRACKETRIGHT:
			set_brush(_brush + 1)
		KEY_S:
			_save()
		KEY_L:
			_load_next()
		KEY_N:
			_push_undo()
			bridge.EditorBeginBlank()
			_sel = Rect2i()
			fit_view()
			_revalidate()
		KEY_Z:
			if cmd:
				if event.shift_pressed: _redo_step()
				else: _undo_step()
		KEY_Y:
			if cmd:
				_redo_step()
		KEY_A:
			if cmd:
				_sel = Rect2i(0, 0, cols(), rows())
				_notify("selected all  ·  %dx%d" % [cols(), rows()])
		KEY_C:
			if cmd:
				copy_selection()
			else:
				var sel: int = bridge.EditorSelectedGuard
				if sel != 0:
					_push_undo()
					bridge.EditorRouteClear(sel)
					_revalidate()
		KEY_X:
			if cmd:
				cut_selection()
		KEY_V:
			if cmd:
				begin_paste()
		KEY_R:
			if _pasting:
				rotate_clip()
		KEY_H:
			if _pasting:
				flip_clip()
		KEY_DELETE:
			delete_selection()
		KEY_ESCAPE:
			_escape()
		KEY_F1:
			_help = not _help
		KEY_BACKSPACE:
			var sel2: int = bridge.EditorSelectedGuard
			if sel2 != 0:
				_push_undo()
				bridge.EditorRouteUndoPoint(sel2)
				_revalidate()
		KEY_F2:
			_renaming = true
			_name_buf = bridge.EditorName
		KEY_F:
			fit_view()
		KEY_LEFT, KEY_RIGHT, KEY_UP, KEY_DOWN:
			# Plain arrows pan the view; with ctrl/cmd they resize the LEVEL,
			# which is the only way to author anything bigger than one screen.
			if cmd:
				_resize_by(event.keycode)
			else:
				_pan_by(event.keycode)
		KEY_MINUS:
			_zoom_by(1.0 / 1.25)
		KEY_EQUAL:
			_zoom_by(1.25)
		KEY_ENTER, KEY_KP_ENTER:
			playtest_requested.emit()


## ESC backs out of one thing at a time, innermost first, so it is always safe
## to press: a shape half-dragged, then a paste, then the selection, then help.
func _escape() -> void:
	if _painting != 0:
		_cancel_stroke()
		_notify("cancelled")
	elif _pasting:
		_pasting = false
	elif _sel.has_area():
		_sel = Rect2i()
	elif _help:
		_help = false


func set_tool(i: int) -> void:
	if i >= 0 and i < TOOLS.size():
		_tool = i


func set_mode(i: int) -> void:
	if i < 0 or i >= MODES.size():
		return
	if _painting != 0:
		_cancel_stroke()
	_pasting = false
	_mode = i


func set_brush(n: int) -> void:
	_brush = clampi(n, 1, BRUSH_MAX)
	_notify("brush %dx%d" % [_brush, _brush])


func _pan_by(keycode: int) -> void:
	var step: float = PAN_STEP / maxf(0.001, _zoom)
	match keycode:
		KEY_LEFT: _pan.x -= step
		KEY_RIGHT: _pan.x += step
		KEY_UP: _pan.y -= step
		KEY_DOWN: _pan.y += step
	_clamp_pan()


func _zoom_by(factor: float) -> void:
	_zoom = clampf(_zoom * factor, ED_MIN_ZOOM, ED_MAX_ZOOM)
	_clamp_pan()


## Keep at least some of the level on screen. Not a hard clamp to the bounds --
## authoring wants a little margin past the edge to work against.
func _clamp_pan() -> void:
	var lvl: Vector2 = level_px()
	var margin: float = 120.0
	_pan.x = clampf(_pan.x, -margin, lvl.x + margin)
	_pan.y = clampf(_pan.y, -margin, lvl.y + margin)
	_recompute()


func _resize_by(keycode: int) -> void:
	var c: int = cols()
	var r: int = rows()
	match keycode:
		KEY_RIGHT: c += RESIZE_STEP
		KEY_LEFT: c -= RESIZE_STEP
		KEY_DOWN: r += RESIZE_STEP
		KEY_UP: r -= RESIZE_STEP
	resize_to(c, r)


## Resize the level, reporting anything the shrink dropped rather than eating
## it silently. Undoable like any other edit, which is what makes it safe to
## allow a destructive shrink at all.
func resize_to(c: int, r: int) -> void:
	if c == cols() and r == rows():
		return
	_push_undo()
	var dropped: int = bridge.EditorResize(c, r)
	fit_view()
	_revalidate()
	if dropped > 0:
		_notice = "%dx%d  ·  %d cell(s) dropped  ·  ctrl+Z to undo" % [cols(), rows(), dropped]
	else:
		_notice = "%dx%d" % [cols(), rows()]
	_notice_t = 3.0


func _handle_mouse_button(event: InputEventMouseButton) -> void:
	# get_local_mouse_position, not event.position: under canvas_items stretch
	# the raw event is in window pixels, so painting would land on the wrong
	# cell the moment the window is resized or goes fullscreen.
	var m: Vector2 = get_local_mouse_position()
	if event.button_index == MOUSE_BUTTON_MIDDLE:
		_panning = event.pressed
		return
	if event.button_index == MOUSE_BUTTON_WHEEL_UP and event.pressed:
		_zoom_by(1.12)
		return
	if event.button_index == MOUSE_BUTTON_WHEEL_DOWN and event.pressed:
		_zoom_by(1.0 / 1.12)
		return
	if event.button_index != MOUSE_BUTTON_LEFT and event.button_index != MOUSE_BUTTON_RIGHT:
		return

	if not event.pressed:
		if event.button_index == _stroke_button:
			release()
		return

	if m.y >= FIELD_H:
		if event.button_index == MOUSE_BUTTON_LEFT:
			_click_toolbar(m)
		return
	# One stroke at a time: the other button mid-drag is ignored rather than
	# starting a second stroke inside the first one's undo step.
	if _painting != 0:
		return

	# The issues panel's header folds it and a line that names a place is a
	# link. Anything else on the panel paints through, as it always did -- the
	# panel sits over the level, and most of its lines are not links.
	if event.button_index == MOUSE_BUTTON_LEFT and not _pasting:
		var hit: int = issue_at(m)
		if hit == -1:
			_issues_open = not _issues_open
			return
		if hit >= 0:
			goto_issue(_issues[hit])
			return

	var erase: bool = event.button_index == MOUSE_BUTTON_RIGHT
	if event.alt_pressed and not erase:
		pick(_cell_at(m))
		return
	_stroke_button = event.button_index
	press(_raw_cell(m), erase, event.shift_pressed)


func _click_toolbar(m: Vector2) -> void:
	for i in range(TOOLS.size()):
		if _tool_rect(i).has_point(m):
			set_tool(i)
			return
	for i in range(MODES.size()):
		if _mode_rect(i).has_point(m):
			set_mode(i)
			return
	var b: Rect2 = _brush_rect()
	if b.has_point(m):
		set_brush(_brush + (-1 if m.x < b.get_center().x else 1))
	elif _mirror_rect().has_point(m):
		set_mirror(_mirror + 1)


# --------------------------------------------------------------- strokes
#
# press/drag_to/release take a CELL; the mouse handlers read the cursor once
# and hand it in -- the stash screen's bargain, so the harness can drive every
# tool without a cursor to read.

func press(cell: Vector2i, erase: bool, shift: bool = false) -> void:
	if _pasting:
		if erase:
			_pasting = false
		else:
			paste_at(cell)
		return

	if _mode == M_SELECT:
		if erase:
			_sel = Rect2i()
			return
		_painting = 1
		_anchor = cell
		_drag_end = cell
		_sel = _clip_rect(_rect_between(cell, cell))
		return

	_shift_paint = shift
	if _tool == T_WAYPOINT and _press_route(cell, erase):
		return
	# The actor tools place one thing per press whatever the mode: a dragged
	# waypoint tool used to drop a waypoint on every cell it crossed.
	if not erase and not _stampable():
		_begin_edit()
		_paint(cell, false)
		_end_edit()
		return

	_painting = 2 if erase else 1
	_begin_edit()
	match _mode:
		M_BRUSH:
			_last = cell
			_stamp_many(_footprint(cell), erase)
		M_FILL:
			if _in_bounds(cell):
				for p in mirrored(cell):
					bridge.EditorFlood(p.x, p.y, _glyph(erase))
			_painting = 0
			_stroke_button = 0
			_end_edit()
		_:
			_anchor = cell
			_drag_end = cell


func drag_to(cell: Vector2i) -> void:
	if _painting == 0:
		return
	if _route_drag >= 0:
		var c := Vector2i(clampi(cell.x, 0, cols() - 1), clampi(cell.y, 0, rows() - 1))
		bridge.EditorRouteMovePoint(bridge.EditorSelectedGuard, _route_drag, c.x, c.y)
		return
	match _mode:
		M_SELECT:
			_drag_end = cell
			_sel = _clip_rect(_rect_between(_anchor, cell))
		M_BRUSH:
			var cells: Array[Vector2i] = []
			for p in line_cells(_last, cell):
				cells.append_array(_footprint(p))
			_stamp_many(cells, _painting == 2)
			_last = cell
		_:
			_drag_end = cell


func release() -> void:
	if _painting == 0:
		return
	var erase: bool = _painting == 2
	_painting = 0
	_stroke_button = 0
	if _route_drag >= 0:
		_route_drag = -1
		_end_edit()
		return
	if _mode == M_SELECT:
		return
	if _mode != M_BRUSH:
		_stamp_many(shape_cells(_mode, _anchor, _drag_end), erase)
	_end_edit()


## Drop a half-dragged shape or brush stroke. A brush has already painted, so
## the stroke is rolled back to its own undo snapshot.
func _cancel_stroke() -> void:
	var was: int = _painting
	var dragging_route: bool = _route_drag >= 0
	_painting = 0
	_stroke_button = 0
	_route_drag = -1
	if was == 0 or (_mode == M_SELECT and not dragging_route):
		return
	if (dragging_route or _mode == M_BRUSH) and not _undo.is_empty():
		_restore(_undo.back())
	_end_edit()


# ------------------------------------------------------------------ routes

## The waypoint tool, when a guard is selected: press ON one of his waypoints
## to drag it, press on his route LINE to insert a point there and drag that,
## press anywhere else to append one and drag it into place. RMB on a waypoint
## deletes just that point. Returns false to fall through to the plain tool
## (RMB off the route erases, as every tool's RMB does).
func _press_route(cell: Vector2i, erase: bool) -> bool:
	var id: int = bridge.EditorSelectedGuard
	if id == 0:
		if not erase:
			_notify("no guard selected  ·  6 then click a guard, or G to cycle")
			return true
		return false
	var idx: int = waypoint_at(cell)
	if erase:
		if idx < 0:
			return false
		_begin_edit()
		bridge.EditorRouteRemovePoint(id, idx)
		_end_edit()
		return true
	if not _in_bounds(cell):
		return true
	_begin_edit()
	if idx < 0:
		idx = segment_at(cell)
		if idx >= 0:
			bridge.EditorRouteInsertPoint(id, idx, cell.x, cell.y)
		else:
			# Through EditorPaint, which gives a sentry a route to append to.
			_paint(cell, false)
			idx = bridge.EditorGetRoute(id).size() / 2 - 1
	_route_drag = idx
	_painting = 1
	return true


## Index of the selected guard's waypoint on `cell`, or -1. The LAST one
## wins, so of two stacked points the one drawn on top is the one you grab.
func waypoint_at(cell: Vector2i) -> int:
	var route: PackedInt32Array = bridge.EditorGetRoute(bridge.EditorSelectedGuard)
	var found: int = -1
	for i in range(route.size() / 2):
		if route[i * 2] == cell.x and route[i * 2 + 1] == cell.y:
			found = i
	return found


## Where a point pressed on the selected guard's route LINE is inserted: the
## index after the segment's start, or -1 if `cell` is not on one. The route
## is a closed loop (spec §2.1), so the closing segment counts, and inserting
## on it appends.
func segment_at(cell: Vector2i) -> int:
	var route: PackedInt32Array = bridge.EditorGetRoute(bridge.EditorSelectedGuard)
	var n: int = route.size() / 2
	if n < 2:
		return -1
	var p := Vector2(cell)
	for i in range(n):
		var j: int = (i + 1) % n
		var a := Vector2(route[i * 2], route[i * 2 + 1])
		var b := Vector2(route[j * 2], route[j * 2 + 1])
		if a == b:
			continue
		var t: float = clampf((p - a).dot(b - a) / (b - a).length_squared(), 0.0, 1.0)
		if p.distance_to(a + (b - a) * t) < 0.5:
			return i + 1
	return -1


func _guard_cell(id: int) -> Vector2i:
	var grid: PackedByteArray = bridge.EditorGetGrid()
	var i: int = grid.find(id)
	return Vector2i(-1, -1) if i < 0 else Vector2i(i % cols(), i / cols())


## G / shift+G: select the next or previous guard and bring him into view.
## On a 144-column level, finding guard q by eye is most of the work.
func cycle_guard(step: int) -> void:
	var ids: PackedByteArray = bridge.EditorGuardIds()
	if ids.is_empty():
		_notify("no guards placed")
		return
	var at: int = ids.find(bridge.EditorSelectedGuard)
	var next: int = ids[posmod(at + step, ids.size())] if at >= 0 \
		else ids[0 if step > 0 else ids.size() - 1]
	bridge.EditorSelectGuard(next)
	var n: int = bridge.EditorGetRoute(next).size() / 2
	_notify("guard %s  ·  %s" % [char(next),
		"sentry" if n <= 1 else "%d waypoints" % n])
	focus_cell(_guard_cell(next))


## Centre the view on a cell and flash it, keeping the zoom.
func focus_cell(cell: Vector2i) -> void:
	if not _in_bounds(cell):
		return
	_pan = Vector2(cell) * CS + Vector2(CS, CS) * 0.5
	_clamp_pan()
	_flash = cell
	_flash_t = 1.2


## Follow an issue line: a guard it names is selected, a cell it names is
## shown. Returns false for a line that names neither.
func goto_issue(text: String) -> bool:
	var went: bool = false
	var g := RegEx.create_from_string("guard ([a-z])\\b")
	var gm: RegExMatch = g.search(text)
	if gm != null:
		var id: int = gm.get_string(1).unicode_at(0)
		if bridge.EditorGuardIds().has(id):
			bridge.EditorSelectGuard(id)
			focus_cell(_guard_cell(id))
			went = true
	var c := RegEx.create_from_string("\\((\\d+),(\\d+)\\)")
	var cm: RegExMatch = c.search(text)
	if cm != null:
		focus_cell(Vector2i(cm.get_string(1).to_int(), cm.get_string(2).to_int()))
		went = true
	return went


# ------------------------------------------------------------------ mirror

func set_mirror(m: int) -> void:
	_mirror = posmod(m, MIRRORS.size())
	_notify("mirror %s" % MIRRORS[_mirror])


## A cell and its mirror images under the current mirror, without repeats.
func mirrored(p: Vector2i) -> Array[Vector2i]:
	var out: Array[Vector2i] = [p]
	var mx := Vector2i(cols() - 1 - p.x, p.y)
	var my := Vector2i(p.x, rows() - 1 - p.y)
	var mxy := Vector2i(mx.x, my.y)
	if _mirror & 1:
		out.append(mx)
	if _mirror & 2:
		out.append(my)
	if _mirror == 3:
		out.append(mxy)
	var uniq: Array[Vector2i] = []
	for q in out:
		if not uniq.has(q):
			uniq.append(q)
	return uniq


## The code EditorPaint is sent for a palette tool: the tool's own index,
## except shift+chest, which is the objective. Static so the harness can ask.
static func paint_code(tool: int, shift: bool) -> int:
	if tool == T_SWEEP:
		return SWEEP_CODE
	return T_OBJECTIVE if tool == T_CHEST and shift else tool


## The glyph this press stamps: floor for the eraser, '!' for shift+chest,
## otherwise the tool's own.
func _glyph(erase: bool) -> int:
	if erase:
		return 46
	if paint_code(_tool, _shift_paint) == T_OBJECTIVE:
		return 33
	return String(TOOLS[_tool]["glyph"]).unicode_at(0)


func _stampable() -> bool:
	return bridge.EditorStampable(String(TOOLS[_tool]["glyph"]).unicode_at(0))


## Erasing clears actors too (EditorPaint's eraser takes a guard's route with
## him); painting STAMPS, so a brush dragged back over its own stroke never
## toggles an exit off again.
func _stamp_many(cells: Array[Vector2i], erase: bool, mirror: bool = true) -> void:
	var g: int = _glyph(erase)
	for p0 in cells:
		var targets: Array[Vector2i] = [p0]
		if mirror:
			targets = mirrored(p0)
		for p in targets:
			if not _in_bounds(p):
				continue
			if erase:
				if bridge.EditorGetCell(p.x, p.y) != 46:
					bridge.EditorPaint(p.x, p.y, 0, true)
			else:
				bridge.EditorStamp(p.x, p.y, g)


func _footprint(center: Vector2i) -> Array[Vector2i]:
	var out: Array[Vector2i] = []
	var off: int = (_brush - 1) / 2
	for y in range(_brush):
		for x in range(_brush):
			out.append(center + Vector2i(x - off, y - off))
	return out


## Bresenham, both ends included.
static func line_cells(a: Vector2i, b: Vector2i) -> Array[Vector2i]:
	var out: Array[Vector2i] = []
	var dx: int = absi(b.x - a.x)
	var dy: int = -absi(b.y - a.y)
	var sx: int = 1 if a.x < b.x else -1
	var sy: int = 1 if a.y < b.y else -1
	var err: int = dx + dy
	var p: Vector2i = a
	while true:
		out.append(p)
		if p == b:
			break
		var e2: int = 2 * err
		if e2 >= dy:
			err += dy
			p.x += sx
		if e2 <= dx:
			err += dx
			p.y += sy
	return out


## The cells a shape covers, unclipped and without duplicates. A line is the
## brush swept along it; a rect's walls are brush-thick and grow INWARD, so
## the corners you dragged to are the room's outer corners whatever the size.
func shape_cells(mode: int, a: Vector2i, b: Vector2i) -> Array[Vector2i]:
	var out: Array[Vector2i] = []
	match mode:
		M_LINE:
			var seen: Dictionary = {}
			for p in line_cells(a, b):
				for q in _footprint(p):
					if not seen.has(q):
						seen[q] = true
						out.append(q)
		M_RECT, M_BOX:
			var r: Rect2i = _rect_between(a, b)
			var t: int = _brush
			for y in range(r.position.y, r.end.y):
				for x in range(r.position.x, r.end.x):
					var edge: bool = x - r.position.x < t or r.end.x - 1 - x < t \
						or y - r.position.y < t or r.end.y - 1 - y < t
					if mode == M_BOX or edge:
						out.append(Vector2i(x, y))
	return out


## Eyedropper: alt+click takes the tool that made the cell under it. On a guard
## it also selects him, so alt+click then 7 edits an existing route.
func pick(cell: Vector2i) -> void:
	if not _in_bounds(cell):
		return
	var g: int = bridge.EditorGetCell(cell.x, cell.y)
	var note: String = ""
	if g >= 97 and g <= 122:
		_tool = T_GUARD
		bridge.EditorSelectGuard(g)
		note = "  ·  guard %s selected" % char(g)
	elif g == 33:
		_tool = T_CHEST
		note = "  ·  hold shift for the objective"
	else:
		for i in range(TOOLS.size()):
			if String(TOOLS[i]["glyph"]) == char(g):
				_tool = i
				break
	_notify("picked %s%s" % [TOOLS[_tool]["label"], note])


# -------------------------------------------------------- select and paste

func copy_selection() -> bool:
	var r: Rect2i = _clip_rect(_sel)
	if not r.has_area():
		_notify("nothing selected  ·  M to Select mode, then drag")
		return false
	var grid: PackedByteArray = bridge.EditorGetGrid()
	var nc: int = cols()
	_clip = PackedByteArray()
	_clip.resize(r.size.x * r.size.y)
	for y in range(r.size.y):
		for x in range(r.size.x):
			var g: int = grid[(r.position.y + y) * nc + r.position.x + x]
			_clip[y * r.size.x + x] = g if bridge.EditorStampable(g) else 46
	_clip_size = r.size
	_notify("copied %dx%d  ·  ctrl+V to paste" % [r.size.x, r.size.y])
	return true


## Cut takes the STRUCTURE and leaves actors standing on floor, matching what
## the clipboard holds: a cut that deleted guards the paste cannot put back
## would simply lose them.
func cut_selection() -> void:
	if not copy_selection():
		return
	var r: Rect2i = _clip_rect(_sel)
	_begin_edit()
	for y in range(r.position.y, r.end.y):
		for x in range(r.position.x, r.end.x):
			if bridge.EditorStampable(bridge.EditorGetCell(x, y)):
				bridge.EditorStamp(x, y, 46)
	_end_edit()
	_notify("cut %dx%d  ·  ctrl+V to paste" % [r.size.x, r.size.y])


## DEL empties the selection outright, actors included -- the eraser, in bulk.
func delete_selection() -> void:
	var r: Rect2i = _clip_rect(_sel)
	if not r.has_area():
		return
	var cells: Array[Vector2i] = []
	for y in range(r.position.y, r.end.y):
		for x in range(r.position.x, r.end.x):
			cells.append(Vector2i(x, y))
	_begin_edit()
	_stamp_many(cells, true, false)
	_end_edit()


func begin_paste() -> void:
	if _clip.is_empty():
		_notify("clipboard is empty  ·  select, then ctrl+C")
		return
	if _painting != 0:
		_cancel_stroke()
	_pasting = true


## Stamp the clipboard with its top-left at `origin`. Floor in the clipboard
## does not bury a spawn or a guard: it is the floor they were standing on.
## Paste mode stays up, so a room can be stamped down the length of a corridor.
func paste_at(origin: Vector2i) -> void:
	_begin_edit()
	for y in range(_clip_size.y):
		for x in range(_clip_size.x):
			var g: int = _clip[y * _clip_size.x + x]
			# Mirrored point by point, which lays the far copy down flipped --
			# the whole point of painting symmetrically.
			for p in mirrored(origin + Vector2i(x, y)):
				if not _in_bounds(p):
					continue
				if g == 46 and not bridge.EditorStampable(bridge.EditorGetCell(p.x, p.y)):
					continue
				bridge.EditorStamp(p.x, p.y, g)
	_end_edit()


## Quarter turn clockwise.
func rotate_clip() -> void:
	var w: int = _clip_size.x
	var h: int = _clip_size.y
	var out := PackedByteArray()
	out.resize(w * h)
	for y in range(h):
		for x in range(w):
			out[x * h + (h - 1 - y)] = _clip[y * w + x]
	_clip = out
	_clip_size = Vector2i(h, w)


## Mirror left to right. Rotate twice and flip for top to bottom.
func flip_clip() -> void:
	var w: int = _clip_size.x
	var out := PackedByteArray()
	out.resize(_clip.size())
	for y in range(_clip_size.y):
		for x in range(w):
			out[y * w + (w - 1 - x)] = _clip[y * w + x]
	_clip = out


# ------------------------------------------------------------- the view

func cols() -> int:
	return bridge.EditorCols


func rows() -> int:
	return bridge.EditorRows


func level_px() -> Vector2:
	return Vector2(cols() * CS, rows() * CS)


## Fit the whole level in the field, never magnifying past 1:1.
func fit_zoom() -> float:
	var lvl: Vector2 = level_px()
	var fit: float = minf(FIELD_W / maxf(1.0, lvl.x), FIELD_H / maxf(1.0, lvl.y))
	return clampf(fit, ED_MIN_ZOOM, 1.0)


func fit_view() -> void:
	_zoom = fit_zoom()
	_pan = level_px() * 0.5
	_recompute()


func _recompute() -> void:
	_origin = Vector2(FIELD_W, FIELD_H) * 0.5 - _pan * _zoom


func _to_screen(w: Vector2) -> Vector2:
	return _origin + w * _zoom


func _to_world(s: Vector2) -> Vector2:
	return (s - _origin) / maxf(0.001, _zoom)


func _cell_at(pos: Vector2) -> Vector2i:
	# The palette and status strip live below the field; a click down there is
	# not a cell however the view is panned.
	if pos.y >= FIELD_H:
		return Vector2i(-1, -1)
	var w: Vector2 = _to_world(pos)
	if w.x < 0.0 or w.y < 0.0:
		return Vector2i(-1, -1)
	var cell := Vector2i(int(w.x) / CS, int(w.y) / CS)
	if cell.x >= cols() or cell.y >= rows():
		return Vector2i(-1, -1)
	return cell


## The cell under a point with no bounds check, so a rectangle can be dragged
## from outside the level and still land its near corner on the edge.
func _raw_cell(pos: Vector2) -> Vector2i:
	var w: Vector2 = _to_world(pos)
	return Vector2i(floori(w.x / CS), floori(w.y / CS))


func _in_bounds(cell: Vector2i) -> bool:
	return cell.x >= 0 and cell.y >= 0 and cell.x < cols() and cell.y < rows()


func _rect_between(a: Vector2i, b: Vector2i) -> Rect2i:
	var p0 := Vector2i(mini(a.x, b.x), mini(a.y, b.y))
	var p1 := Vector2i(maxi(a.x, b.x), maxi(a.y, b.y))
	return Rect2i(p0, p1 - p0 + Vector2i.ONE)


func _clip_rect(r: Rect2i) -> Rect2i:
	return r.intersection(Rect2i(0, 0, cols(), rows()))


func _paint(cell: Vector2i, erase: bool) -> void:
	if not _in_bounds(cell):
		return
	bridge.EditorPaint(cell.x, cell.y, paint_code(_tool, _shift_paint), erase)


# -------------------------------------------------------------------- undo

## Snapshots are whole level texts. That is wasteful per keystroke and exactly
## right here: it round-trips through the same parser the game uses, so an undo
## can never restore a state the format cannot express.
## LOOT ASSIGNMENT. Points are dollars in the shop's prices: a guard spends his
## on a gun, maybe a vest, then apparel and attachments (sim/LootTable). Each
## change is one undo step, since both live in the level text.
func adjust_guard_points(guard: int, step: int) -> void:
	if guard == 0:
		_notify("select a guard (click one, or G) to assign his loot points")
		return
	_push_undo()
	bridge.EditorSetGuardPoints(guard, maxi(0, bridge.EditorGuardPoints(guard) + step))
	_notify("guard %s carries %s  ·  all guards %s" % [char(guard),
		_money(bridge.EditorGuardPoints(guard)), _money(bridge.EditorGuardLootTotal)])


func adjust_chest_budget(step: int) -> void:
	_push_undo()
	bridge.EditorSetChestBudget(maxi(0, bridge.EditorChestBudget + step))
	_notify("the chests hold %s between them" % _money(bridge.EditorChestBudget))


static func _money(n: int) -> String:
	return CAT.money(n)


func _push_undo() -> void:
	_undo.append(bridge.EditorToText())
	while _undo.size() > UNDO_LIMIT:
		_undo.pop_front()
	_redo.clear()


## An edit that may turn out to change nothing -- a stroke over cells that
## already held the glyph, a cancelled shape. _end_edit drops its snapshot if
## so, and gives back the redo history it would otherwise have cost.
func _begin_edit() -> void:
	_redo_held = _redo.duplicate()
	_push_undo()


func _end_edit() -> void:
	if not _undo.is_empty() and _undo.back() == bridge.EditorToText():
		_undo.pop_back()
		_redo = _redo_held.duplicate()
	_redo_held.clear()
	_revalidate()


func _undo_step() -> void:
	if _undo.is_empty():
		_notify("nothing to undo")
		return
	_redo.append(bridge.EditorToText())
	_restore(_undo.pop_back())
	_notify("undo")


func _redo_step() -> void:
	if _redo.is_empty():
		_notify("nothing to redo")
		return
	_undo.append(bridge.EditorToText())
	_restore(_redo.pop_back())
	_notify("redo")


## Put a snapshot back without throwing away the view or the selected guard.
## Re-fitting on every ctrl+Z bounced you out of whatever you were zoomed in
## on; only a snapshot of a different SIZE (resize is undoable) needs it.
func _restore(text: String) -> void:
	var c0: int = cols()
	var r0: int = rows()
	var sel: int = bridge.EditorSelectedGuard
	bridge.EditorBeginFrom(text)
	if sel != 0 and bridge.EditorGuardIds().has(sel):
		bridge.EditorSelectGuard(sel)
	if cols() != c0 or rows() != r0:
		fit_view()
	_revalidate()


func _revalidate() -> void:
	_issues = bridge.EditorValidate()


func _notify(text: String) -> void:
	_notice = text
	_notice_t = 3.0


# ------------------------------------------------------------------ files

func _slug(name: String) -> String:
	var out: String = ""
	for ch in name.to_lower():
		if (ch >= "a" and ch <= "z") or (ch >= "0" and ch <= "9"):
			out += ch
		elif out.length() > 0 and not out.ends_with("_"):
			out += "_"
	out = out.trim_suffix("_")
	return "untitled" if out.is_empty() else out


func _save() -> void:
	var text: String = bridge.EditorToText()
	var file_name: String = _slug(bridge.EditorName) + ".txt"

	# levels/ is the version-controlled home for level text (spec §2.1). That
	# path is writable when running from the project directory but not from an
	# exported build, so fall back rather than silently losing the work.
	var path: String = LEVELS_DIR + "/" + file_name
	var f := FileAccess.open(path, FileAccess.WRITE)
	if f == null:
		DirAccess.make_dir_recursive_absolute(FALLBACK_DIR)
		path = FALLBACK_DIR + "/" + file_name
		f = FileAccess.open(path, FileAccess.WRITE)
	if f == null:
		_notify("could not write %s" % file_name)
		push_error("editor: could not write %s" % path)
		return

	f.store_string(text)
	f.close()
	_notify("saved %s" % ProjectSettings.globalize_path(path))
	print("editor: saved %s" % ProjectSettings.globalize_path(path))


## Shared with the start screen's mission select, so a level one of them can
## see and the other cannot is impossible by construction.
func _level_files() -> PackedStringArray:
	return LEVELS.list()


func _load_next() -> void:
	var files: PackedStringArray = _level_files()
	if files.is_empty():
		_notify("no level files found")
		return
	var want: String = _slug(bridge.EditorName) + ".txt"
	var idx: int = -1
	for i in range(files.size()):
		if files[i].ends_with(want):
			idx = i
			break
	var next: String = files[(idx + 1) % files.size()]
	var text: String = FileAccess.get_file_as_string(next)
	if text.is_empty():
		_notify("could not read %s" % next)
		return
	_push_undo()
	bridge.EditorBeginFrom(text)
	_sel = Rect2i()
	fit_view()
	_revalidate()
	_notify("loaded %s" % next)


# ----------------------------------------------------------------- drawing

func _draw() -> void:
	if not active:
		return
	draws += 1

	draw_rect(Rect2(0, 0, FIELD_W, FIELD_H + HUD_H), C_BG)

	# The level pass is transformed; the palette and status strip below are not.
	draw_set_transform(_origin, 0.0, Vector2(_zoom, _zoom))
	draw_rect(Rect2(Vector2.ZERO, level_px()), C_FLOOR)

	_draw_cells()
	_draw_grid_lines()
	_draw_routes()
	_draw_selection()
	_draw_guides()
	_draw_preview()

	draw_set_transform(Vector2.ZERO, 0.0, Vector2.ONE)
	# The field ends at FIELD_H; a panned level must not bleed over the palette.
	draw_rect(Rect2(0, FIELD_H, FIELD_W, HUD_H), C_BG)
	_draw_palette()
	_draw_status()
	_draw_issues()
	_draw_help()


func _draw_cells() -> void:
	var grid: PackedByteArray = bridge.EditorGetGrid()
	var nc: int = cols()
	var nr: int = rows()
	for r in range(nr):
		for c in range(nc):
			var ch: int = grid[r * nc + c]
			if ch == 46:      # '.'
				continue
			_draw_glyph(ch, c * CS, r * CS, 1.0)


## One cell's glyph. `a` below 1 is the ghost a paste previews with.
func _draw_glyph(ch: int, x: float, y: float, a: float) -> void:
	var cell := Rect2(x, y, CS, CS)
	match ch:
		35:           # '#'
			draw_rect(cell, Color(C_WALL, a))
		46:           # '.' -- only ever drawn as a paste ghost
			draw_rect(cell, Color(C_FLOOR.lightened(0.12), a))
		64:           # '@'
			draw_rect(cell, Color(C_SPAWN, 0.22 * a))
			draw_circle(Vector2(x + CS * 0.5, y + CS * 0.5), 5.0, Color(C_SPAWN, a))
		88:           # 'X'
			draw_rect(cell, Color(C_EXIT, 0.25 * a))
			draw_rect(cell.grow(-3.0), Color(C_EXIT, a), false, 1.5)
		36:           # '$'
			draw_rect(cell, Color(C_CACHE, 0.18 * a))
			draw_rect(Rect2(x + 5, y + 5, CS - 10, CS - 10), Color(C_CACHE, a), false, 1.5)
		67:           # 'C'
			draw_rect(cell, Color(C_CHEST, 0.20 * a))
			draw_rect(Rect2(x + 3, y + 5, CS - 6, CS - 9), Color(C_CHEST, a), false, 1.5)
			draw_line(Vector2(x + 3, y + 9), Vector2(x + CS - 3, y + 9), Color(C_CHEST, a), 1.0)
		33:           # '!'
			draw_rect(cell, Color(C_OBJECTIVE, 0.22 * a))
			draw_rect(cell.grow(-3.0), Color(C_OBJECTIVE, a), false, 1.5)
			draw_string(_font, Vector2(x + 7, y + 15), "!",
				HORIZONTAL_ALIGNMENT_LEFT, -1, 12, Color(C_OBJECTIVE, a))
		61:           # '='
			draw_rect(cell, Color(C_GLASS, 0.22 * a))
			draw_rect(cell, Color(C_GLASS, a), false, 1.0)
			draw_line(Vector2(x + 5, y + CS - 5), Vector2(x + CS - 5, y + 5),
				Color(1, 1, 1, 0.45 * a), 1.0)
		43:           # '+'
			draw_rect(cell, Color(C_DOOR, a))
			draw_rect(cell.grow(-2.0), Color(C_DOOR_EDGE, a), false, 1.0)
		42:           # '*' -- a sweep node: a hollow ring, an eye on the floor
			var ctr := Vector2(x + CS * 0.5, y + CS * 0.5)
			draw_arc(ctr, 6.5, 0.0, TAU, 20, Color(C_SEL, a), 1.5)
			draw_circle(ctr, 1.6, Color(C_SEL, a))
		_:
			if ch >= 97 and ch <= 122:    # 'a'..'z'
				var selected: bool = bridge.EditorSelectedGuard == ch
				var col: Color = C_SEL if selected else C_GUARD
				draw_rect(cell, Color(col, 0.22 * a))
				draw_circle(Vector2(x + CS * 0.5, y + CS * 0.5), 6.0, Color(col, a))
				draw_string(_font, Vector2(x + 6, y + 15), char(ch),
					HORIZONTAL_ALIGNMENT_LEFT, -1, 11, C_BG)


func _draw_grid_lines() -> void:
	var lvl: Vector2 = level_px()
	for c in range(cols() + 1):
		var x: float = c * CS
		draw_line(Vector2(x, 0), Vector2(x, lvl.y),
			C_GRID_MAJOR if c % 4 == 0 else C_GRID, 1.0)
	for r in range(rows() + 1):
		var y: float = r * CS
		draw_line(Vector2(0, y), Vector2(lvl.x, y),
			C_GRID_MAJOR if r % 4 == 0 else C_GRID, 1.0)


func _draw_routes() -> void:
	var ids: PackedByteArray = bridge.EditorGuardIds()
	for id in ids:
		var route: PackedInt32Array = bridge.EditorGetRoute(id)
		if route.size() < 2:
			continue
		var selected: bool = bridge.EditorSelectedGuard == id
		var col: Color = C_SEL if selected else C_ROUTE
		if not selected:
			col.a = 0.45

		var pts := PackedVector2Array()
		var i: int = 0
		while i < route.size():
			pts.append(Vector2(route[i] * CS + CS * 0.5, route[i + 1] * CS + CS * 0.5))
			i += 2

		# The loop closes implicitly (spec §2.1), so draw it closed.
		for j in range(pts.size()):
			var a: Vector2 = pts[j]
			var b: Vector2 = pts[(j + 1) % pts.size()]
			draw_line(a, b, col, 2.0 if selected else 1.0)

		for j2 in range(pts.size()):
			draw_circle(pts[j2], 3.0, col)
			if selected:
				draw_string(_font, pts[j2] + Vector2(5, -4), str(j2),
					HORIZONTAL_ALIGNMENT_LEFT, -1, 9, col)


func _cell_rect(r: Rect2i) -> Rect2:
	return Rect2(r.position.x * CS, r.position.y * CS, r.size.x * CS, r.size.y * CS)


func _draw_selection() -> void:
	var r: Rect2i = _clip_rect(_sel)
	if not r.has_area():
		return
	var box: Rect2 = _cell_rect(r)
	draw_rect(box, Color(C_SEL, 0.07))
	draw_rect(box, C_SEL, false, 1.5)
	_label(box.position + Vector2(2, -4), "%dx%d" % [r.size.x, r.size.y], C_SEL)


## What the press is about to do, drawn where it will land: the brush's
## footprint, the shape being dragged, or the clipboard under the cursor.
func _draw_preview() -> void:
	if _pasting:
		for y in range(_clip_size.y):
			for x in range(_clip_size.x):
				var p: Vector2i = _hover_raw + Vector2i(x, y)
				if _in_bounds(p):
					_draw_glyph(_clip[y * _clip_size.x + x], p.x * CS, p.y * CS, 0.6)
		var box: Rect2 = _cell_rect(Rect2i(_hover_raw, _clip_size))
		draw_rect(box, C_SEL, false, 1.5)
		_label(box.position + Vector2(2, -4), "paste %dx%d" % [_clip_size.x, _clip_size.y], C_SEL)
		return

	var tint: Color = C_ERR if _painting == 2 else C_SEL
	if _painting != 0 and _mode in [M_LINE, M_RECT, M_BOX]:
		if _mode == M_BOX:
			draw_rect(_cell_rect(_clip_rect(_rect_between(_anchor, _drag_end))), Color(tint, 0.25))
		else:
			for p in shape_cells(_mode, _anchor, _drag_end):
				if _in_bounds(p):
					draw_rect(Rect2(p.x * CS, p.y * CS, CS, CS), Color(tint, 0.30))
		var span: Rect2i = _rect_between(_anchor, _drag_end)
		_label(Vector2(_drag_end.x * CS + CS + 4, _drag_end.y * CS - 2),
			"%dx%d" % [span.size.x, span.size.y], tint)
		return

	if _painting != 0 or not _in_bounds(_hover):
		return
	var size: int = _brush if _stampable() and _mode in [M_BRUSH, M_LINE, M_RECT] else 1
	var off: int = (size - 1) / 2
	var foot := Rect2((_hover.x - off) * CS, (_hover.y - off) * CS, size * CS, size * CS)
	draw_rect(foot, Color(1, 1, 1, 0.10))
	draw_rect(foot, Color(1, 1, 1, 0.45), false, 1.0)
	# Where the mirror will put the same stroke.
	if _stampable() and _mode != M_SELECT:
		for q in mirrored(_hover).slice(1):
			var ghost := Rect2((q.x - off) * CS, (q.y - off) * CS, size * CS, size * CS)
			draw_rect(ghost, Color(C_SEL, 0.35), false, 1.0)
	var what: String = _cell_name(bridge.EditorGetCell(_hover.x, _hover.y))
	_label(foot.position + Vector2(2, -3), "%d,%d %s" % [_hover.x, _hover.y, what],
		Color(1, 1, 1, 0.55))

	# The waypoint tool says what a press will do before you make it: grab a
	# point, insert on the line, or append.
	if _tool == T_WAYPOINT and bridge.EditorSelectedGuard != 0:
		var centre := Vector2(_hover) * CS + Vector2(CS, CS) * 0.5
		if waypoint_at(_hover) >= 0:
			draw_arc(centre, 7.0, 0.0, TAU, 20, C_SEL, 2.0)
		elif segment_at(_hover) >= 0:
			draw_line(centre - Vector2(5, 0), centre + Vector2(5, 0), C_SEL, 2.0)
			draw_line(centre - Vector2(0, 5), centre + Vector2(0, 5), C_SEL, 2.0)


## What the hover label calls a glyph.
func _cell_name(g: int) -> String:
	match g:
		35: return "wall"
		46: return ""
		64: return "spawn"
		88: return "exit"
		36: return "records"
		67: return "chest"
		33: return "objective"
		61: return "glass"
		43: return "door"
		42: return "sweep node"
	if g >= 97 and g <= 122:
		return "guard " + char(g)
	return ""


## Mirror axes, and the cell a jump from the issues panel landed on.
func _draw_guides() -> void:
	var lvl: Vector2 = level_px()
	var axis := Color(C_SEL, 0.35)
	if _mirror & 1:
		var x: float = lvl.x * 0.5
		for y in range(0, int(lvl.y), 16):
			draw_line(Vector2(x, y), Vector2(x, minf(y + 8, lvl.y)), axis, 2.0)
	if _mirror & 2:
		var y2: float = lvl.y * 0.5
		for x2 in range(0, int(lvl.x), 16):
			draw_line(Vector2(x2, y2), Vector2(minf(x2 + 8, lvl.x), y2), axis, 2.0)
	if _flash_t > 0.0 and _in_bounds(_flash):
		var a: float = clampf(_flash_t, 0.0, 1.0)
		var r: float = CS * (0.8 + 0.6 * (1.2 - _flash_t))
		draw_arc(Vector2(_flash) * CS + Vector2(CS, CS) * 0.5, r, 0.0, TAU, 28,
			Color(C_SEL, a), 2.0)


## A label in the WORLD pass, drawn at screen scale so it stays readable
## however far out the view is zoomed.
func _label(at: Vector2, text: String, col: Color) -> void:
	draw_set_transform(_to_screen(at), 0.0, Vector2.ONE)
	draw_string(_font, Vector2.ZERO, text, HORIZONTAL_ALIGNMENT_LEFT, -1, 10, col)
	draw_set_transform(_origin, 0.0, Vector2(_zoom, _zoom))


func _tool_rect(i: int) -> Rect2:
	return Rect2(10.0 + i * 85.5, FIELD_H + 5.0, 81.0, 20.0)


func _mode_rect(i: int) -> Rect2:
	return Rect2(10.0 + i * 64.0, FIELD_H + 29.0, 60.0, 18.0)


func _brush_rect() -> Rect2:
	return Rect2(10.0 + MODES.size() * 64.0 + 6.0, FIELD_H + 29.0, 104.0, 18.0)


func _mirror_rect() -> Rect2:
	return Rect2(_brush_rect().end.x + 6.0, FIELD_H + 29.0, 84.0, 18.0)


func _button(box: Rect2, text: String, on: bool, dim: bool = false) -> void:
	draw_rect(box, Color(0.16, 0.19, 0.24) if on else Color(0.09, 0.10, 0.13))
	draw_rect(box, Color(0.85, 0.88, 0.94) if on else Color(0.22, 0.25, 0.30), false, 1.0)
	var col: Color = Color(0.92, 0.94, 0.97) if on else Color(0.55, 0.60, 0.67)
	if dim:
		col.a = 0.45
	draw_string(_font, Vector2(box.position.x + 6, box.position.y + box.size.y - 5),
		text, HORIZONTAL_ALIGNMENT_LEFT, box.size.x - 10, 10, col)


func _draw_palette() -> void:
	for i in range(TOOLS.size()):
		var t: Dictionary = TOOLS[i]
		_button(_tool_rect(i), "%s %s" % [t["key"], t["label"]], i == _tool)

	# Modes only shape the structure tools; with an actor in hand they are
	# greyed, not hidden, and Select still works.
	var actor: bool = not _stampable()
	for i in range(MODES.size()):
		_button(_mode_rect(i), MODES[i]["label"], i == _mode and not _pasting,
			actor and i != M_SELECT)
	_button(_brush_rect(), "-   brush %dx%d   +" % [_brush, _brush], false, actor)
	_button(_mirror_rect(), "K mirror %s" % MIRRORS[_mirror], _mirror != 0)

	var hint: String = MODES[_mode]["hint"]
	var hint_col: Color = C_INFO
	var sel: Rect2i = _clip_rect(_sel)
	if _pasting:
		hint = "PASTE %dx%d  ·  click stamps  ·  R rotate  ·  H flip  ·  ESC done" % [
			_clip_size.x, _clip_size.y]
		hint_col = C_SEL
	elif sel.has_area():
		hint = "selection %dx%d  ·  ctrl+C/X copy/cut  ·  DEL clear  ·  ESC drop" % [
			sel.size.x, sel.size.y]
		hint_col = C_SEL
	elif _tool == T_WAYPOINT and _mode != M_SELECT:
		hint = "drag a point to move it  ·  click the line to insert  ·  RMB a point deletes" \
			if bridge.EditorSelectedGuard != 0 else "select a guard first: 6 then click him, or G"
	elif actor and _mode != M_SELECT:
		hint = "%s places one per click" % TOOLS[_tool]["label"]
	elif _tool == T_CHEST:
		hint += "  ·  shift: objective"
	var hx: float = _mirror_rect().end.x + 10.0
	draw_string(_font, Vector2(hx, FIELD_H + 42), hint,
		HORIZONTAL_ALIGNMENT_LEFT, FIELD_W - hx - 8.0, 10, hint_col)

	draw_string(_font, Vector2(10, FIELD_H + 63),
		"LMB paint  RMB erase  alt+LMB pick  M mode  [ ] brush  K mirror  G next guard  I issues  ctrl+Z/Y undo/redo  ctrl+C/X/V  F1 all keys",
		HORIZONTAL_ALIGNMENT_LEFT, FIELD_W - 20, 10, C_INFO)
	draw_string(_font, Vector2(10, FIELD_H + 76),
		"S save   L load   N new   F2 rename   ENTER playtest   TAB game",
		HORIZONTAL_ALIGNMENT_LEFT, 460, 10, C_INFO)
	draw_string(_font, Vector2(FIELD_W - 480, FIELD_H + 76),
		"arrows/MMB pan   -/= or wheel zoom   F fit   ctrl+arrows resize",
		HORIZONTAL_ALIGNMENT_RIGHT, 472, 10, C_INFO)


func _draw_status() -> void:
	var name_text: String = bridge.EditorName
	if _renaming:
		name_text = _name_buf + "_"
	var sel: int = bridge.EditorSelectedGuard
	var sel_text: String = "none" if sel == 0 else char(sel)

	# Size is in the header because it is now a thing you can change, and a
	# level quietly two rows shorter than you meant is hard to spot by eye.
	# The selected guard's loot points ride beside his letter, and the floor's
	# two loot totals at the end: what the mission select will say it is worth.
	var pts: String = "" if sel == 0 else " %s" % _money(bridge.EditorGuardPoints(sel))
	var header: String = "EDITOR   %s   %dx%d @%d%%   guard %s%s   loot %s chests / %s guards" % [
		name_text, cols(), rows(), int(round(_zoom * 100.0)), sel_text, pts,
		_money(bridge.EditorChestBudget), _money(bridge.EditorGuardLootTotal)]
	draw_rect(Rect2(0, 0, FIELD_W, 20), Color(0.02, 0.03, 0.04, 0.82))
	draw_string(_font, Vector2(8, 14), header, HORIZONTAL_ALIGNMENT_LEFT, 620, 11,
		C_SEL if _renaming else Color(0.82, 0.86, 0.92))

	if _notice_t > 0.0:
		var a: float = clampf(_notice_t, 0.0, 1.0)
		draw_string(_font, Vector2(FIELD_W - 500, 14), _notice,
			HORIZONTAL_ALIGNMENT_RIGHT, 492, 11, Color(0.55, 0.85, 0.70, a))


## Authoring problems, live. The parser is total so none of these stop a level
## loading — they predict a level that plays badly, which is worse.
func _draw_issues() -> void:
	if _issues.is_empty():
		return
	var box: Rect2 = _issues_box()
	draw_rect(box, Color(0.02, 0.03, 0.04, 0.80))
	draw_rect(box, Color(0.18, 0.20, 0.25), false, 1.0)

	var errors: int = 0
	var warns: int = 0
	for issue in _issues:
		if issue.begins_with("error"): errors += 1
		elif issue.begins_with("warn"): warns += 1
	var head: Rect2 = _issue_row(-1)
	var head_col: Color = C_ERR if errors > 0 else (C_WARN if warns > 0 else C_INFO)
	draw_string(_font, Vector2(head.position.x + 7, head.end.y - 3),
		"%s %d error(s)  ·  %d warning(s)  ·  I or click to %s" % [
			"[-]" if _issues_open else "[+]", errors, warns, "fold" if _issues_open else "open"],
		HORIZONTAL_ALIGNMENT_LEFT, box.size.x - 14, 10, head_col)
	if not _issues_open:
		return

	var m: Vector2 = get_local_mouse_position()
	for i in range(_issues.size()):
		var issue: String = _issues[i]
		var row: Rect2 = _issue_row(i)
		var col: Color = C_INFO
		if issue.begins_with("error"):
			col = C_ERR
		elif issue.begins_with("warn"):
			col = C_WARN
		var link: bool = issue_links(issue)
		if link and row.has_point(m):
			draw_rect(row, Color(1, 1, 1, 0.06))
		draw_string(_font, Vector2(row.position.x + 7, row.end.y - 3), issue,
			HORIZONTAL_ALIGNMENT_LEFT, box.size.x - 26, 10, col)
		if link:
			draw_string(_font, Vector2(row.end.x - 14, row.end.y - 3), "›",
				HORIZONTAL_ALIGNMENT_LEFT, -1, 11, col)


func _issues_box() -> Rect2:
	var n: int = 1 + (_issues.size() if _issues_open else 0)
	return Rect2(FIELD_W - 428.0, 26.0, 420.0, 14.0 * n + 8.0)


## Row -1 is the header; rows 0.. are the issues.
func _issue_row(i: int) -> Rect2:
	var box: Rect2 = _issues_box()
	return Rect2(box.position.x, box.position.y + 4.0 + 14.0 * (i + 1), box.size.x, 14.0)


## What a press at `m` hits on the issues panel: -1 the header, an issue index
## for a LINK, -2 for anything else (which paints through).
func issue_at(m: Vector2) -> int:
	if _issues.is_empty() or not _issues_box().has_point(m):
		return -2
	if _issue_row(-1).has_point(m):
		return -1
	if not _issues_open:
		return -2
	for i in range(_issues.size()):
		if _issue_row(i).has_point(m) and issue_links(_issues[i]):
			return i
	return -2


static func issue_links(text: String) -> bool:
	return RegEx.create_from_string("guard [a-z]\\b|\\(\\d+,\\d+\\)").search(text) != null


## F1. A heading row has an empty second column.
const HELP := [
	["TOOLS", ""],
	["1-9, 0", "wall floor spawn exit records guard waypoint chest glass door"],
	["shift+LMB (chest)", "an objective site instead of a chest"],
	["O", "sweep node: where hunting guards look first, one per click"],
	["click a button", "both toolbar rows are clickable"],
	["alt+LMB", "eyedropper: take the tool under the cursor; on a guard, select him"],
	["MODES", ""],
	["M / shift+M", "cycle brush, line, rect, filled rect, fill, select"],
	["[  ]", "brush size 1-9: brush width, line width, rect wall thickness"],
	["LMB / RMB", "paint / erase, in whatever shape the mode draws"],
	["K / shift+K", "mirror off, X, Y, XY: strokes, fills and pastes repeat across the centre"],
	["ESC", "cancel the drag, then the paste, then the selection"],
	["SELECTION", ""],
	["ctrl+A", "select the whole level"],
	["ctrl+C / ctrl+X", "copy / cut structure (guards and spawn stay put)"],
	["ctrl+V", "paste mode: LMB stamps (repeatably), R rotates, H flips"],
	["DEL", "clear the selection to floor, guards included"],
	["ROUTES", ""],
	["6 then LMB", "place a guard, or select an existing one"],
	["G / shift+G", "select the next / previous guard and centre on him"],
	["7, LMB", "on a waypoint: drag it  ·  on the route line: insert  ·  else: append"],
	["7, RMB", "on a waypoint: delete just that point"],
	["backspace / C", "drop the last waypoint / clear the route"],
	["LEVEL", ""],
	["ctrl+Z / ctrl+Y", "undo / redo (ctrl+shift+Z also redoes)"],
	["I", "fold the issues panel; click an issue naming a cell or guard to go there"],
	["ctrl+arrows", "resize by 4 cells; a shrink says what it dropped"],
	["S  L  N  F2", "save, load next, new, rename"],
	["ENTER  TAB", "playtest this buffer, back to the game"],
]


func _draw_help() -> void:
	if not _help:
		return
	var w: float = 620.0
	var h: float = 15.0 * HELP.size() + 40.0
	var box := Rect2((FIELD_W - w) * 0.5, (FIELD_H - h) * 0.5, w, h)
	draw_rect(box, Color(0.02, 0.03, 0.04, 0.94))
	draw_rect(box, Color(0.30, 0.34, 0.40), false, 1.0)
	draw_string(_font, box.position + Vector2(14, 20), "LEVEL EDITOR KEYS   ·   F1 or ESC closes",
		HORIZONTAL_ALIGNMENT_LEFT, w - 28, 11, Color(0.82, 0.86, 0.92))
	var y: float = box.position.y + 44.0
	for row in HELP:
		if String(row[1]).is_empty():
			draw_string(_font, Vector2(box.position.x + 14, y), row[0],
				HORIZONTAL_ALIGNMENT_LEFT, -1, 10, C_SEL)
		else:
			draw_string(_font, Vector2(box.position.x + 24, y), row[0],
				HORIZONTAL_ALIGNMENT_LEFT, 130, 10, Color(0.82, 0.86, 0.92))
			draw_string(_font, Vector2(box.position.x + 160, y), row[1],
				HORIZONTAL_ALIGNMENT_LEFT, w - 174, 10, C_INFO)
		y += 15.0
