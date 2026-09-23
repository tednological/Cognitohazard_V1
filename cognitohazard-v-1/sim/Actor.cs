using System.Collections.Generic;

namespace Cognitohazard.Sim;

/// <summary>
/// A guard's POSTURE: how alert he is (Guard_AI.md §2). What the HUD glyph
/// shows and what picks his cone. What he is DOING about it is the
/// <see cref="GuardTask"/>. Ordinals are hashed and mirrored by game/main.gd
/// (ST_*): append, never reorder.
/// </summary>
public enum GuardState
{
	Relaxed, Curious, Combat, Hunting, Down, Dead
}

/// <summary>
/// What a guard is doing within his posture (Guard_AI.md §2). Hashed and
/// mirrored by game/main.gd (TASK_*): append, never reorder.
/// </summary>
public enum GuardTask
{
	None,
	Patrol, Post,                              // Relaxed
	Look, Investigate, LookAround, Return,     // Curious (and Hunting's side trips)
	Engage, Converge, SearchLkp, Radio, HoldForBackup, Rally,   // Combat
	Sweep, HoldPost,                           // Hunting
	Assault,                                   // Combat: a squad's flanking move (P3)
	WatchExit,                                 // Hunting: a sweep group kept near the exit (P4)
}

/// <summary>Why a guard is keying the radio (Guard_AI.md §5.3, §7).</summary>
public enum RadioPurpose
{
	None,
	/// <summary>Alone in a fight: send help. Completing it dispatches responders.</summary>
	Backup,
	/// <summary>A body, or a fight that ended with the player gone. Completing it
	/// compromises the level.</summary>
	Report,
}

/// <summary>
/// Player and guards (spec §3.1). All hash-feeding state is integer: position
/// in 1/256 px, facing in BRAD, awareness accumulated in tenths x 200.
///
/// Timers are held in MILLI-TICKS (one tick == 200 mt). A clock advances a
/// timer by its own scale numerator each tick, so a guard under WORLD_SLOW
/// (36/200) ages its cooldowns at 0.18x exactly, with no float delta and no
/// accumulated rounding error.
/// </summary>
public sealed class Actor
{
	public const int Mt = Fx.ScaleDen;      // milli-ticks per tick

	public char Id;
	public int X, Y;                   // fixed-point
	public int Radius = Tune.ActorRadius;
	public int Facing;                 // BRAD
	public bool Alive = true;

	/// <summary>Health and armour, both integer (RPG plan §2). Damage lands on
	/// armour first and the remainder carries into health; armour is spent for
	/// the mission and does not regenerate.</summary>
	public int Health = Tune.BaseHealth;
	public int Armour;

	/// <summary>
	/// What <see cref="Armour"/> started at, so presentation can draw a plate
	/// that visibly depletes rather than one that simply vanishes. Set once at
	/// spawn and never touched again; hashed only for consistency with every
	/// other spawn-time field.
	/// </summary>
	public int ArmourMax;

	public int EffectiveHp => Health + Armour;

	/// <summary>
	/// Apply damage. Returns true if this killed the actor. Reports how much
	/// armour absorbed so presentation can distinguish a plate stopping a round
	/// from a round going through.
	/// </summary>
	public bool TakeDamage(int amount, out int absorbedByArmour)
		=> TakeDamage(amount, 0, out absorbedByArmour);

	/// <summary>
	/// As above, with a Q8 fraction of the damage bypassing armour entirely.
	/// Armour-piercing ammo raises it; without this AP would be a flat damage
	/// bonus with a fancy name.
	/// </summary>
	public bool TakeDamage(int amount, int pierceQ8, out int absorbedByArmour)
	{
		absorbedByArmour = 0;
		if (amount <= 0 || !Alive) return false;

		int pierced = 0;
		if (pierceQ8 > 0 && Armour > 0)
		{
			if (pierceQ8 > Fx.One) pierceQ8 = Fx.One;
			pierced = (int)(((long)amount * pierceQ8) >> Fx.Shift);
			amount -= pierced;
		}

		if (Armour > 0)
		{
			absorbedByArmour = amount < Armour ? amount : Armour;
			Armour -= absorbedByArmour;
			amount -= absorbedByArmour;
		}

		amount += pierced;
		if (amount > 0) Health -= amount;
		if (Health > 0) return false;

		Health = 0;
		Alive = false;
		return true;
	}

	// ------------------------------------------------------------- player
	public int Mag = Tune.Magazine;
	public int ReloadMt;
	public int CooldownMt;

	/// <summary>Counts down while holstering one weapon and drawing the other.
	/// Firing is blocked for the duration, the way it is during a reload.</summary>
	public int SwapMt;

	/// <summary>
	/// The magazine of the weapon NOT in hand. Swapping exchanges this with Mag,
	/// so each weapon keeps its own ammunition instead of the count following the
	/// player across a swap.
	/// </summary>
	public int MagStowed;

	/// <summary>
	/// What this guard is carrying, as gear item ids. Emptied one item at a time
	/// as the player strips the body. Player actors leave this empty.
	/// </summary>
	public readonly List<int> Kit = new();

	public int Heat;                   // Q8, [0, 256]

	/// <summary>
	/// Aim disturbance, Q8 in [0, 256]. Raised by swinging the weapon and by
	/// moving, decays once the shooter settles. Carried by the PLAYER AND BY
	/// GUARDS -- the firing-error model is the same on both sides.
	/// </summary>
	public int SwayQ8;

	/// <summary>Which movement tier the player is on this tick
	/// (InputFrame.TierStealth..TierSprint). Guards do not use it.</summary>
	public int MoveTier = InputFrame.TierWalk;

	/// <summary>
	/// Milli-ticks until the weapon is back on target after a sprint. While it
	/// is running, sway is floored high and an aim lock cannot build — the
	/// "you have to re-aim after sprinting" cost, expressed in the terms the
	/// firing model already has rather than as a mechanic of its own.
	/// </summary>
	public int ReadyMt;

	/// <summary>
	/// Milli-ticks of rotary spin-up accumulated while the trigger is held.
	/// A weapon whose SpinUpTicks is zero never reads it, which is every
	/// weapon but one.
	/// </summary>
	public int SpinMt;

	/// <summary>Moving as quietly as the player can. Derived, so it cannot
	/// disagree with the tier.</summary>
	public bool Sneaking => MoveTier == InputFrame.TierStealth;
	public int MovedFx;                // distance covered this tick
	public int NoiseRadius;
	public int RecoilQ8;               // presentation only

	/// <summary>Guard index the crosshair is currently held on, or -1.</summary>
	public int AimTarget = -1;

	/// <summary>How long the crosshair has been on that target, in milli-ticks.
	/// At AimLockTicks the next round becomes a headshot.</summary>
	public int AimLockMt;

	public bool Aiming;

	public bool HeadshotReady => AimLockMt >= Tune.AimLockTicks * Mt;

	// -------------------------------------------------------------- guard
	public GuardState State = GuardState.Relaxed;
	public GuardTask Task = GuardTask.Patrol;

	/// <summary>Milli-ticks spent in the current task, where the task times
	/// itself (Look, LookAround, Investigate).</summary>
	public int TaskMt;

	/// <summary>Where the current task is headed (Return, HoldForBackup, Rally)
	/// and, where it has one, the facing it settles on.</summary>
	public int TaskX, TaskY, TaskFacing;

	/// <summary>The facing a sentry was posted with, which Return restores.</summary>
	public int PostFacing;

	/// <summary>This investigation is at SpeedHunt: he set off past AwHunt.</summary>
	public bool Hurry;

	/// <summary>Milli-ticks the player has stood inside snap-sight range in
	/// plain view (Guard_AI.md §5.1). Reset the moment that stops.</summary>
	public int SnapMt;

	/// <summary>Milli-ticks into a radio call, and what the call is for. A
	/// purpose outlives an interruption: a guard who breaks off to shoot back
	/// re-keys once the player is out of sight.</summary>
	public int RadioMt;
	public RadioPurpose Radio;

	/// <summary><see cref="GuardNet"/> squad id, or -1.</summary>
	public int SquadId = -1;

	/// <summary>
	/// This guard's leg of a squad assault (Guard_AI.md §5.5): his own route to
	/// the LKP, planned to avoid his squad-mates' so they come in from
	/// different sides. Fixed-point waypoints; the last is the LKP as planned.
	/// </summary>
	public readonly List<int> RouteX = new();
	public readonly List<int> RouteY = new();
	public int RouteIndex;

	/// <summary>Milli-ticks to hold before moving off, so a squad whose routes
	/// differ in length still breaks in together.</summary>
	public int WaitMt;

	/// <summary><see cref="GuardNet"/> sweep group id on a compromised level, or -1.</summary>
	public int GroupId = -1;

	/// <summary>
	/// AFRAID (Guard_AI.md §4.1): milli-ticks left frozen in fear, 0 when not.
	/// Layered OVER the posture rather than replacing it: a frightened guard
	/// keeps knowing what he knows, and keeps his squad, group and radio call,
	/// he just cannot act on any of it until the freeze passes.
	/// </summary>
	public int FearMt;

	public bool Afraid => FearMt > 0;

	/// <summary>Awareness in tenths x 200. Exposed as tenths via <see cref="Awareness"/>.
	/// The extra 200x resolution is what lets a guard accumulate suspicion at
	/// WORLD_SLOW without the per-tick gain truncating to zero.</summary>
	public int AwAcc;

	public int Awareness => AwAcc / Mt;

	public int GraceMt;
	public int HomeX, HomeY;
	public int LkpX, LkpY;
	public bool HasLkp;
	public int[]? PathX;
	public int[]? PathY;
	public int WaypointIndex;

	// ---- navigation (Guard_AI.md §8). The current path, as fixed-point
	// waypoints; the LAST one is always steered at the live goal rather than
	// where the goal was when the path was planned.
	public readonly List<int> NavX = new();
	public readonly List<int> NavY = new();
	public int NavIndex;

	/// <summary>Cell of the goal the current path was planned for, -1 for
	/// none. A goal that moves to another cell asks for a new path.</summary>
	public int NavGoalCell = -1;

	/// <summary>The goal asked for THIS tick, read when path requests are
	/// served at the end of the guard pass.</summary>
	public int NavGoalX, NavGoalY;

	/// <summary>Milli-ticks before a moved goal may replace the path.</summary>
	public int RepathMt;

	/// <summary>Set when the guard wants a path this tick. Transient: set by
	/// the behaviour, cleared when requests are served the same tick.</summary>
	public bool NavPending;

	/// <summary>The straight line got this guard stuck; plan round it.</summary>
	public bool NavNoShortcut;

	public int AimMt;
	public int CallMt;
	public int SearchMt;
	public int StuckMt;
	public int SearchX, SearchY;
	public bool HasSearchPt;

	/// <summary>Set once this body has been discovered, so it is only ever
	/// reported once (prototype parity).</summary>
	public bool Found;

	public int DeadFacing;
	public int DeadRoll;               // BRAD, cosmetic sprawl

	public List<Record> Carried = new();

	public bool Downed => State == GuardState.Down;
	public bool Prone => State == GuardState.Down || State == GuardState.Dead;

	public void SetAwareness(int tenths) => AwAcc = tenths * Mt;

	public void SetLkp(int x, int y) { LkpX = x; LkpY = y; HasLkp = true; }

	public void HashInto(ref Hash64 h)
	{
		h.Add(Id); h.Add(X); h.Add(Y); h.Add(Facing); h.Add(Alive);
		h.Add(Health); h.Add(Armour); h.Add(ArmourMax);
		h.Add(Mag); h.Add(ReloadMt); h.Add(CooldownMt); h.Add(Heat); h.Add(SwayQ8);
		h.Add(MoveTier); h.Add(ReadyMt); h.Add(SpinMt); h.Add((int)State); h.Add(AwAcc); h.Add(GraceMt);
		h.Add((int)Task); h.Add(TaskMt); h.Add(TaskX); h.Add(TaskY); h.Add(TaskFacing); h.Add(PostFacing);
		h.Add(SnapMt); h.Add(RadioMt); h.Add((int)Radio); h.Add(SquadId); h.Add(Hurry);
		h.Add(RouteIndex); h.Add(WaitMt); h.Add(GroupId); h.Add(FearMt); h.Add(RouteX.Count);
		for (int i = 0; i < RouteX.Count; i++) { h.Add(RouteX[i]); h.Add(RouteY[i]); }
		h.Add(AimTarget); h.Add(AimLockMt); h.Add(Aiming);
		h.Add(LkpX); h.Add(LkpY); h.Add(HasLkp); h.Add(WaypointIndex);
		h.Add(AimMt); h.Add(CallMt); h.Add(SearchMt); h.Add(StuckMt);
		h.Add(SearchX); h.Add(SearchY); h.Add(HasSearchPt); h.Add(Found);
		h.Add(NavIndex); h.Add(NavGoalCell); h.Add(NavGoalX); h.Add(NavGoalY);
		h.Add(RepathMt); h.Add(NavNoShortcut);
		h.Add(NavX.Count);
		for (int i = 0; i < NavX.Count; i++) { h.Add(NavX[i]); h.Add(NavY[i]); }
		h.Add(Carried.Count);
		for (int i = 0; i < Carried.Count; i++) Carried[i].HashInto(ref h);
	}
}
