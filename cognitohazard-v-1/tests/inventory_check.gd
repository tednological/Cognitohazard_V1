extends SceneTree

## Headless verification for the grid inventory (rpg_extension_plan.md §1, §4).
##
##   Godot --headless --path . --script res://tests/inventory_check.gd
##
## The C# sim harness cannot cover this: the stash is the META layer and lives
## in game/, deliberately outside sim/. So it gets its own runner, like the
## editor and audio harnesses.
##
## The packing core takes no bridge and no scene, so most of this is plain
## assertions on integers. The one part that does need the bridge is the mirror
## check: item_catalog.gd duplicates the sim's names and ids, and this harness
## is what makes that duplication safe -- if sim/Loadout.cs renames or renumbers
## anything, these fail loudly rather than the stash quietly mislabelling gear.

const GRID := preload("res://game/inventory_grid.gd")
const CAT := preload("res://game/item_catalog.gd")
const STASH := preload("res://game/stash.gd")
const LEVELS := preload("res://game/levels.gd")
const HUD_LAYOUT := preload("res://game/hud_layout.gd")
const CAMPAIGN := preload("res://game/campaign.gd")
const TIP := preload("res://game/item_tooltip.gd")

var _pass: int = 0
var _fail: int = 0
var _done: bool = false

## The player's user://stash.txt, held across the whole run. See _process.
var _stash_path: String = ""
var _stash_had: bool = false
var _stash_backup: String = ""


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

	print("inventory harness - rpg plan 1 and 4")
	print()

	# The PLAYER'S REAL SAVE, held for the duration.
	#
	# Several things here open a stash screen, and BOTH of its close paths call
	# _save(), which writes user://stash.txt. _check_screens guarded that and
	# _check_screens_start did not, so every run of this harness quietly reset
	# the player's stash to stock_default() and nobody noticed -- the file was
	# still there, still valid, just not theirs.
	#
	# Guarding it once, around everything, rather than in each test that happens
	# to touch it: the next test to open a stash screen would have the same bug
	# again, and would pass.
	_stash_path = "user://stash.txt"
	_stash_had = FileAccess.file_exists(_stash_path)
	_stash_backup = FileAccess.get_file_as_string(_stash_path) if _stash_had else ""

	print("-- packing geometry --")
	_check_geometry()
	print("-- rotation --")
	_check_rotation()
	print("-- fitting --")
	_check_fitting()
	print("-- placement ids --")
	_check_ids()
	print("-- grid save format --")
	_check_grid_text()
	var b: RefCounted = load("res://game/SimBridge.cs").new()
	b.Load(FileAccess.get_file_as_string("res://levels/substation_4.txt"), 1)

	print("-- catalog through the bridge --")
	_check_catalog(b)
	print("-- the two small enums still mirror sim/GearCatalog.cs --")
	_check_mirror(b)
	_check_rarity(b)
	print("-- stash equipping --")
	_check_equipping(b)
	print("-- stash resolves into a Loadout --")
	_check_apply(b)
	print("-- stash save format --")
	_check_stash_text(b)
	print("-- loadout presets --")
	_check_presets(b)
	print("-- the screens --")
	_check_screens(b)
	print("-- loot inspection panel --")
	_check_loot_panel(b)
	print("-- how screens are left --")
	_check_screen_exits(b)
	print("-- the in-mission inventory --")
	_check_mission_mode(b)
	print("-- every way an item can move --")
	_check_drag_paths(b)
	print("-- and every way it can move inside a run --")
	_check_field_drag_paths(b)
	print("-- what an item does --")
	_check_tooltips(b)
	print("-- the tooltip on screen --")
	_check_tooltip_layout(b)
	print("-- each weapon its own rails --")
	_check_per_weapon_rails(b)
	print("-- the bag you walk in with --")
	_check_carrying(b)
	print("-- what dying costs, and what extracting does not --")
	_check_loss_and_landing(b)
	print("-- developer menu --")
	_check_dev_menu(b)
	_check_hud_edges()
	_check_hud_layout()
	_check_campaign(b)

	_restore_player_stash()

	print()
	print("%d passed, %d failed" % [_pass, _fail])
	quit(0 if _fail == 0 else 1)
	return true


## Put the player's stash back exactly as it was found, and SAY SO -- an
## assertion, not a hope, because "the file still exists" is also what a stash
## reset to the starting kit looks like.
func _restore_player_stash() -> void:
	print("-- the player's own save --")
	if _stash_had:
		var f := FileAccess.open(_stash_path, FileAccess.WRITE)
		if f != null:
			f.store_string(_stash_backup)
			f.close()
	else:
		DirAccess.remove_absolute(ProjectSettings.globalize_path(_stash_path))

	_eq("the stash file is back to what it was",
		FileAccess.file_exists(_stash_path), _stash_had)
	if _stash_had:
		_eq("byte for byte", FileAccess.get_file_as_string(_stash_path),
			_stash_backup)


func _check_geometry() -> void:
	var g: RefCounted = GRID.new(6, 4)
	_eq("a fresh grid is empty", g.used_cells(), 0)
	_eq("and all of it is free", g.free_cells(), 24)

	var pi: int = g.place(1, 2, 3, 0, 0, GRID.ROT_NONE)
	_check("a 2x3 fits at the origin", pi != GRID.NONE)
	_eq("it covers six cells", g.used_cells(), 6)
	_eq("the top-left cell reports it", g.placement_at(0, 0), pi)
	_eq("so does the bottom-right of its span", g.placement_at(1, 2), pi)
	_eq("the cell past its span is free", g.placement_at(2, 0), GRID.NONE)

	_eq("overlapping it is refused", g.place(2, 1, 1, 1, 1, GRID.ROT_NONE), GRID.NONE)
	_eq("and nothing was consumed", g.used_cells(), 6)

	_eq("running off the right edge is refused",
		g.place(2, 2, 1, 5, 0, GRID.ROT_NONE), GRID.NONE)
	_eq("running off the bottom is refused",
		g.place(2, 1, 2, 0, 3, GRID.ROT_NONE), GRID.NONE)
	_eq("a negative origin is refused",
		g.place(2, 1, 1, -1, 0, GRID.ROT_NONE), GRID.NONE)

	_check("it removes", g.remove(pi))
	_eq("and gives the cells back", g.used_cells(), 0)
	_check("removing it twice is refused", not g.remove(pi))

	# A degenerate grid must not crash anything that touches it.
	var z: RefCounted = GRID.new(0, 0)
	_eq("a zero-size grid holds nothing", z.free_cells(), 0)
	_eq("and refuses a placement", z.place(1, 1, 1, 0, 0, GRID.ROT_NONE), GRID.NONE)
	_eq("and reads as empty out of bounds", z.placement_at(0, 0), GRID.NONE)


func _check_rotation() -> void:
	_eq("unturned, a 3x1 spans 3x1", GRID.span(3, 1, GRID.ROT_NONE), Vector2i(3, 1))
	_eq("turned, it spans 1x3", GRID.span(3, 1, GRID.ROT_90), Vector2i(1, 3))

	# A 3-wide item cannot lie down in a 2-wide grid, but it can stand up.
	var g: RefCounted = GRID.new(2, 4)
	_eq("a 3x1 will not lie down in a 2-wide grid",
		g.place(1, 3, 1, 0, 0, GRID.ROT_NONE), GRID.NONE)
	var pi: int = g.place(1, 3, 1, 0, 0, GRID.ROT_90)
	_check("but it stands up", pi != GRID.NONE)
	_eq("standing, it spans 1x3", g.span_of(pi), Vector2i(1, 3))
	_eq("its base size is unchanged", g.base_of(pi), Vector2i(3, 1))
	_eq("it occupies the column", g.placement_at(0, 2), pi)

	_check("turning it back is refused, there is no room",
		not g.rotate_placement(pi))
	_eq("and it did not move", g.span_of(pi), Vector2i(1, 3))
	_eq("nor did the grid change", g.used_cells(), 3)

	# Room to turn: a 3x1 in a 4x4 rotates about its top-left cell.
	var big: RefCounted = GRID.new(4, 4)
	var p2: int = big.place(1, 3, 1, 0, 0, GRID.ROT_NONE)
	_check("with room, it turns", big.rotate_placement(p2))
	_eq("and now spans 1x3", big.span_of(p2), Vector2i(1, 3))
	_eq("turning about the top-left keeps the origin", big.pos_of(p2), Vector2i(0, 0))
	_check("and turns back", big.rotate_placement(p2))
	_eq("returning to 3x1", big.span_of(p2), Vector2i(3, 1))

	_check("it moves to a clear spot", big.move(p2, 1, 2, GRID.ROT_NONE))
	_eq("and the old cells are free", big.placement_at(0, 0), GRID.NONE)
	_eq("and the new ones are not", big.placement_at(1, 2), p2)


func _check_fitting() -> void:
	var g: RefCounted = GRID.new(4, 2)
	_eq("an empty grid fits it at the origin", g.find_fit(2, 1), [0, 0, GRID.ROT_NONE])

	g.place(1, 2, 1, 0, 0, GRID.ROT_NONE)
	_eq("the next one goes beside it", g.find_fit(2, 1), [2, 0, GRID.ROT_NONE])
	g.place(2, 2, 1, 2, 0, GRID.ROT_NONE)
	_eq("then onto the next row", g.find_fit(2, 1), [0, 1, GRID.ROT_NONE])

	# Only the turned footprint can still fit, so find_fit must reach for it.
	var narrow: RefCounted = GRID.new(1, 3)
	_eq("it turns an item when that is the only way in",
		narrow.find_fit(3, 1), [0, 0, GRID.ROT_90])

	var full: RefCounted = GRID.new(2, 2)
	full.place(1, 2, 2, 0, 0, GRID.ROT_NONE)
	_eq("a full grid fits nothing", full.find_fit(1, 1), [])
	_eq("and auto_place refuses", full.auto_place(9, 1, 1), GRID.NONE)

	# Scan order is fixed, so the same items always pack the same way.
	var a: RefCounted = GRID.new(5, 5)
	var b: RefCounted = GRID.new(5, 5)
	for item_id in [1, 2, 3, 4]:
		a.auto_place(item_id, 2, 2)
		b.auto_place(item_id, 2, 2)
	_eq("packing is reproducible", a.to_text(), b.to_text())


func _check_ids() -> void:
	var g: RefCounted = GRID.new(4, 4)
	var p0: int = g.place(10, 1, 1, 0, 0, GRID.ROT_NONE)
	var p1: int = g.place(11, 1, 1, 1, 0, GRID.ROT_NONE)
	_check("placements get distinct ids", p0 != p1)
	_eq("both are live", g.live_ids(), PackedInt32Array([p0, p1]))
	_eq("and the item ids come back", g.item_of(p1), 11)

	g.remove(p0)
	_eq("a removed placement is not live", g.live_ids(), PackedInt32Array([p1]))
	_eq("and reads as NONE", g.item_of(p0), GRID.NONE)
	_eq("count follows", g.count(), 1)

	# The dead hole is reused, so the arrays cannot grow without bound, and the
	# surviving placement keeps its id -- the occupancy grid stores these.
	var p2: int = g.place(12, 1, 1, 2, 0, GRID.ROT_NONE)
	_eq("the dead id is reused", p2, p0)
	_eq("the survivor kept its id", g.item_of(p1), 11)
	_eq("and its cells still point at it", g.placement_at(1, 0), p1)


func _check_grid_text() -> void:
	var g: RefCounted = GRID.new(5, 3)
	g.place(102, 4, 2, 0, 0, GRID.ROT_NONE)
	g.place(301, 1, 1, 4, 0, GRID.ROT_NONE)
	var text: String = g.to_text()

	var back: RefCounted = GRID.new(1, 1)
	_eq("a clean save reports nothing skipped", back.from_text(text), 0)
	_eq("the size round-trips", Vector2i(back.w, back.h), Vector2i(5, 3))
	_eq("the contents round-trip", back.to_text(), text)
	_eq("and so does occupancy", back.used_cells(), g.used_cells())

	# Totality: a hand-edited or outdated save must still load.
	var messy: RefCounted = GRID.new(1, 1)
	var skipped: int = messy.from_text(
		"grid 4 4\n# a comment\n\nitem 1 0 0 2 2 0\nnonsense\nitem 5 99 99 1 1 0\n")
	_eq("garbage and unplaceable lines are counted, not fatal", skipped, 2)
	_eq("and the good line still loaded", messy.count(), 1)
	_eq("an empty save leaves an empty grid", GRID.new(2, 2).from_text(""), 0)


func _check_catalog(b: RefCounted) -> void:
	var ids: PackedInt32Array = CAT.all_ids(b)
	_check("the catalog is not empty", ids.size() > 0)
	_eq("and matches the bridge's count", ids.size(), b.GearCount)

	var bad: int = 0
	var seen: Array = []
	for id in ids:
		if seen.has(id):
			bad += 1
		seen.append(id)
		var s: Vector2i = CAT.size_of(b, id)
		if s.x < 1 or s.y < 1:
			bad += 1
		if b.GearName(id).is_empty():
			bad += 1
	_eq("ids are unique, footprints positive, names present", bad, 0)

	# Spot-check against the sim's own numbers rather than a copy of them.
	_eq("the AK-47 is 4x2", CAT.size_of(b, 102), Vector2i(4, 2))
	_eq("and named by the sim", b.GearName(102), "AK-47")
	_eq("the SAW is the bulkiest thing carried", CAT.size_of(b, 104), Vector2i(5, 3))

	# Total, the way the level parser is total.
	_check("an unknown id does not exist", not b.GearExists(999999))
	_eq("and sizes safely", CAT.size_of(b, 999999), Vector2i.ONE)
	_eq("and names safely", b.GearName(999999), "unknown item")

	# A backpack is the one apparel slot the sim reads: it sizes the pack.
	_eq("the large pack provides an 8x5 grid",
		Vector2i(b.GearPackW(503), b.GearPackH(503)), Vector2i(8, 5))
	_eq("a rifle provides no pack at all",
		Vector2i(b.GearPackW(102), b.GearPackH(102)), Vector2i.ZERO)

	# Weapons take either hand; apparel takes exactly its own slot.
	_check("a rifle fits the primary hand", b.GearFitsSlot(102, CAT.SLOT_PRIMARY))
	_check("and the secondary hand", b.GearFitsSlot(102, CAT.SLOT_SECONDARY))
	_check("a helmet does not fit the vest", not b.GearFitsSlot(402, CAT.SLOT_VEST))
	_check("but does fit the helmet slot", b.GearFitsSlot(402, CAT.SLOT_HELMET))
	_check("an attachment fits no body slot at all",
		not b.GearFitsSlot(301, CAT.SLOT_PRIMARY))


## item_catalog.gd no longer copies the item table, but it still names the two
## enums by hand. This is what keeps those honest.
## Rarity: the tiers game/ colours must be the tiers the sim has, every item
## names one, the tooltip says it in words, and the loot figures the mission
## select shows come out of the summary where it reads them.
func _check_rarity(b: RefCounted) -> void:
	print("  -- rarity and loot --")
	_eq("one colour per sim rarity", CAT.RARITY_COLOURS.size(), b.RarityCount)
	_eq("the tiers mirror the sim's", [CAT.RARITY_COMMON, CAT.RARITY_LEGENDARY],
		[0, b.RarityCount - 1])
	_eq("tier names are the sim's", b.RarityName(CAT.RARITY_EPIC), "epic")

	var bad: String = ""
	for id in CAT.all_ids(b):
		var col: Color = CAT.rarity_colour(b, id)
		var want: Color = CAT.OBJECTIVE_COLOUR if b.GearKindOf(id) == CAT.KIND_OBJECTIVE \
			else CAT.RARITY_COLOURS[b.GearRarity(id)]
		if col != want:
			bad = "%s is drawn off its tier" % b.GearName(id)
		var rows: Array = TIP.rows_for(b, PackedInt32Array(), id)
		if b.GearKindOf(id) != CAT.KIND_OBJECTIVE and rows.size() > 0 \
				and rows[0][2] != b.RarityName(b.GearRarity(id)):
			bad = "%s's tooltip does not name its tier" % b.GearName(id)
	_check("every item is drawn in its tier's colour and its tooltip names it",
		bad.is_empty(), bad)
	_check("the five tiers are five different colours",
		CAT.RARITY_COLOURS[0] != CAT.RARITY_COLOURS[1]
		and CAT.RARITY_COLOURS[2] != CAT.RARITY_COLOURS[3]
		and CAT.RARITY_COLOURS[3] != CAT.RARITY_COLOURS[4])

	_eq("money has separators", CAT.money(18000), "$18,000")
	_eq("and handles the small and the negative", [CAT.money(0), CAT.money(950),
		CAT.money(-1234567)], ["$0", "$950", "-$1,234,567"])

	var sm: PackedInt32Array = PackedInt32Array(b.LevelSummary(
		FileAccess.get_file_as_string("res://levels/substation_4.txt")))
	_check("the summary carries the loot figures", sm.size() >= 11, "%d fields" % sm.size())
	if sm.size() >= 11:
		_check("chests and guards are both worth something", sm[9] > 0 and sm[10] > 0,
			"%d / %d" % [sm[9], sm[10]])
	var authored: PackedInt32Array = PackedInt32Array(b.LevelSummary("loot: 4321\nguard_loot: 100\n"
		+ FileAccess.get_file_as_string("res://levels/substation_4.txt")))
	_eq("an authored budget is the budget shown", authored[9], 4321)
	_eq("and so are the guards' points", authored[10], 100 * sm[2])


func _check_mirror(b: RefCounted) -> void:
	_eq("the sim still has eight slots", b.GearSlotCount, CAT.SLOT_COUNT)

	var names: Array = [
		[CAT.SLOT_HELMET, "helmet"],
		[CAT.SLOT_VEST, "vest"],
		[CAT.SLOT_BACKPACK, "backpack"],
		[CAT.SLOT_FOOTWARE, "footware"],
		[CAT.SLOT_CHEST, "shirt/chest"],
		[CAT.SLOT_ARMS, "arms"],
		[CAT.SLOT_PRIMARY, "primary weapon"],
		[CAT.SLOT_SECONDARY, "secondary weapon"],
	]
	var mismatch: int = 0
	for row in names:
		if b.GearSlotName(row[0]) != row[1]:
			mismatch += 1
	_eq("every slot constant names the slot the sim names", mismatch, 0)

	# The kind constants are ordinals too, so check one item of each kind lands
	# where this file thinks it does.
	_eq("a rifle is a weapon", b.GearKindOf(102), CAT.KIND_WEAPON)
	_eq("a vest is armour", b.GearKindOf(201), CAT.KIND_ARMOUR)
	_eq("a red dot is an attachment", b.GearKindOf(301), CAT.KIND_ATTACHMENT)
	_eq("a field pack is a pack", b.GearKindOf(502), CAT.KIND_PACK)
	_eq("a helmet is apparel", b.GearKindOf(402), CAT.KIND_APPAREL)
	_eq("the sim still has six weapon sub-slots", CAT.ATTACH_COUNT, 6)


func _check_equipping(b: RefCounted) -> void:
	# Stocked EXPLICITLY rather than from stock_default(). What a new campaign
	# starts with is an economy decision that changes as the game is balanced --
	# it is now a bare pistol and a satchel -- and a test about which slot an
	# item lands in should not fail every time that number moves.
	var s: RefCounted = STASH.new(b, 10, 6)
	for item_id in [102, 100, 502, 201, 402, 602, 701, 801, 301, 331]:
		s.add(item_id)
	_eq("the fixture stocks ten items", s.grid.count(), 10)

	var ak: int = _find(s, 102)
	_check("the AK is in the stash", ak != GRID.NONE)
	var before: int = s.grid.used_cells()
	_check("equipping it succeeds", s.equip_from_grid(ak))
	_eq("it fills the primary hand", s.equipped_in(CAT.SLOT_PRIMARY), 102)
	_eq("and left the grid", s.grid.used_cells(), before - 8)

	# The second weapon fills the empty hand rather than displacing the first.
	var glock: int = _find(s, 100)
	_check("equipping the Glock succeeds", s.equip_from_grid(glock))
	_eq("the rifle keeps the primary hand", s.equipped_in(CAT.SLOT_PRIMARY), 102)
	_eq("and the pistol takes the secondary", s.equipped_in(CAT.SLOT_SECONDARY), 100)

	# With both hands full, a third weapon displaces the primary.
	var mp7: int = s.add(101)
	_check("a third weapon fits the stash", mp7 != GRID.NONE)
	_check("equipping it succeeds", s.equip_from_grid(mp7))
	_eq("it takes the primary hand", s.equipped_in(CAT.SLOT_PRIMARY), 101)
	_check("and the rifle went back to the stash", _find(s, 102) != GRID.NONE)

	# Apparel goes only to its own slot, even if asked for another.
	var helmet: int = _find(s, 402)
	_check("the helmet equips", s.equip_from_grid(helmet))
	_eq("into the helmet slot", s.equipped_in(CAT.SLOT_HELMET), 402)
	var boots: int = _find(s, 602)
	_check("asking for the wrong slot is refused",
		not s.equip_from_grid(boots, CAT.SLOT_HELMET))
	_eq("and the helmet is untouched", s.equipped_in(CAT.SLOT_HELMET), 402)
	_check("the boots still equip to their own slot", s.equip_from_grid(boots))
	_eq("which is footware", s.equipped_in(CAT.SLOT_FOOTWARE), 602)

	# An attachment routes to a weapon sub-slot, never to a body slot.
	var dot: int = _find(s, 301)
	_check("the sight equips", s.equip_from_grid(dot))
	_eq("onto the sight sub-slot", s.attached_at(0), 301)

	_check("unequipping returns gear", s.unequip(CAT.SLOT_FOOTWARE))
	_eq("leaving the slot empty", s.equipped_in(CAT.SLOT_FOOTWARE), STASH.NONE)
	_check("unequipping an empty slot is refused", not s.unequip(CAT.SLOT_FOOTWARE))

	# All-or-nothing: the displaced item must have somewhere to land.
	var tight: RefCounted = STASH.new(b, 3, 3)
	_check("heavy plate fills a 3x3 stash", tight.equip_from_grid(tight.add(203)))
	var weave: int = tight.add(201)
	_check("light weave then fits", weave != GRID.NONE)
	for filler in [301, 302, 311, 312, 313]:
		tight.add(filler)
	_eq("and the rest is packed solid", tight.grid.free_cells(), 0)
	_check("a swap that cannot rehouse the displaced item is refused",
		not tight.equip_from_grid(weave))
	_eq("the heavy plate is untouched", tight.equipped_in(CAT.SLOT_VEST), 203)
	_eq("the light weave is still in the grid", tight.grid.item_of(weave), 201)
	_eq("and nothing was lost", tight.grid.free_cells(), 0)


func _check_apply(b: RefCounted) -> void:
	var s: RefCounted = STASH.new(b, 10, 6)

	# An empty stash resolves to the sim's own defaults: there is no unarmed
	# state, no pack, and so nothing can be looted.
	s.apply_to(b)
	_eq("an empty stash resolves to the default weapon", b.CurrentWeaponId, 0)
	_eq("an empty holster stays empty", b.CurrentSecondaryId, -1)
	_eq("and there is no armour", b.CurrentArmourId, 0)
	_eq("and no backpack", b.CurrentBackpackId, 0)

	_check("the AK equips", s.equip_from_grid(s.add(102)))
	_check("a Glock goes to the holster", s.equip_from_grid(s.add(100)))
	_check("heavy plate equips", s.equip_from_grid(s.add(203)))
	_check("a large pack equips", s.equip_from_grid(s.add(503)))
	s.apply_to(b)
	_eq("the primary reaches the sim", b.CurrentWeaponId, 2)
	_eq("by name", b.CurrentWeaponName, "AK-47")
	_eq("the holstered pistol reaches the sim", b.CurrentSecondaryId, 0)
	_check("and registers as a secondary", b.HasSecondary)
	_eq("the armour reaches the sim", b.CurrentArmourId, 3)
	_eq("the backpack reaches the sim", b.CurrentBackpackId, 503)

	# The backpack is the one apparel slot with a sim effect, so prove it lands.
	b.Restart(1)
	_eq("and sizes the mission pack", Vector2i(b.PackWidth, b.PackHeight), Vector2i(8, 5))
	_eq("which starts empty", b.PackUsedCells, 0)

	# The mask still holds across two weapons: the AK takes a grip, the Glock
	# does not, and it is the weapon in HAND that decides.
	_check("a tactical grip equips", s.equip_from_grid(s.add(312)))
	s.apply_to(b)
	_eq("the grip resolves onto the rifle", b.GetAttachment(1), 2)
	_check("the Glock has no grip slot", not b.WeaponHasSlot(0, 1))

	# Apparel with no reader must change nothing that reaches the sim.
	var before_weapon: int = b.CurrentWeaponId
	var before_armour: int = b.CurrentArmourId
	_check("a helmet equips", s.equip_from_grid(s.add(402)))
	_check("fatigues equip", s.equip_from_grid(s.add(701)))
	_check("gloves equip", s.equip_from_grid(s.add(801)))
	s.apply_to(b)
	_eq("apparel changes no weapon", b.CurrentWeaponId, before_weapon)
	_eq("and no armour", b.CurrentArmourId, before_armour)
	_eq("but is still worn", s.equipped_in(CAT.SLOT_HELMET), 402)


func _check_stash_text(b: RefCounted) -> void:
	# Roomy on purpose. stock_default alone fills 38 cells, so an 8x5 stash has
	# no space left to add anything, and the equips below would quietly no-op --
	# which is exactly how this test first failed.
	var s: RefCounted = STASH.new(b, 10, 8)
	s.stock_default()
	_check("the vest equips", s.equip_from_grid(s.add(203)))
	_check("the ammo mounts", s.equip_from_grid(s.add(341)))
	# Named explicitly: with both hands empty a weapon takes the PRIMARY one, so
	# asking for the holster is the only way to test the holster.
	_check("the pistol holsters", s.equip_from_grid(s.add(100), CAT.SLOT_SECONDARY))
	var text: String = s.to_text()

	var back: RefCounted = STASH.new(b, 1, 1)
	_eq("a clean stash save reports nothing skipped", back.from_text(text), 0)
	_eq("the grid round-trips", Vector2i(back.grid.w, back.grid.h), Vector2i(10, 8))
	_eq("the worn vest round-trips", back.equipped_in(CAT.SLOT_VEST), 203)
	_eq("the holstered pistol round-trips", back.equipped_in(CAT.SLOT_SECONDARY), 100)
	_eq("the mounted ammo round-trips", back.attached_at(4), 341)
	_eq("and the whole file round-trips", back.to_text(), text)

	# Totality: gear this build has never heard of is dropped, not fatal.
	var odd: RefCounted = STASH.new(b, 1, 1)
	var skipped: int = odd.from_text(
		"stash 2\ngrid 3 3\nitem 100 0 0 2 2 0\nequip 6 100\nequip 6 4242\nrubbish\n")
	_eq("unknown gear and rubbish are counted", skipped, 2)
	_eq("and the known part survived", odd.equipped_in(CAT.SLOT_PRIMARY), 100)


## First placement holding an item id, or GRID.NONE.
func _find(s: RefCounted, item_id: int) -> int:
	for pi in s.grid.live_ids():
		if s.grid.item_of(pi) == item_id:
			return pi
	return GRID.NONE


## The screens are Node2Ds, so they need a tree to get _ready(). The mouse hit
## tests cannot run headlessly -- get_local_mouse_position has no cursor to read
## -- so what is checked here is everything a drop does once a target is known,
## which is where gear could actually be lost.
func _check_screens(b: RefCounted) -> void:
	var host := Node.new()
	root.add_child(host)

	# Do not clobber a real stash while testing -- and do not depend on one
	# either. open_screen() loads this file when it exists, so leaving a
	# developer's saved stash in place would silently test "loaded a save"
	# instead of "stocked defaults", and the assertions below would fail on any
	# machine where the equipment screen had ever been used. Move it aside for
	# the duration and put it back afterwards.
	var save_path: String = "user://stash.txt"
	var had: bool = FileAccess.file_exists(save_path)
	var backup: String = FileAccess.get_file_as_string(save_path) if had else ""
	if had:
		DirAccess.remove_absolute(ProjectSettings.globalize_path(save_path))
	_check("the test starts with no saved stash", not FileAccess.file_exists(save_path))

	# The separate read-only pack screen is gone: there is ONE inventory now and
	# it is the equipment screen, which shows the pack alongside the stash.
	var eq: Node2D = load("res://game/stash_screen.gd").new()
	eq.bridge = b
	host.add_child(eq)
	_check("the equipment screen starts closed", not eq.active)
	_eq("and has no stash until opened", eq.stash, null)

	eq.open_screen()
	_check("opening builds a stash", eq.stash != null)
	_check("and stocks it", eq.stash.grid.count() > 0)

	# The panel is centred, and everything drawn must land inside it. The first
	# version of this layout ran its attachment rows to y=668 on a 620-tall
	# screen, so this is checked arithmetically rather than by eye.
	_eq("the panel is centred horizontally", eq.PANEL_X * 2.0 + eq.PANEL_W, 960.0)
	_eq("and vertically", eq.PANEL_Y * 2.0 + eq.PANEL_H, 620.0)
	_check("the panel fits the screen", eq.PANEL_W <= 960.0 and eq.PANEL_H <= 620.0)

	# The slots sit on a PAPER DOLL, so their order says nothing about where
	# the lowest one is: take the lowest of all of them.
	var slots_bottom: float = 0.0
	for slot in range(CAT.SLOT_COUNT):
		slots_bottom = maxf(slots_bottom, eq.slot_box(slot).end.y)
	_check("the slots fit inside the screen", slots_bottom <= eq.SCREEN_H)
	_check("the sub-slots start below them", eq.ATTACH_Y >= slots_bottom,
		"slots end %.0f, rails start %.0f" % [slots_bottom, eq.ATTACH_Y])
	_check("every slot is on the doll",
		eq.DOLL_AT.size() == CAT.SLOT_COUNT, "%d positions" % eq.DOLL_AT.size())

	var attach_bottom: float = eq.attach_box(CAT.ATTACH_COUNT - 1).end.y
	_check("and fit inside the panel too", attach_bottom <= eq.PANEL_H)
	_check("with room left for the footer", attach_bottom <= eq.PANEL_H - 38.0)

	# The MISSION PACK sits under the stash grid, in the middle column.
	# Everything here is arithmetic because the failure mode is text drawn over
	# text, which no other assertion can see. The pack's title and status are
	# two lines, the higher 24 px above it.
	var grid_bottom_y: float = eq.GRID_Y + eq.stash.grid.h * eq.CELL
	_check("the stash grid clears the pack header", grid_bottom_y < eq.PACK_Y - 36,
		"grid ends %.0f, pack header %.0f" % [grid_bottom_y, eq.PACK_Y - 36])
	_check("the grid is in the stash column, clear of the doll",
		eq.GRID_X >= eq.SLOT_X + eq.DOLL_W + 8.0)
	_check("and the rails", eq.GRID_X >= eq.attach_box(CAT.ATTACH_COUNT - 1).end.x + 8.0)
	_eq("the pack is under the grid", eq.PACK_X, eq.GRID_X)

	var pack_bottom: float = eq.PACK_Y + bridge_pack_rows(eq) * eq.PACK_CELL
	_check("the pack fits inside the screen", pack_bottom <= eq.SCREEN_H - 40.0,
		"pack ends %.0f of %.0f" % [pack_bottom, eq.PANEL_H])
	_check("the largest bag fits its column",
		eq.PACK_X + 8 * eq.PACK_CELL <= eq.DIVIDER)
	var bin: Rect2 = eq.bin_rect()
	_check("the field's bin fits the screen", bin.end.y <= eq.SCREEN_H - 40.0
		and bin.end.x <= eq.PANEL_W, "bin %s" % bin)

	# ---- dropping from the mission pack ----
	#
	# The drop only STAGES an index; main.gd hands it to the sim as recorded
	# intent. What is testable here is that the right things can be dropped and
	# the wrong things cannot.
	#
	# IN THE FIELD, because that is the only place a floor exists. A drop staged
	# at base was still staged when the next mission began, and the sim then
	# performed it on that run's first tick.
	var dropped_fired: Array = [false]
	eq.dropped.connect(func() -> void: dropped_fired[0] = true)

	var was_field: bool = eq.mission_mode
	eq.mission_mode = true
	eq.pending_drop = -1
	eq._drag = 4
	eq._drag_pi = 3
	eq._drag_item = 102
	_check("an item from the pack can be dropped", eq._drop_on_floor())
	_eq("and it stages that placement", eq.pending_drop, 3)
	_eq("with the item it holds", eq.pending_drop_item, 102)
	_check("and says so", dropped_fired[0])

	# Anything not from the pack is refused. Dropping a stash item on the floor
	# of a level would be throwing away something that is not there.
	for kind in [1, 2, 3]:
		eq.pending_drop = -1
		eq._drag = kind
		eq._drag_pi = 3
		eq._drag_item = 102
		_check("drag kind %d cannot be dropped on the floor" % kind,
			not eq._drop_on_floor())
		_eq("and stages nothing", eq.pending_drop, -1)

	eq.pending_drop = -1
	eq._drag = 0
	_check("and nor can nothing at all", not eq._drop_on_floor())

	# Nor can anything at all from base, whatever is being carried.
	eq.mission_mode = false
	eq.pending_drop = -1
	eq._drag = 4
	eq._drag_pi = 3
	eq._drag_item = 102
	_check("and nothing at all from base", not eq._drop_on_floor())
	_eq("which stages nothing either", eq.pending_drop, -1)
	eq.mission_mode = was_field
	eq._cancel_drag()

	var grid_right: float = eq.GRID_X + eq.stash.grid.w * eq.CELL
	var grid_bottom: float = eq.GRID_Y + eq.stash.grid.h * eq.CELL
	_check("the stash grid fits the panel width", grid_right <= eq.PANEL_W - eq.SLOT_X)
	_check("and the panel height", grid_bottom <= eq.PANEL_H)
	_check("the grid starts beside the rails, not under them",
		eq.GRID_X >= eq.attach_box(CAT.ATTACH_COUNT - 1).end.x)

	# Labels scale with the longest edge of the piece, so the biggest gun in the
	# game reads larger than a 1x1 attachment.
	_eq("a 1x1 gets the smallest label", CAT.label_size(Vector2i(1, 1)), CAT.LABEL_MIN)
	_check("a rifle gets a bigger one",
		CAT.label_size(Vector2i(4, 2)) > CAT.label_size(Vector2i(1, 1)))
	_eq("the longest edge drives it, not the width",
		CAT.label_size(Vector2i(1, 3)), CAT.label_size(Vector2i(3, 1)))
	_eq("and it is capped", CAT.label_size(Vector2i(99, 99)), CAT.LABEL_MAX)
	# A label must still fit the box it names: cell is 34px, so a 1x1 box is 32.
	_check("the smallest label fits the smallest box", CAT.label_size(Vector2i(1, 1)) < eq.CELL)

	# Four of the eight slots reach the sim; the screen must say so honestly.
	var reaches: int = 0
	for slot in range(CAT.SLOT_COUNT):
		if eq._slot_reaches_sim(slot):
			reaches += 1
	_eq("exactly four slots reach the sim", reaches, 4)
	_check("the primary does", eq._slot_reaches_sim(CAT.SLOT_PRIMARY))
	_check("the backpack does", eq._slot_reaches_sim(CAT.SLOT_BACKPACK))
	_check("the helmet does not", not eq._slot_reaches_sim(CAT.SLOT_HELMET))

	# Turning a carried item is free, because it is not in the grid yet.
	_eq("a carried item starts unturned", eq._drag_rot, GRID.ROT_NONE)
	eq._drag_rot_flip()
	_eq("and turns", eq._drag_rot, GRID.ROT_90)
	eq._drag_rot_flip()
	_eq("and back", eq._drag_rot, GRID.ROT_NONE)

	# Dropping a stash item onto the hand it belongs in.
	#
	# Added rather than assumed: a new campaign now opens with a bare pistol, so
	# there is no rifle lying in the stash to find. What is under test is the
	# DROP, not what the game hands out on day one.
	var ak: int = _find(eq.stash, 102)
	if ak == GRID.NONE:
		ak = eq.stash.add(102)
	_check("the AK is in the stash", ak != GRID.NONE)
	eq._drag = 1
	eq._drag_pi = ak
	eq._drag_item = 102
	_check("dropping it on the primary hand equips it",
		eq._drop_on_slot(CAT.SLOT_PRIMARY))
	_eq("and it is worn", eq.stash.equipped_in(CAT.SLOT_PRIMARY), 102)
	eq._cancel_drag()
	_eq("cancelling clears the carry", eq._drag, 0)

	# A rifle must not drop into the helmet slot.
	var glock: int = _find(eq.stash, 100)
	eq._drag = 1
	eq._drag_pi = glock
	eq._drag_item = 100
	_check("a weapon will not drop on the helmet slot",
		not eq._drop_on_slot(CAT.SLOT_HELMET))
	_eq("and the helmet slot is still empty",
		eq.stash.equipped_in(CAT.SLOT_HELMET), eq.stash.NONE)
	_eq("and the weapon is still in the stash", eq.stash.grid.item_of(glock), 100)
	eq._cancel_drag()

	# Dropping a worn item back on the grid returns it.
	eq._drag = 2
	eq._drag_slot = CAT.SLOT_PRIMARY
	eq._drag_item = 102
	_check("dropping the worn rifle on the grid unequips it",
		eq._drop_on_grid(Vector2i(0, 0)))
	_eq("the hand is empty", eq.stash.equipped_in(CAT.SLOT_PRIMARY), eq.stash.NONE)
	_check("and the rifle is back in the stash", _find(eq.stash, 102) != GRID.NONE)
	eq._cancel_drag()

	# Hand to hand, via the grid, without losing the weapon.
	var ak2: int = _find(eq.stash, 102)
	eq._drag = 1
	eq._drag_pi = ak2
	eq._drag_item = 102
	_check("the rifle equips again", eq._drop_on_slot(CAT.SLOT_PRIMARY))
	eq._cancel_drag()
	eq._drag = 2
	eq._drag_slot = CAT.SLOT_PRIMARY
	eq._drag_item = 102
	_check("moving it to the other hand succeeds",
		eq._drop_on_slot(CAT.SLOT_SECONDARY))
	_eq("it is in the secondary hand", eq.stash.equipped_in(CAT.SLOT_SECONDARY), 102)
	_eq("and out of the primary", eq.stash.equipped_in(CAT.SLOT_PRIMARY), eq.stash.NONE)
	eq._cancel_drag()

	# Dropping onto the same slot it came from must not consume it.
	eq._drag = 2
	eq._drag_slot = CAT.SLOT_SECONDARY
	eq._drag_item = 102
	_check("dropping a slot on itself changes nothing",
		not eq._drop_on_slot(CAT.SLOT_SECONDARY))
	_eq("and it is still worn", eq.stash.equipped_in(CAT.SLOT_SECONDARY), 102)
	eq._cancel_drag()

	# Closing writes the stash and resolves it into the sim.
	var fired: Array = [false]
	eq.closed.connect(func() -> void: fired[0] = true)
	eq.close_screen()
	_check("closing emits closed", fired[0])
	_check("and wrote the stash", FileAccess.file_exists(save_path))

	# What was written must load back identically.
	var written: String = FileAccess.get_file_as_string(save_path)
	var reload: RefCounted = STASH.new(b, 1, 1)
	_eq("the written stash reports nothing skipped", reload.from_text(written), 0)
	_eq("and round-trips", reload.to_text(), written)
	_eq("with the secondary still worn",
		reload.equipped_in(CAT.SLOT_SECONDARY), 102)

	# A reopened screen loads that file rather than restocking.
	var eq2: Node2D = load("res://game/stash_screen.gd").new()
	eq2.bridge = b
	host.add_child(eq2)
	eq2.open_screen()
	_eq("reopening loads the saved stash",
		eq2.stash.equipped_in(CAT.SLOT_SECONDARY), 102)

	# Restore whatever was there before.
	if had:
		var f := FileAccess.open(save_path, FileAccess.WRITE)
		if f != null:
			f.store_string(backup)
			f.close()
	else:
		DirAccess.remove_absolute(ProjectSettings.globalize_path(save_path))

	_check_screens_start(b, host)

	host.queue_free()


func _check_presets(b: RefCounted) -> void:
	var rows: Array = STASH.presets()
	_check("there are presets to choose from", rows.size() >= 3)

	var bad: int = 0
	var missing_primary: int = 0
	var missing_pack: int = 0
	for i in range(rows.size()):
		var row: Array = rows[i]
		if str(row[0]).is_empty() or str(row[1]).is_empty():
			bad += 1
		var items: Array = row[2]
		if items.is_empty():
			bad += 1
		var has_primary: bool = false
		var has_pack: bool = false
		for pair in items:
			if not b.GearExists(pair[0]):
				bad += 1
			if pair[1] == CAT.SLOT_PRIMARY:
				has_primary = true
			if pair[1] == CAT.SLOT_BACKPACK:
				has_pack = true
		if not has_primary:
			missing_primary += 1
		if not has_pack:
			missing_pack += 1
	_eq("every preset names real gear", bad, 0)
	_eq("every preset arms you", missing_primary, 0)
	# No bag means nothing can be looted, which would make a preset a trap.
	_eq("and every preset gives you a bag", missing_pack, 0)

	# Applying one wears it whole.
	# The DEFAULT size deliberately, not a roomier one. Testing presets against a
	# bigger stash than the game builds is exactly how the Breacher kit shipped
	# without its vest or its backpack.
	var s2: RefCounted = STASH.new(b)
	s2.stock_default()
	_eq("the default stash fits a whole preset", s2.apply_preset(1), 0)
	_eq("the Assault primary is the AK", s2.equipped_in(CAT.SLOT_PRIMARY), 102)
	_eq("with the Glock holstered", s2.equipped_in(CAT.SLOT_SECONDARY), 100)
	_eq("a field pack on the back", s2.equipped_in(CAT.SLOT_BACKPACK), 502)
	_eq("and the grip on the rifle", s2.attached_at(1), 312)

	# Switching presets must not leave the previous one's gear worn.
	_eq("switching to Breacher fits too", s2.apply_preset(2), 0)
	_eq("the shotgun is primary", s2.equipped_in(CAT.SLOT_PRIMARY), 103)
	_eq("heavy plate replaced the carrier", s2.equipped_in(CAT.SLOT_VEST), 203)
	_eq("the large pack replaced the field pack", s2.equipped_in(CAT.SLOT_BACKPACK), 503)
	# Breacher lists no attachments, so the grip must be gone, not inherited.
	_eq("and the previous preset's grip was stripped", s2.attached_at(1), STASH.NONE)

	# Marksman mounts two attachments on one weapon.
	_eq("Marksman fits", s2.apply_preset(3), 0)
	_eq("the scope is mounted", s2.attached_at(0), 303)
	_eq("and the extended mag", s2.attached_at(3), 331)

	# Every preset must apply WHOLE at the default stash size, and every part of
	# it that the sim can read must actually arrive. This is the regression: the
	# Breacher kit deployed with no vest and a 0x0 pack, because the items were
	# skipped for want of staging room and a skipped item is indistinguishable
	# from one you chose to leave behind.
	var incomplete: int = 0
	var no_pack: int = 0
	var no_vest: int = 0
	var no_weapon: int = 0
	for i in range(rows.size()):
		if s2.apply_preset(i) != 0:
			incomplete += 1
		s2.apply_to(b)
		b.Restart(1)
		if b.PackWidth <= 0 or b.PackHeight <= 0:
			no_pack += 1
		if b.CurrentArmourId <= 0:
			no_vest += 1
		if s2.equipped_in(CAT.SLOT_PRIMARY) == STASH.NONE:
			no_weapon += 1
	_eq("every preset applies whole at the default size", incomplete, 0)
	_eq("every preset arrives with a pack the sim can loot into", no_pack, 0)
	_eq("every preset arrives with armour on", no_vest, 0)
	_eq("and a weapon in hand", no_weapon, 0)

	# The fallback that guarantees it: even a stash with no spare cell at all
	# must still deploy a complete kit.
	var cramped: RefCounted = STASH.new(b, 2, 2)
	_eq("a 2x2 stash still applies a preset whole", cramped.apply_preset(2), 0)
	_eq("the shotgun arrived", cramped.equipped_in(CAT.SLOT_PRIMARY), 103)
	_eq("the heavy plate arrived", cramped.equipped_in(CAT.SLOT_VEST), 203)
	_eq("and so did the pack", cramped.equipped_in(CAT.SLOT_BACKPACK), 503)
	cramped.apply_to(b)
	b.Restart(1)
	_eq("which the sim can loot into", Vector2i(b.PackWidth, b.PackHeight), Vector2i(8, 5))

	_eq("an out-of-range preset is refused, not fatal", s2.apply_preset(99), -1)
	_eq("and so is a negative one", s2.apply_preset(-1), -1)


## The title screen and the mission half of the stash.
##
## These replaced the start menu, which answered "what am I carrying" before it
## answered "am I continuing something". Both of those are now separate screens
## and the order between them matters.
func _check_screens_start(b: RefCounted, host: Node) -> void:
	const CAMPAIGN2 := preload("res://game/campaign.gd")

	# The title reads the DISK to decide whether Continue is available, so the
	# real save has to be stashed and put back.
	var save_path: String = CAMPAIGN2.SAVE_PATH
	var had: bool = FileAccess.file_exists(save_path)
	var backup: String = FileAccess.get_file_as_string(save_path) if had else ""
	if had:
		DirAccess.remove_absolute(ProjectSettings.globalize_path(save_path))

	var camp: RefCounted = CAMPAIGN2.new()
	var ti: Node2D = load("res://game/title_screen.gd").new()
	ti.campaign = camp
	host.add_child(ti)

	_check("the title starts closed", not ti.active)
	_check("and with no save on disk, cannot continue", not ti.has_save())

	ti.open_screen()
	_check("it opens", ti.active and ti.visible)

	# The build stamp the title draws in its corner. It is read from
	# project.godot rather than typed into the screen, so that bumping the
	# version is one edit -- and an empty string would draw nothing at all,
	# silently, leaving every bug report about an unnamed build.
	_eq("the title names the build it is",
		ti._version,
		str(ProjectSettings.get_setting("application/config/version", "")))
	_check("and that build has a version at all", not ti._version.is_empty())
	_eq("and lands on New Game when there is nothing to continue",
		ti._row, ti.ROW_NEW)
	_check("Continue is disabled", not ti.row_enabled(ti.ROW_CONTINUE))
	_check("but the other three are not",
		ti.row_enabled(ti.ROW_NEW) and ti.row_enabled(ti.ROW_OPTIONS)
		and ti.row_enabled(ti.ROW_BUILDER))

	# The cursor must never come to rest on a row that does nothing.
	var landed_on_disabled: bool = false
	for i in range(12):
		ti.move(1)
		if not ti.row_enabled(ti._row):
			landed_on_disabled = true
	_check("moving never rests on a disabled row", not landed_on_disabled)

	# Confirming a disabled row emits nothing.
	var fired: Array = [0]
	ti.continue_requested.connect(func() -> void: fired[0] += 1)
	ti._row = ti.ROW_CONTINUE
	ti.confirm()
	_eq("confirming a disabled row does nothing", fired[0], 0)

	# With a save, Continue comes alive and is where it opens.
	camp.earn(500)
	_check("fixture: the campaign saves", camp.save())
	_check("the title now sees a save", ti.has_save())
	ti.open_screen()
	_eq("and opens on Continue", ti._row, ti.ROW_CONTINUE)
	ti.confirm()
	_eq("which now fires", fired[0], 1)

	# Each row emits its own signal and no other.
	var seen := {"new": 0, "options": 0, "builder": 0}
	ti.new_game_requested.connect(func() -> void: seen["new"] += 1)
	ti.options_requested.connect(func() -> void: seen["options"] += 1)
	ti.builder_requested.connect(func() -> void: seen["builder"] += 1)
	ti._row = ti.ROW_NEW
	ti.confirm()
	ti._row = ti.ROW_OPTIONS
	ti.confirm()
	ti._row = ti.ROW_BUILDER
	ti.confirm()
	_eq("New Game fires once", seen["new"], 1)
	_eq("Options fires once", seen["options"], 1)
	_eq("Level Builder fires once", seen["builder"], 1)
	_eq("and none of them fired Continue again", fired[0], 1)

	ti.close_screen()
	_check("the title closes", not ti.active and not ti.visible)

	# ---- the mission half of the stash ----
	var ss: Node2D = load("res://game/stash_screen.gd").new()
	ss.bridge = b
	ss.stash = STASH.new(b)
	ss.stash.stock_default()
	ss.campaign = camp
	host.add_child(ss)

	ss.open_screen()
	_check("the stash opens", ss.active and ss.visible)
	_check("and finds the levels", ss.level_count() > 0, "%d" % ss.level_count())
	_check("selecting a real file", ss.selected_level().ends_with(".txt"))

	var files: PackedStringArray = LEVELS.list()
	if files.size() > 1:
		ss.open_screen(files[files.size() - 1])
		_eq("opening on a level selects it", ss.selected_level(),
			files[files.size() - 1])
		ss.open_screen(files[0])
		var first: String = ss.selected_level()
		ss.cycle_level(1)
		_check("cycling moves on", ss.selected_level() != first)
		ss.cycle_level(-1)
		_eq("and back", ss.selected_level(), first)
		ss.cycle_level(-1)
		_eq("wrapping to the last", ss.selected_level(), files[files.size() - 1])

	# An unknown path falls back rather than selecting nothing.
	ss.open_screen("res://levels/does_not_exist.txt")
	_eq("an unknown level falls back to the first", ss.selected_level(), files[0])

	# Picking a mission must not disturb the kit, and vice versa.
	var kit_before: int = ss.stash.equipped_in(CAT.SLOT_PRIMARY)
	ss.cycle_level(1)
	_eq("changing mission leaves the kit alone",
		ss.stash.equipped_in(CAT.SLOT_PRIMARY), kit_before)

	# The two halves must not overlap: the inventory is left of the divider and
	# the missions are right of it.
	_check("the inventory stays left of the divider",
		ss.GRID_X + ss.stash.grid.w * ss.CELL <= ss.DIVIDER,
		"grid ends %.0f, divider %.0f" % [ss.GRID_X + ss.stash.grid.w * ss.CELL,
			ss.DIVIDER])
	for slot in range(CAT.SLOT_COUNT):
		_check("worn slot %d is left of the stash column" % slot,
			ss.slot_box(slot).end.x <= ss.COL2_X - 8.0,
			"slot ends %.0f" % ss.slot_box(slot).end.x)
	_check("and the sub-slots",
		ss.attach_box(CAT.ATTACH_COUNT - 1).end.x <= ss.COL2_X - 8.0)
	var dims: Vector2i = ss.pack_dims()
	_check("and the bag", ss.PACK_X + dims.x * ss.PACK_CELL <= ss.DIVIDER)
	_check("the missions stay right of it", ss.MISSION_X >= ss.DIVIDER)
	_check("and inside the screen", ss.MISSION_X + ss.MISSION_W <= 960.0)

	# Nothing on the inventory half may overlap anything else on it.
	var boxes: Array = []
	for i in range(CAT.SLOT_COUNT):
		boxes.append(ss.slot_box(i))
	for i in range(CAT.ATTACH_COUNT):
		boxes.append(ss.attach_box(i))
	boxes.append(Rect2(ss.GRID_X, ss.GRID_Y, ss.stash.grid.w * ss.CELL,
		ss.stash.grid.h * ss.CELL))
	# The bag at its LARGEST, and with its two header lines, whatever is worn.
	boxes.append(Rect2(ss.PACK_X, ss.PACK_Y - 36.0, 8 * ss.PACK_CELL,
		5 * ss.PACK_CELL + 36.0))
	var clashes: int = 0
	for i in range(boxes.size()):
		for j in range(i + 1, boxes.size()):
			if boxes[i].intersects(boxes[j]):
				clashes += 1
	_eq("nothing on the inventory half overlaps", clashes, 0)

	# Everything fits the screen.
	var lowest: float = 0.0
	for bx in boxes:
		lowest = maxf(lowest, bx.end.y)
	_check("and it all fits above the footer", lowest <= ss.SCREEN_H - 40.0,
		"lowest %.0f of %.0f" % [lowest, ss.SCREEN_H - 40.0])

	ss.close_screen_silent()
	_check("and it closes", not ss.active)

	if had:
		var f := FileAccess.open(save_path, FileAccess.WRITE)
		if f != null:
			f.store_string(backup)
	else:
		DirAccess.remove_absolute(ProjectSettings.globalize_path(save_path))
	_check("the harness left no campaign save behind",
		FileAccess.file_exists(save_path) == had)

## with no way back short of a reset.
## The RIGHT EDGE, which the fuzzer caught landing off-grid.
##
## set_pos used to snap and then clamp. Those look interchangeable and are not:
## the right edge sits at FIELD_W - size.x, so any element whose width is not a
## multiple of GRID was clamped to a position that was not on the grid — from
## the one function whose whole job is to keep it there.
func _check_hud_edges() -> void:
	var layout: RefCounted = HUD_LAYOUT.new()
	var off_grid: int = 0
	var off_screen: int = 0
	var widths := {}
	for e in HUD_LAYOUT.ELEMENTS:
		var id: String = e["id"]
		var sz: Vector2 = layout.size_of(id)
		widths[id] = sz
		# Shove it hard at each edge and each corner.
		for target in [Vector2(9999, 9999), Vector2(-9999, -9999),
				Vector2(9999, -9999), Vector2(-9999, 9999),
				Vector2(959, 619), Vector2(957, 617)]:
			layout.set_pos(id, target)
			var p: Vector2 = layout.pos_of(id)
			if fmod(p.x, HUD_LAYOUT.GRID) != 0.0 or fmod(p.y, HUD_LAYOUT.GRID) != 0.0:
				off_grid += 1
			if p.x < 0.0 or p.y < 0.0 or p.x + sz.x > 960.0 or p.y + sz.y > 620.0:
				off_screen += 1

	_eq("shoved against every edge, nothing lands off the grid", off_grid, 0)
	_eq("and nothing lands off the screen", off_screen, 0)

	# The fix must not have bought the grid by letting things overhang: at least
	# one element has a width that is NOT a grid multiple, which is what made
	# the old order wrong in the first place.
	var ragged: int = 0
	for id in widths:
		if fmod(widths[id].x, HUD_LAYOUT.GRID) != 0.0:
			ragged += 1
	_check("fixture: some element has an off-grid width", ragged > 0,
		"every element is a multiple of %d wide, so this cannot regress"
		% int(HUD_LAYOUT.GRID))


func _check_hud_layout() -> void:
	var host := Node.new()
	root.add_child(host)

	# The editor SAVES on close, and this harness exercises that path -- so it
	# would otherwise leave the player's real HUD arranged however the last
	# assertion happened to drag it. Stash whatever is on disk and put it back.
	var save_path: String = HUD_LAYOUT.SAVE_PATH
	var had_save: bool = FileAccess.file_exists(save_path)
	var saved_text: String = FileAccess.get_file_as_string(save_path) if had_save else ""

	var L: RefCounted = HUD_LAYOUT.new()

	# Every declared element must have a default and a non-empty box, or it is
	# unreachable in the editor and invisible in the game.
	var ids: Array = L.ids()
	_check("the layout declares elements", ids.size() >= 8, "%d" % ids.size())
	var complete: bool = true
	var overlapping: Array = []
	for id in ids:
		if not HUD_LAYOUT.DEFAULTS.has(id):
			complete = false
		if L.size_of(id).x <= 0.0 or L.size_of(id).y <= 0.0:
			complete = false
		if L.label_of(id).is_empty():
			complete = false
	_check("every element has a default, a size and a label", complete)

	# Unknown ids answer rather than crash, and say so by echoing the id.
	_eq("an unknown id has no size", L.size_of("nope"), Vector2.ZERO)
	_eq("and falls back to naming itself", L.label_of("nope"), "nope")
	_eq("and sits at the origin", L.pos_of("nope"), Vector2.ZERO)

	# Nothing starts off screen, and nothing starts stacked on something else.
	var on_screen: bool = true
	for id in ids:
		var r: Rect2 = L.rect_of(id)
		if r.position.x < 0.0 or r.position.y < 0.0:
			on_screen = false
		if r.end.x > HUD_LAYOUT.FIELD_W or r.end.y > HUD_LAYOUT.SCREEN_H:
			on_screen = false
	_check("every element starts on screen", on_screen)

	for i in range(ids.size()):
		for j in range(i + 1, ids.size()):
			if L.rect_of(ids[i]).intersects(L.rect_of(ids[j])):
				overlapping.append("%s/%s" % [ids[i], ids[j]])
	_check("and no two overlap in the shipped layout", overlapping.is_empty(),
		" ".join(overlapping))

	# Moving snaps to the grid and clamps to the screen.
	L.set_pos("alarm", Vector2(101, 203))
	var p: Vector2 = L.pos_of("alarm")
	_check("a move snaps to the grid",
		fmod(p.x, HUD_LAYOUT.GRID) == 0.0 and fmod(p.y, HUD_LAYOUT.GRID) == 0.0, str(p))

	L.set_pos("alarm", Vector2(-9999, -9999))
	_check("dragging off the top-left clamps", L.pos_of("alarm") == Vector2.ZERO,
		str(L.pos_of("alarm")))
	L.set_pos("alarm", Vector2(9999, 9999))
	var far: Rect2 = L.rect_of("alarm")
	_check("dragging off the bottom-right clamps",
		far.end.x <= HUD_LAYOUT.FIELD_W and far.end.y <= HUD_LAYOUT.SCREEN_H, str(far))

	_check("a moved element is no longer at its default", not L.is_default("alarm"))
	L.reset()
	_check("and reset puts it back", L.is_default("alarm"))

	# Hit testing picks the topmost box, and misses cleanly.
	_eq("empty space hits nothing", L.hit(Vector2(2, 300)), "")
	_eq("a point inside an element hits it",
		L.hit(L.rect_of("vitals").get_center()), "vitals")

	# Round-trip through the save format.
	L.set_pos("weapon", Vector2(40, 80))
	L.set_pos("records", Vector2(8, 120))
	var text: String = L.to_text()
	var M: RefCounted = HUD_LAYOUT.new()
	_eq("every element round-trips", M.from_text(text), L.ids().size())
	var same: bool = true
	for id in ids:
		if M.pos_of(id) != L.pos_of(id):
			same = false
	_check("and lands in the same places", same)

	# A corrupt or partial file must cost the arrangement, never the HUD.
	var N: RefCounted = HUD_LAYOUT.new()
	N.set_pos("alarm", Vector2(400, 400))
	_eq("garbage applies nothing", N.from_text("!!! nonsense\nweapon\nzz 1 2"), 0)
	_check("and falls back to the defaults", N.is_default("alarm"))
	_eq("an empty file applies nothing", N.from_text(""), 0)

	var O: RefCounted = HUD_LAYOUT.new()
	_eq("a half-written file applies what it can",
		O.from_text("weapon 40 80\nnot_an_element 1 1\nalarm bad bad\n"), 1)
	_check("the good line landed", O.pos_of("weapon") == Vector2(40, 80))
	_check("and the bad ones left defaults", O.is_default("alarm"))

	# An out-of-range saved position is clamped on load, so a file written by an
	# older build with a different screen cannot hide an element off screen.
	var P: RefCounted = HUD_LAYOUT.new()
	P.from_text("weapon 5000 5000\n")
	_check("a saved position past the screen is clamped on load",
		P.rect_of("weapon").end.x <= HUD_LAYOUT.FIELD_W, str(P.rect_of("weapon")))

	# ---- the editor ----
	var ed: Node2D = load("res://game/hud_editor.gd").new()
	ed.layout = L
	host.add_child(ed)

	_check("the hud editor starts closed", not ed.active)
	L.reset()
	ed.open_editor()
	_check("it opens", ed.active and ed.visible)

	# A drag moves exactly the element it grabbed, and nothing else.
	var before_weapon: Vector2 = L.pos_of("weapon")
	var others: Dictionary = {}
	for id in ids:
		others[id] = L.pos_of(id)

	var grab: Vector2 = L.rect_of("weapon").get_center()
	ed.press(grab)
	ed.drag(grab + Vector2(-100, -200))
	ed.release()
	_check("dragging moves the grabbed element", L.pos_of("weapon") != before_weapon)
	var disturbed: Array = []
	for id in ids:
		if id != "weapon" and L.pos_of(id) != others[id]:
			disturbed.append(id)
	_check("and disturbs no other", disturbed.is_empty(), " ".join(disturbed))

	# Pressing empty space grabs nothing, so a stray click cannot teleport an
	# element to the cursor.
	var held_before: Vector2 = L.pos_of("weapon")
	ed.press(Vector2(2, 300))
	ed.drag(Vector2(500, 500))
	ed.release()
	_check("a press on empty space grabs nothing", L.pos_of("weapon") == held_before)

	# ESC discards the session's changes; ENTER keeps them.
	L.reset()
	ed.open_editor()
	ed.press(L.rect_of("alarm").get_center())
	ed.drag(Vector2(300, 300))
	ed.release()
	_check("fixture: the element moved", not L.is_default("alarm"))
	ed.close_editor(false)
	_check("discarding restores what it was", L.is_default("alarm"))

	ed.open_editor()
	ed.press(L.rect_of("alarm").get_center())
	ed.drag(Vector2(300, 300))
	ed.release()
	ed.close_editor(true)
	_check("keeping leaves the move in place", not L.is_default("alarm"))

	# R resets everything at once.
	ed.open_editor()
	ed.reset_all()
	var all_default: bool = true
	for id in ids:
		if not L.is_default(id):
			all_default = false
	_check("R resets every element", all_default)
	ed.close_editor(false)
	_check("and it closes", not ed.active and not ed.visible)

	# Saving and reloading through the real file, then leaving the disk as we
	# found it.
	var S: RefCounted = HUD_LAYOUT.new()
	S.set_pos("alarm", Vector2(120, 240))
	_check("the layout saves", S.save())
	var T: RefCounted = HUD_LAYOUT.new()
	_check("and loads back", T.load_saved() == T.ids().size())
	_eq("with the moved element where it was left",
		T.pos_of("alarm"), Vector2(120, 240))

	if had_save:
		var f := FileAccess.open(save_path, FileAccess.WRITE)
		if f != null:
			f.store_string(saved_text)
	else:
		DirAccess.remove_absolute(ProjectSettings.globalize_path(save_path))
	_check("the harness left no layout behind",
		FileAccess.file_exists(save_path) == had_save)


## The campaign ledger and the shop that spends it.
##
## Every failure here is silent and expensive: a payout that double-counts, a
## purchase that charges for an item the stash refused, a save that loses your
## balance. None of it produces an error — just a number that is wrong.
func _check_campaign(b: RefCounted) -> void:
	var host := Node.new()
	root.add_child(host)

	# The harness writes the real save file, so stash what is there and put it
	# back -- a test run must not spend the player's money.
	var save_path: String = CAMPAIGN.SAVE_PATH
	var had_save: bool = FileAccess.file_exists(save_path)
	var saved_text: String = FileAccess.get_file_as_string(save_path) if had_save else ""

	var c: RefCounted = CAMPAIGN.new()
	_eq("a new campaign is broke", c.money, 0)
	_eq("with no runs behind it", c.runs, 0)

	# Payout is a pure function, so the debrief and the ledger cannot disagree.
	#
	# THE headline rule: no objective, no money. Not the mission fee, not the
	# records, not the fence. You keep the gear -- that is handled in the stash,
	# not here -- but a run that did not do the job is not paid for doing it.
	_eq("failing the objective pays nothing at all",
		CAMPAIGN.payout(false, 400, 9, 9, 9999), 0)
	_check("and completing it pays", CAMPAIGN.payout(true, 400, 0, 0, 0) > 0)
	_eq("a completed run pays the mission rate",
		CAMPAIGN.payout(true, 400, 0, 0, 0), 400)
	_check("provable evidence pays more than unprovable",
		CAMPAIGN.payout(true, 0, 1, 0, 0) > CAMPAIGN.payout(true, 0, 0, 1, 0))
	_eq("what the fence paid is added on top",
		CAMPAIGN.payout(true, 0, 0, 0, 500) - CAMPAIGN.payout(true, 0, 0, 0, 0), 500)
	_eq("nothing is charged to enter", CAMPAIGN.payout(true, 0, 0, 0, 0), 0)

	# The fence pays well under shop price, so overflowing the stash is a
	# consolation rather than a business model.
	_check("the fence pays less than half", CAMPAIGN.salvage_value(1000) < 500,
		"%d of 1000" % CAMPAIGN.salvage_value(1000))
	_check("but more than nothing", CAMPAIGN.salvage_value(1000) > 0)
	_eq("and nothing for nothing", CAMPAIGN.salvage_value(0), 0)

	# Settling banks exactly what payout() says.
	var paid: int = c.settle("m.txt", true, 400, 3, 2, 100, 5)
	_eq("settling pays the computed amount", paid,
		CAMPAIGN.payout(true, 400, 3, 2, 100))
	_eq("and banks it", c.money, paid)
	_eq("the breakdown adds up", c.total_of_last(), paid)
	_check("and it is recorded as completed", c.last_completed)
	_eq("a run is counted", c.runs, 1)
	_eq("and an extraction", c.extractions, 1)

	# Extracting WITHOUT the objective: counted, banked nothing.
	var before_fail: int = c.money
	var got: int = c.settle("m.txt", false, 400, 3, 2, 100, 5)
	_eq("an incomplete extraction pays nothing", got, 0)
	_eq("and the balance does not move", c.money, before_fail)
	_check("and it is not recorded as completed", not c.last_completed)
	_eq("but it still counts as a run", c.runs, 2)
	_eq("and as an extraction", c.extractions, 2)

	# Dying pays nothing, and says so.
	c.settle_loss("m.txt")
	_eq("dying pays nothing", c.money, before_fail)
	_eq("but still counts as a run", c.runs, 3)
	_eq("and not as an extraction", c.extractions, 2)
	_eq("with an empty breakdown", c.total_of_last(), 0)

	# Per-mission history, keyed by file name.
	var rec: Dictionary = c.mission_record("m.txt")
	_eq("the mission counted three runs", int(rec["runs"]), 3)
	_eq("one of them completed", int(rec["completions"]), 1)
	_eq("and remembers the best payout", int(rec["best"]), paid)
	var unknown: Dictionary = c.mission_record("never_played.txt")
	_eq("an unplayed mission has no runs", int(unknown["runs"]), 0)

	# Spending.
	_check("you can afford what you have", c.can_afford(paid))
	_check("and not what you do not", not c.can_afford(paid + 1))
	_check("a free item is never affordable, it is free", not c.can_afford(0))
	_check("spending succeeds", c.spend(100))
	_eq("and leaves the change", c.money, paid - 100)
	_check("overspending is refused", not c.spend(999999))
	_eq("and takes nothing", c.money, paid - 100)

	# Save round-trip.
	var text: String = c.to_text()
	var d: RefCounted = CAMPAIGN.new()
	_eq("a saved ledger reloads with nothing skipped", d.from_text(text), 0)
	_eq("with the money intact", d.money, c.money)
	_eq("and the run count", d.runs, c.runs)
	_eq("and the per-mission history",
		int(d.mission_record("m.txt")["runs"]), 3)
	_eq("including completions", int(d.mission_record("m.txt")["completions"]), 1)

	# A malformed mission line is skipped; a level that no longer exists is kept,
	# because deleting a file should not erase the history of having played it.
	var m: RefCounted = CAMPAIGN.new()
	_eq("a short mission line is skipped",
		m.from_text("campaign 2\nmission onlyname\n"), 1)
	_eq("a well-formed one for a missing level is kept",
		m.from_text("campaign 2\nmission gone.txt 4 2 900\n"), 0)
	_eq("with its figures", int(m.mission_record("gone.txt")["best"]), 900)

	# Total parser, like every other save here.
	var e: RefCounted = CAMPAIGN.new()
	e.earn(500)
	# Three bad lines, three skips: a bare "!!!", a key with no value, and a
	# value that is not a number.
	_eq("garbage skips its lines", e.from_text("!!!\nmoney\nmoney bananas\n"), 3)
	_eq("and resets rather than keeping stale money", e.money, 0)
	_eq("an empty file is not an error", e.from_text(""), 0)
	var f2: RefCounted = CAMPAIGN.new()
	f2.from_text("campaign 1\nmoney -50\n")
	_eq("a negative balance is clamped to zero", f2.money, 0)

	# ---- the shop ----
	var stash: RefCounted = STASH.new(b)
	var shop: Node2D = load("res://game/shop_screen.gd").new()
	shop.bridge = b
	shop.stash = stash
	shop.campaign = c
	host.add_child(shop)

	shop.open_screen()
	_check("the shop opens", shop.active and shop.visible)
	_check("and has stock", shop.row_count() > 0, "%d" % shop.row_count())

	# Stock comes from the catalogue and is priced and sorted.
	var stock: PackedInt32Array = PackedInt32Array(b.GetShopStock())
	var priced: bool = true
	var sorted: bool = true
	for i in range(stock.size()):
		if b.GearPrice(stock[i]) <= 0:
			priced = false
		if i > 0 and b.GearPrice(stock[i]) < b.GearPrice(stock[i - 1]):
			sorted = false
	_check("everything on sale has a price", priced)
	_check("and the list runs cheapest first", sorted)
	_check("the free starting pistol is not on the shelf", not stock.has(100))

	# Selection wraps.
	_eq("it starts on the first row", shop.selected_item(), stock[0])
	shop.move(-1)
	_eq("moving up from the top wraps", shop.selected_item(), stock[stock.size() - 1])
	shop.move(1)
	_eq("and back round", shop.selected_item(), stock[0])

	# Buying: money leaves, the item arrives.
	c.money = 100000
	var want: int = shop.selected_item()
	var owned_before: int = stash.count_of(want)
	var before_money: int = c.money
	_check("buying succeeds", shop.buy())
	_eq("the item is in the stash", stash.count_of(want), owned_before + 1)
	_eq("and the money is gone", c.money, before_money - b.GearPrice(want))

	# Too poor: nothing moves. The order matters -- charging for an item the
	# stash then refuses is the bug this guards.
	c.money = 0
	var poor_owned: int = stash.count_of(want)
	_check("buying broke fails", not shop.buy())
	_eq("and takes no money", c.money, 0)
	_eq("and delivers nothing", stash.count_of(want), poor_owned)

	# Stash full: refused, and NOT charged.
	var tiny: RefCounted = STASH.new(b, 1, 1)
	shop.stash = tiny
	c.money = 100000
	var rich: int = c.money
	# Fill the one cell so nothing else fits.
	tiny.add(801)
	shop.move(0)
	var full_ok: bool = shop.buy()
	_check("a full stash refuses the sale", not full_ok)
	_eq("and the money is untouched", c.money, rich)

	shop.stash = stash
	shop.close_screen()
	_check("the shop closes", not shop.active and not shop.visible)

	if had_save:
		var wf := FileAccess.open(save_path, FileAccess.WRITE)
		if wf != null:
			wf.store_string(saved_text)
	else:
		DirAccess.remove_absolute(ProjectSettings.globalize_path(save_path))
	_check("the harness left no campaign save behind",
		FileAccess.file_exists(save_path) == had_save)


## Rows the pack draws at its largest, for the layout check above.
func bridge_pack_rows(eq: Node2D) -> int:
	return maxi(5, eq.bridge.PackHeight)


## `closed` means ONE thing: the player backed out, and the handler for it
## returns to the title. A screen left because something else is taking over
## must go out SILENTLY.
##
## This is a real bug twice over. Deploying closed the stash loudly, so ENTER on
## the mission select deployed and was then thrown straight back to the main
## menu. Opening the HUD editor closed Options loudly, so the title opened at
## z 300 over an editor at z 200 that still held the keyboard.
func _check_screen_exits(b: RefCounted) -> void:
	var host := Node.new()
	root.add_child(host)

	# BOTH close paths call stash_screen._save(), which writes user://stash.txt
	# -- the player's REAL stash. Back it up byte for byte and put it back at the
	# end. Without this the harness silently empties the save of whoever runs it,
	# which is exactly what it did once.
	var save_path: String = "user://stash.txt"
	var had: bool = FileAccess.file_exists(save_path)
	var backup: String = FileAccess.get_file_as_string(save_path) if had else ""

	var stash: RefCounted = STASH.new(b)
	var ss: Node2D = load("res://game/stash_screen.gd").new()
	ss.bridge = b
	ss.stash = stash
	host.add_child(ss)

	var shouts: Array = []
	ss.closed.connect(func() -> void: shouts.append("stash"))

	ss.open_screen()
	_check("the stash screen opens", ss.active)
	ss.close_screen_silent()
	_check("a silent close deactivates it", not ss.active and not ss.visible)
	_eq("and says nothing -- this is the deploy path", shouts, [])

	ss.open_screen()
	ss.close_screen()
	_check("a loud close deactivates it too", not ss.active and not ss.visible)
	_eq("and announces the player backed out", shouts, ["stash"])

	var opt: Node2D = load("res://game/options_screen.gd").new()
	host.add_child(opt)
	var oshouts: Array = []
	opt.closed.connect(func() -> void: oshouts.append("options"))

	opt.open_screen()
	_check("options opens", opt.active)
	opt.close_screen_silent()
	_check("a silent close deactivates it", not opt.active and not opt.visible)
	_eq("and says nothing -- this is the HUD editor path", oshouts, [])

	opt.open_screen()
	opt.close_screen()
	_eq("a loud close announces it", oshouts, ["options"])

	# The z-order that made the second bug visible rather than merely wrong.
	var hud_ed: Node2D = load("res://game/hud_editor.gd").new()
	var title: Node2D = load("res://game/title_screen.gd").new()
	host.add_child(hud_ed)
	host.add_child(title)
	_check("the title still outranks the HUD editor", title.z_index > hud_ed.z_index,
		"title %d, editor %d" % [title.z_index, hud_ed.z_index])

	if had:
		var wf := FileAccess.open(save_path, FileAccess.WRITE)
		if wf != null:
			wf.store_string(backup)
	else:
		DirAccess.remove_absolute(ProjectSettings.globalize_path(save_path))
	# Byte-identical, not merely present: "the file still exists" is what an
	# emptied stash looks like too.
	_eq("the harness left the player's stash exactly as it found it",
		FileAccess.get_file_as_string(save_path) if had else "", backup)

	host.queue_free()


## E DURING A RUN. The screen shows only what is being carried, and the two
## things worth pinning are that it reads the SIM for the worn kit -- not the
## stash, which is what you left at base -- and that putting something on is
## STAGED rather than done, because equipping moves sim state.
func _check_mission_mode(b: RefCounted) -> void:
	var host := Node.new()
	root.add_child(host)

	var stash: RefCounted = STASH.new(b)
	stash.stock_default()
	var ss: Node2D = load("res://game/stash_screen.gd").new()
	ss.bridge = b
	ss.stash = stash
	host.add_child(ss)

	# A known kit in the SIM, deliberately different from the stash's.
	b.SetWeapon(2)          # AK-47
	b.SetSecondary(0)       # Glock
	b.SetArmour(3)          # heavy plate
	b.SetBackpack(503)
	b.Restart(1)

	ss.open_screen("", true)
	_check("E in a mission opens the field view", ss.mission_mode)
	_check("and the screen is up", ss.active and ss.visible)

	# ---- it reads the SIM, not the stash ----
	_eq("the primary is the sim's weapon", ss.worn_in(CAT.SLOT_PRIMARY), 102)
	_eq("the secondary too", ss.worn_in(CAT.SLOT_SECONDARY), 100)
	_eq("and the vest", ss.worn_in(CAT.SLOT_VEST), 203)
	_eq("and the pack", ss.worn_in(CAT.SLOT_BACKPACK), 503)
	stash._slots[CAT.SLOT_PRIMARY] = 109
	_eq("changing the STASH does not change what the field view shows",
		ss.worn_in(CAT.SLOT_PRIMARY), 102)

	# ---- EVERY worn slot is in this view ----
	# It used to show only the four the sim read, which meant looting a helmet
	# or a backpack gave you something with nowhere to put it.
	var shown: int = 0
	for slot in range(CAT.SLOT_COUNT):
		if ss.slot_box(slot).size.x > 0.0:
			shown += 1
	_eq("every worn slot is shown", shown, CAT.SLOT_COUNT)
	for slot in range(CAT.SLOT_COUNT):
		_check("slot %d has a box in the field" % slot,
			ss.slot_box(slot).size.x > 0.0)

	# ---- nothing overlaps, and it is all on screen ----
	var boxes: Array = []
	for slot in MISSION_SLOTS_OF(ss):
		boxes.append(ss.slot_box(slot))
	boxes.append(ss.bin_rect())
	var pack_box := Rect2(ss.pack_origin(),
		Vector2(b.PackWidth * ss.pack_cell(), b.PackHeight * ss.pack_cell()))
	boxes.append(pack_box)
	var clashes: int = 0
	for i in range(boxes.size()):
		for j in range(i + 1, boxes.size()):
			if boxes[i].intersects(boxes[j]):
				clashes += 1
	_eq("nothing in the field view overlaps", clashes, 0)
	for bx in boxes:
		_check("and it is on screen", bx.position.x >= 0.0 and bx.position.y >= 0.0
			and bx.end.x <= 960.0 and bx.end.y <= ss.SCREEN_H - 30.0,
			"%s" % bx)

	# ---- there is no stash grid to drop into ----
	_eq("no stash cell is reachable in the field", ss.cell_at(Vector2(100, 300)).x, -1)

	# ---- staging an equip ----
	ss.refresh_pack()
	var pack: PackedInt32Array = PackedInt32Array(b.GetPackPlacements())
	# Give the pack something to equip.
	b.Step(0, 0, 0, 0, 0, 1, 0, 101, 0, 0)     # an MP7
	ss.refresh_pack()
	pack = PackedInt32Array(b.GetPackPlacements())
	_check("fixture: the pack has an item", pack.size() >= 5)

	var placement: int = pack[0]
	ss._drag = 4
	ss._drag_pi = placement
	ss._drag_item = pack[1]
	_check("a weapon can be staged into the primary",
		ss._equip_from_pack(CAT.SLOT_PRIMARY))
	_check("which stages an equip", ss.pending_equip != 0)
	_eq("naming the placement", (ss.pending_equip & 0xFF) - 1, placement)
	_eq("and the slot", ss.pending_equip >> 8, CAT.SLOT_PRIMARY)
	_eq("and the item, for the caller to report", ss.pending_equip_item, pack[1])

	# Nothing has actually MOVED: the sim performs it on the next tick.
	_eq("but the sim has not been touched", b.GetWornSim()[CAT.SLOT_PRIMARY], 102)

	# ---- refusals mirror the sim's own rule ----
	ss.pending_equip = 0
	ss._drag = 4
	ss._drag_pi = placement
	ss._drag_item = pack[1]
	_check("a weapon does not go in the vest",
		not ss._equip_from_pack(CAT.SLOT_VEST))
	_eq("and nothing is staged", ss.pending_equip, 0)

	# A bag CAN be changed now, but only when the kit fits it -- the screen
	# asks the sim rather than deciding for itself.
	ss._drag = 4
	ss._drag_pi = placement
	ss._drag_item = 503
	_eq("whether a bag can be worn is the sim's answer",
		ss._equip_from_pack(CAT.SLOT_BACKPACK),
		b.CanEquipMidRun(503, CAT.SLOT_BACKPACK) and b.BackpackWouldHold(503))
	ss.pending_equip = 0
	# An attachment CAN be fitted in the field now, onto a weapon slot -- and
	# only onto one.
	ss.pending_equip = 0
	ss._drag = 4
	ss._drag_pi = placement
	ss._drag_item = 301                       # a red dot
	_check("an attachment fits a weapon slot",
		ss._equip_from_pack(CAT.SLOT_PRIMARY))
	ss.pending_equip = 0
	ss._drag = 4
	ss._drag_pi = placement
	ss._drag_item = 301
	_check("but not the helmet", not ss._equip_from_pack(CAT.SLOT_HELMET))
	_eq("and nothing is staged for it", ss.pending_equip, 0)
	ss._cancel_drag()

	# Whatever the screen refuses, the SIM must refuse too, and vice versa.
	# These two disagreeing is how a screen ends up promising something the
	# tick then silently ignores.
	var mismatch: int = 0
	for i in range(b.GearCount):
		var id: int = b.GearIdAt(i)
		for slot in range(CAT.SLOT_COUNT):
			var screen_ok: bool = b.CanEquipMidRun(id, slot)
			var kind: int = b.GearKindOf(id)
			var want: bool = (kind == CAT.KIND_WEAPON
				and (slot == CAT.SLOT_PRIMARY or slot == CAT.SLOT_SECONDARY)) \
				or (kind == CAT.KIND_ARMOUR and slot == CAT.SLOT_VEST) \
				or (kind == CAT.KIND_PACK and slot == CAT.SLOT_BACKPACK) \
				or (kind == CAT.KIND_APPAREL and slot == b.GearSlotOf(id)) \
				or (kind == CAT.KIND_ATTACHMENT
					and (slot == CAT.SLOT_PRIMARY or slot == CAT.SLOT_SECONDARY))
			if screen_ok != want:
				mismatch += 1
	_eq("the screen's rule is exactly the sim's", mismatch, 0)

	# Worn gear cannot be taken OFF in the field -- _press guards drag kinds 2
	# and 3 behind `not mission_mode`. That guard is not asserted here: _press
	# reads the real cursor, and a headless mouse sits at (0,0) where no slot
	# is, so any such test would pass whether the guard existed or not.

	# ---- at base, the same call is refused ----
	ss.close_screen_silent()
	ss.open_screen("", false)
	_check("reopening at base leaves mission mode", not ss.mission_mode)
	ss._drag = 4
	ss._drag_pi = placement
	ss._drag_item = pack[1]
	_check("equipping from the pack is refused at base",
		not ss._equip_from_pack(CAT.SLOT_PRIMARY))
	ss._cancel_drag()
	ss.close_screen_silent()

	host.queue_free()


## The field view's slot order, read off the screen so the test cannot drift
## from it.
func MISSION_SLOTS_OF(ss: Node2D) -> Array:
	return ss.MISSION_SLOTS


## F8. The menu's whole job is to reach the catalogue UNFILTERED, so most of
## this is about the things the shop deliberately hides still being there, and
## about a new sim GearKind not quietly becoming unspawnable.
func _check_dev_menu(b: RefCounted) -> void:
	var host := Node.new()
	root.add_child(host)

	var stash: RefCounted = STASH.new(b)
	var dev: Node2D = load("res://game/dev_menu.gd").new()
	dev.bridge = b
	dev.stash = stash
	host.add_child(dev)

	dev.open_screen()
	_check("the dev menu opens", dev.active and dev.visible)
	_eq("and lists the whole catalogue", dev.row_count(), b.GearCount)
	_eq("starting unfiltered", dev.tab_name(), "all")

	# The point of the screen: what the shop will not sell is still reachable.
	var stock: PackedInt32Array = PackedInt32Array(b.GetShopStock())
	var all_ids: PackedInt32Array = PackedInt32Array()
	for i in range(dev.row_count()):
		all_ids.append(dev.selected_item())
		dev.move(1)
	_eq("stepping the list returns to the top", dev.selected_item(), all_ids[0])
	_check("the free starting pistol is spawnable", all_ids.has(100))
	_check("though the shop will not sell it", not stock.has(100))
	_check("so is the mission objective", all_ids.has(900))
	_check("which the shop must never stock", not stock.has(900))

	# Selection wraps both ways.
	dev.move(-1)
	_eq("moving up from the top wraps", dev.selected_item(), all_ids[all_ids.size() - 1])
	dev.move(1)
	_eq("and back round", dev.selected_item(), all_ids[0])

	# Every tab admits only its own kind, and between them they admit the lot.
	# That second half is the guard: a GearKind added to the sim without a tab
	# here would be spawnable only from "all", and a kind added without a name
	# would draw as a number.
	var seen: Dictionary = {}
	var pure: bool = true
	for t in range(1, dev.KINDS.size()):
		dev.cycle_tab(1)
		var want: int = dev.KINDS[t]
		for i in range(dev.row_count()):
			var id: int = dev.selected_item()
			if b.GearKindOf(id) != want:
				pure = false
			seen[id] = true
			dev.move(1)
	_check("every tab shows only its own kind", pure)
	_eq("and between them they reach every item", seen.size(), b.GearCount)
	dev.cycle_tab(1)
	_eq("cycling past the end comes back to all", dev.tab_name(), "all")
	_eq("and the whole catalogue with it", dev.row_count(), b.GearCount)

	var named: bool = true
	for i in range(b.GearCount):
		var id: int = b.GearIdAt(i)
		if b.GearKindOf(id) < 0 or b.GearKindOf(id) >= dev.KIND_SINGULAR.size():
			named = false
	_check("every kind in the sim has a name here", named,
		"a GearKind was added to sim/GearCatalog.cs without a row tag")

	# ---- target: the stash when no run is under way ----
	dev.cycle_tab(0)
	dev.live = false
	_eq("with no run, ENTER targets the stash", dev.target_name(), "stash")

	var want_id: int = dev.selected_item()
	var before: int = stash.count_of(want_id)
	var got: Array = []
	var queued: Array = []
	dev.spawned.connect(func(x: int) -> void: got.append(x))
	dev.pack_spawn_requested.connect(func(x: int) -> void: queued.append(x))
	_check("spawning succeeds", dev.spawn())
	_eq("the item is in the stash", stash.count_of(want_id), before + 1)
	_eq("and the spawn was announced", got, [want_id])
	_eq("and nothing was queued for the pack", queued, [])

	# A full stash refuses rather than dropping it on the floor, which is the
	# bug stash.gd `_issue` exists to stop presets committing.
	var tiny: RefCounted = STASH.new(b, 1, 1)
	tiny.add(801)
	dev.stash = tiny
	_check("a full stash refuses the spawn", not dev.spawn())
	dev.stash = stash

	# ---- target: the mission pack mid-run ----
	# The menu must ASK rather than act. The pack feeds the state hash and rides
	# in the replay, so the item has to be conjured inside a tick through
	# InputFrame.SpawnItem -- a screen that placed it here would desync every
	# replay from that tick on.
	dev.live = true
	_eq("mid-run, ENTER targets the pack", dev.target_name(), "mission pack")

	got.clear()
	queued.clear()
	var stash_before: int = stash.count_of(want_id)
	var pack_before: int = b.GetPackPlacements().size()
	_check("spawning into the pack succeeds", dev.spawn())
	_eq("it is QUEUED, not performed", queued, [want_id])
	_eq("the stash is untouched", stash.count_of(want_id), stash_before)
	_eq("and so is the pack, until the sim steps",
		b.GetPackPlacements().size(), pack_before)
	_eq("nothing claimed to reach the stash", got, [])

	# The sim is what actually conjures it, through the recorded field.
	b.Step(0, 0, 0, 0, 0, 1, 0, want_id, 0, 0)
	_check("stepping with the spawn field puts it in the pack",
		b.GetPackPlacements().size() > pack_before,
		"%d -> %d" % [pack_before, b.GetPackPlacements().size()])

	dev.live = false

	# Layout, by arithmetic: every band inside the panel, in order, clear of the
	# next, and the panel itself on a 960x620 screen.
	var rows_bottom: float = dev.ROWS_Y + dev.VISIBLE_ROWS * dev.ROW_H
	var indicator: float = rows_bottom + 16.0
	var notice: float = dev.PANEL_H - 52.0
	var footer: float = dev.PANEL_H - 30.0
	var sub: float = dev.PANEL_H - 14.0
	_check("the rows clear the page indicator", indicator - rows_bottom >= 12.0)
	_check("the indicator clears the notice line", notice - indicator >= 12.0)
	_check("the notice clears the footer", footer - notice >= 12.0)
	_check("the footer clears its second line", sub - footer >= 12.0)
	_check("and the last line is inside the panel", sub <= dev.PANEL_H - 8.0)
	_check("the panel is on screen horizontally",
		dev.PANEL_X >= 0.0 and dev.PANEL_X + dev.PANEL_W <= 960.0)
	_check("and vertically",
		dev.PANEL_Y >= 0.0 and dev.PANEL_Y + dev.PANEL_H <= 620.0)

	# The tab strip is drawn from measured text, so adding a kind can silently
	# run it off the panel.
	var font: Font = ThemeDB.fallback_font
	var strip: float = 0.0
	for name in dev.KIND_NAMES:
		strip += font.get_string_size(name, HORIZONTAL_ALIGNMENT_LEFT, -1, 11).x + 20.0
	_check("the filter tabs fit across the panel", strip <= dev.PANEL_W - dev.PAD * 2.0,
		"strip %.0f px, room %.0f" % [strip, dev.PANEL_W - dev.PAD * 2.0])

	dev.close_screen()
	_check("the dev menu closes", not dev.active and not dev.visible)

	host.queue_free()


func _check_loot_panel(b: RefCounted) -> void:
	var host := Node.new()
	root.add_child(host)

	var lp: Node2D = load("res://game/loot_panel.gd").new()
	lp.bridge = b
	host.add_child(lp)

	_check("the panel starts hidden", not lp.active)
	_eq("and inspecting nothing returns nothing", lp.inspect_at(Vector2.ZERO), -1)

	var kit := PackedInt32Array([402, 201, 301])
	lp.show_for(0, kit)
	_check("holding loot over a body shows it", lp.active and lp.visible)

	# Nothing is identified to begin with.
	var hidden: int = 0
	for id in kit:
		if not lp.is_seen(0, id):
			hidden += 1
	_eq("every item starts unidentified", hidden, kit.size())

	# Identifying one row reveals that row's item, and only that one.
	var row0: Rect2 = lp.row_rect(0)
	_eq("the first row is where it says it is", lp.row_at(row0.get_center()), 0)
	_eq("identifying it reveals that item", lp.inspect_at(row0.get_center()), 402)
	_check("which is now identified", lp.is_seen(0, 402))
	_check("and the others are not", not lp.is_seen(0, 201) and not lp.is_seen(0, 301))

	# Inspecting the same row again is a no-op rather than a second reveal.
	_eq("inspecting it twice reveals nothing new", lp.inspect_at(row0.get_center()), -1)
	_check("but it stays identified", lp.is_seen(0, 402))

	var row2: Rect2 = lp.row_rect(2)
	_eq("the third row reveals the third item", lp.inspect_at(row2.get_center()), 301)

	# A click outside every row does nothing.
	_eq("clicking off the rows reveals nothing",
		lp.inspect_at(Vector2(4, 4)), -1)
	_eq("and row_at says so", lp.row_at(Vector2(4, 4)), -1)

	# Dwell to identify, click to take. A click on a mystery must NEVER return a
	# pick, or an unidentified item would leave the body without being named --
	# and it must not identify it either, or the dwell bar would be decoration
	# that players learn to click past.
	# On its own guard index, so it cannot disturb what the assertions above and
	# below have established about bodies 0 and 1.
	lp.show_for(7, kit)
	var first: int = lp.click_at(lp.row_rect(1).get_center())
	_eq("clicking a mystery takes nothing", first, 0)
	_check("and does not identify it either", not lp.is_seen(7, 201))

	# The dwell is what identifies, and it takes IDENTIFY_SECONDS of held cursor.
	var centre: Vector2 = lp.row_rect(1).get_center()
	_check("a partial dwell is not enough",
		lp.advance_identify(centre, lp.IDENTIFY_SECONDS * 0.5) < 1.0)
	_check("and leaves the row unidentified", not lp.is_seen(7, 201))
	_eq("a partial dwell still takes nothing", lp.click_at(centre), 0)

	# Progress is kept rather than reset, so the rest of the dwell finishes it.
	_check("the dwell resumes where it left off",
		lp.identify_progress(7, 201) > 0.0)
	_eq("completing the dwell identifies it",
		lp.advance_identify(centre, lp.IDENTIFY_SECONDS * 0.6), 1.0)
	_check("which is now identified", lp.is_seen(7, 201))

	var second: int = lp.click_at(centre)
	_eq("and now a click picks that row", second, 2)
	_eq("which is the row index plus one, so zero can mean no pick", second, 1 + 1)

	# Dwelling on one row must not advance any other.
	_eq("the neighbour was untouched by it", lp.identify_progress(7, 301), 0.0)
	_eq("so clicking it still picks nothing", lp.click_at(lp.row_rect(2).get_center()), 0)
	lp.mark_seen(7, 301)
	_eq("and only picks once identified", lp.click_at(lp.row_rect(2).get_center()), 3)

	# A dwell off the rows advances nothing at all.
	_eq("dwelling off the rows does nothing",
		lp.advance_identify(Vector2(4, 4), lp.IDENTIFY_SECONDS), 0.0)

	_eq("clicking off the rows picks nothing", lp.click_at(Vector2(4, 4)), 0)
	lp.hide_panel()
	_eq("and a hidden panel picks nothing", lp.click_at(lp.row_rect(0).get_center()), 0)
	lp.show_for(0, kit)

	# What was learnt about one body says nothing about another.
	_check("another body is still a mystery", not lp.is_seen(1, 402))
	lp.show_for(1, kit)
	_eq("so its rows are identified in turn", lp.inspect_at(lp.row_rect(0).get_center()), 402)
	_check("without disturbing the first body", lp.is_seen(0, 402))

	# Walking away hides it but keeps what was learnt, so coming back is not a
	# fresh mystery.
	lp.hide_panel()
	_check("walking away hides it", not lp.active)
	_check("but what was learnt is kept", lp.is_seen(0, 402))

	# A new run means new bodies.
	lp.forget_all()
	_check("restarting forgets it all", not lp.is_seen(0, 402))
	_check("for every body", not lp.is_seen(1, 402))

	# An emptied body must not crash the panel.
	lp.show_for(0, PackedInt32Array())
	_eq("a stripped body has no rows", lp.row_at(lp.row_rect(0).get_center()), -1)
	_eq("and nothing to inspect", lp.inspect_at(lp.row_rect(0).get_center()), -1)
	_eq("and nothing to dwell on",
		lp.advance_identify(lp.row_rect(0).get_center(), 1.0), 0.0)

	# The panel has to stay on screen, however heavy the kit.
	lp.show_for(0, PackedInt32Array([402, 201, 301, 331, 341, 502, 602, 701, 801]))
	var last: Rect2 = lp.row_rect(8)
	_check("a nine-item body still fits the screen", last.end.y <= 620.0)
	_check("and stays inside the right edge", last.end.x <= 960.0)

	host.queue_free()


## EVERY WAY AN ITEM CAN MOVE, driven the way the player drives it: a press at a
## point and a release at another point.
##
## This is the hole the rest of this file left open. _check_screens asserts what
## _drop_on_slot and _drop_on_grid do ONCE A TARGET IS KNOWN, and says so -- the
## hit tests "cannot run headlessly" because they read the cursor. So the half
## that decides WHICH target a release names was never tested at all, and a
## destination the screen draws but _release does not handle looks exactly like
## a drag that did nothing.
##
## press_at/release_at take the point for that reason. Every coordinate below is
## the CENTRE of a box the screen itself draws, so a layout change moves the test
## with it rather than breaking it.
func _check_drag_paths(b: RefCounted) -> void:
	var host := Node.new()
	root.add_child(host)

	var stash: RefCounted = STASH.new(b)
	var ss: Node2D = load("res://game/stash_screen.gd").new()
	ss.bridge = b
	ss.stash = stash
	host.add_child(ss)

	# A known grid, placed by hand so every coordinate below is arithmetic
	# rather than a search: AK at (0,0) 4x2, weave at (0,2) 2x2, red dot at
	# (0,4) 1x1, satchel at (4,2) 2x2, Glock at (6,0) 2x2.
	stash.grid.place(102, 4, 2, 0, 0, GRID.ROT_NONE)
	stash.grid.place(201, 2, 2, 0, 2, GRID.ROT_NONE)
	stash.grid.place(301, 1, 1, 0, 4, GRID.ROT_NONE)
	stash.grid.place(501, 2, 2, 4, 2, GRID.ROT_NONE)
	stash.grid.place(100, 2, 2, 6, 0, GRID.ROT_NONE)
	ss.open_screen("")
	_check("the stash opens at base, not in the field", not ss.mission_mode)

	# ---- stash grid -> a worn slot ----
	_drag_to(ss, _cell_point(ss, 0, 2), _slot_point(ss, CAT.SLOT_VEST))
	_eq("stash to a worn slot puts it on", stash.equipped_in(CAT.SLOT_VEST), 201)
	_eq("and it is out of the grid", stash.grid.placement_at(0, 2), GRID.NONE)
	_eq("and the drag is over", ss._drag, 0)

	# ---- a worn slot -> the stash grid ----
	_drag_to(ss, _slot_point(ss, CAT.SLOT_VEST), _cell_point(ss, 0, 2))
	_eq("a worn slot back to the grid takes it off",
		stash.equipped_in(CAT.SLOT_VEST), STASH.NONE)
	_check("and the item is in the stash again", ss._find_placement(201) != GRID.NONE)

	# ---- grid -> a slot it does not belong in ----
	_drag_to(ss, _cell_point(ss, 0, 0), _slot_point(ss, CAT.SLOT_HELMET))
	_eq("a rifle does not go on your head", stash.equipped_in(CAT.SLOT_HELMET),
		STASH.NONE)
	_eq("and the rifle is still where it was", stash.grid.placement_at(0, 0),
		ss._find_placement(102))

	# ---- grid -> grid, the move that makes packing a decision ----
	var ak: int = ss._find_placement(102)
	_drag_to(ss, _cell_point(ss, 0, 0), _cell_point(ss, 6, 6))
	_eq("an item moves within the grid", stash.grid.pos_of(ak), Vector2i(6, 6))
	_eq("leaving its old cells empty", stash.grid.placement_at(0, 0), GRID.NONE)

	# ---- grid -> a cell it does not fit ----
	_drag_to(ss, _cell_point(ss, 6, 6), _cell_point(ss, 6, 0))
	_eq("a move that would overlap is refused", stash.grid.pos_of(ak),
		Vector2i(6, 6))
	_eq("and the item it would have landed on is untouched",
		stash.grid.placement_at(6, 0), ss._find_placement(100))

	# ---- grid -> nothing at all ----
	_drag_to(ss, _cell_point(ss, 6, 6), Vector2(ss.MISSION_X + 40, 300))
	_eq("a drop on nothing is a cancel, not a loss", stash.grid.pos_of(ak),
		Vector2i(6, 6))
	_eq("and nothing is left carried", ss._drag, 0)

	# ---- two weapons, one hand each ----
	_drag_to(ss, _cell_point(ss, 6, 6), _slot_point(ss, CAT.SLOT_PRIMARY))
	_drag_to(ss, _cell_point(ss, 6, 0), _slot_point(ss, CAT.SLOT_SECONDARY))
	_eq("a rifle goes in the primary hand", stash.equipped_in(CAT.SLOT_PRIMARY), 102)
	_eq("and a pistol in the other", stash.equipped_in(CAT.SLOT_SECONDARY), 100)

	# ---- slot -> slot: swapping hands ----
	_drag_to(ss, _slot_point(ss, CAT.SLOT_PRIMARY), _slot_point(ss, CAT.SLOT_SECONDARY))
	_eq("dragging one hand onto the other swaps them",
		stash.equipped_in(CAT.SLOT_SECONDARY), 102)
	_eq("and what was there comes back", stash.equipped_in(CAT.SLOT_PRIMARY), 100)

	# ---- grid -> a worn slot that is already full ----
	var weave: int = ss._find_placement(201)
	var at: Vector2i = stash.grid.pos_of(weave)
	_drag_to(ss, _cell_point(ss, at.x, at.y), _slot_point(ss, CAT.SLOT_VEST))
	_eq("fixture: a vest is worn", stash.equipped_in(CAT.SLOT_VEST), 201)
	stash.add(203)
	var plate: int = ss._find_placement(203)
	var pat: Vector2i = stash.grid.pos_of(plate)
	_drag_to(ss, _cell_point(ss, pat.x, pat.y), _slot_point(ss, CAT.SLOT_VEST))
	_eq("a second vest displaces the first", stash.equipped_in(CAT.SLOT_VEST), 203)
	_check("and the one it displaced lands back in the grid",
		ss._find_placement(201) != GRID.NONE)

	# ---- grid -> a weapon sub-slot ----
	#
	# The screen DRAWS six attachment boxes and press_at can lift an attachment
	# out of one, so a release on one has to mean something. It named no
	# destination at all: not a slot, not a cell, so the release fell through to
	# the cancel and the drag simply evaporated.
	var dot: int = ss._find_placement(301)
	var dat: Vector2i = stash.grid.pos_of(dot)
	_drag_to(ss, _cell_point(ss, dat.x, dat.y), _attach_point(ss, 0))
	_eq("an attachment dropped on its sub-slot is fitted",
		stash.attached_at(0), 301)
	_eq("and leaves the grid", ss._find_placement(301), GRID.NONE)

	# ---- a weapon sub-slot -> the stash grid ----
	_drag_to(ss, _attach_point(ss, 0), _cell_point(ss, 0, 4))
	_eq("dragging it off the gun takes it off", stash.attached_at(0), STASH.NONE)
	_check("and puts it back in the stash", ss._find_placement(301) != GRID.NONE)

	# ---- sub-slot -> sub-slot ----
	stash.add(303)
	var scope: Vector2i = stash.grid.pos_of(ss._find_placement(303))
	_drag_to(ss, _cell_point(ss, scope.x, scope.y), _attach_point(ss, 0))
	_eq("fixture: a scope is fitted", stash.attached_at(0), 303)
	_drag_to(ss, _attach_point(ss, 0), _attach_point(ss, 1))
	_eq("a fitted attachment dragged to the wrong sub-slot stays put",
		stash.attached_at(0), 303)
	_eq("and does not land in the wrong one", stash.attached_at(1), STASH.NONE)

	# ---- the FLOOR is not reachable from base ----
	var n_before: int = stash.grid.count()
	var bag: int = ss._find_placement(501)
	_check("fixture: the spare bag is in the grid", bag != GRID.NONE)
	var bat: Vector2i = stash.grid.pos_of(bag)
	_drag_to(ss, _cell_point(ss, bat.x, bat.y), _bin_point(ss))
	_eq("nothing in the stash can be thrown on the floor of a level",
		stash.grid.count(), n_before)
	_eq("and it has not moved", stash.grid.pos_of(bag), bat)

	# ---- the stash grid -> THE BAG ----
	#
	# What you choose to take with you. It LEAVES the stash, which is the whole
	# point: what goes with you is what you lose when you die.
	var sack: int = stash.add(503)                   # large pack, 8x5
	_check("fixture: a bag is in the stash", sack != GRID.NONE)
	var sackat: Vector2i = stash.grid.pos_of(sack)
	_drag_to(ss, _cell_point(ss, sackat.x, sackat.y), _slot_point(ss, CAT.SLOT_BACKPACK))
	_eq("fixture: the bag is worn", stash.equipped_in(CAT.SLOT_BACKPACK), 503)
	ss.refresh_pack()
	_check("so the pack panel has a size now", ss.pack_dims().x > 0)

	var ammo: int = stash.add(343)
	var aat: Vector2i = stash.grid.pos_of(ammo)
	_drag_to(ss, _cell_point(ss, aat.x, aat.y), _pack_area_point(ss))
	_eq("dragging onto the bag packs it", stash.carried.size(), 1)
	_eq("naming the item", stash.carried[0], 343)
	_eq("AND IT IS GONE FROM THE STASH GRID",
		stash.grid.placement_at(aat.x, aat.y), GRID.NONE)
	_eq("but it is still yours", stash.count_of(343), 1)
	_check("and the panel now draws it", ss._pack.size() >= 5)

	# ...and back out again.
	_drag_to(ss, _pack_point(ss, ss._pack), _cell_point(ss, aat.x, aat.y))
	_eq("dragging it back out unpacks it", stash.carried.size(), 0)
	_check("and it is in the stash again", ss._find_placement(343) != GRID.NONE)
	_eq("still exactly one of it", stash.count_of(343), 1)

	# Worn gear does not go in the bag: it comes off into the stash first.
	_drag_to(ss, _slot_point(ss, CAT.SLOT_BACKPACK), _pack_area_point(ss))
	_eq("a worn slot cannot be dropped into the bag", stash.carried.size(), 0)
	_eq("and the bag is still on your back",
		stash.equipped_in(CAT.SLOT_BACKPACK), 503)

	# ---- a swap with nowhere to put what it displaces ----
	# The grid is the only place a displaced item can go, so a full one has to
	# refuse the whole transaction rather than half-applying it and losing one.
	var worn_vest: int = stash.equipped_in(CAT.SLOT_VEST)
	var spare: int = stash.add(202)
	_check("fixture: a second vest is in the grid", spare != GRID.NONE)
	var sat: Vector2i = stash.grid.pos_of(spare)
	while stash.add(801) != GRID.NONE:
		pass                      # 1x1 gloves until not one cell is left
	_drag_to(ss, _cell_point(ss, sat.x, sat.y), _slot_point(ss, CAT.SLOT_VEST))
	_eq("a swap with nowhere to put the displaced item is refused",
		stash.equipped_in(CAT.SLOT_VEST), worn_vest)
	_eq("and the one that would have gone on is where it was",
		stash.grid.pos_of(spare), sat)

	# ---- the MISSION PACK at base ----
	#
	# Read-only here: a pack is sim state and rides in a replay, and base is not
	# a place where a drop on the floor means anything.
	b.Restart(1)
	b.Step(0, 0, 0, 0, 0, 1, 0, 101, 0, 0)         # conjure an MP7 into the pack
	ss.refresh_pack()
	var pack: PackedInt32Array = PackedInt32Array(b.GetPackPlacements())
	_check("fixture: the SIM's pack has something in it", pack.size() >= 5)

	# The base panel shows WHAT YOU PACKED, never what the sim is still
	# holding: the run that filled it has ended and _settle_run has already
	# banked every one of those items into the stash.
	_eq("the pack draws what YOU packed, not what the sim is still holding",
		ss._pack.size(), 0)
	_eq("but it is sized by the bag you will wear", ss.pack_dims(),
		Vector2i(b.GearPackW(stash.equipped_in(CAT.SLOT_BACKPACK)),
			b.GearPackH(stash.equipped_in(CAT.SLOT_BACKPACK))))

	if pack.size() >= 5:
		var ppt: Vector2 = _pack_point(ss, pack)
		ss.pending_drop = -1
		ss.pending_equip = 0
		_eq("so nothing can be lifted out of it here",
			ss.pack_placement_at(ppt), -1)
		_drag_to(ss, ppt, _slot_point(ss, CAT.SLOT_PRIMARY))
		_eq("the mission pack does not arm you at base", ss.pending_equip, 0)
		_drag_to(ss, ppt, _bin_point(ss))
		_eq("and nothing can be dropped on a floor you are not standing on",
			ss.pending_drop, -1)

	# ---- a drag cannot outlive the button that started it ----
	# Releasing outside the window delivers no button-up, so _drag stayed set
	# and press_at then refused every click for the rest of the session.
	var bagp: Vector2 = _cell_point(ss, bat.x, bat.y)
	ss.press_at(bagp)
	_check("fixture: something is being carried", ss._drag != 0)
	ss._process(0.016)
	_eq("a drag with no button behind it is dropped", ss._drag, 0)
	ss.press_at(bagp)
	_check("and the screen takes presses again", ss._drag != 0)
	ss._cancel_drag()

	host.queue_free()


# --------------------------------------------------- drag-path coordinates
#
# Every point is the CENTRE of a box the screen draws, taken from the screen's
# own geometry, so a layout change moves these with it.

func _drag_to(ss: Node2D, from: Vector2, to: Vector2) -> void:
	ss.press_at(from)
	ss.release_at(to)


func _cell_point(ss: Node2D, cx: int, cy: int) -> Vector2:
	return Vector2(ss.GRID_X + (cx + 0.5) * ss.CELL,
		ss.GRID_Y + (cy + 0.5) * ss.CELL)


func _slot_point(ss: Node2D, slot: int) -> Vector2:
	return ss.slot_box(slot).get_center()


func _attach_point(ss: Node2D, sub: int) -> Vector2:
	return ss.attach_box(sub).get_center()


func _bin_point(ss: Node2D) -> Vector2:
	return ss.bin_rect().get_center()


## The middle of the pack PANEL, empty or not: a bag has to be droppable into
## before anything is in it.
func _pack_area_point(ss: Node2D) -> Vector2:
	var dims: Vector2i = ss.pack_dims()
	var cw: float = ss.pack_cell()
	return ss.pack_origin() + Vector2(dims.x * cw, dims.y * cw) * 0.5


## The centre of the first entry in a GetPackPlacements run.
func _pack_point(ss: Node2D, pack: PackedInt32Array) -> Vector2:
	var o: Vector2 = ss.pack_origin()
	var cw: float = ss.pack_cell()
	return Vector2(o.x + (pack[2] + 0.5) * cw, o.y + (pack[3] + 0.5) * cw)


## THE FIELD VIEW, and the floor. The same drags, inside a run, where the rules
## are different: nothing comes OFF, the stash does not exist, and everything
## that does happen is STAGED for the sim rather than performed here.
func _check_field_drag_paths(b: RefCounted) -> void:
	var host := Node.new()
	root.add_child(host)

	var stash: RefCounted = STASH.new(b)
	stash.stock_default()
	var ss: Node2D = load("res://game/stash_screen.gd").new()
	ss.bridge = b
	ss.stash = stash
	host.add_child(ss)

	b.SetWeapon(2)                      # AK-47
	b.SetSecondary(-1)
	b.SetArmour(0)
	b.SetBackpack(503)                  # the big bag, so there is room to work
	# Nothing carried IN, so the pack holds only what this test puts there.
	# The block above leaves a kit staged on the bridge, and Restart packs it.
	b.ClearCarried()
	b.Restart(1)
	b.Step(0, 0, 0, 0, 0, 1, 0, 201, 0, 0)         # a light weave into the pack
	ss.open_screen("", true)
	ss.refresh_pack()
	_check("the field view is up", ss.active and ss.mission_mode)

	var pack: PackedInt32Array = PackedInt32Array(b.GetPackPlacements())
	_check("fixture: the pack has the vest", pack.size() >= 5)
	if pack.size() < 5:
		host.queue_free()
		return
	var ppt: Vector2 = _pack_point(ss, pack)

	# ---- pack -> a worn slot ----
	_drag_to(ss, ppt, _slot_point(ss, CAT.SLOT_VEST))
	_check("pack to a worn slot stages an equip", ss.pending_equip != 0)
	_eq("naming the placement", (ss.pending_equip & 0xFF) - 1, pack[0])
	_eq("and the slot", ss.pending_equip >> 8, CAT.SLOT_VEST)
	_eq("but nothing has moved yet -- the tick does it",
		b.GetWornSim()[CAT.SLOT_VEST], 0)

	# The sim performs it, exactly as main.gd hands it over.
	b.Step(0, 0, 0, 0, 0, 1, 0, 0, ss.pending_equip, 0)
	ss.pending_equip = 0
	ss.refresh_pack()
	_eq("and the next tick puts it on", b.GetWornSim()[CAT.SLOT_VEST], 201)

	# ---- a worn slot -> anywhere: gear does not come OFF in the field ----
	b.Step(0, 0, 0, 0, 0, 1, 0, 102, 0, 0)         # a rifle to drag at
	ss.refresh_pack()
	ss.pending_equip = 0
	_drag_to(ss, _slot_point(ss, CAT.SLOT_VEST), _bin_point(ss))
	_eq("a worn slot cannot be emptied onto the floor", ss.pending_drop, -1)
	_eq("and the vest is still on", b.GetWornSim()[CAT.SLOT_VEST], 201)
	_drag_to(ss, _slot_point(ss, CAT.SLOT_VEST), _slot_point(ss, CAT.SLOT_HELMET))
	_eq("nor moved to another slot", b.GetWornSim()[CAT.SLOT_VEST], 201)
	_eq("and nothing is staged", ss.pending_equip, 0)

	# ---- pack -> the floor, and the floor -> back ----
	pack = PackedInt32Array(b.GetPackPlacements())
	_check("fixture: the pack still holds something", pack.size() >= 5)
	var dropped_item: int = pack[1]
	_drag_to(ss, _pack_point(ss, pack), _bin_point(ss))
	_eq("pack to the floor stages a drop", ss.pending_drop, pack[0])
	_eq("naming what is going down", ss.pending_drop_item, dropped_item)

	var carried_before: int = PackedInt32Array(b.GetPackItems()).size()
	b.Step(0, 0, 0, 0, 0, 1, ss.pending_drop + 1, 0, 0, 0)
	ss.pending_drop = -1
	ss.refresh_pack()
	_eq("the tick takes it out of the pack",
		PackedInt32Array(b.GetPackItems()).size(), carried_before - 1)

	# It is now an ordinary loot target at the player's feet.
	var lt: PackedInt32Array = PackedInt32Array(b.GetLootTarget())
	_check("what was dropped is in reach", lt.size() >= 5, "%s" % lt)
	if lt.size() >= 5:
		var kit: PackedInt32Array = PackedInt32Array(b.GetLootKit(lt[4]))
		_check("and the pile holds it", kit.has(dropped_item), "%s" % kit)
		var at: int = 0
		for i in range(kit.size()):
			if kit[i] == dropped_item:
				at = i
		# LootPick is the kit index PLUS ONE -- 0 means "took nothing".
		b.Step(0, 0, 0, 0, at + 1, 1, 0, 0, 0, 0)
		ss.refresh_pack()
		_eq("and taking it puts it back in the pack",
			PackedInt32Array(b.GetPackItems()).size(), carried_before)
		_check("the same item, not a copy",
			PackedInt32Array(b.GetPackItems()).has(dropped_item))

	# ---- there is no stash in a corridor ----
	_eq("no stash cell is reachable", ss.cell_at(Vector2(100, 300)).x, -1)
	_eq("and no attachment rail either", ss.attach_at(Vector2(60, 215)), -1)

	host.queue_free()


## WHAT AN ITEM DOES, as the tooltip says it.
##
## The rows are pure data, so all of this is assertable -- which is the point of
## building them as data rather than as draw calls. The alternative is reading a
## tooltip off a screenshot.
func _check_tooltips(b: RefCounted) -> void:
	var none := PackedInt32Array()
	none.resize(CAT.ATTACH_COUNT)

	# ---- the stat indices still line up with the sim ----
	var resolved: PackedInt32Array = PackedInt32Array(b.ResolvedStats())
	_eq("the stat table is as long as the bridge's array", TIP.ST_COUNT,
		resolved.size())
	var menu: Script = load("res://game/loadout_menu.gd")
	for pair in [["ST_DAMAGE", TIP.ST_DAMAGE], ["ST_PIERCE", TIP.ST_PIERCE],
			["ST_MAG", TIP.ST_MAG], ["ST_CADENCE", TIP.ST_CADENCE],
			["ST_RELOAD", TIP.ST_RELOAD], ["ST_SPREAD", TIP.ST_SPREAD],
			["ST_RADIUS", TIP.ST_RADIUS], ["ST_WALK", TIP.ST_WALK],
			["ST_TURN", TIP.ST_TURN], ["ST_PELLETS", TIP.ST_PELLETS],
			["ST_VISION", TIP.ST_VISION], ["ST_DETECT", TIP.ST_DETECT]]:
		_eq("%s agrees with the loadout menu" % pair[0],
			menu.get_script_constant_map()[pair[0]], pair[1])

	# ---- every item in the build says something ----
	var bad: Array[String] = []
	var no_title: Array[String] = []
	for id in CAT.all_ids(b):
		var rows: Array = TIP.rows_for(b, none, id)
		if rows.is_empty():
			bad.append(str(id))
			continue
		if rows[0][0] != "title" or rows[0][1] != b.GearName(id):
			no_title.append(str(id))
		for row in rows:
			if row[1] == "":
				bad.append("%d: an empty line" % id)
			if row.size() != 3:
				bad.append("%d: a malformed row" % id)
	_check("every item in the catalogue has a tooltip", bad.is_empty(),
		" | ".join(bad))
	_check("and every one opens with its own name", no_title.is_empty(),
		" | ".join(no_title))
	_eq("an item that does not exist has none", TIP.rows_for(b, none, 99999), [])
	_eq("nor does nothing at all", TIP.rows_for(b, none, 0), [])

	# ---- a weapon lists EVERY rail, present or not ----
	var ak: Array = TIP.rows_for(b, none, 102)            # AK-47
	var rails: int = 0
	var offs: int = 0
	for row in ak:
		if row[0] == "rail":
			rails += 1
		elif row[0] == "railoff":
			offs += 1
	_eq("a weapon accounts for every attachment slot", rails + offs, b.SlotCount)
	var sim: int = b.GearSimA(102)
	var has: int = 0
	for s in range(b.SlotCount):
		if b.WeaponHasSlot(sim, s):
			has += 1
	_eq("the rails it HAS are the ones the sim says it has", rails, has)
	_check("and it has at least one it does not", offs + has == b.SlotCount)

	# A pistol carries fewer rails than a rifle, which is the whole point of
	# showing the empty ones.
	var glock_rails: int = 0
	for row in TIP.rows_for(b, none, 100):
		if row[0] == "rail":
			glock_rails += 1
	_check("a pistol has fewer rails than a rifle", glock_rails < rails,
		"glock %d, ak %d" % [glock_rails, rails])

	# ---- what is FITTED is named on the rail ----
	var fitted := PackedInt32Array()
	fitted.resize(CAT.ATTACH_COUNT)
	fitted[b.GearSimA(303)] = 303                 # a scope on the sight rail
	fitted[b.GearSimA(343)] = 343                 # armour piercing on the ammo rail
	fitted[b.GearSimA(352)] = 352                 # a heavy stock, which no pistol takes
	var fitted_rows: Array = TIP.rows_for(b, fitted, 102)
	var named: Array[String] = []
	for row in fitted_rows:
		if row[0] == "rail":
			named.append(row[2])
	_check("a fitted attachment is named on its rail",
		named.has(b.GearName(303)) and named.has(b.GearName(343)),
		" | ".join(named))
	_check("and an empty rail says so", named.has("empty"), " | ".join(named))

	# ONE set applies to whichever weapon is held, so the same scope that is
	# fitted for the rifle is going to waste on a pistol with no sight rail.
	# The rail row says "no rail" either way; the count is the part worth
	# acting on, and it is the only place the shared set is visible at all.
	var stranded: String = ""
	for row in TIP.rows_for(b, fitted, 100):          # a Glock
		if row[0] == "note" and row[1].contains("cannot take"):
			stranded = row[1]
	_check("a weapon says how much of the fitted set it cannot use",
		stranded != "", stranded)
	var wasted: int = 0
	for sub in range(b.SlotCount):
		if fitted[sub] > 0 and not b.WeaponHasSlot(b.GearSimA(100), sub):
			wasted += 1
	_eq("and counts it right", stranded,
		"%d fitted item(s) this weapon cannot take" % wasted)
	var none_wasted: String = ""
	for row in TIP.rows_for(b, none, 100):
		if row[0] == "note" and row[1].contains("cannot take"):
			none_wasted = row[1]
	_eq("with nothing fitted, it says nothing", none_wasted, "")

	# ---- and the FIGURES are the fitted ones ----
	var moved: int = 0
	for row in fitted_rows:
		if row[0] == "up" or row[0] == "down":
			moved += 1
	_check("fitting something moves at least one figure", moved > 0)
	var plain: int = 0
	for row in ak:
		if row[0] == "up" or row[0] == "down":
			plain += 1
	_eq("a bare weapon shows no figure as moved", plain, 0)

	# Armour piercing is the clearest case: a bare AK has none, and the row is
	# left out entirely rather than printed as nothing.
	var bare_has_pierce: bool = false
	for row in ak:
		if row[1] == "armour pierce":
			bare_has_pierce = true
	_check("a weapon with no pierce does not print a pierce of nothing",
		not bare_has_pierce)
	var ap_pierce: String = ""
	for row in fitted_rows:
		if row[1] == "armour pierce":
			ap_pierce = row[0]
	_eq("but AP ammo puts it there, and upward", ap_pierce, "up")

	# ---- an attachment says what it DOES ----
	var scope: Array = TIP.rows_for(b, none, 303)
	var deltas: int = 0
	var arrows: int = 0
	for row in scope:
		if row[0] == "up" or row[0] == "down":
			deltas += 1
			if row[2].contains("→"):
				arrows += 1
	_check("an attachment lists what it changes", deltas > 0)
	_eq("each as a before and an after", arrows, deltas)
	var on_line: String = ""
	for row in scope:
		if row[0] == "head" and row[1].begins_with("ON "):
			on_line = row[1]
	_check("named against a weapon that actually has the rail", on_line != "",
		" | ".join(PackedStringArray([str(scope)])))

	# ...and against the player's OWN weapon when it has the rail, because
	# "what does this scope do" has no answer in the abstract.
	var ak_sim: int = b.GearSimA(102)
	var on_mine: String = ""
	for row in TIP.rows_for(b, none, 303, ak_sim):
		if row[0] == "head":
			on_mine = row[1]
	_eq("an attachment is described against the weapon you carry", on_mine,
		"ON A %s" % b.WeaponNameOf(ak_sim).to_upper())

	# A weapon without the rail falls back rather than lying about it: a Glock
	# has no stock, so a heavy stock cannot be described against one.
	var glock_sim: int = b.GearSimA(100)
	var stock_sub: int = b.GearSimA(352)
	_check("fixture: a pistol has no stock rail",
		not b.WeaponHasSlot(glock_sim, stock_sub))
	var fallback: String = ""
	for row in TIP.rows_for(b, none, 352, glock_sim):
		if row[0] == "head":
			fallback = row[1]
	_eq("and one without the rail falls back to a weapon that has it",
		fallback, "ON A %s"
			% b.WeaponNameOf(b.FirstWeaponWithSlot(stock_sub)).to_upper())

	# Every attachment in the build is described against something it fits.
	var silent: Array[String] = []
	for id in CAT.ids_of_kind(b, CAT.KIND_ATTACHMENT):
		var on: int = b.FirstWeaponWithSlot(b.GearSimA(id))
		if not b.WeaponHasSlot(on, b.GearSimA(id)):
			silent.append("%d: no weapon carries its rail" % id)
			continue
		var changed: int = 0
		for row in TIP.rows_for(b, none, id):
			if row[0] == "up" or row[0] == "down":
				changed += 1
		if changed == 0:
			silent.append("%d: changes nothing" % id)
	_check("every attachment is described against a weapon that fits it",
		silent.is_empty(), " | ".join(silent))

	# ---- the other kinds ----
	var vest: Array = TIP.rows_for(b, none, 203)
	var says_protection: bool = false
	for row in vest:
		if row[1] == "protection":
			says_protection = row[2] == str(b.ArmourValueOf(b.GearSimA(203)))
	_check("a vest states its protection, from the sim", says_protection)

	var bag: Array = TIP.rows_for(b, none, 503)
	var says_room: bool = false
	for row in bag:
		if row[1] == "carries":
			says_room = row[2].begins_with("%dx%d" % [b.GearPackW(503), b.GearPackH(503)])
	_check("a backpack states the room it gives", says_room)

	var shirt: Array = TIP.rows_for(b, none, 702)
	var says_cosmetic: bool = false
	for row in shirt:
		if row[1].begins_with("cosmetic"):
			says_cosmetic = true
	_check("apparel says plainly that it changes nothing", says_cosmetic)

	var case_rows: Array = TIP.rows_for(b, none, 900)
	_eq("the objective is not for sale",
		case_rows[case_rows.size() - 1][1], "not for sale")
	var priced: Array = TIP.rows_for(b, none, 102)
	_eq("and everything else says what it is worth",
		priced[priced.size() - 1][1], "worth %d" % b.GearPrice(102))

	# ---- the probe must NEVER leak into the staged loadout ----
	#
	# It builds a scratch Loadout to answer "what would this do", and the
	# staged one decides what the player actually deploys with. A tooltip that
	# re-armed you by being looked at would be the worst bug in the game.
	b.SetWeapon(4)
	b.SetArmour(2)
	b.SetAttachment(0, 1)
	var w_before: int = b.CurrentWeaponId
	var a_before: int = b.CurrentArmourId
	var s_before: int = b.GetAttachment(0)
	for id in CAT.all_ids(b):
		TIP.rows_for(b, fitted, id)
	_eq("looking at an item does not change the staged weapon",
		b.CurrentWeaponId, w_before)
	_eq("nor the armour", b.CurrentArmourId, a_before)
	_eq("nor what is fitted", b.GetAttachment(0), s_before)


## The tooltip's geometry: it must hold what it is asked to hold, and it must
## land ON SCREEN. A panel clipped by the edge loses exactly the figure being
## reached for.
func _check_tooltip_layout(b: RefCounted) -> void:
	var host := Node.new()
	root.add_child(host)
	var stash: RefCounted = STASH.new(b)
	stash.stock_default()
	stash.equip_starting_kit()
	var ss: Node2D = load("res://game/stash_screen.gd").new()
	ss.bridge = b
	ss.stash = stash
	host.add_child(ss)
	ss.open_screen("")

	var none := PackedInt32Array()
	none.resize(CAT.ATTACH_COUNT)

	_eq("nothing to say needs no panel", ss.tip_size([]), Vector2.ZERO)

	var small: Vector2 = ss.tip_size(TIP.rows_for(b, none, 801))   # gloves
	var big: Vector2 = ss.tip_size(TIP.rows_for(b, none, 102))     # a rifle
	_check("a panel has real size", small.x > 0.0 and small.y > 0.0, "%s" % small)
	_check("and a weapon needs a taller one than a glove", big.y > small.y,
		"%s vs %s" % [big, small])
	_check("but never a wider one than the cap", big.x <= ss.TIP_MAX_W,
		"%s" % big)

	# The height is the sum of the row heights, exactly -- the measure and the
	# draw read one table, so the last line cannot hang out of the box.
	var rows: Array = TIP.rows_for(b, none, 102)
	var summed: float = ss.TIP_PAD * 2.0
	for row in rows:
		summed += ss.tip_metrics(row[0]).y
	_eq("the box is exactly as tall as the rows it holds",
		ss.tip_size(rows).y, summed)

	# Every cursor position on the screen, against the biggest panel there is.
	var off: Array[String] = []
	var widest: Vector2 = Vector2.ZERO
	for id in CAT.all_ids(b):
		var sz: Vector2 = ss.tip_size(TIP.rows_for(b, none, id))
		if sz.y > widest.y:
			widest = sz
	var x: int = 0
	while x <= 960:
		var y: int = 0
		while y <= 620:
			var o: Vector2 = ss.tip_origin(Vector2(x, y), widest)
			if o.x < 0.0 or o.y < 0.0 or o.x + widest.x > 960.0 \
					or o.y + widest.y > ss.SCREEN_H:
				off.append("%d,%d -> %s" % [x, y, o])
			y += 31
		x += 37
	_check("the panel lands wholly on screen from anywhere", off.is_empty(),
		" | ".join(off))
	_check("fixture: the biggest panel is a real size",
		widest.x > 0.0 and widest.y > 0.0, "%s" % widest)

	# ---- what is under the cursor ----
	var rifle: int = stash.add(102)
	_check("fixture: a rifle is in the stash", rifle != GRID.NONE)
	var rat: Vector2i = stash.grid.pos_of(rifle)
	_eq("hovering a stash item names it",
		ss.hovered_item(_cell_point(ss, rat.x, rat.y)), 102)
	_eq("hovering empty grid names nothing",
		ss.hovered_item(_cell_point(ss, 11, 9)), STASH.NONE)
	var worn: int = stash.equipped_in(CAT.SLOT_BACKPACK)
	_check("fixture: a backpack is worn", worn != STASH.NONE)
	_eq("hovering a worn slot names what is in it",
		ss.hovered_item(_slot_point(ss, CAT.SLOT_BACKPACK)), worn)
	_eq("hovering an empty slot names nothing",
		ss.hovered_item(_slot_point(ss, CAT.SLOT_LEGS)), STASH.NONE)
	stash._attach[0] = 303
	_eq("hovering a weapon rail names what is fitted",
		ss.hovered_item(_attach_point(ss, 0)), 303)
	_eq("and an empty rail names nothing",
		ss.hovered_item(_attach_point(ss, 1)), STASH.NONE)
	_eq("hovering nothing at all names nothing",
		ss.hovered_item(Vector2(700, 560)), STASH.NONE)

	# ---- in the field: the pack, and the fitted strip ----
	b.SetBackpack(503)
	b.Restart(1)
	b.Step(0, 0, 0, 0, 0, 1, 0, 102, 0, 0)
	ss.open_screen("", true)
	var pack: PackedInt32Array = PackedInt32Array(b.GetPackPlacements())
	_check("fixture: the pack holds a rifle", pack.size() >= 5)
	if pack.size() >= 5:
		_eq("hovering a pack item names it",
			ss.hovered_item(_pack_point(ss, pack)), pack[1])
	var chips: Array = ss.fitted_chips()
	_check("the fitted strip is laid out from the SIM in the field",
		chips.size() == _sim_fitted_count(b), "%d chips" % chips.size())
	for chip in chips:
		_eq("and hovering a chip names what it is",
			ss.hovered_item(chip[0].get_center()), chip[1])

	host.queue_free()


func _sim_fitted_count(b: RefCounted) -> int:
	var n: int = 0
	for it in PackedInt32Array(b.GetFittedSim(0)):
		if it > 0:
			n += 1
	return n


## EACH WEAPON ITS OWN RAILS, and the kit you walk in CARRYING.
##
## Three rules that only make sense together: a scope is on a gun, the bag is
## packed at base, and dying takes all of it.
func _check_per_weapon_rails(b: RefCounted) -> void:
	var stash: RefCounted = STASH.new(b)
	stash.add(102)                                  # AK
	stash.add(101)                                  # MP7
	stash.add(303)                                  # scope
	stash.add(331)                                  # extended mag
	stash.equip_from_grid(ss_find(stash, 102), CAT.SLOT_PRIMARY)
	stash.equip_from_grid(ss_find(stash, 101), CAT.SLOT_SECONDARY)

	# ---- fitted to ONE gun ----
	_check("a scope goes on the primary",
		stash.equip_from_grid(ss_find(stash, 303), STASH.NONE, 0))
	_eq("and is on the primary", stash.attached_at(0, 0), 303)
	_eq("and NOT on the holster", stash.attached_at(0, 1), STASH.NONE)

	_check("and a mag on the holster",
		stash.equip_from_grid(ss_find(stash, 331), STASH.NONE, 1))
	_eq("which is on the holster", stash.attached_at(3, 1), 331)
	_eq("and not on the primary", stash.attached_at(3, 0), STASH.NONE)

	# ---- and it reaches the sim that way ----
	stash.apply_to(b)
	var sight: int = b.GearSimA(303)
	var mag: int = b.GearSimA(331)
	_eq("the sim has the scope on the primary",
		b.GetAttachmentFor(0, sight), b.GearSimB(303))
	_eq("and nothing on the holster's sight rail",
		b.GetAttachmentFor(1, sight), 0)
	_eq("the sim has the mag on the holster",
		b.GetAttachmentFor(1, mag), b.GearSimB(331))
	_eq("and nothing on the primary's", b.GetAttachmentFor(0, mag), 0)

	# The figures follow: two weapons, two magazines.
	b.Restart(1)
	var held: int = b.GetPackItems().size()          # force the snapshot
	_check("fixture: the run is live", held >= 0)

	# ---- the save format carries both sets ----
	var text: String = stash.to_text()
	var back: RefCounted = STASH.new(b)
	_eq("a stash with two sets reloads with nothing skipped", back.from_text(text), 0)
	_eq("the primary's scope survives", back.attached_at(0, 0), 303)
	_eq("the holster's mag survives", back.attached_at(3, 1), 331)
	_eq("and neither leaks into the other", back.attached_at(0, 1), STASH.NONE)

	# A stash written before the holster had rails reads as an empty second
	# set, which is what those saves meant.
	var legacy: RefCounted = STASH.new(b)
	legacy.from_text("stash 2\ngrid 12 10\nequip 6 102\nattach 0 303\n")
	_eq("an old stash keeps the primary's rails", legacy.attached_at(0, 0), 303)
	_eq("and gives the holster none", legacy.attached_at(0, 1), STASH.NONE)

	# ---- taking one off takes it off ONE gun ----
	_check("the holster's mag comes off", stash.unequip_attach(3, 1))
	_eq("leaving the holster bare", stash.attached_at(3, 1), STASH.NONE)
	_eq("and the primary's scope alone", stash.attached_at(0, 0), 303)


## THE BAG: what you choose to take, and what that costs when it goes wrong.
func _check_carrying(b: RefCounted) -> void:
	var stash: RefCounted = STASH.new(b)
	stash.add(100)
	stash.equip_from_grid(ss_find(stash, 100), CAT.SLOT_PRIMARY)
	stash.add(503)                                   # large pack, 8x5
	stash.equip_from_grid(ss_find(stash, 503), CAT.SLOT_BACKPACK)
	stash.apply_to(b)

	_eq("a fresh stash carries nothing", stash.carried.size(), 0)

	# ---- stash -> bag ----
	var vest: int = stash.add(203)
	var at: Vector2i = stash.grid.pos_of(vest)
	_check("a vest can be packed", stash.carry_from_grid(vest))
	_eq("it is in the bag", stash.carried.size(), 1)
	_eq("naming the item", stash.carried[0], 203)
	_eq("AND IT IS OUT OF THE STASH", stash.grid.placement_at(at.x, at.y), GRID.NONE)
	_eq("the stash no longer holds one anywhere", ss_find(stash, 203), GRID.NONE)
	_eq("but you still own exactly one", stash.count_of(203), 1)

	# ---- bag -> stash ----
	_check("and it can come back out", stash.uncarry(0))
	_eq("leaving the bag empty", stash.carried.size(), 0)
	_check("with the vest in the stash again", ss_find(stash, 203) != GRID.NONE)
	_eq("still exactly one of them", stash.count_of(203), 1)
	_check("taking out what is not there is refused", not stash.uncarry(0))

	# ---- the BAG decides how much fits ----
	stash.carry_from_grid(ss_find(stash, 203))
	var refused: int = 0
	var packed: int = 0
	for i in range(40):
		var pi: int = stash.add(203)                 # 3x3 plates
		if pi == GRID.NONE:
			break
		if stash.carry_from_grid(pi):
			packed += 1
		else:
			refused += 1
			break
	_check("the bag fills up and then refuses", refused > 0,
		"packed %d, refused %d" % [packed, refused])
	_check("and what it refused is still in the stash", ss_find(stash, 203) != GRID.NONE)

	# What is staged is what the SIM will build the pack from.
	stash.apply_to(b)
	_eq("the sim is told what is carried", b.CarriedCount, stash.carried.size())
	b.Restart(1)
	_eq("and Restart puts every one of them in the pack",
		PackedInt32Array(b.GetPackItems()).size(), stash.carried.size())

	# ---- a smaller bag cannot hold the kit packed for a bigger one ----
	var small: RefCounted = STASH.new(b)
	small.add(501)                                   # satchel, 4x3
	small.equip_from_grid(ss_find(small, 501), CAT.SLOT_BACKPACK)
	small.apply_to(b)
	var big: int = small.add(203)                    # a 3x3 plate
	_check("a 3x3 fits a 4x3 satchel once", small.carry_from_grid(big))
	var second: int = small.add(203)
	_check("but not twice", not small.carry_from_grid(second))
	_eq("and the second is still in the stash",
		small.grid.item_of(second), 203)


## DYING TAKES EVERYTHING ON YOU. Extracting does not.
func _check_loss_and_landing(b: RefCounted) -> void:
	var stash: RefCounted = STASH.new(b)
	stash.add(102)
	stash.equip_from_grid(ss_find(stash, 102), CAT.SLOT_PRIMARY)
	stash.add(203)
	stash.equip_from_grid(ss_find(stash, 203), CAT.SLOT_VEST)
	stash.add(503)
	stash.equip_from_grid(ss_find(stash, 503), CAT.SLOT_BACKPACK)
	stash.add(303)
	stash.equip_from_grid(ss_find(stash, 303), STASH.NONE, 0)
	stash.apply_to(b)
	stash.add(201)                                   # left at base
	stash.carry_from_grid(stash.add(101))            # taken along

	var left_behind: int = stash.grid.count()
	_check("fixture: something is worn", stash.equipped_in(CAT.SLOT_PRIMARY) != STASH.NONE)
	_check("fixture: something is fitted", stash.attached_at(0, 0) != STASH.NONE)
	_check("fixture: something is carried", stash.carried.size() > 0)
	_check("fixture: something is left at base", left_behind > 0)

	stash.lose_kit()
	_eq("dying strips every worn slot", stash.equipped_in(CAT.SLOT_PRIMARY), STASH.NONE)
	_eq("and the vest with it", stash.equipped_in(CAT.SLOT_VEST), STASH.NONE)
	_eq("and the bag off your back", stash.equipped_in(CAT.SLOT_BACKPACK), STASH.NONE)
	_eq("and what was fitted to the gun", stash.attached_at(0, 0), STASH.NONE)
	_eq("and everything in the bag", stash.carried.size(), 0)
	_eq("but WHAT YOU LEFT AT BASE IS UNTOUCHED", stash.grid.count(), left_behind)
	_check("and it is still the same gear", ss_find(stash, 201) != GRID.NONE)
	_eq("the rifle is gone for good", stash.count_of(102), 0)

	# ---- but the next run always starts with a bag ----
	_check("a bagless kit is given a satchel", stash.ensure_pack())
	_eq("the satchel is worn", stash.equipped_in(CAT.SLOT_BACKPACK), STASH.STARTER_PACK)
	_eq("issued, not taken from what was left at base", stash.grid.count(), left_behind)
	_check("and a worn bag is left alone", not stash.ensure_pack())
	stash.unequip(CAT.SLOT_BACKPACK)
	var owned: int = stash.count_of(STASH.STARTER_PACK)
	_check("a satchel taken off at base goes back on", stash.ensure_pack())
	_eq("the SAME satchel, not a second one", stash.count_of(STASH.STARTER_PACK), owned)

	# ---- extracting: the case is handed in, the rest is banked ----
	var out: RefCounted = STASH.new(b)
	out.add(503)
	out.equip_from_grid(ss_find(out, 503), CAT.SLOT_BACKPACK)
	out.apply_to(b)
	out.carry_from_grid(out.add(301))
	_eq("fixture: something is in the bag", out.carried.size(), 1)

	var recovered := PackedInt32Array([301, 102, 900, 203])
	var banked: Array = out.bank_recovered(recovered)
	_eq("the objective is handed in, not kept", banked[3], 1)
	_eq("and is nowhere in the stash", ss_find(out, 900), GRID.NONE)
	_eq("nor owned at all", out.count_of(900), 0)
	_eq("everything else is kept", banked[0], 3)
	_eq("nothing was fenced from an empty stash", banked[1], 0)
	_check("the rifle came home", ss_find(out, 102) != GRID.NONE)
	_eq("AND THE BAG IS EMPTY -- what was in it is now in the stash",
		out.carried.size(), 0)
	_eq("worn gear is untouched by extracting",
		out.equipped_in(CAT.SLOT_BACKPACK), 503)

	# Overflow is fenced rather than lost, and fenced gear is not also kept.
	var full: RefCounted = STASH.new(b)
	while full.add(203) != GRID.NONE:
		pass
	var over: Array = full.bank_recovered(PackedInt32Array([102, 900]))
	_eq("a full stash fences what it cannot hold", over[1], 1)
	_eq("at the item's own price", over[2], b.GearPrice(102))
	_eq("keeps nothing", over[0], 0)
	_eq("and still hands the case in", over[3], 1)

	_check_field_equip_comes_home(b)


## cognitohazard_loot_flow.md §6.1, through the REAL bridge: deploy in a Glock,
## equip a rifle, a vest and a red dot in the field, extract. The settle used to
## read the pack alone, so all three were destroyed and the Glock -- displaced
## into the pack -- came home beside the copy the stash still wore.
func _check_field_equip_comes_home(b: RefCounted) -> void:
	var home: RefCounted = STASH.new(b)
	home.equip_from_grid(home.add(100), CAT.SLOT_PRIMARY)
	home.equip_from_grid(home.add(503), CAT.SLOT_BACKPACK)
	home.apply_to(b)
	b.Restart(1)
	var deployed: Array = STASH.sim_kit(b)
	_eq("fixture: deployed with the Glock", deployed[0][CAT.SLOT_PRIMARY], 100)

	var dot_sub: int = CAT.attach_slot_of(b, 301)
	for item_id in [102, 201]:
		b.Step(0, 0, 0, 0, 0, 1, 0, item_id, 0, 0)
		var pi: int = _pack_placement_of(b, item_id)
		var slot: int = CAT.SLOT_PRIMARY if item_id == 102 else CAT.SLOT_VEST
		b.Step(0, 0, 0, 0, 0, 1, 0, 0, b.MakeEquipPick(pi, slot), 0)
	# The dot goes on AFTER the rifle, so it is the rifle's.
	b.Step(0, 0, 0, 0, 0, 1, 0, 301, 0, 0)
	b.Step(0, 0, 0, 0, 0, 1, 0, 0,
		b.MakeEquipPick(_pack_placement_of(b, 301), CAT.SLOT_PRIMARY), 0)
	_eq("fixture: the rifle is in hand", b.GetWornSim()[CAT.SLOT_PRIMARY], 102)
	_eq("fixture: the vest is on", b.GetWornSim()[CAT.SLOT_VEST], 201)
	_eq("fixture: the dot is fitted", b.GetFittedSim(0)[dot_sub], 301)

	var changed: int = home.reconcile_worn(deployed, STASH.sim_kit(b))
	_eq("three things changed in the field", changed, 3)
	home.bank_recovered(PackedInt32Array(b.GetPackItems()))
	_eq("THE RIFLE COMES HOME, worn", home.equipped_in(CAT.SLOT_PRIMARY), 102)
	_eq("and exactly once", home.count_of(102), 1)
	_eq("the vest too", home.equipped_in(CAT.SLOT_VEST), 201)
	_eq("the dot is still on the rifle", home.attached_at(dot_sub, 0), 301)
	_eq("THE GLOCK IS NOT DUPLICATED", home.count_of(100), 1)
	_check("it is in the grid, where the pack put it", ss_find(home, 100) != GRID.NONE)
	_eq("the bag is still worn", home.equipped_in(CAT.SLOT_BACKPACK), 503)

	# What the field did NOT touch keeps the STASH'S answer, which is not
	# always the sim's: an empty hand deploys as the default Glock.
	var bare: RefCounted = STASH.new(b)
	bare.equip_from_grid(bare.add(503), CAT.SLOT_BACKPACK)
	bare.apply_to(b)
	b.Restart(1)
	var bare_kit: Array = STASH.sim_kit(b)
	_eq("fixture: an empty hand deploys as a Glock", bare_kit[0][CAT.SLOT_PRIMARY], 100)
	b.Step(0, 0, 0, 0, 0, 1, 0, 0, 0, 0)
	_eq("an untouched kit changes nothing",
		bare.reconcile_worn(bare_kit, STASH.sim_kit(b)), 0)
	_eq("and mints no Glock", bare.count_of(100), 0)
	_eq("and the empty hand stays empty", bare.equipped_in(CAT.SLOT_PRIMARY), STASH.NONE)


func _pack_placement_of(b: RefCounted, item_id: int) -> int:
	var pack: PackedInt32Array = PackedInt32Array(b.GetPackPlacements())
	var i: int = 0
	while i < pack.size():
		if pack[i + 1] == item_id:
			return pack[i]
		i += 5
	return -1


## First placement holding an item, or GRID.NONE. Named apart from _find so the
## two blocks above read as one sentence each.
func ss_find(stash: RefCounted, item_id: int) -> int:
	for pi in stash.grid.live_ids():
		if stash.grid.item_of(pi) == item_id:
			return pi
	return GRID.NONE
