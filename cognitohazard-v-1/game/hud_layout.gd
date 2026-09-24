extends RefCounted

## Where every HUD element sits, as data rather than as arithmetic buried in
## draw calls.
##
## The HUD used to be a wall of magic offsets -- `right - 170`, `FIELD_H + 34`,
## `FIELD_W * 0.5 - 90` -- which meant no element had a position you could name,
## let alone move. Each one now has an id, a size, and a top-left corner that
## lives here. main.gd asks for the corner and draws relative to it; the layout
## editor drags the corner around; this file loads and saves the result.
##
## Coordinates are DESIGN pixels (960x620), the same space everything else in
## game/ draws in, so they survive window resizing and fullscreen without
## conversion.
##
## It owns no drawing and no input. That is deliberate: positions are the only
## thing shared between the HUD and its editor, and keeping them here is what
## stops the two from disagreeing about where anything is.

const FIELD_W: float = 960.0
const FIELD_H: float = 560.0
const HUD_H: float = 60.0
const SCREEN_H: float = FIELD_H + HUD_H

const SAVE_PATH: String = "user://hud_layout.txt"

## Snap while dragging. Small enough to place things precisely, big enough that
## a row of elements lines up without pixel-nudging.
const GRID: float = 4.0

## id, label shown in the editor, and the box the element occupies.
##
## The SIZE is what the editor drags and what clamping keeps on screen, so it
## has to match what the element actually paints -- an element that draws
## outside its box can be dragged half off the screen and look like a bug in the
## clamp rather than a wrong number here.
const ELEMENTS: Array = [
	{"id": "records",  "label": "record strip",  "size": Vector2(460, 30)},
	{"id": "vitals",   "label": "health/armour", "size": Vector2(216, 24)},
	{"id": "weapon",   "label": "weapon + ammo", "size": Vector2(160, 32)},
	{"id": "alarm",    "label": "alarm level",   "size": Vector2(160, 14)},
	{"id": "exposure", "label": "exposure",      "size": Vector2(216, 18)},
	{"id": "score",    "label": "records score", "size": Vector2(330, 16)},
	{"id": "dilating", "label": "dilating",      "size": Vector2(90, 16)},
	{"id": "notice",   "label": "notices",       "size": Vector2(400, 14)},
	{"id": "replay",   "label": "replay saved",  "size": Vector2(400, 12)},
	{"id": "swap",     "label": "weapon swap",   "size": Vector2(120, 18)},
	{"id": "movement", "label": "move speed",    "size": Vector2(160, 26)},
	{"id": "pending",  "label": "kit staged",    "size": Vector2(160, 14)},
	{"id": "objective","label": "objective",     "size": Vector2(160, 26)},
	{"id": "light",    "label": "light (how visible you are)", "size": Vector2(160, 26)},
]

## Record slots that fit the strip's declared width. The strip used to draw up
## to nine and simply overlap whatever was to its right -- which went unnoticed
## only because a player rarely holds nine records. Its box now means what it
## says, and holding more than fits shows the NEWEST that fit, which are the
## ones about to be spent anyway.
const RECORD_SLOT_W: float = 92.0

## The shipped arrangement: fuel on the left, vitals centred, weapon and threat
## on the right, transient messages above the bar where they do not fight the
## permanent readouts for space.
## Every coordinate is a multiple of GRID. Anything else would be snapped the
## first time it went through set_pos -- which happens on load -- so a default
## off the grid means the shipped layout and the reloaded one differ by a pixel
## or two for no reason anybody could see.
const DEFAULTS: Dictionary = {
	"records":  Vector2(12, 564),
	"score":    Vector2(12, 596),
	"dilating": Vector2(352, 596),
	"vitals":   Vector2(488, 564),
	"exposure": Vector2(488, 592),
	"weapon":   Vector2(716, 560),
	"alarm":    Vector2(716, 592),
	"notice":   Vector2(12, 532),
	"replay":   Vector2(428, 532),
	"swap":     Vector2(420, 484),
	"movement": Vector2(12, 496),
	"pending":  Vector2(716, 496),
	"objective": Vector2(12, 464),
	"light":    Vector2(716, 464),
}


## How many record slots fit the strip's current box.
func record_slots() -> int:
	return maxi(1, int(size_of("records").x / RECORD_SLOT_W))

var _pos: Dictionary = {}


func _init() -> void:
	reset()


func reset() -> void:
	_pos = DEFAULTS.duplicate(true)


func ids() -> Array:
	var out: Array = []
	for e in ELEMENTS:
		out.append(e["id"])
	return out


func size_of(id: String) -> Vector2:
	for e in ELEMENTS:
		if e["id"] == id:
			return e["size"]
	return Vector2.ZERO


func label_of(id: String) -> String:
	for e in ELEMENTS:
		if e["id"] == id:
			return e["label"]
	return id


func pos_of(id: String) -> Vector2:
	return _pos.get(id, DEFAULTS.get(id, Vector2.ZERO))


func rect_of(id: String) -> Rect2:
	return Rect2(pos_of(id), size_of(id))


func is_default(id: String) -> bool:
	return pos_of(id).is_equal_approx(DEFAULTS.get(id, Vector2.ZERO))


## Move an element, snapped to the grid and clamped so no part of it leaves the
## screen. An element dragged off the edge is gone with no way to get it back
## short of a reset, so the clamp is not a nicety.
func set_pos(id: String, p: Vector2) -> void:
	var sz: Vector2 = size_of(id)
	# CLAMP FIRST, THEN SNAP DOWN. Snapping and then clamping looks equivalent
	# and is not: the right edge is FIELD_W - size.x, and an element whose width
	# is not a multiple of GRID lands there off-grid, which is the one thing
	# this function exists to prevent. Flooring after the clamp always moves the
	# element further INSIDE the screen, so it cannot undo the clamp.
	var clamped := Vector2(
		clampf(p.x, 0.0, maxf(0.0, FIELD_W - sz.x)),
		clampf(p.y, 0.0, maxf(0.0, SCREEN_H - sz.y)))
	_pos[id] = Vector2(
		maxf(0.0, floorf(clamped.x / GRID) * GRID),
		maxf(0.0, floorf(clamped.y / GRID) * GRID))


## The topmost element under `p`, or "". Walks the list backwards so the element
## drawn last -- the one visually on top -- is the one you grab.
func hit(p: Vector2) -> String:
	for i in range(ELEMENTS.size() - 1, -1, -1):
		var id: String = ELEMENTS[i]["id"]
		if rect_of(id).has_point(p):
			return id
	return ""


# ------------------------------------------------------------ persistence

func to_text() -> String:
	var lines := PackedStringArray()
	for id in ids():
		var p: Vector2 = pos_of(id)
		lines.append("%s %d %d" % [id, int(p.x), int(p.y)])
	return "\n".join(lines) + "\n"


## Total parser, like the level format: any input at all yields a usable layout.
## An unreadable or half-written file falls back to the defaults for whatever it
## could not parse, so a corrupt save costs you your arrangement and never the
## ability to see your own health bar.
func from_text(text: String) -> int:
	reset()
	if text.is_empty():
		return 0
	var applied: int = 0
	for raw in text.replace("\r", "").split("\n"):
		var line: String = raw.strip_edges()
		if line.is_empty() or line.begins_with("#"):
			continue
		var bits: PackedStringArray = line.split(" ", false)
		if bits.size() < 3:
			continue
		var id: String = bits[0]
		if not DEFAULTS.has(id):
			continue
		if not bits[1].is_valid_int() or not bits[2].is_valid_int():
			continue
		set_pos(id, Vector2(float(bits[1].to_int()), float(bits[2].to_int())))
		applied += 1
	return applied


func save() -> bool:
	var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	if f == null:
		return false
	f.store_string(to_text())
	return true


func load_saved() -> int:
	if not FileAccess.file_exists(SAVE_PATH):
		return 0
	return from_text(FileAccess.get_file_as_string(SAVE_PATH))
