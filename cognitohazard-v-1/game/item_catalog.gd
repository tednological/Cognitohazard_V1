extends RefCounted

## Slot and kind constants for the equipment screens, plus thin helpers over the
## bridge's gear catalog.
##
## There is no item table here any more. sim/GearCatalog.cs is the authority --
## it has to be, because the mission pack is sim state and footprints feed the
## state hash -- and the bridge exposes it, so duplicating 33 rows in GDScript
## would only create something to drift. What remains is the two small enums,
## and tests/inventory_check.gd asserts even those still line up with the sim.

## Mirrors sim GearSlot. Order is load-bearing: two items touching one stat
## resolve in slot order, and that order is this one.
const SLOT_HELMET: int = 0
const SLOT_VEST: int = 1
const SLOT_BACKPACK: int = 2
const SLOT_FOOTWARE: int = 3
const SLOT_CHEST: int = 4
const SLOT_ARMS: int = 5
const SLOT_PRIMARY: int = 6
const SLOT_SECONDARY: int = 7
## APPENDED, mirroring sim GearSlot. New slots go on the END.
const SLOT_LEGS: int = 8
const SLOT_COUNT: int = 9

## Mirrors sim GearKind.
const KIND_WEAPON: int = 0
const KIND_ARMOUR: int = 1
const KIND_ATTACHMENT: int = 2
const KIND_PACK: int = 3
const KIND_APPAREL: int = 4
## Appended, mirroring sim/GearCatalog.cs GearKind. New kinds go on the END.
const KIND_OBJECTIVE: int = 5

## Mirrors sim Rarity. Ordinals are the sim's: append, never reorder.
const RARITY_COMMON: int = 0
const RARITY_UNCOMMON: int = 1
const RARITY_RARE: int = 2
const RARITY_EPIC: int = 3
const RARITY_LEGENDARY: int = 4

## The colour every item name is drawn in, by rarity: grey, green, blue,
## purple, orange. ONE table, used by every screen that names an item, so a
## rare rifle is the same blue in the stash, the loot panel and the shop. The
## sim decides which item is which tier; game/ only decides what that looks like.
const RARITY_COLOURS: Array[Color] = [
	Color(0.66, 0.68, 0.72),     # common: grey
	Color(0.36, 0.82, 0.40),     # uncommon: green
	Color(0.34, 0.60, 1.00),     # rare: blue
	Color(0.72, 0.42, 0.98),     # epic: purple
	Color(1.00, 0.60, 0.16),     # legendary: orange
]

## The mission objective is not loot and has no tier. It keeps the cold cyan
## the HUD and the chest glyph already use for it.
const OBJECTIVE_COLOUR := Color(0.55, 0.85, 0.95)


## Dollars with thousands separators: "$18,000", not "18000".
static func money(n: int) -> String:
	var digits: String = str(absi(n))
	var out: String = ""
	var count: int = 0
	for i in range(digits.length() - 1, -1, -1):
		out = digits[i] + out
		count += 1
		if count % 3 == 0 and i > 0:
			out = "," + out
	return ("-$" if n < 0 else "$") + out


static func rarity_colour(bridge: RefCounted, item_id: int) -> Color:
	if bridge.GearKindOf(item_id) == KIND_OBJECTIVE:
		return OBJECTIVE_COLOUR
	return RARITY_COLOURS[clampi(bridge.GearRarity(item_id), 0, RARITY_COLOURS.size() - 1)]


## Six weapon sub-slots, mirroring sim AttachSlot.
const ATTACH_COUNT: int = 6

const NONE: int = -1


## A loot label scales with the piece it names, so a rifle reads at a glance
## while a 1x1 attachment does not get a font wider than its own box.
##
## Driven by the LONGEST edge, not the area or the width: a 1x3 item standing on
## end is as big a thing as a 3x1 lying flat, and should read at the same size.
const LABEL_MIN: int = 10
const LABEL_PER_CELL: int = 3
const LABEL_MAX: int = 22


static func label_size(span: Vector2i) -> int:
	var longest: int = maxi(maxi(span.x, span.y), 1)
	return mini(LABEL_MIN + LABEL_PER_CELL * (longest - 1), LABEL_MAX)


## Every item id the build knows, in table order.
static func all_ids(bridge: RefCounted) -> PackedInt32Array:
	var out: PackedInt32Array = PackedInt32Array()
	for i in range(bridge.GearCount):
		out.append(bridge.GearIdAt(i))
	return out


static func ids_for_slot(bridge: RefCounted, slot: int) -> PackedInt32Array:
	var out: PackedInt32Array = PackedInt32Array()
	for id in all_ids(bridge):
		if bridge.GearFitsSlot(id, slot):
			out.append(id)
	return out


static func ids_of_kind(bridge: RefCounted, kind: int) -> PackedInt32Array:
	var out: PackedInt32Array = PackedInt32Array()
	for id in all_ids(bridge):
		if bridge.GearKindOf(id) == kind:
			out.append(id)
	return out


static func size_of(bridge: RefCounted, item_id: int) -> Vector2i:
	return Vector2i(bridge.GearWidth(item_id), bridge.GearHeight(item_id))


## Which weapon sub-slot an attachment fits, or NONE if it is not an attachment.
static func attach_slot_of(bridge: RefCounted, item_id: int) -> int:
	if bridge.GearKindOf(item_id) != KIND_ATTACHMENT:
		return NONE
	return bridge.GearSimA(item_id)
