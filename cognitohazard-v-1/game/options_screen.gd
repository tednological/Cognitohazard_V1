extends Node2D

## Options. Deliberately short: everything here is something a player might
## actually want to change, and nothing here is a setting invented to make the
## screen look fuller.
##
## Each row is a toggle or an action, not a slider, because every one of these
## is a yes/no. It owns no state either -- it reads and writes the things that
## already own it (the audio node, the window, the campaign file).

const CAMPAIGN := preload("res://game/campaign.gd")
const HUD_LAYOUT := preload("res://game/hud_layout.gd")

const FIELD_W: float = 960.0
const FIELD_H: float = 560.0
const SCREEN_H: float = FIELD_H + 60.0

const ROW_H: float = 44.0
const ROWS_Y: float = 150.0
const ROW_W: float = 560.0

const ROW_SOUND: int = 0
const ROW_FULLSCREEN: int = 1
const ROW_HUD: int = 2
const ROW_WIPE: int = 3
const ROW_COUNT: int = 4

var active: bool = false
var audio: Node
var campaign: RefCounted
var draws: int = 0

var _row: int = 0
var _font: Font
var _notice: String = ""
var _notice_t: float = 0.0

## Wiping a campaign is the one irreversible thing on this screen, so it takes
## two presses: the row arms, and the next ENTER does it.
var _wipe_armed: bool = false

signal closed()
signal hud_editor_requested()
signal campaign_wiped()

const C_BG := Color(0.030, 0.036, 0.045)
const C_TEXT := Color(0.86, 0.90, 0.95)
const C_DIM := Color(0.40, 0.45, 0.52)
const C_SEL := Color(1.0, 0.95, 0.55)
const C_ROW := Color(0.07, 0.083, 0.10)
const C_GOOD := Color(0.35, 0.80, 0.55)
const C_BAD := Color(0.92, 0.45, 0.40)


func _ready() -> void:
	_font = ThemeDB.fallback_font
	visible = false
	z_index = 300


func open_screen() -> void:
	_row = 0
	_wipe_armed = false
	active = true
	visible = true
	queue_redraw()


## Closing because the PLAYER backed out. `closed` means exactly that, and its
## handler returns to the title.
func close_screen() -> void:
	close_screen_silent()
	closed.emit()


## Closing because something else is taking over the screen -- currently only
## the HUD editor. Leaving through here must NOT emit `closed`, or the title
## would open underneath at z 300 and draw over the editor's z 200 while the
## editor still held the keyboard.
func close_screen_silent() -> void:
	active = false
	visible = false
	_wipe_armed = false


func move(delta: int) -> void:
	_row = (_row + delta + ROW_COUNT) % ROW_COUNT
	# Moving off the wipe row disarms it: an armed destructive action must not
	# survive you looking at something else.
	_wipe_armed = false
	queue_redraw()


func confirm() -> void:
	match _row:
		ROW_SOUND:
			if audio != null:
				audio.enabled = not audio.enabled
				_notify("sound %s" % ("on" if audio.enabled else "off"), C_GOOD)
		ROW_FULLSCREEN:
			var full: bool = DisplayServer.window_get_mode() \
				== DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN
			DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED if full
				else DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN)
			_notify("fullscreen %s" % ("off" if full else "on"), C_GOOD)
		ROW_HUD:
			hud_editor_requested.emit()
		ROW_WIPE:
			if not _wipe_armed:
				_wipe_armed = true
				_notify("press ENTER again to erase everything", C_BAD)
			else:
				_wipe_armed = false
				campaign_wiped.emit()
				_notify("campaign erased", C_BAD)


func _notify(text: String, col: Color) -> void:
	_notice = text
	_notice_col = col
	_notice_t = 3.0


var _notice_col: Color = C_GOOD


func _process(delta: float) -> void:
	if _notice_t > 0.0:
		_notice_t -= delta
	if active:
		queue_redraw()


func _draw() -> void:
	if not active:
		return
	draws += 1

	draw_rect(Rect2(0, 0, FIELD_W, SCREEN_H), C_BG)
	draw_string(_font, Vector2(0, 100), "OPTIONS", HORIZONTAL_ALIGNMENT_CENTER,
		FIELD_W, 32, C_TEXT)

	var sound_on: bool = audio == null or audio.enabled
	var full: bool = DisplayServer.window_get_mode() \
		== DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN

	_draw_row(ROW_SOUND, "Sound", "on" if sound_on else "off",
		C_GOOD if sound_on else C_DIM)
	_draw_row(ROW_FULLSCREEN, "Fullscreen", "on" if full else "off",
		C_GOOD if full else C_DIM)
	_draw_row(ROW_HUD, "Arrange the HUD", "F4 in a mission", C_DIM)
	_draw_row(ROW_WIPE, "Erase campaign",
		"ENTER again to confirm" if _wipe_armed else _wipe_summary(),
		C_BAD if _wipe_armed else C_DIM)

	if _notice_t > 0.0:
		var col: Color = _notice_col
		col.a = clampf(_notice_t, 0.0, 1.0)
		draw_string(_font, Vector2(0, ROWS_Y + ROW_COUNT * ROW_H + 40), _notice,
			HORIZONTAL_ALIGNMENT_CENTER, FIELD_W, 13, col)

	draw_string(_font, Vector2(0, SCREEN_H - 18),
		"up / down choose  ·  ENTER toggles  ·  ESC goes back",
		HORIZONTAL_ALIGNMENT_CENTER, FIELD_W, 12, C_DIM)


func _wipe_summary() -> String:
	if campaign == null:
		return "money, stash and mission history"
	return "%d on hand, %d run(s)" % [campaign.money, campaign.runs]


func _draw_row(i: int, label: String, value: String, value_col: Color) -> void:
	var box := Rect2(FIELD_W * 0.5 - ROW_W * 0.5, ROWS_Y + i * ROW_H,
		ROW_W, ROW_H - 8.0)
	draw_rect(box, C_ROW)
	if i == _row:
		draw_rect(box, C_SEL, false, 2.0)
	draw_string(_font, box.position + Vector2(16, 24), label,
		HORIZONTAL_ALIGNMENT_LEFT, 300, 15, C_SEL if i == _row else C_TEXT)
	draw_string(_font, box.position + Vector2(box.size.x - 260, 24), value,
		HORIZONTAL_ALIGNMENT_RIGHT, 244, 12, value_col)
