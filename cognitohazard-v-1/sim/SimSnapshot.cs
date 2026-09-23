using System.Collections.Generic;

namespace Cognitohazard.Sim;

public readonly struct ActorView
{
	public readonly char Id;
	public readonly int X, Y, Facing;
	public readonly GuardState State;
	public readonly GuardTask Task;
	public readonly int Awareness;

	/// <summary>How far through a radio call, Q8 in [0, 256]; 0 when not calling.</summary>
	public readonly int RadioQ8;

	/// <summary>Frozen in fear (Guard_AI.md §4.1).</summary>
	public readonly bool Afraid;
	public readonly int DeadFacing, DeadRoll;
	public readonly int RecoilQ8;
	public readonly int Health;

	/// <summary>Plate left, and what it started at. Presentation draws the
	/// silhouette from ArmourMax (is this one worth engaging?) and the bar from
	/// Armour (am I getting anywhere?).</summary>
	public readonly int Armour, ArmourMax;

	public ActorView(Actor a)
	{
		Id = a.Id; X = a.X; Y = a.Y; Facing = a.Facing;
		State = a.State; Task = a.Task; Awareness = a.Awareness;
		Afraid = a.Afraid;
		RadioQ8 = a.Task == GuardTask.Radio
			? (int)((long)a.RadioMt * Fx.One / (Tune.RadioTicks * Actor.Mt)) : 0;
		DeadFacing = a.DeadFacing; DeadRoll = a.DeadRoll; RecoilQ8 = a.RecoilQ8;
		Health = a.Health;
		Armour = a.Armour; ArmourMax = a.ArmourMax;
	}
}

public readonly struct BulletView
{
	public readonly int X, Y, Heading;
	public readonly bool FromPlayer;

	/// <summary>Fixed-point px/s. Presentation draws the tracer from this, so a
	/// fast round reads as a streak and a subsonic one as a dot.</summary>
	public readonly int Speed;

	/// <summary>A BulletKind ordinal: what to draw it as. A grenade is not a
	/// tracer, and speed alone cannot tell a rolling grenade from a slow round.</summary>
	public readonly int Kind;

	/// <summary>Whole ticks left to live. For a grenade, the fuse.</summary>
	public readonly int LifeTicks;

	public BulletView(int x, int y, int heading, bool fromPlayer, int speed,
		int kind = 0, int lifeTicks = 0)
	{ X = x; Y = y; Heading = heading; FromPlayer = fromPlayer; Speed = speed;
	  Kind = kind; LifeTicks = lifeTicks; }
}

public readonly struct CacheView
{
	public readonly int X, Y;
	public readonly bool Taken;
	public CacheView(int x, int y, bool taken) { X = x; Y = y; Taken = taken; }
}

/// <summary>A gear chest, and whether anything is left in it.</summary>
public readonly struct ChestView
{
	public readonly int X, Y, Items;
	public readonly bool Objective;
	public ChestView(int x, int y, int items, bool objective)
	{ X = x; Y = y; Items = items; Objective = objective; }
}

/// <summary>Gear lying on the floor, and how much of it.</summary>
public readonly struct GroundView
{
	public readonly int X, Y, Items;
	public GroundView(int x, int y, int items) { X = x; Y = y; Items = items; }
}

/// <summary>A glass pane or a door, and whether it is open (a door swung, a
/// pane shot out).</summary>
public readonly struct PanelView
{
	public readonly PanelKind Kind;
	public readonly Rect Rect;
	public readonly bool Vertical, Open;
	public PanelView(PanelKind kind, Rect rect, bool vertical, bool open)
	{ Kind = kind; Rect = rect; Vertical = vertical; Open = open; }
}

public readonly struct RecordView
{
	public readonly int NameIndex, Tier, Charge, State;
	public RecordView(int nameIndex, int tier, int charge, int state)
	{ NameIndex = nameIndex; Tier = tier; Charge = charge; State = state; }
}

/// <summary>
/// Read-only per-tick view for game/ and tests/ (spec §3.2). This is the whole
/// boundary: presentation reads this and nothing else, and there are no
/// callbacks back into sim on the hot path.
/// </summary>
public sealed class SimSnapshot
{
	public long Tick;

	public int PlayerX, PlayerY, PlayerFacing;
	public bool PlayerAlive, PlayerSneaking, PlayerReloading;

	/// <summary>Which movement tier the player is on, and how much of the
	/// post-sprint recovery is left as a Q8 fraction. Both drive the HUD.</summary>
	public int PlayerMoveTier;
	public int PlayerReadyQ8;

	/// <summary>Rotary spin-up, Q8. Zero for any weapon that has none.</summary>
	public int PlayerSpinQ8;
	public int PlayerMag, PlayerHeat;
	public int PlayerHealth, PlayerArmour, PlayerArmourMax;
	public bool PlayerAiming;
	public int PlayerAimTarget, PlayerAimLockMt, PlayerAimLockFullMt;

	/// <summary>Aim disturbance, Q8. Presentation only.</summary>
	public int PlayerSway;

	/// <summary>
	/// HALF the cone the next round will be drawn from, in BRAD, with heat,
	/// sway and the aiming stance already folded in. Carried across the
	/// boundary rather than recomputed in game/, because a reticle that
	/// disagrees with the bullet is worse than no reticle.
	/// </summary>
	public int PlayerSpreadHalf;

	public int WorldScale, PlayerScale;
	public bool Dilating;

	public int AlarmLevel;
	public int Exposure;
	public string? Over;

	public int Provable, Unprovable, DestroyedScore;

	/// <summary>Mission objectives required, and how many are in the pack.</summary>
	public int ObjectivesTotal, ObjectivesCarried;
	public int FuelIndex;
	public int Kills, Subdues, Shots;

	public List<ActorView> Guards = new();
	public List<BulletView> Bullets = new();
	public List<CacheView> Caches = new();
	public List<ChestView> Chests = new();
	public List<GroundView> Ground = new();
	public List<PanelView> Panels = new();
	public List<RecordView> Records = new();
	public List<SimEvent> Events = new();
}
