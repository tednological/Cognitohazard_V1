extends Node2D

## What is still on the body you are standing over, listed item by item, with
## nothing identified until you have looked at it long enough.
##
## Identifying is a DWELL: rest the cursor on a row and a bar fills across it;
## when the bar completes, the item resolves from "unidentified" into its name.
## Taking is a RIGHT-CLICK, and only ever on something already identified, so
## nothing leaves a body that the player has not named.
##
## The RIGHT button, not either one. Taking the last item empties the kit, which
## ends the rummage on that same tick -- and the left button still being held
## read as fire, so grabbing the last thing out of a chest fired a round into
## it. Looting is bound to the button that is not the trigger.
##
## The dwell is on the cursor rather than on the body, which is the distinction
## that matters: standing over a corpse still costs nothing and reveals nothing.
## The cost is paid per item, by the player choosing which of five bulges is
## worth the seconds to work out -- with the world running the whole time.
##
## The world KEEPS RUNNING while this is up -- guards still patrol and the alarm
## still climbs -- because freezing it would
## have made rummaging a corpse free, which is the opposite of what looting in
## the open should feel like. main.gd withholds the fire and aim flags from the
## sim while the panel is open, so a click inspects instead of shooting.
##
## What has been inspected, and how far along each dwell is, is PRESENTATION and
## not sim state: it feeds no hash and is not recorded, so a replay of a run will
## not reproduce which items the player had bothered to look at. That is the
## right trade -- recording it would mean the identity of an item changed the
## simulation, which it does not. It is also why the dwell runs on frame delta
## rather than on a sim clock: it is allowed to, because nothing downstream of
## it is deterministic.

const CAT := preload("res://game/item_catalog.gd")

const FIELD_W: float = 960.0
const FIELD_H: float = 560.0

const PANEL_W: float = 250.0
const PANEL_X: float = FIELD_W - PANEL_W - 16.0
const PANEL_Y: float = 96.0
const ROW_H: float = 30.0
const HEAD_H: float = 52.0
const PAD: float = 10.0

## Seconds of held cursor to identify one item. Long enough that a five-item
## body is a real decision with guards still walking, short enough that
## identifying the one thing you actually came for is never a chore.
const IDENTIFY_SECONDS: float = 0.7

var active: bool = false
var bridge: RefCounted
var draws: int = 0

var _font: Font

## The guard being looked at, and the kit as of this frame.
var _guard: int = -1
var _kit: PackedInt32Array = PackedInt32Array()

## How far along the dwell is for each item, per guard index:
## {guard_index: {item_id: progress_0_to_1}}. At 1.0 the item is identified.
##
## Keyed by item id rather than by position in the kit, because the kit SHRINKS
## as items are taken and every position after the gap would otherwise shift its
## identity onto a different item. The cost is that two identical items on one
## body reveal together; kits are rolled one-per-category, so that is rare.
##
## Partial progress is KEPT when the cursor moves away. The cost of looting is
## already the real seconds spent with the world running; making a twitch of the
## mouse throw that away would punish the hand rather than the decision.
var _progress: Dictionary = {}

## Item ids the player has already been told the name of, anywhere. Populated
## when they DROP something: you plainly know what you just put down, and making
## you dwell on your own rifle to pick it back up would be a puzzle about the
## interface rather than about the level.
var _known: Dictionary = {}

const C_PANEL := Color(0.043, 0.051, 0.063, 0.90)
const C_EDGE := Color(0.26, 0.30, 0.37, 0.95)
const C_ROW := Color(0.09, 0.105, 0.13, 0.92)
const C_TEXT := Color(0.82, 0.86, 0.92)
const C_DIM := Color(0.42, 0.47, 0.54)
const C_HIDDEN := Color(0.30, 0.33, 0.39)
const C_GOOD := Color(0.35, 0.80, 0.55)
const C_BAD := Color(0.92, 0.45, 0.40)
const C_SEL := Color(1.0, 0.95, 0.55)
const C_FILL := Color(0.30, 0.52, 0.62, 0.85)


func _ready() -> void:
	_font = ThemeDB.fallback_font
	visible = false
	z_index = 180


## Called every frame the player is holding the loot key over a body.
func show_for(guard_index: int, kit: PackedInt32Array) -> void:
	_guard = guard_index
	_kit = kit
	active = true
	visible = true


func hide_panel() -> void:
	active = false
	visible = false
	_guard = -1
	_kit = PackedInt32Array()


## The dwell runs off frame delta, not the sim clock -- see the note at the top
## about why that is allowed here and nowhere else.
func _process(delta: float) -> void:
	if not active:
		return
	advance_identify(get_local_mouse_position(), delta)
	# The bar has to animate between sim ticks, or it would step in 60ths.
	queue_redraw()


## Advance the dwell on whatever row is under `pos` and return its progress.
## Returns 0.0 when the cursor is not on a row, or on an already-identified one.
func advance_identify(pos: Vector2, delta: float) -> float:
	if not active or delta <= 0.0:
		return 0.0
	var i: int = row_at(pos)
	if i < 0:
		return 0.0

	var item_id: int = _kit[i]
	var now: float = identify_progress(_guard, item_id)
	if now >= 1.0:
		return 1.0

	now = minf(1.0, now + delta / IDENTIFY_SECONDS)
	_set_progress(_guard, item_id, now)
	return now


## How far along this item is, 0.0 to 1.0. What the bar draws.
func identify_progress(guard_index: int, item_id: int) -> float:
	if not _progress.has(guard_index):
		return 0.0
	var rows: Dictionary = _progress[guard_index]
	return rows.get(item_id, 0.0)


func _set_progress(guard_index: int, item_id: int, value: float) -> void:
	if not _progress.has(guard_index):
		_progress[guard_index] = {}
	var rows: Dictionary = _progress[guard_index]
	rows[item_id] = value
	_progress[guard_index] = rows


## Inspecting is per body, so walking away and coming back keeps what you learnt.
func is_seen(guard_index: int, item_id: int) -> bool:
	if _known.has(item_id):
		return true
	return identify_progress(guard_index, item_id) >= 1.0


## This item needs no identifying, wherever it turns up. Used for gear the
## player dropped themselves.
func mark_known(item_id: int) -> void:
	_known[item_id] = true


## Finish the dwell outright. The player never gets this -- it is how the
## harness skips the timer, and how anything that should reveal an item
## immediately would do it.
func mark_seen(guard_index: int, item_id: int) -> void:
	_set_progress(guard_index, item_id, 1.0)


## Everything learnt is forgotten on a restart, along with the bodies it was
## learnt from.
func forget_all() -> void:
	_progress.clear()
	_known.clear()


func row_rect(i: int) -> Rect2:
	return Rect2(PANEL_X + PAD, PANEL_Y + HEAD_H + i * ROW_H,
		PANEL_W - PAD * 2.0, ROW_H - 4.0)


## The row under a point, or -1. Takes a position already in design coordinates.
func row_at(pos: Vector2) -> int:
	for i in range(_kit.size()):
		if row_rect(i).has_point(pos):
			return i
	return -1


## A RIGHT-CLICK on a row TAKES it, and only ever if it has already been
## identified. (This function is button-agnostic; main.gd decides which button
## calls it.)
##
## Clicking a mystery deliberately does nothing at all -- not even start the
## dwell. Identifying is the cursor resting on the row; if a click also did it,
## the bar would be decoration that players learn to click past.
##
## Returns the LootPick to hand the sim this tick -- the row index PLUS ONE -- or
## 0 when the click landed on nothing, or on something not yet identified. The
## plus one is what lets 0 mean "no pick" in a single byte of the input frame.
func click_at(pos: Vector2) -> int:
	if not active:
		return 0
	var i: int = row_at(pos)
	if i < 0:
		return 0
	if not is_seen(_guard, _kit[i]):
		return 0
	return i + 1


## Identify what is under the cursor without taking it. Returns the item id
## revealed, or -1 if there was nothing new to learn.
func inspect_at(pos: Vector2) -> int:
	if not active:
		return -1
	var i: int = row_at(pos)
	if i < 0:
		return -1
	var item_id: int = _kit[i]
	if is_seen(_guard, item_id):
		return -1
	mark_seen(_guard, item_id)
	return item_id


func _draw() -> void:
	if not active or bridge == null:
		return
	draws += 1

	var h: float = HEAD_H + maxf(_kit.size(), 1) * ROW_H + PAD
	var panel := Rect2(PANEL_X, PANEL_Y, PANEL_W, h)
	draw_rect(panel, C_PANEL)
	draw_rect(panel, C_EDGE, false, 1.0)

	draw_string(_font, Vector2(PANEL_X + PAD, PANEL_Y + 22), "ON THE BODY",
		HORIZONTAL_ALIGNMENT_LEFT, -1, 14, C_TEXT)

	var unseen: int = 0
	for i in range(_kit.size()):
		if not is_seen(_guard, _kit[i]):
			unseen += 1
	var note: String = "hold the cursor to identify" if unseen > 0 else "right-click to take"
	draw_string(_font, Vector2(PANEL_X + PAD, PANEL_Y + 40),
		"%d item(s)   ·  %s" % [_kit.size(), note],
		HORIZONTAL_ALIGNMENT_LEFT, PANEL_W - PAD * 2.0, 11, C_DIM)

	if _kit.is_empty():
		draw_string(_font, Vector2(PANEL_X + PAD, PANEL_Y + HEAD_H + 20),
			"stripped clean", HORIZONTAL_ALIGNMENT_LEFT, -1, 12, C_DIM)
		return

	for i in range(_kit.size()):
		_draw_row(i, _kit[i])


func _draw_row(i: int, item_id: int) -> void:
	var box: Rect2 = row_rect(i)
	var seen: bool = is_seen(_guard, item_id)
	var hover: bool = box.has_point(get_local_mouse_position())

	draw_rect(box, C_ROW)

	if not seen:
		# The dwell fills the row itself rather than a separate strip. The row IS
		# the bar, so the thing being worked out and the progress toward working
		# it out occupy the same place, and the text sits on top of the fill.
		var p: float = identify_progress(_guard, item_id)
		if p > 0.0:
			draw_rect(Rect2(box.position, Vector2(box.size.x * p, box.size.y)), C_FILL)

		if hover:
			draw_rect(box, C_SEL, false, 1.0)

		# Unidentified: the SHAPE is visible because you can see the bulge, but
		# not what it is.
		var s: Vector2i = CAT.size_of(bridge, item_id)
		draw_string(_font, box.position + Vector2(8, 19), "unidentified",
			HORIZONTAL_ALIGNMENT_LEFT, -1, 13,
			C_TEXT if p > 0.0 else C_HIDDEN)
		draw_string(_font, box.position + Vector2(box.size.x - 40, 19),
			"%dx%d" % [s.x, s.y], HORIZONTAL_ALIGNMENT_LEFT, -1, 11, C_HIDDEN)
		return

	if hover:
		draw_rect(box, C_SEL, false, 1.0)

	# Identified, so say whether clicking again would actually take it. Asked of
	# the sim's own pack rather than recomputed here, so a row cannot promise
	# something the pick will then refuse.
	var fits: bool = bridge.PackWouldFit(item_id)
	# Identified, it shows its tier: the name in the rarity colour and a bar
	# down the row's edge. NOT before -- an unidentified row that glowed orange
	# would give away exactly what the dwell is there to make you work out.
	var tier: Color = CAT.rarity_colour(bridge, item_id)
	draw_rect(Rect2(box.position, Vector2(3.0, box.size.y)), tier)
	draw_string(_font, box.position + Vector2(8, 19), bridge.GearName(item_id),
		HORIZONTAL_ALIGNMENT_LEFT, box.size.x - 62, 13, tier)
	draw_string(_font, box.position + Vector2(box.size.x - 50, 19),
		"take" if fits else "too big", HORIZONTAL_ALIGNMENT_LEFT, -1, 11,
		C_GOOD if fits else C_BAD)
