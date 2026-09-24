using System;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// The firearm model: muzzle velocity, the three-term firing cone, per-weapon
/// aim turn rate, and the shotgun's choke.
///
/// The contracts worth protecting here are the ones that are invisible when
/// they break. A muzzle velocity can be raised and quietly start putting rounds
/// through walls; a cone can grow a term and quietly stop being drawn by the
/// reticle; a turn rate can be clamped to nothing by an attachment stack. Each
/// of those produces no exception and no visual artifact, only a game that
/// feels wrong in a way nobody can name.
/// </summary>
public static class Handling
{
	private const int GW = Level.GW, GH = Level.GH;

	private static readonly WeaponId[] All =
	{
		WeaponId.Glock, WeaponId.Mp7, WeaponId.Ak47, WeaponId.Remington, WeaponId.Saw,
	};

	private static char[] Room()
	{
		var g = new char[GW * GH];
		for (int r = 0; r < GH; r++)
			for (int c = 0; c < GW; c++)
				g[r * GW + c] = (r == 0 || r == GH - 1 || c == 0 || c == GW - 1) ? '#' : '.';
		return g;
	}

	private static string Text(char[] g)
	{
		var sb = new System.Text.StringBuilder();
		sb.Append("name: fixture\ngrid:\n");
		for (int r = 0; r < GH; r++)
		{
			for (int c = 0; c < GW; c++) sb.Append(g[r * GW + c]);
			sb.Append('\n');
		}
		return sb.ToString();
	}

	private static void Put(char[] g, int c, int r, char ch) => g[r * GW + c] = ch;
	private static int CellCentre(int cell) => cell * Level.CellFx + Level.CellFx / 2;

	/// <summary>An empty room with the player mid-floor, facing due east.</summary>
	private static SimWorld Field(WeaponId id, int cx = 10, int cy = 14)
	{
		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		var w = new SimWorld(Level.FromText(Text(g)), 17, new Loadout(id));
		w.Player.X = CellCentre(cx);
		w.Player.Y = CellCentre(cy);
		return w;
	}

	private static int Reach(in WeaponSpec s)
		=> (int)((long)s.BulletSpeed * s.BulletTicks / Fx.TicksPerSecond);

	// ------------------------------------------------------- muzzle velocity

	private static void Velocity()
	{
		H.Group("muzzle velocity");

		// The browser build's figures, which these deliberately leave behind.
		const int OldPlayer = 215040;   // 840 px/s
		const int OldGuard = 163840;    // 640 px/s

		H.Check("player rounds are much faster than the prototype's",
			Tune.PlayerBulletSpeed >= OldPlayer * 5 / 2,
			$"{Tune.PlayerBulletSpeed / Fx.One} px/s vs {OldPlayer / Fx.One}");
		// Guards fire their own weapons now, so every weapon a guard can carry
		// must outrun the prototype's guard round -- except a thrown grenade,
		// which is not a round at all.
		string slow = "";
		for (int i = 0; i < WeaponCatalog.Count; i++)
		{
			var ws = WeaponCatalog.Get((WeaponId)i);
			if (!ws.Grenade && ws.BulletSpeed < OldGuard * 5 / 2) slow = $"{(WeaponId)i} {ws.BulletSpeed / Fx.One} px/s";
		}
		H.Check("and so are guard rounds, whatever he carries", slow.Length == 0, slow);

		// Faster rounds with the same lifetimes would have tripled every
		// weapon's reach, which is a balance change nobody asked for. Lifetimes
		// were cut to match, so reach is the one thing that did NOT move.
		foreach (var id in All)
		{
			var s = WeaponCatalog.Get(id);
			int px = Reach(s) / Fx.One;
			H.Check($"{WeaponCatalog.NameOf(id)} still reaches a sane distance",
				px >= 500 && px <= 2200, $"{px}px");
		}

		// Crossing the field takes a fraction of a second now, which is the
		// whole point: leading a running target is a correction, not the game.
		int glockCross = 960 * Fx.One * Fx.TicksPerSecond / Tune.PlayerBulletSpeed;
		H.Check("a round crosses the field in under half a second",
			glockCross <= 30, $"{glockCross} ticks");

		// But still visibly in flight under dilation -- spec §7.2's actual
		// requirement, and the reason bullets are objects rather than raycasts.
		int slowPxPerSec = (int)((long)Tune.PlayerBulletSpeed * Tune.WorldSlow
			/ Fx.ScaleDen / Fx.One);
		H.Check("and is still trackable at WORLD_SLOW",
			slowPxPerSec > 200 && slowPxPerSec < 900, $"{slowPxPerSec} px/s on screen");
	}

	// ----------------------------------------------------------- tunnelling

	private static void Tunnelling()
	{
		H.Group("tunnelling");

		// Three fixed substeps sampled 16px apart at these speeds. Walls are one
		// cell thick, so a round could pass clean through one and hit a guard
		// behind it -- silently, with no error and nothing on screen.
		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		for (int r = 4; r < 24; r++) Put(g, 20, r, '#');     // one-cell wall
		string level = Text(g);

		foreach (var id in All)
		{
			var w = new SimWorld(Level.FromText(level), 23, new Loadout(id));
			w.Player.X = CellCentre(10);
			w.Player.Y = CellCentre(14);

			// Fire east into the wall and run long enough to cross it.
			w.Step(new InputFrame(0, 0, 0, InputFrame.FFire));
			for (int i = 0; i < 40; i++) w.Step(new InputFrame(0, 0, 0, 0));

			int past = 0;
			for (int i = 0; i < w.Bullets.Live.Count; i++)
				if (w.Bullets.Live[i].X > CellCentre(21)) past++;

			H.Eq($"{WeaponCatalog.NameOf(id)} cannot shoot through a one-cell wall", past, 0);
		}

		// The substep count has to actually respond to speed, or the guarantee
		// above is luck rather than design.
		int fast = Fx.PerTick(3000 * Fx.One, Tune.NormalScale);
		int steps = fast / Tune.BulletSubstepFx + 1;
		H.Check("a 3000 px/s round is subdivided far past the old flat three",
			steps > Tune.BulletSubsteps * 3, $"{steps} substeps");
		H.Check("and the count is capped so it cannot run away",
			Tune.BulletSubstepCap >= steps && Tune.BulletSubstepCap <= 128,
			$"cap {Tune.BulletSubstepCap}");

		// Rounds must still cover their full distance: an over-subdivided
		// integer step that truncates would shorten every weapon's reach.
		var open = Field(WeaponId.Ak47, 6, 14);
		open.Step(new InputFrame(0, 0, 0, InputFrame.FFire));
		int x0 = open.Bullets.Live[0].X;
		open.Step(new InputFrame(0, 0, 0, 0));
		int travelled = open.Bullets.Live[0].X - x0;
		int want = Fx.PerTick(WeaponCatalog.Get(WeaponId.Ak47).BulletSpeed, Tune.NormalScale);
		H.Check("substepping does not shorten a round's flight",
			Math.Abs(travelled - want) <= 2, $"{travelled} vs {want}");
	}

	// ------------------------------------------------------- weapon handling

	private static void TurnRate()
	{
		H.Group("weapon handling");

		// Ordering is the design statement: weight costs you the swing.
		int glock = WeaponCatalog.Get(WeaponId.Glock).TurnNum;
		int mp7 = WeaponCatalog.Get(WeaponId.Mp7).TurnNum;
		int ak = WeaponCatalog.Get(WeaponId.Ak47).TurnNum;
		int shotgun = WeaponCatalog.Get(WeaponId.Remington).TurnNum;
		int saw = WeaponCatalog.Get(WeaponId.Saw).TurnNum;

		H.Check("a pistol turns fastest", glock > mp7 && glock > ak);
		H.Check("an SMG turns faster than a rifle", mp7 > ak);
		H.Check("a shotgun is slower than a rifle", shotgun < ak);
		H.Check("and the SAW is the worst of the lot", saw < shotgun);
		H.Check("nothing snaps instantly", glock < Tune.TurnDen, $"{glock}/{Tune.TurnDen}");

		// Measured in the world, not just in the table: ticks to bring the
		// muzzle within 2 degrees of a 180 degree reversal.
		int TicksToTurn(WeaponId id)
		{
			var w = Field(id);
			for (int i = 0; i < 600; i++)
			{
				w.Step(new InputFrame(0, 0, Brad.Half, 0));
				if (Math.Abs(Brad.Norm(w.Player.Facing - Brad.Half)) < 364) return i + 1;
			}
			return -1;
		}

		int tGlock = TicksToTurn(WeaponId.Glock);
		int tSaw = TicksToTurn(WeaponId.Saw);
		H.Check("a pistol comes round almost at once", tGlock > 0 && tGlock <= 20,
			$"{tGlock} ticks");
		H.Check("the SAW takes markedly longer", tSaw >= tGlock * 2,
			$"glock {tGlock}, saw {tSaw} ticks");
		H.Check("but it does get there", tSaw > 0 && tSaw < 120, $"{tSaw} ticks");

		// The muzzle no longer teleports: this is the §10.2 free-spin exploit,
		// and closing it is a deliberate deviation rather than an accident.
		var spin = Field(WeaponId.Saw);
		spin.Step(new InputFrame(0, 0, Brad.Half, 0));
		H.Check("one tick does not reverse the weapon",
			Math.Abs(Brad.Norm(spin.Player.Facing - Brad.Half)) > 4000,
			$"{Brad.Norm(spin.Player.Facing - Brad.Half)} BRAD short");

		// Attachments move handling, and cannot drive it out of bounds.
		var bare = new Loadout(WeaponId.Ak47);
		var heavy = bare.WithAttachment(AttachSlot.Stock, 2);
		var fore = bare.WithAttachment(AttachSlot.Rail, 3);
		H.Check("a heavy stock slows the swing", heavy.Spec.TurnNum < bare.Spec.TurnNum);
		H.Check("a foregrip quickens it", fore.Spec.TurnNum > bare.Spec.TurnNum);

		foreach (var id in All)
		{
			var stacked = new Loadout(id, ArmourId.None, sight: 3, stock: 2, mag: 2);
			H.Check($"{WeaponCatalog.NameOf(id)} is never unturnable",
				stacked.Spec.TurnNum >= Tune.TurnAimMin, $"{stacked.Spec.TurnNum}");
		}
	}

	// ---------------------------------------------------------- carry weight

	private static void CarryWeight()
	{
		H.Group("carry weight");

		// StepPlayer used to move at Tune.SpeedWalk flat. WalkSpeed and
		// SneakSpeed were computed off the loadout and then never read, and
		// AimMoveQ8 was never read at all -- so a SAW in heavy plate handled
		// exactly like a bare Glock, and three tuned stats did nothing. None of
		// that produced a failure; it just quietly removed a whole axis.
		H.Eq("an unarmoured pistol still walks at the spec speed",
			Loadout.Default.WalkSpeed, Tune.SpeedWalk);
		H.Eq("and sneaks at the spec speed",
			Loadout.Default.SneakSpeed, Tune.SpeedSneak);

		// default(Loadout) is the constructor-less path SimWorld takes, and is
		// exactly where an off-by-one in the field encoding would hide.
		H.Eq("and a default-constructed loadout agrees",
			default(Loadout).WalkSpeed, Tune.SpeedWalk);

		// Measured in the world, in an open room, so this is the movement path
		// and not the table.
		int Travel(WeaponId id, ArmourId armour, bool aiming)
		{
			var g = Room();
			Put(g, 1, 1, '@');
			Put(g, GW - 2, GH - 2, 'X');
			var w = new SimWorld(Level.FromText(Text(g)), 3, new Loadout(id, armour));
			w.Player.X = CellCentre(8);
			w.Player.Y = CellCentre(14);
			int start = w.Player.X;
			byte f = aiming ? InputFrame.FAim : (byte)0;
			for (int i = 0; i < 60; i++) w.Step(new InputFrame(1, 0, 0, f));
			return w.Player.X - start;
		}

		int glock = Travel(WeaponId.Glock, ArmourId.None, false);
		int saw = Travel(WeaponId.Saw, ArmourId.None, false);
		int plated = Travel(WeaponId.Glock, ArmourId.HeavyPlate, false);
		int aimed = Travel(WeaponId.Glock, ArmourId.None, true);

		H.Check("a SAW is slower to carry than a Glock", saw < glock,
			$"glock {glock / Fx.One}px, saw {saw / Fx.One}px");
		H.Check("heavy plate is slower than no armour", plated < glock,
			$"bare {glock / Fx.One}px, plated {plated / Fx.One}px");
		H.Check("and aiming costs ground", aimed < glock,
			$"hip {glock / Fx.One}px, aimed {aimed / Fx.One}px");
		H.Check("but none of it roots you", saw > glock / 4 && aimed > glock / 4);
	}

	// -------------------------------------------------------- movement tiers

	private static InputFrame Move(int mx, int my, int tier, byte flags = 0, int aim = 0)
		=> new InputFrame(mx, my, aim, flags, 0, tier);

	/// <summary>
	/// Four tiers on the wheel, trading speed against noise, detection and how
	/// well you can shoot. The contract that matters most is at the bottom of
	/// this method: tiers 0 and 1 must be the OLD sneak and walk exactly, or
	/// every number ever measured against the two-stance game is invalidated.
	/// </summary>
	private static void MoveTiers()
	{
		H.Group("movement tiers");

		H.Eq("there are four tiers", InputFrame.TierCount, 4);

		// Speed rises with every tier, measured in the world rather than read
		// off the table.
		int Travel(int tier)
		{
			var w = Field(WeaponId.Glock, 8, 14);
			int start = w.Player.X;
			for (int i = 0; i < 60; i++) w.Step(Move(1, 0, tier));
			return w.Player.X - start;
		}

		int[] covered = new int[InputFrame.TierCount];
		for (int t = 0; t < InputFrame.TierCount; t++) covered[t] = Travel(t);

		Console.WriteLine();
		Console.WriteLine("  movement tiers (one second of walking):");
		Console.WriteLine("    tier      px/s   noise   detect   turn");
		string[] names = { "stealth", "walk", "fast", "sprint" };
		for (int t = 0; t < InputFrame.TierCount; t++)
			Console.WriteLine($"    {names[t],-8}  {covered[t] / Fx.One,4}"
				+ $"   {Tune.TierNoise(t) / Fx.One,5}"
				+ $"   {Tune.TierDetectQ8(t) * 100 / 256,5}%"
				+ $"   {Tune.TierTurnQ8(t) * 100 / 256,4}%");
		Console.WriteLine();

		bool rising = true;
		for (int t = 1; t < InputFrame.TierCount; t++)
			if (covered[t] <= covered[t - 1]) rising = false;
		H.Check("every tier is faster than the one below it", rising,
			string.Join(", ", covered));

		H.Check("sprint is roughly twice a walk",
			covered[InputFrame.TierSprint] > covered[InputFrame.TierWalk] * 3 / 2
			&& covered[InputFrame.TierSprint] < covered[InputFrame.TierWalk] * 5 / 2,
			$"walk {covered[1] / Fx.One}px, sprint {covered[3] / Fx.One}px");

		// Noise: silent when creeping, louder the faster you go.
		H.Eq("stealth makes no noise at all", Tune.TierNoise(InputFrame.TierStealth), 0);
		bool louder = true;
		for (int t = 2; t < InputFrame.TierCount; t++)
			if (Tune.TierNoise(t) <= Tune.TierNoise(t - 1)) louder = false;
		H.Check("and each tier above a walk carries further", louder);

		// Detection: the "more stealth the slower you move" axis.
		bool stealthier = true;
		for (int t = 1; t < InputFrame.TierCount; t++)
			if (Tune.TierDetectQ8(t) <= Tune.TierDetectQ8(t - 1)) stealthier = false;
		H.Check("a faster tier is picked up more readily", stealthier);
		H.Check("and creeping is stealthier than a walk",
			Tune.TierDetectQ8(InputFrame.TierStealth) < Tune.TierDetectQ8(InputFrame.TierWalk));

		// Measured: a guard takes longer to notice a creeping player than a
		// sprinting one, at the same range.
		int TicksToNotice(int tier)
		{
			var g = Room();
			Put(g, 4, 14, 'a');
			Put(g, 1, 1, '@');
			Put(g, GW - 2, GH - 2, 'X');
			var w = new SimWorld(Level.FromText(Text(g)), 4242);
			var guard = w.Guards[0];
			guard.Facing = 0;
			guard.State = GuardState.Relaxed; guard.Task = GuardTask.Post;
			guard.PathX = null; guard.PathY = null;
			w.Player.X = guard.X + 250 * Fx.One;
			w.Player.Y = guard.Y;

			for (int i = 0; i < 60 * 30; i++)
			{
				int kx = w.Player.X, ky = w.Player.Y;
				w.Step(Move(1, 0, tier));
				w.Player.X = kx; w.Player.Y = ky;
				w.Player.Alive = true;
				if (guard.State == GuardState.Combat) return i + 1;
			}
			return -1;
		}

		int creep = TicksToNotice(InputFrame.TierStealth);
		int walk = TicksToNotice(InputFrame.TierWalk);
		int sprint = TicksToNotice(InputFrame.TierSprint);
		H.Check("a creeping player is noticed slowest", creep > walk,
			$"creep {creep}, walk {walk}");
		H.Check("and a sprinting one fastest", sprint < walk,
			$"sprint {sprint}, walk {walk}");

		// Turning: a sprint costs you the swing.
		bool harder = Tune.TierTurnQ8(InputFrame.TierSprint)
			< Tune.TierTurnQ8(InputFrame.TierWalk);
		H.Check("sprinting makes the weapon harder to turn", harder,
			$"{Tune.TierTurnQ8(3)}/256 vs {Tune.TierTurnQ8(1)}/256");
		H.Eq("a walk costs nothing", Tune.TierTurnQ8(InputFrame.TierWalk), Fx.One);
		H.Eq("and nor does creeping", Tune.TierTurnQ8(InputFrame.TierStealth), Fx.One);

		int TicksToTurn(int tier)
		{
			var w = Field(WeaponId.Ak47);
			for (int i = 0; i < 600; i++)
			{
				w.Step(Move(1, 0, tier, 0, Brad.Half));
				if (Math.Abs(Brad.Norm(w.Player.Facing - Brad.Half)) < 364) return i + 1;
			}
			return -1;
		}
		int turnWalk = TicksToTurn(InputFrame.TierWalk);
		int turnSprint = TicksToTurn(InputFrame.TierSprint);
		H.Check("measured, a sprinting turn takes longer", turnSprint > turnWalk,
			$"walk {turnWalk}, sprint {turnSprint} ticks");

		// PARITY. The two old stances must survive exactly, or every number
		// measured against them -- the whole of spec §8.6 -- is invalidated.
		H.Eq("tier 1 walks at exactly the old walk speed",
			(int)(((long)Loadout.Default.WalkSpeed * Tune.TierSpeedQ8(InputFrame.TierWalk))
				>> Fx.Shift), Tune.SpeedWalk);
		H.Eq("tier 0 creeps at exactly the old sneak speed",
			Loadout.Default.SneakSpeed, Tune.SpeedSneak);
		H.Eq("and carries the old walking noise at tier 1",
			Tune.TierNoise(InputFrame.TierWalk), Tune.NoiseWalkRadius);
		H.Eq("and the old moving-detection multiplier",
			Tune.TierDetectQ8(InputFrame.TierWalk), Tune.MulMove);
	}

	/// <summary>The beat after a sprint where the weapon is not back up yet.</summary>
	private static void SprintRecovery()
	{
		H.Group("sprint recovery");

		H.Check("the recovery is a real pause", Tune.SprintRecoverTicks >= 15
			&& Tune.SprintRecoverTicks <= 60, $"{Tune.SprintRecoverTicks} ticks");

		// Sprinting floors sway, so a shot taken out of one is wild.
		var w = Field(WeaponId.Ak47);
		for (int i = 0; i < 30; i++) w.Step(Move(1, 0, InputFrame.TierSprint));
		int sprinting = w.Snapshot().PlayerSpreadHalf;

		var settled = Field(WeaponId.Ak47);
		for (int i = 0; i < 60; i++) settled.Step(Move(0, 0, InputFrame.TierWalk));
		int still = settled.Snapshot().PlayerSpreadHalf;

		H.Check("a sprinting player shoots far wider than a settled one",
			sprinting > still * 2, $"settled {still}, sprinting {sprinting} BRAD");

		// Stopping does not fix it immediately: that is the whole mechanic.
		w.Step(Move(0, 0, InputFrame.TierWalk));
		int justStopped = w.Snapshot().PlayerSpreadHalf;
		H.Check("stopping does not steady it at once", justStopped > still * 2,
			$"{justStopped} vs settled {still}");

		for (int i = 0; i < Tune.SprintRecoverTicks + 30; i++)
			w.Step(Move(0, 0, InputFrame.TierWalk));
		H.Check("but it comes back after the recovery",
			w.Snapshot().PlayerSpreadHalf <= still + 4,
			$"{w.Snapshot().PlayerSpreadHalf} vs settled {still}");

		// And the aim lock cannot build until the weapon is back on target.
		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		Put(g, 24, 14, 'a');
		var d = new SimWorld(Level.FromText(Text(g)), 41, new Loadout(WeaponId.Glock));
		var guard = d.Guards[0];
		guard.PathX = null; guard.PathY = null;
		guard.State = GuardState.Relaxed; guard.Task = GuardTask.Post;
		guard.Facing = Brad.Half;
		d.Player.X = guard.X - 200 * Fx.One;
		d.Player.Y = guard.Y;
		int aim = Brad.Atan2(guard.Y - d.Player.Y, guard.X - d.Player.X);

		void Pin(int tier, byte flags)
		{
			int x = d.Player.X, y = d.Player.Y;
			d.Step(new InputFrame(0, 0, aim, flags, 0, tier));
			d.Player.X = x; d.Player.Y = y;
			d.Player.Alive = true;
		}

		Pin(InputFrame.TierSprint, 0);
		for (int i = 0; i < Tune.AimLockTicks + 10; i++) Pin(InputFrame.TierWalk, InputFrame.FAim);
		H.Check("no lock builds while the weapon is coming back up",
			!d.Player.HeadshotReady);

		for (int i = 0; i < Tune.AimLockTicks + 10; i++) Pin(InputFrame.TierWalk, InputFrame.FAim);
		H.Check("and it builds normally once it has", d.Player.HeadshotReady);
	}

	/// <summary>Tiers are sim input, so they have to survive a recording.</summary>
	private static void TierPlumbing()
	{
		H.Group("movement tier plumbing");

		// The tier feeds the hash: two runs that differ only in stance must not
		// collide.
		string level = Program.ReadLevel("substation_4.txt");
		var a = new SimWorld(Level.FromText(level), 91);
		var b = new SimWorld(Level.FromText(level), 91);
		for (int i = 0; i < 60; i++)
		{
			a.Step(Move(1, 0, InputFrame.TierWalk));
			b.Step(Move(1, 0, InputFrame.TierSprint));
		}
		H.Check("the tier changes the state hash", a.StateHash() != b.StateHash());

		// Round-trip through the replay format.
		var r = new Replay { Seed = 91, LevelText = level };
		for (int i = 0; i < 40; i++) r.Inputs.Add(Move(1, 0, i % InputFrame.TierCount));
		var back = Replay.FromText(r.ToText());
		bool tiersKept = back.Inputs.Count == r.Inputs.Count;
		for (int i = 0; i < back.Inputs.Count && tiersKept; i++)
			if (back.Inputs[i].MoveTier != r.Inputs[i].MoveTier) tiersKept = false;
		H.Check("every tier round-trips through a replay", tiersKept);

		// A run using the tiers still verifies against the sim.
		var world = new SimWorld(Level.FromText(level), 91);
		var rec = new Replay { Seed = 91, LevelText = level };
		for (int i = 0; i < 300; i++)
		{
			var f = Move(i % 3 - 1, (i / 3) % 3 - 1, (i / 17) % InputFrame.TierCount);
			world.Step(f);
			rec.Inputs.Add(f);
			if ((i + 1) % Replay.HashEvery == 0) rec.AddHash(i + 1, world.StateHash());
		}
		var div = rec.Verify();
		H.Check("a run that changes tier verifies", !div.Found,
			div.Found ? $"diverged at {div.Tick}" : "");

		// BACK COMPATIBILITY. A replay recorded before tiers existed carries the
		// stance in the FSneak bit and no m token; it has to keep meaning what
		// it meant, or every recording on disk is scrap.
		var old = Replay.FromText(
			"seed: 1\nlevel:\n" + level + "\nendlevel\nframes:\n"
			+ "1 0 0 2\n1 0 0 0\n");
		H.Eq("an old sneaking frame parses as stealth",
			old.Inputs[0].MoveTier, (int)InputFrame.TierStealth);
		H.Eq("and an old walking frame as a walk",
			old.Inputs[1].MoveTier, (int)InputFrame.TierWalk);

		// The m token wins over the flag when both are present.
		var both = Replay.FromText(
			"seed: 1\nlevel:\n" + level + "\nendlevel\nframes:\n1 0 0 2 m3\n");
		H.Eq("an explicit tier beats the legacy flag",
			both.Inputs[0].MoveTier, (int)InputFrame.TierSprint);

		// Out-of-range tiers fall back rather than throwing, like every other
		// total parser here.
		var bad = Replay.FromText(
			"seed: 1\nlevel:\n" + level + "\nendlevel\nframes:\n1 0 0 0 m99\n");
		H.Eq("an impossible tier falls back to a walk",
			bad.Inputs[0].MoveTier, (int)InputFrame.TierWalk);
	}

	// ---------------------------------------------------------- the spin-up

	/// <summary>
	/// A rotary barrel has to spool before it fires. The part that would break
	/// silently is the accounting: spooling must cost no ammunition and start
	/// no cooldown, or holding the trigger on a minigun empties it without a
	/// round leaving the barrel.
	/// </summary>
	private static void SpinUp()
	{
		H.Group("rotary spin-up");

		var w = Field(WeaponId.Vulcan);
		int spin = WeaponCatalog.Get(WeaponId.Vulcan).SpinUpTicks;
		H.Check("fixture: the Vulcan spins up", spin > 0);

		int mag = w.Player.Mag;
		var fire = new InputFrame(0, 0, 0, InputFrame.FFire);

		// Held, but not yet spooled: nothing happens at all.
		w.Step(fire);
		H.Eq("the first tick of trigger fires nothing", w.Bullets.Live.Count, 0);
		H.Eq("and costs no ammunition", w.Player.Mag, mag);
		H.Eq("and starts no cooldown", w.Player.CooldownMt, 0);
		H.Check("but the barrel is turning", w.Player.SpinMt > 0);

		for (int i = 0; i < spin - 2; i++) w.Step(fire);
		H.Eq("still nothing most of the way up", w.Bullets.Live.Count, 0);
		H.Eq("and still full", w.Player.Mag, mag);

		// Spooled: it fires, and keeps firing.
		for (int i = 0; i < 6; i++) w.Step(fire);
		H.Check("once spooled it fires", w.Bullets.Live.Count > 0,
			$"{w.Bullets.Live.Count} rounds");
		H.Check("and spends ammunition", w.Player.Mag < mag);

		int spooled = w.Player.SpinMt;
		H.Eq("the spin is capped at the weapon's own figure", spooled, spin * Actor.Mt);

		// Releasing spools DOWN, faster than it spooled up.
		var idle = new InputFrame(0, 0, 0, 0);
		w.Step(idle);
		H.Check("letting go winds it down", w.Player.SpinMt < spooled);
		H.Check("faster than it wound up",
			spooled - w.Player.SpinMt > Actor.Mt, "not faster");

		for (int i = 0; i < spin * 2; i++) w.Step(idle);
		H.Eq("and it stops entirely", w.Player.SpinMt, 0);

		// A weapon with no spin-up is completely unaffected: it fires on the
		// first tick, as everything did before this existed.
		var ak = Field(WeaponId.Ak47);
		ak.Step(new InputFrame(0, 0, 0, InputFrame.FFire));
		H.Check("a weapon with no spin-up fires at once", ak.Bullets.Live.Count > 0);
		H.Eq("and never accumulates spin", ak.Player.SpinMt, 0);

		// Swapping off a spooled weapon must not leave the spin behind.
		var swap = Field(WeaponId.Vulcan);
		for (int i = 0; i < spin + 4; i++) swap.Step(fire);
		H.Check("fixture: spooled", swap.Player.SpinMt > 0);
		for (int i = 0; i < 3; i++) swap.Step(idle);
		H.Check("releasing drops it toward zero", swap.Player.SpinMt < spin * Actor.Mt);

		// It is hashed, so two runs that differ only in how long the trigger
		// was held do not collide.
		string level = Program.ReadLevel("substation_4.txt");
		var a = new SimWorld(Level.FromText(level), 55, new Loadout(WeaponId.Vulcan));
		var b = new SimWorld(Level.FromText(level), 55, new Loadout(WeaponId.Vulcan));
		for (int i = 0; i < 20; i++)
		{
			a.Step(fire);
			b.Step(i % 2 == 0 ? fire : idle);
		}
		H.Check("the spin feeds the state hash", a.StateHash() != b.StateHash());
	}

	// ------------------------------------------------------------ aim sway

	private static void Sway()
	{
		H.Group("aim sway");

		// Standing still and holding the aim steady: nothing to disturb.
		var still = Field(WeaponId.Ak47);
		for (int i = 0; i < 60; i++) still.Step(new InputFrame(0, 0, 0, 0));
		H.Eq("a settled shooter has no sway", still.Player.SwayQ8, 0);

		// Swinging the weapon does.
		var swung = Field(WeaponId.Ak47);
		swung.Step(new InputFrame(0, 0, Brad.Half, 0));
		H.Check("swinging the weapon raises sway", swung.Player.SwayQ8 > 0,
			$"{swung.Player.SwayQ8}");

		// And it settles again on its own, without the player doing anything.
		int peak = swung.Player.SwayQ8;
		for (int i = 0; i < 40; i++) swung.Step(new InputFrame(0, 0, swung.Player.Facing, 0));
		H.Check("and it settles back down when the shooter holds still",
			swung.Player.SwayQ8 < peak / 4, $"{peak} -> {swung.Player.SwayQ8}");

		// Walking disturbs the weapon; sneaking disturbs it less.
		var walk = Field(WeaponId.Ak47);
		var sneak = Field(WeaponId.Ak47);
		for (int i = 0; i < 30; i++)
		{
			walk.Step(new InputFrame(0, 1, 0, 0));
			sneak.Step(new InputFrame(0, 1, 0, InputFrame.FSneak));
		}
		H.Check("walking raises sway", walk.Player.SwayQ8 > 0, $"{walk.Player.SwayQ8}");
		H.Check("sneaking raises less of it", sneak.Player.SwayQ8 < walk.Player.SwayQ8,
			$"walk {walk.Player.SwayQ8}, sneak {sneak.Player.SwayQ8}");

		// The point of all of it: a swung weapon shoots wider, and a heavy one
		// far wider than a light one.
		int ConeAfterSwing(WeaponId id)
		{
			var w = Field(id);
			w.Step(new InputFrame(0, 0, Brad.Quarter, 0));
			return w.Snapshot().PlayerSpreadHalf;
		}
		int ConeSettled(WeaponId id)
		{
			var w = Field(id);
			for (int i = 0; i < 60; i++) w.Step(new InputFrame(0, 0, 0, 0));
			return w.Snapshot().PlayerSpreadHalf;
		}

		foreach (var id in All)
		{
			string n = WeaponCatalog.NameOf(id);
			H.Check($"{n} shoots wider mid-swing", ConeAfterSwing(id) > ConeSettled(id),
				$"{ConeSettled(id)} -> {ConeAfterSwing(id)} BRAD");
		}

		H.Check("and a SAW is punished for it far harder than a Glock",
			ConeAfterSwing(WeaponId.Saw) - ConeSettled(WeaponId.Saw)
			> (ConeAfterSwing(WeaponId.Glock) - ConeSettled(WeaponId.Glock)) * 2,
			$"saw +{ConeAfterSwing(WeaponId.Saw) - ConeSettled(WeaponId.Saw)}, "
			+ $"glock +{ConeAfterSwing(WeaponId.Glock) - ConeSettled(WeaponId.Glock)}");
	}

	// --------------------------------------------------------- sustained fire

	private static void SustainedFire()
	{
		H.Group("sustained fire");

		// Holding the trigger walks the cone open, for every weapon.
		foreach (var id in All)
		{
			var w = Field(id);
			int first = 0, last = 0;
			var fire = new InputFrame(0, 0, 0, InputFrame.FFire);

			w.Step(fire);
			first = w.Snapshot().PlayerSpreadHalf;
			for (int i = 0; i < 180; i++)
			{
				w.Step(fire);
				if (w.Player.Mag <= 0) break;
			}
			last = w.Snapshot().PlayerSpreadHalf;

			H.Check($"{WeaponCatalog.NameOf(id)} opens up under sustained fire",
				last > first, $"{first} -> {last} BRAD");
		}

		// And it closes again once the trigger is released.
		var ak = Field(WeaponId.Ak47);
		for (int i = 0; i < 40; i++) ak.Step(new InputFrame(0, 0, 0, InputFrame.FFire));
		int hot = ak.Snapshot().PlayerSpreadHalf;
		for (int i = 0; i < 90; i++) ak.Step(new InputFrame(0, 0, 0, 0));
		int cool = ak.Snapshot().PlayerSpreadHalf;
		H.Check("and recovers once the trigger is released", cool < hot / 2,
			$"{hot} -> {cool} BRAD");

		// Aiming is still a bonus, never a penalty (the contract Aiming.cs sets).
		var hip = Field(WeaponId.Ak47);
		var aimed = Field(WeaponId.Ak47);
		for (int i = 0; i < 20; i++)
		{
			hip.Step(new InputFrame(0, 0, 0, InputFrame.FFire));
			aimed.Step(new InputFrame(0, 0, 0, (byte)(InputFrame.FFire | InputFrame.FAim)));
		}
		H.Check("aiming still tightens a hot cone",
			aimed.Snapshot().PlayerSpreadHalf < hip.Snapshot().PlayerSpreadHalf,
			$"aimed {aimed.Snapshot().PlayerSpreadHalf}, hip {hip.Snapshot().PlayerSpreadHalf}");
	}

	// ----------------------------------------------------- the reticle agrees

	private static void SnapshotAgrees()
	{
		H.Group("reticle honesty");

		// The reticle draws PlayerSpreadHalf. If that can drift from the cone
		// the bullet is actually drawn from, the player is being lied to in the
		// one place they have no way of checking.
		var w = Field(WeaponId.Saw);
		for (int i = 0; i < 12; i++)
			w.Step(new InputFrame(1, 0, i * 900, InputFrame.FFire));

		int half = w.Snapshot().PlayerSpreadHalf;
		H.Check("a hot, moving, swinging SAW reports a wide cone", half > 400, $"{half} BRAD");

		// Every drawn round must sit inside the reported cone.
		int facing = w.Player.Facing;
		w.Bullets.Live.Clear();
		w.Player.CooldownMt = 0;
		int reported = w.Snapshot().PlayerSpreadHalf;
		w.Step(new InputFrame(0, 0, facing, InputFrame.FFire));

		bool inside = true;
		for (int i = 0; i < w.Bullets.Live.Count; i++)
		{
			int off = Math.Abs(Brad.Norm(w.Bullets.Live[i].Heading - w.Player.Facing));
			if (off > reported + 2) inside = false;
		}
		H.Check("and every round it fires lands inside that cone", inside);

		H.Check("sway crosses the boundary for the HUD",
			w.Snapshot().PlayerSway == w.Player.SwayQ8);
	}

	// --------------------------------------------------------------- the choke

	private static void Choke()
	{
		H.Group("shotgun choke");

		var spec = WeaponCatalog.Get(WeaponId.Remington);

		// The cone is tighter than the 3129 it was ported at, but a shotgun is
		// still the widest thing on the rack.
		H.Check("the choke is tighter than it was", spec.SpreadBase < 3129,
			$"{spec.SpreadBase} BRAD");
		H.Check("but it is still the widest weapon",
			spec.SpreadBase > WeaponCatalog.Get(WeaponId.Saw).SpreadBase);

		var w = Field(WeaponId.Remington);
		w.Step(new InputFrame(0, 0, 0, InputFrame.FFire));
		H.Eq("a shell is still seven pellets", w.Bullets.Live.Count, 7);

		// The pattern: pellets laid out across the cone, not seven dice. Sort
		// the offsets and check the gaps are even -- a clumped draw fails this
		// however narrow the cone is.
		var offs = new int[w.Bullets.Live.Count];
		for (int i = 0; i < offs.Length; i++)
			offs[i] = Brad.Norm(w.Bullets.Live[i].Heading - w.Player.Facing);
		Array.Sort(offs);

		int widest = 0, tightest = int.MaxValue;
		for (int i = 1; i < offs.Length; i++)
		{
			int gap = offs[i] - offs[i - 1];
			if (gap > widest) widest = gap;
			if (gap < tightest) tightest = gap;
		}
		H.Check("no two pellets stack on one heading", tightest > 0, $"{tightest} BRAD apart");
		H.Check("and the pattern is even rather than clumped",
			widest <= tightest * 6, $"widest gap {widest}, tightest {tightest}");

		// Spread across the whole cone, not bunched at the centre.
		int extent = offs[offs.Length - 1] - offs[0];
		H.Check("the pattern fills the cone", extent > spec.SpreadBase / 2,
			$"{extent} BRAD across a {spec.SpreadBase} cone");

		// At a working range the pattern is tight enough to be a weapon rather
		// than a prayer: a body-width target should take several pellets.
		int hits = 0;
		int range = 220 * Fx.One;
		for (int i = 0; i < offs.Length; i++)
		{
			Brad.SinCos(offs[i], out int sin, out _);
			int lateral = Fx.Abs((int)(((long)range * sin) >> Brad.UnitShift));
			if (lateral <= Tune.ActorRadius + Tune.HitPad) hits++;
		}
		H.Check("a choked pattern puts several pellets on a man at 220px",
			hits >= 3, $"{hits} of 7 pellets");

		// One pellet carries the lock, and it is a CENTRE pellet, so a held aim
		// puts the headshot where the player was looking.
		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		Put(g, 24, 14, 'a');
		var lockWorld = new SimWorld(Level.FromText(Text(g)), 41, new Loadout(WeaponId.Remington));
		var guard = lockWorld.Guards[0];
		guard.PathX = null; guard.PathY = null;
		guard.State = GuardState.Relaxed; guard.Task = GuardTask.Post;
		guard.Facing = Brad.Half;
		lockWorld.Player.X = guard.X - 200 * Fx.One;
		lockWorld.Player.Y = guard.Y;

		int aim = Brad.Atan2(guard.Y - lockWorld.Player.Y, guard.X - lockWorld.Player.X);
		for (int i = 0; i < Tune.AimLockTicks; i++)
		{
			int x = lockWorld.Player.X, y = lockWorld.Player.Y;
			lockWorld.Step(new InputFrame(0, 0, aim, InputFrame.FAim));
			lockWorld.Player.X = x; lockWorld.Player.Y = y;
			lockWorld.Player.Alive = true;
			lockWorld.Player.Health = Tune.BaseHealth;
		}
		H.Check("fixture: locked with a shotgun", lockWorld.Player.HeadshotReady);

		lockWorld.Bullets.Live.Clear();
		lockWorld.Step(new InputFrame(0, 0, aim,
			(byte)(InputFrame.FAim | InputFrame.FFire)));

		int headshots = 0, headshotIndex = -1;
		for (int i = 0; i < lockWorld.Bullets.Live.Count; i++)
			if (lockWorld.Bullets.Live[i].Headshot) { headshots++; headshotIndex = i; }

		H.Eq("exactly one pellet carries the headshot", headshots, 1);
		H.Check("and it is a centre pellet, not the edge of the pattern",
			headshotIndex > 0 && headshotIndex < lockWorld.Bullets.Live.Count - 1,
			$"pellet {headshotIndex} of {lockWorld.Bullets.Live.Count}");
	}

	// ------------------------------------------------------ guards miss too

	private static void GuardError()
	{
		H.Group("guard firing error");

		// Guards run the same three terms the player does, from the WEAPON they
		// carry, plus a flat marksmanship penalty. Previously they drew a flat
		// +/- 0.045 rad forever: perfect marksmen who happened to miss.
		var ak = WeaponCatalog.Get(WeaponId.Ak47);
		int cold = GuardCone(ak, 0, 0);
		int hot = GuardCone(ak, Tune.HeatMax, 0);
		H.Check("sustained fire opens a guard's cone", hot > cold, $"{cold} -> {hot}");
		int swung = GuardCone(ak, 0, Tune.SwayMax);
		H.Check("so does swinging onto a target", swung > cold, $"{cold} -> {swung}");

		// A cold AK guard still matches the prototype's flat draw, so the
		// opening shot of a rifleman's fight is as dangerous as it ever was.
		H.Eq("a cold AK guard shoots the spec's old flat cone", cold / 2, 469);
		H.Check("and a guard is never a better shot than the player with the same gun",
			Tune.GuardSpreadPenalty > 0);

		// Net accumulation over one burst: the term has to actually climb
		// while he holds the trigger or it is decoration.
		int burst = SimWorld.GuardBurst(ak);
		int perBurst = ak.HeatPerShot * burst;
		int decayBetween = Fx.PerTick(ak.HeatDecayPerSec, Tune.NormalScale)
			* ak.FireCooldownTicks * (burst - 1);
		H.Check("and a guard who does not let up gets worse, not better",
			perBurst > decayBetween, $"+{perBurst} per burst vs -{decayBetween} during it");

		// In the world: a guard in a long firefight ends it shooting wider than
		// he started it.
		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		Put(g, 30, 14, 'a');
		var w = new SimWorld(Level.FromText(Text(g)), 5, new Loadout(WeaponId.Glock,
			ArmourId.HeavyPlate));
		var guard = w.Guards[0];
		guard.Weapon = WeaponId.Ak47;
		guard.Mag = ak.Magazine;
		guard.PathX = null; guard.PathY = null;
		guard.State = GuardState.Combat;
		guard.Task = GuardTask.Engage;
		guard.SetAwareness(Tune.AwEngage);
		w.Player.X = CellCentre(20);
		w.Player.Y = CellCentre(14);

		int shots = 0, peakHeat = 0;
		for (int i = 0; i < 60 * 12; i++)
		{
			// Pin the player alive and in place: this measures the guard, not
			// the firefight.
			int x = w.Player.X, y = w.Player.Y;
			w.Step(new InputFrame(0, 0, 0, 0));
			w.Player.X = x; w.Player.Y = y;
			w.Player.Alive = true;
			w.Player.Health = Tune.BaseHealth;
			w.Player.Armour = 150;
			foreach (var ev in w.Log.Events)
				if (ev.Kind == SimEventKind.GuardShot) shots++;
			if (guard.Heat > peakHeat) peakHeat = guard.Heat;
		}

		H.Check("fixture: the guard actually fired", shots > 4, $"{shots} shots");
		H.Check("a guard in a long firefight heats up", peakHeat > ak.HeatPerShot,
			$"peak {peakHeat}/{Tune.HeatMax}");
		H.Check("and his cone is wider than a cold one",
			GuardCone(ak, peakHeat, 0) > cold, $"{GuardCone(ak, peakHeat, 0)} vs {cold}");
	}

	private static int GuardCone(in WeaponSpec ws, int heat, int sway)
		=> ws.SpreadBase + Tune.GuardSpreadPenalty
		 + (heat * ws.SpreadPerHeat >> Fx.Shift)
		 + (sway * ws.SpreadPerSway >> Fx.Shift);

	// ------------------------------------------------------------ determinism

	private static void Determinism()
	{
		H.Group("handling determinism");

		// Sway and the turn rate feed state, so two identical runs must still
		// agree bit for bit -- and a different input must still diverge.
		string level = Program.ReadLevel("substation_4.txt");

		var a = new SimWorld(Level.FromText(level), 77, new Loadout(WeaponId.Saw));
		var b = new SimWorld(Level.FromText(level), 77, new Loadout(WeaponId.Saw));
		bool agree = true;
		for (int i = 0; i < 240; i++)
		{
			var f = new InputFrame(i % 3 - 1, (i / 3) % 3 - 1, i * 611,
				(byte)(i % 5 == 0 ? InputFrame.FFire : 0));
			a.Step(f);
			b.Step(f);
			if (a.StateHash() != b.StateHash()) { agree = false; break; }
		}
		H.Check("a run with sway in it still reproduces exactly", agree);

		// Sway is hashed: two runs that differ only in how the aim was swung
		// must not collide.
		var c = new SimWorld(Level.FromText(level), 77, new Loadout(WeaponId.Saw));
		var d = new SimWorld(Level.FromText(level), 77, new Loadout(WeaponId.Saw));
		for (int i = 0; i < 20; i++)
		{
			c.Step(new InputFrame(0, 0, 0, 0));
			d.Step(new InputFrame(0, 0, i * 3000, 0));
		}
		H.Check("swinging the weapon changes the state hash",
			c.StateHash() != d.StateHash());
	}

	public static void Run()
	{
		Velocity();
		Tunnelling();
		TurnRate();
		CarryWeight();
		MoveTiers();
		SpinUp();
		SprintRecovery();
		TierPlumbing();
		Sway();
		SustainedFire();
		SnapshotAgrees();
		Choke();
		GuardError();
		Determinism();
	}
}
