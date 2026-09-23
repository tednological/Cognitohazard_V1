using System;
using System.Collections.Generic;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// Glass ('=') and doors ('+'): the two kinds of cell that change during a run.
///
/// Every rule the feature makes is pinned here against a purpose-built room:
/// what each one blocks (walking, seeing, rounds), what breaks or moves it, who
/// hears it, and that a level WITHOUT either behaves exactly as before -- the
/// last is what keeps the golden hashes where they were.
/// </summary>
public static class Panels
{
	// ------------------------------------------------------------ fixtures

	private const int W = 40, HGT = 14;
	private const int Divider = 12;        // the column the partition stands in

	/// <summary>
	/// A room split by a wall at column 12, with a two-cell DOOR at rows 5-6 and
	/// a two-cell WINDOW at rows 9-10. Spawn on the left, exit far right.
	/// Guards: 'a' at (20,3) close enough to hear the window, 'b' at (36,3) out
	/// of earshot. Both sentries facing east -- away from the partition.
	/// </summary>
	private static char[] Room(bool withGuards = true)
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
		g[5 * W + 6] = '@';
		g[11 * W + 37] = 'X';
		if (withGuards)
		{
			g[3 * W + 20] = 'a';
			g[3 * W + 36] = 'b';
		}
		return g;
	}

	private static string Text(char[] g, params string[] routes)
	{
		var sb = new System.Text.StringBuilder("name: panels\n");
		sb.Append(Level.HeaderComment).Append('\n');
		sb.Append("grid:\n");
		for (int r = 0; r < HGT; r++)
		{
			for (int c = 0; c < W; c++) sb.Append(g[r * W + c]);
			sb.Append('\n');
		}
		foreach (var rt in routes) sb.Append(rt).Append('\n');
		return sb.ToString();
	}

	private static SimWorld World(char[] g, Loadout loadout = default, params string[] routes)
		=> new SimWorld(Level.FromText(Text(g, routes)), 1234UL, loadout);

	private const int GlassIx = 0;   // glass merges first
	private const int DoorIx = 1;

	private static int Cx(int c) => c * Level.CellFx + Level.CellFx / 2;

	private static void Place(Actor a, int x, int y) { a.X = x; a.Y = y; }

	private static InputFrame Idle(int doorPick = 0, int tier = InputFrame.TierWalk, int aim = 0,
		byte flags = 0, int mx = 0, int my = 0)
		=> new InputFrame(mx, my, aim, flags, 0, tier, 0, 0, 0, doorPick);

	private static bool Logged(SimWorld w, SimEventKind k)
	{
		foreach (var e in w.Log.Events) if (e.Kind == k) return true;
		return false;
	}

	// --------------------------------------------------------------- runner

	public static void Run()
	{
		Parsing();
		Blocking();
		Doors();
		Glass();
		Hands();
		Guards();
		Recording();
		NoPanelsNoChange();
	}

	private static void Parsing()
	{
		H.Group("panels / parsing");

		string text = Text(Room());
		var L = Level.FromText(text);
		H.Eq("a window and a door", L.Panels.Count, 2);
		H.Check("glass first, then doors",
			L.Panels[GlassIx].Kind == PanelKind.Glass && L.Panels[DoorIx].Kind == PanelKind.Door);
		H.Check("a two-cell vertical run is ONE panel, marked vertical",
			L.Panels[DoorIx].Rect.H == 2 * Level.CellFx && L.Panels[DoorIx].Rect.W == Level.CellFx
			&& L.Panels[DoorIx].Vertical);
		H.Check("round-trip is lossless", L.ToText() == text);

		// Panel cells are not walls: the wall merge must not cover them, or a
		// door would stay shut in the one array nothing ever rebuilds.
		bool clear = true;
		foreach (var wr in L.Walls)
			foreach (var pd in L.Panels)
				if (wr.X < pd.Rect.X1 && wr.X1 > pd.Rect.X && wr.Y < pd.Rect.Y1 && wr.Y1 > pd.Rect.Y)
					clear = false;
		H.Check("no wall rect covers a panel cell", clear);

		// Runs are capped, so a window wall breaks in sections and a long run of
		// door glyphs is several doors.
		var strip = Room(false);
		for (int c = 2; c < 12; c++) strip[2 * W + c] = '=';
		for (int c = 14; c < 19; c++) strip[2 * W + c] = '+';
		var S = Level.FromText(Text(strip));
		int panes = 0, doors = 0, widest = 0;
		foreach (var pd in S.Panels)
		{
			if (pd.Rect.Y != 2 * Level.CellFx) continue;
			if (pd.Kind == PanelKind.Glass) panes++; else doors++;
			int cells = pd.Rect.W / Level.CellFx;
			if (pd.Kind == PanelKind.Glass && cells > widest) widest = cells;
		}
		H.Eq("ten cells of glass are three panes", panes, 3);
		H.Eq("no pane is longer than the cap", widest, Level.GlassPaneCells);
		H.Eq("five cells of door are two doors", doors, 2);

		H.Check("a door does not seal a level", L.ExitReachable());
		var sealedByGlass = Room(false);
		sealedByGlass[5 * W + Divider] = '#';
		sealedByGlass[6 * W + Divider] = '#';
		var G = Level.FromText(Text(sealedByGlass));
		H.Check("glass does not seal a level either -- it can be shot out", G.ExitReachable());
		H.Check("but the strict question says the way is through glass",
			!G.ExitReachable(glassBlocks: true));
	}

	private static void Blocking()
	{
		H.Group("panels / what blocks what");

		var w = World(Room(false));
		int y = 9 * Level.CellFx + Level.CellFx;          // between the window's two cells
		int ax = Cx(6), bx = Cx(18);

		H.Check("a whole pane is seen through",
			Geometry.ClearLine(w.Opaque, ax, y, bx, y));
		H.Check("a whole pane cannot be walked through",
			!Geometry.ClearLine(w.Solid, ax, y, bx, y));

		int dy = 5 * Level.CellFx + Level.CellFx;          // through the door
		H.Check("a shut door cannot be seen through", !Geometry.ClearLine(w.Opaque, ax, dy, bx, dy));
		H.Check("a shut door cannot be walked through", !Geometry.ClearLine(w.Solid, ax, dy, bx, dy));

		// Walk east into the shut door for two seconds: the player must stop at it.
		var p = w.Player;
		Place(p, Cx(8), dy);
		for (int i = 0; i < 120; i++) w.Step(Idle(mx: 1));
		H.Check("the player stops at a shut door",
			p.X + p.Radius <= Divider * Level.CellFx, $"x {p.X / Fx.One}");

		Place(p, Cx(8), y);
		for (int i = 0; i < 120; i++) w.Step(Idle(mx: 1));
		H.Check("and at a whole pane", p.X + p.Radius <= Divider * Level.CellFx, $"x {p.X / Fx.One}");
	}

	private static void Doors()
	{
		H.Group("panels / doors");

		var w = World(Room(false));
		var p = w.Player;
		int dy = 5 * Level.CellFx + Level.CellFx;
		int door = DoorIx + 1;

		Place(p, Cx(Divider) - Level.CellFx - Fx.One * 2, dy);   // standing at the door
		H.Eq("the door in reach is the door", w.NearestDoor(), DoorIx);

		w.Step(Idle(door));
		H.Check("G opens it", w.Panels[DoorIx].Open);
		H.Check("and says so", Logged(w, SimEventKind.DoorOpened));
		H.Check("an open door is seen through",
			Geometry.ClearLine(w.Opaque, Cx(6), dy, Cx(18), dy));

		for (int i = 0; i < 90; i++) w.Step(Idle(mx: 1));
		H.Check("and walked through", p.X > (Divider + 1) * Level.CellFx, $"x {p.X / Fx.One}");

		// Standing IN the doorway, a close is refused out loud.
		Place(p, Cx(Divider), dy);
		w.Step(Idle(door));
		H.Check("a door will not shut on someone in it", w.Panels[DoorIx].Open);
		H.Check("and the refusal is reported", Logged(w, SimEventKind.DoorBlocked));

		Place(p, Cx(Divider) + Level.CellFx + Fx.One * 2, dy);
		w.Step(Idle(door));
		H.Check("from either side, G shuts it again", !w.Panels[DoorIx].Open);
		H.Check("and says so", Logged(w, SimEventKind.DoorClosed));

		// Out of reach, the pick is refused: the sim does not take the game's
		// word for which door is near.
		Place(p, Cx(3), Cx(3));
		w.Step(Idle(door));
		H.Check("a door across the room does not move", !w.Panels[DoorIx].Open);
		H.Eq("and none is offered", w.NearestDoor(), -1);

		// A pick naming glass, or nothing, is ignored rather than trusted.
		Place(p, Cx(Divider) - Level.CellFx - Fx.One * 2, 9 * Level.CellFx + Level.CellFx);
		w.Step(Idle(GlassIx + 1));
		H.Check("G does not open a window", !w.Panels[GlassIx].Open);
		H.NoThrow("a pick past the end is ignored", () => w.Step(Idle(999)));

		// Rounds stop at a shut door.
		var s = World(Room(false));
		Place(s.Player, Cx(8), dy);
		s.Player.Facing = 0;
		bool doorStops = false;
		for (int i = 0; i < 40; i++)
		{
			s.Step(Idle(aim: 0, flags: InputFrame.FFire));
			foreach (var e in s.Log.Events)
				if (e.Kind == SimEventKind.WallHit && Math.Abs(e.X - Divider * Level.CellFx) < 4 * Fx.One)
					doorStops = true;
		}
		H.Check("a round stops at a shut door", doorStops && !s.Panels[DoorIx].Open);
	}

	private static void Glass()
	{
		H.Group("panels / glass");

		// A Welrod, whose 130 px report reaches nobody: whatever the guards hear
		// here, they hear from the GLASS.
		var w = World(Room(), new Loadout(WeaponId.Welrod));
		var p = w.Player;
		int y = 9 * Level.CellFx + Level.CellFx;
		Place(p, Cx(6), y);
		p.Facing = 0;

		var near = w.Guards[0];
		var far = w.Guards[1];
		int before = near.Awareness;

		bool broke = false, flew = false;
		for (int i = 0; i < 60 && !flew; i++)
		{
			w.Step(Idle(aim: 0, flags: InputFrame.FFire));
			if (Logged(w, SimEventKind.GlassBroken)) broke = true;
			foreach (var e in w.Log.Events)
				if (e.Kind == SimEventKind.WallHit && e.X > (W - 3) * Level.CellFx) flew = true;
		}
		H.Check("a round shatters a pane", broke && w.Panels[GlassIx].Open);
		H.Check("and flies on through it to the far wall", flew);
		H.Check("a broken pane is open floor",
			Geometry.ClearLine(w.Solid, Cx(6), y, Cx(18), y));

		int gx = Divider * Level.CellFx + Level.CellFx / 2;
		// Curious (Guard_AI.md §4): Notice raised him to GlassAwareness, past
		// AwHunt, so he stares at the pane and then hurries over to look. A
		// breaking pane is a noise, not a gunshot: it never starts a fight.
		H.Check("a guard in earshot comes to look",
			near.Awareness > before && near.State == GuardState.Curious
			&& (near.Task == GuardTask.Look || near.Task == GuardTask.Investigate
				|| near.Task == GuardTask.LookAround),
			$"awareness {near.Awareness}, state {near.State}/{near.Task}");
		H.Check("and looks at the glass, not at the shooter",
			near.HasLkp && Fx.Dist(near.LkpX, near.LkpY, gx, y) < Level.CellFx * 2);
		// The floor alarm the near guard's callout raises still lifts him to
		// its awareness floor -- that is the alarm, not the glass.
		H.Check("a guard out of earshot hears nothing",
			far.State == GuardState.Relaxed && far.Awareness < Tune.AwCurious && !far.HasLkp,
			$"awareness {far.Awareness}, state {far.State}");

		// A pane only breaks once, however many rounds reach it.
		int shattered = 0;
		for (int i = 0; i < 60; i++)
		{
			w.Step(Idle(aim: 0, flags: InputFrame.FFire));
			foreach (var e in w.Log.Events) if (e.Kind == SimEventKind.GlassBroken) shattered++;
		}
		H.Eq("a broken pane does not break again", shattered, 0);

		// A shotgun blast breaks a pane ONCE, though several pellets reach it.
		var sg = World(Room(false), new Loadout(WeaponId.Remington));
		Place(sg.Player, Cx(9), y);
		sg.Player.Facing = 0;
		int blast = 0;
		for (int i = 0; i < 10; i++)
		{
			sg.Step(Idle(aim: 0, flags: InputFrame.FFire));
			foreach (var e in sg.Log.Events) if (e.Kind == SimEventKind.GlassBroken) blast++;
		}
		H.Eq("six pellets, one pane, one break", blast, 1);
	}

	private static void Hands()
	{
		H.Group("panels / hands do not reach through glass");

		// A guard pressed to the far side of a window, back turned, inside
		// subdue range: with the pane there he cannot be taken, and with floor
		// there he can.
		Actor Try(char cell)
		{
			var g = Room(false);
			g[9 * W + Divider] = cell;
			g[10 * W + Divider] = cell;
			g[9 * W + 14] = 'a';
			var w = World(g);
			int y = 9 * Level.CellFx + Level.CellFx / 2;
			var e = w.Guards[0];
			Place(e, Divider * Level.CellFx + Level.CellFx + e.Radius + Fx.One, y);
			e.Facing = 0;
			Place(w.Player, Divider * Level.CellFx - w.Player.Radius - Fx.One, y);
			w.Step(Idle(flags: InputFrame.FSubdue));
			return e;
		}
		H.Check("no subdue through a pane", Try('=').State != GuardState.Down);
		H.Check("the same reach with no pane subdues", Try('.').State == GuardState.Down);
	}

	private static void Guards()
	{
		H.Group("panels / guards and doors");

		// A patrol whose route runs through the shut door. He opens it himself,
		// and leaves it open.
		var g = Room(false);
		g[6 * W + 18] = 'c';
		var w = World(g, default, "> c 18,6 6,6");
		bool byGuard = false;
		for (int i = 0; i < 900; i++)
		{
			w.Step(Idle());
			foreach (var e in w.Log.Events)
				if (e.Kind == SimEventKind.DoorOpened && e.Heading == 1) byGuard = true;
		}
		H.Check("a patrolling guard opens a shut door on his route", byGuard);
		H.Check("and leaves it open behind him", w.Panels[DoorIx].Open);

		// Closing a door near a sentry is heard; at the stealth tier the same
		// door, heard from the same place, is quieter.
		int Heard(int tier)
		{
			var r = Room(false);
			r[4 * W + 14] = 'a';
			var dw = World(r);
			var guard = dw.Guards[0];
			guard.Facing = 0;
			Place(dw.Player, Cx(Divider) - Level.CellFx - Fx.One * 2, 5 * Level.CellFx + Level.CellFx);
			dw.Step(Idle(DoorIx + 1, tier));
			return guard.Awareness;
		}
		int loud = Heard(InputFrame.TierWalk), quiet = Heard(InputFrame.TierStealth);
		H.Check("a guard beside a door hears it swing", loud > 0, $"{loud}");
		H.Check("easing it at the stealth tier is quieter", quiet < loud, $"{quiet} vs {loud}");
		H.Check("and no door alone can push a guard past the noise cap", loud <= Tune.NoiseCap);
	}

	private static void Recording()
	{
		H.Group("panels / replays");

		var inputs = new List<InputFrame>();
		var dr = new DetRng(99);
		for (int i = 0; i < 600; i++)
		{
			int door = i % 50 == 25 ? DoorIx + 1 : 0;
			inputs.Add(new InputFrame(dr.NextRange(-1, 1), dr.NextRange(-1, 1), dr.NextBrad(),
				(byte)(dr.NextInt(5) == 0 ? InputFrame.FFire : 0), 0, InputFrame.TierWalk,
				0, 0, 0, door));
		}

		string level = Text(Room());
		var rec = new Replay { Seed = 7, LevelText = level };
		var a = new SimWorld(Level.FromText(level), 7);
		foreach (var f in inputs)
		{
			a.Step(f);
			if (a.Tick % Replay.HashEvery == 0) rec.AddHash((int)a.Tick, a.StateHash());
			rec.Inputs.Add(f);
		}

		var back = Replay.FromText(rec.ToText());
		bool same = back.Inputs.Count == inputs.Count;
		for (int i = 0; same && i < inputs.Count; i++)
			if (back.Inputs[i].DoorPick != inputs[i].DoorPick) same = false;
		H.Check("a door pick survives the text format", same);
		var v = back.Verify();
		H.Check("and the run it drove verifies", !v.Found && v.Compared > 0,
			v.Found ? $"diverged at {v.Tick}" : $"{v.Compared} compared");

		// The panel state IS the run: two worlds differing only in one door
		// must hash differently.
		var x = new SimWorld(Level.FromText(level), 7);
		var y = new SimWorld(Level.FromText(level), 7);
		Place(x.Player, Cx(Divider) - Level.CellFx - Fx.One * 2, 5 * Level.CellFx + Level.CellFx);
		Place(y.Player, x.Player.X, x.Player.Y);
		x.Step(Idle(DoorIx + 1));
		y.Step(Idle());
		H.Check("an open door and a shut one hash differently", x.StateHash() != y.StateHash());
	}

	private static void NoPanelsNoChange()
	{
		H.Group("panels / a level without them is untouched");

		var w = new SimWorld(Level.FromText(Program.ReadLevel("substation_4.txt")), 5UL);
		H.Eq("the reference level has none", w.Panels.Count, 0);
		H.Check("so what blocks movement IS the wall array", ReferenceEquals(w.Solid, w.Level.Walls));
		H.Check("and so does what blocks sight", ReferenceEquals(w.Opaque, w.Level.Walls));
	}
}
