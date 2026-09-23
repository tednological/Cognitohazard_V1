namespace Cognitohazard.Sim;

/// <summary>
/// Every tuning constant from spec §5–§8, in one place, in integer units
/// (spec §3.4). These are PORTED EXACTLY. Per spec §0 and §12.2 they are not
/// ours to adjust: a drifted feel constant produces no test failure and no
/// visible artifact, which makes it the most dangerous silent regression here.
///
/// Units: distances in fixed-point px (1/256), angles in BRAD (65536 = turn),
/// speeds in fixed-point px/s, durations in ticks (60/s), awareness in tenths.
/// </summary>
public static class Tune
{
	// ---------------------------------------------------- §5 time dilation
	public const int WorldSlow = 36;        // 0.18  over ScaleDen 200
	public const int PlayerClock = 124;     // 0.62
	public const int GatedStill = 9;        // 0.045
	public const int DeathScale = 24;       // 0.12
	public const int NormalScale = Fx.ScaleDen;

	public const int RecordStageTicks = 240;   // 4.0 s per stage
	public const int JoltTicks = 18;           // 0.30 s hard snap to real time

	// ----------------------------------------------- aiming and headshots

	/// <summary>
	/// How long the crosshair must stay on ONE target before the next round is
	/// a headshot. 0.5 s at 60 Hz.
	///
	/// DEVIATES FROM THE SPEC, deliberately and on request: the browser build
	/// tuned this to 1.5 s (90 ticks). Changed to 0.5 s, which makes a locked
	/// headshot a far more available answer in a firefight than it was.
	/// </summary>
	public const int AimLockTicks = 30;

	/// <summary>Half-angle the target must stay inside to hold the lock. 0.08 rad
	/// — forgiving enough to track a walking guard, tight enough that it has to
	/// be deliberate.</summary>
	public const int AimLockCone = 834;

	/// <summary>Beyond this the lock will not start or hold.</summary>
	public const int AimLockRange = 600 * Fx.One;

	// ------------------------------------------- health and armour (RPG §2)

	/// <summary>Base health for every actor. Not gear; armour is the gear.
	/// This is the PLAYER's pool; guards have their own, below.</summary>
	public const int BaseHealth = 100;

	/// <summary>
	/// What a guard starts with, and deliberately well under the player's 100.
	///
	/// A guard is not a second player. The interesting question when you round a
	/// corner should be "is he wearing a plate", not "how deep is his health
	/// pool" -- so the pool is shallow and the ARMOUR is where the variance
	/// lives. At 60 an AK drops an unarmoured guard in one round and a Glock in
	/// two, while the same guard in a heavy plate takes three and four. That
	/// spread is the whole point: armour, not hit points, is what separates a
	/// dangerous guard from a soft one, and it is a spread the player can SEE
	/// before committing (main.gd draws the plate on the silhouette).
	/// </summary>
	public const int GuardHealth = 60;

	/// <summary>Damage a guard's rifle does. At 100 health the player dies in 2
	/// shots unarmoured and 5 in heavy plate (RPG plan §3).</summary>
	public const int GuardDamage = 50;

	// ------------------------------------------------------- §7.1 player
	public const int PlayerRadius = 11 * Fx.One;
	public const int SpeedWalk = 50176;        // 196 px/s
	public const int SpeedSneak = 25088;       // 98 px/s
	public const int Magazine = 8;
	public const int FireCooldownTicks = 10;   // 0.17 s @60 -> 10.2, truncated
	public const int ReloadTicks = 84;         // 1.4 s
	public const int MuzzleOffset = 22 * Fx.One;
	public const int DryFireTicks = 13;        // 0.22 s

	/// <summary>
	/// Ruling #4: the prototype computes (rand-0.5)*(0.014 + heat*0.085), so
	/// these are the TOTAL cone width, and a shot draws +/- half of it. The
	/// spec's prose reads as an additive magnitude; its own §7.3 reticle formula
	/// agrees with the prototype.
	/// </summary>
	public const int SpreadBase = 146;         // 0.014 rad, full width
	public const int SpreadPerHeat = 887;      // 0.085 rad at heat == 1
	public const int HeatPerShot = 87;         // 0.34 of HeatMax
	public const int HeatMax = 256;            // heat is Q8 in [0,1]
	public const int HeatDecayPerSec = 384;    // 1.5/s

	// ------------------------------------------------- movement tiers (§7.1a)
	//
	// NOT IN THE PORT SPEC. The prototype had two stances, walk and sneak, on a
	// held key. There are now FOUR on the scroll wheel, and the whole point of
	// them is that they trade against each other on three axes at once: how
	// fast you cross a room, how far the noise carries, and how well you can
	// shoot while doing it.
	//
	// Indexed by InputFrame.TierStealth..TierSprint. Tiers 0 and 1 reproduce
	// the old sneak and walk EXACTLY — same speed, same noise, same detection
	// multiplier — so everything measured against the two-stance game still
	// holds and only the two new tiers are new behaviour.

	/// <summary>Speed as a Q8 fraction of the loadout's walk speed. Stealth is
	/// the exception and takes the loadout's own sneak speed, because armour
	/// tunes that separately and 0.5x would quietly retune three plates.</summary>
	private static readonly int[] TierSpeed = { 128, 256, 353, 471 };

	/// <summary>How far footsteps carry. Silent at stealth, by design: that is
	/// what the tier is FOR.</summary>
	private static readonly int[] TierNoiseRadius =
	{
		0, NoiseWalkRadius, 240 * Fx.One, 320 * Fx.One,
	};

	/// <summary>
	/// Multiplier on how readily a guard picks up a MOVING player, Q8. The
	/// "more stealth the slower you move" axis, and the reason a sprint across
	/// a lit room is a decision rather than a free option: 2.5x against the
	/// walk's 1.35x, and against 0.81x for creeping.
	/// </summary>
	private static readonly int[] TierDetect = { 208, MulMove, 486, 640 };

	/// <summary>
	/// Multiplier on the weapon's aim turn rate, Q8. A sprinting player is
	/// running with the weapon down and cannot whip it round; at a walk or
	/// slower nothing is taken away.
	/// </summary>
	private static readonly int[] TierTurn = { 256, 256, 186, 115 };

	private static int Tier(int[] table, int tier)
		=> table[tier < 0 || tier >= InputFrame.TierCount ? InputFrame.TierWalk : tier];

	public static int TierSpeedQ8(int tier) => Tier(TierSpeed, tier);
	public static int TierNoise(int tier) => Tier(TierNoiseRadius, tier);
	public static int TierDetectQ8(int tier) => Tier(TierDetect, tier);
	public static int TierTurnQ8(int tier) => Tier(TierTurn, tier);

	/// <summary>
	/// How long the weapon takes to come back up after a sprint. 0.45 s.
	///
	/// This is the cost that stops sprint from being strictly better than walk
	/// for closing distance: you arrive, and for a beat you cannot shoot
	/// straight or hold an aim lock. Implemented as a floor under SWAY rather
	/// than as a separate mechanic, so it reads on the reticle the player is
	/// already watching.
	/// </summary>
	public const int SprintRecoverTicks = 27;

	/// <summary>
	/// How much faster a rotary barrel winds DOWN than it winds up, as a Q8
	/// multiplier on the spin timer. Faster than it spools, so letting go of
	/// the trigger really does cost you the commitment — but not instant, so a
	/// short pause between bursts is not a full restart.
	/// </summary>
	public const int SpinDownQ8 = 384;   // 1.5x

	/// <summary>The sway floor while sprinting and for SprintRecoverTicks after.
	/// High enough that a snap shot out of a sprint misses at any range worth
	/// the name.</summary>
	public const int SprintSwayQ8 = 205;   // 0.80 of SwayMax

	// -------------------------------------------------- §7.2 projectiles
	//
	// DEVIATES FROM THE SPEC, deliberately and on request: muzzle velocities are
	// three times the browser build's 840/640 px/s. Round LIFETIMES are cut in
	// the same proportion, so every weapon's REACH is unchanged and only the
	// time of flight moves. Leading a running target is now a small correction
	// rather than most of the skill of the game, and a round crosses the field
	// in about a third of a second.
	//
	// The dilation mechanic survives this: at WorldSlow (0.18x) a 2520 px/s
	// round still travels at 454 px/s on screen, which is plainly in flight.
	// That visibility is the property spec §7.2 actually asks for, not the
	// specific number.
	public const int PlayerBulletSpeed = 645120;   // 2520 px/s (was 840)
	public const int PlayerBulletTicks = 40;       // 0.67 s -> the same ~1680 px
	public const int GuardBulletSpeed = 563200;    // 2200 px/s (was 640)
	public const int GuardBulletTicks = 38;        // 0.63 s -> the same ~1390 px

	/// <summary>Floor on collision samples per round per tick.</summary>
	public const int BulletSubsteps = 3;

	/// <summary>
	/// Ceiling on how far ONE substep may carry a round. Three fixed substeps
	/// were enough at 840 px/s; at 2900 they would sample 16 px apart and put
	/// rounds straight through a one-cell wall. Substeps are now derived from
	/// the distance a round actually covers this tick, so muzzle velocity stays
	/// a tuning knob instead of silently reopening the tunnelling hole.
	/// </summary>
	public const int BulletSubstepFx = 4 * Fx.One;
	public const int BulletSubstepCap = 64;

	public const int HitPad = 2 * Fx.One;

	// ------------------------------------------------ aim sway (both sides)
	//
	// NOT IN THE PORT SPEC. Added on request, as the "firing error on both
	// sides" axis. Sway is one Q8 disturbance value that every shooter carries,
	// raised by swinging the weapon and by moving, and decaying once the
	// shooter settles. It is what gives the per-weapon turn rate teeth: whipping
	// a SAW onto a target and pulling immediately throws the burst wide, and the
	// same arithmetic runs on a guard who has just spun to face you.
	//
	// It is an ENVELOPE, not an accumulator: sway jumps straight to whatever is
	// disturbing the shooter this tick, then decays back down. A plain
	// accumulator against a constant decay is bistable -- it either pegs or
	// sits on zero, with nothing in between.
	public const int SwayMax = 256;

	/// <summary>Turning this far in ONE tick pegs sway. 4096 BRAD == 22.5 deg,
	/// which is roughly a pistol flick, so a flick costs the shot and a tracked
	/// target does not.</summary>
	public const int SwayTurnFullBrad = 4096;

	/// <summary>Q8 of sway per fixed-point unit moved this tick. Walking
	/// (836 fx/tick) settles around 68/256; sneaking halves it.</summary>
	public const int SwayPerMovedQ8 = 21;

	/// <summary>3.0/s: a shooter settles in about a third of a second.</summary>
	public const int SwayDecayPerSec = 768;

	// -------------------------------------------------- guard firing error
	//
	// NOT IN THE PORT SPEC. Guards used to draw a flat +/- 0.045 rad and nothing
	// else, which made them perfect marksmen who happened to miss sometimes.
	// They now run the SAME three-term cone the player does -- a base, a
	// sustained-fire term and a sway term -- so a long firefight degrades their
	// shooting the way it degrades yours.
	public const int GuardSpreadBase = 938;        // 0.09 rad full width
	public const int GuardSpreadPerHeat = 1043;    // +0.10 rad under sustained fire
	public const int GuardSpreadPerSway = 1565;    // +0.15 rad while swinging

	/// <summary>
	/// Guards heat up and cool down far more slowly than the player, because
	/// they fire single aimed rounds 0.8 s apart. A flat port of the player's
	/// 1.5/s decay would clear between every shot and the term would never once
	/// be felt. At these numbers a guard reaches a full cone after about eight
	/// rounds, or six seconds of not letting up.
	/// </summary>
	public const int GuardHeatPerShot = 80;
	public const int GuardHeatDecayPerSec = 96;    // 0.375/s

	// ------------------------------------------------------- choke (§7.1a)
	//
	// NOT IN THE PORT SPEC. Pellets used to take seven independent draws from
	// the cone, which clumps at random: the same blast could miss a man at ten
	// paces and shred him at twenty. They are now laid out evenly across the
	// cone and jittered inside their own slice, which is what a choke actually
	// does. This is the jitter, as a Q8 fraction of one slice.
	public const int ChokeJitterQ8 = 160;          // 0.625 of a slice

	// ----------------------------------------------- weapon handling (§7.1a)
	//
	// NOT IN THE PORT SPEC. Clamps on the per-weapon aim turn rate, so no
	// attachment stack can leave a weapon that will not turn at all -- or one
	// that snaps, which would hand the heaviest gun the pistol's handling.
	public const int TurnAimMin = 24;              // 0.08 of the gap per tick
	public const int TurnAimMax = 285;             // 0.95: never instant

	// -------------------------------------------------------- §7.3 feel
	public const int HitstopGuardTicks = 3;        // 0.055 s
	public const int HitstopPlayerTicks = 6;       // 0.10 s
	public const int DecalCap = 150;

	// ------------------------------------------------- §8.2 perception
	public const int RangePatrol = 400 * Fx.One;
	public const int RangeCurious = 430 * Fx.One;
	public const int RangeHunt = 450 * Fx.One;
	public const int RangeSearch = 450 * Fx.One;
	public const int RangeEngage = 470 * Fx.One;
	public const int RangeAlarmBonus = 60 * Fx.One;

	public const int HalfPatrol = 8866;        // 0.85 rad
	public const int HalfCurious = 10430;      // 1.00
	public const int HalfHunt = 11473;         // 1.10
	public const int HalfSearch = 10952;       // 1.05
	public const int HalfEngage = 11995;       // 1.15

	// Awareness is in TENTHS (spec §4.1): thresholds 30/66/100 -> 300/660/1000.
	public const int AwGainPerSec = 780;       // 78/s
	public const int AwDecayPerSec = 140;      // 14/s
	public const int GraceTicks = 48;          // 0.8 s
	public const int AwCurious = 300;
	public const int AwHunt = 660;
	public const int AwEngage = 1000;
	public const int AwCap = 1200;

	// Stimulus multipliers, Q8.
	public const int MulMove = 346;            // 1.35
	public const int MulStill = 128;           // 0.50
	public const int MulSneak = 154;           // 0.60
	public const int MulAlerted = 486;         // 1.9
	public const int QCentreFloor = 77;        // clamp lower bound 0.30
	public const int QNearFloor = 38;          // clamp lower bound 0.15

	// ---------------------------------------------- §8.3 non-visual stimuli
	public const int NoiseWalkRadius = 170 * Fx.One;
	public const int NoiseGainPerSec = 300;    // 30/s in tenths
	public const int NoiseCap = 620;           // 62
	public const int GunRange = 640 * Fx.One;
	public const int GunAwareness = 920;       // 92
	public const int BodyRange = 300 * Fx.One;
	public const int BodyBroadcastRange = 520 * Fx.One;
	public const int BodyBroadcastAw = 740;    // 74
	public const int SubdueRange = 120 * Fx.One;
	public const int SubdueAw = 340;           // 34
	public const int CalloutRange = 300 * Fx.One;
	public const int CalloutTicks = 78;        // 1.3 s
	public const int CalloutAw = 720;          // 72

	// ------------------------------------------------------ §8.4 alarm
	public const int AlarmFloor1 = 160;        // 16
	public const int AlarmFloor2 = 380;        // 38
	public const int AlarmDecayTicks = 1320;   // 22 s

	/// <summary>
	/// Ruling #1: spec §8.4 lists Curious among the states that hold the alarm
	/// up; the prototype's anyHot check omits it. Following the SPEC. Flip this
	/// to false to get prototype parity.
	/// </summary>
	public const bool CuriousHoldsAlarm = true;

	// ----------------------------------------------------- §8.5 engage
	public const int EngageStrafeSpeed = 19456;    // 76 px/s
	public const int EngageFarDist = 210 * Fx.One;
	public const int EngageNearDist = 115 * Fx.One;
	public const int AimDelayTicks = 27;           // 0.45 s
	public const int EngageCooldownTicks = 48;     // 0.8 s
	public const int ReaimTicks = 10;              // 0.16 s

	// ------------------------------------------------------ §8.1 speeds
	public const int SpeedPatrol = 24576;      // 96 px/s
	public const int SpeedPatrolAlarm = 30208; // 118 px/s
	public const int SpeedHunt = 40448;        // 158 px/s
	public const int SpeedSearch = 26624;      // 104 px/s
	public const int SearchTicks = 540;        // 9 s
	public const int HuntCloseDist = 150 * Fx.One;
	public const int LkpReach = 30 * Fx.One;
	public const int SearchPtReach = 24 * Fx.One;
	public const int SearchRadiusMin = 40 * Fx.One;
	public const int SearchRadiusMax = 150 * Fx.One;

	/// <summary>Sentry sway: +/- 0.16 rad/s, phase-offset per guard so they do
	/// not sweep in lockstep.</summary>
	public const int SentrySwayAmp = 1669;     // 0.16 rad
	public const int SentrySwayRate = 600;     // BRAD of phase per tick

	/// <summary>Cosmetic sprawl on death, +/- 0.5 rad.</summary>
	public const int DeadRollMax = 5215;

	/// <summary>
	/// Ruling #3: turn rates are PROPORTIONAL LERPS, num/den per tick, exactly
	/// as the prototype's `min(1, dt*k)`. The spec's "3.2 rad/s" / "9 rad/s"
	/// prose describes a constant angular velocity, which is a different curve.
	/// §8.6's acceptance table was measured against these lerps, so they win
	/// (spec §12: the prototype is correct about feel).
	/// Fraction is k/60 per tick. The denominator is 300 rather than 60 so that
	/// Curious's k = 3.2 stays exact: at TurnDen 60 it would truncate to 3,
	/// a 6% slower turn than the prototype, which is precisely the kind of
	/// silent feel drift spec §0 warns about.
	/// </summary>
	public const int TurnDen = 300;
	public const int TurnCurious = 16;         // 3.2/60
	public const int TurnEngage = 45;          // 9/60
	public const int TurnHunt = 35;            // 7/60
	public const int TurnSteer = 30;           // 6/60

	// ---------------------------------------------------- §2.4 acquisition
	public const int SubdueReach = 50 * Fx.One;
	public const int SubdueRearArc = 18253;    // 1.75 rad
	public const int CacheReach = 28 * Fx.One;
	public const int CacheDwellTicks = 42;     // 0.7 s

	// ---------------------------------------------- record scarcity (§2.3)
	//
	// NOT IN THE PORT SPEC, and a deliberate reversal of it. Every guard used to
	// carry one to three records and every cache two, which put roughly
	// twenty-three burnable records on the reference level — enough dilation
	// that the parasite was a resource you spent freely rather than one you
	// rationed. Records are the fuel, the score and the loss condition at once
	// (spec §2.3), so making them plentiful undercut all three at the same time.
	//
	// Now: one guard in GuardRecordEvery carries a SINGLE record, and a cache
	// holds one. That is seven on the reference level against twenty-three.

	/// <summary>
	/// Only every Nth guard, by letter, carries anything at all.
	///
	/// Tracks the guard count so record scarcity stays a DECISION rather than a
	/// side effect of a difficulty change: it went 3 -> 5 when floors went to
	/// twenty guards, and 5 -> 4 when they came back to ten. At ten guards this
	/// is three carriers, which with the caches and the briefing puts about
	/// seven records on a floor — the figure the scarcity pass aimed at.
	/// </summary>
	public const int GuardRecordEvery = 4;

	/// <summary>A cache holds one record, and it is a good one — that is what
	/// makes walking to it worth the exposure.</summary>
	public const int CacheRecordTier = 3;

	// ------------------------------------------------------ chests (§2.4a)
	//
	// NOT IN THE PORT SPEC. Containers of GEAR rather than of records: the
	// supply side of the campaign economy, and the reason to search a floor you
	// have already cleared. Looted through the same panel as a body.
	public const int ChestReach = 34 * Fx.One;

	/// <summary>
	/// Drops landing within this of an existing pile join it rather than
	/// starting another. Emptying a pack otherwise leaves six overlapping
	/// one-item piles on the same tile, and picking them back up means
	/// rummaging six times.
	/// </summary>
	public const int DropMergeDist = 26 * Fx.One;

	// ------------------------------------------------- looting and swapping
	// NEW NUMBERS. Nothing in the port spec covers either mechanic, so these are
	// invented and meant to be tuned. LootReach is deliberately shorter than
	// SubdueReach: you have to stand over a body, not walk past it.
	//
	// There is no loot dwell any more: the player picks items out of the body's
	// kit by clicking them, so the cost of looting is the time spent choosing
	// with the world still running, not a timer.
	public const int LootReach = 34 * Fx.One;
	public const int SwapTicks = 36;           // 0.6 s to holster and draw

	// ------------------------------------------------------------- loot
	// NEW NUMBERS, in the shop's dollars. They replaced hand-weighted roll
	// tables, and were set so a floor holds about what it did before: an
	// average chest from the old table was worth ~$1,800, and so was an
	// average guard's kit.

	/// <summary>Dollars per supply chest when a level authors no loot: line.</summary>
	public const int LootPerChest = 1800;

	/// <summary>Points per guard when a level authors no guard_loot: line.</summary>
	public const int GuardLootPoints = 1800;

	/// <summary>Most items one chest holds. A budget too big to spend in this
	/// many picks rolls on to the next chest rather than into junk.</summary>
	public const int ChestMaxItems = 5;

	/// <summary>
	/// Luck, rolled once per run, in percent: the sum of two draws so the middle
	/// is common and the ends are rare (50 to 150, mean 100). It changes WHAT the
	/// chest budget buys, not how much there is: at 150 legendaries are four
	/// times as likely and commons almost vanish; at 50 the reverse.
	/// </summary>
	public const int LuckMin = 50;
	public const int LuckSpread = 50;          // each of the two draws, 0..50

	/// <summary>How far luck tilts each rarity step, in percent of weight per
	/// point of luck away from 100, per tier away from Rare. At 200, luck 150
	/// makes a legendary five times as likely and luck 50 all but rules it out.</summary>
	public const int LuckTiltPerTier = 200;

	/// <summary>Base weights by rarity, common to legendary, before luck.</summary>
	public static readonly int[] RarityWeight = { 40, 30, 18, 9, 3 };

	/// <summary>A guard spends at most this share of his points on his gun, so
	/// a rich guard is not a walking rifle and nothing else. 80% of the default
	/// 1800 is an AK; 60% could not reach one, and half the floor carried Glocks.</summary>
	public const int GuardGunSharePct = 80;

	/// <summary>Chance a guard buys body armour at all, percent.</summary>
	public const int GuardArmourChancePct = 60;

	/// <summary>Most extra items (apparel, pack, attachments) on one guard.</summary>
	public const int GuardMaxExtras = 5;

	// --------------------------------------------------- glass and doors
	// NEW NUMBERS. Nothing in the port spec has a cell that changes state, so
	// every figure here is invented and meant to be tuned in play.

	/// <summary>
	/// How far a pane shattering carries. Deliberately wider than any SILENCED
	/// weapon's report (Welrod 130, VSS 260): shooting out a window with a quiet
	/// gun is still loud, because it is the glass that is heard, not the gun.
	/// That is also what makes a window a LURE -- break one across the room and
	/// the guards walk to it, not to you.
	/// </summary>
	public const int GlassNoiseRadius = 340 * Fx.One;

	/// <summary>
	/// Awareness a guard in earshot is raised TO. 70 is past AwHunt (66), so he
	/// comes to look, and short of AwEngage (100), so a sound alone never starts
	/// a firefight -- the same ceiling footsteps have (spec §8.3), set higher
	/// because breaking glass is not something a floor ignores.
	/// </summary>
	public const int GlassAwareness = 700;

	/// <summary>Reach from the player's centre to the nearest point of a door:
	/// the player's radius plus a short arm. Stand at it, not across the room.</summary>
	public const int DoorReach = 30 * Fx.One;

	/// <summary>A door is not silent. Guards within this hear it, with falloff
	/// and no line of sight, at <see cref="DoorNoiseAw"/> at the door itself.
	/// Halved at the stealth tier: easing a door is the quiet way to do it.</summary>
	public const int DoorNoiseRadius = 150 * Fx.One;

	/// <summary>Awareness ADDED by a door heard up close, in tenths. Two or three
	/// right beside a guard make him curious; one heard from across a room is
	/// nothing. Capped at NoiseCap like footsteps, so it can never engage.</summary>
	public const int DoorNoiseAw = 140;

	/// <summary>How far ahead of himself a walking guard checks for a shut door
	/// to open. A guard does not stop at a closed door; he opens it.</summary>
	public const int GuardDoorProbe = 8 * Fx.One;

	// ------------------------------------------------------- misc geometry
	public const int ActorRadius = 11 * Fx.One;
	public const int InvSqrt2 = 181;           // Q8, for diagonal input
	public const int UnstickNudge = 11473;     // 1.1 rad
	public const int UnstickTicks = 27;        // 0.45 s
	public const int WaypointReach = 18 * Fx.One;

	// ------------------------------------------- navigation (Guard_AI.md P0)
	// NEW NUMBERS, not in the port spec: guards steered straight at targets
	// (spec §10.1) until pathfinding was asked for. Proposals, to be tuned.

	/// <summary>A nav waypoint counts as passed inside this. Well under a
	/// cell, so a guard does not cut a corner the smoothing kept.</summary>
	public const int NavReach = 8 * Fx.One;

	/// <summary>
	/// A* searches the whole sim may run in one tick, served round-robin by
	/// guard index. A guard waiting its turn keeps its old path, or steers
	/// straight if it has none. Straight-line shortcuts are free.
	/// </summary>
	public const int NavSearchesPerTick = 4;

	/// <summary>How long a path stands before a moved goal may replace it.
	/// The last waypoint tracks the live goal meanwhile.</summary>
	public const int RepathCooldownTicks = 30;     // 0.5 s

	/// <summary>
	/// Speed while moving more than 90 degrees off facing, Q8. What makes a
	/// guard who watches his back slow (Guard_AI.md §6.2), and what a guard
	/// pays for the first few ticks of turning round on his route.
	/// </summary>
	public const int BackpedalQ8 = 179;            // 0.7

	// ---------------------------- postures, radio, backup (Guard_AI.md P1-P2)
	// NEW NUMBERS, not in the port spec: the posture model replaced spec §8.1's
	// state table on request. Proposals (Guard_AI.md §12), to be play-tested.

	/// <summary>
	/// SNAP SIGHT (the "faster meter"): a player this close, inside the cone and
	/// in plain view for SnapReactTicks, is recognised outright instead of
	/// filling the meter. Stance-blind by design, which is why the 120 px row of
	/// the spec §8.6 curve moved.
	/// </summary>
	public const int SnapSightRange = 150 * Fx.One;
	public const int SnapReactTicks = 12;          // 0.2 s: a human reaction

	/// <summary>A curious guard stares for this long after the stimulus stops
	/// before walking over to look.</summary>
	public const int CuriousLookTicks = 48;        // 0.8 s
	/// <summary>Walking pace to a stimulus. A guard past AwHunt hurries, at SpeedHunt.</summary>
	public const int SpeedInvestigate = SpeedPatrol;
	/// <summary>Give up walking to a point he cannot reach after this long.</summary>
	public const int InvestigateMaxTicks = 1200;   // 20 s
	public const int LookAroundTicks = 240;        // 4 s for the three-heading scan
	public const int LookAroundArc = 12517;        // 1.2 rad each side

	/// <summary>
	/// "Am I alone?" (Guard_AI.md §5.2): another guard within this many PATH
	/// cost units (10 per 20 px cell, i.e. 350 px walked) is an ally, and the
	/// guard shouts instead of radioing.
	/// </summary>
	public const int AllyPathCost = 175;

	/// <summary>How long a radio call takes. World clock, so dilation buys
	/// the player time to reach a guard mid-call.</summary>
	public const int RadioTicks = 90;              // 1.5 s

	/// <summary>Responders sent = Base + PerKill x guards killed this incident, capped.</summary>
	public const int ResponderBase = 2;
	public const int ResponderPerKill = 1;
	public const int ResponderCap = 5;

	/// <summary>Radius, in cells, a lone caller looks for a spot out of the
	/// LKP's line of sight to wait in.</summary>
	public const int HoldSearchCells = 6;

	/// <summary>A squad gives up gathering and goes with whoever came.</summary>
	public const int BackupWaitMaxTicks = 1200;    // 20 s
	public const int RallyRadius = 80 * Fx.One;

	/// <summary>Nobody has seen the player for this long AND the LKP has been
	/// searched: contact is lost, and the level is compromised.</summary>
	public const int ContactLostTicks = 480;       // 8 s

	/// <summary>A sentry holding his post on a compromised level sweeps his
	/// sector this far either side of his posted facing.</summary>
	public const int HoldSwayAmp = 5215;           // 0.5 rad

	// ------------------------------------------ flanking (Guard_AI.md P3)
	// NEW NUMBERS, proposals (Guard_AI.md §12).

	/// <summary>
	/// Path cost added to every cell within FlankPenaltyCells of an earlier
	/// squad-mate's route, so each later member takes another way in where the
	/// map has one. 60 is six straight steps: enough to prefer a second door a
	/// few cells further, not enough to walk the whole level round.
	/// </summary>
	public const int FlankPenalty = 60;
	public const int FlankPenaltyCells = 2;

	/// <summary>No penalty this close to the LKP: every route has to end
	/// there, and penalising the last few cells biases nothing.</summary>
	public const int FlankGoalFreeCells = 3;

	/// <summary>The longest a member with a short route waits for the others,
	/// so the squad arrives together rather than one at a time.</summary>
	public const int EtaSyncMaxTicks = 180;        // 3 s

	/// <summary>An LKP that moves this far re-plans the assault, at most once
	/// per FlankReplanTicks. No sync wait on a re-plan: the fight is on.</summary>
	public const int FlankReplanDist = 80 * Fx.One;
	public const int FlankReplanTicks = 60;        // 1 s

	// -------------------------------- the compromised sweep (Guard_AI.md P4)
	// NEW NUMBERS, proposals (Guard_AI.md §12).

	/// <summary>How far behind the leader each watcher walks, per place in line.</summary>
	public const int PairSpacing = 36 * Fx.One;
	/// <summary>The leader waits for anyone further back than this.</summary>
	public const int GroupWaitDist = 4 * PairSpacing;
	/// <summary>Leader pace: search speed at the backpedal rate, so the man
	/// walking backwards behind him keeps up. Slow and deliberate.</summary>
	public const int SpeedSweep = SpeedSearch * BackpedalQ8 / Fx.One;   // ~73 px/s
	/// <summary>Each watcher sweeps his sector this far either side.</summary>
	public const int WatchSwayArc = 4172;          // 0.4 rad
	/// <summary>A trio's flank man switches sides this often.</summary>
	public const int FlankSwapTicks = 120;         // 2 s
	/// <summary>Leader breadcrumbs: one every TrailStep, the newest TrailMax kept.</summary>
	public const int TrailStep = 8 * Fx.One;
	public const int TrailMax = 32;

	/// <summary>Sweep nodes tile the floor in blocks of this many cells a side.</summary>
	public const int SweepNodeCells = 6;
	/// <summary>A node counts as SEEN when a guard has it in his cone within this.</summary>
	public const int SweepSeeRange = 200 * Fx.One;
	public const int NodeReach = 24 * Fx.One;
	public const int NodeDwellTicks = 90;          // 1.5 s looking round
	/// <summary>A node not reached in this long is given up and another chosen.</summary>
	public const int NodeTravelMaxTicks = 3600;    // 60 s
	/// <summary>Node score, in ticks: staleness, less one tick per path-cost
	/// unit to walk there (about 1.6 ticks of walking), plus bonuses.</summary>
	public const int SweepDistWeight = 1;
	/// <summary>Nodes near where the level was compromised from are checked first.</summary>
	public const int SweepFocusBonusTicks = 1800;  // 30 s
	public const int SweepFocusCells = 20;
	/// <summary>A designer's '*' is worth this much staleness on top.</summary>
	public const int AuthoredNodeBonusTicks = 600; // 10 s
	/// <summary>The exit group keeps to nodes this close to the exit.</summary>
	public const int ExitWatchCells = 8;
	/// <summary>Fewer mobile guards than this and nobody is spared for the exit.</summary>
	public const int ExitWatchMinMobile = 4;

	// ----------------------------------------------- fear (Guard_AI.md §4.1)
	// NEW NUMBERS, proposals.

	/// <summary>How long a frightened guard freezes. World clock.</summary>
	public const int FearTicks = 60;               // 1 s

	/// <summary>Chance (Q8) a RELAXED guard freezes when bullets start flying:
	/// he hears the shot or blast, or is hit.</summary>
	public const int FearGunfireQ8 = 90;           // ~35%

	/// <summary>Chance (Q8) a guard freezes on SEEING an ally die, unless he is
	/// already in the fight...</summary>
	public const int FearAllyDeathQ8 = 128;        // 50%

	/// <summary>...when he mostly keeps his head.</summary>
	public const int FearAllyDeathCombatQ8 = 26;   // ~10%

	// ------------------------------------------- the specialist weapons
	// NEW NUMBERS, not in the port spec: the browser build had none of these
	// weapons. Proposals, to be tuned.

	/// <summary>How far a lightning discharge jumps from one body to the next.
	/// Ten cells: a patrol pair walking together is one shot, two guards at
	/// opposite ends of a room are not.</summary>
	public const int ArcReach = 200 * Fx.One;

	/// <summary>
	/// Inside this, the SHOOTER is the next conductor, whatever else is near.
	/// Checked at every node of the chain -- the strike point included, so a
	/// bolt into the wall beside you comes straight back.
	/// </summary>
	public const int ArcPlayerReach = 110 * Fx.One;

	/// <summary>What the arc does when it finds you. Lethal through any vest,
	/// the same as it is to anything else it reaches.</summary>
	public const int ArcSelfDamage = 999;

	/// <summary>What each wall a penetrating round goes through takes off it,
	/// Q8: 0.30. Three walls leave a third of the round.</summary>
	public const int WallPierceLossQ8 = 77;

	/// <summary>Fragments per grenade, laid evenly round the full circle with
	/// a little jitter each, so a blast is a pattern rather than dice.</summary>
	public const int FragCount = 28;

	/// <summary>Jitter on each fragment, as a fraction of the gap between
	/// neighbours, Q8.</summary>
	public const int FragJitterQ8 = 96;

	public const int FragSpeed = 358400;           // 1400 px/s
	public const int FragTicks = 10;               // ~230 px of reach

	/// <summary>How far a blast is HEARD. Guards who hear it hunt the blast
	/// point, not the thrower -- which is what makes a grenade a distraction
	/// as well as a weapon.</summary>
	public const int BlastHeardRadius = 1100 * Fx.One;

	/// <summary>A grenade's body, for bouncing off walls.</summary>
	public const int GrenadeRadius = 4 * Fx.One;

	/// <summary>Speed lost to rolling, per tick at full clock, Q8. About 5%,
	/// which stops an 800 px/s throw about 270 px out.</summary>
	public const int GrenadeDragQ8 = 13;

	/// <summary>Speed kept through a bounce, Q8: half.</summary>
	public const int GrenadeBounceQ8 = 128;

	/// <summary>Below this a grenade is at rest (fixed-point px/s).</summary>
	public const int GrenadeRestSpeed = 12 * Fx.One;

	/// <summary>An aimed throw is an underhand lob: this fraction of the
	/// throw speed, Q8.</summary>
	public const int GrenadeLobQ8 = 128;
}
