extends SceneTree

## Render levels in their map kit to PNG, offscreen, for review.
##
##   Godot --path . --script res://tools/render_level_art.gd -- --out /abs/dir [--level substation_4] [--scale 1.0]
##
## NOT headless: it needs a renderer to read pixels back. Draws through the
## real game/level_art.gd and the real bridge, the way main.gd does: floor,
## the unlit overlay from the spawn point's vision polygon, the exit, walls and
## props, doors (shut) and panes. Two images per level: the whole floor at
## --scale, and a 960x560 crop round the spawn at the game's PLAY_ZOOM 1.35.
## Touches nothing in user://.

const ART := preload("res://game/level_art.gd")
const PLAY_ZOOM: float = 1.35

var _jobs: Array = []
var _out: String = ""
var _scale: float = 1.0
var _started: bool = false


func _initialize() -> void:
	var args: PackedStringArray = OS.get_cmdline_user_args()
	var only: String = ""
	for i in args.size():
		if args[i] == "--out" and i + 1 < args.size():
			_out = args[i + 1]
		elif args[i] == "--level" and i + 1 < args.size():
			only = args[i + 1]
		elif args[i] == "--scale" and i + 1 < args.size():
			_scale = args[i + 1].to_float()
	if _out == "":
		push_error("render_level_art: --out <dir> is required")
		quit(2)
		return
	DirAccess.make_dir_recursive_absolute(_out)
	for f in DirAccess.get_files_at("res://levels"):
		if f.ends_with(".txt") and (only == "" or f.get_basename() == only):
			_jobs.append("res://levels/" + f)


func _process(_delta: float) -> bool:
	if _started:
		return false
	_started = true
	_run()
	return false


func _run() -> void:
	var b: RefCounted = load("res://game/SimBridge.cs").new()
	for path: String in _jobs:
		b.Load(FileAccess.get_file_as_string(path), 1)
		var art: RefCounted = ART.new()
		var ok: bool = art.build(b.GetGrid(), b.GridCols, b.GridRows, b.LevelTheme)
		if not ok:
			push_error("render_level_art: kit not ready for %s" % path)
			continue
		var size := Vector2(b.GridWidthPx, b.GridHeightPx)
		var p: PackedInt32Array = b.GetPlayer()
		var spawn := Vector2(p[0] / 256.0, p[1] / 256.0)
		var vision: PackedVector2Array = b.GetVisionPolygon(400, 430)
		var panels: PackedInt32Array = b.GetPanels()
		var ex: PackedInt32Array = b.GetExits()
		var exit_rects: Array[Rect2] = []
		for i in range(0, ex.size() - 3, 4):
			exit_rects.append(Rect2(ex[i] / 256.0, ex[i + 1] / 256.0, ex[i + 2] / 256.0, ex[i + 3] / 256.0))
		var name: String = path.get_file().get_basename()

		var whole: Image = await _shoot(art, size * _scale, Transform2D(0.0, Vector2(_scale, _scale), 0.0, Vector2.ZERO),
			Rect2(Vector2.ZERO, size), vision, spawn, size, panels, exit_rects)
		whole.save_png(_out.path_join("%s_whole.png" % name))

		var view := Vector2(960, 560) / PLAY_ZOOM
		var tl: Vector2 = (spawn - view * 0.5).clamp(Vector2.ZERO, (size - view).max(Vector2.ZERO))
		var crop: Image = await _shoot(art, Vector2(960, 560),
			Transform2D(0.0, Vector2(PLAY_ZOOM, PLAY_ZOOM), 0.0, -tl * PLAY_ZOOM),
			Rect2(tl, view), vision, spawn, size, panels, exit_rects)
		crop.save_png(_out.path_join("%s_play.png" % name))
		print("render_level_art: %s (%s) -> %s" % [name, art.theme, _out])
	quit(0)


func _shoot(art: RefCounted, px: Vector2, xf: Transform2D, view: Rect2,
		vision: PackedVector2Array, spawn: Vector2, size: Vector2,
		panels: PackedInt32Array, exit_rects: Array[Rect2]) -> Image:
	var vp := SubViewport.new()
	vp.size = Vector2i(px.ceil())
	vp.render_target_update_mode = SubViewport.UPDATE_ONCE
	vp.transparent_bg = false
	root.add_child(vp)
	var n := Node2D.new()
	vp.add_child(n)
	n.draw.connect(func() -> void:
		n.draw_set_transform_matrix(Transform2D.IDENTITY)
		n.draw_rect(Rect2(Vector2.ZERO, px), Color(0.02, 0.025, 0.032))
		n.draw_set_transform_matrix(xf)
		art.draw_floor(n, view)
		art.draw_unlit(n, vision, spawn, size, Color(0.02, 0.025, 0.032))
		for exit_rect: Rect2 in exit_rects:
			n.draw_rect(exit_rect, Color(0.25, 0.72, 0.48, 0.16))
			n.draw_rect(exit_rect, Color(0.25, 0.72, 0.48), false, 1.5)
		art.draw_structure(n, view)
		var i: int = 0
		while i + 6 <= panels.size():
			var r := Rect2(panels[i + 1] / 256.0, panels[i + 2] / 256.0,
				panels[i + 3] / 256.0, panels[i + 4] / 256.0)
			if panels[i] == 1:
				art.draw_door(n, r, (panels[i + 5] & 1) != 0, (panels[i + 5] & 2) != 0)
			else:
				n.draw_rect(r, Color(0.62, 0.84, 0.95, 0.16))
			i += 6
		n.draw_circle(spawn, 9.0, Color(0.85, 0.87, 0.92)))
	await process_frame
	await RenderingServer.frame_post_draw
	await process_frame
	await RenderingServer.frame_post_draw
	var img: Image = vp.get_texture().get_image()
	vp.queue_free()
	return img
