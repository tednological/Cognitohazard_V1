extends RefCounted

## How dangerous a mission is, and what it pays.
##
## DERIVED from the level, not authored in it (campaign plan §2.1). A level file
## says what is on the floor; this reads that and decides what the floor is
## worth. Nothing new to author, nothing to keep in sync, and a level built in
## the editor five minutes ago is rated correctly the moment it is saved.
##
## What that cannot know is SHAPE — eight guards in a corridor is not eight
## guards in an atrium. When a level turns out misrated, the fix is an optional
## `difficulty:` header line that wins over this; the plan deliberately leaves
## room for it rather than building it before there is evidence it is needed.
##
## Static and pure throughout, like levels.gd. It touches no sim state, and the
## sim has never heard of a payout.

## Cells of floor per point of threat. A bigger level means more ground crossed
## in the open, which is the thing that actually gets you seen.
##
## Weighted heavily -- 150, not the 400 this started at -- because the level
## format caps guards at EIGHT ('a' to 'h'), so every level worth playing has
## the same eight and the guard term cannot differentiate them. At 400 the three
## shipped levels rated 73 / 79 / 90 and all drew the same pips, which told the
## player nothing. Size is the difficulty lever the format actually offers.
const CELLS_PER_POINT: float = 150.0

## Per guard. The dominant term, because a guard is the only thing on a level
## that hunts you.
const THREAT_PER_GUARD: float = 10.0

## Supply makes a floor survivable, so it is worth threat back — capped, or a
## chest-heavy level would rate as safe as an empty one.
const THREAT_PER_CHEST: float = 2.0
const CHEST_RELIEF_CAP: float = 20.0

## The scale the pips are drawn against.
##
## Raised from 200 when guard counts went up two and a half times: the shipped
## levels then rated 198 to 260 and every one of them pinned at five pips, which
## is the same failure as rating them all at two. A scale has to have the levels
## that exist somewhere in the middle of it.
const THREAT_MAX: float = 300.0
const PIPS: int = 5

## What a completed mission pays before difficulty. Multiplied by
## payout_multiplier(), and paid ONLY when the objective comes out with you.
const MISSION_BASE: int = 400


## Threat for a level summary as SimBridge.LevelSummary returns it:
## [cols, rows, guards, caches, walls, chests, objectives].
static func threat_of(summary: PackedInt32Array) -> int:
	if summary.size() < 7:
		return 0
	var guards: float = float(summary[2])
	var area: float = float(summary[0]) * float(summary[1])
	var chests: float = float(summary[5])

	var t: float = guards * THREAT_PER_GUARD \
		+ floorf(area / CELLS_PER_POINT) \
		- minf(chests * THREAT_PER_CHEST, CHEST_RELIEF_CAP)
	return int(maxf(0.0, t))


## Filled pips, 0..PIPS. What the player actually reads — a 0-100 score invites
## arithmetic, five pips invite a decision.
static func pips_of(summary: PackedInt32Array) -> int:
	var frac: float = clampf(float(threat_of(summary)) / THREAT_MAX, 0.0, 1.0)
	return clampi(int(ceilf(frac * float(PIPS))), 1 if threat_of(summary) > 0 else 0, PIPS)


## Payout multiplier, as a Q8 fixed point so the money arithmetic stays integer
## and a debrief cannot disagree with the ledger by a rounding step.
static func multiplier_q8(summary: PackedInt32Array) -> int:
	return 256 + int(float(threat_of(summary)) * 256.0 / 100.0)


static func multiplier_text(summary: PackedInt32Array) -> String:
	return "%0.1fx" % (float(multiplier_q8(summary)) / 256.0)


## What completing this mission pays, before records and before salvage.
##
## The multiplier is applied HERE and nowhere else. It must never reach
## salvage: an item's fence value is already its own worth, and scaling that by
## mission difficulty pays twice for one thing.
static func mission_payout(summary: PackedInt32Array) -> int:
	return (MISSION_BASE * multiplier_q8(summary)) >> 8


## The last mission. Completing it -- extracting WITH the objective -- wins the
## game. Keyed by FILE NAME, like the per-mission history, because titles can be
## renamed and file names are what mission select sorts by (hence the `zz_`).
const FINAL_LEVEL: String = "zz_black_site.txt"


## Where the final level ships. The SHIPPED file only: the editor saves to
## user://levels in an exported build, and an edited copy under the same name
## must not win the game.
const FINAL_PATH: String = "res://levels/" + FINAL_LEVEL


## Whether a settled run wins the game. Pure, so the harness can pin it without
## a run: only the shipped final level counts, and only a completed run of it.
static func is_victory(level_path: String, completed: bool) -> bool:
	return completed and level_path == FINAL_PATH
