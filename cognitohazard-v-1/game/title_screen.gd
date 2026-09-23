extends Node2D

## The first thing the game shows. Four doors and nothing else.
##
## It exists because the game used to open straight onto a loadout menu, which
## answered "what am I carrying" before it answered "am I continuing something".
## Those are different questions and the second one comes first.
##
## It owns no state. Whether a save EXISTS is a fact about the disk, so it asks
## the disk; what to do about it is main.gd's business, which is why every row
## here is a signal rather than an action.

const CAMPAIGN := preload("res://game/campaign.gd")

const FIELD_W: float = 960.0
const FIELD_H: float = 560.0
const SCREEN_H: float = FIELD_H + 60.0

const ROW_H: float = 46.0
const ROWS_Y: float = 300.0
const ROW_W: float = 380.0

var active: bool = false
var campaign: RefCounted
var draws: int = 0

## The build this is, as project.godot states it -- read rather than repeated,
## so bumping the version is one edit. A bug report that cannot name a build is
## a bug report about an unknown game, which is why it is on the first screen
## instead of behind Options.
var _version: String = ""

var _row: int = 0
var _font: Font

## Rows are fixed; "continue" is DISABLED rather than hidden when there is no
## save. A menu whose rows move depending on state is a menu you cannot learn.
const ROW_CONTINUE: int = 0
const ROW_NEW: int = 1
const ROW_OPTIONS: int = 2
const ROW_BUILDER: int = 3
const ROW_COUNT: int = 4

signal continue_requested()
signal new_game_requested()
signal options_requested()
signal builder_requested()

const C_BG := Color(0.030, 0.036, 0.045)
const C_TEXT := Color(0.86, 0.90, 0.95)
const C_DIM := Color(0.40, 0.45, 0.52)
const C_OFF := Color(0.26, 0.29, 0.34)
const C_SEL := Color(1.0, 0.95, 0.55)
const C_ROW := Color(0.07, 0.083, 0.10)
const C_RULE := Color(0.20, 0.23, 0.29)


func _ready() -> void:
	_font = ThemeDB.fallback_font
	_version = str(ProjectSettings.get_setting("application/config/version", ""))
	visible = false
	z_index = 300


func open_screen() -> void:
	# Land on Continue when there is something to continue, and on New Game
	# when there is not, so ENTER is always the sensible thing.
	_row = ROW_CONTINUE if has_save() else ROW_NEW
	active = true
	visible = true
	queue_redraw()


func close_screen() -> void:
	active = false
	visible = false


## A save is a campaign file on disk. The stash is not enough on its own -- it
## exists from the first time the equipment screen is opened.
func has_save() -> bool:
	return FileAccess.file_exists(CAMPAIGN.SAVE_PATH)


func row_enabled(row: int) -> bool:
	return has_save() if row == ROW_CONTINUE else true


func move(delta: int) -> void:
	# Skip anything disabled rather than letting the cursor rest on it.
	for _i in range(ROW_COUNT):
		_row = (_row + delta + ROW_COUNT) % ROW_COUNT
		if row_enabled(_row):
			break
	queue_redraw()


func confirm() -> void:
	if not row_enabled(_row):
		return
	match _row:
		ROW_CONTINUE: continue_requested.emit()
		ROW_NEW: new_game_requested.emit()
		ROW_OPTIONS: options_requested.emit()
		ROW_BUILDER: builder_requested.emit()


func _process(_delta: float) -> void:
	if active:
		queue_redraw()


func _draw() -> void:
	if not active:
		return
	draws += 1

	draw_rect(Rect2(0, 0, FIELD_W, SCREEN_H), C_BG)

	draw_string(_font, Vector2(0, 176), "COGNITOHAZARD",
		HORIZONTAL_ALIGNMENT_CENTER, FIELD_W, 56, C_TEXT)
	draw_string(_font, Vector2(0, 208), "take the evidence, and get out",
		HORIZONTAL_ALIGNMENT_CENTER, FIELD_W, 14, C_DIM)
	draw_line(Vector2(FIELD_W * 0.5 - 180, 236), Vector2(FIELD_W * 0.5 + 180, 236),
		C_RULE, 1.0)

	var labels := ["Continue previous save", "Start new game", "Options",
		"Level Builder"]
	for i in range(ROW_COUNT):
		_draw_row(i, labels[i])

	if campaign != null and has_save():
		draw_string(_font, Vector2(0, SCREEN_H - 40),
			"%d on hand  ·  %d run(s), %d completed" % [
				campaign.money, campaign.runs, campaign.extractions],
			HORIZONTAL_ALIGNMENT_CENTER, FIELD_W, 12, C_DIM)

	draw_string(_font, Vector2(0, SCREEN_H - 18),
		"up / down choose  ·  ENTER selects",
		HORIZONTAL_ALIGNMENT_CENTER, FIELD_W, 12, C_DIM)

	if not _version.is_empty():
		draw_string(_font, Vector2(14, SCREEN_H - 18), "v" + _version,
			HORIZONTAL_ALIGNMENT_LEFT, 140, 12, C_OFF)


func _draw_row(i: int, label: String) -> void:
	var on: bool = row_enabled(i)
	var box := Rect2(FIELD_W * 0.5 - ROW_W * 0.5, ROWS_Y + i * ROW_H,
		ROW_W, ROW_H - 8.0)
	draw_rect(box, C_ROW)
	if i == _row:
		draw_rect(box, C_SEL, false, 2.0)

	var col: Color = C_OFF
	if on:
		col = C_SEL if i == _row else C_TEXT
	draw_string(_font, box.position + Vector2(0, 25), label,
		HORIZONTAL_ALIGNMENT_CENTER, box.size.x, 17, col)

	if i == ROW_CONTINUE and not on:
		draw_string(_font, box.position + Vector2(box.size.x - 110, 25), "no save",
			HORIZONTAL_ALIGNMENT_RIGHT, 100, 11, C_OFF)
