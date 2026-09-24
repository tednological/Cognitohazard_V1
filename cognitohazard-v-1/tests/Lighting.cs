using System;
using System.Collections.Generic;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// Light as a stealth axis (cognitohazard_lighting_plan.md, phases L0-L6).
///
/// Every rule the feature makes is pinned against a purpose-built room: what a
/// lamp lights and what stops it, that the incremental map always equals a
/// fresh one, that darkness slows a guard and shortens his reach but never
/// blinds him up close, that a flash gives the shooter away, that lamps shatter
/// and switches work (and guards put them back), and that torches light what
/// they point at. And, first, that a level WITHOUT lighting runs exactly the
/// code it ran before -- which is what keeps the golden hashes where they were.
/// </summary>
public static class Lighting
{
	// ------------------------------------------------------------ fixtures

	private const int W = 40, HGT = 14;
	private const int Divider = 12;

	/// <summary>
	/// A room split by a wall at column 12, a two-cell DOOR at rows 5-6 and a
	/// two-cell WINDOW at rows 9-10, as in Panels. Lamps and extra glyphs are
	/// laid on by the caller.
	/// </summary>
	private static char[] Room()
	{
		var g = new char[W * HGT];
		for (int r = 0; r < HGT; r++)
			for (int c = 0; c < W; c++)
			{
				bool edge = r == 0 || r == HGT - 1 || c == 0 || c == W - 1;
				char ch = edge ? '#' : '.';
				if (c == Divider && !edge)
					ch = (r == 5 || r == 6) ? '+' : (r == 9 || r == 10) ? '=' : '#';
				g[r * W + c] = ch;
			}
		g[11 * W + 3] = '@';
		g[12 * W + 37] = 'X';
		return g;
	}

	private static string Text(char[] g, int ambient)
	{
		var sb = new System.Text.StringBuilder("name: lighting\n");
		if (ambient >= 0) sb.Append("ambient: ").Append(ambient).Append('\n');
		sb.Append(Level.HeaderComment).Append('\n');
		sb.Append("grid:\n");
		for (int r = 0; r < HGT; r++)
		{
			for (int c = 0; c < W; c++) sb.Append(g[r * W + c]);
			sb.Append('\n');
		}
		return sb.ToString();
	}

	private static SimWorld World(char[] g, int ambient, Loadout loadout = default)
		=> new SimWorld(Level.FromText(Text(g, ambient)), 99UL, loadout);

	private static int Cx(int c) => c * Level.CellFx + Level.CellFx / 2;

	private static InputFrame Idle(int doorPick = 0, byte flags = 0, int aim = 0,
		int mx = 0, int my = 0)
		=> new InputFrame(mx, my, aim, flags, 0, InputFrame.TierWalk, 0, 0, 0, doorPick);

	private static bool Logged(SimWorld w, SimEventKind k, out SimEvent ev)
	{
		foreach (var e in w.Log.Events) if (e.Kind == k) { ev = e; return true; }
		ev = default;
		return false;
	}

	private static bool Fresh(SimWorld w)
	{
		var f = w.FreshLight();
		return f != null && w.Light != null && w.Light.SameAs(f);
	}

	// --------------------------------------------------------------- runner

	public static void Run()
	{
		Identity();
		Parsing();
		TheMap();
		Doors();
		Perception();
		Bodies();
		Flash();
		Lamps();
		Switches();
		Torches();
		Seeing();
		Recording();
	}

	// ------------------------------------------------------------- identity

	private static void Identity()
	{
		H.Group("lighting / a lit level is unchanged");

		bool allNull = true, allFull = true;
		foreach (var text in Fuzz.LevelTexts())
		{
			var L = Level.FromText(text);
			if (L.Ambient >= 0) continue;          // a shipped DARK level is not this test
			var w = new SimWorld(L, 1UL);
			if (w.Light != null) allNull = false;
			for (int y = Level.CellFx / 2; y < L.HeightFx; y += Level.CellFx * 3)
				for (int x = Level.CellFx / 2; x < L.WidthFx; x += Level.CellFx * 3)
					if (w.LightAt(x, y) != Fx.One) allFull = false;
			if (w.PlayerLightQ8 != Fx.One) allFull = false;
		}
		H.Check("a level with no ambient line builds no light map", allNull);
		H.Check("and is full light everywhere", allFull);

		var lit = World(Room(), 100);
		H.Check("ambient: 100 is also a lit level", lit.Light == null);

		// Perception reduces to the identity at full light, exactly.
		bool same = true;
		for (int q = 0; q <= 256; q += 7)
			for (int d = 0; d < 600; d += 37)
				if (Sim.Perception.InLight(q, d * Fx.One, 400 * Fx.One, Fx.One) != q) same = false;
		H.Check("InLight at full light is the identity", same);
		H.Eq("VisQ8 at full light is 256", Sim.Perception.VisQ8(Fx.One), Fx.One);
		H.Eq("DarkReach at full light is the range", Sim.Perception.DarkReach(400, Fx.One, 180), 400);
		H.Eq("pitch dark leaves VisFloor", Sim.Perception.VisQ8(0), Tune.VisFloor);
	}

	private static void Parsing()
	{
		H.Group("lighting / parsing");

		var g = Room();
		g[3 * W + 5] = 'L';
		g[4 * W + 30] = 'L';
		g[7 * W + 1] = 'S';
		string text = Text(g, 25);
		var L = Level.FromText(text);
		H.Eq("ambient parses", L.Ambient, 25);
		H.Eq("as Q8", L.AmbientQ8, 25 * 256 / 100);
		H.Eq("two lamps, row-major", L.Lamps.Count, 2);
		H.Check("in row-major order", L.Lamps[0] == (5, 3) && L.Lamps[1] == (30, 4));
		H.Eq("one switch", L.Switches.Count, 1);
		H.Check("round-trip is lossless", L.ToText() == text);
		H.Check("a lamp is floor to the wall merge",
			!Geometry.HitsWall(L.Walls, Cx(5), Level.CellFx * 3 + Level.CellFx / 2, Fx.One));
		H.Check("and to the nav grid", !NavGrid.BlocksNav('L') && !NavGrid.BlocksNav('S'));

		H.Eq("ambient clamps high", Level.FromText("ambient: 99999999999\n").Ambient, 100);
		H.Eq("and low", Level.FromText("ambient: -5\n").Ambient, 0);
		H.Eq("garbage is not authored", Level.FromText("ambient: dusk\n").Ambient, -1);
		H.Check("not authored writes nothing", !Level.FromText("name: x\n").ToText().Contains("ambient"));
	}

	// -------------------------------------------------------------- the map

	private static void TheMap()
	{
		H.Group("lighting / the light map (L0)");

		var g = Room();
		g[9 * W + 8] = 'L';                    // four cells west of the window, row 9
		var w = World(g, 0);
		var m = w.Light!;
		H.Check("a dark level builds a map", w.Light != null);
		H.Eq("the lamp's own cell is full", m.CellQ8(8, 9), Tune.LampIntensity);
		int d3 = m.CellQ8(5, 9);
		H.Eq("three cells off it falls off linearly",
			d3, (int)((long)Tune.LampIntensity * (Tune.LampRadius - 60 * Fx.One) / Tune.LampRadius));
		H.Check("and keeps falling", m.CellQ8(4, 9) < d3 && m.CellQ8(3, 9) < m.CellQ8(4, 9));
		H.Eq("nothing past its radius", m.CellQ8(1, 9), 0);
		H.Check("light passes the pane", m.CellQ8(14, 9) > 0, $"{m.CellQ8(14, 9)}");
		H.Eq("and stops at the wall", m.CellQ8(14, 7), 0);
		H.Eq("a wall cell itself carries nothing", m.CellQ8(Divider, 7), 0);

		// Interpolation is smooth: a pixel of movement never jumps the value.
		int worst = 0, prev = w.LightAt(Cx(2), Cx(9));
		for (int x = Cx(2) + Fx.One; x <= Cx(11); x += Fx.One)
		{
			int v = w.LightAt(x, Cx(9));
			worst = Math.Max(worst, Math.Abs(v - prev));
			prev = v;
		}
		H.Check("one pixel of movement moves the light a little", worst <= 14, $"worst step {worst}");

		var twin = World(g, 0).Light!;
		H.Check("the map is deterministic", twin.SameAs(m));
		H.Check("and equals a fresh build", Fresh(w));

		var dim = World(g, 30);
		H.Eq("ambient lights what no lamp reaches", dim.Light!.CellQ8(30, 3), 30 * 256 / 100);
		var tex = m.Texture2x();
		H.Eq("the texture is two texels a cell", tex.Length, W * 2 * HGT * 2);
		// The partition's west face (lit side) is lit, its east face is not.
		int tw = W * 2;
		H.Check("a wall's lit face is lit", tex[(8 * 2) * tw + Divider * 2] > 0);
		H.Eq("and its far face is dark", tex[(8 * 2) * tw + Divider * 2 + 1], 0);
	}

	private static void Doors()
	{
		H.Group("lighting / doors");

		var g = Room();
		g[5 * W + 8] = 'L';                    // west of the door, its row
		var w = World(g, 0);
		var p = w.Player;
		H.Eq("a shut door keeps the light in", w.Light!.CellQ8(14, 5), 0);

		p.X = Cx(Divider) - Level.CellFx - Fx.One * 2; p.Y = 6 * Level.CellFx;
		w.Step(Idle(2));                       // panel 1 is the door (glass first)
		H.Check("the door opened", w.Panels[1].Open);
		H.Check("light spills through an open door", w.Light!.CellQ8(14, 5) > 0);
		H.Check("the incremental map equals a fresh one", Fresh(w));
		w.Step(Idle(2));
		H.Check("the door shut", !w.Panels[1].Open);
		H.Eq("and the light is kept in again", w.Light!.CellQ8(14, 5), 0);
		H.Check("still equal to a fresh one", Fresh(w));
	}

	// ----------------------------------------------------------- perception

	/// <summary>
	/// Ticks until a sentry facing the player reaches Curious, the player
	/// standing still at <paramref name="dist"/> px. The guard is re-aimed at
	/// the player each tick so only the light differs between two runs.
	/// </summary>
	private static int TicksToNotice(int ambient, int distPx, int limit = 900)
	{
		var g = Room();
		g[11 * W + 3] = '.';
		g[3 * W + 36] = 'a';
		var w = World(g, ambient);
		var e = w.Guards[0];
		w.Player.X = e.X - distPx * Fx.One;
		w.Player.Y = e.Y;
		for (int t = 0; t < limit; t++)
		{
			// Half a radian off: in his cone, OUT of his torch beam, so what
			// is measured is the light and not the torch.
			e.Facing = Brad.Atan2(w.Player.Y - e.Y, w.Player.X - e.X) + OffBeam & Brad.Mask;
			w.Step(Idle());
			if (e.Awareness >= Tune.AwCurious) return t;
		}
		return limit;
	}

	/// <summary>About 0.48 rad: inside a relaxed guard's 0.85 rad cone and
	/// outside his torch's 0.35 rad beam.</summary>
	private const int OffBeam = 5000;

	private static void Perception()
	{
		H.Group("lighting / perception (L1)");

		int lit160 = TicksToNotice(-1, 160), dark160 = TicksToNotice(0, 160);
		H.Check("in the dark it takes at least three times as long", dark160 >= 3 * lit160,
			$"lit {lit160} dark {dark160}");
		H.Check("but he does notice", dark160 < 900, $"{dark160}");

		int lit240 = TicksToNotice(-1, 240), dark240 = TicksToNotice(0, 240);
		H.Check("lit, he sees you across the room", lit240 < 900, $"{lit240}");
		H.Eq("in pitch dark beyond DarkSightRange he never does", dark240, 900);
		H.Check("dim light is between the two",
			TicksToNotice(50, 200) < 900 && TicksToNotice(50, 200) > TicksToNotice(-1, 200));

		int lit40 = TicksToNotice(-1, 40), dark40 = TicksToNotice(0, 40);
		H.Eq("in his face, the dark does not help", dark40, lit40);
	}

	private static void Bodies()
	{
		H.Group("lighting / bodies");

		foreach (var (ambient, expect) in new[] { (-1, true), (0, false) })
		{
			var g = Room();
			g[3 * W + 36] = 'a';
			g[3 * W + 28] = 'b';               // 160 px west of a
			var w = World(g, ambient);
			var a = w.Guards[0];
			var b = w.Guards[1];
			b.Alive = false; b.Health = 0; b.State = GuardState.Dead;
			bool found = false;
			for (int t = 0; t < 60 && !found; t++)
			{
				a.Facing = Brad.Atan2(b.Y - a.Y, b.X - a.X) + OffBeam & Brad.Mask;
				w.Step(Idle());
				found = b.Found;
			}
			H.Check(expect ? "a body in the light is found" : "the same body in the dark is not",
				found == expect);
		}
	}

	private static void Flash()
	{
		H.Group("lighting / the muzzle flash (L3)");

		foreach (int ambient in new[] { -1, 0 })
		{
			var g = Room();
			g[11 * W + 3] = '.';
			g[3 * W + 38] = 'a';
			var w = World(g, ambient);
			var e = w.Guards[0];
			var p = w.Player;
			// 420 px west, same room: out of the Glock's 400 px report, inside its flash.
			p.X = e.X - 420 * Fx.One; p.Y = e.Y;
			e.Facing = Brad.Atan2(p.Y - e.Y, p.X - e.X);
			for (int t = 0; t < 30; t++) w.Step(Idle(aim: 0));
			int before = e.Awareness;
			w.Step(Idle(flags: InputFrame.FFire, aim: 0));
			if (ambient < 0)
			{
				H.Check("lit: a shot out of earshot goes unnoticed", e.Awareness == before);
			}
			else
			{
				H.Check("dark: the flash is seen from out of earshot",
					e.Awareness >= Tune.FlashAwareness, $"{e.Awareness}");
				H.Check("and he knows where it was",
					e.HasLkp && Fx.Dist(e.LkpX, e.LkpY, p.X, p.Y) < 4 * Fx.One);
				H.Check("the shooter is lit by it", w.PlayerLightQ8 >= Tune.FlashLightQ8,
					$"{w.PlayerLightQ8}");
				for (int t = 0; t < Tune.FlashTicks + 2; t++) w.Step(Idle(aim: 0));
				H.Check("and it passes", w.PlayerLightQ8 < Tune.FlashLightQ8, $"{w.PlayerLightQ8}");
			}
		}

		// A suppressor hides the flash as it hides the bang.
		{
			var g = Room();
			g[11 * W + 3] = '.';
			g[3 * W + 38] = 'a';
			var w = World(g, 0, new Loadout(WeaponId.Welrod));
			var e = w.Guards[0];
			w.Player.X = e.X - 420 * Fx.One; w.Player.Y = e.Y;
			e.Facing = Brad.Atan2(0, -1);
			w.Step(Idle(flags: InputFrame.FFire));
			H.Check("a Welrod's flash is not seen at 420 px", e.Awareness < Tune.AwCurious,
				$"{e.Awareness}");
		}
	}

	// --------------------------------------------------------------- lamps

	private static void Lamps()
	{
		H.Group("lighting / shooting out a lamp");

		var g = Room();
		g[11 * W + 3] = '.';
		g[7 * W + 24] = 'L';
		g[3 * W + 30] = 'a';                   // within earshot of the lamp
		var w = World(g, 0);
		var p = w.Player;
		p.X = Cx(16); p.Y = Cx(7);
		int litBefore = w.Light!.CellQ8(24, 7);
		bool broke = false;
		SimEvent ev = default;
		for (int t = 0; t < 40 && !broke; t++)
		{
			w.Step(Idle(flags: t % 12 == 0 ? InputFrame.FFire : (byte)0, aim: 0));
			broke = Logged(w, SimEventKind.LampBroken, out ev);
		}
		H.Check("a round shatters the lamp", broke && w.Lamps[0].Broken);
		H.Eq("and names it", ev.Value, 0);
		H.Check("it was lit", litBefore == Tune.LampIntensity);
		H.Eq("and is dark now", w.Light!.CellQ8(24, 7), 0);
		H.Check("the map equals a fresh one", Fresh(w));
		H.Check("the guard heard it", w.Guards[0].Awareness >= Tune.LampAwareness
			|| w.Guards[0].State == GuardState.Combat);

		// A grenade rolls along the floor under a lamp without breaking it.
		var g2 = Room();
		g2[7 * W + 20] = 'L';
		var w2 = World(g2, 0, new Loadout(WeaponId.Frag));
		w2.Player.X = Cx(16); w2.Player.Y = Cx(7);
		// The blast's fragments DO fly at head height, so only the roll is pinned.
		bool blasted = false, intact = true;
		for (int t = 0; t < 400 && !blasted; t++)
		{
			w2.Step(Idle(flags: t == 0 ? InputFrame.FFire : (byte)0, aim: 0));
			blasted = Logged(w2, SimEventKind.Blast, out _);
			if (!blasted && w2.Lamps[0].Broken) intact = false;
		}
		H.Check("a rolling grenade passes under a lamp", blasted && intact);
	}

	// ------------------------------------------------------------ switches

	/// <summary>West room: a switch at (2,3), two lamps. East room, beyond the
	/// partition: one lamp, not on the circuit. A guard in the west room.</summary>
	private static SimWorld SwitchWorld(out int pick)
	{
		var g = Room();
		g[11 * W + 3] = '.';
		g[3 * W + 2] = 'S';
		g[3 * W + 6] = 'L';
		g[11 * W + 8] = 'L';
		g[7 * W + 30] = 'L';
		g[10 * W + 4] = 'a';
		var w = World(g, 0);
		pick = w.Panels.Count + 0 + 1;
		w.Player.X = Cx(3); w.Player.Y = Cx(3);
		return w;
	}

	private static void Switches()
	{
		H.Group("lighting / switches (L6)");

		var w = SwitchWorld(out int pick);
		H.Eq("the switch's circuit is its room's two lamps", w.Switches[0].Lamps.Length, 2);
		H.Eq("G names the switch", w.NearestUse(), w.Panels.Count);
		H.Check("the room starts lit", w.RoomLit(0));

		w.Step(Idle(pick));
		// Row-major: 0 is (6,3), 1 the east room's (30,7), 2 is (8,11).
		H.Check("G darkens the room", !w.RoomLit(0) && !w.Lamps[0].Lit && !w.Lamps[2].Lit);
		H.Check("but not the room next door", w.Lamps[1].Lit);
		H.Check("and says so", Logged(w, SimEventKind.LightsOff, out var ev) && ev.Heading == 0);
		H.Eq("the lamp's cell is dark", w.Light!.CellQ8(6, 3), 0);
		H.Check("the map equals a fresh one", Fresh(w));

		var e = w.Guards[0];
		H.Check("the guard in the room noticed", e.Awareness >= Tune.LightsAwareness,
			$"{e.Awareness}");
		H.Check("and comes to the switch",
			e.HasLkp && e.LkpX == w.Switches[0].X && e.LkpY == w.Switches[0].Y);

		// Step away -- through the wall, out of his sight -- and let him walk
		// over and put the lights back on.
		w.Player.X = Cx(34); w.Player.Y = Cx(11);
		bool restored = false;
		for (int t = 0; t < 900 && !restored; t++)
		{
			w.Step(Idle());
			restored = Logged(w, SimEventKind.LightsOn, out var on) && on.Heading == 1;
		}
		H.Check("he turns them back on", restored && w.RoomLit(0));
		H.Check("and the map follows", Fresh(w));

		// Out of reach, refused.
		var far = SwitchWorld(out int pick2);
		far.Player.X = Cx(9); far.Player.Y = Cx(3);
		far.Step(Idle(pick2));
		H.Check("a switch out of reach is refused", far.RoomLit(0));

		// Shot-out lamps stay dark, whoever throws the switch.
		var sw = SwitchWorld(out int pick3);
		sw.Lamps[0].Broken = true;
		sw.Light!.SetLamp(0, false);
		sw.Step(Idle(pick3));
		sw.Step(Idle(pick3));
		H.Check("a switch never relights a broken lamp", !sw.Lamps[0].Lit && sw.Lamps[1].Lit);
	}

	// ------------------------------------------------------------- torches

	private static void Torches()
	{
		H.Group("lighting / guard torches (L5)");

		var g = Room();
		g[11 * W + 3] = '.';
		g[7 * W + 30] = 'a';
		var w = World(g, 20);                  // at or below GuardTorchAmbient
		var e = w.Guards[0];
		var p = w.Player;
		H.Check("on a very dark level every guard carries a torch", w.TorchOn(e));

		p.X = e.X - 200 * Fx.One; p.Y = e.Y;
		e.Facing = Brad.Atan2(0, -1);
		w.Step(Idle());
		H.Check("standing in his beam, you are lit", w.PlayerLightQ8 >= Tune.TorchLightQ8,
			$"{w.PlayerLightQ8}");

		e.Facing = 0;                           // turned away
		e.State = GuardState.Relaxed;
		w.Step(Idle());
		H.Check("behind him, you are not", w.PlayerLightQ8 < Tune.TorchLightQ8,
			$"{w.PlayerLightQ8}");

		var dim = World(g, 60);
		H.Check("on a dim level a relaxed guard carries none", !dim.TorchOn(dim.Guards[0]));
		dim.Guards[0].State = GuardState.Hunting;
		H.Check("a hunting one does", dim.TorchOn(dim.Guards[0]));

		var lit = World(g, -1);
		lit.Guards[0].State = GuardState.Hunting;
		H.Check("and on a lit level nobody does", !lit.TorchOn(lit.Guards[0]));
	}

	/// <summary>The player sees a guard by the same rule (plan §3.3).</summary>
	private static void Seeing()
	{
		H.Group("lighting / what the player sees");

		var g = Room();
		g[11 * W + 3] = '.';
		g[7 * W + 38] = 'a';
		var w = World(g, 50);                  // dim, and no torches
		var e = w.Guards[0];
		var p = w.Player;
		int sight = 430 * Fx.One;
		p.X = e.X - 400 * Fx.One; p.Y = e.Y;
		H.Eq("a guard in the dim beyond reach is not seen", w.PlayerSeesQ8(e, sight), 0);
		p.X = e.X - 200 * Fx.One;
		int q = w.PlayerSeesQ8(e, sight);
		H.Check("closer, he is a shape", q > 0 && q < Fx.One, $"{q}");
		p.X = e.X - 40 * Fx.One;
		H.Eq("up close, plainly", w.PlayerSeesQ8(e, sight), Fx.One);
		e.State = GuardState.Combat;
		p.X = e.X - 400 * Fx.One;
		H.Eq("a torch gives him away from anywhere in sight", w.PlayerSeesQ8(e, sight), Fx.One);

		var lit = World(g, -1);
		lit.Player.X = lit.Guards[0].X - 400 * Fx.One; lit.Player.Y = lit.Guards[0].Y;
		H.Eq("on a lit level, as before", lit.PlayerSeesQ8(lit.Guards[0], sight), Fx.One);
	}

	// ------------------------------------------------------------ recording

	private static void Recording()
	{
		H.Group("lighting / replays");

		var w = SwitchWorld(out int pick);
		var level = w.Level;
		var inputs = new List<InputFrame>();
		for (int t = 0; t < 200; t++)
			inputs.Add(Idle(t == 10 || t == 120 ? pick : 0,
				flags: t % 30 == 5 ? InputFrame.FFire : (byte)0, aim: t * 300,
				mx: t >= 20 && t < 60 ? 1 : 0));

		var rec = new Replay { Seed = 99UL, LevelText = level.ToText() };
		foreach (var f in inputs) rec.Inputs.Add(f);
		var back = Replay.FromText(rec.ToText());

		var a = new SimWorld(Level.FromText(rec.LevelText), 99UL);
		var b = new SimWorld(Level.FromText(back.LevelText), back.Seed);
		a.Player.X = b.Player.X = Cx(3); a.Player.Y = b.Player.Y = Cx(3);
		bool same = true, switched = false;
		for (int t = 0; t < inputs.Count; t++)
		{
			a.Step(inputs[t]);
			b.Step(back.Inputs[t]);
			if (a.StateHash() != b.StateHash()) same = false;
			if (Logged(a, SimEventKind.LightsOff, out _)) switched = true;
		}
		H.Check("a switch pick rides the `u` token and replays", same && switched);
	}
}
