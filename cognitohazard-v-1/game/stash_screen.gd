extends Node2D

## THE STASH: everything that happens between missions, on one screen.
##
## Three columns: the CHARACTER -- a paper doll with every worn slot on its
## body part, and the rails of one gun -- then the stash grid with the bag
## under it, then the mission select. The mission select and the inventory
## were two screens ('E' and the start menu) and the
## split never made sense: what you are carrying and where you are taking it are
## the same decision, and answering them on separate screens meant flipping back
## and forth to make one choice.
##
## This is the META layer (rpg_extension_plan.md §1). Nothing here steps the sim.
## Closing the screen resolves what is worn into a Loadout and STAGES it for the
## next run. It used to restart immediately, which meant closing a screen you
## had opened to look at something wiped the run you were in. F5 is the only
## thing that restarts now.
##
## The drag NEVER mutates anything until the drop is committed. A pick-up only
## remembers where the item came from; if the drop is refused, nothing has moved
## and nothing can be lost halfway.
##
## Mouse positions come from get_local_mouse_position(), never from an
## InputEvent's own position: the canvas is scaled and letterboxed, so a raw
## window pixel would land on the wrong cell (see CLAUDE.md, Display).

const CAT := preload("res://game/item_catalog.gd")
const STASH := preload("res://game/stash.gd")
const GRID := preload("res://game/inventory_grid.gd")
const LEVELS := preload("res://game/levels.gd")
const MISSIONS := preload("res://game/missions.gd")
const TIP := preload("res://game/item_tooltip.gd")

const FIELD_W: float = 960.0
const FIELD_H: float = 560.0

## A centred panel over the live field rather than a full-screen takeover, so
## the mission stays visible behind the gear being arranged.
## Full screen, not a panel over the field: between missions there is no field
## worth seeing behind it.
const SCREEN_H: float = FIELD_H + 60.0
const PANEL_W: float = FIELD_W
const PANEL_H: float = SCREEN_H
const PANEL_X: float = 0.0
const PANEL_Y: float = 0.0

## THREE columns: the CHARACTER (the paper doll and the rails of one gun), the
## STASH (the grid and the bag you pack), and the MISSIONS. The whole of the
## inventory -- worn, fitted, stored and carried -- is on this one screen, so
## nothing you own is ever somewhere you cannot see it.
## Everything inventory is left of DIVIDER, everything mission right of it.
const COL2_X: float = 344.0
const DIVIDER: float = 600.0
const MISSION_X: float = 612.0
const MISSION_W: float = 332.0

## Everything below is PANEL-RELATIVE. _draw() shifts the transform to the panel
## origin and the hit tests subtract it, so the layout is written once and the
## panel can move without touching any of it.
const CELL: float = 20.0
const GRID_X: float = COL2_X
const GRID_Y: float = 80.0

## THE PAPER DOLL. Every worn slot sits ON the body part it dresses: helmet on
## the head, shirt and vest on the torso, arms and bag on the shoulders, a gun
## in each hand, legs, and boots on the feet. A column of labelled boxes made
## the player read nine names to find the vest; a body is read at a glance.
##
## ONE layout for both views -- only the origin moves -- so the vest is in the
## same place on the body at base and in a corridor.
const SLOT_X: float = 16.0
const DOLL_Y: float = 62.0
const M_DOLL_X: float = 24.0
const M_DOLL_Y: float = 84.0
const DOLL_W: float = 312.0
const DOLL_H: float = 378.0
## The body's centre line, doll-relative.
const DOLL_CX: float = 156.0
const SLOT_W: float = 100.0
const SLOT_H: float = 36.0

## Where each slot's box sits on the doll, doll-relative, INDEXED BY GearSlot
## ordinal (helmet, vest, backpack, footware, chest, arms, primary, secondary,
## legs). A new slot appends a position here or has no box at all.
const DOLL_AT: Array = [
	Vector2(106, 6),        # helmet     -- the head
	Vector2(106, 116),      # vest       -- the chest plate
	Vector2(212, 70),       # backpack   -- over the right shoulder
	Vector2(106, 342),      # footware   -- the feet
	Vector2(106, 70),       # chest      -- the shirt, upper torso
	Vector2(0, 70),         # arms       -- the left shoulder
	Vector2(212, 212),      # primary    -- the right hand
	Vector2(0, 212),        # secondary  -- the left hand
	Vector2(106, 256),      # legs       -- the thighs
]

## What each box on the doll is called, by GearSlot ordinal. SHORTER than
## GearSlotName ("secondary weapon", "shirt/chest"), which does not fit a box
## the width of a torso beside its tag -- and on a body, "primary" in a hand
## already says weapon.
const DOLL_LABELS: Array = ["HELMET", "VEST", "BACKPACK", "FEET", "SHIRT",
	"ARMS", "PRIMARY", "SECONDARY", "LEGS"]

## The rails of ONE weapon, under the doll: three columns of two, each box a
## rail name over what is on it.
const ATTACH_Y: float = 470.0
const ATTACH_H: float = 30.0
const ATTACH_W: float = 100.0
const ATTACH_COL_GAP: float = 6.0
const ATTACH_ROWS: int = 2

## THE BAG, under the stash grid: at base the kit you are packing, in the field
## the live mission pack.
##
## The pack is SIM STATE in the field: it can be looked at and dropped from,
## but not rearranged, because a drag cannot be recorded into an InputFrame and
## a rearrangement nobody recorded desyncs every replay.
const PACK_CELL: float = 20.0
const PACK_X: float = COL2_X
const PACK_Y: float = 330.0

## MISSION MODE geometry: the doll on the left, the pack at nearly twice the
## size on the right, the floor under it. There is no grid and no mission
## column in the field, and the pack is the point of this view.
const M_PACK_X: float = 470.0
const M_PACK_Y: float = 118.0
const M_PACK_CELL: float = 38.0
## Drag an item here to put it on the floor. Its own target rather than "drop
## outside the panel", which is already how a drag is cancelled. FIELD ONLY.
const M_BIN_X: float = 470.0
const M_BIN_Y: float = 334.0
const M_BIN_W: float = 440.0
const M_BIN_H: float = 72.0
const M_ATTACH_Y: float = 440.0

## ALL the worn slots, weapons and armour first because those are what a
## firefight is about. Every one of them can be changed in the field, so every
## one of them is shown: gear that can be looted with nowhere to put it is the
## bug this list used to be.
const MISSION_SLOTS: Array = [CAT.SLOT_PRIMARY, CAT.SLOT_SECONDARY,
	CAT.SLOT_VEST, CAT.SLOT_BACKPACK, CAT.SLOT_HELMET,
	CAT.SLOT_CHEST, CAT.SLOT_ARMS, CAT.SLOT_LEGS, CAT.SLOT_FOOTWARE]

const SAVE_PATH: String = "user://stash.txt"

var active: bool = false
var bridge: RefCounted
var stash: RefCounted
var draws: int = 0

var _font: Font

## Where the carried item came from. Nothing is removed until the drop lands.
## 0 nothing, 1 a stash placement, 2 a worn slot, 3 a weapon sub-slot.
var _drag: int = 0
var _drag_item: int = -1
var _drag_pi: int = -1
var _drag_slot: int = -1

## Which weapon an attachment being carried came off. A drag that started on
## the primary's rail must go back to the primary's rail, even if the block has
## been switched to the other gun in between.
var _drag_hand: int = 0
var _notice: String = ""
var _notice_t: float = 0.0

## The pack, as SimBridge.GetPackPlacements returns it: stride 5 of
## placement, item, x, y, rot. Refreshed on open and after every drop.
var _pack: PackedInt32Array = PackedInt32Array()

## The placement index a drop names, or -1. Read and cleared by main.gd, which
## is the only thing allowed to hand it to the sim.
## MISSION MODE: this screen opened from inside a run, on E.
##
## It then shows ONLY what the player is carrying -- the four worn slots the sim
## actually reads, the mission pack, and the floor -- and nothing about base.
## The stash grid and the mission select are not merely hidden: a stash is at
## base and cannot be reached from a corridor, and the mission has already been
## chosen. Showing either would be offering something that cannot happen.
##
## The worn kit in this mode comes from the SIM, not from the stash object,
## because mid-run the sim's Loadout is the truth and the two deliberately
## disagree the moment anything is equipped in the field.
var mission_mode: bool = false

## WHICH WEAPON'S RAILS the attachment block shows: 0 the primary, 1 the
## holster. There are two sets now and room on screen for six boxes, so the
## block shows one gun at a time and says which; clicking a weapon slot picks
## it. Defaulting to the primary, which is the gun most kits are about.
var rail_hand: int = 0

## bridge.GetWornSim(), refreshed with the pack. Item ids by GearSlot, 0 empty.
var _worn_sim: PackedInt32Array = PackedInt32Array()

## A staged equip, as a packed InputFrame.EquipPick, or 0 for none. Staged only:
## main.gd hands it to the sim on the next tick, because putting a rifle in the
## player's hands is sim state and a replay has to reproduce it.
var pending_equip: int = 0
var pending_equip_item: int = -1

var pending_drop: int = -1

## The item id that placement holds, so main.gd can tell the loot panel the
## player already knows what it is.
var pending_drop_item: int = -1

## The campaign ledger, for the money line and the per-mission history.
var campaign: RefCounted

## The mission half. Levels on disk, which is highlighted, and its summary --
## cached rather than re-parsed per frame, since it costs a file read and a
## full parse and only changes when the selection does.
var _levels: PackedStringArray = PackedStringArray()
var _level: int = 0
var _summary: PackedInt32Array = PackedInt32Array()
var _title: String = ""

## Four lines a row: name, floor, stakes, and what is on it to take. Raised
## from 62 when the loot line joined; five levels still end the stakes block
## well above the footer.
const MISSION_ROW_H: float = 76.0
const MISSION_ROWS_Y: float = 96.0

signal deploy_requested()
signal shop_requested()

signal dropped()
## Something was staged to be put on. main.gd closes the screen on this, so the
## very next tick applies it and the player sees it happen.
signal equipped()

signal closed()

const C_BG := Color(0.035, 0.042, 0.052)
const C_PANEL := Color(0.07, 0.082, 0.10)
const C_LINE := Color(0.18, 0.20, 0.25)
const C_TEXT := Color(0.82, 0.86, 0.92)
const C_DIM := Color(0.42, 0.47, 0.54)
const C_SEL := Color(1.0, 0.95, 0.55)
const C_GOOD := Color(0.35, 0.80, 0.55)
const C_BAD := Color(0.92, 0.45, 0.40)
const C_ITEM := Color(0.20, 0.28, 0.34)
## Loot figures on the mission select: warm, like the chests that hold them.
const C_LOOT := Color(0.85, 0.66, 0.36)
const C_EMPTY := Color(0.10, 0.12, 0.15)

## The panel is translucent and the field behind it is dimmed rather than hidden.
const C_SCRIM := Color(0.0, 0.0, 0.0, 0.42)
const C_PANEL_BG := Color(0.043, 0.051, 0.063, 0.88)
const C_PANEL_EDGE := Color(0.26, 0.30, 0.37, 0.95)


func _ready() -> void:
	_font = ThemeDB.fallback_font
	visible = false
	z_index = 200


## Re-read the pack from the sim. Called on open and after a drop lands, since
## placements shift when one is removed.
## Re-scan the levels directory. Done on every open, so a level saved from the
## builder shows up without relaunching.
func refresh_levels(current_level: String = "") -> void:
	_levels = LEVELS.list()
	var at: int = LEVELS.index_of(_levels, current_level)
	_level = at if at >= 0 else 0
	_reload_summary()


func level_count() -> int:
	return _levels.size()


## The level DEPLOY will run, or "" when the directory is empty and main.gd
## should keep whatever it already had.
func selected_level() -> String:
	if _levels.is_empty():
		return ""
	return _levels[clampi(_level, 0, _levels.size() - 1)]


func cycle_level(delta: int) -> void:
	if _levels.size() <= 1:
		return
	_level = (_level + delta + _levels.size()) % _levels.size()
	_reload_summary()
	queue_redraw()


func _reload_summary() -> void:
	_summary = PackedInt32Array()
	_title = ""
	if bridge == null or _levels.is_empty():
		return
	var text: String = LEVELS.read(selected_level())
	if text.is_empty():
		_title = "unreadable"
		return
	_summary = PackedInt32Array(bridge.LevelSummary(text))
	_title = bridge.LevelTitle(text)


func refresh_pack() -> void:
	if bridge == null:
		_pack = PackedInt32Array()
		_worn_sim = PackedInt32Array()
		return
	# AT BASE THE PACK IS EMPTY, whatever the sim still holds. A mission builds
	# its pack at Restart, so between runs GetPackPlacements returns the pack of
	# the run that just ENDED -- and _settle_run has already banked every one of
	# those items into the stash. Drawing them here showed each recovered item
	# TWICE, once in the stash grid where it now lives and once in a panel that
	# would not let go of it, which is exactly what "I cannot move my stuff out
	# of my pack" looks like.
	# AT BASE this is the kit you are PACKING, laid out by the sim's own
	# AutoPlace in the bag you will deploy with, so what is shown is where it
	# actually lands. In the field it is the live pack.
	_pack = PackedInt32Array(bridge.GetPackPlacements()) if mission_mode \
		else PackedInt32Array(bridge.CarriedPlacements())
	_worn_sim = PackedInt32Array(bridge.GetWornSim())


## `in_mission` opens the FIELD view: the pack, the four slots the sim reads,
## and the floor. See mission_mode.
func open_screen(current_level: String = "", in_mission: bool = false) -> void:
	mission_mode = in_mission
	pending_equip = 0
	pending_equip_item = -1
	# A staged action names a PACK PLACEMENT, and placements belong to the pack
	# that was open when they were staged. One left over from a previous
	# opening would be handed to the sim naming an index into a pack that has
	# since changed -- or, worse, a pack from a previous run.
	pending_drop = -1
	pending_drop_item = -1
	_cancel_drag()
	if stash == null:
		stash = STASH.new(bridge)
		# A saved stash if there is one, otherwise a starting kit.
		var loaded: bool = false
		if FileAccess.file_exists(SAVE_PATH):
			var skipped: int = stash.from_text(FileAccess.get_file_as_string(SAVE_PATH))
			loaded = stash.grid.count() > 0
			if skipped > 0:
				_notice = "%d unreadable line(s) in the saved stash were skipped" % skipped
				_notice_t = 5.0
		if not loaded:
			stash.stock_default()
	refresh_pack()
	refresh_levels(current_level)
	active = true
	visible = true
	queue_redraw()


## Close WITHOUT announcing it, for the shop hop: the shop reopens the stash
## when it closes, and firing `closed` would send the player to the title in
## between.
func close_screen_silent() -> void:
	_cancel_drag()
	_save()
	active = false
	visible = false


func close_screen() -> void:
	_cancel_drag()
	_save()
	active = false
	visible = false
	closed.emit()


func _save() -> void:
	var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	if f == null:
		push_error("could not write stash: %s" % SAVE_PATH)
		return
	f.store_string(stash.to_text())
	f.close()


func _process(delta: float) -> void:
	if not active:
		return
	# In the field the rails shown are the ones on the gun IN HAND, and the
	# hand can change under the screen: X is a keyboard key and is not withheld
	# while this is up, so the strip has to follow it.
	if mission_mode:
		rail_hand = 1 if bridge.ActiveWeaponSlot == 1 else 0
	# A drag can only be live while the button is. Releasing OUTSIDE the window
	# never delivers a button-up, so _drag stayed set -- and press_at refuses
	# while something is already carried, so every click after that did nothing
	# at all. The screen simply stopped responding, with nothing to say why.
	if _drag != 0 and not Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT):
		_cancel_drag()
	if _notice_t > 0.0:
		_notice_t -= delta
	queue_redraw()


func _unhandled_input(event: InputEvent) -> void:
	if not active:
		return

	# R turns the item under the cursor, or the one being carried.
	if event.is_action_pressed("reload"):
		if _drag != 0:
			# A carried item has no cell yet, so turning it is free: it is only
			# resolved against the grid when it lands.
			_drag_rot_flip()
		else:
			var pi: int = grid_placement_at(_mouse())
			if pi != GRID.NONE and not stash.grid.rotate_placement(pi):
				_say("no room to turn that here")
		return

	# The cursor is read ONCE, here, and handed down. Every hit test below takes
	# the point rather than reading it back: get_local_mouse_position() has no
	# cursor to read in a headless run, so a press that read it for itself was a
	# press no harness could make -- which is why every drag path through this
	# screen went untested while the drops they end in were covered twice over.
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed:
			press_at(_mouse())
		else:
			release_at(_mouse())


## Turning a carried item: tracked separately because it is not in the grid.
var _drag_rot: int = GRID.ROT_NONE


func _drag_rot_flip() -> void:
	_drag_rot = GRID.ROT_NONE if _drag_rot == GRID.ROT_90 else GRID.ROT_90


## The left button went down at `m`, in panel space. See _unhandled_input for
## why the point is an argument.
func press_at(m: Vector2) -> void:
	if _drag != 0:
		return

	var pi: int = grid_placement_at(m)
	if pi != GRID.NONE:
		_drag = 1
		_drag_pi = pi
		_drag_item = stash.grid.item_of(pi)
		_drag_rot = stash.grid.rot_of(pi)
		return

	# In the field, worn gear cannot be taken OFF: the sim models no unarmed
	# state, and there is nowhere for a vest to go but a pack that may not have
	# room. Putting things ON is the whole of what this view offers.
	if not mission_mode:
		var slot: int = slot_at(m)
		# An EMPTY weapon slot still picks which gun's rails are shown. There
		# is nothing there to lift, and a holster you have not filled yet is
		# exactly the one whose rails you want to look at.
		if slot != -1 and stash.equipped_in(slot) == STASH.NONE:
			_select_rail(slot)
			return
		if slot != -1 and stash.equipped_in(slot) != STASH.NONE:
			_drag = 2
			_drag_slot = slot
			_drag_item = stash.equipped_in(slot)
			_drag_rot = GRID.ROT_NONE
			return

		var sub: int = attach_at(m)
		if sub != -1 and stash.attached_at(sub, rail_hand) != STASH.NONE:
			_drag = 3
			_drag_slot = sub
			_drag_hand = rail_hand
			_drag_item = stash.attached_at(sub, rail_hand)
			_drag_rot = GRID.ROT_NONE
			return

	# Drag 4: out of the MISSION PACK. The only thing that can be done with it
	# is drop it on the floor -- moving it into the stash mid-run would teleport
	# loot home, and rearranging the pack cannot be recorded.
	var pack_pi: int = pack_placement_at(m)
	if pack_pi != -1:
		_drag = 4
		_drag_pi = pack_pi
		_drag_item = _pack_item_of(pack_pi)
		_drag_rot = GRID.ROT_NONE


## The left button came up at `m`, in panel space. See _unhandled_input for why
## the point is an argument.
func release_at(m: Vector2) -> void:
	if _drag == 0:
		return

	var to_slot: int = slot_at(m)
	var to_sub: int = attach_at(m)
	var to_cell: Vector2i = cell_at(m)
	var ok: bool = false

	# Anything dropped on the bin goes on the floor. From the PACK that is a
	# recorded sim action; from the stash or a worn slot it is refused, because
	# the stash is at base and cannot be thrown on the ground of a level.
	if over_bin(m):
		if not _drop_on_floor():
			_say("only what you are carrying can be dropped")
		_cancel_drag()
		return

	# Out of the pack IN THE FIELD there are exactly two destinations: the
	# floor, handled above, and a worn slot -- which is an EQUIP. Anywhere else
	# it goes back where it was, because rearranging the pack cannot be
	# recorded into an InputFrame.
	#
	# AT BASE the bag is not sim state yet -- it is a list the stash is still
	# packing -- so an item can come back OUT of it into the stash. That falls
	# through to the ordinary destinations below.
	if _drag == 4 and mission_mode:
		if to_slot != -1:
			_equip_from_pack(to_slot)
		_cancel_drag()
		return

	if over_pack(m) and not mission_mode:
		# The BAG, at base: the kit you walk in with. Only from the stash, and
		# only here -- in the field the pack is the sim's and a drag into it
		# could not be recorded into an InputFrame.
		if not _drop_on_pack():
			_say("that will not go in the bag" if _drag == 1
				else "only the stash packs the bag")
		_cancel_drag()
		refresh_carry()
		return

	if to_slot != -1 and _drag == 2 and to_slot == _drag_slot:
		# Released on the slot it came off: a CLICK, not a move. It picks which
		# weapon the rail block shows. It used to report "that will not go
		# there", which is a strange thing to say about putting something back
		# exactly where it was.
		_select_rail(to_slot)
		_cancel_drag()
		return

	if to_slot != -1:
		ok = _drop_on_slot(to_slot)
	elif to_sub != -1:
		# The WEAPON ATTACHMENT rail. It was drawn, and press_at could lift an
		# item out of it, and release_at named no destination for it at all --
		# so a scope dragged onto its own rail was neither a slot nor a cell,
		# fell through to the cancel below, and simply evaporated. Nothing said
		# so, because nothing had gone wrong: the drag had just ended nowhere.
		ok = _drop_on_attach(to_sub)
	elif to_cell.x >= 0:
		ok = _drop_on_grid(to_cell)
	else:
		# Dropped on nothing at all: treat as a cancel rather than a loss.
		_cancel_drag()
		return

	if not ok:
		_say("that will not go there")
	_cancel_drag()
	refresh_carry()


## Stage the carried pack item to be PUT ON.
##
## Only stages it, for the same reason dropping only stages: equipping changes
## the sim's Loadout -- damage, spread, speed, magazine size -- and all of that
## feeds the hash, so it has to reach the world as recorded intent on a tick
## rather than as a menu reaching in.
##
## The refusal comes from bridge.CanEquipMidRun, which is StepEquip's own rule
## read back. Asking the sim rather than re-deciding here is what stops the
## screen promising something the tick will then ignore.
func _equip_from_pack(slot: int) -> bool:
	if _drag != 4 or _drag_pi < 0:
		return false
	if not mission_mode:
		# Unreachable from the UI now that the base pack draws empty, but this
		# is the seam that stages sim state, so it keeps its own guard.
		_say("that is the mission pack \u2014 gear is put on from the stash")
		return false
	if not bridge.CanEquipMidRun(_drag_item, slot):
		_say("%s does not go in the %s out here"
			% [bridge.GearName(_drag_item), bridge.GearSlotName(slot)])
		return false
	# The one equip that can fail for a reason the item alone does not show:
	# a smaller bag may simply not hold what is already being carried.
	if slot == CAT.SLOT_BACKPACK and not bridge.BackpackWouldHold(_drag_item):
		_say("what you are carrying will not fit a %s"
			% bridge.GearName(_drag_item))
		return false

	var pick: int = bridge.MakeEquipPick(_drag_pi, slot)
	if pick == 0:
		_say("cannot equip that from there")
		return false

	pending_equip = pick
	pending_equip_item = _drag_item
	_say("equipping %s" % bridge.GearName(_drag_item))
	equipped.emit()
	return true


## Stage the carried item to be put on the floor. Refuses anything that is not
## from the MISSION PACK: the stash is at base and cannot be thrown on the
## ground of a level.
##
## Only STAGES it. main.gd hands pending_drop to the sim as recorded intent on
## the next tick, because moving sim state from a menu is what a replay cannot
## reproduce.
func _drop_on_floor() -> bool:
	if _drag != 4 or _drag_pi < 0:
		return false
	# Only from inside a run. There is no floor at base -- the pack shown there
	# belongs to a mission that has not begun -- and a drop staged here was
	# still staged when one did, so the sim performed it on the FIRST TICK of
	# the next mission. over_bin() refuses at base as well; this is the guard on
	# the seam that actually writes the recorded intent.
	if not mission_mode:
		return false
	pending_drop = _drag_pi
	pending_drop_item = _drag_item
	_say("dropping %s" % bridge.GearName(_drag_item))
	dropped.emit()
	return true


func _drop_on_slot(slot: int) -> bool:
	if _drag == 1:
		return stash.equip_from_grid(_drag_pi, slot)
	# Slot to slot: an EXCHANGE, which is what dragging one worn thing onto
	# another means everywhere else. See stash.swap_slots for what it used to
	# do instead.
	if _drag == 2 and _drag_slot != slot:
		return stash.swap_slots(_drag_slot, slot)
	return false


## Grid or sub-slot -> a WEAPON SUB-SLOT.
##
## The item names its own sub-slot (GearSimA), so this only accepts the one it
## belongs in: a scope dropped on the grip rail is refused and says so, rather
## than being quietly fitted to the sight rail because the sim reads the item
## and ignores the destination. That exact confusion was a bug in the field
## view already.
func _drop_on_attach(sub: int) -> bool:
	if _drag == 1:
		if CAT.attach_slot_of(bridge, _drag_item) != sub:
			return false
		# No slot argument: equip_from_grid routes an attachment by its kind.
		# The HAND is the gun the block is showing, which is the gun the
		# player is looking at when they drop it.
		return stash.equip_from_grid(_drag_pi, STASH.NONE, rail_hand)
	if _drag == 3:
		# Put back where it came from. It never left the sub-slot -- press_at
		# only marks it carried -- so this is a no-op, not a failure.
		return _drag_slot == sub and _drag_hand == rail_hand
	return false


func _drop_on_grid(cell: Vector2i) -> bool:
	if _drag == 1:
		return stash.grid.move(_drag_pi, cell.x, cell.y, _drag_rot)
	if _drag == 2:
		return stash.unequip(_drag_slot)
	if _drag == 3:
		return stash.unequip_attach(_drag_slot, _drag_hand)
	# Out of the BAG and back into the stash, at base. The placement index a
	# base pack names is the carry list's own index; see CarriedPlacements.
	if _drag == 4 and not mission_mode:
		return stash.uncarry(_drag_pi)
	return false


## Which weapon the attachment block is about. A no-op for anything that is
## not a weapon slot, so clicking the vest does not silently change it.
func _select_rail(slot: int) -> void:
	var hand: int = STASH.hand_of_slot(slot)
	if hand < 0 or mission_mode or hand == rail_hand:
		return
	rail_hand = hand
	_say("attachments: %s" % bridge.GearSlotName(slot))
	queue_redraw()


## Anything -> THE BAG, at base. Only out of the stash: worn gear comes off
## into the stash first, and the bag is not a second stash.
func _drop_on_pack() -> bool:
	if mission_mode or _drag != 1:
		return false
	return stash.carry_from_grid(_drag_pi)


## The mission pack panel, as a drop target. Its rect, not its contents: an
## empty bag has to be droppable into or nothing could ever be packed.
func over_pack(m: Vector2) -> bool:
	var dims: Vector2i = pack_dims()
	if dims.x <= 0 or dims.y <= 0:
		return false
	var cw: float = pack_cell()
	return Rect2(pack_origin(), Vector2(dims.x * cw, dims.y * cw)).has_point(m)


func _cancel_drag() -> void:
	_drag = 0
	_drag_item = -1
	_drag_pi = -1
	_drag_slot = -1
	_drag_hand = 0
	_drag_rot = GRID.ROT_NONE


func _say(msg: String) -> void:
	_notice = msg
	_notice_t = 2.5


func _find_placement(item_id: int) -> int:
	for pi in stash.grid.live_ids():
		if stash.grid.item_of(pi) == item_id:
			return pi
	return GRID.NONE


# ------------------------------------------------------------------ hit tests

## The cursor in panel space. Always via get_local_mouse_position(), never an
## InputEvent's own position: the canvas is scaled and letterboxed, so a raw
## window pixel would land on the wrong cell (CLAUDE.md, Display).
func _mouse() -> Vector2:
	return get_local_mouse_position() - Vector2(PANEL_X, PANEL_Y)


## The floor, as a drop target. FIELD ONLY: at base the bin is not drawn and
## not hittable, because a stash is at base and there is no ground of a level
## to put anything on.
func over_bin(m: Vector2) -> bool:
	return mission_mode and bin_rect().has_point(m)


## Placement index of the pack item at `m`, or -1.
func pack_placement_at(m: Vector2) -> int:
	var o: Vector2 = pack_origin()
	var cell: float = pack_cell()
	var i: int = 0
	while i < _pack.size():
		var r := Rect2(o.x + _pack[i + 2] * cell, o.y + _pack[i + 3] * cell,
			_pack_w(i) * cell, _pack_h(i) * cell)
		if r.has_point(m):
			return _pack[i]
		i += 5
	return -1


func _pack_item_of(placement: int) -> int:
	var i: int = 0
	while i < _pack.size():
		if _pack[i] == placement:
			return _pack[i + 1]
		i += 5
	return -1


## Footprint of the pack entry at flat index `i`, respecting its rotation.
func _pack_w(i: int) -> int:
	var s: Vector2i = CAT.size_of(bridge, _pack[i + 1])
	return s.y if _pack[i + 4] != 0 else s.x


func _pack_h(i: int) -> int:
	var s: Vector2i = CAT.size_of(bridge, _pack[i + 1])
	return s.x if _pack[i + 4] != 0 else s.y


## Where a worn slot's box is: on its body part of the doll. Defined once --
## the hit test and the draw used to each compute this, and that is exactly
## where two layouts drift apart.
func slot_box(slot: int) -> Rect2:
	if slot < 0 or slot >= DOLL_AT.size():
		return Rect2()
	return Rect2(doll_origin() + DOLL_AT[slot], Vector2(SLOT_W, SLOT_H))


## The doll's top-left. The only thing about the doll the two views disagree on.
func doll_origin() -> Vector2:
	return Vector2(M_DOLL_X, M_DOLL_Y) if mission_mode else Vector2(SLOT_X, DOLL_Y)


## Where the mission pack is drawn, and how big a cell is. One source for the
## draw and the hit test, which is the same reason slot_box exists.
func pack_origin() -> Vector2:
	return Vector2(M_PACK_X, M_PACK_Y) if mission_mode else Vector2(PACK_X, PACK_Y)


func pack_cell() -> float:
	return M_PACK_CELL if mission_mode else PACK_CELL


## How big the pack grid is. In the field that is the SIM'S pack; at base it is
## the one the worn backpack will build at deploy.
##
## Not the same number between runs: SetBackpack only stages the bag, and the
## sim rebuilds Pack at Restart -- so at base PackWidth/PackHeight still
## describe the LAST run's bag, and changing bags in the stash changed nothing
## on screen until after the next deploy.
func pack_dims() -> Vector2i:
	if mission_mode:
		return Vector2i(bridge.PackWidth, bridge.PackHeight)
	var bag: int = stash.equipped_in(CAT.SLOT_BACKPACK)
	if bag == STASH.NONE:
		return Vector2i.ZERO
	return Vector2i(bridge.GearPackW(bag), bridge.GearPackH(bag))


## The pack panel needs re-laying whenever the carry list or the bag changes,
## and both happen inside a drag. One call, so no path can forget one half.
func refresh_carry() -> void:
	if not mission_mode:
		refresh_pack()


## The floor's box. There is only the field's: at base there is no floor, and
## over_bin refuses there whatever this says.
func bin_rect() -> Rect2:
	return Rect2(M_BIN_X, M_BIN_Y, M_BIN_W, M_BIN_H)


## What is WORN. In the field that is the sim's Loadout; at base it is the
## stash. Reading the wrong one is how a screen ends up showing the rifle you
## left at home while you are holding the one you took off a guard.
func worn_in(slot: int) -> int:
	if not mission_mode:
		return stash.equipped_in(slot)
	if slot < 0 or slot >= _worn_sim.size():
		return STASH.NONE
	return _worn_sim[slot] if _worn_sim[slot] != 0 else STASH.NONE


## What is on the weapon rails, as gear item ids by sub-slot, 0 for an empty
## one. The STASH'S set at base and the SIM'S in the field -- attachments can be
## fitted mid-run, and mid-run the sim is the truth, exactly as it is for the
## worn slots.
## `hand` is 0 for the primary and 1 for the holster; -1 asks for whichever
## the rail block is currently showing.
func fitted_set(hand: int = -1) -> PackedInt32Array:
	var h: int = rail_hand if hand < 0 else hand
	if mission_mode:
		return PackedInt32Array(bridge.GetFittedSim(h))
	var out: PackedInt32Array = PackedInt32Array()
	out.resize(CAT.ATTACH_COUNT)
	for sub in range(CAT.ATTACH_COUNT):
		var it: int = stash.attached_at(sub, h)
		out[sub] = it if it != STASH.NONE else 0
	return out


func fitted_in(sub: int, hand: int = -1) -> int:
	var f: PackedInt32Array = fitted_set(hand)
	if sub < 0 or sub >= f.size():
		return STASH.NONE
	return f[sub] if f[sub] > 0 else STASH.NONE


## The weapon the rail block is about, as a worn slot.
func rail_slot() -> int:
	return CAT.SLOT_SECONDARY if rail_hand == 1 else CAT.SLOT_PRIMARY


## Six empty rails. A weapon that is not on a body has none: it is a gun, not a
## gun-and-its-kit, and lending it the worn weapon's scope for the tooltip
## would be the shared-set bug drawn rather than simulated.
func _no_rails() -> PackedInt32Array:
	var out: PackedInt32Array = PackedInt32Array()
	out.resize(CAT.ATTACH_COUNT)
	return out


## The rails belonging to whatever is under the cursor. Only a WORN weapon has
## any; everything else -- a rifle in the stash, one in the bag, a vest -- gets
## six empty ones.
func rails_under(m: Vector2) -> PackedInt32Array:
	var slot: int = slot_at(m)
	var hand: int = STASH.hand_of_slot(slot) if slot != -1 else -1
	return fitted_set(hand) if hand >= 0 else _no_rails()


## The item under the cursor, or -1. FOUR panels can be under it -- the stash
## grid, a worn slot, a weapon rail, the pack -- and the tooltip must not have
## to care which, so they are resolved in one place and in the same order
## release_at resolves its destinations.
func hovered_item(m: Vector2) -> int:
	var pi: int = grid_placement_at(m)
	if pi != GRID.NONE:
		return stash.grid.item_of(pi)

	var slot: int = slot_at(m)
	if slot != -1:
		return worn_in(slot)

	var sub: int = attach_at(m)
	if sub != -1:
		return fitted_in(sub)

	for chip in fitted_chips():
		if chip[0].has_point(m):
			return chip[1]

	var pp: int = pack_placement_at(m)
	if pp != -1:
		return _pack_item_of(pp)
	return STASH.NONE


## Where a weapon sub-slot's box is. Three columns of ATTACH_ROWS.
func attach_box(sub: int) -> Rect2:
	var col: int = sub / ATTACH_ROWS
	var row: int = sub % ATTACH_ROWS
	return Rect2(SLOT_X + col * (ATTACH_W + ATTACH_COL_GAP),
		ATTACH_Y + row * ATTACH_H, ATTACH_W, ATTACH_H - 2.0)


func cell_at(m: Vector2) -> Vector2i:
	# There is no stash grid in the field, so nothing can land in one.
	if mission_mode:
		return Vector2i(-1, -1)
	var c: int = int(floor((m.x - GRID_X) / CELL))
	var r: int = int(floor((m.y - GRID_Y) / CELL))
	if c < 0 or r < 0 or c >= stash.grid.w or r >= stash.grid.h:
		return Vector2i(-1, -1)
	return Vector2i(c, r)


func grid_placement_at(m: Vector2) -> int:
	var cell: Vector2i = cell_at(m)
	if cell.x < 0:
		return GRID.NONE
	return stash.grid.placement_at(cell.x, cell.y)


func slot_at(m: Vector2) -> int:
	for slot in range(CAT.SLOT_COUNT):
		if slot_box(slot).has_point(m):
			return slot
	return -1


func attach_at(m: Vector2) -> int:
	# Attachments are fixed at deploy, so the field view draws what is FITTED
	# and offers no rail to drop onto. Returning -1 here rather than relying on
	# the caller also keeps these boxes from shadowing the field view's own
	# slots, which sit over the same pixels.
	if mission_mode:
		return -1
	for sub in range(CAT.ATTACH_COUNT):
		if attach_box(sub).has_point(m):
			return sub
	return -1


# ---------------------------------------------------------------------- draw

func _draw() -> void:
	if not active or bridge == null or stash == null:
		return
	draws += 1

	draw_rect(Rect2(0, 0, FIELD_W, SCREEN_H), C_PANEL_BG)

	if mission_mode:
		_draw_mission_view()
		return

	draw_string(_font, Vector2(SLOT_X, 36), "STASH", HORIZONTAL_ALIGNMENT_LEFT,
		-1, 22, C_TEXT)
	if campaign != null:
		draw_string(_font, Vector2(SLOT_X, 36), "%d on hand" % campaign.money,
			HORIZONTAL_ALIGNMENT_RIGHT, DIVIDER - SLOT_X - 16, 15, C_GOOD)

	# The three columns, and the rules between them: a faint one inside the
	# inventory, a full one where the inventory ends and the missions begin.
	draw_line(Vector2(COL2_X - 8, 50), Vector2(COL2_X - 8, SCREEN_H - 48),
		Color(C_LINE, 0.5), 1.0)
	draw_line(Vector2(DIVIDER, 24), Vector2(DIVIDER, SCREEN_H - 40), C_LINE, 1.0)

	draw_string(_font, Vector2(SLOT_X, DOLL_Y - 6), "WORN",
		HORIZONTAL_ALIGNMENT_LEFT, -1, 10, C_DIM)
	_draw_doll()
	_draw_slots()
	_draw_attachments()
	_draw_grid()
	draw_string(_font, Vector2(GRID_X, GRID_Y - 6), "STASH GRID",
		HORIZONTAL_ALIGNMENT_LEFT, 120, 10, C_DIM)
	draw_string(_font, Vector2(GRID_X, GRID_Y - 6),
		"%d of %d free" % [stash.grid.free_cells(), stash.grid.w * stash.grid.h],
		HORIZONTAL_ALIGNMENT_RIGHT, stash.grid.w * CELL, 10, C_DIM)
	_draw_pack()
	_draw_bin()
	_draw_missions()
	_draw_carried()
	_draw_footer()
	_draw_tooltip()


## The FIELD view: what you are carrying and wearing, and nothing about base.
func _draw_mission_view() -> void:
	draw_string(_font, Vector2(M_DOLL_X, 44), "CARRYING",
		HORIZONTAL_ALIGNMENT_LEFT, -1, 22, C_TEXT)
	draw_string(_font, Vector2(M_DOLL_X, 66),
		"drag from the pack onto a slot to put it on  ·  you are standing still",
		HORIZONTAL_ALIGNMENT_LEFT, -1, 11, C_DIM)

	_draw_doll()
	_draw_slots()
	_draw_pack()
	_draw_bin()
	_draw_fitted()
	_draw_carried()
	_draw_footer()
	_draw_tooltip()


## What is bolted to the weapon, read-only. Attachments are fixed at deploy --
## one set applies to whichever weapon is held, so fitting one mid-run is not
## the local change it looks like -- but knowing what is on the gun is exactly
## the sort of thing worth checking mid-mission.
## The fitted strip, as [rect, item id] pairs. Laid out ONCE, for the draw and
## the hit test both -- the same reason slot_box exists, and the chips are
## worse than the slots for it because their widths come from the text.
func fitted_chips() -> Array:
	if not mission_mode or _font == null:
		return []
	var out: Array = []
	var x: float = M_PACK_X
	var y: float = M_ATTACH_Y + 8
	for sub in range(CAT.ATTACH_COUNT):
		var it: int = fitted_in(sub)
		if it == STASH.NONE:
			continue
		var w: float = _font.get_string_size(bridge.GearName(it),
			HORIZONTAL_ALIGNMENT_LEFT, -1, 11).x + 14.0
		# Six chips of long names run off the right edge; wrap to a second
		# row rather than draw a scope nobody can see or hover.
		if x > M_PACK_X and x + w > FIELD_W - 16.0:
			x = M_PACK_X
			y += 24.0
		out.append([Rect2(x, y, w, 18), it])
		x += w + 6.0
	return out


func _draw_fitted() -> void:
	draw_string(_font, Vector2(M_PACK_X, M_ATTACH_Y), "FITTED",
		HORIZONTAL_ALIGNMENT_LEFT, -1, 11, C_DIM)
	var chips: Array = fitted_chips()
	for chip in chips:
		var box: Rect2 = chip[0]
		draw_rect(box, C_ITEM)
		_rarity_strip(box, chip[1])
		draw_string(_font, box.position + Vector2(7, 13),
			bridge.GearName(chip[1]), HORIZONTAL_ALIGNMENT_LEFT, -1, 11,
			CAT.rarity_colour(bridge, chip[1]))
	if chips.is_empty():
		draw_string(_font, Vector2(M_PACK_X, M_ATTACH_Y + 21), "nothing fitted",
			HORIZONTAL_ALIGNMENT_LEFT, -1, 11, C_DIM)


const C_BODY := Color(0.10, 0.12, 0.15)
const C_BODY_EDGE := Color(0.19, 0.22, 0.28)
const C_STRAP := Color(0.15, 0.17, 0.21)


## The figure the slots are worn on. Procedural primitives only (spec §0), and
## drawn BEHIND the boxes: it is there to say where each slot is, not to be
## looked at. The bag shows only when one is worn, peeking past the shoulder
## its box sits on.
func _draw_doll() -> void:
	var o: Vector2 = doll_origin()
	var cx: float = o.x + DOLL_CX
	var y: float = o.y

	if worn_in(CAT.SLOT_BACKPACK) != STASH.NONE:
		var bag := Rect2(cx + 34, y + 56, 64, 124)
		draw_rect(bag, C_STRAP)
		draw_rect(bag, C_BODY_EDGE, false, 1.0)
		draw_line(Vector2(bag.position.x + 6, bag.position.y + 62),
			Vector2(bag.end.x - 6, bag.position.y + 62), C_BODY_EDGE, 1.0)

	for side in [-1.0, 1.0]:
		var sh := Vector2(cx + side * 54, y + 76)
		var el := Vector2(cx + side * 88, y + 152)
		var hand := Vector2(cx + side * 106, y + 228)
		draw_line(sh, el, C_BODY, 20.0)
		draw_line(el, hand, C_BODY, 17.0)
		draw_circle(el, 9.5, C_BODY)
		draw_circle(hand, 11.0, C_BODY)
		draw_circle(sh, 15.0, C_BODY)
		draw_colored_polygon(PackedVector2Array([
			Vector2(cx + side * 3, y + 214), Vector2(cx + side * 46, y + 214),
			Vector2(cx + side * 40, y + 350), Vector2(cx + side * 12, y + 350)]),
			C_BODY)
		var foot_x: float = cx + side * 8 if side > 0.0 else cx - 50
		draw_rect(Rect2(foot_x, y + 346, 42, 18), C_BODY)

	var torso := PackedVector2Array([Vector2(cx - 60, y + 62),
		Vector2(cx + 60, y + 62), Vector2(cx + 46, y + 200),
		Vector2(cx - 46, y + 200)])
	draw_colored_polygon(torso, C_BODY)
	draw_rect(Rect2(cx - 48, y + 194, 96, 28), C_BODY)
	draw_rect(Rect2(cx - 9, y + 42, 18, 24), C_BODY)
	draw_circle(Vector2(cx, y + 26), 25.0, C_BODY)

	# One edge round the torso and head, so the figure reads as a figure
	# against the panel rather than as a stain.
	var edge := torso.duplicate()
	edge.append(torso[0])
	draw_polyline(edge, C_BODY_EDGE, 1.0)
	draw_arc(Vector2(cx, y + 26), 25.0, 0.0, TAU, 40, C_BODY_EDGE, 1.0)


func _draw_slots() -> void:
	var hover: int = slot_at(_mouse())
	var held: int = bridge.ActiveWeaponSlot if mission_mode else -1
	for slot in range(CAT.SLOT_COUNT):
		var box: Rect2 = slot_box(slot)
		if box.size.x <= 0.0:
			continue
		var worn: int = worn_in(slot)
		# In the field the question is not "does this fit the slot" but "will
		# the SIM take it here", which is a narrower thing and is the sim's own
		# rule read back rather than a second opinion.
		var takes: bool = _drag != 0 and (bridge.CanEquipMidRun(_drag_item, slot)
			if mission_mode else bridge.GearFitsSlot(_drag_item, slot))
		var is_held: bool = (slot == CAT.SLOT_PRIMARY and held == 0) \
			or (slot == CAT.SLOT_SECONDARY and held == 1)
		# At base, the gun whose rails the block below is showing.
		var is_rails: bool = not mission_mode and slot == rail_slot()

		# Translucent, so the body shows through at the edges and each box
		# reads as worn ON something rather than floating beside it.
		draw_rect(box, Color(C_PANEL, 0.94) if worn != STASH.NONE
			else Color(C_EMPTY, 0.86))
		var edge: Color = C_LINE
		var width: float = 1.0
		if _drag != 0:
			if takes:
				edge = C_GOOD
				width = 2.0
		elif hover == slot:
			edge = C_SEL
			width = 2.0
		elif is_held:
			edge = C_GOOD
		elif is_rails:
			edge = Color(C_SEL, 0.55)
		draw_rect(box, edge, false, width)

		draw_string(_font, box.position + Vector2(6, 11),
			DOLL_LABELS[slot] if slot < DOLL_LABELS.size()
				else bridge.GearSlotName(slot).to_upper(),
			HORIZONTAL_ALIGNMENT_LEFT, box.size.x - 12, 9, C_DIM)
		# One tag, top right: which gun is in your hands in the field, and
		# which slots the sim cannot read. A helmet that silently did nothing
		# would be the screen implying something it is not.
		var tag: String = ""
		var tag_col: Color = C_DIM
		if is_held:
			tag = "in hand"
			tag_col = C_GOOD
		elif not _slot_reaches_sim(slot):
			tag = "cosmetic"
		if tag != "":
			draw_string(_font, box.position + Vector2(6, 11), tag,
				HORIZONTAL_ALIGNMENT_RIGHT, box.size.x - 12, 8, tag_col)
		# Baseline from the BOX, not a constant: draw_string positions the
		# baseline, so a fixed offset drops the name out of a shorter box.
		draw_string(_font, box.position + Vector2(6, box.size.y - 7.0),
			bridge.GearName(worn) if worn != STASH.NONE else "empty",
			HORIZONTAL_ALIGNMENT_LEFT, box.size.x - 12, 11,
			CAT.rarity_colour(bridge, worn) if worn != STASH.NONE else C_DIM)


## Only four of the eight slots have a reader in sim/. The rest are carried and
## saved and change nothing, and the screen should not imply otherwise.
func _slot_reaches_sim(slot: int) -> bool:
	return slot == CAT.SLOT_PRIMARY or slot == CAT.SLOT_SECONDARY \
		or slot == CAT.SLOT_VEST or slot == CAT.SLOT_BACKPACK


## The rails of ONE weapon, named. There are two sets and room for six boxes,
## so the block shows one gun at a time; clicking a weapon slot switches it.
func _draw_attachments() -> void:
	var gun: int = stash.equipped_in(rail_slot())
	var which: String = bridge.GearSlotName(rail_slot()).to_upper()
	var block_w: float = ATTACH_W * 3.0 + ATTACH_COL_GAP * 2.0
	draw_string(_font, Vector2(SLOT_X, ATTACH_Y - 8),
		"%s RAILS" % which, HORIZONTAL_ALIGNMENT_LEFT, block_w, 10, C_SEL)
	draw_string(_font, Vector2(SLOT_X, ATTACH_Y - 8),
		(bridge.GearName(gun) if gun != STASH.NONE else "no weapon")
		+ "  \u00b7  click a gun to switch",
		HORIZONTAL_ALIGNMENT_RIGHT, block_w, 10, C_DIM)

	var weapon_sim_id: int = bridge.GearSimA(gun) if gun != STASH.NONE else -1

	for sub in range(CAT.ATTACH_COUNT):
		var box: Rect2 = attach_box(sub)
		var mounted: int = stash.attached_at(sub, rail_hand)
		# Greyed rather than hidden when the weapon has no such slot, matching
		# how the loadout menu already presents this.
		var supported: bool = weapon_sim_id >= 0 \
			and bridge.WeaponHasSlot(weapon_sim_id, sub)
		draw_rect(box, C_PANEL if mounted != STASH.NONE else C_EMPTY)
		var takes: bool = _drag == 1 and CAT.attach_slot_of(bridge, _drag_item) == sub
		draw_rect(box, C_GOOD if takes else C_LINE, false, 2.0 if takes else 1.0)

		draw_string(_font, box.position + Vector2(6, 10),
			bridge.SlotName(sub).to_upper() + ("" if supported else "  \u00b7  no rail"),
			HORIZONTAL_ALIGNMENT_LEFT, box.size.x - 12, 8, C_DIM)
		var text: String = bridge.GearName(mounted) if mounted != STASH.NONE else "-"
		if mounted != STASH.NONE and not supported:
			text = "! " + text        # no room for the sentence; the mark says it
		draw_string(_font, box.position + Vector2(6, 23), text,
			HORIZONTAL_ALIGNMENT_LEFT, box.size.x - 12, 10,
			C_TEXT if supported else C_DIM)


func _draw_grid() -> void:
	for r in range(stash.grid.h):
		for c in range(stash.grid.w):
			var cell := Rect2(GRID_X + c * CELL, GRID_Y + r * CELL, CELL - 2.0, CELL - 2.0)
			draw_rect(cell, C_EMPTY)
			draw_rect(cell, C_LINE, false, 1.0)

	for pi in stash.grid.live_ids():
		# The item being carried is drawn at the cursor, not in its old cell.
		if _drag == 1 and pi == _drag_pi:
			continue
		var at: Vector2i = stash.grid.pos_of(pi)
		var span: Vector2i = stash.grid.span_of(pi)
		var box := Rect2(GRID_X + at.x * CELL, GRID_Y + at.y * CELL,
			span.x * CELL - 2.0, span.y * CELL - 2.0)
		var item: int = stash.grid.item_of(pi)
		draw_rect(box, C_ITEM)
		draw_rect(box, C_LINE, false, 1.0)
		_rarity_strip(box, item)
		var size: int = CAT.label_size(span)
		draw_string(_font, box.position + Vector2(5, 4 + size),
			bridge.GearName(item),
			HORIZONTAL_ALIGNMENT_LEFT, box.size.x - 6, size, CAT.rarity_colour(bridge, item))


## A rarity band along the top of an item's box. The name carries the colour
## too, but a 1x1 attachment's name is clipped to a few letters and the band is
## what still says "that one is orange" from across the grid.
func _rarity_strip(box: Rect2, item_id: int) -> void:
	draw_rect(Rect2(box.position.x + 1.0, box.position.y + 1.0, box.size.x - 2.0, 2.0),
		CAT.rarity_colour(bridge, item_id))


## The carried item follows the cursor, and its outline says whether it would
## land: green where it fits, red where it does not.
## What is in the mission pack right now. Read-only except for dropping: see
## PACK_CELL's note on why a drag in here cannot be recorded.
func _draw_pack() -> void:
	var dims: Vector2i = pack_dims()
	var cols: int = dims.x
	var rows: int = dims.y
	var o: Vector2 = pack_origin()
	var cw: float = pack_cell()
	# The status goes on its OWN line under the title. Right-aligned across
	# the grid it ran back over the title on the satchel, which is 4 cells wide.
	var label_w: float = maxf(cols * cw, 240.0)

	draw_string(_font, o + Vector2(0, -24), "MISSION PACK",
		HORIZONTAL_ALIGNMENT_LEFT, -1, 13, C_TEXT)
	# Which bag, straight after the title: right-aligned it floated off a
	# narrow grid, and it is the bag that decides how big this panel is.
	var bag: int = worn_in(CAT.SLOT_BACKPACK)
	if bag != STASH.NONE:
		var title_w: float = _font.get_string_size("MISSION PACK",
			HORIZONTAL_ALIGNMENT_LEFT, -1, 13).x
		draw_string(_font, o + Vector2(title_w + 8.0, -24),
			"\u00b7  " + bridge.GearName(bag), HORIZONTAL_ALIGNMENT_LEFT,
			label_w - title_w - 8.0, 10, C_DIM)
	if not mission_mode:
		# What you are PACKING, and how much room is left for what you find.
		if cols > 0 and rows > 0:
			var packed: int = stash.carried.size()
			draw_string(_font, o + Vector2(0, -8),
				("%d packed  \u00b7  drag gear in" % packed) if packed > 0
					else "empty  \u00b7  drag gear in to take it with you",
				HORIZONTAL_ALIGNMENT_LEFT, label_w, 10,
				C_SEL if packed > 0 else C_DIM)
	elif cols > 0 and rows > 0:
		# A PERCENTAGE, not only a cell count: "38 of 40 free" needs arithmetic
		# before it means anything, and the question being asked over a body is
		# "have I room for this", which a percentage answers directly.
		var full: int = bridge.PackFullPercent
		draw_string(_font, o + Vector2(0, -8),
			"%d%% full  ·  %d of %d free" % [full, bridge.PackFreeCells, cols * rows],
			HORIZONTAL_ALIGNMENT_LEFT, label_w, 10,
			C_BAD if full >= 90 else (C_SEL if full >= 70 else C_DIM))

	if cols <= 0 or rows <= 0:
		draw_string(_font, o + Vector2(0, 14),
			"no backpack worn \u2014 nothing can be carried",
			HORIZONTAL_ALIGNMENT_LEFT, 240, 11, C_BAD)
		return

	for r in range(rows):
		for c in range(cols):
			var cell := Rect2(o.x + c * cw, o.y + r * cw, cw - 1.0, cw - 1.0)
			draw_rect(cell, C_EMPTY)
			draw_rect(cell, C_LINE, false, 1.0)

	var hover: int = pack_placement_at(_mouse())
	var i: int = 0
	while i < _pack.size():
		var placement: int = _pack[i]
		var box := Rect2(o.x + _pack[i + 2] * cw, o.y + _pack[i + 3] * cw,
			_pack_w(i) * cw - 1.0, _pack_h(i) * cw - 1.0)
		var lifted: bool = _drag == 4 and _drag_pi == placement
		draw_rect(box, C_EMPTY if lifted else C_ITEM)
		draw_rect(box, C_SEL if (hover == placement and _drag == 0) else C_LINE,
			false, 1.0)
		if not lifted:
			_rarity_strip(box, _pack[i + 1])
			draw_string(_font, box.position + Vector2(4, 13),
				bridge.GearName(_pack[i + 1]), HORIZONTAL_ALIGNMENT_LEFT,
				box.size.x - 6, 9 if not mission_mode else 11,
				CAT.rarity_colour(bridge, _pack[i + 1]))
		i += 5


## The floor. Drag something out of the pack onto this to put it down.
func _draw_bin() -> void:
	# See over_bin: there is no floor at base, so there is nothing to draw.
	if not mission_mode:
		return
	var box: Rect2 = bin_rect()
	var armed: bool = _drag == 4
	var over: bool = armed and over_bin(_mouse())

	draw_rect(box, C_EMPTY)
	draw_rect(box, C_BAD if over else (C_GOOD if armed else C_LINE), false,
		2.0 if over else 1.0)

	draw_string(_font, box.position + Vector2(0, 30), "DROP ON THE FLOOR",
		HORIZONTAL_ALIGNMENT_CENTER, box.size.x, 13,
		C_TEXT if armed else C_DIM)
	draw_string(_font, box.position + Vector2(8, 52),
		"release to drop it" if armed else "drag here from the pack",
		HORIZONTAL_ALIGNMENT_CENTER, box.size.x - 16, 11, C_DIM)
	draw_string(_font, box.position + Vector2(8, 70),
		"lands at your feet \u2014 G to pick it up",
		HORIZONTAL_ALIGNMENT_CENTER, box.size.x - 16, 10, C_DIM)


## The mission half: one row per level, and the stakes of the highlighted one.
##
## Threat is PIPS, not a score -- a number invites arithmetic, five pips invite
## a decision. The multiplier is shown rather than an expected payout, because
## what a run actually pays depends mostly on what you carry out, which this
## screen cannot know.
func _draw_missions() -> void:
	draw_string(_font, Vector2(MISSION_X, 36), "MISSIONS",
		HORIZONTAL_ALIGNMENT_LEFT, -1, 22, C_TEXT)

	if _levels.is_empty():
		draw_string(_font, Vector2(MISSION_X, MISSION_ROWS_Y),
			"no level files found", HORIZONTAL_ALIGNMENT_LEFT, MISSION_W, 13, C_DIM)
		return

	draw_string(_font, Vector2(MISSION_X, 36), "%d / %d" % [_level + 1, _levels.size()],
		HORIZONTAL_ALIGNMENT_RIGHT, MISSION_W, 12, C_DIM)

	for i in range(_levels.size()):
		_draw_mission_row(i)

	_draw_stakes(MISSION_ROWS_Y + _levels.size() * MISSION_ROW_H + 16.0)


func _draw_mission_row(i: int) -> void:
	var box := Rect2(MISSION_X, MISSION_ROWS_Y + i * MISSION_ROW_H,
		MISSION_W, MISSION_ROW_H - 8.0)
	var on: bool = i == _level
	draw_rect(box, C_PANEL if on else C_EMPTY)
	draw_rect(box, C_SEL if on else C_LINE, false, 2.0 if on else 1.0)

	var path: String = _levels[i]
	var text: String = LEVELS.read(path)
	var sm: PackedInt32Array = PackedInt32Array(bridge.LevelSummary(text))
	var name: String = bridge.LevelTitle(text)

	# Three lines, since the column narrowed to make room for the doll: the
	# name and its threat, what the floor is, what it pays.
	draw_string(_font, box.position + Vector2(12, 19), name,
		HORIZONTAL_ALIGNMENT_LEFT, MISSION_W - 100, 15, C_SEL if on else C_TEXT)
	# Glass and doors change how a floor plays more than its size does, so a
	# floor that has them says so. Fields 7 and 8 were appended to the summary;
	# an older bridge returns seven and the line simply omits them.
	var features: String = ""
	if sm.size() >= 9:
		if sm[8] > 0:
			features += "   %d doors" % sm[8]
		if sm[7] > 0:
			features += "   %d windows" % sm[7]
	draw_string(_font, box.position + Vector2(12, 34),
		"%dx%d   %d guards   %d chests%s" % [sm[0], sm[1], sm[2], sm[5], features],
		HORIZONTAL_ALIGNMENT_LEFT, MISSION_W - 24, 10, C_DIM)

	# Threat pips, top right, and what it pays on the last line.
	var pips: int = MISSIONS.pips_of(sm)
	for p in range(MISSIONS.PIPS):
		var pip := Rect2(box.end.x - 12 - MISSIONS.PIPS * 13 + 4 + p * 13,
			box.position.y + 10, 9, 9)
		draw_rect(pip, (C_BAD if pips >= 4 else C_SEL) if p < pips
			else Color(0.16, 0.18, 0.22))
	# A dark floor says so (lighting plan §9.2): easier to sneak, harder to
	# fight, so it is named rather than priced -- threat does not know light.
	var dark: String = ""
	if sm.size() >= 13 and sm[11] >= 0 and sm[11] < 100:
		dark = "   ·   dark, %d%% light" % sm[11]
	draw_string(_font, box.position + Vector2(12, 49),
		"threat %s%s" % [MISSIONS.multiplier_text(sm), dark],
		HORIZONTAL_ALIGNMENT_LEFT, MISSION_W - 24, 10, C_DIM)
	draw_string(_font, box.position + Vector2(12, 49),
		"pays %d" % MISSIONS.mission_payout(sm),
		HORIZONTAL_ALIGNMENT_RIGHT, MISSION_W - 24, 12, C_GOOD)

	# What is on the floor to take: the chests' budget, and what the guards'
	# kits and weapons are worth between them. Both are the LEVEL's numbers --
	# luck decides what the chest money buys on the day, not how much it is.
	if sm.size() >= 11:
		draw_string(_font, box.position + Vector2(12, 63),
			"loot  %s in chests   ·   %s on guards" % [CAT.money(sm[9]), CAT.money(sm[10])],
			HORIZONTAL_ALIGNMENT_LEFT, MISSION_W - 24, 10, C_LOOT)


func _draw_stakes(y: float) -> void:
	if _summary.size() < 7:
		return

	# The objective is the whole condition for being paid, so it is stated here
	# rather than discovered on the way out.
	var obj: int = _summary[6]
	draw_string(_font, Vector2(MISSION_X, y + 12),
		("carry out %d objective(s) to be paid" % obj) if obj > 0
			else "no objective — this floor pays nothing",
		HORIZONTAL_ALIGNMENT_LEFT, MISSION_W, 12, C_TEXT if obj > 0 else C_BAD)

	if campaign != null:
		var rec: Dictionary = campaign.mission_record(selected_level().get_file())
		var runs: int = int(rec["runs"])
		draw_string(_font, Vector2(MISSION_X, y + 32),
			("%d run(s), %d completed  ·  best %d" % [runs, rec["completions"],
				rec["best"]]) if runs > 0 else "never attempted",
			HORIZONTAL_ALIGNMENT_LEFT, MISSION_W, 11, C_DIM)


func _draw_carried() -> void:
	if _drag == 0:
		return
	var base: Vector2i = CAT.size_of(bridge, _drag_item)
	var span: Vector2i = GRID.span(base.x, base.y, _drag_rot)
	var m: Vector2 = _mouse()
	var cell: Vector2i = cell_at(m)

	# Carried at the size of wherever it came from, so an item lifted out of the
	# field view's big pack does not shrink to base-grid scale under the cursor.
	var cw: float = pack_cell() if mission_mode else CELL
	var pos: Vector2 = m - Vector2(cw * 0.5, cw * 0.5)
	var ok: bool = false
	if cell.x >= 0:
		pos = Vector2(GRID_X + cell.x * CELL, GRID_Y + cell.y * CELL)
		var ignore: int = _drag_pi if _drag == 1 else GRID.NONE
		ok = stash.grid.can_place(base.x, base.y, cell.x, cell.y, _drag_rot, ignore)
	else:
		var slot: int = slot_at(m)
		# The SAME rule the slot highlight uses. In the field that is the sim's
		# CanEquipMidRun, not "does it fit the slot" -- a backpack fits the
		# backpack slot and is still refused out here, and showing it green
		# would be the screen promising what the tick will ignore.
		ok = slot != -1 and (bridge.CanEquipMidRun(_drag_item, slot)
			if mission_mode else bridge.GearFitsSlot(_drag_item, slot))

	var box := Rect2(pos, Vector2(span.x * cw - 2.0, span.y * cw - 2.0))
	draw_rect(box, C_ITEM)
	draw_rect(box, C_GOOD if ok else C_BAD, false, 2.0)
	var size: int = CAT.label_size(span)
	draw_string(_font, box.position + Vector2(5, 4 + size), bridge.GearName(_drag_item),
		HORIZONTAL_ALIGNMENT_LEFT, box.size.x - 6, size, CAT.rarity_colour(bridge, _drag_item))


func _draw_footer() -> void:
	if mission_mode:
		draw_string(_font, Vector2(M_DOLL_X, SCREEN_H - 18),
			"drag onto a slot to equip  ·  onto the floor to drop"
			+ "  ·  E or ESC closes  ·  the world is still running",
			HORIZONTAL_ALIGNMENT_LEFT, FIELD_W - M_DOLL_X * 2.0, 11, C_DIM)
		if _notice_t > 0.0:
			draw_string(_font, Vector2(M_DOLL_X, SCREEN_H - 38), _notice,
				HORIZONTAL_ALIGNMENT_LEFT, FIELD_W - M_DOLL_X * 2.0, 12, C_BAD)
		return

	draw_string(_font, Vector2(SLOT_X, SCREEN_H - 18),
		"drag gear onto the body to wear it  ·  R turns it  ·  into the BAG to take it  ·  B buys",
		HORIZONTAL_ALIGNMENT_LEFT, DIVIDER - SLOT_X - 16, 11, C_DIM)
	draw_string(_font, Vector2(MISSION_X, SCREEN_H - 18),
		"up/down pick  ·  ENTER deploys  ·  ESC title",
		HORIZONTAL_ALIGNMENT_LEFT, MISSION_W, 11, C_DIM)
	if _notice_t > 0.0:
		draw_string(_font, Vector2(SLOT_X, SCREEN_H - 36), _notice,
			HORIZONTAL_ALIGNMENT_LEFT, DIVIDER - SLOT_X - 16, 12, C_BAD)


# -------------------------------------------------------------- the tooltip
#
# What the thing under the cursor actually DOES. The item table has thirty-odd
# entries whose names say almost nothing on their own -- "holographic",
# "angled grip", "subsonic" -- and until now the only way to find out what one
# did was to fit it, deploy, and see whether the fight went differently.
#
# The rows come from item_tooltip.gd, which is pure and tested. This half is
# only geometry and colour.

const TIP_PAD: float = 9.0
const TIP_GAP: float = 16.0
const TIP_MIN_W: float = 190.0
const TIP_MAX_W: float = 320.0

const C_TIP_BG := Color(0.055, 0.065, 0.082, 0.97)
const C_TIP_EDGE := Color(0.34, 0.39, 0.47, 0.95)


## Text size and line height for a row kind. ONE table, read by the measure and
## the draw, because a tooltip whose box is measured by one set of numbers and
## painted by another is a tooltip with its last line hanging out of it.
static func tip_metrics(kind: String) -> Vector2:
	match kind:
		"title":
			return Vector2(16, 22)
		"sub":
			return Vector2(10, 16)
		"head":
			return Vector2(9, 20)
		"note":
			return Vector2(10, 15)
		"price":
			return Vector2(10, 18)
		_:
			return Vector2(11, 16)


## How big the panel has to be to hold `rows`.
func tip_size(rows: Array) -> Vector2:
	if rows.is_empty() or _font == null:
		return Vector2.ZERO
	var w: float = TIP_MIN_W
	var h: float = TIP_PAD * 2.0
	for row in rows:
		var m: Vector2 = tip_metrics(row[0])
		h += m.y
		var need: float = _font.get_string_size(row[1],
			HORIZONTAL_ALIGNMENT_LEFT, -1, int(m.x)).x
		if row[2] != "":
			need += 14.0 + _font.get_string_size(row[2],
				HORIZONTAL_ALIGNMENT_LEFT, -1, int(m.x)).x
		w = maxf(w, need + TIP_PAD * 2.0)
	return Vector2(minf(w, TIP_MAX_W), h)


## Where the panel goes for a cursor at `m`. Beside the cursor, never under it,
## and always wholly on screen: a tooltip clipped by the edge is worse than no
## tooltip, because the figure that falls off is the one being reached for.
static func tip_origin(m: Vector2, size: Vector2) -> Vector2:
	var x: float = m.x + TIP_GAP
	if x + size.x > FIELD_W - 8.0:
		x = m.x - TIP_GAP - size.x
	var y: float = m.y + TIP_GAP
	if y + size.y > SCREEN_H - 8.0:
		y = m.y - TIP_GAP - size.y
	return Vector2(clampf(x, 8.0, maxf(8.0, FIELD_W - 8.0 - size.x)),
		clampf(y, 8.0, maxf(8.0, SCREEN_H - 8.0 - size.y)))


## The weapon an attachment tooltip describes itself against: the one in your
## HANDS in the field, the primary at base. An attachment's whole meaning is
## the weapon it goes on, and the one the player cares about is their own.
func tip_weapon() -> int:
	var slot: int = rail_slot()
	var worn: int = worn_in(slot)
	if worn == STASH.NONE:
		worn = worn_in(CAT.SLOT_PRIMARY)
	return bridge.GearSimA(worn) if worn != STASH.NONE else -1


func _draw_tooltip() -> void:
	# Never while carrying something. The panel would cover the grid being
	# aimed at, and the carried item already says what it is.
	if _drag != 0:
		return

	var m: Vector2 = _mouse()
	var item: int = hovered_item(m)
	if item == STASH.NONE or item <= 0:
		return

	var rows: Array = TIP.rows_for(bridge, rails_under(m), item, tip_weapon())
	var size: Vector2 = tip_size(rows)
	if size == Vector2.ZERO:
		return

	var o: Vector2 = tip_origin(m, size)
	draw_rect(Rect2(o, size), C_TIP_BG)
	draw_rect(Rect2(o, size), C_TIP_EDGE, false, 1.0)

	var y: float = o.y + TIP_PAD
	for row in rows:
		var met: Vector2 = tip_metrics(row[0])
		var size_px: int = int(met.x)
		var base: float = y + met.y - 5.0
		var left: Color = C_DIM
		var right: Color = C_TEXT
		match row[0]:
			"title":
				left = CAT.rarity_colour(bridge, item)
				right = left
			"sub":
				left = C_DIM
			"head":
				left = C_SEL
				right = C_DIM
				# A hairline above a heading, so the sections read as sections
				# rather than as one column of twenty things.
				draw_line(Vector2(o.x + TIP_PAD, y + 3.0),
					Vector2(o.x + size.x - TIP_PAD, y + 3.0), C_LINE, 1.0)
			"up":
				right = C_GOOD
			"down":
				right = C_BAD
			"railoff":
				right = C_DIM
			"price":
				left = C_SEL if row[1].begins_with("worth") else C_DIM
			"note":
				left = C_DIM

		draw_string(_font, Vector2(o.x + TIP_PAD, base), row[1],
			HORIZONTAL_ALIGNMENT_LEFT, size.x - TIP_PAD * 2.0, size_px, left)
		if row[2] != "":
			draw_string(_font, Vector2(o.x + TIP_PAD, base), row[2],
				HORIZONTAL_ALIGNMENT_RIGHT, size.x - TIP_PAD * 2.0, size_px, right)
		y += met.y
