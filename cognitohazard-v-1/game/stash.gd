extends RefCounted

## The persistent stash and what is worn: a packing grid, the eight equipment
## slots, the six weapon sub-slots, and a text save file.
##
## This is the meta layer of rpg_extension_plan.md §1 -- it PRODUCES a resolved
## Loadout and the sim consumes one. It holds no sim state and feeds no hash.
## apply_to() is the single seam where it touches the sim.
##
## Footprints come from the bridge's gear catalog rather than from a table here,
## so the stash and the mission pack can never disagree about how big a rifle is.

const GRID := preload("res://game/inventory_grid.gd")
const CAT := preload("res://game/item_catalog.gd")

const NONE: int = -1

## Mirrors sim Loadout.MaxCarried. A hand-edited save must not be able to ask
## the sim to place ten thousand items, which is the lesson Level.MaxGuards
## already taught.
const MAX_CARRIED: int = 64

## Sized so a WHOLE kit swap fits. equip_kit strips everything worn back into
## the grid before wearing the new kit, so at its peak the grid holds the stock
## items plus an entire unequipped loadout plus whatever the incoming kit adds.
## At 10x6 that peaked over 60 cells, add() started failing, and a preset
## deployed without its vest or its backpack -- silently, because a skipped item
## looks exactly like an item you chose not to bring.
const DEFAULT_W: int = 12
const DEFAULT_H: int = 10

var grid: RefCounted = null

## The bridge, for gear footprints and slot rules. The stash never steps the sim.
var _bridge: RefCounted = null

## Item id worn in each of the eight slots, or NONE.
var _slots: PackedInt32Array = PackedInt32Array()

## Item id mounted on each weapon sub-slot, or NONE — TWO sets of six, the
## primary's then the holster's, indexed hand * ATTACH_COUNT + sub.
##
## Per weapon, not per player. One shared set meant the drum mag bought for the
## rifle also fed the pistol the moment it was drawn, and the two guns could
## never differ; sim/Loadout.cs carries the same split.
var _attach: PackedInt32Array = PackedInt32Array()

## What the player walks in CARRYING, as item ids in packing order. Not worn
## and not in the grid: this is the third place an item can be, and the one
## that is lost when you die.
var carried: PackedInt32Array = PackedInt32Array()

const HANDS: int = 2

## The smallest bag. Every run starts with at least this on your back -- see
## ensure_pack.
const STARTER_PACK: int = 501


func _init(bridge: RefCounted, width: int = DEFAULT_W, height: int = DEFAULT_H) -> void:
	_bridge = bridge
	grid = GRID.new(width, height)
	_slots = PackedInt32Array()
	_slots.resize(CAT.SLOT_COUNT)
	_slots.fill(NONE)
	_attach = PackedInt32Array()
	_attach.resize(HANDS * CAT.ATTACH_COUNT)
	_attach.fill(NONE)
	carried = PackedInt32Array()


## What a new campaign starts with: a pistol, a small bag, and the clothes you
## stand up in.
##
## It used to issue a rifle, a carrier, a helmet and two attachments — which was
## right when nothing could be bought and the screens needed something in them.
## With a shop and a wage it is exactly wrong: everything handed out free is
## something the economy no longer gets to be about. The Glock is priced at zero
## in the catalogue for the same reason, so buying a second one is never the
## answer to anything.
func stock_default() -> void:
	for item_id in [100, 501, 702, 601]:
		add(item_id)


## How many of an item the stash holds, worn or stored. The shop shows it so a
## second scope is a decision rather than an accident.
func count_of(item_id: int) -> int:
	var n: int = 0
	for slot in range(CAT.SLOT_COUNT):
		if _slots[slot] == item_id:
			n += 1
	for i in range(_attach.size()):
		if _attach[i] == item_id:
			n += 1
	for i in range(carried.size()):
		if carried[i] == item_id:
			n += 1
	for pi in range(grid.capacity()):
		if grid.is_live(pi) and grid.item_of(pi) == item_id:
			n += 1
	return n


## Puts an item in the first place it fits. Returns the placement id, or NONE if
## the stash is too full to take it.
func add(item_id: int) -> int:
	var s: Vector2i = CAT.size_of(_bridge, item_id)
	return grid.auto_place(item_id, s.x, s.y)


## Whole kits the start screen offers, so choosing a loadout is one keypress
## rather than eight drags. A row is [name, note, [[item_id, slot], ...]], and a
## slot of NONE means "wherever it naturally goes" -- which is how attachments
## reach their weapon sub-slot and apparel reaches its own slot.
##
## NEW CONTENT, not ported from anywhere: the spec had no loadouts to preset.
## Built as a function rather than a const because the slot names come from a
## preloaded script, which is not a constant expression.
static func presets() -> Array:
	return [
		["Scout", "quiet, light, a small bag", [
			[101, CAT.SLOT_PRIMARY], [341, -1], [201, CAT.SLOT_VEST],
			[501, CAT.SLOT_BACKPACK], [601, CAT.SLOT_FOOTWARE],
			[702, CAT.SLOT_CHEST], [801, CAT.SLOT_ARMS]]],
		["Assault", "rifle and sidearm, room to carry", [
			[102, CAT.SLOT_PRIMARY], [100, CAT.SLOT_SECONDARY], [312, -1],
			[202, CAT.SLOT_VEST], [502, CAT.SLOT_BACKPACK],
			[602, CAT.SLOT_FOOTWARE], [701, CAT.SLOT_CHEST],
			[402, CAT.SLOT_HELMET]]],
		["Breacher", "shotgun, heavy plate, the big bag", [
			[103, CAT.SLOT_PRIMARY], [100, CAT.SLOT_SECONDARY],
			[203, CAT.SLOT_VEST], [503, CAT.SLOT_BACKPACK],
			[602, CAT.SLOT_FOOTWARE], [701, CAT.SLOT_CHEST],
			[402, CAT.SLOT_HELMET], [802, CAT.SLOT_ARMS]]],
		["Marksman", "scoped rifle, a long look", [
			[102, CAT.SLOT_PRIMARY], [303, -1], [331, -1],
			[201, CAT.SLOT_VEST], [502, CAT.SLOT_BACKPACK],
			[601, CAT.SLOT_FOOTWARE], [702, CAT.SLOT_CHEST]]],
	]


## A sensible opening kit, worn rather than merely owned, so a fresh player can
## strip a body on the first run without first having to find the equipment
## screen. Only used when there is no save file to load instead.
## The kit a new campaign deploys in. A pistol and a satchel: armed, and able to
## carry something home, which is the minimum a run needs to be worth running.
func equip_starting_kit() -> void:
	equip_kit([
		[100, CAT.SLOT_PRIMARY], [STARTER_PACK, CAT.SLOT_BACKPACK],
		[601, CAT.SLOT_FOOTWARE], [702, CAT.SLOT_CHEST],
	])


## Wears one of the presets. Returns the number of items it could not fit, so a
## caller can say so rather than quietly deploying half a kit.
func apply_preset(index: int) -> int:
	var rows: Array = presets()
	if index < 0 or index >= rows.size():
		return -1
	return equip_kit(rows[index][2])


## Strips everything worn back into the grid, then wears the given kit. Each item
## is found in the stash if it is already there and added if it is not.
##
## Best-effort by design: an item that will not fit is skipped and counted rather
## than aborting, because a preset that half-applies and SAYS SO is more useful
## than one that refuses because the stash happens to be crowded. Returns the
## number skipped.
func equip_kit(pairs: Array) -> int:
	for slot in range(CAT.SLOT_COUNT):
		unequip(slot)
	for hand in range(HANDS):
		for sub in range(CAT.ATTACH_COUNT):
			unequip_attach(sub, hand)

	var skipped: int = 0
	for pair in pairs:
		var item_id: int = pair[0]
		var slot: int = pair[1]
		var pi: int = _find(item_id)
		if pi == NONE:
			pi = add(item_id)
		if pi == NONE:
			# No room to stage it, so issue it straight into the slot rather
			# than deploying the player without it.
			if not _issue(item_id, slot):
				skipped += 1
			continue
		var ok: bool = equip_from_grid(pi, slot) if slot != NONE \
			else equip_from_grid(pi)
		if not ok:
			skipped += 1
	return skipped


## Wears an item without staging it in the grid first.
##
## A preset ISSUES gear rather than drawing it from what you happen to own, so a
## crowded stash must never be the reason you deploy without a vest. The grid
## route is still preferred -- it keeps the stash honest about what is where --
## and this is the fallback when there is no room to stage the item at all.
func _issue(item_id: int, slot: int) -> bool:
	if _bridge.GearKindOf(item_id) == CAT.KIND_ATTACHMENT:
		# Issued onto the PRIMARY. A preset names a weapon and its kit, and the
		# weapon a preset is about is the one in your hands.
		var at: int = _rail_index(_bridge.GearSimA(item_id), 0)
		if at < 0:
			return false
		_attach[at] = item_id
		return true
	var want: int = slot if slot != NONE else default_slot_for(item_id)
	if want < 0 or want >= CAT.SLOT_COUNT:
		return false
	if not _bridge.GearFitsSlot(item_id, want):
		return false
	_slots[want] = item_id
	return true


## First placement holding an item id, or NONE.
func _find(item_id: int) -> int:
	for pi in grid.live_ids():
		if grid.item_of(pi) == item_id:
			return pi
	return NONE


func equipped_in(slot: int) -> int:
	return _slots[slot] if slot >= 0 and slot < CAT.SLOT_COUNT else NONE


## What is mounted on one rail of one weapon. `hand` is 0 for the primary and
## 1 for the holster.
func attached_at(sub: int, hand: int = 0) -> int:
	var at: int = _rail_index(sub, hand)
	return _attach[at] if at >= 0 else NONE


func _rail_index(sub: int, hand: int) -> int:
	if sub < 0 or sub >= CAT.ATTACH_COUNT or hand < 0 or hand >= HANDS:
		return -1
	return hand * CAT.ATTACH_COUNT + sub


## Which hand a worn slot is, or -1 when it is not a weapon slot at all.
static func hand_of_slot(slot: int) -> int:
	if slot == CAT.SLOT_PRIMARY:
		return 0
	if slot == CAT.SLOT_SECONDARY:
		return 1
	return -1


## Which slot an item would go to when equipped without naming one. A weapon
## fills the empty hand first, and displaces the primary when both are full.
func default_slot_for(item_id: int) -> int:
	if _bridge.GearKindOf(item_id) == CAT.KIND_ATTACHMENT:
		return NONE
	if _bridge.GearKindOf(item_id) == CAT.KIND_WEAPON:
		if _slots[CAT.SLOT_PRIMARY] == NONE:
			return CAT.SLOT_PRIMARY
		if _slots[CAT.SLOT_SECONDARY] == NONE:
			return CAT.SLOT_SECONDARY
		return CAT.SLOT_PRIMARY
	return _bridge.GearSlotOf(item_id)


## Moves the item at a placement out of the grid and into a slot. Whatever was
## in that slot goes back to the grid.
##
## All-or-nothing: if the displaced item has nowhere to land, the swap is
## refused and nothing moves. A half-applied swap would destroy gear.
## `hand` names the weapon an ATTACHMENT is being fitted to; it is ignored for
## everything else.
func equip_from_grid(pi: int, slot: int = NONE, hand: int = 0) -> bool:
	if not grid.is_live(pi):
		return false
	var item_id: int = grid.item_of(pi)

	if _bridge.GearKindOf(item_id) == CAT.KIND_ATTACHMENT:
		return _swap_into(pi, item_id, _attach,
			_rail_index(_bridge.GearSimA(item_id), hand))

	var want: int = slot if slot != NONE else default_slot_for(item_id)
	if not _bridge.GearFitsSlot(item_id, want):
		return false
	return _swap_into(pi, item_id, _slots, want)


## Returns gear to the grid. Refused, changing nothing, if it will not fit.
func unequip(slot: int) -> bool:
	return _take_out(_slots, slot)


## EXCHANGES what is worn in two slots, which is what dragging one worn slot
## onto another means. Nothing passes through the grid at all.
##
## It used to: the source was unequipped into the grid and then equipped into
## the target, which displaced the TARGET'S item into the grid as well -- so
## dragging the rifle onto the pistol hand left you holding the rifle, the
## pistol in the stash, and one hand EMPTY. It also failed outright when the
## grid was full, which is precisely when a player is rearranging what they
## wear rather than what they own.
##
## All-or-nothing, like every other transaction here: if either item will not
## fit the other's slot, nothing moves.
func swap_slots(a: int, b: int) -> bool:
	if a == b or a < 0 or b < 0 or a >= CAT.SLOT_COUNT or b >= CAT.SLOT_COUNT:
		return false
	var ia: int = _slots[a]
	var ib: int = _slots[b]
	if ia == NONE and ib == NONE:
		return false
	if ia != NONE and not _bridge.GearFitsSlot(ia, b):
		return false
	if ib != NONE and not _bridge.GearFitsSlot(ib, a):
		return false
	_slots[a] = ib
	_slots[b] = ia
	return true


func unequip_attach(sub: int, hand: int = 0) -> bool:
	return _take_out(_attach, _rail_index(sub, hand))


## Resolves what is worn into the sim's Loadout. The ONE place this layer speaks
## to sim/, through the bridge's setters.
##
## Only four of the eight slots reach the sim, because only four have a reader:
## Primary and Secondary are weapons, Vest is armour, and Backpack sizes the
## mission pack. Helmet, footware, shirt and arms are carried, saved and shown,
## and change no stat -- no invented effects until sim/ can honour them.
##
## An attachment is pushed only if the weapon in that hand actually carries the
## sub-slot, so a scope cannot resolve onto a pistol with no sight rail.
func apply_to(bridge: RefCounted) -> void:
	var primary: int = _slots[CAT.SLOT_PRIMARY]
	# The sim models no unarmed state, so an empty hand resolves to its default
	# weapon rather than to nothing.
	var weapon_id: int = bridge.GearSimA(primary) if primary != NONE else 0
	bridge.SetWeapon(weapon_id)

	var secondary: int = _slots[CAT.SLOT_SECONDARY]
	bridge.SetSecondary(bridge.GearSimA(secondary) if secondary != NONE else -1)

	var vest: int = _slots[CAT.SLOT_VEST]
	bridge.SetArmour(bridge.GearSimA(vest) if vest != NONE else 0)

	var pack: int = _slots[CAT.SLOT_BACKPACK]
	bridge.SetBackpack(pack if pack != NONE else 0)

	# The four cosmetic slots. They change no stat, but they are worn, hashed
	# and now changeable in the field, so what you left base wearing has to be
	# what the sim thinks you are wearing.
	for slot in [CAT.SLOT_HELMET, CAT.SLOT_FOOTWARE, CAT.SLOT_CHEST, CAT.SLOT_ARMS]:
		var apparel: int = _slots[slot]
		bridge.SetApparel(slot, apparel if apparel != NONE else 0)

	# The rails, per weapon. A scope in the holster's set reaches the holstered
	# gun and nothing else, which is the whole of what "each weapon has its
	# attachments" means.
	for hand in range(HANDS):
		var gun: int = weapon_id if hand == 0 \
			else (bridge.GearSimA(secondary) if secondary != NONE else -1)
		for sub in range(CAT.ATTACH_COUNT):
			var mounted: int = _attach[_rail_index(sub, hand)]
			var ok: bool = mounted != NONE and gun >= 0 \
				and bridge.WeaponHasSlot(gun, sub)
			bridge.SetAttachmentFor(hand, sub, bridge.GearSimB(mounted) if ok else 0)

	# And what is carried in, which _sync_carry does because the SCREEN has to
	# ask the same question on every drag.
	var refused: PackedInt32Array = _sync_carry()
	# Anything the bag will not take goes back in the stash rather than being
	# dropped on the floor of a save file. It can only happen when the bag was
	# swapped for a smaller one after the kit was packed.
	for i in range(refused.size()):
		var back: int = carried.find(refused[i])
		if back >= 0:
			carried.remove_at(back)
		add(refused[i])
	if refused.size() > 0:
		_sync_carry()


# ------------------------------------------------------ what is carried in
#
# The third place an item can be: not worn, not in the stash grid, but IN THE
# BAG you walk in with. It is also the only one of the three you lose.

## Push the bag and the carry list to the bridge, and report anything the bag
## refused.
##
## stash.gd otherwise touches the sim only in apply_to, and this is the stated
## exception. "Will this fit" is a PACKING question, the sim owns the packing,
## and the answer depends on the bag — so both have to be over there before it
## can be asked. Neither is anything apply_to would not set to the same value.
func _sync_carry() -> PackedInt32Array:
	var bag: int = _slots[CAT.SLOT_BACKPACK]
	_bridge.SetBackpack(bag if bag != NONE else 0)
	_bridge.ClearCarried()
	var refused: PackedInt32Array = PackedInt32Array()
	for i in range(carried.size()):
		if not _bridge.AddCarried(carried[i]):
			refused.append(carried[i])
	return refused


## Would one more item still fit the bag, alongside everything already packed?
func carry_would_fit(item_id: int) -> bool:
	_sync_carry()
	return _bridge.CarriedWouldFit(item_id)


## Stash grid -> the bag. The item LEAVES the stash: it is going with you, and
## what goes with you is what you lose when you die.
func carry_from_grid(pi: int) -> bool:
	if not grid.is_live(pi) or carried.size() >= MAX_CARRIED:
		return false
	var item_id: int = grid.item_of(pi)
	if not carry_would_fit(item_id):
		return false
	if not grid.remove(pi):
		return false
	carried.append(item_id)
	_sync_carry()
	return true


## The bag -> the stash grid. Refused, changing nothing, if the stash has no
## room: an item must never fall between the two.
func uncarry(index: int) -> bool:
	if index < 0 or index >= carried.size():
		return false
	if add(carried[index]) == NONE:
		return false
	carried.remove_at(index)
	_sync_carry()
	return true


## The bag is EMPTIED. What was in it has already been banked into the stash by
## the caller; worn gear is untouched, because you walked out still wearing it.
##
## Not the same as losing it: this is what extracting does, and the difference
## between the two is the whole reason the carry list exists.
func clear_carried() -> void:
	carried = PackedInt32Array()
	_sync_carry()


## EVERYTHING CARRIED OUT OF A MISSION, banked. Returns
## [kept, fenced, fencedValue, handedIn].
##
## The OBJECTIVE is handed in rather than kept: it is the job, the job pays the
## mission rate, and keeping the case as well would be paid for it twice and
## leave a 2x2 lump of nothing in the stash forever.
##
## KEPT GEAR PAYS NO CASH -- the item is the reward. Only what the stash has no
## room for is fenced, which is what stops a full stash costing you the find as
## well as the value.
##
## Lives here rather than in main.gd because main.gd:_settle_run is reachable
## by no test at all (see cognitohazard_loot_flow.md §7), and this is the half
## of it that decides where gear ends up.
func bank_recovered(items: PackedInt32Array) -> Array:
	var kept: int = 0
	var fenced: int = 0
	var value: int = 0
	var handed: int = 0
	for i in range(items.size()):
		var item_id: int = items[i]
		if _bridge.GearIsObjective(item_id):
			handed += 1
			continue
		if add(item_id) == NONE:
			fenced += 1
			value += _bridge.GearPrice(item_id)
		else:
			kept += 1
	# The bag is empty now: everything that was in it has just been banked or
	# fenced. Leaving the carry list standing would deploy the next run with a
	# copy of all of it.
	clear_carried()
	return [kept, fenced, value, handed]


## WHAT YOU WALKED OUT WEARING, written back over what you walked in wearing.
## Returns how many slots and rails changed.
##
## Both arguments are SIM kits, taken with sim_kit(): `deployed` on the tick the
## run began and `extracted` on the tick it ended. Only what DIFFERS is taken
## from the sim. That is what makes this exact rather than approximate:
## - a field equip changed the slot, so the sim's item is now worn -- and the
##   item it displaced went into the pack, which bank_recovered puts in the
##   grid. Nothing is lost and nothing is minted.
## - an unchanged slot keeps the STASH'S answer. It is not always the sim's: an
##   empty primary deploys as the default Glock (the sim has no unarmed state),
##   and a rail the gun lacks deploys as nothing. Copying the sim wholesale
##   would mint that Glock and destroy that scope.
##
## Before this, extraction read the pack and nothing else, so everything put on
## in the field was destroyed on the way home and the item it replaced was
## duplicated (cognitohazard_loot_flow.md §6.1).
##
## Only the slots the sim carries: the sim has no legs slot, so it is shorter
## than _slots, and what it does not carry cannot have changed in the field.
func reconcile_worn(deployed: Array, extracted: Array) -> int:
	var changed: int = 0
	var was: PackedInt32Array = deployed[0]
	var now: PackedInt32Array = extracted[0]
	for slot in range(mini(CAT.SLOT_COUNT, mini(was.size(), now.size()))):
		if now[slot] == was[slot]:
			continue
		_slots[slot] = now[slot] if now[slot] > 0 else NONE
		changed += 1
	for hand in range(HANDS):
		var r_was: PackedInt32Array = deployed[1 + hand]
		var r_now: PackedInt32Array = extracted[1 + hand]
		for sub in range(mini(CAT.ATTACH_COUNT, mini(r_was.size(), r_now.size()))):
			if r_now[sub] == r_was[sub]:
				continue
			_attach[_rail_index(sub, hand)] = r_now[sub] if r_now[sub] > 0 else NONE
			changed += 1
	return changed


## The sim's worn kit as reconcile_worn reads it: [worn by slot, rails of the
## primary, rails of the holster], all item ids, 0 for empty.
static func sim_kit(bridge: RefCounted) -> Array:
	return [PackedInt32Array(bridge.GetWornSim()),
		PackedInt32Array(bridge.GetRailsSim(0)),
		PackedInt32Array(bridge.GetRailsSim(1))]


## EVERYTHING ON THE PLAYER, destroyed: what is worn, what is fitted to it, and
## what is in the bag. What dying costs.
##
## The stash GRID survives untouched. That is the whole shape of the decision:
## what you leave at base is safe, and what you take is not.
func lose_kit() -> void:
	_slots.fill(NONE)
	_attach.fill(NONE)
	carried = PackedInt32Array()
	_sync_carry()


## The player ALWAYS deploys with a bag. With the backpack slot empty -- after a
## death, or after taking the bag off at base -- a satchel goes on: the one in
## the grid if you own one, so taking it off and redeploying cannot mint a
## second, and a newly issued one otherwise. Carried is necessarily empty here
## (nothing fits in no bag), so there is no carry list to re-pack.
## Returns true if it changed what is worn.
func ensure_pack() -> bool:
	if _slots[CAT.SLOT_BACKPACK] != NONE:
		return false
	var pi: int = _find(STARTER_PACK)
	if pi != NONE and equip_from_grid(pi, CAT.SLOT_BACKPACK):
		return true
	return _issue(STARTER_PACK, CAT.SLOT_BACKPACK)


## Text, like the levels and the replays: a save you can read in a diff is a
## save you can debug.
func to_text() -> String:
	var out: String = "stash 2\n" + grid.to_text()
	for slot in range(CAT.SLOT_COUNT):
		if _slots[slot] != NONE:
			out += "equip %d %d\n" % [slot, _slots[slot]]
	for sub in range(CAT.ATTACH_COUNT):
		if _attach[sub] != NONE:
			out += "attach %d %d\n" % [sub, _attach[sub]]
	# Appended, so a stash written before the holster had rails of its own
	# still loads: no attach2 lines is an empty second set, which is what
	# those saves meant.
	for sub in range(CAT.ATTACH_COUNT):
		var it: int = _attach[_rail_index(sub, 1)]
		if it != NONE:
			out += "attach2 %d %d\n" % [sub, it]
	for i in range(carried.size()):
		out += "carry %d\n" % carried[i]
	return out


## Total, the way the level parser is total: an unreadable or unplaceable line is
## skipped, never fatal. Returns the number of lines it could not honour, so a
## caller can report a partly recovered save instead of pretending.
func from_text(text: String) -> int:
	var grid_lines: PackedStringArray = PackedStringArray()
	var tail: PackedStringArray = PackedStringArray()
	for raw in text.split("\n"):
		var line: String = raw.strip_edges()
		if line.is_empty() or line.begins_with("#") or line.begins_with("stash "):
			continue
		if line.begins_with("grid ") or line.begins_with("item "):
			grid_lines.append(line)
		else:
			tail.append(line)

	var skipped: int = grid.from_text("\n".join(grid_lines))
	_slots.fill(NONE)
	_attach.fill(NONE)
	carried = PackedInt32Array()

	for line in tail:
		var f: PackedStringArray = line.split(" ", false)
		if f.size() >= 3 and f[0] == "equip":
			var slot: int = int(f[1])
			var item_id: int = int(f[2])
			if slot >= 0 and slot < CAT.SLOT_COUNT and _bridge.GearExists(item_id):
				_slots[slot] = item_id
			else:
				skipped += 1
		elif f.size() >= 3 and (f[0] == "attach" or f[0] == "attach2"):
			var at: int = _rail_index(int(f[1]), 1 if f[0] == "attach2" else 0)
			var item2: int = int(f[2])
			if at >= 0 and _bridge.GearExists(item2):
				_attach[at] = item2
			else:
				skipped += 1
		elif f.size() >= 2 and f[0] == "carry":
			var item3: int = int(f[1])
			if _bridge.GearExists(item3) and carried.size() < MAX_CARRIED:
				carried.append(item3)
			else:
				skipped += 1
		else:
			skipped += 1
	return skipped


## Grid -> slot, displacing whatever is there back into the grid.
func _swap_into(pi: int, item_id: int, target: PackedInt32Array, index: int) -> bool:
	if index < 0 or index >= target.size():
		return false
	var displaced: int = target[index]
	var at: Vector2i = grid.pos_of(pi)
	var rot: int = grid.rot_of(pi)

	if not grid.remove(pi):
		return false

	if displaced != NONE:
		var ds: Vector2i = CAT.size_of(_bridge, displaced)
		# Prefer the vacated cells, so a swap does not shuffle the whole stash.
		var back: int = grid.place(displaced, ds.x, ds.y, at.x, at.y, rot)
		if back == NONE:
			back = grid.auto_place(displaced, ds.x, ds.y)
		if back == NONE:
			# Nowhere to put it: undo and refuse rather than lose the gear.
			var s: Vector2i = CAT.size_of(_bridge, item_id)
			grid.place(item_id, s.x, s.y, at.x, at.y, rot)
			return false

	target[index] = item_id
	return true


## Slot -> grid.
func _take_out(target: PackedInt32Array, index: int) -> bool:
	if index < 0 or index >= target.size():
		return false
	var item_id: int = target[index]
	if item_id == NONE:
		return false
	if add(item_id) == NONE:
		return false
	target[index] = NONE
	return true
