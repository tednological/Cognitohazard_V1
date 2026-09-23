extends Node2D

## Weapon customisation (RPG extension plan §3A).
##
## Pick a weapon, fit attachments, see what they actually did. The resolved
## column is the point: an attachment that claims to tighten the cone shows the
## cone tightening, and one whose slot the weapon does not have simply is not
## offered.
##
## Like the editor, this owns input and pixels only. Every stat it prints comes
## back from SimBridge already resolved; it never computes a game number itself.

const FIELD_W: float = 960.0
const FIELD_H: float = 560.0
const HUD_H: float = 60.0

# Rows: weapon, armour, then one row per attachment slot.
const ROW_WEAPON: int = 0
const ROW_ARMOUR: int = 1
const ROW_SLOT0: int = 2

var active: bool = false
var bridge: RefCounted
var draws: int = 0

var _row: int = 0
var _font: Font

signal closed()

const C_BG := Color(0.035, 0.042, 0.052)
const C_PANEL := Color(0.07, 0.082, 0.10)
const C_LINE := Color(0.18, 0.20, 0.25)
const C_TEXT := Color(0.82, 0.86, 0.92)
const C_DIM := Color(0.42, 0.47, 0.54)
const C_SEL := Color(1.0, 0.95, 0.55)
const C_GOOD := Color(0.35, 0.80, 0.55)
const C_BAD := Color(0.92, 0.45, 0.40)
const C_COLD := Color(0.40, 0.76, 0.85)

# Resolved-stat indices, mirroring SimBridge.StatsOf.
const ST_DAMAGE := 0
const ST_PIERCE := 1
const ST_MAG := 2
const ST_CADENCE := 3
const ST_RELOAD := 4
const ST_SPREAD := 5
const ST_SPREAD_HEAT := 6
const ST_HEAT := 7
const ST_BULLET_SPEED := 8
const ST_BULLET_LIFE := 9
const ST_PELLETS := 10
const ST_RADIUS := 11
const ST_WALK := 12
const ST_SNEAK := 13
const ST_VISION := 14
const ST_DETECT := 15
const ST_SPREAD_SWAY := 16
const ST_TURN := 17


func _ready() -> void:
	_font = ThemeDB.fallback_font
	set_process_unhandled_input(true)


func open_menu() -> void:
	active = true
	_row = 0
	queue_redraw()


func close_menu() -> void:
	active = false
	queue_redraw()
	closed.emit()


func _process(_delta: float) -> void:
	if active:
		queue_redraw()


# ------------------------------------------------------------------- input

func _unhandled_input(event: InputEvent) -> void:
	if not active:
		return
	if not (event is InputEventKey and event.pressed and not event.echo):
		return

	match event.keycode:
		KEY_UP, KEY_W:
			_row = wrapi(_row - 1, 0, _row_count())
		KEY_DOWN, KEY_S:
			_row = wrapi(_row + 1, 0, _row_count())
		KEY_LEFT, KEY_A:
			_cycle(-1)
		KEY_RIGHT, KEY_D:
			_cycle(1)
		KEY_ESCAPE, KEY_ENTER, KEY_KP_ENTER, KEY_E:
			close_menu()
	get_viewport().set_input_as_handled()


func _row_count() -> int:
	return ROW_SLOT0 + bridge.SlotCount


## Rows for slots the equipped weapon does not have are shown greyed and refuse
## to cycle, rather than being hidden — so the menu makes the weapon's
## limitations visible instead of silently shrinking.
func _cycle(dir: int) -> void:
	if _row == ROW_WEAPON:
		bridge.SetWeapon(wrapi(bridge.CurrentWeaponId + dir, 0, bridge.WeaponCount))
	elif _row == ROW_ARMOUR:
		bridge.SetArmour(wrapi(bridge.CurrentArmourId + dir, 0, bridge.ArmourCount))
	else:
		var slot: int = _row - ROW_SLOT0
		if not bridge.WeaponHasSlot(bridge.CurrentWeaponId, slot):
			return
		var count: int = bridge.OptionCount(slot)
		bridge.SetAttachment(slot, wrapi(bridge.GetAttachment(slot) + dir, 0, count))


# ----------------------------------------------------------------- drawing

func _draw() -> void:
	if not active:
		return
	draws += 1

	draw_rect(Rect2(0, 0, FIELD_W, FIELD_H + HUD_H), C_BG)

	var weapon_id: int = bridge.CurrentWeaponId
	draw_string(_font, Vector2(24, 40), "LOADOUT", HORIZONTAL_ALIGNMENT_LEFT, -1, 22, C_TEXT)
	draw_string(_font, Vector2(24, 60),
		"%s  ·  %s" % [bridge.WeaponNameOf(weapon_id), bridge.WeaponClassOf(weapon_id)],
		HORIZONTAL_ALIGNMENT_LEFT, -1, 12, C_COLD)

	_draw_rows(weapon_id)
	_draw_stats()

	draw_string(_font, Vector2(24, FIELD_H + 38),
		"up/down choose row   left/right change   E or ENTER stage it   F5 deploys",
		HORIZONTAL_ALIGNMENT_LEFT, 640, 11, C_DIM)


func _draw_rows(weapon_id: int) -> void:
	var x: float = 24.0
	var y: float = 96.0
	var w: float = 430.0

	for row in range(_row_count()):
		var label: String
		var value: String
		var enabled: bool = true

		if row == ROW_WEAPON:
			label = "weapon"
			value = bridge.WeaponNameOf(weapon_id)
		elif row == ROW_ARMOUR:
			label = "armour"
			value = "%s  (+%d)" % [bridge.CurrentArmourName,
				bridge.ArmourValueOf(bridge.CurrentArmourId)]
		else:
			var slot: int = row - ROW_SLOT0
			label = bridge.SlotName(slot)
			enabled = bridge.WeaponHasSlot(weapon_id, slot)
			value = bridge.OptionName(slot, bridge.GetAttachment(slot)) if enabled else "—"

		var selected: bool = row == _row
		var box := Rect2(x, y, w, 26.0)
		if selected:
			draw_rect(box, C_PANEL)
			draw_rect(box, C_SEL, false, 1.0)

		var label_col: Color = C_DIM if enabled else Color(0.26, 0.28, 0.32)
		var value_col: Color = (C_SEL if selected else C_TEXT) if enabled else Color(0.30, 0.32, 0.36)

		draw_string(_font, Vector2(x + 10, y + 18), label.to_upper(),
			HORIZONTAL_ALIGNMENT_LEFT, 110, 10, label_col)
		draw_string(_font, Vector2(x + 126, y + 18), value,
			HORIZONTAL_ALIGNMENT_LEFT, w - 140, 13, value_col)

		if selected and enabled:
			draw_string(_font, Vector2(x + w - 26, y + 18), "< >",
				HORIZONTAL_ALIGNMENT_LEFT, 24, 11, C_SEL)
		if not enabled:
			draw_string(_font, Vector2(x + w - 150, y + 18), "not on this weapon",
				HORIZONTAL_ALIGNMENT_RIGHT, 144, 9, Color(0.30, 0.32, 0.36))

		y += 30.0


## Base against resolved, with the delta coloured. Some stats are better when
## they go down — spread, reload, cadence, and the gunshot radius — so each
## carries its own polarity rather than assuming bigger is better.
func _draw_stats() -> void:
	var base: PackedInt32Array = bridge.BaseStats()
	var now: PackedInt32Array = bridge.ResolvedStats()

	var x: float = 500.0
	var y: float = 96.0
	var w: float = 436.0

	draw_rect(Rect2(x, y - 26.0, w, 478.0), C_PANEL)
	draw_rect(Rect2(x, y - 26.0, w, 478.0), C_LINE, false, 1.0)
	draw_string(_font, Vector2(x + 12, y - 8), "RESOLVED", HORIZONTAL_ALIGNMENT_LEFT, -1, 11, C_DIM)
	draw_string(_font, Vector2(x + w - 180, y - 8), "base        fitted",
		HORIZONTAL_ALIGNMENT_RIGHT, 174, 10, C_DIM)

	# label, index, higher-is-better, formatter
	var rows := [
		["damage", ST_DAMAGE, true, "int"],
		["pellets", ST_PELLETS, true, "int"],
		["armour pierce", ST_PIERCE, true, "pct"],
		["magazine", ST_MAG, true, "int"],
		["rate of fire", ST_CADENCE, false, "rps"],
		["reload", ST_RELOAD, false, "sec"],
		["spread", ST_SPREAD, false, "rad"],
		["spread under fire", ST_SPREAD_HEAT, false, "rad"],
		["spread when swung", ST_SPREAD_SWAY, false, "rad"],
		["heat per shot", ST_HEAT, false, "int"],
		["handling", ST_TURN, true, "turn"],
		["muzzle velocity", ST_BULLET_SPEED, true, "px"],
		["round lifetime", ST_BULLET_LIFE, true, "sec"],
		["gunshot radius", ST_RADIUS, false, "pxr"],
		["walk speed", ST_WALK, true, "px"],
		["sneak speed", ST_SNEAK, true, "px"],
		["vision bonus", ST_VISION, true, "pxr"],
		["detection", ST_DETECT, false, "det"],
	]

	var ry: float = y + 4.0
	for row in rows:
		var label: String = row[0]
		var idx: int = row[1]
		var higher_better: bool = row[2]
		var fmt: String = row[3]

		var b: int = base[idx]
		var n: int = now[idx]

		draw_string(_font, Vector2(x + 12, ry + 12), label,
			HORIZONTAL_ALIGNMENT_LEFT, 200, 11, C_DIM)
		draw_string(_font, Vector2(x + w - 180, ry + 12), _fmt(b, fmt),
			HORIZONTAL_ALIGNMENT_RIGHT, 80, 11, Color(0.45, 0.49, 0.56))

		var col: Color = C_TEXT
		if n != b:
			var better: bool = (n > b) == higher_better
			col = C_GOOD if better else C_BAD
		draw_string(_font, Vector2(x + w - 92, ry + 12), _fmt(n, fmt),
			HORIZONTAL_ALIGNMENT_RIGHT, 80, 12, col)

		ry += 25.0


func _fmt(v: int, kind: String) -> String:
	match kind:
		"sec":
			return "%0.2fs" % (float(v) / 60.0)
		"rps":
			return "%0.1f/s" % (60.0 / maxf(1.0, float(v)))
		"rad":
			return "%0.3f" % (float(v) / 65536.0 * TAU)
		"px":
			return "%d" % (v / 256)
		"pxr":
			return "%dpx" % (v / 256)
		"pct":
			return "%d%%" % (v * 100 / 256)
		"det":
			return "%d%%" % (v * 100 / 256)
		"turn":
			# How much of the gap to the cursor the weapon closes each tick.
			return "%d%%" % (v * 100 / maxi(1, bridge.TurnDen))
		_:
			return str(v)
