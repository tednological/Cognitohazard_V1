using System;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// The three specialist weapons, each of which breaks one rule every other
/// weapon obeys -- and each of which is only balanced if the price of breaking
/// it is actually charged:
///   - the Tesla kills more than one thing, and must come back for a shooter
///     who fires it too close;
///   - the AWM goes through walls, and must lose something at each one;
///   - the Frag is thrown, not fired, and its shrapnel must have no side.
///
/// Every case runs THROUGH the sim from an InputFrame, not by calling the
/// resolvers, because the interesting failures are in the wiring: a bolt that
/// never discharges, a round that pierces but still reports a wall stop, a
/// grenade that explodes and hits nobody because fragments kept their side.
/// </summary>
public static class Specialists
{
	private const int GW = Level.GW, GH = Level.GH;

	public static void Run()
	{
		Catalog();
		Tesla();
		Awm();
		Frag();
	}

	// ---------------------------------------------------------------- fixture

	private static char[] Room()
	{
		var g = new char[GW * GH];
		for (int r = 0; r < GH; r++)
			for (int c = 0; c < GW; c++)
				g[r * GW + c] = (r == 0 || r == GH - 1 || c == 0 || c == GW - 1) ? '#' : '.';
		return g;
	}

	private static void Put(char[] g, int c, int r, char ch) => g[r * GW + c] = ch;

	/// <summary>A full-height wall one cell thick at column c.</summary>
	private static void WallAt(char[] g, int c)
	{
		for (int r = 1; r < GH - 1; r++) Put(g, c, r, '#');
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

	private static int Centre(int cell) => cell * Level.CellFx + Level.CellFx / 2;

	/// <summary>The player stood at (cx, row 14) facing due east, with the
	/// exit tucked in a corner out of the way.</summary>
	private static SimWorld World(char[] g, WeaponId w, int cx = 5, int cy = 14)
	{
		Put(g, 1, 1, '@');
		Put(g, GW - 2, 1, 'X');
		var world = new SimWorld(Level.FromText(Text(g)), 23, new Loadout(w));
		world.Player.X = Centre(cx);
		world.Player.Y = Centre(cy);
		world.Player.Facing = 0;
		Bare(world);
		return world;
	}

	/// <summary>
	/// Strip the vests the loot roll put on the fixture's guards. A guard's
	/// plate comes from the seed, so a test that assumes a bare guard and does
	/// not say so passes or fails on which seed it happened to use. Tests that
	/// care about armour set it themselves, after this.
	/// </summary>
	private static void Bare(SimWorld w)
	{
		for (int i = 0; i < w.Guards.Count; i++)
		{
			w.Guards[i].Armour = 0;
			w.Guards[i].ArmourMax = 0;
		}
	}

	private static readonly InputFrame Idle = new InputFrame(0, 0, 0, 0);
	private static readonly InputFrame Shoot = new InputFrame(0, 0, 0, InputFrame.FFire);

	private static readonly InputFrame Aimed =
		new InputFrame(0, 0, 0, (byte)(InputFrame.FFire | InputFrame.FAim));
	private static readonly InputFrame AimOnly = new InputFrame(0, 0, 0, InputFrame.FAim);

	/// <summary>One trigger pull, then let the world run. With aim held
	/// throughout when asked: a sniper rifle is not fired from the hip.</summary>
	private static int FireOnce(SimWorld w, int ticks, SimEventKind watch = SimEventKind.Blast,
		bool aim = false)
	{
		int seen = 0;
		w.Step(aim ? Aimed : Shoot);
		for (int i = 0; i < ticks && w.Over == null; i++)
		{
			w.Step(aim ? AimOnly : Idle);
			for (int e = 0; e < w.Log.Events.Count; e++)
				if (w.Log.Events[e].Kind == watch) seen++;
		}
		return seen;
	}

	private static int Dead(SimWorld w)
	{
		int n = 0;
		for (int i = 0; i < w.Guards.Count; i++) if (!w.Guards[i].Alive) n++;
		return n;
	}

	// ---------------------------------------------------------------- catalog

	private static void Catalog()
	{
		H.Group("specialists / catalog");

		var tesla = WeaponCatalog.Get(WeaponId.Tesla);
		var frag = WeaponCatalog.Get(WeaponId.Frag);
		var awm = WeaponCatalog.Get(WeaponId.Awm);

		H.Eq("the Tesla holds three charges", tesla.Magazine, 3);
		H.Eq("and chains through four bodies", tesla.ArcTargets, 4);
		H.Check("its recharge outlasts the Arc Lance's",
			tesla.ReloadTicks > WeaponCatalog.Get(WeaponId.ArcLance).ReloadTicks,
			$"{tesla.ReloadTicks} ticks");
		H.Check("a bolt kills anything a guard can wear",
			tesla.Damage * (long)tesla.ArmourPierce / Fx.One >= Tune.GuardHealth
				&& tesla.ArmourPierce == Fx.One);
		H.Check("the arc comes back from closer than it jumps",
			Tune.ArcPlayerReach < Tune.ArcReach);

		H.Check("the AWM goes through walls", awm.WallPierce > 0);
		H.Check("and nothing else does", Only(s => s.WallPierce > 0, WeaponId.Awm));
		bool slowest = true;
		for (int i = 0; i < WeaponCatalog.Count; i++)
			if ((WeaponId)i != WeaponId.Awm
				&& WeaponCatalog.Get((WeaponId)i).FireCooldownTicks >= awm.FireCooldownTicks)
				slowest = false;
		H.Check("its bolt is the longest cycle of any weapon", slowest,
			$"{awm.FireCooldownTicks} ticks");
		H.Check("it is useless from the hip and a needle aimed",
			awm.SpreadBase > WeaponCatalog.Get(WeaponId.Ak47).SpreadBase
			&& awm.SpreadBase * (long)awm.AimSpreadQ8 / Fx.One
				< WeaponCatalog.Get(WeaponId.Vss).SpreadBase);
		H.Check("each wall costs the round something",
			PastWalls(awm.Damage, 1) < awm.Damage && PastWalls(awm.Damage, 3) > 0,
			$"{awm.Damage} -> {PastWalls(awm.Damage, 1)} -> {PastWalls(awm.Damage, 3)}");

		H.Check("the Frag is thrown", frag.Grenade);
		H.Check("and nothing else is", Only(s => s.Grenade, WeaponId.Frag));
		H.Check("only the Tesla arcs", Only(s => s.ArcTargets > 0, WeaponId.Tesla));
		H.Check("a throw is nearly silent; the blast is not",
			frag.GunshotRadius < Tune.BlastHeardRadius / 4);

		for (int i = 10; i < WeaponCatalog.Count; i++)
			H.Check($"{WeaponCatalog.NameOf((WeaponId)i)} says what it does",
				WeaponCatalog.TraitOf((WeaponId)i).Length > 0);
		H.Check("and an ordinary weapon has no trait line",
			WeaponCatalog.TraitOf(WeaponId.Ak47).Length == 0);

		// Attachments are folded in by WeaponSpec.With, which rebuilds the
		// whole struct. A trait it forgot to carry would vanish the moment
		// anything was fitted -- a scoped Tesla that no longer arcs.
		var scoped = new Loadout(WeaponId.Tesla, ArmourId.None, sight: 3, rail: 1).Spec;
		H.Eq("a fitted Tesla still arcs", scoped.ArcTargets, tesla.ArcTargets);
		var drum = new Loadout(WeaponId.Frag, ArmourId.None, mag: 2).Spec;
		H.Check("a bigger bandolier is still grenades", drum.Grenade && drum.Magazine > frag.Magazine,
			$"{drum.Magazine}");
		var loaded = new Loadout(WeaponId.Awm, ArmourId.None, 3, 3, 3, 3, 3, 2).Spec;
		H.Eq("a fully fitted AWM still pierces", loaded.WallPierce, awm.WallPierce);
	}

	private static int PastWalls(int damage, int walls)
	{
		for (int i = 0; i < walls; i++) damage -= (damage * Tune.WallPierceLossQ8) >> Fx.Shift;
		return damage;
	}

	private static bool Only(Func<WeaponSpec, bool> trait, WeaponId who)
	{
		for (int i = 0; i < WeaponCatalog.Count; i++)
			if (trait(WeaponCatalog.Get((WeaponId)i)) != ((WeaponId)i == who)) return false;
		return true;
	}

	// ------------------------------------------------------------------ Tesla

	private static void Tesla()
	{
		H.Group("specialists / tesla");

		// Five guards in a line, six cells apart: well inside a jump of each
		// other, and the first fifteen cells from the shooter.
		var g = Room();
		Put(g, 20, 14, 'a'); Put(g, 26, 14, 'b'); Put(g, 32, 14, 'c');
		Put(g, 38, 14, 'd'); Put(g, 44, 14, 'e');
		var w = World(g, WeaponId.Tesla);
		int jumps = FireOnce(w, 20, SimEventKind.ArcJump);
		H.Eq("one bolt kills four", Dead(w), 4);
		H.Check("and the fifth is spared: the charge is spent", w.Guards[4].Alive);
		H.Eq("four nodes: the strike and three jumps", jumps, 4);
		H.Check("the shooter, far back, is untouched",
			w.Over == null && w.Player.Health == Tune.BaseHealth);
		H.Eq("one charge spent", w.Player.Mag, 2);

		// Plate is irrelevant to a bolt.
		var gp = Room();
		Put(gp, 20, 14, 'a');
		var wp = World(gp, WeaponId.Tesla);
		wp.Guards[0].Armour = 150; wp.Guards[0].ArmourMax = 150;
		FireOnce(wp, 20);
		H.Check("a heavy plate does not stop it", !wp.Guards[0].Alive);

		// Guards out of jump range of each other: one dies, the other does not.
		var gf = Room();
		Put(gf, 20, 14, 'a'); Put(gf, 34, 14, 'b');   // 280 px apart
		var wf = World(gf, WeaponId.Tesla);
		FireOnce(wf, 20);
		H.Check("a guard beyond the jump is not reached",
			!wf.Guards[0].Alive && wf.Guards[1].Alive);

		// A shut door breaks the chain: arcs need a clear line like an eye.
		var gw = Room();
		Put(gw, 20, 14, 'a'); Put(gw, 20, 18, 'b');
		for (int c = 17; c <= 23; c++) Put(gw, c, 16, '#');
		var ww = World(gw, WeaponId.Tesla);
		FireOnce(ww, 20);
		H.Check("a wall between two guards stops the jump",
			!ww.Guards[0].Alive && ww.Guards[1].Alive);

		// TOO CLOSE: the guard is four cells off. The bolt kills him and the
		// shooter is the next conductor.
		var gc = Room();
		Put(gc, 9, 14, 'a');
		var wc = World(gc, WeaponId.Tesla);
		FireOnce(wc, 20);
		H.Check("point blank, the guard dies", !wc.Guards[0].Alive);
		H.Check("and so do you", wc.Over == "dead" && !wc.Player.Alive,
			$"over={wc.Over} health={wc.Player.Health}");

		// And armour does not save you from your own bolt either.
		var ga = Room();
		Put(ga, 9, 14, 'a');
		var wa = new SimWorld(Level.FromText(Text(Stamp(ga))), 23,
			new Loadout(WeaponId.Tesla, ArmourId.HeavyPlate));
		Place(wa, 5, 14);
		FireOnce(wa, 20);
		H.Check("heavy plate does not save the shooter", wa.Over == "dead");

		// A wall strike beside you comes straight back.
		var gb = Room();
		WallAt(gb, 8);
		var wb = World(gb, WeaponId.Tesla);
		FireOnce(wb, 20);
		H.Check("a bolt into the wall at your elbow kills you", wb.Over == "dead");

		// A far wall strike GROUNDS: it does not go looking for guards.
		var gg = Room();
		WallAt(gg, 20);
		Put(gg, 18, 11, 'a');   // near the strike point, off the line of fire
		var wg = World(gg, WeaponId.Tesla);
		FireOnce(wg, 20);
		H.Check("a miss into a far wall kills nobody",
			wg.Guards[0].Alive && wg.Over == null);

		// Nodes are logged in chain order, so game/ can draw the chain.
		var gl = Room();
		Put(gl, 20, 14, 'a'); Put(gl, 26, 14, 'b');
		var wl = World(gl, WeaponId.Tesla);
		wl.Step(in Shoot);
		int last = -1;
		bool ordered = true;
		int nodes = 0;
		for (int i = 0; i < 20; i++)
		{
			wl.Step(in Idle);
			for (int e = 0; e < wl.Log.Events.Count; e++)
			{
				var ev = wl.Log.Events[e];
				if (ev.Kind != SimEventKind.ArcJump) continue;
				if (ev.Value != last + 1) ordered = false;
				last = ev.Value;
				nodes++;
			}
		}
		H.Check("the chain is logged node by node, in order", ordered && nodes == 2,
			$"{nodes} nodes");

		// Three charges, then a click, then a long wait.
		var gm = Room();
		var wm = World(gm, WeaponId.Tesla, 10);   // the far wall grounds every bolt
		for (int i = 0; i < 3 * 46 + 5 && wm.Over == null; i++) wm.Step(in Shoot);
		H.Eq("three charges and it is empty", wm.Player.Mag, 0);
	}

	private static char[] Stamp(char[] g)
	{
		Put(g, 1, 1, '@');
		Put(g, GW - 2, 1, 'X');
		return g;
	}

	private static void Place(SimWorld w, int cx, int cy)
	{
		w.Player.X = Centre(cx);
		w.Player.Y = Centre(cy);
		w.Player.Facing = 0;
	}

	// -------------------------------------------------------------------- AWM

	private static void Awm()
	{
		H.Group("specialists / awm");

		// One wall between the shooter and a bare guard.
		var g1 = Room();
		WallAt(g1, 15);
		Put(g1, 25, 14, 'a');
		var w1 = World(g1, WeaponId.Awm);
		int through = FireOnce(w1, 40, SimEventKind.WallPierced, aim: true);
		H.Check("the round goes through a wall and kills", !w1.Guards[0].Alive);
		H.Check("and says it went through", through >= 1, $"{through}");

		var c1 = World(Clone(g1), WeaponId.Ak47);
		FireOnce(c1, 40, aim: true);
		H.Check("the same wall stops an AK round",
			c1.Guards[0].Alive && c1.Guards[0].Health == Tune.GuardHealth);

		// Heavy plate behind one wall: the round arrives weakened, and fails.
		var gp = Room();
		WallAt(gp, 15);
		Put(gp, 25, 14, 'a');
		var wp = World(gp, WeaponId.Awm);
		wp.Guards[0].Armour = 150; wp.Guards[0].ArmourMax = 150;
		FireOnce(wp, 40, aim: true);
		H.Check("heavy plate survives a round that has been through a wall",
			wp.Guards[0].Alive && wp.Guards[0].Health < Tune.GuardHealth,
			$"health {wp.Guards[0].Health}");

		// But not one that has not.
		var gn = Room();
		Put(gn, 25, 14, 'a');
		var wn = World(gn, WeaponId.Awm);
		wn.Guards[0].Armour = 150; wn.Guards[0].ArmourMax = 150;
		FireOnce(wn, 40, aim: true);
		H.Check("and dies to a clean one", !wn.Guards[0].Alive);

		// The budget: three walls go through, the fourth stops it.
		var g4 = Room();
		WallAt(g4, 10); WallAt(g4, 14); WallAt(g4, 18); WallAt(g4, 22);
		Put(g4, 30, 14, 'a');
		var w4 = World(g4, WeaponId.Awm);
		FireOnce(w4, 40, aim: true);
		H.Check("a fourth wall stops it cold",
			w4.Guards[0].Health == Tune.GuardHealth, $"health {w4.Guards[0].Health}");

		var g3 = Room();
		WallAt(g3, 10); WallAt(g3, 14); WallAt(g3, 18);
		Put(g3, 30, 14, 'a');
		var w3 = World(g3, WeaponId.Awm);
		FireOnce(w3, 40, aim: true);
		H.Check("through three it still arrives, weakened",
			w3.Guards[0].Health < Tune.GuardHealth, $"health {w3.Guards[0].Health}");
		H.Check("but it no longer kills", w3.Guards[0].Alive);

		// The bolt: a second trigger pull inside the cycle does nothing.
		var gb = Room();
		var wb = World(gb, WeaponId.Awm);
		for (int i = 0; i < 60; i++) wb.Step(in Shoot);
		H.Eq("held for a second, it fires once", wb.Shots, 1);

		// From the hip, at twenty cells, it is a coin you cannot afford to toss.
		int hipHits = 0, aimHits = 0;
		for (int seed = 0; seed < 12; seed++)
		{
			var gh = Room();
			Put(gh, 25, 14, 'a');
			Put(gh, 1, 1, '@'); Put(gh, GW - 2, 1, 'X');
			var hip = new SimWorld(Level.FromText(Text(gh)), (ulong)(100 + seed), new Loadout(WeaponId.Awm));
			Place(hip, 5, 14);
			Bare(hip);
			FireOnce(hip, 40);
			if (!hip.Guards[0].Alive) hipHits++;
			var aimed = new SimWorld(Level.FromText(Text(gh)), (ulong)(100 + seed), new Loadout(WeaponId.Awm));
			Place(aimed, 5, 14);
			Bare(aimed);
			FireOnce(aimed, 40, aim: true);
			if (!aimed.Guards[0].Alive) aimHits++;
		}
		H.Eq("aimed, it does not miss at 400 px", aimHits, 12);
		H.Check("from the hip, it often does", hipHits < 12, $"{hipHits}/12 from the hip");
	}

	private static char[] Clone(char[] g)
	{
		var c = new char[g.Length];
		Array.Copy(g, c, g.Length);
		return c;
	}

	// ------------------------------------------------------------------- Frag

	private static Bullet? Grenade(SimWorld w)
	{
		for (int i = 0; i < w.Bullets.Live.Count; i++)
			if (w.Bullets.Live[i].Kind == BulletKind.Grenade) return w.Bullets.Live[i];
		return null;
	}

	private static void Frag()
	{
		H.Group("specialists / frag");
		var spec = WeaponCatalog.Get(WeaponId.Frag);

		// Thrown into an open floor: where does it come to rest?
		var ga = Room();
		var wa = World(ga, WeaponId.Frag);
		wa.Step(in Shoot);
		var nade = Grenade(wa);
		H.Check("a trigger pull throws a grenade", nade != null);
		H.Eq("and spends one", wa.Player.Mag, spec.Magazine - 1);
		int restX = 0, restY = 0;
		int blasts = 0, frags = 0;
		for (int i = 0; i < spec.BulletTicks + 4; i++)
		{
			var gn = Grenade(wa);
			if (gn != null) { restX = gn.X; restY = gn.Y; }
			wa.Step(in Idle);
			for (int e = 0; e < wa.Log.Events.Count; e++)
				if (wa.Log.Events[e].Kind == SimEventKind.Blast) blasts++;
			for (int b = 0; b < wa.Bullets.Live.Count; b++)
				if (wa.Bullets.Live[b].Kind == BulletKind.Frag) frags = Math.Max(frags, 1);
		}
		int thrown = (restX - wa.Player.X) / Fx.One;
		H.Check("it lands a room away", thrown > 180 && thrown < 360, $"{thrown} px");
		H.Check("and comes to rest", Math.Abs(restY - wa.Player.Y) < 20 * Fx.One);
		H.Eq("the fuse goes once", blasts, 1);
		H.Check("and throws shrapnel", frags > 0);
		H.Check("the thrower, a room away, walks off",
			wa.Over == null && wa.Player.Health == Tune.BaseHealth,
			$"health {wa.Player.Health}");

		// The same throw with a guard stood where it lands. Grenades do not
		// touch bodies, so his being there cannot change where it goes.
		var gk = Room();
		Put(gk, 40, 22, 'a');
		var wk = World(gk, WeaponId.Frag);
		AddGuardAt(wk, restX + 10 * Fx.One, restY);
		FireOnce(wk, spec.BulletTicks + 20);
		H.Check("a guard stood on it dies", wk.Guards.Count == 1 && !wk.Guards[0].Alive);

		// A lob with aim held lands short.
		var gl = Room();
		var wl = World(gl, WeaponId.Frag);
		var aimed = new InputFrame(0, 0, 0, (byte)(InputFrame.FFire | InputFrame.FAim));
		var hold = new InputFrame(0, 0, 0, InputFrame.FAim);
		wl.Step(in aimed);
		int lobX = 0;
		for (int i = 0; i < spec.BulletTicks - 2; i++)
		{
			wl.Step(in hold);
			var gn = Grenade(wl);
			if (gn != null) lobX = gn.X;
		}
		int lobbed = (lobX - wl.Player.X) / Fx.One;
		H.Check("aimed, it is an underhand lob", lobbed > 40 && lobbed < thrown * 2 / 3,
			$"{lobbed} px against {thrown}");

		// Shrapnel has no side: thrown in a closet, it finds the thrower.
		var gs = Room();
		for (int c = 3; c <= 7; c++) { Put(gs, c, 12, '#'); Put(gs, c, 16, '#'); }
		for (int r = 12; r <= 16; r++) { Put(gs, 3, r, '#'); Put(gs, 7, r, '#'); }
		var ws = World(gs, WeaponId.Frag);
		int bounces = FireOnce(ws, spec.BulletTicks + 20, SimEventKind.GrenadeBounce);
		H.Check("it bounces off the walls of a closet", bounces > 0, $"{bounces}");
		H.Check("and its shrapnel hits the one who threw it",
			ws.Player.Health < Tune.BaseHealth || ws.Over == "dead",
			$"health {ws.Player.Health}");

		// Heard at the blast, a long way off.
		var gh = Room();
		Put(gh, 40, 22, 'a');
		var wh = World(gh, WeaponId.Frag);
		AddGuardAt(wh, restX + 800 * Fx.One / 2, restY + 200 * Fx.One);
		FireOnce(wh, spec.BulletTicks + 4);
		H.Check("the blast is heard across the floor", wh.Guards[0].Awareness > 0,
			$"awareness {wh.Guards[0].Awareness}");

		// Determinism: the whole business of bouncing and bursting is integer.
		var d1 = World(Room(), WeaponId.Frag);
		var d2 = World(Room(), WeaponId.Frag);
		for (int i = 0; i < 200; i++)
		{
			var f = (i % 70) < 2 ? Shoot : Idle;
			d1.Step(in f); d2.Step(in f);
		}
		H.Check("two identical runs with grenades hash the same",
			d1.StateHash() == d2.StateHash());
	}

	/// <summary>
	/// A guard dropped at an exact point. Built as a second world from a grid
	/// with one guard glyph, then moved -- the only public way to have one.
	/// </summary>
	private static void AddGuardAt(SimWorld w, int x, int y)
	{
		if (w.Guards.Count == 0) throw new InvalidOperationException("fixture needs a guard glyph");
		var e = w.Guards[0];
		e.X = x; e.Y = y; e.HomeX = x; e.HomeY = y;
		e.Facing = 0;   // east, his back to the thrower: he hears it, never sees you
	}
}
