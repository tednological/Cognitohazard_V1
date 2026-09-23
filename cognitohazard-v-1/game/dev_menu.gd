extends Node2D

## F8: spawn any item in the game into your pack or your stash.
##
## The list is sim/GearCatalog.cs, unfiltered -- the shop's stock is that same
## table filtered to things with a price and no objectives, and the whole point
## of this screen is to reach what the shop deliberately hides: the free starter
## gear, and the mission objective itself.
##
## WHERE it spawns depends on whether a run is under way, because the two
## destinations are different kinds of thing:
##
## - MID-RUN it goes into the MISSION PACK, which is sim state: its contents
##   feed the state hash and ride in replays. So the menu does NOT put it there
##   itself. It QUEUES the item id, main.gd hands one per tick to
##   InputFrame.SpawnItem, and the sim conjures it inside the tick -- recorded
##   intent, exactly like a loot pick or a drop, which is what lets a replay
##   reproduce a spawned item instead of diverging at it.
## - OTHERWISE (title, stash screen, after a death) there is no pack to speak
##   of, so it goes into the STASH, which is meta, saved to user://stash.txt,
##   and costs the sim nothing.
##
## One rule, and the screen says which one is in force rather than making you
## infer it.

const CAT := preload("res://game/item_catalog.gd")

const FIELD_W: float = 960.0
const FIELD_H: float = 560.0

const PANEL_W: float = 780.0
const PANEL_H: float = 540.0
const PANEL_X: float = (FIELD_W - PANEL_W) * 0.5
const PANEL_Y: float = ((FIELD_H + 60.0) - PANEL_H) * 0.5

const PAD: float = 24.0
const ROW_H: float = 26.0
const ROWS_Y: float = 104.0
const VISIBLE_ROWS: int = 13

## Filter tabs. KIND_ALL is not a sim kind; it is the absence of a filter, and
## it sorts first so opening the menu shows everything.
const KIND_ALL: int = -1
const KINDS: Array = [KIND_ALL, CAT.KIND_WEAPON, CAT.KIND_ARMOUR, CAT.KIND_ATTACHMENT,
	CAT.KIND_PACK, CAT.KIND_APPAREL, CAT.KIND_OBJECTIVE]
const KIND_NAMES: Array = ["all", "weapons", "armour", "attachments", "packs",
	"apparel", "objectives"]
## Indexed by sim GearKind, so this one has no "all" entry and is NOT parallel
## to KINDS/KIND_NAMES.
const KIND_SINGULAR: Array = ["weapon", "armour", "attachment", "pack",
	"apparel", "objective"]

var active: bool = false
var bridge: RefCounted
var stash: RefCounted
var draws: int = 0

## Whether a mission is under way, which decides the target. Set by main.gd from
## SimBridge.RunLive each time the menu is opened -- the menu cannot work it out
## for itself, because "a run is live" is main.gd's business, not the sim's.
var live: bool = false

var _row: int = 0
var _scroll: int = 0
var _tab: int = 0
var _items: PackedInt32Array = PackedInt32Array()
var _notice: String = ""
var _notice_col: Color = Color.WHITE
var _notice_t: float = 0.0
var _font: Font

signal closed()
## Went into the stash, which is done by the time this fires.
signal spawned(item_id: int)
## Wants to go into the mission pack. NOT done yet: main.gd queues it for the
## sim to perform inside a tick, because the pack is hashed, recorded state.
signal pack_spawn_requested(item_id: int)

const C_SCRIM := Color(0.0, 0.0, 0.0, 0.62)
const C_PANEL := Color(0.055, 0.043, 0.063, 0.96)
const C_EDGE := Color(0.46, 0.32, 0.52, 0.95)
const C_TEXT := Color(0.82, 0.86, 0.92)
const C_DIM := Color(0.42, 0.47, 0.54)
const C_SEL := Color(1.0, 0.95, 0.55)
const C_GOOD := Color(0.35, 0.80, 0.55)
const C_BAD := Color(0.92, 0.45, 0.40)
const C_ROW := Color(0.10, 0.09, 0.13)
const C_TAG := Color(0.72, 0.52, 0.86)


func _ready() -> void:
	_font = ThemeDB.fallback_font
	visible = false
	# Above every other screen: this one is opened ON TOP of whatever is already
	# up, including the stash and the title, and it takes the keyboard whole.
	z_index = 320


func open_screen() -> void:
	_tab = 0
	_row = 0
	_scroll = 0
	_notice = ""
	_notice_t = 0.0
	rebuild()
	active = true
	visible = true
	queue_redraw()


func close_screen() -> void:
	active = false
	visible = false
	closed.emit()


## Every catalogue id the current tab admits, in catalogue order -- which is
## authoring order, already grouped by kind, so the unfiltered list reads as
## sections rather than as a shuffle.
func rebuild() -> void:
	var ids: PackedInt32Array = PackedInt32Array()
	if bridge != null:
		var want: int = KINDS[_tab]
		for i in range(bridge.GearCount):
			var id: int = bridge.GearIdAt(i)
			if want == KIND_ALL or bridge.GearKindOf(id) == want:
				ids.append(id)
	_items = ids
	_row = clampi(_row, 0, maxi(0, _items.size() - 1))
	_clamp_scroll()
	queue_redraw()


func row_count() -> int:
	return _items.size()


func tab_name() -> String:
	return KIND_NAMES[_tab]


func selected_item() -> int:
	if _items.is_empty():
		return -1
	return _items[clampi(_row, 0, _items.size() - 1)]


func move(delta: int) -> void:
	if _items.is_empty():
		return
	_row = (_row + delta + _items.size()) % _items.size()
	_clamp_scroll()
	queue_redraw()


## Change filter. The highlight goes back to the top rather than trying to
## follow an item across a list it may not be in.
func cycle_tab(delta: int) -> void:
	_tab = (_tab + delta + KINDS.size()) % KINDS.size()
	_row = 0
	_scroll = 0
	rebuild()


func _clamp_scroll() -> void:
	if _row < _scroll:
		_scroll = _row
	elif _row >= _scroll + VISIBLE_ROWS:
		_scroll = _row - VISIBLE_ROWS + 1
	_scroll = clampi(_scroll, 0, maxi(0, _items.size() - VISIBLE_ROWS))


## Where ENTER will put it, as a word for the screen to show.
func target_name() -> String:
	return "mission pack" if live else "stash"


## Spawn the highlighted item into whichever target is in force.
##
## The two halves are asymmetric on purpose. The stash is done HERE and reported
## done. The pack is only ASKED for -- the sim performs it inside a tick so the
## replay carries it -- so the notice says queued, and whether it actually fit
## is the sim's answer, arriving as a Spawned or PackFull event once the world
## is stepping again.
func spawn() -> bool:
	var item: int = selected_item()
	if item < 0:
		return false

	if live:
		_notify("queued %s for the pack" % bridge.GearName(item), C_GOOD)
		pack_spawn_requested.emit(item)
		return true

	if stash == null:
		return false
	# Refuses with a reason rather than dropping it silently, which is the bug
	# stash.gd's `_issue` was written to stop presets committing.
	if stash.add(item) == stash.NONE:
		_notify("no room in the stash for %s" % bridge.GearName(item), C_BAD)
		return false
	_notify("spawned %s into the stash" % bridge.GearName(item), C_GOOD)
	spawned.emit(item)
	return true


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
	if not active or bridge == null:
		return
	draws += 1

	draw_rect(Rect2(0, 0, FIELD_W, FIELD_H + 60), C_SCRIM)
	var panel := Rect2(PANEL_X, PANEL_Y, PANEL_W, PANEL_H)
	draw_rect(panel, C_PANEL)
	draw_rect(panel, C_EDGE, false, 1.0)

	draw_set_transform(Vector2(PANEL_X, PANEL_Y), 0.0, Vector2.ONE)

	draw_string(_font, Vector2(PAD, 44), "DEVELOPER", HORIZONTAL_ALIGNMENT_LEFT, -1, 24, C_TAG)
	draw_string(_font, Vector2(PAD, 66), "spawn any item into the %s" % target_name(),
		HORIZONTAL_ALIGNMENT_LEFT, -1, 12, C_SEL if live else C_DIM)
	draw_string(_font, Vector2(PANEL_W - PAD - 300, 44),
		"%d items" % bridge.GearCount, HORIZONTAL_ALIGNMENT_RIGHT, 300, 16, C_TEXT)
	draw_string(_font, Vector2(PANEL_W - PAD - 300, 64), "in the catalogue",
		HORIZONTAL_ALIGNMENT_RIGHT, 300, 10, C_DIM)

	_draw_tabs()
	_draw_rows()
	_draw_footer()

	draw_set_transform(Vector2.ZERO, 0.0, Vector2.ONE)


func _draw_tabs() -> void:
	var x: float = PAD
	for i in range(KINDS.size()):
		var label: String = KIND_NAMES[i]
		var w: float = _font.get_string_size(label, HORIZONTAL_ALIGNMENT_LEFT, -1, 11).x + 16.0
		if i == _tab:
			draw_rect(Rect2(x, 76, w, 18), C_TAG * Color(1, 1, 1, 0.30))
		draw_string(_font, Vector2(x + 8, 89), label, HORIZONTAL_ALIGNMENT_LEFT, -1, 11,
			C_SEL if i == _tab else C_DIM)
		x += w + 4.0


func _draw_rows() -> void:
	if _items.is_empty():
		draw_string(_font, Vector2(PAD, ROWS_Y + 20), "no items of this kind",
			HORIZONTAL_ALIGNMENT_LEFT, -1, 13, C_DIM)
		return

	var last: int = mini(_items.size(), _scroll + VISIBLE_ROWS)
	var y: float = ROWS_Y
	for i in range(_scroll, last):
		var item: int = _items[i]
		var box := Rect2(PAD, y, PANEL_W - PAD * 2.0, ROW_H - 3.0)
		draw_rect(box, C_ROW)
		if i == _row:
			draw_rect(box, C_SEL, false, 2.0)

		var s: Vector2i = CAT.size_of(bridge, item)
		var owned: int = 0 if stash == null else stash.count_of(item)
		var price: int = bridge.GearPrice(item)
		draw_rect(Rect2(box.position, Vector2(3.0, box.size.y)), CAT.rarity_colour(bridge, item))
		draw_string(_font, box.position + Vector2(10, 16), bridge.GearName(item),
			HORIZONTAL_ALIGNMENT_LEFT, 220, 13, CAT.rarity_colour(bridge, item))
		draw_string(_font, box.position + Vector2(238, 16), "%dx%d" % [s.x, s.y],
			HORIZONTAL_ALIGNMENT_LEFT, 56, 10, C_DIM)
		draw_string(_font, box.position + Vector2(300, 16),
			bridge.GearSlotName(bridge.GearSlotOf(item)),
			HORIZONTAL_ALIGNMENT_LEFT, 120, 10, C_DIM)
		draw_string(_font, box.position + Vector2(428, 16), _kind_name(item),
			HORIZONTAL_ALIGNMENT_LEFT, 100, 10, C_TAG if bridge.GearIsObjective(item) else C_DIM)
		if owned > 0:
			draw_string(_font, box.position + Vector2(536, 16), "have %d" % owned,
				HORIZONTAL_ALIGNMENT_LEFT, 80, 10, C_GOOD)
		draw_string(_font, box.position + Vector2(box.size.x - 106, 16),
			"-" if price <= 0 else str(price), HORIZONTAL_ALIGNMENT_RIGHT, 96, 11, C_DIM)
		y += ROW_H

	draw_string(_font, Vector2(PAD, ROWS_Y + VISIBLE_ROWS * ROW_H + 16),
		"%d - %d of %d" % [_scroll + 1, last, _items.size()],
		HORIZONTAL_ALIGNMENT_LEFT, 300, 10, C_DIM)


## The row tag, singular, where KIND_NAMES is the plural used by the tabs.
func _kind_name(item_id: int) -> String:
	var k: int = bridge.GearKindOf(item_id)
	if k < 0 or k >= KIND_SINGULAR.size():
		return "kind %d" % k
	return KIND_SINGULAR[k]


func _draw_footer() -> void:
	if _notice_t > 0.0:
		var col: Color = _notice_col
		col.a = clampf(_notice_t, 0.0, 1.0)
		draw_string(_font, Vector2(PAD, PANEL_H - 52), _notice,
			HORIZONTAL_ALIGNMENT_LEFT, PANEL_W - PAD * 2.0, 12, col)

	draw_string(_font, Vector2(PAD, PANEL_H - 30),
		"up / down choose  ·  left / right filter  ·  ENTER spawns  ·  F8 or ESC closes",
		HORIZONTAL_ALIGNMENT_LEFT, -1, 12, C_TEXT)
	var note: String = "a run is under way · it goes into the pack on the next tick" \
		if live else "no run under way · it goes to the stash · wear it with E, then F5"
	draw_string(_font, Vector2(PAD, PANEL_H - 14), note,
		HORIZONTAL_ALIGNMENT_LEFT, -1, 10, C_DIM)
