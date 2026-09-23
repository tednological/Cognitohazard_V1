extends RefCounted

## What an item DOES, as rows a screen can draw.
##
## Every figure comes back from the bridge already resolved; nothing here
## computes a game number. That is the same bargain loadout_menu.gd makes, and
## for the same reason: a second opinion about what a scope is worth is a second
## opinion that will drift from the one the sim fires with.
##
## Pure and static, so the whole of it is testable headlessly -- which matters
## more here than usual, because the alternative is reading a tooltip in a
## screenshot and hoping.
##
## A row is [kind, left, right]:
##   title     the item's name
##   sub       what it is, in small type
##   head      a section heading
##   stat      a label and a figure
##   up/down   a figure an attachment has moved, for the better or the worse
##   rail      an attachment rail this weapon HAS, and what is in it
##   railoff   a rail it does not have
##   note      a plain sentence
##   price     what it is worth

const CAT := preload("res://game/item_catalog.gd")

## Resolved-stat indices, mirroring SimBridge.StatsOf. loadout_menu.gd carries
## the same list; inventory_check.gd asserts the two still agree with each
## other and with the array the bridge actually returns.
const ST_DAMAGE: int = 0
const ST_PIERCE: int = 1
const ST_MAG: int = 2
const ST_CADENCE: int = 3
const ST_RELOAD: int = 4
const ST_SPREAD: int = 5
const ST_SPREAD_HEAT: int = 6
const ST_HEAT: int = 7
const ST_BULLET_SPEED: int = 8
const ST_BULLET_LIFE: int = 9
const ST_PELLETS: int = 10
const ST_RADIUS: int = 11
const ST_WALK: int = 12
const ST_SNEAK: int = 13
const ST_VISION: int = 14
const ST_DETECT: int = 15
const ST_SPREAD_SWAY: int = 16
const ST_TURN: int = 17
const ST_COUNT: int = 18

## The headline figures a weapon tooltip shows, in order:
## [label, index, higher-is-better, format]. Deliberately SHORTER than the
## loadout menu's table -- a tooltip is read at a glance over a grid, and the
## full eighteen rows belong on the screen built to compare them.
const WEAPON_ROWS: Array = [
	["damage", ST_DAMAGE, true, "int"],
	["armour pierce", ST_PIERCE, true, "pct"],
	["magazine", ST_MAG, true, "int"],
	["rate of fire", ST_CADENCE, false, "rps"],
	["reload", ST_RELOAD, false, "sec"],
	["spread", ST_SPREAD, false, "rad"],
	["handling", ST_TURN, true, "turn"],
	["gunshot radius", ST_RADIUS, false, "pxr"],
	["walk speed", ST_WALK, true, "px"],
]

## EVERY figure an attachment can move, for the attachment tooltip. It has to
## be the full table, not the headline one: a rubber grip touches only the
## cone under fire and when swung, and against the short list it read
## "changes nothing" -- which is both wrong and exactly the thing a player
## opened the tooltip to find out. Only the rows that actually move are
## printed, so a simple attachment still gets a short tooltip.
const ATTACH_ROWS: Array = [
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
	["vision", ST_VISION, true, "pxr"],
	["detection", ST_DETECT, false, "pct"],
]


## One value, in the units a player reads. The same vocabulary loadout_menu.gd
## prints, so a figure means the same thing wherever it appears.
static func fmt(bridge: RefCounted, v: int, kind: String) -> String:
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
		"turn":
			return "%d%%" % (v * 100 / maxi(1, bridge.TurnDen))
		_:
			return str(v)


## Everything to say about `item_id`.
##
## `fitted` is what is on the weapon rails right now, as gear item ids by
## sub-slot -- the stash's set at base, the SIM'S in the field, because
## attachments can be fitted mid-run and the two then disagree.
##
## `on_weapon` is the weapon an ATTACHMENT should be described against, as a
## sim WeaponId: the one in the player's hands. "What does this scope do" has
## no answer in the abstract, and the answer a player wants is the one about
## the rifle they own. -1, or a weapon without the rail, falls back to the
## first weapon in the catalogue that has one.
static func rows_for(bridge: RefCounted, fitted: PackedInt32Array,
		item_id: int, on_weapon: int = -1) -> Array:
	if bridge == null or item_id <= 0 or not bridge.GearExists(item_id):
		return []

	var rows: Array = []
	var kind: int = bridge.GearKindOf(item_id)
	var w: int = bridge.GearWidth(item_id)
	var h: int = bridge.GearHeight(item_id)

	# The tier rides on the title line's right, in words as well as colour, so
	# it reads for anyone who cannot tell the orange from the purple.
	rows.append(["title", bridge.GearName(item_id),
		"" if kind == CAT.KIND_OBJECTIVE else bridge.RarityName(bridge.GearRarity(item_id))])

	match kind:
		CAT.KIND_WEAPON:
			rows.append(["sub", "%s  ·  %dx%d cells"
				% [bridge.WeaponClassOf(bridge.GearSimA(item_id)), w, h], ""])
			_weapon_rows(bridge, fitted, item_id, rows)
		CAT.KIND_ATTACHMENT:
			_attachment_rows(bridge, item_id, w, h, rows, on_weapon)
		CAT.KIND_ARMOUR:
			rows.append(["sub", "vest  ·  %dx%d cells" % [w, h], ""])
			rows.append(["stat", "protection",
				str(bridge.ArmourValueOf(bridge.GearSimA(item_id)))])
			rows.append(["note", "soaks damage until it is gone", ""])
			rows.append(["note", "a headshot ignores it entirely", ""])
		CAT.KIND_PACK:
			var pw: int = bridge.GearPackW(item_id)
			var ph: int = bridge.GearPackH(item_id)
			rows.append(["sub", "backpack  ·  %dx%d cells" % [w, h], ""])
			rows.append(["stat", "carries", "%dx%d  ·  %d cells" % [pw, ph, pw * ph]])
			rows.append(["note", "what you carry out is what you keep", ""])
		CAT.KIND_OBJECTIVE:
			rows.append(["sub", "mission objective  ·  %dx%d cells" % [w, h], ""])
			rows.append(["note", "carry it out to complete the mission", ""])
		_:
			rows.append(["sub", "%s  ·  %dx%d cells"
				% [bridge.GearSlotName(bridge.GearSlotOf(item_id)), w, h], ""])
			rows.append(["note", "cosmetic — worn and saved, changes no stat", ""])

	var price: int = bridge.GearPrice(item_id)
	rows.append(["price", "not for sale" if price <= 0 else "worth %d" % price, ""])
	return rows


## A weapon: what it does as fitted, and what is on its rails.
##
## The figures are the FITTED ones, with the bare weapon beside any that an
## attachment has moved. Asking what a rifle does and being told what it would
## do with nothing on it is answering a different question.
static func _weapon_rows(bridge: RefCounted, fitted: PackedInt32Array,
		item_id: int, rows: Array) -> void:
	var sim: int = bridge.GearSimA(item_id)

	bridge.ProbeWeapon(sim)
	for sub in range(bridge.SlotCount):
		var on: int = _fitted_at(fitted, sub)
		if on > 0:
			bridge.ProbeAttach(sub, bridge.GearSimB(on))
	var now: PackedInt32Array = PackedInt32Array(bridge.ProbeStats())
	var bare: PackedInt32Array = PackedInt32Array(bridge.ProbeBareStats())

	var pellets: int = now[ST_PELLETS]
	if pellets > 1:
		rows.append(["note", "%d pellets a shell, damage is PER PELLET" % pellets, ""])
	# The specialists break a rule every other weapon keeps (arcs, walls, being
	# thrown). The line comes from the sim, which owns every figure in it.
	# One row per line: a single long note would run past the panel's width cap.
	var trait_text: String = bridge.WeaponTraitOf(sim)
	if trait_text != "":
		for line in trait_text.split("\n"):
			rows.append(["note", line, ""])
	var spin: int = bridge.WeaponStats(sim)[6]
	if spin > 0:
		rows.append(["note", "%0.2fs of held trigger before the first round"
			% (float(spin) / 60.0), ""])

	rows.append(["head", "FIGURES", "fitted" if _any_fitted(fitted) else ""])
	for r in WEAPON_ROWS:
		var idx: int = r[1]
		# A pierce of nothing on a weapon that never had any is noise.
		if idx == ST_PIERCE and now[idx] == 0:
			continue
		var kind: String = "stat"
		if now[idx] != bare[idx]:
			kind = "up" if (now[idx] > bare[idx]) == bool(r[2]) else "down"
		rows.append([kind, r[0], fmt(bridge, now[idx], r[3])])

	# The rails. EVERY slot, so a weapon that cannot take a stock says so
	# rather than simply not mentioning one -- the same reason the loadout menu
	# greys a slot instead of hiding it.
	rows.append(["head", "ATTACHMENTS", ""])
	var stranded: int = 0
	for sub in range(bridge.SlotCount):
		var name: String = bridge.SlotName(sub)
		var on: int = _fitted_at(fitted, sub)
		if not bridge.WeaponHasSlot(sim, sub):
			rows.append(["railoff", name, "no rail"])
			if on > 0:
				stranded += 1
			continue
		rows.append(["rail", name,
			bridge.GearName(on) if on > 0 else "empty"])

	# ONE set of attachments applies to whichever weapon is held, so a stock
	# fitted for the rifle does nothing at all on the pistol. The rail above
	# says "no rail" either way; this says that something is sitting in it
	# going to waste, which is the part worth acting on.
	if stranded > 0:
		rows.append(["note", "%d fitted item(s) this weapon cannot take"
			% stranded, ""])


## An attachment: the rail it fits, and what it does to a weapon that has one.
##
## Shown as a DELTA against a real weapon, because that is what an attachment
## is -- there is no such thing as the damage of a scope. Only the figures it
## actually moves are listed, so a tooltip is as short as the item is simple.
static func _attachment_rows(bridge: RefCounted, item_id: int, w: int, h: int,
		rows: Array, on_weapon: int) -> void:
	var sub: int = bridge.GearSimA(item_id)
	var option: int = bridge.GearSimB(item_id)
	# "<name> · attachment", not "<name> rail": one of the six rails is itself
	# called "rail", and "rail rail" is not a thing anyone has ever said.
	rows.append(["sub", "%s  ·  attachment  ·  %dx%d cells"
		% [bridge.SlotName(sub), w, h], ""])

	var on: int = on_weapon
	if on < 0 or not bridge.WeaponHasSlot(on, sub):
		on = bridge.FirstWeaponWithSlot(sub)
	bridge.ProbeWeapon(on)
	var bare: PackedInt32Array = PackedInt32Array(bridge.ProbeBareStats())
	bridge.ProbeAttach(sub, option)
	var now: PackedInt32Array = PackedInt32Array(bridge.ProbeStats())

	rows.append(["head", "ON A %s" % bridge.WeaponNameOf(on).to_upper(), ""])
	var moved: int = 0
	for r in ATTACH_ROWS:
		var idx: int = r[1]
		if now[idx] == bare[idx]:
			continue
		moved += 1
		var kind: String = "up" if (now[idx] > bare[idx]) == bool(r[2]) else "down"
		rows.append([kind, r[0], "%s → %s"
			% [fmt(bridge, bare[idx], r[3]), fmt(bridge, now[idx], r[3])]])

	if moved == 0:
		rows.append(["note", "changes nothing on this weapon", ""])


static func _fitted_at(fitted: PackedInt32Array, sub: int) -> int:
	if sub < 0 or sub >= fitted.size():
		return 0
	return fitted[sub] if fitted[sub] > 0 else 0


static func _any_fitted(fitted: PackedInt32Array) -> bool:
	for i in range(fitted.size()):
		if fitted[i] > 0:
			return true
	return false
