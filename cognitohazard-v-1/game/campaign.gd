extends RefCounted

## Money, and what a run is worth.
##
## The meta layer's ledger: the stash holds WHAT you own, this holds what you
## can afford and how the last run went. Like the stash it produces no sim
## state and feeds no hash — the sim has never heard of money and does not need
## to, because nothing you buy changes a rule, only which Loadout you walk in
## with.
##
## The loop this closes: records are scarce (Tune.GuardRecordEvery), so a run's
## CASH comes mostly from evidence, and the gear you carry out is its own
## reward — you keep the item. Those are deliberately separate currencies. An
## early version paid full shop value for salvage AND put the item in the
## stash, which meant one chest bought a rifle outright and the economy was
## over after a single run.

const SAVE_PATH: String = "user://campaign.txt"

## Nothing is charged to enter a mission and nothing is paid merely for coming
## back. You are paid for DOING THE JOB: the mission payout arrives only when
## the objective comes out of the level with you, and it is
## missions.gd:mission_payout() -- scaled by how bad the floor was.

## Per point of record tier carried out. Provable evidence is worth several
## times what a degraded copy is, and a destroyed one is worth nothing at all —
## burning a record for dilation really does spend it.
const PROVABLE_RATE: int = 90
const UNPROVABLE_RATE: int = 25

## What a fence pays for gear you had nowhere to put, as a Q8 fraction of the
## shop price. Well under half, so overflowing the stash is a consolation and
## never a strategy — carrying an item home to KEEP is worth more than carrying
## it home to sell.
const SALVAGE_RATE_Q8: int = 90


## What the fence pays for a stack of items worth `full_value` at shop prices.
static func salvage_value(full_value: int) -> int:
	return (full_value * SALVAGE_RATE_Q8) >> 8

var money: int = 0
var runs: int = 0
var extractions: int = 0

## What the last settled run paid, broken down, so the debrief can show its
## working rather than a single number the player has to trust.
var last_mission: int = 0
var last_records: int = 0
var last_salvage: int = 0
var last_items: int = 0
var last_completed: bool = false


func total_of_last() -> int:
	return last_mission + last_records + last_salvage


## Missions attempted and completed, by level FILE NAME -- titles are not
## unique and can be renamed. {file: {runs, completions, best}}.
var missions: Dictionary = {}


func mission_record(file: String) -> Dictionary:
	return missions.get(file, {"runs": 0, "completions": 0, "best": 0})


## What a run pays. Pure, so the debrief and the ledger cannot disagree about
## the arithmetic.
##
## `completed` is the whole point: walking out WITHOUT the objective pays
## NOTHING IN CASH. Not the mission fee, not the records. You keep every item
## you carried — gear is not money and never was — but a run that did not do
## the job does not get paid for doing it.
static func payout(completed: bool, mission_pay: int, provable: int,
		unprovable: int, sold_value: int) -> int:
	if not completed:
		return 0
	return mission_pay + provable * PROVABLE_RATE \
		+ unprovable * UNPROVABLE_RATE + sold_value


func can_afford(price: int) -> bool:
	return price > 0 and money >= price


func spend(price: int) -> bool:
	if not can_afford(price):
		return false
	money -= price
	return true


func earn(amount: int) -> void:
	money += maxi(0, amount)


## Settle a SUCCESSFUL extraction. Returns the total paid.
##
## `sold_value` is what the fence paid for items the stash had no room for —
## NOT the value of everything carried out. Gear that fits is kept, and kept
## gear pays nothing: the item is the reward.
##
## Dying pays nothing and is not settled at all — see main.gd. That is the whole
## risk: everything in the pack is on the body you left behind.
func settle(file: String, completed: bool, mission_pay: int, provable: int,
		unprovable: int, sold_value: int, items: int) -> int:
	last_completed = completed
	last_mission = mission_pay if completed else 0
	last_records = (provable * PROVABLE_RATE + unprovable * UNPROVABLE_RATE) if completed else 0
	last_salvage = sold_value if completed else 0
	last_items = items

	var total: int = total_of_last()
	earn(total)
	extractions += 1
	runs += 1
	_note_mission(file, completed, total)
	return total


func _note_mission(file: String, completed: bool, paid: int) -> void:
	if file.is_empty():
		return
	var rec: Dictionary = mission_record(file)
	rec["runs"] = int(rec["runs"]) + 1
	if completed:
		rec["completions"] = int(rec["completions"]) + 1
	rec["best"] = maxi(int(rec["best"]), paid)
	missions[file] = rec


## A run that ended in the dirt. Counted, paid nothing.
func settle_loss(file: String = "") -> void:
	last_completed = false
	last_mission = 0
	last_records = 0
	last_salvage = 0
	last_items = 0
	runs += 1
	_note_mission(file, false, 0)


func to_text() -> String:
	var out: String = "campaign 2\nmoney %d\nruns %d\nextractions %d\n" % [
		money, runs, extractions]
	# Sorted, so a save file diffs cleanly between sessions.
	var files: Array = missions.keys()
	files.sort()
	for f in files:
		var r: Dictionary = missions[f]
		out += "mission %s %d %d %d\n" % [f, r["runs"], r["completions"], r["best"]]
	return out


## Total, like every other save parser here: a line it cannot read is skipped,
## never fatal. Returns how many it had to skip.
func from_text(text: String) -> int:
	money = 0
	runs = 0
	extractions = 0
	missions = {}
	if text.is_empty():
		return 0

	var skipped: int = 0
	for raw in text.replace("\r", "").split("\n"):
		var line: String = raw.strip_edges()
		if line.is_empty() or line.begins_with("#") or line.begins_with("campaign "):
			continue
		var f: PackedStringArray = line.split(" ", false)

		# mission <file> <runs> <completions> <best>. A level that no longer
		# exists is kept rather than dropped: deleting a file should not erase
		# the history of having played it.
		if f[0] == "mission":
			if f.size() < 5 or not f[2].is_valid_int() or not f[3].is_valid_int() \
					or not f[4].is_valid_int():
				skipped += 1
				continue
			missions[f[1]] = {
				"runs": maxi(0, f[2].to_int()),
				"completions": maxi(0, f[3].to_int()),
				"best": maxi(0, f[4].to_int()),
			}
			continue

		if f.size() < 2 or not _is_sane_int(f[1]):
			skipped += 1
			continue
		match f[0]:
			"money": money = maxi(0, f[1].to_int())
			"runs": runs = maxi(0, f[1].to_int())
			"extractions": extractions = maxi(0, f[1].to_int())
			_: skipped += 1
	return skipped


## Whether a token is an integer this machine can actually hold.
##
## `is_valid_int()` is NOT enough on its own: it answers "is this all digits",
## so a corrupt save carrying 99999999999999999999 passes it and then makes
## to_int() print "Cannot represent ... as a 64-bit signed integer" once per
## line. A total parser must not shout at the console about a line it is
## perfectly able to skip.
static func _is_sane_int(token: String) -> bool:
	if not token.is_valid_int():
		return false
	# int64 tops out at 19 digits; anything longer cannot be represented, and
	# 18 is a safe bound to test against without parsing it first.
	var digits: String = token.trim_prefix("-").trim_prefix("+")
	return digits.length() <= 18


func save() -> bool:
	var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	if f == null:
		return false
	f.store_string(to_text())
	return true


func load_saved() -> bool:
	if not FileAccess.file_exists(SAVE_PATH):
		return false
	from_text(FileAccess.get_file_as_string(SAVE_PATH))
	return true
