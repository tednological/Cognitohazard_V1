extends RefCounted

## Spatial packing model for the campaign stash: a rectangular grid of cells
## that multi-cell items occupy, with a 90-degree turn available, so packing is
## a real decision rather than a capacity count.
##
## Pure geometry. It knows footprints, never what an item *is* -- the catalog
## and the gear slots sit on top of this in game/stash.gd. That split is the
## whole point: the packing rules are then testable headlessly, with no bridge,
## no save file and no scene.
##
## rpg_extension_plan.md §1 is binding here. The campaign layer lives OUTSIDE
## sim/, so nothing in this file is hash-feeding and sim/ must never import it.
##
## A rectangle has only two distinct footprints, so ROT_NONE and ROT_90 are the
## entire rotation space: 180 is indistinguishable from 0, and 270 from 90. That
## is a property of rectangles, not a corner cut to revisit later.

const ROT_NONE: int = 0
const ROT_90: int = 1

## "Nothing here." Used for both an empty cell and a missing placement id, so a
## caller can test either against one constant.
const NONE: int = -1

var w: int = 0
var h: int = 0

## Occupancy, row-major, w*h entries, each holding a placement id or NONE.
var _cells: PackedInt32Array = PackedInt32Array()

## Placements, as parallel arrays indexed by placement id. Removing one leaves a
## dead hole rather than shifting the others down, because _cells stores these
## ids: compacting would silently invalidate every occupied cell in the grid.
## The next place() reuses a dead hole, so the arrays do not grow forever.
var _item: PackedInt32Array = PackedInt32Array()
var _x: PackedInt32Array = PackedInt32Array()
var _y: PackedInt32Array = PackedInt32Array()
var _fw: PackedInt32Array = PackedInt32Array()
var _fh: PackedInt32Array = PackedInt32Array()
var _rot: PackedInt32Array = PackedInt32Array()
var _live: PackedInt32Array = PackedInt32Array()


func _init(width: int = 0, height: int = 0) -> void:
	resize(width, height)


## Drops every placement: a grid of a different size cannot keep them honestly.
func resize(width: int, height: int) -> void:
	w = maxi(0, width)
	h = maxi(0, height)
	_cells = PackedInt32Array()
	_cells.resize(w * h)
	_cells.fill(NONE)
	_item = PackedInt32Array()
	_x = PackedInt32Array()
	_y = PackedInt32Array()
	_fw = PackedInt32Array()
	_fh = PackedInt32Array()
	_rot = PackedInt32Array()
	_live = PackedInt32Array()


func clear() -> void:
	resize(w, h)


## The footprint a base w-by-h item covers once turned.
static func span(fw: int, fh: int, rot: int) -> Vector2i:
	return Vector2i(fh, fw) if rot == ROT_90 else Vector2i(fw, fh)


## `ignore` exempts one placement from the overlap test, which is what lets a
## move or a turn be checked against a position that overlaps where the item
## already sits.
func can_place(fw: int, fh: int, x: int, y: int, rot: int, ignore: int = NONE) -> bool:
	var s: Vector2i = span(fw, fh, rot)
	if s.x <= 0 or s.y <= 0:
		return false
	if x < 0 or y < 0 or x + s.x > w or y + s.y > h:
		return false
	for ry in range(y, y + s.y):
		for rx in range(x, x + s.x):
			var occ: int = _cells[ry * w + rx]
			if occ != NONE and occ != ignore:
				return false
	return true


## Returns the new placement id, or NONE if it does not fit.
func place(item_id: int, fw: int, fh: int, x: int, y: int, rot: int = ROT_NONE) -> int:
	if not can_place(fw, fh, x, y, rot):
		return NONE
	var pi: int = _claim_slot()
	_item[pi] = item_id
	_x[pi] = x
	_y[pi] = y
	_fw[pi] = fw
	_fh[pi] = fh
	_rot[pi] = rot
	_live[pi] = 1
	_stamp(pi, pi)
	return pi


## First position the item fits, scanned top-left first and preferring the
## unturned footprint at each cell. Returns [x, y, rot], or [] if it never fits.
## Scan order is fixed so the same stash always packs the same way.
func find_fit(fw: int, fh: int) -> Array:
	for y in range(h):
		for x in range(w):
			for rot in [ROT_NONE, ROT_90]:
				if can_place(fw, fh, x, y, rot):
					return [x, y, rot]
	return []


func auto_place(item_id: int, fw: int, fh: int) -> int:
	var at: Array = find_fit(fw, fh)
	if at.is_empty():
		return NONE
	return place(item_id, fw, fh, at[0], at[1], at[2])


func remove(pi: int) -> bool:
	if not is_live(pi):
		return false
	_stamp(pi, NONE)
	_live[pi] = 0
	return true


## Turns in place about the item's top-left cell. Fails, changing nothing, if
## the turned footprint would not fit there.
func rotate_placement(pi: int) -> bool:
	if not is_live(pi):
		return false
	var want: int = ROT_NONE if _rot[pi] == ROT_90 else ROT_90
	return move(pi, _x[pi], _y[pi], want)


func move(pi: int, x: int, y: int, rot: int) -> bool:
	if not is_live(pi):
		return false
	if not can_place(_fw[pi], _fh[pi], x, y, rot, pi):
		return false
	_stamp(pi, NONE)
	_x[pi] = x
	_y[pi] = y
	_rot[pi] = rot
	_stamp(pi, pi)
	return true


func placement_at(x: int, y: int) -> int:
	if x < 0 or y < 0 or x >= w or y >= h:
		return NONE
	return _cells[y * w + x]


## How many placement slots exist. Not how many are USED -- iterate and test
## is_live for that.
func capacity() -> int:
	return _live.size()


func is_live(pi: int) -> bool:
	return pi >= 0 and pi < _live.size() and _live[pi] == 1


func item_of(pi: int) -> int:
	return _item[pi] if is_live(pi) else NONE


func pos_of(pi: int) -> Vector2i:
	return Vector2i(_x[pi], _y[pi]) if is_live(pi) else Vector2i.ZERO


func rot_of(pi: int) -> int:
	return _rot[pi] if is_live(pi) else ROT_NONE


func base_of(pi: int) -> Vector2i:
	return Vector2i(_fw[pi], _fh[pi]) if is_live(pi) else Vector2i.ZERO


func span_of(pi: int) -> Vector2i:
	return span(_fw[pi], _fh[pi], _rot[pi]) if is_live(pi) else Vector2i.ZERO


## Live placement ids, ascending. By index, never by iterating a dictionary, so
## the order is the same on every machine and the save file round-trips.
func live_ids() -> PackedInt32Array:
	var out: PackedInt32Array = PackedInt32Array()
	for pi in range(_live.size()):
		if _live[pi] == 1:
			out.append(pi)
	return out


func count() -> int:
	return live_ids().size()


func used_cells() -> int:
	var n: int = 0
	for i in range(_cells.size()):
		if _cells[i] != NONE:
			n += 1
	return n


func free_cells() -> int:
	return w * h - used_cells()


## Text, not binary, for the same reason the levels are: a save you can read in
## a diff is a save you can debug. The footprint travels with each line so the
## grid reloads standalone; reconciling against a catalog that has since changed
## an item's size is the stash layer's job, not the grid's.
func to_text() -> String:
	var lines: PackedStringArray = PackedStringArray()
	lines.append("grid %d %d" % [w, h])
	for pi in live_ids():
		lines.append("item %d %d %d %d %d %d"
			% [_item[pi], _x[pi], _y[pi], _fw[pi], _fh[pi], _rot[pi]])
	return "\n".join(lines) + "\n"


## Total, the way the level parser is total: a malformed or unplaceable line is
## skipped, never fatal, so a hand-edited or outdated save still loads. Returns
## the number of lines it could not honour.
func from_text(text: String) -> int:
	resize(0, 0)
	var skipped: int = 0
	for raw in text.split("\n"):
		var line: String = raw.strip_edges()
		if line.is_empty() or line.begins_with("#"):
			continue
		var f: PackedStringArray = line.split(" ", false)
		if f[0] == "grid" and f.size() >= 3:
			resize(int(f[1]), int(f[2]))
		elif f[0] == "item" and f.size() >= 7:
			if place(int(f[1]), int(f[4]), int(f[5]), int(f[2]), int(f[3]), int(f[6])) == NONE:
				skipped += 1
		else:
			skipped += 1
	return skipped


func _claim_slot() -> int:
	for pi in range(_live.size()):
		if _live[pi] == 0:
			return pi
	_item.append(0)
	_x.append(0)
	_y.append(0)
	_fw.append(0)
	_fh.append(0)
	_rot.append(ROT_NONE)
	_live.append(0)
	return _live.size() - 1


func _stamp(pi: int, value: int) -> void:
	var s: Vector2i = span(_fw[pi], _fh[pi], _rot[pi])
	for ry in range(_y[pi], _y[pi] + s.y):
		for rx in range(_x[pi], _x[pi] + s.x):
			_cells[ry * w + rx] = value
