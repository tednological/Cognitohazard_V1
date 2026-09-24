extends SceneTree

## Headless verification for the level dressing (game/level_art.gd).
##
##   Godot --headless --path . --script res://tests/level_art_check.gd
##
## The art is presentation, so the thing worth pinning is that it never LIES
## about the level: whatever looks solid is a wall to the sim, every wall looks
## solid, and whatever sits on walkable floor is flat paint. Checked over every
## shipped level and a few hundred random grids. `build()` is pure data from
## the grid and the kit manifest, so all of this runs with no renderer; what a
## headless run cannot do is read textures back, so drawing itself is left to
## tools/render_level_art.gd and a look.

const ART := preload("res://game/level_art.gd")
const HASH_GLYPH: int = 35   # '#'

var _pass: int = 0
var _fail: int = 0
var _done: bool = false


func _check(name: String, ok: bool, detail: String = "") -> void:
	if ok:
		_pass += 1
		print("   PASS  %s" % name)
	else:
		_fail += 1
		print("   FAIL  %s\n       %s" % [name, detail])


func _eq(name: String, got, want) -> void:
	_check(name, got == want, "expected %s, got %s" % [str(want), str(got)])


func _process(_delta: float) -> bool:
	if _done:
		return true
	_done = true
	print("level art harness - the map kits")
	print()
	_check_kits()
	_check_shipped()
	_check_masks()
	_check_pipes()
	_check_props()
	_check_unlit()
	_check_editor_theme()
	_check_fuzz()
	print()
	print("%d passed, %d failed" % [_pass, _fail])
	quit(0 if _fail == 0 else 1)
	return true


# ------------------------------------------------------------------ helpers

## A grid from rows of text, as GetGrid would hand it over.
static func _grid(lines: Array) -> PackedByteArray:
	var g := PackedByteArray()
	for row: String in lines:
		g.append_array(row.to_ascii_buffer())
	return g


## The first contract breach in a built level, or "". This is the whole
## promise: solid on '#' only and on every '#', flat things off it, a floor
## under every walkable cell, and never a lit fixture of its own.
static func _breach(art: RefCounted, g: PackedByteArray, w: int, h: int) -> String:
	var solid := PackedInt32Array()
	solid.resize(w * h)
	for r: Rect2 in art.solid_rects:
		for cell in _cells(r):
			if cell.x < 0 or cell.y < 0 or cell.x >= w or cell.y >= h:
				return "solid rect %s leaves the level" % r
			if g[cell.y * w + cell.x] != HASH_GLYPH:
				return "solid drawn on walkable cell %s" % cell
			solid[cell.y * w + cell.x] += 1
	for i in w * h:
		if g[i] == HASH_GLYPH and solid[i] != 1:
			return "wall cell (%d,%d) drawn solid %d times" % [i % w, i / w, solid[i]]
	for list: String in ["prop_rects", "pipe_rects"]:
		for r: Rect2 in art.get(list):
			for cell in _cells(r):
				if g[cell.y * w + cell.x] != HASH_GLYPH:
					return "%s on walkable cell %s" % [list, cell]
	for r: Rect2 in art.decal_rects:
		for cell in _cells(r):
			if g[cell.y * w + cell.x] == HASH_GLYPH:
				return "floor marking on wall cell %s" % cell
	var floor := PackedInt32Array()
	floor.resize(w * h)
	for cmd: Array in art.commands(ART.L_FLOOR):
		for cell in _cells(cmd[1]):
			if g[cell.y * w + cell.x] == HASH_GLYPH:
				return "floor texture on wall cell %s" % cell
			floor[cell.y * w + cell.x] += 1
	for i in w * h:
		if g[i] != HASH_GLYPH and floor[i] != 1:
			return "walkable cell (%d,%d) floored %d times" % [i % w, i / w, floor[i]]
	for layer in ART.LAYERS:
		for cmd: Array in art.commands(layer):
			if String(cmd[0]).begins_with("light_"):
				return "a decorative light fixture (%s): light is the sim's" % cmd[0]
	return ""


## The cells a world rect covers, by overlap (a lane straddling two rows
## touches both).
static func _cells(r: Rect2) -> Array[Vector2i]:
	var out: Array[Vector2i] = []
	var c0: int = int(floor(r.position.x / ART.CELL + 0.001))
	var r0: int = int(floor(r.position.y / ART.CELL + 0.001))
	var c1: int = int(ceil(r.end.x / ART.CELL - 0.001))
	var r1: int = int(ceil(r.end.y / ART.CELL - 0.001))
	for y in range(r0, r1):
		for x in range(c0, c1):
			out.append(Vector2i(x, y))
	return out


static func _snapshot(art: RefCounted) -> String:
	var parts: PackedStringArray = []
	for layer in ART.LAYERS:
		parts.append(str(art.commands(layer)))
	return "|".join(parts)


# ------------------------------------------------------------------ checks

func _check_kits() -> void:
	print("kits")
	for theme: String in ART.THEMES:
		var kit: Dictionary = ART.manifest(theme)
		_check("%s: the manifest parses and has every piece the renderer names" % theme,
			kit["ok"], "missing one of %s" % str(ART.required_ids()))
		var missing: Array = []
		for id: String in kit["assets"]:
			for map: String in ["albedo", "normal", "emission"]:
				var p: String = ART.TILESET_DIR + String(kit["assets"][id]["maps"].get(map, ""))
				if not ResourceLoader.exists(p):
					missing.append(p)
		_check("%s: every map in the manifest is an imported resource" % theme,
			missing.is_empty(), "e.g. %s (run --import)" % str(missing.slice(0, 3)))
		var narrow_ok: bool = true
		for n: String in ART.NARROW_PROPS.get(theme, []):
			narrow_ok = narrow_ok and kit["props"].has(n)
		_check("%s: every bench prop is a prop in the kit" % theme, narrow_ok)
		_check("%s: a wall rim tone is set" % theme, ART.WALL_BASE.has(theme))
	_eq("a level with no theme line is drawn in the default kit", ART.resolve_theme(""), ART.DEFAULT_THEME)
	_eq("so is one naming a kit that does not exist", ART.resolve_theme("jungle"), ART.DEFAULT_THEME)


func _check_shipped() -> void:
	print("shipped levels")
	var b: RefCounted = load("res://game/SimBridge.cs").new()
	var want := {"substation_4": "industrial", "relay_nine": "scientific",
		"terminal_twelve": "industrial", "meridian_glasshouse": "scientific",
		"vault_row": "industrial", "vault_row_night": "industrial",
		"zz_black_site": "scientific"}
	var seen: int = 0
	for f in DirAccess.get_files_at("res://levels"):
		if not f.ends_with(".txt"):
			continue
		var name: String = f.get_basename()
		b.Load(FileAccess.get_file_as_string("res://levels/" + f), 1)
		var before: int = b.StateHash()
		var art: RefCounted = ART.new()
		art.build(b.GetGrid(), b.GridCols, b.GridRows, b.LevelTheme)
		var g: PackedByteArray = b.GetGrid()
		_check("%s: builds from its kit" % name, art.built)
		if want.has(name):
			seen += 1
			_eq("%s: declares the %s kit" % [name, want[name]], b.LevelTheme, want[name])
		_check("%s: nothing drawn lies about what blocks" % name,
			_breach(art, g, b.GridCols, b.GridRows) == "", _breach(art, g, b.GridCols, b.GridRows))
		var again: RefCounted = ART.new()
		again.build(b.GetGrid(), b.GridCols, b.GridRows, b.LevelTheme)
		_check("%s: dresses the same way every time" % name, _snapshot(art) == _snapshot(again))
		_check("%s: dressing it touches no sim state" % name, b.StateHash() == before)
		var walls: int = 0
		for i in g.size():
			if g[i] == HASH_GLYPH:
				walls += 1
		_check("%s: some walls are wall tiles, and the rest props" % name,
			art.commands(ART.L_WALL).size() > 0 and art.solid_rects.size() <= walls)
	_eq("every shipped level is covered", seen, want.size())

	# The floors that make the shipped levels worth dressing: the reference
	# level has a 2x2 block and a bench, the terminal twenty 2x2 blocks.
	b.Load(FileAccess.get_file_as_string("res://levels/substation_4.txt"), 1)
	var sub: RefCounted = ART.new()
	sub.build(b.GetGrid(), b.GridCols, b.GridRows, b.LevelTheme)
	_eq("substation_4: its 2x2 block and its 1x2 bench carry equipment", sub.prop_rects.size(), 2)
	_check("substation_4: the exit is framed in the kit's paint",
		sub.commands(ART.L_DECAL).filter(func(c: Array) -> bool:
			return String(c[0]).begins_with("marking_corner")).size() == 4)
	# A level with several exits frames EACH of them, not one bracket round the
	# whole floor: the Black Site's four corner exits get four corners apiece.
	b.Load(FileAccess.get_file_as_string("res://levels/zz_black_site.txt"), 1)
	var site: RefCounted = ART.new()
	site.build(b.GetGrid(), b.GridCols, b.GridRows, b.LevelTheme)
	var brackets: Array = site.commands(ART.L_DECAL).filter(func(c: Array) -> bool:
		return String(c[0]).begins_with("marking_corner"))
	_eq("zz_black_site: every one of its four exits is framed", brackets.size(), 16)
	_eq("and the bridge reports four exits", b.GetExits().size(), 16)
	b.Load(FileAccess.get_file_as_string("res://levels/substation_4.txt"), 1)
	_check("substation_4: the conduit runs the whole border",
		sub.pipe_rects.size() == 2 * (b.GridCols + b.GridRows) - 4)
	b.Load(FileAccess.get_file_as_string("res://levels/terminal_twelve.txt"), 1)
	var term: RefCounted = ART.new()
	term.build(b.GetGrid(), b.GridCols, b.GridRows, b.LevelTheme)
	_check("terminal_twelve: its corridors are laned", term.decal_rects.size() > 40,
		"%d marking pieces" % term.decal_rects.size())
	_check("terminal_twelve: its rooms do not all share one floor",
		Array(term.region_floor).reduce(func(acc: Dictionary, f: String) -> Dictionary:
			acc[f] = true
			return acc, {}).size() >= 3)


func _check_masks() -> void:
	print("wall connection masks")
	var art: RefCounted = ART.new()
	# A pier, a plus, and a 3x3 block, each on its own in a walled room.
	var g: PackedByteArray = _grid([
		"###############",
		"#.............#",
		"#.#....#......#",
		"#.....###.....#",
		"#......#..###.#",
		"#.........###.#",
		"#.........###.#",
		"#.............#",
		"###############"])
	art.build(g, 15, 9, "industrial")
	var at := {}
	for cmd: Array in art.commands(ART.L_WALL):
		var r: Rect2 = cmd[1]
		if r.size == Vector2(ART.CELL, ART.CELL):
			at[Vector2i(r.position / ART.CELL)] = cmd[0]
	_eq("a lone pier is wall_00", at.get(Vector2i(2, 2), ""), "wall_00")
	_eq("the middle of a plus is wall_15", at.get(Vector2i(7, 3), ""), "wall_15")
	_eq("its north arm connects south only", at.get(Vector2i(7, 2), ""), "wall_04")
	_eq("its east arm connects west only", at.get(Vector2i(8, 3), ""), "wall_08")
	_eq("a border corner connects east and south", at.get(Vector2i(0, 0), ""), "wall_06")
	var patches := {}
	for cmd: Array in art.commands(ART.L_WALL):
		var r2: Rect2 = cmd[1]
		if r2.size.x < ART.CELL:
			var cell := Vector2i(r2.position / ART.CELL)
			patches[cell] = patches.get(cell, 0) + 1
	# The 3x3 at (10..12, 4..6) is odd-sized, so it stays wall, not a prop.
	_eq("the middle of a solid 3x3 closes all four notches", patches.get(Vector2i(11, 5), 0), 4)
	_eq("its corner cell closes the one notch facing in", patches.get(Vector2i(10, 4), 0), 1)
	_eq("the plus closes none: its diagonals are floor", patches.get(Vector2i(7, 3), 0), 0)
	_eq("a notch patch covers the tile's bevel as well as its notch", ART.CORNER_PATCH, 5.0)


func _check_pipes() -> void:
	print("conduit pieces")
	var bases := {"pipe_straight": ART.N | ART.S, "pipe_elbow": ART.N | ART.E,
		"pipe_tee": ART.N | ART.E | ART.S, "pipe_cross": 15}
	var ok: bool = true
	var bad: String = ""
	for m in 16:
		var bits: int = (m & 1) + ((m >> 1) & 1) + ((m >> 2) & 1) + ((m >> 3) & 1)
		if bits < 2:
			continue
		var piece: Array = ART._pipe_piece(m)
		var got: int = ART.rotate_mask(bases[piece[0]], piece[1])
		if got != m:
			ok = false
			bad = "mask %d -> %s turned %d = %d" % [m, piece[0], piece[1], got]
	_check("every mask of two or more joins is drawn by a piece turned to match it", ok, bad)
	_eq("a clockwise quarter turn takes north to east", ART.rotate_mask(ART.N, 1), ART.E)
	_eq("and west back to north", ART.rotate_mask(ART.W, 1), ART.N)


func _check_props() -> void:
	print("equipment on free-standing blocks")
	var g: PackedByteArray = _grid([
		"################",
		"#..............#",
		"#.##...##......#",
		"#.##...##+.....#",
		"#..............#",
		"#.###....##....#",
		"#.###..........#",
		"#..............#",
		"################"])
	var art: RefCounted = ART.new()
	art.build(g, 16, 9, "scientific")
	var on := {}
	for r: Rect2 in art.prop_rects:
		on[Vector2i(r.position / ART.CELL)] = r.size / ART.CELL
	_eq("a free 2x2 block carries one prop", on.get(Vector2i(2, 2), Vector2.ZERO), Vector2(2, 2))
	_check("a block a door is set into stays wall", not on.has(Vector2i(7, 2)))
	_check("an odd 3x2 block stays wall", not on.has(Vector2i(2, 5)))
	_eq("a 2x1 block is a bench, one cell deep", on.get(Vector2i(9, 5), Vector2.ZERO), Vector2(2, 1))
	_check("and all of it tells the truth", _breach(art, g, 16, 9) == "", _breach(art, g, 16, 9))


func _check_unlit() -> void:
	print("the unlit overlay")
	_eq("no polygon, no triangles (the whole floor is dimmed instead)",
		ART.unlit_triangles(PackedVector2Array(), Vector2.ZERO).size(), 0)
	# A fan of 64 rays: a circle of radius 50, with one ray cut short.
	var n: int = 64
	var origin := Vector2(300, 200)
	var poly := PackedVector2Array()
	for i in n:
		var d: float = 50.0 if i != 10 else 0.0
		poly.append(origin + Vector2.from_angle(TAU * i / n) * d)
	var tris: PackedVector2Array = ART.unlit_triangles(poly, origin)
	_eq("two triangles per ray", tris.size(), n * 6)
	var rng := RandomNumberGenerator.new()
	rng.seed = 7
	var wrong_in: int = 0
	var wrong_out: int = 0
	for k in 2000:
		var p: Vector2 = origin + Vector2.from_angle(rng.randf() * TAU) * rng.randf_range(0.0, 900.0)
		var c: int = _covered(tris, p)
		if Geometry2D.is_point_in_polygon(p, poly):
			if c != 0:
				wrong_in += 1
		elif c != 1:
			wrong_out += 1
	_eq("nothing inside the polygon is dimmed, the cut-short ray's notch included", wrong_in, 0)
	_eq("everything outside it is dimmed exactly once, so the shade is even", wrong_out, 0)


static func _covered(tris: PackedVector2Array, p: Vector2) -> int:
	var hits: int = 0
	var i: int = 0
	while i < tris.size():
		if Geometry2D.point_is_inside_triangle(p, tris[i], tris[i + 1], tris[i + 2]):
			hits += 1
		i += 3
	return hits


func _check_editor_theme() -> void:
	print("the theme in the editor")
	var b: RefCounted = load("res://game/SimBridge.cs").new()
	b.Load(FileAccess.get_file_as_string("res://levels/relay_nine.txt"), 1)
	b.EditorBeginFromCurrent()
	_eq("the editor opens a level with its theme", b.EditorTheme, "scientific")
	b.EditorTheme = "  Industrial!!"
	_eq("a theme set in the editor is cleaned as the parser would", b.EditorTheme, "industrial")
	_check("and saved in the level text", b.EditorToText().contains("\ntheme: industrial\n"))
	b.EditorResize(b.EditorCols + 4, b.EditorRows)
	_eq("a resize keeps it", b.EditorTheme, "industrial")


func _check_fuzz() -> void:
	print("fuzzed grids")
	var rng := RandomNumberGenerator.new()
	rng.seed = 20260923
	var glyphs: PackedByteArray = ".........#####+=X$Ca@*LS".to_ascii_buffer()
	var first_bad: String = ""
	var runs: int = 300
	for k in runs:
		var w: int = rng.randi_range(1, 40)
		var h: int = rng.randi_range(1, 30)
		var g := PackedByteArray()
		g.resize(w * h)
		var density: float = rng.randf()
		for i in w * h:
			# Blocky walls as well as noise, so the prop paths see real blocks.
			if rng.randf() < density * 0.5:
				g[i] = HASH_GLYPH
			else:
				g[i] = glyphs[rng.randi_range(0, glyphs.size() - 1)]
		if rng.randf() < 0.5:
			for n in rng.randi_range(0, 6):
				var bx: int = rng.randi_range(0, w - 1)
				var by: int = rng.randi_range(0, h - 1)
				var bw: int = rng.randi_range(1, 4)
				var bh: int = rng.randi_range(1, 4)
				for y in range(by, mini(by + bh, h)):
					for x in range(bx, mini(bx + bw, w)):
						g[y * w + x] = HASH_GLYPH
		var theme: String = ART.THEMES[k % ART.THEMES.size()]
		var art: RefCounted = ART.new()
		art.build(g, w, h, theme)
		if not art.built:
			first_bad = "seed %d: %dx%d did not build" % [k, w, h]
			break
		var why: String = _breach(art, g, w, h)
		if why != "":
			first_bad = "grid %d (%dx%d, %s): %s" % [k, w, h, theme, why]
			break
	_check("%d random grids: every one builds and none lies about what blocks" % runs,
		first_bad == "", first_bad)
	var short: RefCounted = ART.new()
	_check("a grid shorter than its stated size is refused, not read past",
		not short.build(PackedByteArray([35, 35]), 4, 4, "industrial") and not short.built)
