using System.Collections.Generic;

namespace Cognitohazard.Sim;

/// <summary>
/// A gear chest standing in the level. Looted through exactly the same panel a
/// body is, and through the same index space (see SimWorld.TryLootTarget), so
/// the player learns one interaction rather than two.
/// </summary>
/// <summary>
/// Gear on the floor, dropped by the player. Looted through the same panel and
/// the same index space as a body or a chest, so putting something down and
/// picking it up again are the two halves of one interaction rather than two
/// separate ones.
/// </summary>
public sealed class GroundPile
{
	public int X, Y;
	public readonly List<int> Kit = new();

	public void HashInto(ref Hash64 h)
	{
		h.Add(X); h.Add(Y); h.Add(Kit.Count);
		for (int i = 0; i < Kit.Count; i++) h.Add(Kit[i]);
	}
}

public sealed class ChestRuntime
{
	public int X, Y;

	/// <summary>An objective site rather than a supply chest. It holds the one
	/// thing the mission is about and nothing else.</summary>
	public bool Objective;

	public readonly List<int> Kit = new();

	/// <summary>Stripped bare. Kept in the world rather than removed so an
	/// emptied chest still reads as somewhere you have already been.</summary>
	public bool Empty => Kit.Count == 0;

	public void HashInto(ref Hash64 h)
	{
		h.Add(X); h.Add(Y); h.Add(Objective); h.Add(Kit.Count);
		for (int i = 0; i < Kit.Count; i++) h.Add(Kit[i]);
	}
}

/// <summary>
/// A glass pane or a door, as it stands right now. <see cref="Open"/> is the
/// only thing that changes: a door swung open, or a pane shot out. A broken
/// pane never mends and an open door can be shut again.
/// </summary>
public sealed class PanelRuntime
{
	public PanelKind Kind;
	public Rect Rect;
	public bool Vertical;
	public bool Open;

	public bool IsDoor => Kind == PanelKind.Door;
	public bool IsGlass => Kind == PanelKind.Glass;

	/// <summary>Blocks WALKING: an intact pane or a shut door.</summary>
	public bool Solid => !Open;

	/// <summary>Blocks SEEING and stops ROUNDS: a shut door only. Glass, whole
	/// or broken, is transparent, and a round goes through it.</summary>
	public bool Opaque => IsDoor && !Open;
}

public sealed class CacheRuntime
{
	public int X, Y;
	public bool Taken;
	public int DwellMt;
	public List<Record> Contents = new();

	public void HashInto(ref Hash64 h)
	{
		h.Add(X); h.Add(Y); h.Add(Taken); h.Add(DwellMt); h.Add(Contents.Count);
		for (int i = 0; i < Contents.Count; i++) Contents[i].HashInto(ref h);
	}
}

/// <summary>
/// Owns the tick and every subsystem; the only public entry point (spec §3.1).
///
/// step() takes NO delta. One call == one tick == 1/60 s of world time. Time
/// dilation is expressed as WHICH subsystems advance and by how much of a tick,
/// as an exact rational over 200 — never as a variable float delta (spec §1.3).
/// </summary>
public sealed partial class SimWorld
{
	public readonly Level Level;
	public readonly DetRng Rng;

	/// <summary>
	/// What the player brought in (RPG plan §1), produced by the campaign layer
	/// in game/; sim/ never learns that an inventory or a save file exists.
	///
	/// No longer readonly: drawing the secondary weapon changes which hand is
	/// full, and every weapon stat in this file resolves through Loadout.Held.
	/// The setter is private -- only StepSwap moves it.
	/// </summary>
	public Loadout Loadout { get; private set; }
	public readonly TimeAuthority Clocks = new();
	public readonly AlarmState Alarm = new();
	public readonly RecordStore Records = new();
	public readonly Projectiles Bullets = new();
	public readonly EventLog Log = new();

	public readonly Actor Player = new();
	public readonly List<Actor> Guards = new();
	public readonly List<CacheRuntime> Caches = new();
	public readonly List<ChestRuntime> Chests = new();
	public readonly List<GroundPile> Ground = new();

	/// <summary>
	/// Glass and doors, parallel to Level.Panels and in the same order: that
	/// index is what InputFrame.DoorPick names.
	/// </summary>
	public readonly List<PanelRuntime> Panels = new();

	/// <summary>
	/// What stops a body moving: the walls, every intact pane and every shut
	/// door. Derived from Panels by <see cref="RebuildBlockers"/>, never stored
	/// state, so it needs no hashing of its own. On a level with no panels this
	/// IS Level.Walls, the same array, which is why adding the feature moved no
	/// golden hash.
	/// </summary>
	public Rect[] Solid { get; private set; } = System.Array.Empty<Rect>();

	/// <summary>What stops an eye and a round: the walls and every shut door.
	/// Glass is not in it. Same derivation and the same no-panel identity.</summary>
	public Rect[] Opaque { get; private set; } = System.Array.Empty<Rect>();

	// Every pane, whole or not, with the panel each one is and whether it is
	// still whole -- the shape Projectiles wants. Rebuilt with the rest.
	private Rect[] _glassRects = System.Array.Empty<Rect>();
	private int[] _glassPanel = System.Array.Empty<int>();
	private bool[] _glassIntact = System.Array.Empty<bool>();
	private bool _blockersDirty;

	/// <summary>
	/// How many objective items this mission requires, and how many are in the
	/// pack right now. Extracting with fewer is allowed — it simply is not a
	/// completed mission, and game/ pays nothing for it.
	/// </summary>
	public int ObjectivesTotal;

	public int ObjectivesCarried
	{
		get
		{
			int n = 0;
			for (int pi = 0; pi < Pack.Capacity; pi++)
				if (Pack.IsLive(pi) && GearCatalog.IsObjective(Pack.ItemOf(pi))) n++;
			return n;
		}
	}

	/// <summary>Every objective this mission asked for is in the pack.</summary>
	public bool ObjectivesMet => ObjectivesCarried >= ObjectivesTotal;

	/// <summary>
	/// What the player has stripped off bodies this mission, as a spatial grid
	/// sized by the worn backpack. Sim state: hashed, and carried in replays.
	/// </summary>
	public readonly PackGrid Pack = new();

	/// <summary>
	/// Guard kits are rolled from their OWN stream, salted off the mission seed.
	/// Sharing Rng would mean adding a loot roll shifted every later draw, so
	/// every guard's patrol and every spread cone would change the moment this
	/// feature landed. A separate stream keeps combat determinism where it was.
	/// </summary>
	public readonly DetRng LootRng;

	public long Tick { get; private set; }
	public string? Over { get; private set; }

	public int Kills, Subdues, Shots;

	private readonly List<Projectiles.Impact> _impacts = new();

	/// <summary>
	/// default(Loadout) is the pistol, so callers that do not pass one keep the
	/// behaviour the game had before loadouts existed.
	/// </summary>
	public SimWorld(Level level, ulong seed, Loadout loadout = default)
	{
		Level = level;
		Rng = new DetRng(seed);
		Loadout = loadout;
		Records.ResetNaming();

		Player.Id = '@';
		Player.X = level.SpawnX;
		Player.Y = level.SpawnY;
		Player.Radius = Tune.PlayerRadius;
		Player.Mag = loadout.Spec.Magazine;
		Player.Health = Tune.BaseHealth;
		Player.Armour = loadout.ArmourSpec.Armour;

		for (int i = 0; i < level.Panels.Count; i++)
		{
			var d = level.Panels[i];
			Panels.Add(new PanelRuntime { Kind = d.Kind, Rect = d.Rect, Vertical = d.Vertical });
		}
		RebuildBlockers();

		// Guards iterate by index, always (spec §4.1). Level.Guards comes from a
		// row-major grid scan, so this order is stable across runs.
		for (int i = 0; i < level.Guards.Count; i++)
		{
			var g = level.Guards[i];
			var a = new Actor
			{
				Id = g.Id,
				Health = Tune.GuardHealth,
				X = g.X, Y = g.Y,
				HomeX = g.X, HomeY = g.Y,
				PathX = g.PathX, PathY = g.PathY,
				State = GuardState.Relaxed,
				Task = g.PathX != null ? GuardTask.Patrol : GuardTask.Post,
				WaypointIndex = g.PathX != null && g.PathX.Length > 0 ? 1 % g.PathX.Length : 0,
			};
			for (int t = 0; t < g.Tiers.Length; t++) a.Carried.Add(Records.Mint(g.Tiers[t]));
			Guards.Add(a);
		}

		for (int i = 0; i < level.Caches.Count; i++)
		{
			var c = level.Caches[i];
			var rc = new CacheRuntime { X = c.X, Y = c.Y };
			for (int t = 0; t < c.Tiers.Length; t++) rc.Contents.Add(Records.Mint(c.Tiers[t]));
			Caches.Add(rc);
		}

		// The stowed weapon carries its own full magazine, so a swap is a fresh
		// gun rather than the same ammunition following the player across it.
		//
		// Loadout.Stowed, NOT Loadout.Secondary: with the secondary already
		// drawn (active = 1) the holstered weapon is the one in HAND, and this
		// line was filling the stowed magazine from the gun the player was
		// holding. And StowedSpec, not the raw catalogue entry, or an extended
		// mag fitted to the stowed weapon is ignored until the first reload --
		// which is the everyday half of the same bug. StowedSpec rather than
		// SpecFor(Stowed) since the rails went per-weapon: the stowed gun's
		// magazine is the one fitted to THAT gun, not to the one in hand.
		Player.MagStowed = loadout.HasSecondary
			? loadout.StowedSpec.Magazine
			: 0;

		Pack.Resize(loadout.PackW, loadout.PackH);

		// WHAT THE PLAYER WALKED IN WITH. Auto-placed in the order it was
		// packed at base, through the same door looting uses, so a kit and a
		// find land in the pack by one rule.
		//
		// Anything that will not fit is simply not placed: the pack is the
		// authority on its own room, and a bag swapped for a smaller one after
		// the kit was chosen must not be able to make the sim throw. The stash
		// screen refuses to stage more than will fit, so this is the floor
		// under that, not the rule itself.
		for (int i = 0; i < loadout.CarriedCount; i++)
			Pack.AutoPlace(loadout.CarriedAt(i));

		for (int i = 0; i < level.Chests.Count; i++)
			Chests.Add(new ChestRuntime
			{
				X = level.Chests[i].X,
				Y = level.Chests[i].Y,
				Objective = level.Chests[i].Objective,
			});

		ObjectivesTotal = level.Objectives;

		LootRng = new DetRng(seed ^ LootSalt);
		RollGuardKits();
		// AFTER the guards, deliberately: rolling chests first would shift every
		// guard's kit on every level that has one, for no reason a player could
		// see, and invalidate every recording made before chests existed.
		RollChests();

		// The player starts holding one tier-1 record. This solves the
		// empty-wallet problem: without it the first room has no dilation
		// available, which is the moment the player is least equipped. Known
		// fiction debt, not a bug (spec §2.4).
		var briefing = Records.Mint(1);
		Records.Held.Add(briefing);
		Records.FuelIndex = 0;
	}

	// =====================================================================
	// Tick
	// =====================================================================

	public void Step(in InputFrame input)
	{
		Log.Clear();
		_impacts.Clear();

		StepParasite(in input);
		Clocks.Resolve();

		int pScale = Clocks.PlayerScale;
		int wScale = Clocks.WorldScale;

		StepPlayer(in input, pScale);
		StepSwap(in input, pScale);
		StepDoor(in input);
		StepLoot(in input);
		StepDrop(in input);
		StepEquip(in input);
		StepSpawn(in input);
		StepGuards(wScale);
		Bullets.Step(Opaque, _glassRects, _glassIntact, Guards, Player, wScale, _impacts);
		ResolveImpacts();
		if (_blockersDirty) RebuildBlockers();
		Alarm.Step(Guards, wScale);

		Tick++;
	}

	// =====================================================================
	// §6 The parasite — dilation paid for with knowledge
	// =====================================================================

	private void StepParasite(in InputFrame input)
	{
		if (!Records.FuelValid) Records.FuelIndex = Records.PickFuel();

		bool wants = input.Dilate && Player.Alive && Over == null;

		// The jolt MUST interrupt: the player cannot hold through a degradation.
		// That is what makes the cost felt rather than merely counted (spec §6.1).
		if (!wants || Clocks.JoltActive || !Records.FuelValid)
		{
			Clocks.RequestDilation(false);
			return;
		}

		Clocks.RequestDilation(true);

		// Charge drains by 1 per REAL frame, not per world frame.
		var f = Records.Held[Records.FuelIndex];
		f.Charge--;
		if (f.Charge > 0) return;

		if (f.State == RecordState.Intact)
		{
			f.State = RecordState.Degraded;
			f.Charge = Tune.RecordStageTicks;
			Records.Degraded++;
			Clocks.RequestJolt(Tune.JoltTicks);
			Log.Add(SimEventKind.Degrade, Player.X, Player.Y, 0, f.Tier);
		}
		else
		{
			f.State = RecordState.Gone;
			f.Charge = 0;
			Records.Destroyed++;
			Clocks.RequestJolt(Tune.JoltTicks);
			Log.Add(SimEventKind.Destroy, Player.X, Player.Y, 0, f.Tier);
			Records.FuelIndex = Records.PickFuel();
		}
	}

	// =====================================================================
	// §7 Player
	// =====================================================================

	private void StepPlayer(in InputFrame input, int scale)
	{
		var p = Player;
		var weapon = Loadout.Spec;

			p.MoveTier = input.MoveTier;
		p.MovedFx = 0;
		p.NoiseRadius = 0;

		if (!p.Alive || Over != null) return;
		if (scale <= 0) return;

		// DEVIATES FROM THE SPEC, deliberately and on request. Aim rotation used
		// to be free, instant and unscaled: `p.Facing = input.AimBrad`, which the
		// spec preserved as a known exploit (§10.2) because the prototype did it.
		// The weapon now SWINGS toward the cursor at its own rate, so bringing a
		// SAW round onto a target takes four times what a Glock takes, and the
		// swing feeds sway below. Two consequences worth naming:
		//   - The muzzle and the cursor genuinely diverge, so the reticle is
		//     drawn on the muzzle line (game/main.gd) rather than under the mouse.
		//   - §10.2's free spin is closed as a side effect, because the turn is
		//     now paid for on the player clock like every other action.
		// The tier scales the weapon's own turn rate, so a SAW at a sprint is
		// close to unsteerable and a Glock at a walk is unaffected.
		int turnNum = (int)(((long)weapon.TurnNum * Tune.TierTurnQ8(p.MoveTier)) >> Fx.Shift);
		int wasFacing = p.Facing;
		p.Facing = Brad.TurnToward(p.Facing, input.AimBrad,
			turnNum * scale / Fx.ScaleDen, Tune.TurnDen);
		int turnedBrad = Brad.Norm(p.Facing - wasFacing);

		if (input.MoveX != 0 || input.MoveY != 0)
		{
			// Only two cases exist for -1/0/+1 input: cardinal (1.0) and
			// diagonal (1/sqrt2 == 181/256), so no square root is needed.
			int unit = (input.MoveX != 0 && input.MoveY != 0) ? Tune.InvSqrt2 : Fx.One;

			// Through the loadout, not off Tune: this is where a weapon's weight
			// and a plate carrier's bulk are actually paid for. They had been
			// computed and then never read, which quietly made every weapon
			// handle like a bare Glock.
			// Stealth takes the loadout's own sneak speed; the rest scale the
			// walk speed, so a plate carrier's penalty follows you up the tiers
			// instead of only applying at one of them.
			int speed = p.Sneaking
				? Loadout.SneakSpeed
				: (int)(((long)Loadout.WalkSpeed * Tune.TierSpeedQ8(p.MoveTier)) >> Fx.Shift);
			if (input.Aim) speed = (int)(((long)speed * weapon.AimMoveQ8) >> Fx.Shift);

			int stepFx = Fx.Mul(Fx.PerTick(speed, scale), unit);

			int ox = p.X, oy = p.Y;
			Geometry.MoveSlide(Solid, ref p.X, ref p.Y,
				stepFx * input.MoveX, stepFx * input.MoveY, p.Radius);
			p.MovedFx = Fx.Hypot(p.X - ox, p.Y - oy);

			if (p.MovedFx > 2) p.NoiseRadius = Tune.TierNoise(p.MoveTier);
		}

		// Sprinting keeps the weapon down. Leaving a sprint starts the clock on
		// getting it back up; while that runs, sway is floored and the aim lock
		// will not build (StepAim).
		if (p.MoveTier == InputFrame.TierSprint)
		{
			p.ReadyMt = Tune.SprintRecoverTicks * Actor.Mt;
		}
		else if (p.ReadyMt > 0)
		{
			p.ReadyMt -= scale;
			if (p.ReadyMt < 0) p.ReadyMt = 0;
		}

		StepSway(p, turnedBrad, p.MovedFx, scale, p.ReadyMt > 0 ? Tune.SprintSwayQ8 : 0);
		StepAim(in input, scale);

		// Weapon timers run on the player clock.
		if (p.CooldownMt > 0) p.CooldownMt -= scale;
		if (p.RecoilQ8 > 0) p.RecoilQ8 -= Fx.PerTick(Tune.HeatDecayPerSec * 7 / 2, scale);
		if (p.RecoilQ8 < 0) p.RecoilQ8 = 0;
		if (p.Heat > 0)
		{
			p.Heat -= Fx.PerTick(weapon.HeatDecayPerSec, scale);
			if (p.Heat < 0) p.Heat = 0;
		}
		if (p.ReloadMt > 0)
		{
			p.ReloadMt -= scale;
			if (p.ReloadMt <= 0) { p.ReloadMt = 0; p.Mag = weapon.Magazine; }
		}

		if (input.Reload && p.ReloadMt <= 0 && p.Mag < weapon.Magazine)
		{
			p.ReloadMt = weapon.ReloadTicks * Actor.Mt;
			Log.Add(SimEventKind.Reload, p.X, p.Y);
		}

		// The barrel spools while the trigger is held and spools down faster
		// when it is not. A weapon with no spin-up pins this at zero, so every
		// other weapon behaves exactly as it did.
		int spinCap = weapon.SpinUpTicks * Actor.Mt;
		if (spinCap <= 0)
		{
			p.SpinMt = 0;
		}
		else if (input.Fire)
		{
			p.SpinMt += scale;
			if (p.SpinMt > spinCap) p.SpinMt = spinCap;
		}
		else
		{
			p.SpinMt -= (int)(((long)scale * Tune.SpinDownQ8) >> Fx.Shift);
			if (p.SpinMt < 0) p.SpinMt = 0;
		}

		if (input.Subdue) TrySubdue();
		if (input.Fire) Fire();

		StepCaches(scale);

		if (Geometry.InRect(p.X, p.Y, in Level.Exit))
		{
			Over = "out";
			Log.Add(SimEventKind.Exit, p.X, p.Y);
		}
	}

	/// <summary>
	/// Advance one shooter's aim sway. Sway is an ENVELOPE: it rises straight to
	/// whatever is disturbing the shooter this tick — how far the weapon swung,
	/// how far they moved — and decays back down once they settle. Holding still
	/// therefore tightens the cone and swinging or walking opens it, with no
	/// accumulator to peg or stall.
	///
	/// Shared by the player and by guards on purpose: the firing error is the
	/// same mechanic on both sides, and a guard who has just spun to face you
	/// misses for exactly the reason you do.
	/// </summary>
	private static void StepSway(Actor a, int turnedBrad, int movedFx, int scale,
		int floorQ8 = 0)
	{
		if (turnedBrad < 0) turnedBrad = -turnedBrad;

		int disturb = turnedBrad * Tune.SwayMax / Tune.SwayTurnFullBrad
			+ ((movedFx * Tune.SwayPerMovedQ8) >> Fx.Shift);
		// A floor, not an addition: a sprinting player's weapon is disturbed by
		// the sprint whether or not they are also turning.
		if (disturb < floorQ8) disturb = floorQ8;
		if (disturb > Tune.SwayMax) disturb = Tune.SwayMax;

		int settled = a.SwayQ8 - Fx.PerTick(Tune.SwayDecayPerSec, scale);
		a.SwayQ8 = settled > disturb ? settled : disturb;
		if (a.SwayQ8 < 0) a.SwayQ8 = 0;
	}

	/// <summary>
	/// Track what the crosshair is held on. Holding one target for
	/// <see cref="Tune.AimLockTicks"/> makes the next round a headshot.
	///
	/// The lock is deliberately fragile: it drops the moment the target changes,
	/// breaks line of sight, leaves the cone, or the aim button is released. It
	/// is meant to reward patient, committed aim, not to be a passive bonus.
	/// </summary>
	private void StepAim(in InputFrame input, int scale)
	{
		var p = Player;
		bool wasReady = p.HeadshotReady;
		int previous = p.AimTarget;

		p.Aiming = input.Aim;

		// Coming out of a sprint the weapon is not on target yet, so there is
		// nothing to hold a lock with. This is what "you have to re-aim" means
		// in practice: the 0.5 s lock clock cannot even start until the 0.45 s
		// recovery has run.
		if (!input.Aim || p.ReadyMt > 0)
		{
			if (previous >= 0) Log.Add(SimEventKind.AimLost, p.X, p.Y);
			p.AimTarget = -1;
			p.AimLockMt = 0;
			return;
		}

		// Most centred living guard inside the cone, with a clear line. Guards
		// iterate by index so ties resolve the same way every run.
		int best = -1;
		int bestOffset = int.MaxValue;
		for (int i = 0; i < Guards.Count; i++)
		{
			var e = Guards[i];
			if (e.Prone || !e.Alive) continue;

			int dist = Fx.Dist(e.X, e.Y, p.X, p.Y);
			if (dist > Tune.AimLockRange) continue;

			int toTarget = Brad.Atan2(e.Y - p.Y, e.X - p.X);
			int offset = Brad.Norm(toTarget - p.Facing);
			if (offset < 0) offset = -offset;
			if (offset > Tune.AimLockCone) continue;

			if (!Geometry.ClearLine(Opaque, p.X, p.Y, e.X, e.Y)) continue;

			if (offset < bestOffset) { bestOffset = offset; best = i; }
		}

		if (best < 0)
		{
			if (previous >= 0) Log.Add(SimEventKind.AimLost, p.X, p.Y);
			p.AimTarget = -1;
			p.AimLockMt = 0;
			return;
		}

		if (best != previous)
		{
			// Switching targets starts the clock again from zero.
			if (previous >= 0) Log.Add(SimEventKind.AimLost, p.X, p.Y);
			p.AimTarget = best;
			p.AimLockMt = 0;
			// Deliberately NOT returning. The tick that acquires a target is
			// also the first tick of its clock, so a lock takes exactly
			// AimLockTicks. Returning here spent one tick acquiring before the
			// clock started, which made the lock silently take 91 ticks where
			// both the constant and its test said 90.
		}

		p.AimLockMt += scale;
		int cap = Tune.AimLockTicks * Actor.Mt;
		if (p.AimLockMt > cap) p.AimLockMt = cap;

		if (!wasReady && p.HeadshotReady)
			Log.Add(SimEventKind.AimLocked, Guards[best].X, Guards[best].Y, 0, best);
	}

	private void StepCaches(int scale)
	{
		var p = Player;
		for (int i = 0; i < Caches.Count; i++)
		{
			var c = Caches[i];
			if (c.Taken) continue;
			if (Fx.Dist(c.X, c.Y, p.X, p.Y) < Tune.CacheReach)
			{
				c.DwellMt += scale;
				if (c.DwellMt > Tune.CacheDwellTicks * Actor.Mt)
				{
					c.Taken = true;
					Records.Take(c.Contents);
					Log.Add(SimEventKind.Pickup, c.X, c.Y, 0, c.Contents.Count);
					c.Contents = new List<Record>();
				}
			}
			else if (c.DwellMt > 0)
			{
				c.DwellMt -= scale;
				if (c.DwellMt < 0) c.DwellMt = 0;
			}
		}
	}

	/// <summary>
	/// Salt for the loot stream. Any constant would do; what matters is that it
	/// is fixed, so the same seed always yields the same kits.
	/// </summary>
	private const ulong LootSalt = 0x9E3779B97F4A7C15UL;

	/// <summary>
	/// What each guard is carrying: a POINT BUY (LootTable.KitGuard) with the
	/// points the level assigns him (Level.PointsFor). Index order, never a
	/// hash-ordered collection (spec §3.3), from the loot stream, so the same
	/// seed dresses the same floor. The vest he buys is the vest he WEARS.
	/// </summary>
	private void RollGuardKits()
	{
		for (int i = 0; i < Guards.Count; i++)
			LootTable.KitGuard(LootRng, Guards[i], Level.PointsFor(Guards[i].Id));
	}

	/// <summary>
	/// This run's luck, percent (Tune.LuckMin .. +2*LuckSpread). Rolled AFTER the
	/// guards so it cannot move a body, and before the chests it tilts.
	/// </summary>
	public int Luck { get; private set; } = LootTable.NeutralLuck;

	/// <summary>
	/// What is in the chests: the level's budget (Level.ChestBudget) spread
	/// across them, spent by rarity weights that this run's luck tilts. The
	/// money on the floor is fixed by the level; luck decides whether it comes
	/// as one legendary or a drawer of commons.
	/// </summary>
	private void RollChests()
	{
		Luck = LootTable.RollLuck(LootRng);
		LootTable.StockChests(LootRng, Chests, Level.ChestBudget, Luck);
	}

	/// <summary>
	/// Bodies and chests in ONE index space: guards first, then chests. Both are
	/// looted through the same panel and the same InputFrame.LootPick, so giving
	/// them one addressing scheme means the loot path has no idea which it is
	/// working on — and no second copy of itself for the other case.
	/// </summary>
	public int LootTargetCount => Guards.Count + Chests.Count + Ground.Count;

	public bool TryLootTarget(int index, out int x, out int y, out List<int>? kit)
	{
		x = 0; y = 0; kit = null;
		if (index < 0) return false;

		if (index < Guards.Count)
		{
			var g = Guards[index];
			if (!g.Prone) return false;       // only a fallen guard can be stripped
			x = g.X; y = g.Y; kit = g.Kit;
			return true;
		}

		int c = index - Guards.Count;
		if (c < Chests.Count)
		{
			var chest = Chests[c];
			x = chest.X; y = chest.Y; kit = chest.Kit;
			return true;
		}

		int p = c - Chests.Count;
		if (p >= Ground.Count) return false;
		var pile = Ground[p];
		x = pile.X; y = pile.Y; kit = pile.Kit;
		return true;
	}

	/// <summary>The nearest body or chest within reach that still holds
	/// something, or -1.</summary>
	public int NearestLootTarget()
	{
		var p = Player;
		int best = -1, bestDist = 0;
		for (int i = 0; i < LootTargetCount; i++)
		{
			if (!TryLootTarget(i, out int x, out int y, out var kit)) continue;
			if (kit == null || kit.Count == 0) continue;
			int d = Fx.Dist(x, y, p.X, p.Y);
			if (d >= Tune.LootReach) continue;
			if (PanelBetween(p.X, p.Y, x, y)) continue;
			if (best < 0 || d < bestDist) { best = i; bestDist = d; }
		}
		return best;
	}

	/// <summary>
	/// Holster one weapon and draw the other. Deliberately not instant: the
	/// swap window is the cost of carrying two guns, and firing is blocked for
	/// its duration the way it is during a reload.
	/// </summary>
	private void StepSwap(in InputFrame input, int scale)
	{
		var p = Player;

		if (p.SwapMt > 0)
		{
			p.SwapMt -= scale;
			if (p.SwapMt > 0) return;

			p.SwapMt = 0;
			Loadout = Loadout.Swapped();

			// Each weapon keeps its own magazine, so the counts trade places.
			int stowed = p.MagStowed;
			p.MagStowed = p.Mag;
			p.Mag = stowed;

			// A swap interrupts a reload rather than surviving it.
			p.ReloadMt = 0;
			Log.Add(SimEventKind.WeaponSwapped, p.X, p.Y, p.Facing, Loadout.ActiveIndex);
			return;
		}

		if (!input.Swap) return;
		if (!Loadout.HasSecondary) return;
		p.SwapMt = Tune.SwapTicks * Actor.Mt;
	}

	/// <summary>
	/// Take ONE named item off the body in reach. The player picks it out of that
	/// guard's kit by clicking it, and input.LootPick carries the choice, so the
	/// decision is recorded and a replay reproduces exactly what was taken.
	///
	/// There is no dwell any more. Looting used to strip one item per second for
	/// as long as the key was held, which meant the player never chose anything:
	/// the sim decided the order and the only input was patience. The cost of
	/// looting is now the time spent stood over a body with the world still
	/// running, which is the same cost, paid attentively.
	///
	/// The body is chosen the same way the panel chooses it -- nearest prone guard
	/// in reach, ties to the lower index -- so what the player clicked cannot
	/// disagree with what this takes.
	/// </summary>
	/// <summary>
	/// Put one item from the pack on the floor.
	///
	/// Driven by InputFrame.DropPick rather than by the screen that shows the
	/// pack, because this moves sim state: the item leaves the pack and enters
	/// the world, and a replay has to reproduce both. Piles merge within
	/// Tune.DropMergeDist so emptying a pack leaves one heap, not six.
	/// </summary>
	private void StepDrop(in InputFrame input)
	{
		if (input.DropPick == 0) return;

		var p = Player;
		if (!p.Alive || Over != null) return;

		int pi = input.DropPick - 1;
		if (!Pack.IsLive(pi)) return;

		int itemId = Pack.ItemOf(pi);
		if (!Pack.Remove(pi)) return;

		// Index order, never a hash-ordered scan: the first pile in range wins,
		// and "first" has to mean the same thing on every machine.
		for (int i = 0; i < Ground.Count; i++)
		{
			if (Fx.Dist(Ground[i].X, Ground[i].Y, p.X, p.Y) < Tune.DropMergeDist)
			{
				Ground[i].Kit.Add(itemId);
				Log.Add(SimEventKind.Dropped, Ground[i].X, Ground[i].Y, 0, itemId);
				return;
			}
		}

		var pile = new GroundPile { X = p.X, Y = p.Y };
		pile.Kit.Add(itemId);
		Ground.Add(pile);
		Log.Add(SimEventKind.Dropped, p.X, p.Y, 0, itemId);
	}

	/// <summary>
	/// Put something from the pack ON, mid-mission: the rifle you just stripped
	/// off a body becomes the rifle in your hands.
	///
	/// Driven by InputFrame.EquipPick rather than by the screen showing the
	/// pack, because this moves SimWorld.Loadout — damage, spread, speed and
	/// magazine size all change, and every one of them feeds the hash.
	///
	/// It is a TRADE, never a gain. The displaced weapon or vest goes into the
	/// pack as an item, and if there is no room for it the equip is refused
	/// whole. That is what stops the screen being a way to conjure space, and it
	/// is why the new item is lifted out BEFORE the old one is put back: doing
	/// it the other way round fails whenever the pack is nearly full, even
	/// though the trade would have fitted.
	///
	/// EVERY worn slot can be changed in the field EXCEPT the weapon sub-slots.
	/// ATTACHMENTS stay fixed at deploy: one set applies to whichever weapon is
	/// held (see the simplification noted on Loadout), so fitting one mid-run is
	/// not the local change it looks like.
	///
	/// The BACKPACK is a special case handled by EquipBackpack: it is the
	/// container the rest of the pack lives in, so it is test-fitted before it
	/// is committed and refused whole when what you carry will not fit.
	/// </summary>
	private void StepEquip(in InputFrame input)
	{
		if (input.EquipPick == 0) return;

		var p = Player;
		if (!p.Alive || Over != null) return;

		// Not in the middle of drawing the other weapon: the swap is holding
		// Mag and MagStowed apart, and landing an equip in that window would
		// have to guess which magazine it was replacing.
		if (p.SwapMt > 0) return;

		int pi = input.EquipPlacement;
		if (pi < 0 || !Pack.IsLive(pi)) return;

		int itemId = Pack.ItemOf(pi);
		if (!GearCatalog.Exists(itemId)) return;
		var item = GearCatalog.Get(itemId);

		int slot = input.EquipSlot;
		if (item.Kind == GearKind.Weapon)
		{
			if (slot != (int)GearSlot.Primary && slot != (int)GearSlot.Secondary) return;
			EquipWeapon(pi, itemId, item, slot == (int)GearSlot.Secondary);
			return;
		}

		if (item.Kind == GearKind.Armour && slot == (int)GearSlot.Vest)
		{
			EquipArmour(pi, itemId, item);
			return;
		}

		if (item.Kind == GearKind.Pack && slot == (int)GearSlot.Backpack)
		{
			EquipBackpack(pi, itemId, item);
			return;
		}

		// An ATTACHMENT names its own destination: SimA is the AttachSlot and
		// SimB the option, so the GearSlot the player dropped it on is ignored
		// rather than second-guessed. Fitting one mid-mission used to be
		// refused outright; it is a trade like every other equip.
		if (item.Kind == GearKind.Attachment)
		{
			// It still has to be dropped on a WEAPON. The item names which
			// sub-slot it fills, but "anywhere at all" would mean fitting a
			// scope by dropping it on your boots -- and the screen only offers
			// the weapon slots, so accepting more here would be the tick and
			// the screen disagreeing.
			if (slot != (int)GearSlot.Primary && slot != (int)GearSlot.Secondary)
				return;
			EquipAttachment(pi, itemId, item);
			return;
		}

		// Apparel goes in its OWN slot and no other. These change no stat, but
		// they are worn, recorded and hashed, so that gear which can be looted
		// always has somewhere to go.
		if (item.Kind == GearKind.Apparel && slot == (int)item.Slot)
		{
			EquipApparel(pi, itemId, (GearSlot)slot);
			return;
		}
	}

	/// <summary>
	/// Fit an attachment to the weapon in hand, mid-mission.
	///
	/// Onto the weapon IN HAND and no other: the rails are per weapon now, so
	/// a scope fitted while holding the pistol is on the PISTOL, and the rifle
	/// in the holster keeps whatever is on its own. Masked by that weapon's
	/// slots, so fitting a stock to something that cannot take one is allowed
	/// and simply does nothing — the behaviour the stash screen has always
	/// had, now available in the field.
	/// </summary>
	private void EquipAttachment(int pi, int itemId, in GearItem item)
	{
		var p = Player;
		var slot = (AttachSlot)item.SimA;

		// What is fitted there now, as an item to put back.
		int oldItem = GearCatalog.AttachmentItemId(item.SimA, Loadout.Attachment(slot));

		if (!Pack.Remove(pi)) return;

		if (oldItem != 0 && Pack.AutoPlace(oldItem) == PackGrid.None)
		{
			Pack.AutoPlace(itemId);
			Log.Add(SimEventKind.PackFull, p.X, p.Y, 0, oldItem);
			return;
		}

		Loadout = Loadout.WithAttachment(slot, item.SimB);

		// A smaller magazine holds fewer rounds. Swapping a drum for an
		// extended mag used to leave the drum's count loaded in a gun that now
		// takes eight -- Fuzz caught an Arc Lance holding eleven. The extra
		// rounds go with the magazine that came off.
		if (p.Mag > Loadout.Spec.Magazine) p.Mag = Loadout.Spec.Magazine;

		// Fitting glass to a gun is not instant, and it must not be free in the
		// middle of a firefight: it interrupts a reload the way a swap does.
		p.ReloadMt = 0;

		Log.Add(SimEventKind.Equipped, p.X, p.Y, p.Facing, itemId);
	}

	private void EquipApparel(int pi, int itemId, GearSlot slot)
	{
		var p = Player;
		int oldItem = Loadout.ApparelIn(slot);

		if (!Pack.Remove(pi)) return;

		if (oldItem != 0 && Pack.AutoPlace(oldItem) == PackGrid.None)
		{
			Pack.AutoPlace(itemId);
			Log.Add(SimEventKind.PackFull, p.X, p.Y, 0, oldItem);
			return;
		}

		Loadout = Loadout.WithApparel(slot, itemId);
		Log.Add(SimEventKind.Equipped, p.X, p.Y, p.Facing, itemId);
	}

	/// <summary>
	/// Change the BAG, mid-mission.
	///
	/// This is the one equip that cannot be a simple trade, because the thing
	/// being swapped is the container everything else is in: the grid changes
	/// size and every item has to be re-placed. A bigger bag is easy; a smaller
	/// one may simply not hold what is already being carried.
	///
	/// So it is TEST-FITTED FIRST, into a scratch grid, and only committed when
	/// everything fits. Mutating the real pack and unwinding on failure would
	/// mean reconstructing positions that no longer exist; this way the pack is
	/// either untouched or wholly valid, and never half re-gridded.
	///
	/// The commit replays the same items in the same order into the same
	/// dimensions, and AutoPlace is deterministic, so it lands exactly where the
	/// test fit said it would.
	/// </summary>
	private void EquipBackpack(int pi, int itemId, in GearItem item)
	{
		var p = Player;
		int oldItem = Loadout.Backpack;
		if (oldItem == itemId) return;

		// Everything that will have to live in the new bag: what is in the pack
		// now, minus the bag being put on, plus the bag coming off. Collected by
		// PLACEMENT INDEX so the order is the same on every machine.
		var carry = new List<int>();
		for (int i = 0; i < Pack.Capacity; i++)
			if (Pack.IsLive(i) && i != pi) carry.Add(Pack.ItemOf(i));
		if (oldItem != 0) carry.Add(oldItem);

		var scratch = new PackGrid(item.PackW, item.PackH);
		for (int i = 0; i < carry.Count; i++)
		{
			if (scratch.AutoPlace(carry[i]) != PackGrid.None) continue;
			// Refused WHOLE. Nothing has been touched yet, which is the point
			// of fitting into scratch rather than into the real pack.
			Log.Add(SimEventKind.PackFull, p.X, p.Y, 0, carry[i]);
			return;
		}

		Pack.Resize(item.PackW, item.PackH);
		for (int i = 0; i < carry.Count; i++) Pack.AutoPlace(carry[i]);

		Loadout = Loadout.WithBackpack(itemId);
		Log.Add(SimEventKind.Equipped, p.X, p.Y, p.Facing, itemId);
	}

	private void EquipWeapon(int pi, int itemId, in GearItem item, bool toHolster)
	{
		var p = Player;
		var want = WeaponCatalog.Clamp(item.SimA);

		// The weapon coming off, as an item to put back. A holster that was
		// EMPTY displaces nothing, which is the one case where this is a pure
		// gain -- and it is a gain the player already paid for by carrying the
		// weapon in the pack.
		bool hadHolster = Loadout.HasSecondary;
		int oldWeapon = toHolster
			? (hadHolster ? (int)Loadout.Secondary : -1)
			: (int)Loadout.Weapon;
		int oldItem = oldWeapon < 0 ? 0 : GearCatalog.WeaponItemId(oldWeapon);

		if (!Pack.Remove(pi)) return;

		if (oldItem != 0 && Pack.AutoPlace(oldItem) == PackGrid.None)
		{
			// Put it back exactly as it was found. AutoPlace rather than the old
			// cell because the cell is gone; the pack had room for it a moment
			// ago, so this cannot fail.
			Pack.AutoPlace(itemId);
			Log.Add(SimEventKind.PackFull, p.X, p.Y, 0, oldItem);
			return;
		}

		Loadout = toHolster ? Loadout.WithSecondary((int)want) : Loadout.WithWeapon(want);

		// The magazine comes EMPTY. A weapon that arrived loaded would make
		// equipping a free instant reload -- there is no ammo pool, so the only
		// cost a reload has is the time it takes, and handing that back would
		// make re-equipping strictly better than reloading.
		//
		// WHICH magazine to clear is the one thing here that is easy to get
		// wrong: Mag belongs to the weapon in hand and MagStowed to the other,
		// so the question is whether the slot just replaced is the held one.
		bool replacedHeld = (Loadout.ActiveIndex == 1) == toHolster;
		if (replacedHeld) p.Mag = 0; else p.MagStowed = 0;

		// A reload only dies if it was THIS weapon being reloaded. Re-arming the
		// holster must not interrupt the rifle in your hands.
		if (replacedHeld) p.ReloadMt = 0;

		Log.Add(SimEventKind.Equipped, p.X, p.Y, p.Facing, itemId);
	}

	private void EquipArmour(int pi, int itemId, in GearItem item)
	{
		var p = Player;
		int oldItem = GearCatalog.ArmourItemId((int)Loadout.Armour);

		if (!Pack.Remove(pi)) return;

		if (oldItem != 0 && Pack.AutoPlace(oldItem) == PackGrid.None)
		{
			Pack.AutoPlace(itemId);
			Log.Add(SimEventKind.PackFull, p.X, p.Y, 0, oldItem);
			return;
		}

		Loadout = Loadout.WithArmour(ArmourCatalog.Clamp(item.SimA));

		// A vest off a body comes WHOLE. The plate the player was wearing may
		// have been half shot away; that damage goes with it into the pack and
		// is forgotten, because a pack holds items, not their condition.
		p.Armour = Loadout.ArmourSpec.Armour;

		Log.Add(SimEventKind.Equipped, p.X, p.Y, p.Facing, itemId);
	}

	/// <summary>
	/// The developer menu's mid-run spawn: an item appears in the pack out of
	/// nothing.
	///
	/// It lives here, in the tick, rather than in the menu that asked for it,
	/// for the same reason dropping does -- the pack feeds the state hash and
	/// rides in the replay, so an item that arrived behind the sim's back would
	/// diverge every replay from that tick on. Routed through the same
	/// Pack.AutoPlace as looting, so a spawn obeys the pack's geometry exactly
	/// like a looted item: conjuring gear is allowed, conjuring ROOM is not.
	/// </summary>
	private void StepSpawn(in InputFrame input)
	{
		if (input.SpawnItem == 0) return;

		var p = Player;
		if (!p.Alive || Over != null) return;

		// A total guard, like the parsers: an id from a corrupt replay or a
		// catalogue that has since changed is ignored rather than fatal.
		int itemId = input.SpawnItem;
		if (!GearCatalog.Exists(itemId)) return;

		if (Pack.AutoPlace(itemId) == PackGrid.None)
		{
			Log.Add(SimEventKind.PackFull, p.X, p.Y, 0, itemId);
			return;
		}

		Log.Add(SimEventKind.Spawned, p.X, p.Y, 0, itemId);
	}

	private void StepLoot(in InputFrame input)
	{
		if (input.LootPick == 0) return;

		int target = NearestLootTarget();
		if (target < 0) return;
		if (!TryLootTarget(target, out int tx, out int ty, out var kit)) return;
		if (kit == null) return;

		int index = input.LootPick - 1;
		// A stale pick -- the kit shrank since the click -- is ignored rather than
		// grabbing whatever slid into that position.
		if (index < 0 || index >= kit.Count) return;

		int itemId = kit[index];
		if (Pack.AutoPlace(itemId) == PackGrid.None)
		{
			Log.Add(SimEventKind.PackFull, tx, ty, 0, itemId);
			return;
		}

		Log.Add(SimEventKind.Looted, tx, ty, 0, itemId);
		kit.RemoveAt(index);
	}

	/// <summary>
	/// Total cone WIDTH the next round will be drawn from; a projectile takes
	/// +/- half of it (ruling #4). Three terms: the weapon's base, what
	/// SUSTAINED FIRE has done to it, and how badly it is being SWUNG or carried
	/// right now. Aiming scales the whole thing, so bracing pays off most
	/// exactly when the other two terms are large.
	///
	/// One function, called by both the shot and the snapshot, so the reticle
	/// and the bullet can never disagree about how accurate the player is.
	/// </summary>
	private int PlayerSpreadWidth()
	{
		var p = Player;
		var w = Loadout.Spec;

		int width = w.SpreadBase
			+ (int)(((long)p.Heat * w.SpreadPerHeat) >> Fx.Shift)
			+ (int)(((long)p.SwayQ8 * w.SpreadPerSway) >> Fx.Shift);

		if (p.Aiming) width = (int)(((long)width * w.AimSpreadQ8) >> Fx.Shift);
		return width;
	}

	private void Fire()
	{
		var p = Player;
		var w = Loadout.Spec;
		if (p.CooldownMt > 0 || p.ReloadMt > 0 || p.SwapMt > 0) return;

		// Still spooling. Costs no ammunition and starts no cooldown: the
		// trigger is held and nothing is happening yet, which is the whole
		// point of a spin-up.
		if (w.SpinUpTicks > 0 && p.SpinMt < w.SpinUpTicks * Actor.Mt) return;

		if (p.Mag <= 0)
		{
			p.CooldownMt = w.DryFireTicks * Actor.Mt;
			Log.Add(SimEventKind.DryFire, p.X, p.Y, p.Facing);
			return;
		}

		p.Mag--;
		p.CooldownMt = w.FireCooldownTicks * Actor.Mt;
		Shots++;
		p.RecoilQ8 = Fx.One;
		p.Heat += w.HeatPerShot;
		if (p.Heat > Tune.HeatMax) p.Heat = Tune.HeatMax;

		int width = PlayerSpreadWidth();

		if (w.Grenade)
		{
			ThrowGrenade(in w, width);
			return;
		}

		Brad.SinCos(p.Facing, out int sin, out int cos);
		int mx = p.X + (int)(((long)w.MuzzleOffset * cos) >> Brad.UnitShift);
		int my = p.Y + (int)(((long)w.MuzzleOffset * sin) >> Brad.UnitShift);

		// A held aim lock rides on the FIRST projectile only, so a shotgun blast
		// cannot turn into seven headshots.
		bool headshot = p.HeadshotReady;
		if (headshot)
		{
			p.AimLockMt = 0;
			Log.Add(SimEventKind.Headshot, p.X, p.Y, p.Facing);
		}

		// One draw per pellet, always in the same order, so a shotgun blast is
		// as reproducible as a pistol shot.
		//
		// THE CHOKE. A single projectile draws freely from the whole cone, as it
		// always has. Several do not: the cone is cut into one slice per pellet
		// and each pellet is drawn inside its own slice, so a blast arrives as a
		// PATTERN rather than seven independent dice. Seven free draws clump —
		// the same shell could miss a man at ten paces and shred him at twenty —
		// and no amount of narrowing the cone fixes that, because the clumping is
		// in the draw, not the width.
		int pellets = w.Pellets < 1 ? 1 : w.Pellets;
		int slice = pellets > 1 ? width / pellets : 0;
		int first = -(slice * (pellets - 1)) / 2;
		int jitter = pellets > 1 ? (slice * Tune.ChokeJitterQ8) >> Fx.Shift : width / 2;

		// The lock rides on the CENTRE pellet, not the edge of the pattern, so a
		// held aim still puts the headshot where the player was looking.
		int centre = pellets / 2;

		for (int i = 0; i < pellets; i++)
		{
			int heading = (p.Facing + first + slice * i + Rng.NextSigned(jitter)) & Brad.Mask;
			var b = Bullets.Spawn(mx, my, heading, w.BulletSpeed, w.BulletTicks, true,
				w.Damage, w.ArmourPierce, headshot && i == centre);
			if (w.ArcTargets > 0) { b.Kind = BulletKind.Arc; b.Arc = w.ArcTargets; }
			else if (w.WallPierce > 0) { b.Kind = BulletKind.Pierce; b.WallsLeft = w.WallPierce; }
		}

		Log.Add(SimEventKind.PlayerShot, mx, my, p.Facing, pellets);

		GunshotHeard(p.X, p.Y, w.GunshotRadius);
	}

	/// <summary>
	/// Throw a grenade. It leaves from the player's CENTRE, not the muzzle: a
	/// muzzle can be inside the wall you are standing against, and a grenade
	/// born inside a wall would bounce in place until it went off in your hand.
	///
	/// Aim held is an underhand LOB at <see cref="Tune.GrenadeLobQ8"/> of the
	/// throw -- the one control over range a thrown weapon has, since aim is a
	/// heading and not a point. A held aim lock is not spent on it; there is no
	/// such thing as a grenade headshot.
	/// </summary>
	private void ThrowGrenade(in WeaponSpec w, int width)
	{
		var p = Player;
		int heading = (p.Facing + Rng.NextSigned(width / 2)) & Brad.Mask;
		int speed = p.Aiming
			? (int)(((long)w.BulletSpeed * Tune.GrenadeLobQ8) >> Fx.Shift)
			: w.BulletSpeed;
		var b = Bullets.Spawn(p.X, p.Y, heading, speed, w.BulletTicks, true,
			w.Damage, w.ArmourPierce);
		b.Kind = BulletKind.Grenade;
		Log.Add(SimEventKind.GrenadeThrown, p.X, p.Y, heading);
		GunshotHeard(p.X, p.Y, w.GunshotRadius);
	}

	/// <summary>
	/// A grenade going off: <see cref="Tune.FragCount"/> fragments laid evenly
	/// round the circle, each jittered inside its own slice -- the Remington's
	/// choke, all the way round. Fragments are ordinary projectiles of kind
	/// Frag, so walls stop them, glass shatters, and they hit whoever is there.
	///
	/// HEARD, and heard AT THE BLAST: GunshotHeard sends every guard in earshot
	/// to the blast point. They were not told who threw it.
	/// </summary>
	private void Blast(int x, int y, int damage, int pierce)
	{
		int n = Tune.FragCount;
		int slice = Brad.Full / n;
		int jitter = (slice * Tune.FragJitterQ8) >> Fx.Shift;
		for (int i = 0; i < n; i++)
		{
			int heading = (slice * i + Rng.NextSigned(jitter)) & Brad.Mask;
			var f = Bullets.Spawn(x, y, heading, Tune.FragSpeed, Tune.FragTicks, true,
				damage, pierce);
			f.Kind = BulletKind.Frag;
		}
		Clocks.RequestHitstop(Tune.HitstopGuardTicks);
		Log.Add(SimEventKind.Blast, x, y, 0, n);
		GunshotHeard(x, y, Tune.BlastHeardRadius);
	}

	/// <summary>
	/// A lightning bolt has struck at (x, y): a guard (<paramref name="first"/>)
	/// or, when that is -1, a wall.
	///
	/// The chain, one node at a time, up to <paramref name="targets"/> bodies:
	///   1. THE SHOOTER FIRST. If the player is within
	///      <see cref="Tune.ArcPlayerReach"/> of the current node with a clear
	///      line to it, the arc goes to them and the chain ends there. Checked
	///      before any guard, so being close is never made safe by there
	///      happening to be a guard closer still.
	///   2. A wall strike GROUNDS: it can come back to you, but it cannot find
	///      a guard. A miss is a miss.
	///   3. Otherwise the nearest standing guard within
	///      <see cref="Tune.ArcReach"/>, clear line, ties to the lower index.
	///
	/// Every node is logged in order (ArcJump, Value = hop), and every body it
	/// reaches takes the bolt's full damage and pierce -- which is to say it
	/// dies. Walls are Opaque: a shut door stops an arc, glass does not.
	/// </summary>
	private void Discharge(int x, int y, int first, int targets, int damage, int pierce,
		int heading)
	{
		var p = Player;
		int hop = 0;
		Log.Add(SimEventKind.ArcJump, x, y, 0, hop);

		int left = targets;
		int cx = x, cy = y;
		if (first >= 0)
		{
			HurtGuard(Guards[first], damage, pierce, heading);
			left--;
		}

		while (left > 0)
		{
			if (p.Alive && Over == null
				&& Fx.Dist(p.X, p.Y, cx, cy) < Tune.ArcPlayerReach
				&& Geometry.ClearLine(Opaque, cx, cy, p.X, p.Y))
			{
				Log.Add(SimEventKind.ArcJump, p.X, p.Y, 1, ++hop);
				HurtPlayer(Tune.ArcSelfDamage, Fx.One, Brad.Atan2(p.Y - cy, p.X - cx));
				return;
			}

			if (first < 0) return;

			int best = -1, bestDist = 0;
			for (int i = 0; i < Guards.Count; i++)
			{
				var e = Guards[i];
				if (e.Prone || !e.Alive) continue;
				int d = Fx.Dist(e.X, e.Y, cx, cy);
				if (d >= Tune.ArcReach) continue;
				if (!Geometry.ClearLine(Opaque, cx, cy, e.X, e.Y)) continue;
				if (best < 0 || d < bestDist) { best = i; bestDist = d; }
			}
			if (best < 0) return;

			var g = Guards[best];
			int jump = Brad.Atan2(g.Y - cy, g.X - cx);
			Log.Add(SimEventKind.ArcJump, g.X, g.Y, 0, ++hop);
			HurtGuard(g, damage, pierce, jump);
			// A body that somehow survived the bolt is not a conductor for the
			// rest of it: without this the chain could bounce between two.
			if (!g.Prone) return;
			cx = g.X; cy = g.Y;
			left--;
		}
	}

	private void TrySubdue()
	{
		var p = Player;
		for (int i = 0; i < Guards.Count; i++)
		{
			var e = Guards[i];
			if (e.State == GuardState.Dead) continue;
			if (Fx.Dist(e.X, e.Y, p.X, p.Y) > Tune.SubdueReach) continue;
			if (PanelBetween(p.X, p.Y, e.X, e.Y)) continue;

			// Already down: this is a search, not a takedown.
			if (e.State == GuardState.Down)
			{
				if (e.Carried.Count > 0)
				{
					Records.Take(e.Carried);
					Log.Add(SimEventKind.Pickup, e.X, e.Y, 0, e.Carried.Count);
					e.Carried = new List<Record>();
				}
				return;
			}

			// From behind only: the player must be outside the guard's front arc.
			int toPlayer = Brad.Atan2(p.Y - e.Y, p.X - e.X);
			int off = Brad.Norm(toPlayer - e.Facing);
			if (off < 0) off = -off;
			if (off < Tune.SubdueRearArc) continue;
			// A guard shooting at you is facing you; one keying a radio is
			// not, and taking him mid-call is the counterplay (Guard_AI.md §5.3).
			if (e.State == GuardState.Combat && e.Task == GuardTask.Engage && !e.Afraid) continue;

			e.State = GuardState.Down;
			OnGuardDown(e, false);
			e.DeadFacing = e.Facing;
			e.DeadRoll = Rng.NextSigned(Tune.DeadRollMax);
			Subdues++;
			Log.Add(SimEventKind.Subdue, e.X, e.Y, e.Facing);

			for (int o = 0; o < Guards.Count; o++)
			{
				var other = Guards[o];
				if (other == e || other.Prone) continue;
				if (Fx.Dist(other.X, other.Y, e.X, e.Y) < Tune.SubdueRange)
					Notice(other, e.X, e.Y, Tune.SubdueAw);
			}

			Records.Take(e.Carried);
			Log.Add(SimEventKind.Pickup, e.X, e.Y, 0, e.Carried.Count);
			e.Carried = new List<Record>();
			return;
		}
	}

	// =====================================================================
	// Glass and doors
	// =====================================================================

	/// <summary>
	/// Re-derive what blocks what from the panels' state. Called at build and
	/// after any panel changes; never per tick otherwise. With no panels, Solid
	/// and Opaque are Level.Walls itself, so a level that uses neither glyph
	/// behaves -- and hashes -- exactly as it did before the feature existed.
	/// </summary>
	private void RebuildBlockers()
	{
		_blockersDirty = false;
		var walls = Level.Walls;
		if (Panels.Count == 0)
		{
			Solid = walls;
			Opaque = walls;
			return;
		}

		var solid = new List<Rect>(walls.Length + Panels.Count);
		var opaque = new List<Rect>(walls.Length + Panels.Count);
		solid.AddRange(walls);
		opaque.AddRange(walls);

		int glass = 0;
		for (int i = 0; i < Panels.Count; i++)
		{
			var pn = Panels[i];
			if (pn.Solid) solid.Add(pn.Rect);
			if (pn.Opaque) opaque.Add(pn.Rect);
			if (pn.IsGlass) glass++;
		}
		Solid = solid.ToArray();
		Opaque = opaque.ToArray();

		if (_glassRects.Length != glass)
		{
			_glassRects = new Rect[glass];
			_glassPanel = new int[glass];
			_glassIntact = new bool[glass];
		}
		int k = 0;
		for (int i = 0; i < Panels.Count; i++)
		{
			if (!Panels[i].IsGlass) continue;
			_glassRects[k] = Panels[i].Rect;
			_glassPanel[k] = i;
			_glassIntact[k] = !Panels[i].Open;
			k++;
		}
	}

	/// <summary>
	/// The nearest door within <see cref="Tune.DoorReach"/> of the player, open
	/// or shut, or -1. Ties go to the lower index. What the G prompt shows, and
	/// the only door <see cref="StepDoor"/> will move.
	/// </summary>
	public int NearestDoor()
	{
		var p = Player;
		int best = -1, bestDist = 0;
		for (int i = 0; i < Panels.Count; i++)
		{
			var pn = Panels[i];
			if (!pn.IsDoor) continue;
			int d = Geometry.DistToRect(p.X, p.Y, in pn.Rect);
			if (d > Tune.DoorReach) continue;
			if (best < 0 || d < bestDist) { best = i; bestDist = d; }
		}
		return best;
	}

	/// <summary>Distance from the player to a panel, fixed-point px, or -1 for
	/// an index that is not one.</summary>
	public int DistToPanel(int index)
	{
		if (index < 0 || index >= Panels.Count) return -1;
		return Geometry.DistToRect(Player.X, Player.Y, in Panels[index].Rect);
	}

	/// <summary>
	/// Open or close the door input.DoorPick names. Refused -- silently, like a
	/// stale loot pick -- for anything that is not a door in reach. A CLOSE is
	/// refused out loud when anybody, standing or fallen, is in the doorway: a
	/// door does not shut through a body, and one that could would be a way to
	/// wall a guard into the frame.
	/// </summary>
	private void StepDoor(in InputFrame input)
	{
		if (input.DoorPick == 0) return;
		var p = Player;
		if (!p.Alive || Over != null) return;

		int i = input.DoorPick - 1;
		if (i < 0 || i >= Panels.Count) return;
		var door = Panels[i];
		if (!door.IsDoor) return;
		if (Geometry.DistToRect(p.X, p.Y, in door.Rect) > Tune.DoorReach) return;

		int cx = door.Rect.X + door.Rect.W / 2, cy = door.Rect.Y + door.Rect.H / 2;
		if (door.Open)
		{
			if (DoorwayOccupied(door))
			{
				Log.Add(SimEventKind.DoorBlocked, cx, cy, 0, i);
				return;
			}
			door.Open = false;
			Log.Add(SimEventKind.DoorClosed, cx, cy, 0, i);
		}
		else
		{
			door.Open = true;
			Log.Add(SimEventKind.DoorOpened, cx, cy, 0, i);
		}
		RebuildBlockers();

		int radius = p.MoveTier == InputFrame.TierStealth
			? Tune.DoorNoiseRadius / 2 : Tune.DoorNoiseRadius;
		DoorHeard(cx, cy, radius);
	}

	private bool DoorwayOccupied(PanelRuntime door)
	{
		if (Player.Alive && Geometry.CircleHitsRect(Player.X, Player.Y, Player.Radius, in door.Rect))
			return true;
		for (int g = 0; g < Guards.Count; g++)
		{
			var e = Guards[g];
			if (Geometry.CircleHitsRect(e.X, e.Y, e.Radius, in door.Rect)) return true;
		}
		return false;
	}

	/// <summary>
	/// A door creaking. Heard without line of sight, with falloff, ADDED to
	/// awareness rather than raising it to a level -- one door across a room is
	/// nothing, three beside a guard make him look. Capped at NoiseCap exactly
	/// as footsteps are, so a door can never start a firefight.
	/// </summary>
	private void DoorHeard(int x, int y, int radius)
	{
		for (int i = 0; i < Guards.Count; i++)
		{
			var e = Guards[i];
			if (e.Prone) continue;
			int d = Fx.Dist(e.X, e.Y, x, y);
			if (d >= radius) continue;
			if (e.Awareness >= Tune.NoiseCap) continue;

			int falloff = Fx.One - (int)((long)d * Fx.One / radius);
			int add = (int)(((long)Tune.DoorNoiseAw * falloff) >> Fx.Shift) * Actor.Mt;
			e.AwAcc += add;
			if (e.AwAcc > Tune.NoiseCap * Actor.Mt) e.AwAcc = Tune.NoiseCap * Actor.Mt;
			e.GraceMt = Tune.GraceTicks * Actor.Mt;
			e.SetLkp(x, y);
			if (e.Awareness >= Tune.AwCurious && e.State != GuardState.Combat) StartLook(e, false);
		}
	}

	/// <summary>
	/// A walking guard who is about to step into a shut door opens it. He does
	/// not close it behind him: a door standing open that you left shut is how
	/// you learn someone has been through, and that tell is worth more to the
	/// stealth game than tidiness is to the fiction.
	/// </summary>
	private void OpenDoorAhead(Actor e, int sin, int cos, int step)
	{
		if (Panels.Count == 0) return;
		int reach = step + Tune.GuardDoorProbe;
		int px = e.X + (int)(((long)reach * cos) >> Brad.UnitShift);
		int py = e.Y + (int)(((long)reach * sin) >> Brad.UnitShift);
		for (int i = 0; i < Panels.Count; i++)
		{
			var pn = Panels[i];
			if (!pn.IsDoor || pn.Open) continue;
			if (!Geometry.CircleHitsRect(px, py, e.Radius, in pn.Rect)) continue;
			pn.Open = true;
			Log.Add(SimEventKind.DoorOpened, pn.Rect.X + pn.Rect.W / 2,
				pn.Rect.Y + pn.Rect.H / 2, 1, i);
			RebuildBlockers();
		}
	}

	/// <summary>
	/// A pane gives. It is floor from now on, and it is LOUD: every guard in
	/// <see cref="Tune.GlassNoiseRadius"/> is raised to hunting awareness with
	/// the pane as his last known position -- which is the glass, not the
	/// shooter. No floor alarm: the gunshot that broke it raises that if it was
	/// heard, and a pane broken by a silenced round is a noise, not an alarm.
	/// </summary>
	private void BreakGlass(int panel, int heading)
	{
		var pn = Panels[panel];
		if (pn.Open) return;
		pn.Open = true;
		_blockersDirty = true;

		int cx = pn.Rect.X + pn.Rect.W / 2, cy = pn.Rect.Y + pn.Rect.H / 2;
		Log.Add(SimEventKind.GlassBroken, cx, cy, heading, panel);

		for (int i = 0; i < Guards.Count; i++)
		{
			var e = Guards[i];
			if (e.Prone) continue;
			if (Fx.Dist(e.X, e.Y, cx, cy) >= Tune.GlassNoiseRadius) continue;
			Notice(e, cx, cy, Tune.GlassAwareness);
		}
	}

	/// <summary>
	/// A shut door or a whole pane lies between two points. Hands do not reach
	/// through glass: subduing and looting ask this, on top of their range.
	/// Always false on a level without panels, so neither changed there.
	/// </summary>
	private bool PanelBetween(int ax, int ay, int bx, int by)
	{
		for (int i = 0; i < Panels.Count; i++)
		{
			var pn = Panels[i];
			if (!pn.Solid) continue;
			if (!Geometry.ClearLine(new[] { pn.Rect }, ax, ay, bx, by)) return true;
		}
		return false;
	}

	// =====================================================================
	// Impacts
	// =====================================================================

	private void ResolveImpacts()
	{
		for (int i = 0; i < _impacts.Count; i++)
		{
			var im = _impacts[i];
			switch (im.Kind)
			{
				case Projectiles.HitKind.Wall:
					Log.Add(im.Through ? SimEventKind.WallPierced : SimEventKind.WallHit,
						im.X, im.Y, im.Heading);
					if (im.Arc > 0)
						Discharge(im.X, im.Y, -1, im.Arc, im.Damage, im.Pierce, im.Heading);
					break;

				case Projectiles.HitKind.Bounce:
					Log.Add(SimEventKind.GrenadeBounce, im.X, im.Y, im.Heading);
					break;

				case Projectiles.HitKind.Blast:
					Blast(im.X, im.Y, im.Damage, im.Pierce);
					break;

				case Projectiles.HitKind.Guard:
					if (im.Arc > 0)
						Discharge(im.X, im.Y, im.GuardIndex, im.Arc, im.Damage, im.Pierce,
							im.Heading);
					else if (im.Headshot) KillGuard(Guards[im.GuardIndex], im.Heading);
					else HurtGuard(Guards[im.GuardIndex], im.Damage, im.Pierce, im.Heading);
					break;

				case Projectiles.HitKind.Player:
					HurtPlayer(im.Damage, im.Pierce, im.Heading);
					break;

				case Projectiles.HitKind.Glass:
					BreakGlass(_glassPanel[im.GuardIndex], im.Heading);
					break;
			}
		}
	}

	/// <summary>
	/// Damage a guard. Guards run a shallow health pool (<see cref="Tune.GuardHealth"/>)
	/// behind whatever plate they happen to be wearing, so the shots-to-kill
	/// spread across a floor comes from the ARMOUR roll rather than from hit
	/// points — one round for a shirtsleeved guard, four for a heavy plate.
	///
	/// Mirrors <see cref="HurtPlayer"/>'s reporting: the plate absorbing a round
	/// and the plate failing are separate events, because a player who cannot
	/// tell those apart cannot tell a working weapon from a useless one.
	/// </summary>
	private void HurtGuard(Actor e, int damage, int pierce, int heading)
	{
		if (e.Prone || !e.Alive) return;

		int armourBefore = e.Armour;
		bool killed = e.TakeDamage(damage, pierce, out int absorbed);

		if (killed)
		{
			KillGuard(e, heading);
			return;
		}

		if (absorbed > 0) Log.Add(SimEventKind.GuardArmourHit, e.X, e.Y, heading, absorbed);
		if (absorbed < damage) Log.Add(SimEventKind.GuardHurt, e.X, e.Y, heading, damage - absorbed);
		if (armourBefore > 0 && e.Armour == 0)
			Log.Add(SimEventKind.GuardArmourBroken, e.X, e.Y, heading, armourBefore);

		// Being shot at is a stimulus even when it does not kill: a guard
		// that survives a round knows exactly where it came from.
		ShotAt(e);
	}

	/// <summary>
	/// Damage the player. Surviving a hit is never free (RPG plan §2): it raises
	/// the floor alarm, freezes the frame and is audible, so eating a round
	/// reads as a disaster survived rather than a counter ticking down.
	/// </summary>
	private void HurtPlayer(int damage, int pierce, int heading)
	{
		var p = Player;
		if (!p.Alive || Over != null) return;

		int armourBefore = p.Armour;
		bool killed = p.TakeDamage(damage, pierce, out int absorbed);

		Alarm.Raise(2);

		if (killed)
		{
			Over = "dead";
			Clocks.RequestHitstop(Tune.HitstopPlayerTicks);
			Log.Add(SimEventKind.PlayerKilled, p.X, p.Y, heading, damage);
			return;
		}

		Clocks.RequestHitstop(Tune.HitstopGuardTicks);
		Log.Add(SimEventKind.PlayerHurt, p.X, p.Y, heading, damage);

		if (armourBefore > 0 && p.Armour == 0)
			Log.Add(SimEventKind.ArmourBroken, p.X, p.Y, heading, absorbed);
	}

	private void KillGuard(Actor e, int heading)
	{
		if (e.Prone) return;

		e.Alive = false;
		e.Health = 0;
		e.State = GuardState.Dead;
		OnGuardDown(e, true);
		e.DeadFacing = e.Facing;
		e.DeadRoll = Rng.NextSigned(Tune.DeadRollMax);
		e.Found = false;
		Kills++;

		// Shooting a guard permanently destroys the records he carried.
		int lost = 0;
		for (int i = 0; i < e.Carried.Count; i++) lost += e.Carried[i].Tier;
		Records.LostToGunfire += lost;
		e.Carried = new List<Record>();

		// Hitstop is the single largest contributor to how the guns feel. The
		// prototype crudely gated it off under dilation; TimeAuthority resolves
		// it by priority instead (spec §5.3).
		Clocks.RequestHitstop(Tune.HitstopGuardTicks);
		Log.Add(SimEventKind.GuardKilled, e.X, e.Y, heading, lost);
	}

	// =====================================================================
	// Boundary
	// =====================================================================

	public SimSnapshot Snapshot()
	{
		Records.Score(out int provable, out int unprovable, out int destroyed);

		var s = new SimSnapshot
		{
			Tick = Tick,
			PlayerX = Player.X, PlayerY = Player.Y, PlayerFacing = Player.Facing,
			PlayerAlive = Player.Alive, PlayerMag = Player.Mag,
			PlayerSneaking = Player.Sneaking,
			PlayerMoveTier = Player.MoveTier,
			PlayerReadyQ8 = Tune.SprintRecoverTicks <= 0 ? 0
				: Player.ReadyMt * Fx.One / (Tune.SprintRecoverTicks * Actor.Mt),
			PlayerSpinQ8 = Loadout.Spec.SpinUpTicks <= 0 ? 0
				: Player.SpinMt * Fx.One / (Loadout.Spec.SpinUpTicks * Actor.Mt),
			PlayerHeat = Player.Heat,
			PlayerAiming = Player.Aiming,
			PlayerAimTarget = Player.AimTarget,
			PlayerAimLockMt = Player.AimLockMt,
			PlayerAimLockFullMt = Tune.AimLockTicks * Actor.Mt,
			ObjectivesTotal = ObjectivesTotal,
			ObjectivesCarried = ObjectivesCarried,
			PlayerSway = Player.SwayQ8,
			PlayerSpreadHalf = PlayerSpreadWidth() / 2,
			PlayerHealth = Player.Health,
			PlayerArmour = Player.Armour,
			PlayerArmourMax = Loadout.ArmourSpec.Armour,
			PlayerReloading = Player.ReloadMt > 0,
			WorldScale = Clocks.WorldScale, PlayerScale = Clocks.PlayerScale,
			Dilating = Clocks.ActivePriority == TimeAuthority.PriDilation,
			AlarmLevel = Alarm.Level,
			Over = Over,
			Provable = provable, Unprovable = unprovable, DestroyedScore = destroyed,
			FuelIndex = Records.FuelIndex,
			Kills = Kills, Subdues = Subdues, Shots = Shots,
		};

		for (int i = 0; i < Guards.Count; i++)
		{
			var g = Guards[i];
			s.Guards.Add(new ActorView(g));
			int e = g.Awareness > Tune.AwEngage ? Tune.AwEngage : g.Awareness;
			if (!g.Prone && e > s.Exposure) s.Exposure = e;
		}

		for (int i = 0; i < Bullets.Live.Count; i++)
		{
			var b = Bullets.Live[i];
			int speed = Fx.Hypot(b.VX, b.VY);
			s.Bullets.Add(new BulletView(b.X, b.Y, b.Heading, b.FromPlayer, speed,
				(int)b.Kind, b.LifeMt / Actor.Mt));
		}

		for (int i = 0; i < Caches.Count; i++)
		{
			var c = Caches[i];
			s.Caches.Add(new CacheView(c.X, c.Y, c.Taken));
		}
		for (int i = 0; i < Chests.Count; i++)
		{
			var c = Chests[i];
			s.Chests.Add(new ChestView(c.X, c.Y, c.Kit.Count, c.Objective));
		}
		for (int i = 0; i < Ground.Count; i++)
		{
			var g = Ground[i];
			s.Ground.Add(new GroundView(g.X, g.Y, g.Kit.Count));
		}
		for (int i = 0; i < Panels.Count; i++)
		{
			var pn = Panels[i];
			s.Panels.Add(new PanelView(pn.Kind, pn.Rect, pn.Vertical, pn.Open));
		}

		for (int i = 0; i < Records.Held.Count; i++)
		{
			var r = Records.Held[i];
			s.Records.Add(new RecordView(r.NameIndex, r.Tier, r.Charge, (int)r.State));
		}

		for (int i = 0; i < Log.Events.Count; i++) s.Events.Add(Log.Events[i]);

		return s;
	}

	/// <summary>
	/// The value the replay test compares (spec §4.2). Order of the Add calls is
	/// part of the contract — changing it invalidates every golden hash.
	/// </summary>
	public ulong StateHash()
	{
		var h = Hash64.New();
		Loadout.HashInto(ref h);
		h.Add(Tick);
		h.Add(Over == null ? 0 : Over.Length);
		h.Add(Kills); h.Add(Subdues); h.Add(Shots);
		Player.HashInto(ref h);
		h.Add(Player.SwapMt); h.Add(Player.MagStowed);
		Pack.HashInto(ref h);
		// Kits by index, and the dwell with them: what is left on each body is
		// state the next tick reads.
		for (int i = 0; i < Guards.Count; i++)
		{
			var g = Guards[i];
			h.Add(g.Kit.Count);
			for (int k = 0; k < g.Kit.Count; k++) h.Add(g.Kit[k]);
		}
		LootRng.HashInto(ref h);
		for (int i = 0; i < Guards.Count; i++) Guards[i].HashInto(ref h);
		h.Add(_navCursor);
		for (int i = 0; i < Caches.Count; i++) Caches[i].HashInto(ref h);
		h.Add(Chests.Count);
		for (int i = 0; i < Chests.Count; i++) Chests[i].HashInto(ref h);
		h.Add(Ground.Count);
		for (int i = 0; i < Ground.Count; i++) Ground[i].HashInto(ref h);
		Records.HashInto(ref h);
		Bullets.HashInto(ref h);
		Clocks.HashInto(ref h);
		Alarm.HashInto(ref h);
		Net.HashInto(ref h);
		if (Sweep != null) Sweep.HashInto(ref h);
		else h.Add(0);
		Rng.HashInto(ref h);
		// Only when there are any: a level without glass or doors hashes exactly
		// as it did before they existed, the rule the `s` replay token follows.
		if (Panels.Count > 0)
		{
			h.Add(Panels.Count);
			for (int i = 0; i < Panels.Count; i++) h.Add(Panels[i].Open);
		}
		return h.Value;
	}
}
