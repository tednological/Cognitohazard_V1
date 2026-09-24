extends RefCounted
## The level dressed in an Astra map kit (`assets/tilesets/<theme>/`,
## `Astra Assets/TILESETS.md`): textured floors, walls drawn by their
## connection mask, equipment on the free-standing blocks, doors, a conduit
## round the perimeter, floor markings.
##
## PRESENTATION ONLY, and more strictly than that: it DERIVES everything from
## the grid the sim already has and ADDS NO SHAPE. The contract, which
## `tests/level_art_check.gd` asserts over every shipped level and fuzzed grids:
##   - anything that LOOKS solid (wall, plinth, prop, pipe) is drawn only on
##     '#' cells, and every '#' cell looks solid -- art never hides a wall or
##     invents one;
##   - anything drawn on a walkable cell is flat (floor texture, a marking);
##   - no lit fixture is placed for decoration. Light is a stealth axis
##     (cognitohazard_lighting_plan.md), so a lamp is drawn only where the sim
##     says there is one, through `draw_lamp`.
## It never reaches an InputFrame, never touches the state hash, and the
## procedural drawing in main.gd stays as the fallback when a kit is missing
## (the headless harnesses have no renderer to read textures back from).
##
## `build()` is pure data from the grid and the kit's JSON manifest, so it runs
## headless; textures are only needed to draw.

const TILESET_DIR := "res://assets/tilesets/"
const THEMES: PackedStringArray = ["industrial", "scientific"]
## A level with no `theme:` line, or one naming a kit that does not exist.
const DEFAULT_THEME := "industrial"

const CELL: float = 20.0
## Kit textures are 4x world size (TILESETS.md), so a texel is 0.25 world px.
const TEX_SCALE: float = 4.0
## Commands are bucketed by CHUNK x CHUNK cells, so a frame visits only the
## chunks in view: a 144x84 floor is ~4,000 wall cells, a view ~300.
const CHUNK: int = 8

## Connection masks, TILESETS.md: N=1, E=2, S=4, W=8.
const N: int = 1
const E: int = 2
const S: int = 4
const W: int = 8

## Orientation codes for a sprite: quarter turns clockwise in the low two
## bits, then flips. Applied flips first, then the turn.
const ROT_MASK: int = 3
const FLIP_X: int = 4
const FLIP_Y: int = 8

## Draw layers. Floor ones go UNDER the unlit overlay (they dim with the floor
## outside the player's view); structure ones go over it, at a constant tone,
## as walls always have -- the map itself is not a secret.
const L_FLOOR: int = 0
const L_DECAL: int = 1
const L_BASE: int = 2
const L_WALL: int = 3
const L_PROP: int = 4
const L_PIPE: int = 5
const LAYERS: int = 6

## How far outside the vision polygon the floor is darkened. The kit's floors
## sit near the old C_FLOOR_LIT tone, and C_FLOOR / C_FLOOR_LIT is ~0.59, so
## 0.42 keeps the lit/unlit split the procedural floor had.
const UNLIT_ALPHA: float = 0.42
## Far enough to cover any level from anywhere in it (MaxDim 512 cells).
const UNLIT_FAR: float = 20000.0

## Wall rim tone per kit, measured off the tiles' own bevel: the 3 px margin a
## wall tile leaves round its cap is filled with it, so a wall still covers
## its whole 20 px cell -- the size of what blocks a bullet.
const WALL_BASE := {
	"industrial": Color8(40, 46, 49),
	"scientific": Color8(43, 59, 66),
}
## A wall tile leaves a 3 px margin on an unconnected side (the base fill
## covers it). The corner patch closes the notch between two arms inside a solid mass:
## the notch (3 px) AND the 2 px bevel round it, or a solid mass shows a grid
## of little squares where its cells meet.
const CORNER_PATCH: float = 5.0

## Props whose art fits a ONE-cell-deep bench footprint (content under ~21 px
## tall), for 2x1 blocks. Measured from the PNGs' alpha; `level_art_check`
## asserts each exists in its manifest.
const NARROW_PROPS := {
	"industrial": ["control_console", "workbench", "pump"],
	"scientific": ["lab_bench", "sample_rack", "analysis_console"],
}

## Floors by kind of space. Weighted by repetition.
const ROOM_FLOORS: PackedStringArray = ["floor_plain", "floor_plain", "floor_plain",
	"floor_worn", "floor_worn", "floor_inset", "floor_raised", "floor_plates",
	"floor_conduit", "floor_drain"]
const CORRIDOR_FLOORS: PackedStringArray = ["floor_grate", "floor_plates"]

## The shortest run of corridor lane worth painting, in cells.
const LANE_MIN: int = 6

## sim Level.MaxExits: exit blobs past it are floor, so they get no frame.
const MAX_EXITS: int = 8

## A valve every so many cells of conduit.
const VALVE_EVERY: int = 13

## Kit cache, per theme, for the whole session: textures are built once.
## {theme: {"ok", "assets": {id: entry}, "props": [ids], "tex": {key: Texture2D},
##  "glow": {key: Texture2D}, "img": {id: Image}, "gimg": {id: Image}, "tex_ok"}}
static var _kits: Dictionary = {}

var theme: String = DEFAULT_THEME
## True when the kit's manifest parsed and the level was built from it.
var built: bool = false
## True when `built` AND the textures exist: main.gd draws the kit, else falls
## back to primitives.
var ready: bool = false

var cols: int = 0
var rows: int = 0
var _grid: PackedByteArray = PackedByteArray()
var _chunk_cols: int = 0
var _chunk_rows: int = 0
## Per chunk, per layer: Array of commands [key, dest Rect2, src Rect2, glow_key].
## A key of "" is a flat fill of `WALL_BASE` over dest.
var _chunks: Array = []
var _base: Color = WALL_BASE[DEFAULT_THEME]

## Build-time facts the harness reads back. Each is a list of Rect2 in world px.
var solid_rects: Array[Rect2] = []   # everything drawn as solid (walls, plinths)
var prop_rects: Array[Rect2] = []    # each prop sprite's destination
var pipe_rects: Array[Rect2] = []
var decal_rects: Array[Rect2] = []   # markings, on floor
## Region label per cell (-1 for wall) and the floor id per region.
var region_of: PackedInt32Array = PackedInt32Array()
var region_floor: PackedStringArray = PackedStringArray()


# ------------------------------------------------------------------ the kit

## The kit's manifest, parsed. Pure data: works headless.
static func manifest(theme_name: String) -> Dictionary:
	if _kits.has(theme_name):
		return _kits[theme_name]
	var kit := {"ok": false, "assets": {}, "props": [], "tex": {}, "glow": {},
		"img": {}, "gimg": {}, "tex_ok": false, "tex_tried": false}
	_kits[theme_name] = kit
	var path: String = TILESET_DIR + theme_name + ".json"
	if not FileAccess.file_exists(path):
		return kit
	var data: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	if typeof(data) != TYPE_DICTIONARY or not (data as Dictionary).has("assets"):
		return kit
	for a: Variant in data["assets"]:
		if typeof(a) != TYPE_DICTIONARY or not (a as Dictionary).has("id"):
			continue
		kit["assets"][a["id"]] = a
		if a.get("category", "") == "prop":
			kit["props"].append(a["id"])
	# The pieces the renderer cannot do without. A kit missing one is not a
	# kit; the level draws procedurally rather than with holes in it.
	for need in required_ids():
		if not kit["assets"].has(need):
			return kit
	kit["ok"] = kit["props"].size() > 0
	return kit


## Every asset id `build()` can name, props aside.
static func required_ids() -> PackedStringArray:
	var ids: PackedStringArray = ["door_closed", "door_open", "light_strip",
		"light_off", "light_broken", "marking_corner", "marking_lane",
		"pipe_straight", "pipe_elbow", "pipe_tee", "pipe_cross", "pipe_valve"]
	for m in 16:
		ids.append("wall_%02d" % m)
	for f in ROOM_FLOORS + CORRIDOR_FLOORS:
		if not ids.has(f):
			ids.append(f)
	return ids


## `theme:` as authored, resolved to a kit that exists.
static func resolve_theme(authored: String) -> String:
	return authored if THEMES.has(authored) else DEFAULT_THEME


## Load the kit's textures once. False headless, where the dummy renderer has
## no pixels to hand back.
static func _load_textures(theme_name: String) -> bool:
	var kit: Dictionary = manifest(theme_name)
	if not kit["ok"]:
		return false
	if kit["tex_tried"]:
		return kit["tex_ok"]
	kit["tex_tried"] = true
	for id: String in kit["assets"]:
		var a: Dictionary = kit["assets"][id]
		var maps: Dictionary = a.get("maps", {})
		var albedo: Image = _read_image(TILESET_DIR + String(maps.get("albedo", "")))
		if albedo == null:
			return false
		kit["img"][id] = albedo
		var cat: String = a.get("category", "")
		if cat in ["door", "light", "prop"]:
			var glow: Image = _glow_image(_read_image(TILESET_DIR + String(maps.get("emission", ""))))
			if glow != null:
				kit["gimg"][id] = glow
	kit["tex_ok"] = true
	return true


static func _read_image(path: String) -> Image:
	if not ResourceLoader.exists(path):
		return null
	var t: Texture2D = load(path) as Texture2D
	if t == null:
		return null
	var img: Image = t.get_image()
	if img == null or img.is_empty():
		return null
	if img.is_compressed():
		img.decompress()
	if img.has_mipmaps():
		img.clear_mipmaps()
	img.convert(Image.FORMAT_RGBA8)
	return img


## The emission map as something ALPHA blending can draw: each texel keeps
## its hue at full brightness and carries its brightness as alpha, so drawn
## over the albedo it approximates the additive "add emission after diffuse"
## TILESETS.md asks for, on a canvas item that has one blend mode for the HUD
## and the world alike. Half resolution: it is glow, not detail. Null when the
## map is black, which is most of the kit.
static func _glow_image(src: Image) -> Image:
	if src == null:
		return null
	var img: Image = src.duplicate()
	img.resize(maxi(1, img.get_width() / 2), maxi(1, img.get_height() / 2), Image.INTERPOLATE_BILINEAR)
	var px: PackedByteArray = img.get_data()
	var lit: bool = false
	var i: int = 0
	while i < px.size():
		var m: int = maxi(px[i], maxi(px[i + 1], px[i + 2]))
		var a: int = (m * px[i + 3]) / 255
		if a < 3:
			px[i + 3] = 0
		else:
			lit = true
			px[i] = (px[i] * 255) / m
			px[i + 1] = (px[i + 1] * 255) / m
			px[i + 2] = (px[i + 2] * 255) / m
			px[i + 3] = a
		i += 4
	if not lit:
		return null
	var out: Image = Image.create_from_data(img.get_width(), img.get_height(), false,
		Image.FORMAT_RGBA8, px)
	out.fix_alpha_edges()
	return out


## The texture for `id` turned by orientation `o`, built on first use. A key
## of "id@o" names it in commands. Mipmapped and linear-filtered PER TEXTURE
## (a CanvasTexture), because the canvas item they are drawn on also draws the
## HUD's text, which must not change filter to suit the floor.
static func _texture(theme_name: String, key: String, glow: bool) -> Texture2D:
	var kit: Dictionary = manifest(theme_name)
	var cache: Dictionary = kit["glow" if glow else "tex"]
	if cache.has(key):
		return cache[key]
	var at: int = key.find("@")
	var id: String = key if at < 0 else key.substr(0, at)
	var o: int = 0 if at < 0 else key.substr(at + 1).to_int()
	var images: Dictionary = kit["gimg" if glow else "img"]
	var t: Texture2D = null
	if images.has(id):
		var img: Image = (images[id] as Image).duplicate()
		if o & FLIP_X:
			img.flip_x()
		if o & FLIP_Y:
			img.flip_y()
		match o & ROT_MASK:
			1: img.rotate_90(CLOCKWISE)
			2: img.rotate_180()
			3: img.rotate_90(COUNTERCLOCKWISE)
		img.generate_mipmaps()
		var ct := CanvasTexture.new()
		ct.diffuse_texture = ImageTexture.create_from_image(img)
		ct.texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS
		# Floors are drawn as whole runs of cells with world-space UVs.
		ct.texture_repeat = CanvasItem.TEXTURE_REPEAT_ENABLED if id.begins_with("floor_") \
			else CanvasItem.TEXTURE_REPEAT_DISABLED
		t = ct
	cache[key] = t
	return t


# ------------------------------------------------------------------ building

func _init() -> void:
	pass


## Dress a level. `grid` is glyph bytes row-major (SimBridge.GetGrid).
## Returns `ready`.
func build(grid: PackedByteArray, w: int, h: int, authored_theme: String) -> bool:
	theme = resolve_theme(authored_theme)
	cols = w
	rows = h
	_grid = grid
	built = false
	ready = false
	_chunks = []
	solid_rects.clear()
	prop_rects.clear()
	pipe_rects.clear()
	decal_rects.clear()
	region_of = PackedInt32Array()
	region_floor = PackedStringArray()
	var kit: Dictionary = manifest(theme)
	if not kit["ok"] or w <= 0 or h <= 0 or grid.size() < w * h:
		return false
	_base = WALL_BASE.get(theme, WALL_BASE[DEFAULT_THEME])

	_chunk_cols = (w + CHUNK - 1) / CHUNK
	_chunk_rows = (h + CHUNK - 1) / CHUNK
	for i in _chunk_cols * _chunk_rows:
		var layers: Array = []
		for l in LAYERS:
			layers.append([])
		_chunks.append(layers)

	var covered: PackedByteArray = PackedByteArray()
	covered.resize(w * h)
	_place_props(kit, covered)
	_place_walls(covered)
	_place_floors()
	_place_markings()
	_place_conduit()

	built = true
	ready = _load_textures(theme)
	return ready


func _at(c: int, r: int) -> int:
	if c < 0 or r < 0 or c >= cols or r >= rows:
		return 0
	return _grid[r * cols + c]


func is_wall(c: int, r: int) -> bool:
	return _at(c, r) == 35   # '#'


func _is_panel(c: int, r: int) -> bool:
	var g: int = _at(c, r)
	return g == 43 or g == 61   # '+' '='


func _add(c: int, r: int, layer: int, key: String, dest: Rect2, src: Rect2,
		glow_key: String = "") -> void:
	var cc: int = clampi(c, 0, cols - 1) / CHUNK
	var cr: int = clampi(r, 0, rows - 1) / CHUNK
	_chunks[cr * _chunk_cols + cc][layer].append([key, dest, src, glow_key])


func _cell_rect(c: int, r: int, cw: int = 1, ch: int = 1) -> Rect2:
	return Rect2(c * CELL, r * CELL, cw * CELL, ch * CELL)


## The texel size of a kit asset, turned by orientation `o`.
func _tex_size(id: String, o: int) -> Vector2:
	var a: Dictionary = manifest(theme)["assets"].get(id, {})
	var ws: Array = a.get("world_size", [CELL, CELL])
	var s := Vector2(float(ws[0]), float(ws[1])) * TEX_SCALE
	return Vector2(s.y, s.x) if (o & 1) else s


func _full(id: String, o: int) -> Rect2:
	return Rect2(Vector2.ZERO, _tex_size(id, o))


func _key(id: String, o: int) -> String:
	return id if o == 0 else "%s@%d" % [id, o]


func _has_glow(id: String) -> bool:
	var a: Dictionary = manifest(theme)["assets"].get(id, {})
	return a.get("category", "") in ["door", "light", "prop"]


## Deterministic: the same level dresses the same way every time it loads, on
## every machine, with nothing drawn from a clock or a RNG.
static func _hash(a: int, b: int, salt: int) -> int:
	var x: int = (a * 73856093) ^ (b * 19349663) ^ (salt * 83492791)
	x = (x ^ (x >> 13)) * 1274126177
	return (x ^ (x >> 16)) & 0x7fffffff


func _theme_salt() -> int:
	return 7 if theme == "scientific" else 3


## FREE-STANDING BLOCKS become equipment: a '#' component that touches neither
## the border nor a door or pane, and is an exact rectangle an even number of
## cells on a side (one row of 2x2 props per two rows), or a one-cell-deep
## bench two or four long. It is still a wall to the sim, so it is drawn on a
## plinth that covers its whole footprint: the collision you walk into is the
## box you can see.
func _place_props(kit: Dictionary, covered: PackedByteArray) -> void:
	var seen: PackedByteArray = PackedByteArray()
	seen.resize(cols * rows)
	var props: Array = kit["props"]
	var narrow: Array = []
	for n: String in NARROW_PROPS.get(theme, []):
		if kit["assets"].has(n):
			narrow.append(n)
	var salt: int = _theme_salt()
	for r in rows:
		for c in cols:
			var i: int = r * cols + c
			if seen[i] or not is_wall(c, r):
				continue
			# Flood the component.
			var cells: Array[Vector2i] = []
			var stack: Array[Vector2i] = [Vector2i(c, r)]
			seen[i] = 1
			var free: bool = true
			var lo := Vector2i(c, r)
			var hi := Vector2i(c, r)
			while not stack.is_empty():
				var p: Vector2i = stack.pop_back()
				cells.append(p)
				lo = Vector2i(mini(lo.x, p.x), mini(lo.y, p.y))
				hi = Vector2i(maxi(hi.x, p.x), maxi(hi.y, p.y))
				if p.x == 0 or p.y == 0 or p.x == cols - 1 or p.y == rows - 1:
					free = false
				for d: Vector2i in [Vector2i(1, 0), Vector2i(-1, 0), Vector2i(0, 1), Vector2i(0, -1)]:
					var q: Vector2i = p + d
					if _is_panel(q.x, q.y):
						free = false
					if is_wall(q.x, q.y) and not seen[q.y * cols + q.x]:
						seen[q.y * cols + q.x] = 1
						stack.append(q)
			if not free:
				continue
			var bw: int = hi.x - lo.x + 1
			var bh: int = hi.y - lo.y + 1
			if cells.size() != bw * bh:
				continue
			var square: bool = bw >= 2 and bh >= 2 and bw % 2 == 0 and bh % 2 == 0
			var bench: bool = narrow.size() > 0 and ((bh == 1 and (bw == 2 or bw == 4)) \
				or (bw == 1 and (bh == 2 or bh == 4)))
			if not square and not bench:
				continue

			var block: Rect2 = _cell_rect(lo.x, lo.y, bw, bh)
			_add(lo.x, lo.y, L_PROP, "", block, Rect2())
			solid_rects.append(block)
			for p in cells:
				covered[p.y * cols + p.x] = 1

			var pick: int = _hash(lo.x, lo.y, salt)
			if square:
				for by in range(0, bh, 2):
					for bx in range(0, bw, 2):
						var id: String = props[(pick + by * 5 + bx * 3) % props.size()]
						pick += 1
						var dest: Rect2 = _cell_rect(lo.x + bx, lo.y + by, 2, 2)
						_add(lo.x, lo.y, L_PROP, id, dest, _full(id, 0),
							id if _has_glow(id) else "")
						prop_rects.append(dest)
			else:
				# A bench: the middle band of the art, which is all of it for
				# these props, turned to lie along the block.
				var along_y: bool = bw == 1
				var o: int = 1 if along_y else 0
				var n: int = maxi(bw, bh) / 2
				for k in n:
					var id2: String = narrow[(pick + k) % narrow.size()]
					var dest2: Rect2 = _cell_rect(lo.x, lo.y + k * 2, 1, 2) if along_y \
						else _cell_rect(lo.x + k * 2, lo.y, 2, 1)
					# Texture space: 160x160; the band is texels 40..120 across.
					var src: Rect2 = Rect2(40, 0, 80, 160) if along_y else Rect2(0, 40, 160, 80)
					_add(lo.x, lo.y, L_PROP, _key(id2, o), dest2, src,
						_key(id2, o) if _has_glow(id2) else "")
					prop_rects.append(dest2)


## Every other '#' is a wall tile chosen by its connection mask, over a fill
## of the wall's rim tone, with the notch between two arms closed where the
## diagonal is wall too -- so a solid mass reads as one surface, not a grid.
## Doors and panes count as connected: the wall runs up to their frame.
func _place_walls(covered: PackedByteArray) -> void:
	for r in rows:
		for c in cols:
			if not is_wall(c, r) or covered[r * cols + c]:
				continue
			var cell: Rect2 = _cell_rect(c, r)
			_add(c, r, L_BASE, "", cell, Rect2())
			solid_rects.append(cell)
			var m: int = 0
			if _joins(c, r - 1): m |= N
			if _joins(c + 1, r): m |= E
			if _joins(c, r + 1): m |= S
			if _joins(c - 1, r): m |= W
			var id: String = "wall_%02d" % m
			_add(c, r, L_WALL, id, cell, _full(id, 0))
			# Close the inner corners. The patch is lifted from the cap in the
			# middle of wall_15, so it is the cap's own colour and grain.
			var cap := Rect2(36, 36, 8, 8)
			var p: float = CORNER_PATCH
			if (m & N) and (m & E) and _joins(c + 1, r - 1):
				_add(c, r, L_WALL, "wall_15", Rect2(cell.end.x - p, cell.position.y, p, p), cap)
			if (m & S) and (m & E) and _joins(c + 1, r + 1):
				_add(c, r, L_WALL, "wall_15", Rect2(cell.end.x - p, cell.end.y - p, p, p), cap)
			if (m & S) and (m & W) and _joins(c - 1, r + 1):
				_add(c, r, L_WALL, "wall_15", Rect2(cell.position.x, cell.end.y - p, p, p), cap)
			if (m & N) and (m & W) and _joins(c - 1, r - 1):
				_add(c, r, L_WALL, "wall_15", Rect2(cell.position.x, cell.position.y, p, p), cap)


func _joins(c: int, r: int) -> bool:
	return is_wall(c, r) or _is_panel(c, r)


## FLOORS BY ROOM. A room is found the way a player sees one: open floor at
## least two cells from any wall is a room's core, and a doorway (a gap in a
## wall, a door, a pane) is too narrow to have one, so it separates cores.
## Every other walkable cell joins the nearest core. Each room gets one floor,
## chosen by a hash of where it is; a long thin room is a corridor and gets a
## corridor floor. The kit's floors differ in pattern, not tone (a +/-4%
## budget), so a room's edge is felt rather than drawn.
func _place_floors() -> void:
	var n: int = cols * rows
	var dist: PackedInt32Array = PackedInt32Array()
	dist.resize(n)
	dist.fill(1 << 20)
	var q: Array[int] = []
	for r in rows:
		for c in cols:
			var i: int = r * cols + c
			if is_wall(c, r) or _is_panel(c, r):
				dist[i] = 0
				q.append(i)
			elif c == 0 or r == 0 or c == cols - 1 or r == rows - 1:
				dist[i] = 1
				q.append(i)
	var head: int = 0
	while head < q.size():
		var i: int = q[head]
		head += 1
		var c: int = i % cols
		var r: int = i / cols
		for dy in range(-1, 2):
			for dx in range(-1, 2):
				var nc: int = c + dx
				var nr: int = r + dy
				if nc < 0 or nr < 0 or nc >= cols or nr >= rows:
					continue
				var j: int = nr * cols + nc
				if dist[j] > dist[i] + 1:
					dist[j] = dist[i] + 1
					q.append(j)

	region_of.resize(n)
	region_of.fill(-1)
	var regions: int = 0
	var seeds: Array[int] = []
	# Cores, flooded 4-connected.
	for i in n:
		if region_of[i] >= 0 or dist[i] < 2:
			continue
		var stack: Array[int] = [i]
		region_of[i] = regions
		seeds.append(i)
		while not stack.is_empty():
			var k: int = stack.pop_back()
			for j in _nbrs4(k):
				if region_of[j] < 0 and dist[j] >= 2:
					region_of[j] = regions
					stack.append(j)
		regions += 1
	# Everything walkable joins the nearest core, breadth first in row-major
	# order, so ties always break the same way.
	var front: Array[int] = []
	for i in n:
		if region_of[i] >= 0:
			front.append(i)
	head = 0
	while head < front.size():
		var k2: int = front[head]
		head += 1
		for j in _nbrs4(k2):
			if region_of[j] < 0 and _grid[j] != 35:
				region_of[j] = region_of[k2]
				front.append(j)
	# Pockets with no core at all (a one-cell alcove behind a door) are rooms
	# of their own.
	for i in n:
		if region_of[i] >= 0 or _grid[i] == 35:
			continue
		var stack2: Array[int] = [i]
		region_of[i] = regions
		seeds.append(i)
		while not stack2.is_empty():
			var k3: int = stack2.pop_back()
			for j in _nbrs4(k3):
				if region_of[j] < 0 and _grid[j] != 35:
					region_of[j] = regions
					stack2.append(j)
		regions += 1

	# Shape of each room.
	var area: PackedInt32Array = PackedInt32Array()
	area.resize(regions)
	var lo: Array[Vector2i] = []
	var hi: Array[Vector2i] = []
	for k in regions:
		lo.append(Vector2i(cols, rows))
		hi.append(Vector2i(-1, -1))
	for i in n:
		var k4: int = region_of[i]
		if k4 < 0:
			continue
		area[k4] += 1
		var p := Vector2i(i % cols, i / cols)
		lo[k4] = Vector2i(mini(lo[k4].x, p.x), mini(lo[k4].y, p.y))
		hi[k4] = Vector2i(maxi(hi[k4].x, p.x), maxi(hi[k4].y, p.y))
	region_floor.resize(regions)
	var salt: int = _theme_salt()
	for k in regions:
		var span: int = maxi(hi[k].x - lo[k].x, hi[k].y - lo[k].y) + 1
		var corridor: bool = span >= 8 and float(area[k]) / span <= 4.5
		var pool: PackedStringArray = CORRIDOR_FLOORS if corridor else ROOM_FLOORS
		region_floor[k] = pool[_hash(seeds[k] % cols, seeds[k] / cols, salt) % pool.size()]

	# Runs of one floor, merged into rectangles per chunk. The texture is
	# sampled in WORLD space, so neighbouring rects meet without a seam.
	for cr in _chunk_rows:
		for cc in _chunk_cols:
			var c0: int = cc * CHUNK
			var r0: int = cr * CHUNK
			var c1: int = mini(c0 + CHUNK, cols)
			var r1: int = mini(r0 + CHUNK, rows)
			var done: Dictionary = {}
			for r in range(r0, r1):
				var c: int = c0
				while c < c1:
					var i: int = r * cols + c
					if region_of[i] < 0 or done.has(i):
						c += 1
						continue
					var f: String = region_floor[region_of[i]]
					var e: int = c
					while e < c1 and region_of[r * cols + e] >= 0 \
							and not done.has(r * cols + e) \
							and region_floor[region_of[r * cols + e]] == f:
						e += 1
					# Grow the run downward while every cell under it agrees.
					var b: int = r + 1
					while b < r1:
						var ok: bool = true
						for x in range(c, e):
							var j: int = b * cols + x
							if region_of[j] < 0 or done.has(j) or region_floor[region_of[j]] != f:
								ok = false
								break
						if not ok:
							break
						b += 1
					for y in range(r, b):
						for x in range(c, e):
							done[y * cols + x] = true
					var dest: Rect2 = _cell_rect(c, r, e - c, b - r)
					_add(c, r, L_FLOOR, f, dest,
						Rect2(dest.position * TEX_SCALE, dest.size * TEX_SCALE))
					c = e


func _nbrs4(i: int) -> Array[int]:
	var out: Array[int] = []
	var c: int = i % cols
	var r: int = i / cols
	if c > 0: out.append(i - 1)
	if c < cols - 1: out.append(i + 1)
	if r > 0: out.append(i - cols)
	if r < rows - 1: out.append(i + cols)
	return out


## FLOOR MARKINGS, flat and walk-over: a lane down the middle of every straight
## run of corridor (3-5 cells wide, 8+ long), and corner brackets round the
## exit so the way out is framed in the kit's own paint.
func _place_markings() -> void:
	# Lanes are gathered as runs first: a lane shorter than LANE_MIN is a stub
	# between two shelves, not a walkway, and reads as litter.
	for vertical: bool in [false, true]:
		var outer: int = cols if vertical else rows
		var inner: int = rows if vertical else cols
		for a in outer:
			var run: Array[Rect2] = []
			for b in inner + 1:
				var lane: Rect2 = Rect2()
				if b < inner:
					lane = _lane_at(a if vertical else b, b if vertical else a, vertical)
				var joins: bool = lane.size != Vector2.ZERO and (run.is_empty() \
					or (lane.position.x == run[-1].position.x if vertical \
						else lane.position.y == run[-1].position.y))
				if joins:
					run.append(lane)
					continue
				if run.size() >= LANE_MIN:
					for piece in run:
						var pc: Vector2 = piece.get_center() / CELL
						# Half the 40 px sprite per cell: its lines run edge
						# to edge, so any 20 px of it continues the next.
						_add(int(pc.x), int(pc.y), L_DECAL,
							_key("marking_lane", 1 if vertical else 0), piece, Rect2(0, 0, 80, 80))
						decal_rects.append(piece)
				run.clear()
				if lane.size != Vector2.ZERO:
					run.append(lane)

	# One frame per EXIT: each 8-connected blob of 'X' is its own way out
	# (sim Level.Exits), so a level with four exits gets four frames rather
	# than one bracket round the whole floor.
	for box: Rect2i in _exit_boxes():
		var ex_lo: Vector2i = box.position
		var ex_hi: Vector2i = box.end - Vector2i.ONE
		var corners: Array = [[ex_lo.x, ex_lo.y, 0], [ex_hi.x, ex_lo.y, FLIP_X],
			[ex_lo.x, ex_hi.y, FLIP_Y], [ex_hi.x, ex_hi.y, FLIP_X | FLIP_Y]]
		for k: Array in corners:
			# The bracket goes on the exit's bounding box, which on a hand-made
			# level need not be all exit: never paint it on a wall.
			if is_wall(k[0], k[1]):
				continue
			var cell: Rect2 = _cell_rect(k[0], k[1])
			_add(k[0], k[1], L_DECAL, _key("marking_corner", k[2]), cell, _full("marking_corner", 0))
			decal_rects.append(cell)


## Each 8-connected blob of 'X' as its bounding box in cells, in row-major order
## of the blob's first cell -- the grid's half of sim Level.FindExits, capped at
## the same MAX_EXITS, since blobs past it are floor to the sim.
func _exit_boxes() -> Array[Rect2i]:
	var out: Array[Rect2i] = []
	var seen: PackedByteArray = PackedByteArray()
	seen.resize(cols * rows)
	for i in cols * rows:
		if _grid[i] != 88 or seen[i]:   # 'X'
			continue
		if out.size() >= MAX_EXITS:
			break
		var lo := Vector2i(i % cols, i / cols)
		var hi := lo
		var stack: Array[int] = [i]
		seen[i] = 1
		while not stack.is_empty():
			var cur: int = stack.pop_back()
			var p := Vector2i(cur % cols, cur / cols)
			lo = Vector2i(mini(lo.x, p.x), mini(lo.y, p.y))
			hi = Vector2i(maxi(hi.x, p.x), maxi(hi.y, p.y))
			for dr: int in [-1, 0, 1]:
				for dc: int in [-1, 0, 1]:
					var q := Vector2i(p.x + dc, p.y + dr)
					if q.x < 0 or q.y < 0 or q.x >= cols or q.y >= rows:
						continue
					var n: int = q.y * cols + q.x
					if seen[n] or _grid[n] != 88:
						continue
					seen[n] = 1
					stack.append(n)
		out.append(Rect2i(lo, hi - lo + Vector2i.ONE))
	return out


## The lane piece for the corridor cross-section through (c, r), or an empty
## rect. `vertical` means the corridor runs north-south. Emitted once per
## cross-section (from its first cell), centred across it, so a four-wide
## corridor's lane straddles its middle two cells.
func _lane_at(c: int, r: int, vertical: bool) -> Rect2:
	var across: Vector2i = _span(c, r, vertical)
	var width: int = across.y - across.x + 1
	if width < 3 or width > 5:
		return Rect2()
	if (c if vertical else r) != across.x:
		return Rect2()
	# Straight: the same cross-section one step either way along it.
	for d: int in [-1, 1]:
		var nc: int = c + (0 if vertical else d)
		var nr: int = r + (d if vertical else 0)
		if _span(nc, nr, vertical) != across:
			return Rect2()
	var along: Vector2i = _span(c, r, not vertical)
	if along.y - along.x + 1 < 8:
		return Rect2()
	var centre: float = (across.x + across.y + 1) * 0.5 * CELL
	var lane: Rect2 = Rect2(centre - CELL * 0.5, r * CELL, CELL, CELL) if vertical \
		else Rect2(c * CELL, centre - CELL * 0.5, CELL, CELL)
	# Never across a doorway or the exit.
	var mc: int = int(lane.get_center().x / CELL)
	var mr: int = int(lane.get_center().y / CELL)
	if _is_panel(mc, mr) or _at(mc, mr) == 88:
		return Rect2()
	return lane


## The run of non-wall cells through (c, r): along x when `horizontal`, else
## along y. Returned as (first, last) on that axis.
func _span(c: int, r: int, horizontal: bool) -> Vector2i:
	if is_wall(c, r) or c < 0 or r < 0 or c >= cols or r >= rows:
		return Vector2i(1, 0)
	if horizontal:
		var a: int = c
		var b: int = c
		while a > 0 and not is_wall(a - 1, r): a -= 1
		while b < cols - 1 and not is_wall(b + 1, r): b += 1
		return Vector2i(a, b)
	var a2: int = r
	var b2: int = r
	while a2 > 0 and not is_wall(c, a2 - 1): a2 -= 1
	while b2 < rows - 1 and not is_wall(c, b2 + 1): b2 += 1
	return Vector2i(a2, b2)


## A CONDUIT round the outer wall: the ring of border cells, where it can only
## ever frame the level. Elbows at the corners, a valve now and then. On '#'
## only; a border cell that is not wall breaks the run.
func _place_conduit() -> void:
	var ring: Array[Vector2i] = []
	for c in cols:
		ring.append(Vector2i(c, 0))
	for r in range(1, rows):
		ring.append(Vector2i(cols - 1, r))
	for c in range(cols - 2, -1, -1):
		ring.append(Vector2i(c, rows - 1))
	for r in range(rows - 2, 0, -1):
		ring.append(Vector2i(0, r))
	var k: int = 0
	for p in ring:
		k += 1
		if not is_wall(p.x, p.y):
			continue
		var m: int = 0
		if _on_ring(p.x, p.y - 1): m |= N
		if _on_ring(p.x + 1, p.y): m |= E
		if _on_ring(p.x, p.y + 1): m |= S
		if _on_ring(p.x - 1, p.y): m |= W
		var piece: Array = _pipe_piece(m)
		var id: String = piece[0]
		if id == "pipe_straight" and k % VALVE_EVERY == 0:
			id = "pipe_valve"
		var cell: Rect2 = _cell_rect(p.x, p.y)
		_add(p.x, p.y, L_PIPE, _key(id, piece[1]), cell, _full(id, piece[1]))
		pipe_rects.append(cell)


func _on_ring(c: int, r: int) -> bool:
	return is_wall(c, r) and (c == 0 or r == 0 or c == cols - 1 or r == rows - 1)


## The pipe piece and quarter turns that make connection mask `m`. The kit
## draws straight N-S, elbow N-E, tee N-E-S; a clockwise turn maps N->E->S->W.
static func _pipe_piece(m: int) -> Array:
	var bases: Array = [["pipe_straight", N | S], ["pipe_elbow", N | E],
		["pipe_tee", N | E | S], ["pipe_cross", N | E | S | W]]
	for b: Array in bases:
		var mm: int = b[1]
		for turn in 4:
			if mm == m:
				return [b[0], turn]
			mm = ((mm << 1) | (mm >> 3)) & 15
	# One connection or none: a straight along whichever axis it has.
	return ["pipe_straight", 1 if (m & (E | W)) else 0]


## A mask turned clockwise by `turns` quarter turns. Exposed for the harness.
static func rotate_mask(m: int, turns: int) -> int:
	for t in turns:
		m = ((m << 1) | (m >> 3)) & 15
	return m


# ------------------------------------------------------------------ drawing

func _chunk_range(view: Rect2) -> Rect2i:
	var c0: int = clampi(int(floor(view.position.x / (CELL * CHUNK))), 0, _chunk_cols - 1)
	var r0: int = clampi(int(floor(view.position.y / (CELL * CHUNK))), 0, _chunk_rows - 1)
	var c1: int = clampi(int(floor(view.end.x / (CELL * CHUNK))), 0, _chunk_cols - 1)
	var r1: int = clampi(int(floor(view.end.y / (CELL * CHUNK))), 0, _chunk_rows - 1)
	return Rect2i(c0, r0, c1 - c0 + 1, r1 - r0 + 1)


func _draw_layers(canvas: CanvasItem, view: Rect2, first: int, last: int) -> void:
	if not ready:
		return
	var span: Rect2i = _chunk_range(view.grow(CELL * 2.0))
	for layer in range(first, last + 1):
		for cr in range(span.position.y, span.end.y):
			for cc in range(span.position.x, span.end.x):
				for cmd: Array in _chunks[cr * _chunk_cols + cc][layer]:
					var key: String = cmd[0]
					if key == "":
						canvas.draw_rect(cmd[1], _base)
						continue
					var t: Texture2D = _texture(theme, key, false)
					if t != null:
						canvas.draw_texture_rect_region(t, cmd[1], cmd[2])
					if cmd[3] != "":
						var g: Texture2D = _texture(theme, cmd[3], true)
						if g != null:
							# The glow texture is half size: scale the source.
							canvas.draw_texture_rect_region(g, cmd[1],
								Rect2(cmd[2].position * 0.5, cmd[2].size * 0.5))


## Floors and markings. WORLD pass, under the unlit overlay.
func draw_floor(canvas: CanvasItem, view: Rect2) -> void:
	_draw_layers(canvas, view, L_FLOOR, L_DECAL)


## Walls, plinths, props and the conduit. WORLD pass, over the unlit overlay.
func draw_structure(canvas: CanvasItem, view: Rect2) -> void:
	_draw_layers(canvas, view, L_BASE, L_PIPE)


## Darken the floor OUTSIDE the vision polygon, then repaint beyond the
## level's edge. The polygon is 400 rays fanned evenly from the player
## (SimBridge.GetVisionPolygon), so its outside is exactly the quads between
## consecutive rays from the polygon out to UNLIT_FAR -- one triangle array,
## no holes to cut. The textured floor is drawn once, lit, and dimmed here,
## which keeps every room's own floor in the dark as well as in the light.
func draw_unlit(canvas: CanvasItem, vision: PackedVector2Array, origin: Vector2,
		level: Vector2, beyond: Color) -> void:
	var shade := Color(0, 0, 0, UNLIT_ALPHA)
	var tris: PackedVector2Array = unlit_triangles(vision, origin)
	if tris.is_empty():
		canvas.draw_rect(Rect2(Vector2.ZERO, level), shade)
	else:
		var idx: PackedInt32Array = PackedInt32Array()
		idx.resize(tris.size())
		for i in tris.size():
			idx[i] = i
		var cols_arr: PackedColorArray = PackedColorArray()
		cols_arr.resize(tris.size())
		cols_arr.fill(shade)
		RenderingServer.canvas_item_add_triangle_array(canvas.get_canvas_item(), idx,
			tris, cols_arr)
	# The overlay ran past the level; beyond it must stay "nothing".
	var f: float = UNLIT_FAR
	canvas.draw_rect(Rect2(-f, -f, level.x + f * 2.0, f), beyond)
	canvas.draw_rect(Rect2(-f, level.y, level.x + f * 2.0, f), beyond)
	canvas.draw_rect(Rect2(-f, 0, f, level.y), beyond)
	canvas.draw_rect(Rect2(level.x, 0, f, level.y), beyond)


## The outside of an evenly fanned vision polygon as a triangle list (six
## points per ray). Ray i's direction is its own angle, i/n of a turn, the
## same brads the bridge cast it at -- so a ray that ended at the player (d=0)
## still has a direction. Empty when there is no polygon.
static func unlit_triangles(vision: PackedVector2Array, origin: Vector2) -> PackedVector2Array:
	var n: int = vision.size()
	var out: PackedVector2Array = PackedVector2Array()
	if n < 3:
		return out
	out.resize(n * 6)
	var k: int = 0
	for i in n:
		var j: int = (i + 1) % n
		var a: Vector2 = vision[i]
		var b: Vector2 = vision[j]
		var fa: Vector2 = origin + Vector2.from_angle(TAU * i / n) * UNLIT_FAR
		var fb: Vector2 = origin + Vector2.from_angle(TAU * j / n) * UNLIT_FAR
		out[k] = a; out[k + 1] = fa; out[k + 2] = fb
		out[k + 3] = a; out[k + 4] = fb; out[k + 5] = b
		k += 6
	return out


## A door in the kit's sliding-door art: a matched pair, so shut and open are
## the same frame and a door someone has left open reads as open from across
## the room. False when there is no kit, and main.gd draws its own.
func draw_door(canvas: CanvasItem, r: Rect2, open: bool, vertical: bool) -> bool:
	if not ready:
		return false
	var id: String = "door_open" if open else "door_closed"
	var o: int = 1 if vertical else 0
	var t: Texture2D = _texture(theme, _key(id, o), false)
	if t == null:
		return false
	# The frame posts sit at the leaf's ends and stretch with a 3-cell door;
	# the kit's door is two cells, and a 60 px leaf is 1.5x of it.
	canvas.draw_texture_rect(t, r, false)
	var g: Texture2D = _texture(theme, _key(id, o), true)
	if g != null:
		canvas.draw_texture_rect(g, r, false)
	return true


## A lamp in the kit's strip-light art: lit, dark or broken. For the lighting
## layer to call where the SIM has a lamp -- this file never places one.
## False when there is no kit.
func draw_lamp(canvas: CanvasItem, pos: Vector2, lit: bool, broken: bool,
		vertical: bool) -> bool:
	if not ready:
		return false
	var id: String = "light_strip" if lit else ("light_broken" if broken else "light_off")
	var o: int = 1 if vertical else 0
	var t: Texture2D = _texture(theme, _key(id, o), false)
	if t == null:
		return false
	var size: Vector2 = _tex_size(id, o) / TEX_SCALE
	var r := Rect2(pos - size * 0.5, size)
	canvas.draw_texture_rect(t, r, false)
	if lit:
		var g: Texture2D = _texture(theme, _key(id, o), true)
		if g != null:
			canvas.draw_texture_rect(g, r, false)
	return true


# ------------------------------------------------------------------ reading back

## Every command on one layer, flattened: [key, dest, src, glow]. For the
## harness; nothing in the game calls it.
func commands(layer: int) -> Array:
	var out: Array = []
	for ch: Array in _chunks:
		out.append_array(ch[layer])
	return out
