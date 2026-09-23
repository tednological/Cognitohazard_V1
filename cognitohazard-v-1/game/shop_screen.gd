extends Node2D

## Spend what the last run paid.
##
## Stock comes from the ONE item table (SimBridge.GetShopStock, which is
## GearCatalog filtered to things with a price), so an item added to the sim is
## purchasable the moment it exists and there is no second list to forget.
##
## Buying puts the item in the STASH, not on your body: what you wear is still
## the equipment screen's business. That separation is why this screen can be
## this small — it moves money one way and an item the other, and nothing else.

const CAT := preload("res://game/item_catalog.gd")

const FIELD_W: float = 960.0
const FIELD_H: float = 560.0

const PANEL_W: float = 700.0
const PANEL_H: float = 500.0
const PANEL_X: float = (FIELD_W - PANEL_W) * 0.5
const PANEL_Y: float = ((FIELD_H + 60.0) - PANEL_H) * 0.5

const ROW_H: float = 26.0
const ROWS_Y: float = 96.0
const PAD: float = 24.0
const VISIBLE_ROWS: int = 12

var active: bool = false
var bridge: RefCounted
var stash: RefCounted
var campaign: RefCounted
var draws: int = 0

var _row: int = 0
var _scroll: int = 0
var _stock: PackedInt32Array = PackedInt32Array()
var _notice: String = ""
var _notice_t: float = 0.0
var _font: Font

signal closed()

const C_SCRIM := Color(0.0, 0.0, 0.0, 0.55)
const C_PANEL := Color(0.043, 0.051, 0.063, 0.95)
const C_EDGE := Color(0.26, 0.30, 0.37, 0.95)
const C_TEXT := Color(0.82, 0.86, 0.92)
const C_DIM := Color(0.42, 0.47, 0.54)
const C_SEL := Color(1.0, 0.95, 0.55)
const C_GOOD := Color(0.35, 0.80, 0.55)
const C_BAD := Color(0.92, 0.45, 0.40)
const C_ROW := Color(0.09, 0.105, 0.13)


func _ready() -> void:
	_font = ThemeDB.fallback_font
	visible = false
	z_index = 240


func open_screen() -> void:
	_stock = PackedInt32Array(bridge.GetShopStock())
	_row = 0
	_scroll = 0
	_notice = ""
	active = true
	visible = true
	queue_redraw()


func close_screen() -> void:
	active = false
	visible = false
	closed.emit()


func row_count() -> int:
	return _stock.size()


func selected_item() -> int:
	if _stock.is_empty():
		return -1
	return _stock[clampi(_row, 0, _stock.size() - 1)]


func move(delta: int) -> void:
	if _stock.is_empty():
		return
	_row = (_row + delta + _stock.size()) % _stock.size()
	# Keep the highlight on screen without letting the list jump around it.
	if _row < _scroll:
		_scroll = _row
	elif _row >= _scroll + VISIBLE_ROWS:
		_scroll = _row - VISIBLE_ROWS + 1
	_scroll = clampi(_scroll, 0, maxi(0, _stock.size() - VISIBLE_ROWS))
	queue_redraw()


## Buy the highlighted item. Refuses, with a reason, rather than half-completing:
## money only leaves the ledger once the stash has actually taken the item, so a
## full stash cannot charge you for something you did not receive.
func buy() -> bool:
	var item: int = selected_item()
	if item < 0:
		return false

	var price: int = bridge.GearPrice(item)
	if not campaign.can_afford(price):
		_notify("%d short" % (price - campaign.money), C_BAD)
		return false

	if stash.add(item) == stash.NONE:
		_notify("no room in the stash", C_BAD)
		return false

	campaign.spend(price)
	_notify("bought %s for %d" % [bridge.GearName(item), price], C_GOOD)
	return true


var _notice_col: Color = C_GOOD

func _notify(text: String, col: Color) -> void:
	_notice = text
	_notice_col = col
	_notice_t = 2.5
	queue_redraw()


func _process(delta: float) -> void:
	if _notice_t > 0.0:
		_notice_t -= delta
	if active:
		queue_redraw()


func _draw() -> void:
	if not active or bridge == null or campaign == null:
		return
	draws += 1

	draw_rect(Rect2(0, 0, FIELD_W, FIELD_H + 60), C_SCRIM)
	var panel := Rect2(PANEL_X, PANEL_Y, PANEL_W, PANEL_H)
	draw_rect(panel, C_PANEL)
	draw_rect(panel, C_EDGE, false, 1.0)

	draw_set_transform(Vector2(PANEL_X, PANEL_Y), 0.0, Vector2.ONE)

	draw_string(_font, Vector2(PAD, 44), "SUPPLY", HORIZONTAL_ALIGNMENT_LEFT, -1, 24, C_TEXT)
	draw_string(_font, Vector2(PAD, 66), "bought gear goes to the stash; wear it with E",
		HORIZONTAL_ALIGNMENT_LEFT, -1, 12, C_DIM)
	draw_string(_font, Vector2(PANEL_W - PAD - 300, 44), "%d" % campaign.money,
		HORIZONTAL_ALIGNMENT_RIGHT, 300, 24, C_GOOD)
	draw_string(_font, Vector2(PANEL_W - PAD - 300, 62), "on hand",
		HORIZONTAL_ALIGNMENT_RIGHT, 300, 10, C_DIM)

	_draw_rows()
	_draw_footer()

	draw_set_transform(Vector2.ZERO, 0.0, Vector2.ONE)


func _draw_rows() -> void:
	if _stock.is_empty():
		draw_string(_font, Vector2(PAD, ROWS_Y + 20), "nothing for sale",
			HORIZONTAL_ALIGNMENT_LEFT, -1, 13, C_DIM)
		return

	var last: int = mini(_stock.size(), _scroll + VISIBLE_ROWS)
	var y: float = ROWS_Y
	for i in range(_scroll, last):
		var item: int = _stock[i]
		var price: int = bridge.GearPrice(item)
		var box := Rect2(PAD, y, PANEL_W - PAD * 2.0, ROW_H - 3.0)
		draw_rect(box, C_ROW)
		if i == _row:
			draw_rect(box, C_SEL, false, 2.0)

		var s: Vector2i = CAT.size_of(bridge, item)
		var owned: int = _owned_count(item)
		# The name in its rarity colour, with a bar down the edge; the 2 px
		# selection box says which row is chosen, so the name need not.
		draw_rect(Rect2(box.position, Vector2(3.0, box.size.y)), CAT.rarity_colour(bridge, item))
		draw_string(_font, box.position + Vector2(10, 16), bridge.GearName(item),
			HORIZONTAL_ALIGNMENT_LEFT, 250, 13, CAT.rarity_colour(bridge, item))
		draw_string(_font, box.position + Vector2(268, 16), "%dx%d" % [s.x, s.y],
			HORIZONTAL_ALIGNMENT_LEFT, 60, 10, C_DIM)
		draw_string(_font, box.position + Vector2(336, 16),
			bridge.GearSlotName(bridge.GearSlotOf(item)),
			HORIZONTAL_ALIGNMENT_LEFT, 140, 10, C_DIM)
		if owned > 0:
			draw_string(_font, box.position + Vector2(470, 16), "owned %d" % owned,
				HORIZONTAL_ALIGNMENT_LEFT, 90, 10, C_DIM)
		draw_string(_font, box.position + Vector2(box.size.x - 110, 16), str(price),
			HORIZONTAL_ALIGNMENT_RIGHT, 100, 13,
			C_GOOD if campaign.can_afford(price) else C_BAD)
		y += ROW_H

	if _stock.size() > VISIBLE_ROWS:
		draw_string(_font, Vector2(PAD, ROWS_Y + VISIBLE_ROWS * ROW_H + 16),
			"%d - %d of %d" % [_scroll + 1, last, _stock.size()],
			HORIZONTAL_ALIGNMENT_LEFT, 300, 10, C_DIM)


func _owned_count(item_id: int) -> int:
	if stash == null:
		return 0
	return stash.count_of(item_id)


func _draw_footer() -> void:
	if _notice_t > 0.0:
		var col: Color = _notice_col
		col.a = clampf(_notice_t, 0.0, 1.0)
		draw_string(_font, Vector2(PAD, PANEL_H - 52), _notice,
			HORIZONTAL_ALIGNMENT_LEFT, PANEL_W - PAD * 2.0, 12, col)

	draw_string(_font, Vector2(PAD, PANEL_H - 30),
		"up / down choose  ·  ENTER buys  ·  B or ESC closes",
		HORIZONTAL_ALIGNMENT_LEFT, -1, 12, C_TEXT)
