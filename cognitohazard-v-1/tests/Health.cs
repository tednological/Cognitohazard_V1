using System;
using System.Collections.Generic;
using System.IO;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// Milestone 8: health, armour and damage (RPG extension plan §2, §3).
///
/// The headline assertion is the shots-to-kill table. It is the number the
/// project owner specifies, and it is the one thing in this milestone that is a
/// design commitment rather than an implementation detail. The plan's was 1-2
/// rounds unarmoured and 4-5 in the best armour, at a 100 pool; the owner then
/// DOUBLED the player's health, which makes it 3-4 and 5-7, and the table below
/// was re-pinned to that on purpose.
/// </summary>
public static class Health
{
	// A fixture round driven straight into the player: the old flat guard
	// rifle's figures. Guards now fire their own weapons (Actor.Weapon), and
	// these tests are about what a round does to a PLAYER, not who fired it.
	private const int FixtureRoundSpeed = 563200;   // 2200 px/s
	private const int FixtureRoundDamage = 50;

	private const int GW = Level.GW, GH = Level.GH;

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

	// ------------------------------------------------------------- the pool

	private static void Pools()
	{
		H.Group("health pools");

		var a = new Actor();
		H.Eq("actors start at full health", a.Health, Tune.BaseHealth);
		H.Eq("and unarmoured by default", a.Armour, 0);

		// Armour absorbs first, one for one, remainder carries into health.
		a.Armour = 30;
		bool dead = a.TakeDamage(50, out int absorbed);
		H.Check("50 damage through 30 armour does not kill", !dead);
		H.Eq("armour absorbs what it can", absorbed, 30);
		H.Eq("armour is spent", a.Armour, 0);
		H.Eq("the remainder hits health", a.Health, Tune.BaseHealth - 20);

		// Partial absorption leaves armour behind.
		var b = new Actor { Armour = 100 };
		b.TakeDamage(40, out int absorbed2);
		H.Eq("a small hit is fully absorbed", absorbed2, 40);
		H.Eq("armour remains", b.Armour, 60);
		H.Eq("health is untouched", b.Health, Tune.BaseHealth);

		// Exact lethality, not "less than".
		var c = new Actor();
		H.Check("exactly lethal damage kills", c.TakeDamage(Tune.BaseHealth, out _));
		H.Eq("health floors at zero", c.Health, 0);
		H.Check("and the actor is not alive", !c.Alive);

		var d = new Actor();
		H.Check("one point short does not kill", !d.TakeDamage(Tune.BaseHealth - 1, out _));
		H.Eq("leaving a single point", d.Health, 1);

		// Damage to the dead is inert.
		var e = new Actor();
		e.TakeDamage(999, out _);
		H.Check("a dead actor takes no further damage", !e.TakeDamage(999, out _));

		H.Check("zero damage is a no-op", !new Actor().TakeDamage(0, out _));
		H.Check("negative damage is a no-op", !new Actor().TakeDamage(-50, out _));
	}

	// -------------------------------------------------- armour catalogue

	private static void ArmourTiers()
	{
		H.Group("armour tiers");

		H.Eq("four tiers", ArmourCatalog.Count, 4);
		H.Eq("none gives no armour", ArmourCatalog.Get(ArmourId.None).Armour, 0);
		H.Eq("light weave", ArmourCatalog.Get(ArmourId.LightWeave).Armour, 50);
		H.Eq("medium carrier", ArmourCatalog.Get(ArmourId.MediumCarrier).Armour, 100);
		H.Eq("heavy plate", ArmourCatalog.Get(ArmourId.HeavyPlate).Armour, 150);

		H.Eq("unknown armour id clamps", (int)ArmourCatalog.Clamp(99), (int)ArmourId.None);

		// The trade: heavier plate is slower and louder, so it is a choice.
		var none = ArmourCatalog.Get(ArmourId.None);
		var heavy = ArmourCatalog.Get(ArmourId.HeavyPlate);
		H.Check("heavy plate walks slower", heavy.WalkSpeed < none.WalkSpeed);
		H.Check("heavy plate sneaks slower", heavy.SneakSpeed < none.SneakSpeed);
		H.Check("heavy plate is louder on foot", heavy.FootstepRadius > none.FootstepRadius);

		// Light weave is the one free-ish tier: armour with no mobility cost.
		var light = ArmourCatalog.Get(ArmourId.LightWeave);
		H.Eq("light weave costs no speed", light.WalkSpeed, none.WalkSpeed);
		H.Check("but still gives armour", light.Armour > 0);
	}

	// --------------------------------------------- the shots-to-kill table

	/// <summary>
	/// Rounds needed to drop a target at (BaseHealth + armour) effective health.
	/// Pure arithmetic against the catalogue, so a tuning change that breaks the
	/// design brief fails here rather than being discovered in play.
	/// </summary>
	private static int ShotsToKill(int damage, int armour)
	{
		var target = new Actor { Armour = armour };
		int shots = 0;
		while (target.Alive && shots < 1000)
		{
			target.TakeDamage(damage, out _);
			shots++;
		}
		return shots;
	}

	private static void ShotsTable()
	{
		H.Group("shots-to-kill (RPG plan §3)");

		var armours = new[]
		{
			(ArmourId.None, 0), (ArmourId.LightWeave, 50),
			(ArmourId.MediumCarrier, 100), (ArmourId.HeavyPlate, 150),
		};

		// weapon -> expected shots at none / light / medium / heavy
		var table = new (string Name, int Damage, int[] Want)[]
		{
			("glock", WeaponCatalog.Get(WeaponId.Glock).Damage, new[] { 4, 5, 6, 7 }),
			("mp7", WeaponCatalog.Get(WeaponId.Mp7).Damage, new[] { 4, 5, 6, 7 }),
			("ak47", WeaponCatalog.Get(WeaponId.Ak47).Damage, new[] { 3, 4, 5, 5 }),
			("saw", WeaponCatalog.Get(WeaponId.Saw).Damage, new[] { 4, 5, 6, 7 }),
		};

		Console.WriteLine();
		Console.WriteLine("  shots to kill:");
		Console.WriteLine("    weapon        dmg   none  light  medium  heavy");

		foreach (var (name, damage, want) in table)
		{
			var got = new int[armours.Length];
			for (int i = 0; i < armours.Length; i++)
			{
				got[i] = ShotsToKill(damage, armours[i].Item2);
				H.Eq($"{name} vs {ArmourCatalog.NameOf(armours[i].Item1)}", got[i], want[i]);
			}
			Console.WriteLine($"    {name,-12}  {damage,3}   {got[0],4}  {got[1],5}  {got[2],6}  {got[3],5}");
		}
		Console.WriteLine();

		// The brief, asserted directly rather than inferred from the table.
		int pistol = WeaponCatalog.Get(WeaponId.Glock).Damage;
		int bare = ShotsToKill(pistol, 0), plated = ShotsToKill(pistol, 150);
		H.Check("unarmoured dies in 3-4 rounds", bare >= 3 && bare <= 4, $"{bare}");
		H.Check("best armour survives to 6-7 rounds", plated >= 6 && plated <= 7,
			$"{plated}");

		// A full shotgun connection USED to kill an unarmoured player outright
		// (100 pool). At 200 it takes most of one, and the second kills.
		int pellet = WeaponCatalog.Get(WeaponId.Remington).Damage;
		int pellets = WeaponCatalog.Get(WeaponId.Remington).Pellets;
		H.Check("a full shotgun blast takes most of an unarmoured target",
			pellet * pellets * 4 >= Tune.BaseHealth * 3, $"{pellet * pellets} damage");
		H.Check("and two kill it", 2 * pellet * pellets >= Tune.BaseHealth,
			$"{2 * pellet * pellets} damage");
		H.Check("but a single stray pellet barely scratches",
			ShotsToKill(pellet, 0) >= 5, $"{ShotsToKill(pellet, 0)} pellets");
	}

	// ------------------------------------------------------- in the world

	private static void InWorld()
	{
		H.Group("health in play");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		Put(g, 20, 14, 'a');
		string level = Text(g);

		// The player starts with the armour they equipped.
		foreach (var id in new[] { ArmourId.None, ArmourId.LightWeave, ArmourId.HeavyPlate })
		{
			var w = new SimWorld(Level.FromText(level), 1, new Loadout(WeaponId.Glock, id));
			H.Eq($"{ArmourCatalog.NameOf(id)} starts with its pool",
				w.Player.Armour, ArmourCatalog.Get(id).Armour);
			H.Eq("and full health", w.Player.Health, Tune.BaseHealth);
		}

		// Guards run their own, shallower pool, behind whatever plate the loot
		// roll dressed them in. See ArmouredGuards() for the full contract.
		var world = new SimWorld(Level.FromText(level), 1);
		H.Eq("guards start on the guard pool", world.Guards[0].Health, Tune.GuardHealth);
		H.Check("and wear whatever plate they were rolled",
			world.Guards[0].Armour == world.Guards[0].ArmourMax,
			$"{world.Guards[0].Armour}/{world.Guards[0].ArmourMax}");

		// A pistol needs two rounds on a guard, and the first one is survived.
		var w2 = new SimWorld(Level.FromText(level), 3);
		var guard = w2.Guards[0];
		guard.PathX = null; guard.PathY = null;
		w2.Player.X = guard.X - 80 * Fx.One;
		w2.Player.Y = guard.Y;

		int hurtEvents = 0, killEvents = 0;
		for (int i = 0; i < 600 && guard.State != GuardState.Dead; i++)
		{
			int kx = w2.Player.X, ky = w2.Player.Y;
			// Aim at the guard every tick: a survivor hunts, so a fixed heading
			// would miss everything after the first round.
			int aim = Brad.Atan2(guard.Y - w2.Player.Y, guard.X - w2.Player.X);
			w2.Step(new InputFrame(0, 0, aim, InputFrame.FFire));
			w2.Player.X = kx; w2.Player.Y = ky;
			w2.Player.Alive = true;
			w2.Player.Health = Tune.BaseHealth;
			w2.Player.Mag = Tune.Magazine;
			foreach (var ev in w2.Log.Events)
			{
				if (ev.Kind == SimEventKind.GuardHurt) hurtEvents++;
				if (ev.Kind == SimEventKind.GuardKilled) killEvents++;
			}
		}
		H.Check("a guard survives the first pistol round", hurtEvents >= 1,
			$"{hurtEvents} hurt events");
		H.Eq("and dies on a later one", killEvents, 1);

		// Taking a hit raises the alarm even when survived.
		var w3 = new SimWorld(Level.FromText(level), 5,
			new Loadout(WeaponId.Glock, ArmourId.HeavyPlate));
		// Mid-room, not at spawn: spawn is cell (1,1), and a round placed 20px
		// west of it starts inside the boundary wall and dies on impact.
		w3.Player.X = CellCentre(24);
		w3.Player.Y = CellCentre(14);
		H.Eq("fixture: alarm starts at 0", w3.Alarm.Level, 0);
		int before = w3.Player.Armour;
		// Drive a guard bullet into the player directly through the sim.
		w3.Bullets.Spawn(w3.Player.X - 20 * Fx.One, w3.Player.Y, 0,
			FixtureRoundSpeed, 10, false, FixtureRoundDamage);
		for (int i = 0; i < 5; i++) w3.Step(new InputFrame(0, 0, 0, 0));
		H.Check("being shot costs armour", w3.Player.Armour < before,
			$"{before} -> {w3.Player.Armour}");
		H.Eq("and raises the alarm to 2", w3.Alarm.Level, 2);
		H.Check("the player survives a round in heavy plate", w3.Player.Alive);
	}

	// ------------------------------------------------------------ armour break

	private static void ArmourBreak()
	{
		H.Group("armour break");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		string level = Text(g);

		var w = new SimWorld(Level.FromText(level), 7,
			new Loadout(WeaponId.Glock, ArmourId.LightWeave));
		w.Player.X = CellCentre(24);
		w.Player.Y = CellCentre(14);

		int hurt = 0, broke = 0, died = 0;
		for (int shot = 0; shot < 6; shot++)
		{
			w.Bullets.Spawn(w.Player.X - 20 * Fx.One, w.Player.Y, 0,
				FixtureRoundSpeed, 10, false, FixtureRoundDamage);
			for (int i = 0; i < 4; i++)
			{
				w.Step(new InputFrame(0, 0, 0, 0));
				foreach (var ev in w.Log.Events)
				{
					if (ev.Kind == SimEventKind.PlayerHurt) hurt++;
					if (ev.Kind == SimEventKind.ArmourBroken) broke++;
					if (ev.Kind == SimEventKind.PlayerKilled) died++;
				}
			}
			if (died > 0) break;
		}

		H.Check("being hit reports PlayerHurt", hurt >= 1, $"{hurt}");
		H.Eq("armour breaking is reported exactly once", broke, 1);
		H.Eq("and death is reported once", died, 1);

		// 50 armour + 200 health against 50-damage rounds is exactly five.
		H.Eq("light weave survives four rounds and dies on the fifth", hurt, 4);
	}

	// ------------------------------------------------------------- hashing

	private static void Hashing()
	{
		H.Group("armour hashing");

		string level = File.ReadAllText(Path.Combine(LevelsDir(), "substation_4.txt"));

		var bare = new SimWorld(Level.FromText(level), 11, new Loadout(WeaponId.Glock));
		var clad = new SimWorld(Level.FromText(level), 11,
			new Loadout(WeaponId.Glock, ArmourId.HeavyPlate));
		H.Check("armour changes the state hash", bare.StateHash() != clad.StateHash());

		// Armour travels in the replay text.
		var r = new Replay
		{
			Seed = 11,
			LevelText = level,
			Loadout = new Loadout(WeaponId.Remington, ArmourId.MediumCarrier),
		};
		r.Inputs.Add(new InputFrame(0, 0, 0, 0));
		string text = r.ToText();
		H.Check("the armour is written", text.Contains("armour=2"));
		var back = Replay.FromText(text);
		H.Eq("armour survives the round-trip", (int)back.Loadout.Armour, (int)ArmourId.MediumCarrier);
		H.Eq("and so does the weapon", (int)back.Loadout.Weapon, (int)WeaponId.Remington);

		// A replay from before armour existed still parses.
		H.Eq("a loadout line with no armour defaults to none",
			(int)Loadout.FromText("weapon=1").Armour, (int)ArmourId.None);
	}

	private static string LevelsDir()
	{
		var d = new DirectoryInfo(AppContext.BaseDirectory);
		while (d != null && !Directory.Exists(Path.Combine(d.FullName, "levels"))) d = d.Parent;
		return d == null ? "levels" : Path.Combine(d.FullName, "levels");
	}

	// ------------------------------------------------------ armoured guards

	/// <summary>
	/// Guards are shallow and variably plated, rather than uniformly deep. The
	/// design claim under test: what separates a dangerous guard from a soft one
	/// is the ARMOUR he is wearing, and the player can tell which is which.
	/// </summary>
	private static void ArmouredGuards()
	{
		H.Group("armoured guards");

		H.Check("a guard has a shallower pool than the player",
			Tune.GuardHealth < Tune.BaseHealth,
			$"guard {Tune.GuardHealth}, player {Tune.BaseHealth}");

		// Shots to kill a guard, by what he is wearing. This is the spread the
		// whole change exists to produce.
		int Kill(int damage, int armour)
		{
			var t = new Actor { Health = Tune.GuardHealth, Armour = armour };
			int n = 0;
			while (t.Alive && n < 1000) { t.TakeDamage(damage, out _); n++; }
			return n;
		}

		int glock = WeaponCatalog.Get(WeaponId.Glock).Damage;
		int ak = WeaponCatalog.Get(WeaponId.Ak47).Damage;

		Console.WriteLine();
		Console.WriteLine("  shots to kill a guard:");
		Console.WriteLine("    weapon    bare  light  medium  heavy");
		Console.WriteLine($"    glock     {Kill(glock, 0),4}  {Kill(glock, 50),5}"
			+ $"  {Kill(glock, 100),6}  {Kill(glock, 150),5}");
		Console.WriteLine($"    ak47      {Kill(ak, 0),4}  {Kill(ak, 50),5}"
			+ $"  {Kill(ak, 100),6}  {Kill(ak, 150),5}");
		Console.WriteLine();

		H.Eq("a rifle drops a bare guard in one", Kill(ak, 0), 1);
		H.Eq("a pistol takes two", Kill(glock, 0), 2);
		H.Check("but a heavy plate costs the rifle three or four",
			Kill(ak, 150) >= 3 && Kill(ak, 150) <= 4, $"{Kill(ak, 150)}");
		H.Check("and the pistol four or five",
			Kill(glock, 150) >= 4 && Kill(glock, 150) <= 5, $"{Kill(glock, 150)}");
		H.Check("armour, not health, is what makes the difference",
			Kill(ak, 150) >= Kill(ak, 0) * 3,
			$"bare {Kill(ak, 0)}, plated {Kill(ak, 150)}");

		// In a real level: guards spawn on the guard pool, and the vest they
		// were rolled as loot is the vest that is actually stopping rounds.
		var w = new SimWorld(Level.FromText(Program.ReadLevel("substation_4.txt")), 11);
		H.Check("fixture: the level has guards", w.Guards.Count > 0);

		int plated = 0, bare = 0;
		bool kitAgrees = true;
		for (int i = 0; i < w.Guards.Count; i++)
		{
			var g = w.Guards[i];
			if (g.ArmourMax > 0) plated++; else bare++;

			H.Eq($"guard {i} spawns on the guard pool", g.Health, Tune.GuardHealth);

			// Whatever he is wearing must be the vest on his body, or the plate
			// the player shot through is not the plate they can then take.
			int worn = 0;
			for (int k = 0; k < g.Kit.Count; k++)
			{
				var item = GearCatalog.Get(g.Kit[k]);
				if (item.Kind == GearKind.Armour)
					worn = ArmourCatalog.Get(ArmourCatalog.Clamp(item.SimA)).Armour;
			}
			if (worn != g.ArmourMax) kitAgrees = false;
		}

		H.Check("some guards are plated", plated > 0, $"{plated} of {w.Guards.Count}");
		H.Check("and some are not", bare > 0, $"{bare} of {w.Guards.Count}");
		H.Check("a guard's plate is the vest on his body", kitAgrees);

		// Armour starts full.
		bool full = true;
		for (int i = 0; i < w.Guards.Count; i++)
			if (w.Guards[i].Armour != w.Guards[i].ArmourMax) full = false;
		H.Check("and it starts intact", full);

		// Rolled from the loot stream, so the same seed dresses the same floor.
		var again = new SimWorld(Level.FromText(Program.ReadLevel("substation_4.txt")), 11);
		bool same = true;
		for (int i = 0; i < w.Guards.Count; i++)
			if (again.Guards[i].ArmourMax != w.Guards[i].ArmourMax) same = false;
		H.Check("the same seed dresses the same floor", same);
	}

	/// <summary>
	/// The answers to a plate. Without these, armoured guards are a wall rather
	/// than a problem with a solution.
	/// </summary>
	private static void ArmourAnswers()
	{
		H.Group("answers to a plate");

		int glock = WeaponCatalog.Get(WeaponId.Glock).Damage;

		// Armour-piercing ammo. Its whole job is this case.
		var ap = new Loadout(WeaponId.Glock, ArmourId.None, ammo: 3).Spec;
		H.Check("AP ammo pierces", ap.ArmourPierce > 0);

		int Rounds(int damage, int pierce, int armour)
		{
			var t = new Actor { Health = Tune.GuardHealth, Armour = armour };
			int n = 0;
			while (t.Alive && n < 1000) { t.TakeDamage(damage, pierce, out _); n++; }
			return n;
		}

		int plain = Rounds(glock, 0, 150);
		int piercing = Rounds(ap.Damage, ap.ArmourPierce, 150);
		H.Check("and gets through a heavy plate faster than ball does",
			piercing < plain, $"ball {plain}, AP {piercing}");

		// A headshot ignores armour entirely, so a held aim beats any plate.
		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		Put(g, 24, 14, 'a');
		var w = new SimWorld(Level.FromText(Text(g)), 41, new Loadout(WeaponId.Glock));
		var guard = w.Guards[0];
		guard.PathX = null; guard.PathY = null;
		guard.State = GuardState.Relaxed; guard.Task = GuardTask.Post;
		guard.Facing = Brad.Half;
		guard.Armour = 150;
		guard.ArmourMax = 150;
		w.Player.X = guard.X - 200 * Fx.One;
		w.Player.Y = guard.Y;

		int aim = Brad.Atan2(guard.Y - w.Player.Y, guard.X - w.Player.X);
		void Pin(byte flags)
		{
			int x = w.Player.X, y = w.Player.Y;
			w.Step(new InputFrame(0, 0, aim, flags));
			w.Player.X = x; w.Player.Y = y;
			w.Player.Alive = true;
			w.Player.Health = Tune.BaseHealth;
		}

		for (int i = 0; i < Tune.AimLockTicks; i++) Pin(InputFrame.FAim);
		H.Check("fixture: locked on a heavily plated guard", w.Player.HeadshotReady);

		bool killed = false;
		for (int i = 0; i < 60 && !killed; i++)
		{
			Pin((byte)(InputFrame.FAim | (i == 0 ? InputFrame.FFire : 0)));
			foreach (var ev in w.Log.Events)
				if (ev.Kind == SimEventKind.GuardKilled) killed = true;
		}
		H.Check("one locked headshot drops him through the plate", killed,
			$"armour {guard.Armour}, health {guard.Health}");
	}

	/// <summary>
	/// A round a plate ate must not look like a round that landed. These events
	/// are the only thing telling the player their weapon is not working.
	/// </summary>
	private static void ArmourFeedback()
	{
		H.Group("armour feedback");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		Put(g, 24, 14, 'a');
		var w = new SimWorld(Level.FromText(Text(g)), 41, new Loadout(WeaponId.Glock));
		var guard = w.Guards[0];
		guard.PathX = null; guard.PathY = null;
		guard.State = GuardState.Relaxed; guard.Task = GuardTask.Post;
		guard.Armour = 150;
		guard.ArmourMax = 150;

		int armourHits = 0, fleshHits = 0, broke = 0, killed = 0;
		for (int shot = 0; shot < 8 && killed == 0; shot++)
		{
			w.Bullets.Spawn(guard.X - 40 * Fx.One, guard.Y, 0,
				Tune.PlayerBulletSpeed, 10, true, WeaponCatalog.Get(WeaponId.Glock).Damage);
			for (int i = 0; i < 4; i++)
			{
				w.Step(new InputFrame(0, 0, 0, 0));
				foreach (var ev in w.Log.Events)
				{
					if (ev.Kind == SimEventKind.GuardArmourHit) armourHits++;
					if (ev.Kind == SimEventKind.GuardHurt) fleshHits++;
					if (ev.Kind == SimEventKind.GuardArmourBroken) broke++;
					if (ev.Kind == SimEventKind.GuardKilled) killed++;
				}
			}
		}

		H.Check("rounds stopped by the plate are reported as such", armourHits >= 2,
			$"{armourHits} armour hits");
		H.Eq("the plate failing is reported exactly once", broke, 1);
		H.Check("and rounds that got through are reported separately", fleshHits >= 1,
			$"{fleshHits} flesh hits");
		H.Eq("and he dies once", killed, 1);

		// A bare guard produces no armour events at all, so the two cases are
		// genuinely distinguishable rather than both firing everything.
		var w2 = new SimWorld(Level.FromText(Text(g)), 41, new Loadout(WeaponId.Glock));
		var bare = w2.Guards[0];
		bare.PathX = null; bare.PathY = null;
		bare.State = GuardState.Relaxed; bare.Task = GuardTask.Post;
		bare.Armour = 0;
		bare.ArmourMax = 0;

		int spurious = 0;
		w2.Bullets.Spawn(bare.X - 40 * Fx.One, bare.Y, 0,
			Tune.PlayerBulletSpeed, 10, true, WeaponCatalog.Get(WeaponId.Glock).Damage);
		for (int i = 0; i < 6; i++)
		{
			w2.Step(new InputFrame(0, 0, 0, 0));
			foreach (var ev in w2.Log.Events)
				if (ev.Kind == SimEventKind.GuardArmourHit
					|| ev.Kind == SimEventKind.GuardArmourBroken) spurious++;
		}
		H.Eq("an unarmoured guard reports no armour events", spurious, 0);

		// game/ mirrors these ordinals by hand, so the two new kinds must be
		// the LAST two. Inserting one above would remap every sound and effect.
		H.Eq("GuardArmourHit is 24", (int)SimEventKind.GuardArmourHit, 24);
		H.Eq("and GuardArmourBroken 25", (int)SimEventKind.GuardArmourBroken, 25);
	}

	public static void Run()
	{
		Pools();
		ArmourTiers();
		ShotsTable();
		ArmouredGuards();
		ArmourAnswers();
		ArmourFeedback();
		InWorld();
		ArmourBreak();
		Hashing();
	}
}
