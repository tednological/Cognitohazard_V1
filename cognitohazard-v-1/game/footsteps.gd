extends RefCounted
## Guard footsteps HEARD through walls, drawn as ripples where each foot lands.
##
## PRESENTATION ONLY. Everything here is derived from `SimBridge.GetGuards()`
## after the tick has run: it never reaches an InputFrame, never feeds the state
## hash, and a replay reproduces it exactly because it is a function of the
## guard positions the replay already reproduces.
##
## The information it gives away is deliberate and bounded:
##   - only guards the player CANNOT see (the bridge's `visible` flag: no clear
##     line through walls and shut doors) -- a guard in view needs no ripple;
##   - only within earshot, which shrinks while the player is moving fast, since
##     your own boots drown out theirs (the movement tiers trade on this axis
##     as well as noise and detection);
##   - only while he is WALKING. A sentry standing at his post makes no sound,
##     so the stealth game's worst surprise -- the man you did not know was
##     there -- survives.
## Steps are counted by DISTANCE walked, not by time, so cadence follows the
## guard's real speed on any clock: a hunting guard's steps come quicker, and
## under dilation they slow with the world.

## Distance between two footfalls. Patrol pace (96 px/s) lands one about every
## 0.4 s, which reads as walking rather than as a pulse.
const STRIDE_PX: float = 38.0
## A move longer than this in one observation is a respawn, restart or replay
## seek, not a walk. Hunting pace at 8x playback is ~21 px per observation.
const TELEPORT_PX: float = 60.0
## Earshot for a relaxed guard's footsteps with the player standing still.
const HEAR_PX: float = 300.0
## Loudness by GuardState ordinal (main.gd ST_*): a man running to a fight is
## louder than one on his rounds. Down and dead make no footsteps.
const POSTURE_LOUD: Array[float] = [1.0, 1.15, 1.5, 1.3, 0.0, 0.0]
## Earshot multiplier by the PLAYER's movement tier, applied only while the
## player is actually moving: stealth, walk, fast, sprint.
const TIER_MASK: Array[float] = [1.0, 0.85, 0.65, 0.45]
## How far each footfall sits off the guard's line of travel, alternating
## sides. The zig-zag is what tells you which way he is walking.
const FOOT_OFFSET_PX: float = 4.0
## Seconds a ripple lasts, and the most alive at once.
const LIFE: float = 1.0
const MAX_RIPPLES: int = 64

## Guard fields this reads out of a GetGuards() stride.
const G_X: int = 0
const G_Y: int = 1
const G_STATE: int = 3
const G_VISIBLE: int = 7

## Positions arrive in 1/256 px fixed point.
const FX: float = 256.0

## Live ripples: {pos: Vector2, t: float (seconds alive), size: float (final
## radius, px), alpha: float (peak opacity, lower for a faint far step)}.
var ripples: Array[Dictionary] = []

var _last: PackedVector2Array = PackedVector2Array()
var _walked: PackedFloat32Array = PackedFloat32Array()
var _foot: PackedInt32Array = PackedInt32Array()
var _player_last: Vector2 = Vector2.ZERO
var _primed: bool = false


## Forget everything: a new run, a new level, a rewound replay.
func reset() -> void:
	ripples.clear()
	_last.clear()
	_walked.clear()
	_foot.clear()
	_primed = false


## How far a guard's footsteps carry. Pure, so the whole rule is assertable.
static func hear_radius(state: int, player_moving: bool, tier: int) -> float:
	if state < 0 or state >= POSTURE_LOUD.size():
		return 0.0
	var r: float = HEAR_PX * POSTURE_LOUD[state]
	if player_moving:
		r *= TIER_MASK[clampi(tier, 0, TIER_MASK.size() - 1)]
	return r


## Read one post-tick guard snapshot and emit a ripple for every footfall the
## player can hear. `stride` is main.gd GUARD_STRIDE; `tier` the player's
## movement tier. Returns how many ripples were emitted.
func observe(guards: PackedInt32Array, stride: int, player: Vector2, tier: int) -> int:
	var n: int = guards.size() / stride
	var player_moving: bool = _primed and player.distance_to(_player_last) > 0.05
	_player_last = player

	# A different guard list is a different level (or the first look at this
	# one): take the positions as the starting point and hear nothing yet.
	if not _primed or n != _last.size():
		_last.resize(n)
		_walked.resize(n)
		_foot.resize(n)
		for i in range(n):
			_last[i] = Vector2(guards[i * stride + G_X], guards[i * stride + G_Y]) / FX
			_walked[i] = 0.0
			_foot[i] = 0
		_primed = true
		return 0

	var emitted: int = 0
	for i in range(n):
		var o: int = i * stride
		var pos := Vector2(guards[o + G_X], guards[o + G_Y]) / FX
		var moved: Vector2 = pos - _last[i]
		_last[i] = pos
		var state: int = guards[o + G_STATE]
		var dist: float = moved.length()
		if dist > TELEPORT_PX or hear_radius(state, false, 0) <= 0.0:
			_walked[i] = 0.0
			continue
		_walked[i] += dist
		if _walked[i] < STRIDE_PX:
			continue
		# One footfall per observation at most; the remainder carries on so the
		# cadence stays true to distance walked.
		_walked[i] = fmod(_walked[i], STRIDE_PX)
		_foot[i] = 1 - _foot[i]

		# The cadence runs whether or not you can hear it, so a guard stepping
		# out of view does not restart on the same foot.
		if guards[o + G_VISIBLE] == 1:
			continue
		var reach: float = hear_radius(state, player_moving, tier)
		var away: float = pos.distance_to(player)
		if away > reach:
			continue

		var side: float = 1.0 if _foot[i] == 1 else -1.0
		var perp := Vector2(-moved.y, moved.x) / maxf(dist, 0.001)
		var near: float = 1.0 - away / reach
		ripples.append({
			"pos": pos + perp * FOOT_OFFSET_PX * side,
			"t": 0.0,
			"size": 10.0 + 10.0 * POSTURE_LOUD[state],
			"alpha": lerpf(0.30, 0.85, near),
		})
		emitted += 1

	while ripples.size() > MAX_RIPPLES:
		ripples.remove_at(0)
	return emitted


## Age every ripple on the render clock.
func step(delta: float) -> void:
	for i in range(ripples.size() - 1, -1, -1):
		ripples[i]["t"] += delta
		if ripples[i]["t"] >= LIFE:
			ripples.remove_at(i)
