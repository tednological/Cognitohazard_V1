extends SceneTree

## Bug HUNTING for the game layer.
##
##   Godot --headless --path . --script res://tests/fuzz_check.gd
##
## inventory_check.gd pins behaviour that is already believed correct. This
## drives thousands of RANDOM operation sequences through the meta layer and
## checks invariants after every one — the packing grid, the stash and its save
## format, the campaign ledger and its parser, the HUD layout, and the mission
## arithmetic.
##
## This layer had no fuzzing at all, and it is the layer where a bug costs the
## player their gear rather than a frame of animation.
##
## Failures are reported ONCE per invariant with the first offending sequence
## named. The generator is a plain LCG written out here rather than randi(), so
## a failure reproduces on any Godot build.
##
## It NEVER touches user:// — every object it uses is a plain RefCounted, and
## nothing here opens a screen, because both of the stash screen's close paths
## write the player's save.

const GRID := preload("res://game/inventory_grid.gd")
const STASH := preload("res://game/stash.gd")
const CAT := preload("res://game/item_catalog.gd")
const CAMPAIGN := preload("res://game/campaign.gd")
const HUD_LAYOUT := preload("res://game/hud_layout.gd")
const MISSIONS := preload("res://game/missions.gd")

var _pass: int = 0
var _fail: int = 0
var _done: bool = false
var _bad: Dictionary = {}

var _rs: int = 0


func _seed(n: int) -> void:
	_rs = n


func _rand(n: int) -> int:
	_rs = (_rs * 1103515245 + 12345) & 0x3FFFFFFF
	return 0 if n <= 0 else _rs % n


## Record the FIRST violation of an invariant. Later ones are usually the same
## bug still running.
func _violation(what: String, detail: String) -> void:
	if not _bad.has(what):
		_bad[what] = detail


func _verdict(what: String) -> void:
	if _bad.has(what):
		_fail += 1
		print("   FAIL  %s\n       %s" % [what, _bad[what]])
	else:
		_pass += 1
		print("   PASS  %s" % what)


func _check(name: String, ok: bool, detail: String = "") -> void:
	if ok:
		_pass += 1
		print("   PASS  %s" % name)
	else:
		_fail += 1
		print("   FAIL  %s\n       %s" % [name, detail])


func _process(_delta: float) -> bool:
	if _done:
		return true
	_done = true

	print("game-layer fuzz harness")
	print()

	var bridge: RefCounted = load("res://game/SimBridge.cs").new()
	bridge.Load(FileAccess.get_file_as_string("res://levels/substation_4.txt"), 1)

	print("-- packing grid --")
	_fuzz_grid()
	print("-- the stash and its save format --")
	_fuzz_stash(bridge)
	print("-- the campaign parser --")
	_fuzz_campaign()
	print("-- the HUD layout --")
	_fuzz_hud()
	print("-- mission arithmetic --")
	_fuzz_missions()

	print()
	print("%d passed, %d failed" % [_pass, _fail])
	quit(0 if _fail == 0 else 1)
	return true


# ------------------------------------------------------------- packing grid

## Random place / rotate / move / remove, checking after EVERY operation that
## the grid is still a valid packing. An overlap or a stray cell is invisible
## until an item is lost, which is exactly when it is too late.
func _fuzz_grid() -> void:
	var ops: int = 0
	for run in range(400):
		_seed(run * 7919 + 13)
		var w: int = 1 + _rand(10)
		var h: int = 1 + _rand(10)
		var g: RefCounted = GRID.new(w, h)

		for step in range(60):
			var roll: int = _rand(100)
			if roll < 45:
				var fw: int = 1 + _rand(4)
				var fh: int = 1 + _rand(4)
				g.place(100 + _rand(20), fw, fh, _rand(w + 2) - 1,
					_rand(h + 2) - 1, _rand(2))
			elif roll < 60:
				g.auto_place(100 + _rand(20), 1 + _rand(3), 1 + _rand(3))
			elif roll < 75:
				g.rotate_placement(_rand(maxi(1, g.capacity() + 2)) - 1)
			elif roll < 88:
				g.move(_rand(maxi(1, g.capacity() + 2)) - 1,
					_rand(w + 2) - 1, _rand(h + 2) - 1, _rand(2))
			else:
				g.remove(_rand(maxi(1, g.capacity() + 2)) - 1)
			ops += 1
			_grid_invariants(g, "run %d step %d" % [run, step])

		# Whatever state it reached must survive the save format.
		var text: String = g.to_text()
		var back: RefCounted = GRID.new(1, 1)
		var skipped: int = back.from_text(text)
		if skipped != 0:
			_violation("a grid reloads its own save with nothing skipped",
				"run %d skipped %d lines of its own output" % [run, skipped])
		elif back.to_text() != text:
			_violation("a grid round-trips through its save format",
				"run %d: %d chars became %d" % [run, text.length(), back.to_text().length()])
		elif back.count() != g.count():
			_violation("a reloaded grid holds the same items",
				"run %d: %d became %d" % [run, g.count(), back.count()])

	_check("the grid fuzzer ran", ops > 20000, "%d operations" % ops)
	_verdict("no two placements ever share a cell")
	_verdict("no placement ever hangs off the grid")
	_verdict("the cell map always agrees with the placement list")
	_verdict("used_cells always equals the footprints")
	_verdict("a live placement always has a positive footprint")
	_verdict("a grid reloads its own save with nothing skipped")
	_verdict("a grid round-trips through its save format")
	_verdict("a reloaded grid holds the same items")


func _grid_invariants(g: RefCounted, at: String) -> void:
	var seen: Dictionary = {}
	var cells: int = 0
	for pi in range(g.capacity()):
		if not g.is_live(pi):
			continue
		var pos: Vector2i = g.pos_of(pi)
		var span: Vector2i = g.span_of(pi)

		if span.x <= 0 or span.y <= 0:
			_violation("a live placement always has a positive footprint",
				"%s: placement %d is %dx%d" % [at, pi, span.x, span.y])
			continue
		if pos.x < 0 or pos.y < 0 or pos.x + span.x > g.w or pos.y + span.y > g.h:
			_violation("no placement ever hangs off the grid",
				"%s: placement %d %dx%d at %d,%d in %dx%d"
				% [at, pi, span.x, span.y, pos.x, pos.y, g.w, g.h])
			continue

		for r in range(pos.y, pos.y + span.y):
			for c in range(pos.x, pos.x + span.x):
				var key: int = r * g.w + c
				if seen.has(key):
					_violation("no two placements ever share a cell",
						"%s: %d and %d both hold %d,%d" % [at, seen[key], pi, c, r])
				seen[key] = pi
				cells += 1
				if g.placement_at(c, r) != pi:
					_violation("the cell map always agrees with the placement list",
						"%s: cell %d,%d maps to %d, not %d"
						% [at, c, r, g.placement_at(c, r), pi])

	if g.used_cells() != cells:
		_violation("used_cells always equals the footprints",
			"%s: reported %d, counted %d" % [at, g.used_cells(), cells])


# ------------------------------------------------------------------- stash

## Random equipping, unequipping and stocking. The invariant that matters is
## that gear is CONSERVED: an item is worn, fitted or in the grid, and moving it
## between those never creates or destroys one.
func _fuzz_stash(bridge: RefCounted) -> void:
	var ids: PackedInt32Array = PackedInt32Array()
	for i in range(bridge.GearCount):
		ids.append(bridge.GearIdAt(i))

	for run in range(160):
		_seed(run * 104729 + 7)
		var st: RefCounted = STASH.new(bridge, 2 + _rand(10), 2 + _rand(8))

		for step in range(50):
			var roll: int = _rand(100)
			if roll < 40:
				st.add(ids[_rand(ids.size())])
			elif roll < 62:
				st.equip_from_grid(_rand(maxi(1, st.grid.capacity() + 2)) - 1,
					_rand(CAT.SLOT_COUNT + 1) - 1)
			elif roll < 80:
				st.unequip(_rand(CAT.SLOT_COUNT + 2) - 1)
			elif roll < 92:
				st.unequip_attach(_rand(CAT.ATTACH_COUNT + 2) - 1)
			else:
				st.grid.remove(_rand(maxi(1, st.grid.capacity() + 2)) - 1)

			_grid_invariants(st.grid, "stash run %d step %d" % [run, step])

			# Nothing may be worn that the slot does not take, and no slot may
			# hold an item the catalogue does not know.
			for slot in range(CAT.SLOT_COUNT):
				var worn: int = st.equipped_in(slot)
				if worn == STASH.NONE:
					continue
				if not bridge.GearExists(worn):
					_violation("every worn item is in the catalogue",
						"run %d slot %d holds %d" % [run, slot, worn])
				elif not bridge.GearFitsSlot(worn, slot):
					_violation("nothing is ever worn in a slot it does not fit",
						"run %d: %s in slot %d"
						% [run, bridge.GearName(worn), slot])

			for sub in range(CAT.ATTACH_COUNT):
				var fitted: int = st.attached_at(sub)
				if fitted != STASH.NONE and not bridge.GearExists(fitted):
					_violation("every fitted attachment is in the catalogue",
						"run %d sub %d holds %d" % [run, sub, fitted])

		# The save format has to carry all of it back.
		var text: String = st.to_text()
		var back: RefCounted = STASH.new(bridge, 2, 2)
		var skipped: int = back.from_text(text)
		if skipped != 0:
			_violation("a stash reloads its own save with nothing skipped",
				"run %d skipped %d" % [run, skipped])
		elif back.to_text() != text:
			_violation("a stash round-trips through its save format",
				"run %d" % run)
		else:
			for id in ids:
				if back.count_of(id) != st.count_of(id):
					_violation("a reloaded stash holds exactly what was saved",
						"run %d: %d of item %d became %d"
						% [run, st.count_of(id), id, back.count_of(id)])
					break

	_verdict("every worn item is in the catalogue")
	_verdict("nothing is ever worn in a slot it does not fit")
	_verdict("every fitted attachment is in the catalogue")
	_verdict("a stash reloads its own save with nothing skipped")
	_verdict("a stash round-trips through its save format")
	_verdict("a reloaded stash holds exactly what was saved")


# ---------------------------------------------------------------- campaign

## The ledger parser, against garbage. It claims to be total, and money is the
## one number a player would notice going wrong.
func _fuzz_campaign() -> void:
	var real: String = ""
	var c: RefCounted = CAMPAIGN.new()
	c.earn(4321)
	c.settle("a.txt", true, 400, 3, 1, 120, 2)
	c.settle_loss("b.txt")
	real = c.to_text()

	var pieces: PackedStringArray = PackedStringArray([
		"", " ", "\n", "campaign", "campaign 2", "money", "money x", "money -1",
		"money 99999999999999999999", "runs -5", "extractions -1",
		"mission", "mission a.txt", "mission a.txt x y z",
		"mission a.txt -1 -1 -1", "\t", "mission  1 2 3",
	])
	# A NUL cannot be written as a literal in a .gd file -- the parser replaces
	# it -- so it is appended here, because a save file that picked one up is
	# exactly the sort of thing a total parser has to survive.
	pieces.append(String.chr(0))
	pieces.append("money " + String.chr(0) + "5")

	for run in range(600):
		_seed(run * 2654435761 + 11)
		var text: String = ""
		if run < 100:
			text = real.substr(0, (real.length() * run) / 100)
		elif run < 300:
			var n: int = 1 + _rand(6)
			for i in range(n):
				text += pieces[_rand(pieces.size())] + "\n"
		else:
			var b: PackedStringArray = real.split("\n")
			if b.size() > 0:
				var at: int = _rand(b.size())
				b[at] = pieces[_rand(pieces.size())]
				text = "\n".join(b)

		var m: RefCounted = CAMPAIGN.new()
		var skipped: int = m.from_text(text)
		if skipped < 0:
			_violation("the campaign parser never reports a negative skip count",
				"run %d: %d" % [run, skipped])
		if m.money < 0:
			_violation("no campaign text yields negative money",
				"run %d: %d" % [run, m.money])
		if m.runs < 0 or m.extractions < 0:
			_violation("no campaign text yields negative counters",
				"run %d: runs %d extractions %d" % [run, m.runs, m.extractions])
		# Whatever it parsed to, it must be able to describe itself and read
		# that back to the same thing. A parser that cannot round-trip its own
		# output loses data on the next save.
		var again: RefCounted = CAMPAIGN.new()
		again.from_text(m.to_text())
		if again.to_text() != m.to_text():
			_violation("whatever the campaign parses to, it round-trips",
				"run %d" % run)

	_verdict("the campaign parser never reports a negative skip count")
	_verdict("no campaign text yields negative money")
	_verdict("no campaign text yields negative counters")
	_verdict("whatever the campaign parses to, it round-trips")


# ---------------------------------------------------------------- HUD layout

## Random positions, including wild ones. Every element must end up snapped to
## the grid and inside the screen, because an element pushed off the edge cannot
## be dragged back.
func _fuzz_hud() -> void:
	var layout: RefCounted = HUD_LAYOUT.new()
	var ids: Array = []
	for e in HUD_LAYOUT.ELEMENTS:
		ids.append(e["id"])

	for run in range(400):
		_seed(run * 40503 + 3)
		for step in range(8):
			var id: String = ids[_rand(ids.size())]
			var x: float = float(_rand(4000) - 1500)
			var y: float = float(_rand(4000) - 1500)
			layout.set_pos(id, Vector2(x, y))

			var p: Vector2 = layout.pos_of(id)
			var size: Vector2 = layout.size_of(id)
			if fmod(p.x, HUD_LAYOUT.GRID) != 0.0 or fmod(p.y, HUD_LAYOUT.GRID) != 0.0:
				_violation("every HUD element snaps to the grid",
					"run %d: %s at %s" % [run, id, p])
			if p.x < 0.0 or p.y < 0.0:
				_violation("no HUD element is pushed off the top or left",
					"run %d: %s at %s" % [run, id, p])
			if p.x + size.x > 960.0 or p.y + size.y > 620.0:
				_violation("no HUD element is pushed off the bottom or right",
					"run %d: %s at %s size %s" % [run, id, p, size])

		# And the arrangement survives its save format.
		var text: String = layout.to_text()
		var back: RefCounted = HUD_LAYOUT.new()
		back.from_text(text)
		for id2 in ids:
			if back.pos_of(id2) != layout.pos_of(id2):
				_violation("a HUD arrangement round-trips",
					"run %d: %s %s became %s"
					% [run, id2, layout.pos_of(id2), back.pos_of(id2)])
				break

	_verdict("every HUD element snaps to the grid")
	_verdict("no HUD element is pushed off the top or left")
	_verdict("no HUD element is pushed off the bottom or right")
	_verdict("a HUD arrangement round-trips")


# ------------------------------------------------------------------ missions

## The threat and payout arithmetic, over every shape of level summary
## including absurd ones. A payout that goes negative pays the player to fail.
func _fuzz_missions() -> void:
	for run in range(4000):
		_seed(run * 22695477 + 5)
		# SEVEN fields, in SimBridge.LevelSummary's order. A six-field
		# summary is simply rejected by threat_of, so fuzzing one tests the
		# rejection rather than the arithmetic.
		var summary := PackedInt32Array([
			_rand(600) - 50,      # 0 width
			_rand(600) - 50,      # 1 height
			_rand(60) - 5,        # 2 guards
			_rand(40) - 5,        # 3 caches
			_rand(900),           # 4 wall rects
			_rand(40) - 5,        # 5 chests
			_rand(6) - 1,         # 6 objectives
		])

		var threat: int = MISSIONS.threat_of(summary)
		var pips: int = MISSIONS.pips_of(summary)
		var mult: int = MISSIONS.multiplier_q8(summary)
		var pay: int = MISSIONS.mission_payout(summary)

		if threat < 0:
			_violation("threat is never negative", "%s -> %d" % [summary, threat])
		# 0..PIPS, not 1..PIPS: pips_of documents an EMPTY scale for a level
		# with no threat at all, and a summary it cannot read is one of those.
		if pips < 0 or pips > MISSIONS.PIPS:
			_violation("pips always land on the scale",
				"%s -> %d of %d" % [summary, pips, MISSIONS.PIPS])
		if threat > 0 and pips < 1:
			_violation("any threat at all fills at least one pip",
				"%s -> threat %d, pips %d" % [summary, threat, pips])
		if mult <= 0:
			_violation("the payout multiplier is always positive",
				"%s -> %d" % [summary, mult])
		if pay < 0:
			_violation("a mission never pays a negative amount",
				"%s -> %d" % [summary, pay])
		if MISSIONS.multiplier_text(summary).is_empty():
			_violation("every multiplier has a label", "%s" % summary)

	_verdict("threat is never negative")
	_verdict("pips always land on the scale")
	_verdict("any threat at all fills at least one pip")
	_verdict("the payout multiplier is always positive")
	_verdict("a mission never pays a negative amount")
	_verdict("every multiplier has a label")
