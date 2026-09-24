using System;
using System.Collections.Generic;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// Guards fire the gun they carry (Actor.Weapon, armed from the loot roll by
/// SimWorld.ArmGuard): its damage, pierce, pellets, cadence, magazine, reload,
/// spin-up and the specialists' traits. Every case runs a real guard in a real
/// fight through the sim; none calls GuardFire directly.
/// </summary>
public static class GuardWeapons
{
	private const int GW = Level.GW, GH = Level.GH;

	public static void Run()
	{
		H.Group("guards fire their own guns");
		Armed();
		EveryWeapon();
		HitsLikeTheGun();
		Bursts();
		SpinUp();
		Magazine();
		Grenades();
		Normalised();
	}

	// ---------------------------------------------------------------- fixture

	private static string Text(int guardCol, int playerCol)
	{
		var sb = new System.Text.StringBuilder();
		sb.Append("name: fixture\ngrid:\n");
		for (int r = 0; r < GH; r++)
		{
			for (int c = 0; c < GW; c++)
			{
				char ch = (r == 0 || r == GH - 1 || c == 0 || c == GW - 1) ? '#' : '.';
				if (r == 1 && c == 1) ch = '@';
				if (r == GH - 2 && c == GW - 2) ch = 'X';
				if (r == 14 && c == guardCol) ch = 'a';
				sb.Append(ch);
			}
			sb.Append('\n');
		}
		return sb.ToString();
	}

	private static int Centre(int cell) => cell * Level.CellFx + Level.CellFx / 2;

	/// <summary>One guard already in a fight with a player he can see, holding
	/// <paramref name="weapon"/> with a full magazine.</summary>
	private static SimWorld Fight(WeaponId weapon, int playerCol = 25, int guardCol = 40, ulong seed = 5)
	{
		var w = new SimWorld(Level.FromText(Text(guardCol, playerCol)), seed,
			new Loadout(WeaponId.Glock, ArmourId.None));
		var g = w.Guards[0];
		g.Weapon = weapon;
		g.Mag = WeaponCatalog.Get(weapon).Magazine;
		g.PathX = null; g.PathY = null;
		g.State = GuardState.Combat;
		g.Task = GuardTask.Engage;
		g.SetAwareness(Tune.AwEngage);
		w.Player.X = Centre(playerCol);
		w.Player.Y = Centre(14);
		// On target, with the aim delay a guard entering a fight really has.
		g.Facing = Brad.Atan2(w.Player.Y - g.Y, w.Player.X - g.X);
		g.AimMt = Tune.AimDelayTicks * Actor.Mt;
		return w;
	}

	/// <summary>Step with the player pinned in place and unkillable: these
	/// measure the guard. Returns the tick of every GuardShot.</summary>
	private static List<int> Shots(SimWorld w, int ticks, Action<int>? each = null)
	{
		var shots = new List<int>();
		int px = w.Player.X, py = w.Player.Y;
		for (int t = 0; t < ticks; t++)
		{
			w.Player.Health = 1_000_000;
			w.Step(new InputFrame(0, 0, 0, 0));
			w.Player.X = px; w.Player.Y = py; w.Player.Alive = true;
			foreach (var ev in w.Log.Events)
				if (ev.Kind == SimEventKind.GuardShot) shots.Add(t);
			each?.Invoke(t);
		}
		return shots;
	}

	// ------------------------------------------------------------------ cases

	private static void Armed()
	{
		string bad = "";
		int n = 0;
		foreach (string name in new[] { "substation_4.txt", "vault_row.txt", "zz_black_site.txt" })
		{
			var L = Level.FromText(Program.ReadLevel(name));
			for (ulong seed = 1; seed <= 5 && bad.Length == 0; seed++)
			{
				var w = new SimWorld(L, seed);
				foreach (var g in w.Guards)
				{
					n++;
					int first = -1;
					foreach (int id in g.Kit)
						if (GearCatalog.Get(id).Kind == GearKind.Weapon) { first = id; break; }
					var want = first < 0 ? WeaponId.Glock : (WeaponId)GearCatalog.Get(first).SimA;
					if (g.Weapon != want || g.Mag != WeaponCatalog.Get(want).Magazine)
						bad = $"{name} seed {seed} guard {g.Id}: holds {g.Weapon} ({g.Mag}), carries {want}";
				}
			}
		}
		H.Check("every guard holds the gun on his body, loaded", bad.Length == 0,
			bad.Length == 0 ? $"{n} guards" : bad);
	}

	private static void EveryWeapon()
	{
		string bad = "";
		for (int i = 0; i < WeaponCatalog.Count && bad.Length == 0; i++)
		{
			var id = (WeaponId)i;
			var spec = WeaponCatalog.Get(id);
			// 15 cells: inside every weapon's reach and past a grenadier's standoff.
			var w = Fight(id, playerCol: 25, guardCol: 40);
			int shotTick = -1;
			var fired = new List<Bullet>();
			Shots(w, 60 * 6, t =>
			{
				if (shotTick >= 0) return;
				foreach (var ev in w.Log.Events)
				{
					if (ev.Kind != SimEventKind.GuardShot) continue;
					shotTick = t;
					if (ev.Value != i) bad = $"{id}: the shot names weapon {ev.Value}";
					foreach (var b in w.Bullets.Live) if (!b.FromPlayer) fired.Add(b);
				}
			});
			if (bad.Length > 0) break;
			if (shotTick < 0) { bad = $"{id}: never fired"; break; }
			int want = spec.Grenade ? 1 : Math.Max(1, spec.Pellets);
			if (fired.Count != want) { bad = $"{id}: {fired.Count} projectiles, want {want}"; break; }
			var b0 = fired[0];
			var kind = spec.Grenade ? BulletKind.Grenade : spec.ArcTargets > 0 ? BulletKind.Arc
				: spec.WallPierce > 0 ? BulletKind.Pierce : BulletKind.Round;
			if (b0.Damage != spec.Damage || b0.ArmourPierce != spec.ArmourPierce || b0.Kind != kind)
				bad = $"{id}: round is {b0.Damage} dmg / {b0.ArmourPierce} pierce / {b0.Kind}";
		}
		H.Check("every weapon, in a guard's hands, fires its own rounds", bad.Length == 0, bad);
	}

	private static void HitsLikeTheGun()
	{
		// What reaches the player is the weapon's damage: a pistol and a sniper
		// rifle are no longer the same 50-point round.
		foreach (var id in new[] { WeaponId.Glock, WeaponId.Awm })
		{
			var w = Fight(id);
			int hurt = -1;
			Shots(w, 60 * 6, t =>
			{
				foreach (var ev in w.Log.Events)
					if (ev.Kind == SimEventKind.PlayerHurt && hurt < 0) hurt = ev.Value;
			});
			H.Eq($"a guard's {id} hits for the {id}'s damage", hurt, WeaponCatalog.Get(id).Damage);
		}

		// And a Tesla bolt is lethal through the heaviest plate there is.
		var tw = new SimWorld(Level.FromText(Text(40, 25)), 5, new Loadout(WeaponId.Glock, ArmourId.HeavyPlate));
		var tg = tw.Guards[0];
		tg.Weapon = WeaponId.Tesla; tg.Mag = 3;
		tg.PathX = null; tg.PathY = null;
		tg.State = GuardState.Combat; tg.Task = GuardTask.Engage; tg.SetAwareness(Tune.AwEngage);
		tw.Player.X = Centre(25); tw.Player.Y = Centre(14);
		for (int t = 0; t < 60 * 8 && tw.Over == null; t++) tw.Step(new InputFrame(0, 0, 0, 0));
		H.Check("a guard's Tesla kills through heavy plate", tw.Over == "dead", tw.Over ?? "alive");
	}

	private static void Bursts()
	{
		var ak = WeaponCatalog.Get(WeaponId.Ak47);
		int burst = SimWorld.GuardBurst(ak);
		H.Eq("an AK guard fires three-round bursts", burst, 3);
		H.Eq("a bolt-action guard fires one", SimWorld.GuardBurst(WeaponCatalog.Get(WeaponId.Awm)), 1);
		H.Check("a minigun guard fires long ones",
			SimWorld.GuardBurst(WeaponCatalog.Get(WeaponId.Vulcan)) >= 10);

		var shots = Shots(Fight(WeaponId.Ak47), 60 * 4);
		H.Check("fixture: the AK guard fired more than one burst", shots.Count > burst, $"{shots.Count}");
		if (shots.Count <= burst) return;
		bool cadence = true;
		for (int k = 1; k < burst; k++)
			if (shots[k] - shots[k - 1] != ak.FireCooldownTicks) cadence = false;
		H.Check("inside a burst, rounds leave at the weapon's cadence", cadence,
			string.Join(",", shots.GetRange(0, burst + 1)));
		H.Check("and between bursts he stops to re-aim",
			shots[burst] - shots[burst - 1] >= Tune.EngageCooldownTicks,
			$"{shots[burst] - shots[burst - 1]} ticks");
	}

	private static void SpinUp()
	{
		int ak = Shots(Fight(WeaponId.Ak47), 60 * 4)[0];
		var vs = Shots(Fight(WeaponId.Vulcan), 60 * 4);
		var spec = WeaponCatalog.Get(WeaponId.Vulcan);
		H.Check("a minigun guard spins up before his first round",
			vs.Count > 0 && vs[0] - ak >= spec.SpinUpTicks - 1, vs.Count > 0 ? $"AK {ak}, Vulcan {vs[0]}" : "never");
	}

	private static void Magazine()
	{
		var spec = WeaponCatalog.Get(WeaponId.Tesla);
		var shots = Shots(Fight(WeaponId.Tesla), 60 * 20);
		H.Check("fixture: the Tesla guard emptied his magazine", shots.Count > spec.Magazine, $"{shots.Count}");
		if (shots.Count <= spec.Magazine) return;
		int gap = shots[spec.Magazine] - shots[spec.Magazine - 1];
		H.Check("and had to reload before the next bolt", gap >= spec.ReloadTicks, $"{gap} ticks");
	}

	private static void Grenades()
	{
		// Start him close: he must back off past his own blast before throwing.
		var w = Fight(WeaponId.Frag, playerCol: 25, guardCol: 32);
		int thrown = 0, tooClose = 0;
		for (int t = 0; t < 60 * 10; t++)
		{
			w.Player.Health = 1_000_000;
			w.Player.X = Centre(25); w.Player.Y = Centre(14);
			w.Step(new InputFrame(0, 0, 0, 0));
			w.Player.Alive = true;
			foreach (var ev in w.Log.Events)
			{
				if (ev.Kind != SimEventKind.GrenadeThrown) continue;
				thrown++;
				if (Fx.Dist(w.Guards[0].X, w.Guards[0].Y, w.Player.X, w.Player.Y) < Tune.GuardGrenadeMinDist)
					tooClose++;
			}
		}
		H.Check("a guard with grenades throws them", thrown > 0, $"{thrown}");
		H.Eq("but never from inside his own blast", tooClose, 0);
	}

	private static void Normalised()
	{
		var w = Fight(WeaponId.Ak47);
		w.Guards[0].Weapon = (WeaponId)99;
		w.Guards[0].BurstShots = -4;
		int before = w.Normalised;
		w.Step(new InputFrame(0, 0, 0, 0));
		H.Check("a guard holding no real weapon is repaired to the Glock",
			w.Guards[0].Weapon == WeaponId.Glock && w.Normalised > before);
	}
}
