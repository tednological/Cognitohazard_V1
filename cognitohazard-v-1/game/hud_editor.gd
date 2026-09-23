extends Node2D

## Drag the HUD into whatever arrangement you want.
##
## F4 opens it. Every element gets a box you can pick up with the left button
## and drop somewhere else; the live HUD keeps drawing underneath, so what you
## are arranging is the real thing and not a mock-up of it. R resets everything
## to the shipped layout, ENTER or F4 closes and saves, ESC closes and throws
## the session's changes away.
##
## It owns the DRAG and nothing else. Where things are lives in hud_layout.gd,
## and what they look like lives in main.gd -- this script never draws a HUD
## element itself, which is what keeps the editor from slowly becoming a second,
## subtly different HUD.
##
## The world keeps running behind it, like the loot panel: the mouse buttons are
## withheld from the sim while it is up so a drag cannot also empty a magazine.

const LAYOUT := preload("res://game/hud_layout.gd")

const FIELD_W: float = 960.0
const FIELD_H: float = 560.0
const HUD_H: float = 60.0

var active: bool = false
var layout: RefCounted
var draws: int = 0

var _font: Font
var _held: String = ""
var _grab: Vector2 = Vector2.ZERO
var _hover: String = ""
var _notice: String = ""
var _notice_t: float = 0.0

## What the layout looked like when the editor opened, so ESC can put it back.
var _restore: String = ""

signal closed()

const C_SCRIM := Color(0.0, 0.0, 0.0, 0.30)
const C_BOX := Color(0.35, 0.62, 0.78, 0.16)
const C_BOX_EDGE := Color(0.45, 0.72, 0.88, 0.70)
const C_HOVER := Color(1.0, 0.95, 0.55, 0.85)
const C_HELD := Color(0.42, 0.88, 0.62, 0.95)
const C_MOVED := Color(0.95, 0.72, 0.40, 0.80)
const C_TEXT := Color(0.86, 0.90, 0.95)
const C_DIM := Color(0.55, 0.60, 0.68)
const C_PANEL := Color(0.043, 0.051, 0.063, 0.92)


func _ready() -> void:
	_font = ThemeDB.fallback_font
	visible = false
	z_index = 200


func open_editor() -> void:
	if layout == null:
		return
	_restore = layout.to_text()
	_held = ""
	active = true
	visible = true
	queue_redraw()


## Closing SAVES, because the arrangement is a preference and losing it to a
## stray keypress would be worse than saving one you did not mean.
func close_editor(keep: bool = true) -> void:
	if not keep and not _restore.is_empty():
		layout.from_text(_restore)
	elif keep:
		layout.save()
	active = false
	visible = false
	_held = ""
	closed.emit()


func _process(delta: float) -> void:
	if _notice_t > 0.0:
		_notice_t -= delta
	if active:
		_hover = _held if not _held.is_empty() else layout.hit(get_local_mouse_position())
		queue_redraw()


func _notify(text: String) -> void:
	_notice = text
	_notice_t = 2.5


# ------------------------------------------------------------------ input
#
# Driven from main.gd rather than from _unhandled_input, because main.gd owns
# every key in this project: polling here and handling there would see one
# physical press twice.

func press(pos: Vector2) -> void:
	if not active:
		return
	var id: String = layout.hit(pos)
	if id.is_empty():
		return
	_held = id
	_grab = pos - layout.pos_of(id)


func drag(pos: Vector2) -> void:
	if not active or _held.is_empty():
		return
	layout.set_pos(_held, pos - _grab)


func release() -> void:
	_held = ""


func reset_all() -> void:
	layout.reset()
	_notify("layout reset to the shipped arrangement")


## Nudge the element under the cursor by one grid step, for the last few pixels
## that a mouse makes fiddly.
func nudge(delta: Vector2) -> void:
	var id: String = _hover
	if id.is_empty():
		return
	layout.set_pos(id, layout.pos_of(id) + delta * LAYOUT.GRID)


func _draw() -> void:
	if not active or layout == null:
		return
	draws += 1

	draw_rect(Rect2(0, 0, FIELD_W, FIELD_H + HUD_H), C_SCRIM)

	for id in layout.ids():
		_draw_box(id)

	_draw_help()


func _draw_box(id: String) -> void:
	var box: Rect2 = layout.rect_of(id)
	var held: bool = id == _held
	var hover: bool = id == _hover

	draw_rect(box, C_BOX)
	var edge: Color = C_BOX_EDGE
	if not layout.is_default(id):
		edge = C_MOVED
	if hover:
		edge = C_HOVER
	if held:
		edge = C_HELD
	draw_rect(box, edge, false, 2.0 if (hover or held) else 1.0)

	# The name sits INSIDE the box, top-left, so an element parked against the
	# screen edge still says what it is.
	draw_string(_font, box.position + Vector2(4, 11), layout.label_of(id),
		HORIZONTAL_ALIGNMENT_LEFT, box.size.x - 8, 10, C_TEXT if hover or held else C_DIM)


func _draw_help() -> void:
	var h: float = 62.0
	var panel := Rect2(0, 0, FIELD_W, h)
	draw_rect(panel, C_PANEL)
	draw_rect(Rect2(0, h, FIELD_W, 1), C_BOX_EDGE)

	draw_string(_font, Vector2(14, 20), "HUD LAYOUT",
		HORIZONTAL_ALIGNMENT_LEFT, -1, 15, C_TEXT)
	draw_string(_font, Vector2(14, 38),
		"drag any box to move it  ·  arrows nudge what is under the cursor",
		HORIZONTAL_ALIGNMENT_LEFT, 620, 11, C_DIM)
	draw_string(_font, Vector2(14, 53),
		"R reset  ·  ENTER or F4 save and close  ·  ESC discard",
		HORIZONTAL_ALIGNMENT_LEFT, 620, 11, C_DIM)

	var moved: int = 0
	for id in layout.ids():
		if not layout.is_default(id):
			moved += 1
	draw_string(_font, Vector2(FIELD_W - 320, 20),
		"%d of %d moved" % [moved, layout.ids().size()],
		HORIZONTAL_ALIGNMENT_RIGHT, 306, 11, C_DIM)

	if not _hover.is_empty():
		var p: Vector2 = layout.pos_of(_hover)
		draw_string(_font, Vector2(FIELD_W - 320, 38),
			"%s   %d, %d" % [layout.label_of(_hover), int(p.x), int(p.y)],
			HORIZONTAL_ALIGNMENT_RIGHT, 306, 11, C_HOVER)

	if _notice_t > 0.0:
		draw_string(_font, Vector2(FIELD_W - 320, 53), _notice,
			HORIZONTAL_ALIGNMENT_RIGHT, 306, 11,
			Color(0.55, 0.85, 0.70, clampf(_notice_t, 0.0, 1.0)))
