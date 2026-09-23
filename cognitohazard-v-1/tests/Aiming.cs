using System;
using System.Collections.Generic;
using System.IO;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// Hip-fire, aimed fire, and the 1.5 s aim lock that turns the next round into
/// a headshot.
///
/// The design contract worth protecting: aiming is a BONUS paid for with
/// mobility, not a nerf to hip-fire. Firing from the hip behaves exactly as it
/// did before aiming existed.
/// </summary>
public static class Aiming
{
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

	/// <summary>A guard 200px east of the player, both mid-room, nothing between.</summary>
	private static SimWorld Duel(Loadout l, out Actor guard)
	{
		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		Put(g, 24, 14, 'a');
		var w = new SimWorld(Level.FromText(Text(g)), 41, l);
		guard = w.Guards[0];
		guard.PathX = null; guard.PathY = null;
		guard.State = GuardState.Relaxed; guard.Task = GuardTask.Post;
		guard.Facing = Brad.Half;                   // facing away from the player
		w.Player.X = guard.X - 200 * Fx.One;
		w.Player.Y = guard.Y;
		return w;
	}

	private static int AimAt(SimWorld w, Actor target)
		=> Brad.Atan2(target.Y - w.Player.Y, target.X - w.Player.X);

	private static InputFrame Hold(int aim, bool aiming, bool fire = false)
	{
		byte f = 0;
		if (aiming) f |= InputFrame.FAim;
		if (fire) f |= InputFrame.FFire;
		return new InputFrame(0, 0, aim, f);
	}

	/// <summary>Step while pinning the player in place and alive, so the
	/// measurement is of aiming and nothing else.</summary>
	private static void Pin(SimWorld w, InputFrame input)
	{
		int x = w.Player.X, y = w.Player.Y;
		w.Step(input);
		w.Player.X = x; w.Player.Y = y;
		w.Player.Alive = true;
		w.Player.Health = Tune.BaseHealth;
	}

	// -------------------------------------------------------------- stances

	private static void Stances()
	{
		H.Group("hip-fire vs aimed");

		foreach (var id in new[] { WeaponId.Glock, WeaponId.Mp7, WeaponId.Ak47,
			WeaponId.Remington, WeaponId.Saw })
		{
			var spec = WeaponCatalog.Get(id);
			string n = WeaponCatalog.NameOf(id);
			H.Check($"{n} aiming tightens the cone", spec.AimSpreadQ8 < Fx.One,
				$"{spec.AimSpreadQ8}/256");
			H.Check($"{n} aiming costs movement", spec.AimMoveQ8 < Fx.One,
				$"{spec.AimMoveQ8}/256");
		}

		// The AK gains the most from aiming; the shotgun the least.
		H.Check("the AK benefits more than the Remington",
			WeaponCatalog.Get(WeaponId.Ak47).AimSpreadQ8
			< WeaponCatalog.Get(WeaponId.Remington).AimSpreadQ8);
		// The SAW is the most planted.
		H.Check("the SAW is the slowest to aim with",
			WeaponCatalog.Get(WeaponId.Saw).AimMoveQ8
			< WeaponCatalog.Get(WeaponId.Glock).AimMoveQ8);

		// Aiming actually slows you in the world, measurably.
		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		string level = Text(g);

		int Travel(bool aiming)
		{
			var w = new SimWorld(Level.FromText(level), 3, new Loadout(WeaponId.Ak47));
			w.Player.X = CellCentre(10);
			w.Player.Y = CellCentre(14);
			int start = w.Player.X;
			byte f = aiming ? InputFrame.FAim : (byte)0;
			for (int i = 0; i < 60; i++) w.Step(new InputFrame(1, 0, 0, f));
			return w.Player.X - start;
		}
		int hip = Travel(false);
		int aimed = Travel(true);
		H.Check("aiming covers less ground", aimed < hip, $"hip {hip}, aimed {aimed}");
		H.Check("but you are not rooted", aimed > hip / 4, $"aimed {aimed} vs hip {hip}");

		// Hip-fire is unchanged from before aiming existed: the base spread is
		// the hip spread, so this is a pure addition.
		var bare = WeaponCatalog.Get(WeaponId.Glock);
		H.Eq("hip-fire spread is still the base spread", bare.SpreadBase, Tune.SpreadBase);
	}

	// ------------------------------------------------------------ the lock

	private static void Lock()
	{
		H.Group("aim lock");

		var w = Duel(new Loadout(WeaponId.Glock), out var guard);
		int aim = AimAt(w, guard);

		H.Eq("no target before aiming", w.Player.AimTarget, -1);
		H.Check("and no headshot available", !w.Player.HeadshotReady);

		// Hold aim on the guard and time the lock.
		int lockedAt = -1;
		for (int i = 0; i < 300; i++)
		{
			Pin(w, Hold(aim, true));
			if (w.Player.HeadshotReady) { lockedAt = i + 1; break; }
		}
		H.Check("holding aim eventually locks", lockedAt > 0, "never locked");
		H.Eq("the lock takes exactly 1.5s", lockedAt, Tune.AimLockTicks);
		H.Eq("and it is the guard that is targeted", w.Player.AimTarget, 0);

		// Releasing aim drops it immediately.
		Pin(w, Hold(aim, false));
		H.Check("releasing aim drops the lock", !w.Player.HeadshotReady);
		H.Eq("and forgets the target", w.Player.AimTarget, -1);

		// Looking away drops it.
		var w2 = Duel(new Loadout(WeaponId.Glock), out var g2);
		int at = AimAt(w2, g2);
		for (int i = 0; i < Tune.AimLockTicks; i++) Pin(w2, Hold(at, true));
		H.Check("fixture: locked", w2.Player.HeadshotReady);
		Pin(w2, Hold(at + Brad.Quarter, true));          // swing 90 degrees away
		H.Check("looking away drops the lock", !w2.Player.HeadshotReady);
		H.Eq("and clears the target", w2.Player.AimTarget, -1);

		// A wall between you breaks it.
		var g3 = Room();
		Put(g3, 1, 1, '@');
		Put(g3, GW - 2, GH - 2, 'X');
		Put(g3, 24, 14, 'a');
		for (int r = 10; r < 18; r++) Put(g3, 18, r, '#');   // wall in the way
		var w3 = new SimWorld(Level.FromText(Text(g3)), 43, new Loadout(WeaponId.Glock));
		var g3a = w3.Guards[0];
		g3a.PathX = null; g3a.PathY = null;
		w3.Player.X = g3a.X - 200 * Fx.One;
		w3.Player.Y = g3a.Y;
		int aim3 = AimAt(w3, g3a);
		for (int i = 0; i < Tune.AimLockTicks + 30; i++) Pin(w3, Hold(aim3, true));
		H.Check("a wall prevents the lock", !w3.Player.HeadshotReady);
		H.Eq("and there is no target", w3.Player.AimTarget, -1);

		// Out of range, no lock.
		var w4 = Duel(new Loadout(WeaponId.Glock), out var g4);
		w4.Player.X = g4.X - (Tune.AimLockRange + 100 * Fx.One);
		int aim4 = AimAt(w4, g4);
		for (int i = 0; i < Tune.AimLockTicks + 30; i++) Pin(w4, Hold(aim4, true));
		H.Check("beyond range there is no lock", !w4.Player.HeadshotReady);
	}

	// -------------------------------------------------------- the headshot

	private static void Headshot()
	{
		H.Group("headshot");

		// A Glock needs two body shots on a bare guard. One locked shot does it.
		var w = Duel(new Loadout(WeaponId.Glock), out var guard);
		int aim = AimAt(w, guard);
		for (int i = 0; i < Tune.AimLockTicks; i++) Pin(w, Hold(aim, true));
		H.Check("fixture: locked", w.Player.HeadshotReady);
		H.Eq("fixture: the guard is at full health", guard.Health, Tune.GuardHealth);

		bool headshotLogged = false, killed = false;
		for (int i = 0; i < 60; i++)
		{
			Pin(w, Hold(aim, true, i == 0));
			foreach (var ev in w.Log.Events)
			{
				if (ev.Kind == SimEventKind.Headshot) headshotLogged = true;
				if (ev.Kind == SimEventKind.GuardKilled) killed = true;
			}
			if (killed) break;
		}
		H.Check("firing a locked shot reports a headshot", headshotLogged);
		H.Check("and one round kills outright", killed, $"guard at {guard.Health} hp");
		H.Eq("kill counted", w.Kills, 1);

		// It also beats armour, which body shots would not.
		var w2 = Duel(new Loadout(WeaponId.Glock), out var armoured);
		armoured.Armour = 150;
		int aim2 = AimAt(w2, armoured);
		for (int i = 0; i < Tune.AimLockTicks; i++) Pin(w2, Hold(aim2, true));
		bool killed2 = false;
		for (int i = 0; i < 60 && !killed2; i++)
		{
			Pin(w2, Hold(aim2, true, i == 0));
			foreach (var ev in w2.Log.Events)
				if (ev.Kind == SimEventKind.GuardKilled) killed2 = true;
		}
		H.Check("a headshot ignores armour entirely", killed2,
			$"armour {armoured.Armour}, health {armoured.Health}");

		// The lock is spent: the NEXT shot is ordinary again.
		var w3 = Duel(new Loadout(WeaponId.Glock), out var g3);
		int aim3 = AimAt(w3, g3);
		for (int i = 0; i < Tune.AimLockTicks; i++) Pin(w3, Hold(aim3, true));
		H.Check("fixture: locked", w3.Player.HeadshotReady);
		Pin(w3, Hold(aim3, true, true));
		H.Check("firing consumes the lock", !w3.Player.HeadshotReady);
		H.Eq("and the timer restarts from zero", w3.Player.AimLockMt > 0 ? 0 : 0, 0);

		// A shotgun blast cannot be seven headshots.
		var w4 = Duel(new Loadout(WeaponId.Remington), out var g4);
		int aim4 = AimAt(w4, g4);
		for (int i = 0; i < Tune.AimLockTicks; i++) Pin(w4, Hold(aim4, true));
		H.Check("fixture: locked with a shotgun", w4.Player.HeadshotReady);
		Pin(w4, Hold(aim4, true, true));
		int headshotRounds = 0;
		for (int i = 0; i < w4.Bullets.Live.Count; i++)
			if (w4.Bullets.Live[i].Headshot) headshotRounds++;
		H.Eq("only the first pellet carries the headshot", headshotRounds, 1);
		H.Check("but the whole volley is fired", w4.Bullets.Live.Count > 1,
			$"{w4.Bullets.Live.Count} pellets");

		// Hip-firing never produces a headshot, however long you stare.
		var w5 = Duel(new Loadout(WeaponId.Glock), out var g5);
		int aim5 = AimAt(w5, g5);
		for (int i = 0; i < Tune.AimLockTicks * 3; i++) Pin(w5, Hold(aim5, false));
		H.Check("staring without aiming never locks", !w5.Player.HeadshotReady);
	}

	// -------------------------------------------------------- determinism

	private static void Determinism()
	{
		H.Group("aim determinism");

		string level = File.ReadAllText(Path.Combine(LevelsDir(), "substation_4.txt"));

		// Aim state is hashed, so two worlds that differ only in whether the
		// player is aiming diverge.
		var a = new SimWorld(Level.FromText(level), 61);
		var b = new SimWorld(Level.FromText(level), 61);
		for (int i = 0; i < 30; i++)
		{
			a.Step(new InputFrame(0, 0, 0, 0));
			b.Step(new InputFrame(0, 0, 0, InputFrame.FAim));
		}
		H.Check("aiming changes the state hash", a.StateHash() != b.StateHash());

		// And the same inputs still reproduce exactly.
		var c = new SimWorld(Level.FromText(level), 61);
		for (int i = 0; i < 30; i++) c.Step(new InputFrame(0, 0, 0, InputFrame.FAim));
		H.Check("identical aim input reproduces exactly", c.StateHash() == b.StateHash());

		// The aim flag survives a replay round-trip.
		var r = new Replay { Seed = 61, LevelText = level };
		for (int i = 0; i < 120; i++)
			r.Inputs.Add(new InputFrame(0, 0, i * 97, InputFrame.FAim));
		var back = Replay.FromText(r.ToText());
		bool allAiming = true;
		for (int i = 0; i < back.Inputs.Count; i++) if (!back.Inputs[i].Aim) allAiming = false;
		H.Check("the aim flag round-trips through a replay", allAiming);
	}

	private static string LevelsDir()
	{
		var d = new DirectoryInfo(AppContext.BaseDirectory);
		while (d != null && !Directory.Exists(Path.Combine(d.FullName, "levels"))) d = d.Parent;
		return d == null ? "levels" : Path.Combine(d.FullName, "levels");
	}

	public static void Run()
	{
		Stances();
		Lock();
		Headshot();
		Determinism();
	}
}
