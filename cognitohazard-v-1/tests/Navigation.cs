using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// Guard AI v2, phase P0 (Guard_AI.md §8, §11): the nav grid, A*, path
/// following and the facing/travel split. Guards still run the spec §8 state
/// machine; what changed is how they get where it sends them.
/// </summary>
public static class Navigation
{
	private const int GW = Level.GW, GH = Level.GH;

	private static readonly string[] Shipped = { "substation_4.txt", "relay_nine.txt", "terminal_twelve.txt",
		"meridian_glasshouse.txt", "vault_row.txt", "vault_row_night.txt", "zz_black_site.txt" };

	private static char[] Room()
	{
		var g = new char[GW * GH];
		for (int r = 0; r < GH; r++)
			for (int c = 0; c < GW; c++)
				g[r * GW + c] = (r == 0 || r == GH - 1 || c == 0 || c == GW - 1) ? '#' : '.';
		return g;
	}

	private static void Put(char[] g, int c, int r, char ch) => g[r * GW + c] = ch;

	private static string Text(char[] g)
	{
		var sb = new System.Text.StringBuilder("name: fixture\ngrid:\n");
		for (int r = 0; r < GH; r++)
		{
			for (int c = 0; c < GW; c++) sb.Append(g[r * GW + c]);
			sb.Append('\n');
		}
		return sb.ToString();
	}

	private static int Centre(int cell) => cell * Level.CellFx + Level.CellFx / 2;

	private static InputFrame Idle => new InputFrame(0, 0, 0, 0);

	/// <summary>A room whose spawn is sealed in a 2x2 pocket in the top-left
	/// corner, so the guard under test can neither see nor hear the player.</summary>
	private static char[] SealedRoom()
	{
		var g = Room();
		Put(g, 3, 1, '#'); Put(g, 3, 2, '#'); Put(g, 3, 3, '#');
		Put(g, 1, 3, '#'); Put(g, 2, 3, '#');
		Put(g, 2, 2, '@');
		Put(g, GW - 2, GH - 2, 'X');
		return g;
	}

	public static void Run()
	{
		H.Group("navigation (Guard_AI P0)");
		MatchesPhysics();
		Corridors();
		Optimal();
		Smoothing();
		Detour();
		Routes();
		Backpedal();
		Budget();
		Degenerate();
	}

	// --------------------------------------------------------------- physics

	/// <summary>
	/// The nav grid's circle test IS Geometry.HitsWall, asked locally. If the
	/// two ever disagree, the nav grid can promise a route MoveSlide refuses.
	/// </summary>
	private static void MatchesPhysics()
	{
		string firstBad = "";
		int points = 0;
		foreach (string name in Shipped)
		{
			var level = Level.FromText(Program.ReadLevel(name));
			var nav = level.Nav;
			var rng = new DetRng(0xA11CE);
			// Everything the nav grid plans round, as rects: the walls plus any
			// glass, merged by the level's own merge.
			var blocking = new char[level.Grid.Length];
			for (int i = 0; i < blocking.Length; i++) blocking[i] = NavGrid.BlocksNav(level.Grid[i]) ? '#' : '.';
			var rects = Level.MergeWalls(blocking, level.W, level.H);
			int[] radii = { Tune.ActorRadius, nav.ClearR, 3 * Fx.One, 19 * Fx.One };

			for (int k = 0; k < 4000 && firstBad.Length == 0; k++)
			{
				int x = rng.NextRange(-2 * Fx.One, level.WidthFx + 2 * Fx.One);
				int y = rng.NextRange(-2 * Fx.One, level.HeightFx + 2 * Fx.One);
				// Every fourth point lands exactly a radius off a cell edge,
				// where the strict comparison decides.
				if ((k & 3) == 0) x = (x / Level.CellFx) * Level.CellFx - Tune.ActorRadius;
				int rad = radii[k % radii.Length];
				bool a = nav.CircleHits(x, y, rad);
				bool b = Geometry.HitsWall(rects, x, y, rad);
				points++;
				if (a != b) firstBad = $"{name} at {x},{y} r {rad}: nav {a}, physics {b}";
			}

			for (int i = 0; i < nav.Passable.Length && firstBad.Length == 0; i++)
				if (nav.Passable[i] && Geometry.HitsWall(rects, nav.NodeX[i], nav.NodeY[i], Tune.ActorRadius))
					firstBad = $"{name} node {i} is passable but a guard there overlaps a wall";
		}
		H.Check("the nav circle test agrees with HitsWall, and every node is standable",
			firstBad.Length == 0, firstBad.Length == 0 ? $"{points} points" : firstBad);

		// Symmetry: A can step to B iff B can step to A.
		string asym = "";
		foreach (string name in Shipped)
		{
			var nav = Level.FromText(Program.ReadLevel(name)).Nav;
			for (int i = 0; i < nav.Edges.Length && asym.Length == 0; i++)
				for (int d = 0; d < 8; d++)
					if ((nav.Edges[i] & (1 << d)) != 0 && (nav.Edges[nav.Step(i, d)] & (1 << ((d + 4) & 7))) == 0)
						asym = $"{name} cell {i} dir {d}";
		}
		H.Check("the nav graph is symmetric", asym.Length == 0, asym);
	}

	// ------------------------------------------------------------- corridors

	/// <summary>
	/// A guard is 22 px wide and a cell 20. A two-cell gap must be open (the
	/// node push exists for this: cell centres there are 10 px off a wall) and a
	/// one-cell gap must be shut, as it is to MoveSlide.
	/// </summary>
	private static void Corridors()
	{
		var two = Room();
		for (int r = 1; r < GH - 1; r++) Put(two, 24, r, '#');
		Put(two, 24, 13, '.'); Put(two, 24, 14, '.');
		var nav2 = Level.FromText(Text(two)).Nav;
		int west = 14 * GW + 10, east = 14 * GW + 38;
		H.Check("a two-cell doorway joins the rooms either side",
			nav2.Region[west] >= 0 && nav2.Region[west] == nav2.Region[east],
			$"regions {nav2.Region[west]} / {nav2.Region[east]}");
		H.Check("and both doorway cells are standable",
			nav2.Passable[13 * GW + 24] && nav2.Passable[14 * GW + 24]);

		var one = Room();
		for (int r = 1; r < GH - 1; r++) Put(one, 24, r, '#');
		Put(one, 24, 14, '.');
		var nav1 = Level.FromText(Text(one)).Nav;
		H.Check("a one-cell gap is too narrow for a guard",
			nav1.Region[west] != nav1.Region[east],
			$"regions {nav1.Region[west]} / {nav1.Region[east]}");

		// And the physics agrees: nowhere in that gap can a guard stand.
		bool fits = false;
		for (int y = 13 * Level.CellFx; y <= 15 * Level.CellFx; y += Fx.One)
			for (int x = 24 * Level.CellFx; x <= 25 * Level.CellFx; x += Fx.One)
				if (!Geometry.HitsWall(Level.FromText(Text(one)).Walls, x, y, Tune.ActorRadius)) fits = true;
		H.Check("MoveSlide agrees the one-cell gap is shut", !fits);
	}

	// ---------------------------------------------------------------- optimal

	/// <summary>Plain Dijkstra over the same edges, as the reference A* must match.</summary>
	private static int Reference(NavGrid nav, int s, int t)
	{
		int n = nav.W * nav.H;
		var dist = new int[n];
		for (int i = 0; i < n; i++) dist[i] = int.MaxValue;
		var pq = new PriorityQueue<int, int>();
		dist[s] = 0;
		pq.Enqueue(s, 0);
		while (pq.TryDequeue(out int cur, out int d))
		{
			if (d > dist[cur]) continue;
			if (cur == t) return d;
			for (int k = 0; k < 8; k++)
			{
				if ((nav.Edges[cur] & (1 << k)) == 0) continue;
				int nb = nav.Step(cur, k);
				int nd = d + NavGrid.CostOf(k);
				if (nd < dist[nb]) { dist[nb] = nd; pq.Enqueue(nb, nd); }
			}
		}
		return -1;
	}

	private static int RandomPassable(NavGrid nav, DetRng rng)
	{
		for (int k = 0; k < 100000; k++)
		{
			int i = rng.NextInt(nav.W * nav.H);
			if (nav.Passable[i]) return i;
		}
		return -1;
	}

	private static void Optimal()
	{
		string bad = "";
		int pairs = 0;
		string nondet = "";
		foreach (string name in Shipped)
		{
			var nav = Level.FromText(Program.ReadLevel(name)).Nav;
			var pf = new PathFinder(nav);
			var pf2 = new PathFinder(nav);
			var rng = new DetRng(0xB0B);
			var cells = new List<int>();
			var cells2 = new List<int>();
			for (int k = 0; k < 150 && bad.Length == 0; k++)
			{
				int s = RandomPassable(nav, rng), t = RandomPassable(nav, rng);
				int got = pf.Search(s, t, cells);
				int want = Reference(nav, s, t);
				pairs++;
				if (got != want) { bad = $"{name} {s}->{t}: A* {got}, Dijkstra {want}"; break; }
				if (got < 0) continue;

				// The path is real: starts and ends right, every step an edge,
				// and its steps add up to the cost reported.
				int sum = 0;
				bool steps = cells[0] == s && cells[^1] == t;
				for (int i = 1; i < cells.Count && steps; i++)
				{
					int dir = -1;
					for (int d = 0; d < 8; d++) if (nav.Step(cells[i - 1], d) == cells[i]) dir = d;
					if (dir < 0 || (nav.Edges[cells[i - 1]] & (1 << dir)) == 0) steps = false;
					else sum += NavGrid.CostOf(dir);
				}
				if (!steps || sum != got) { bad = $"{name} {s}->{t}: path is not a walk of cost {got}"; break; }

				// Determinism: the same question twice, and from a fresh finder,
				// gets the same cells, not merely the same cost.
				for (int pass = 0; pass < 2 && nondet.Length == 0; pass++)
				{
					int again = (pass == 0 ? pf2 : pf).Search(s, t, cells2);
					if (again != got || cells2.Count != cells.Count) { nondet = $"{name} {s}->{t}"; break; }
					for (int i = 0; i < cells.Count; i++)
						if (cells[i] != cells2[i]) { nondet = $"{name} {s}->{t} differs at step {i}"; break; }
				}
			}
		}
		H.Check("A* cost equals Dijkstra's, and every path is a real walk",
			bad.Length == 0, bad.Length == 0 ? $"{pairs} pairs" : bad);
		H.Check("A* breaks ties the same way every time", nondet.Length == 0, nondet);
	}

	// -------------------------------------------------------------- smoothing

	/// <summary>Every smoothed leg but the first (from wherever the guard
	/// actually stands) is a segment a guard can walk.</summary>
	private static void Smoothing()
	{
		string bad = "";
		int legs = 0, cellSteps = 0, waypoints = 0;
		foreach (string name in Shipped)
		{
			var nav = Level.FromText(Program.ReadLevel(name)).Nav;
			var pf = new PathFinder(nav);
			var rng = new DetRng(0x5100);
			var xs = new List<int>();
			var ys = new List<int>();
			var cells = new List<int>();
			for (int k = 0; k < 100 && bad.Length == 0; k++)
			{
				int s = RandomPassable(nav, rng), t = RandomPassable(nav, rng);
				if (!pf.Plan(nav.NodeX[s], nav.NodeY[s], nav.NodeX[t], nav.NodeY[t], false, xs, ys, out _)) continue;
				pf.Search(s, t, cells);
				cellSteps += cells.Count;
				waypoints += xs.Count;
				if (xs[^1] != nav.NodeX[t] || ys[^1] != nav.NodeY[t]) bad = $"{name}: the path does not end at the goal";
				for (int i = 1; i < xs.Count && bad.Length == 0; i++)
				{
					legs++;
					if (!nav.SegmentClear(xs[i - 1], ys[i - 1], xs[i], ys[i]))
						bad = $"{name} {s}->{t} leg {i} is not walkable";
				}
			}
		}
		H.Check("smoothed legs are walkable", bad.Length == 0, bad.Length == 0 ? $"{legs} legs" : bad);
		H.Check("smoothing removes most waypoints", waypoints * 3 < cellSteps,
			$"{waypoints} waypoints from {cellSteps} cells");
	}

	// ------------------------------------------------------------------ detour

	/// <summary>
	/// Spec §10.1's own failure: the LKP inside a U whose back faces the guard.
	/// Straight-at-the-target steering drives into the back of the U. The first
	/// two checks prove the fixture really demands a detour, so the third cannot
	/// pass by the guard simply walking in a straight line.
	/// </summary>
	private static void Detour()
	{
		var g = SealedRoom();
		for (int r = 8; r <= 20; r++) Put(g, 22, r, '#');
		for (int c = 12; c <= 22; c++) { Put(g, c, 8, '#'); Put(g, c, 20, '#'); }
		Put(g, 32, 14, 'a');

		var w = new SimWorld(Level.FromText(Text(g)), 99);
		var e = w.Guards[0];
		e.PathX = null; e.PathY = null;
		e.State = GuardState.Combat;
		e.Task = GuardTask.Converge;
		e.SetAwareness(Tune.AwEngage);
		int lx = Centre(17), ly = Centre(14);
		e.SetLkp(lx, ly);

		H.Check("the U blocks the straight line to the LKP",
			!Geometry.ClearLine(w.Level.Walls, e.X, e.Y, lx, ly));

		var xs = new List<int>();
		var ys = new List<int>();
		new PathFinder(w.Level.Nav).Plan(e.X, e.Y, lx, ly, true, xs, ys, out _);
		long len = 0;
		int px = e.X, py = e.Y;
		for (int i = 0; i < xs.Count; i++) { len += Fx.Dist(px, py, xs[i], ys[i]); px = xs[i]; py = ys[i]; }
		int straight = Fx.Dist(e.X, e.Y, lx, ly);
		H.Check("the planned route goes round the U", len * 2 > straight * 3L,
			$"route {len / Fx.One} px against {straight / Fx.One} px straight");

		int reachedAt = -1;
		for (int t = 0; t < 60 * 20; t++)
		{
			w.Step(Idle);
			if (e.Task == GuardTask.SearchLkp || Fx.Dist(e.X, e.Y, lx, ly) < Tune.LkpReach) { reachedAt = t; break; }
		}
		H.Check("a hunting guard reaches an LKP inside a U", reachedAt >= 0,
			reachedAt >= 0 ? $"{reachedAt / 60.0:F1} s" : $"stuck at {e.X / Fx.One},{e.Y / Fx.One}, {e.State}");
		H.Check("and gets there at about hunt speed, not by grinding",
			reachedAt >= 0 && reachedAt < (int)(len * Fx.TicksPerSecond / Tune.SpeedHunt) * 3 / 2 + 60,
			$"{reachedAt} ticks for {len / Fx.One} px");
	}

	// ------------------------------------------------------------------ routes

	/// <summary>
	/// Every leg of every authored patrol route, the closing leg included, and
	/// spawn to exit, is walkable on every shipped level. Then the behavioural
	/// half: a guard left to patrol completes a full lap.
	/// </summary>
	private static void Routes()
	{
		string bad = "";
		int legs = 0;
		foreach (string name in Shipped)
		{
			var level = Level.FromText(Program.ReadLevel(name));
			var nav = level.Nav;
			var pf = new PathFinder(nav);
			var cells = new List<int>();
			foreach (var gd in level.Guards)
			{
				if (gd.PathX == null) continue;
				int n = gd.PathX.Length;
				// The guard walks from his start cell to waypoint 1 first.
				int fromX = gd.X, fromY = gd.Y;
				for (int i = 1; i <= n && bad.Length == 0; i++)
				{
					int tx = gd.PathX[i % n], ty = gd.PathY![i % n];
					int a = nav.NearestPassable(fromX, fromY), b = nav.NearestPassable(tx, ty);
					legs++;
					if (a < 0 || b < 0 || pf.Search(a, b, cells) < 0)
						bad = $"{name} guard {gd.Id} leg to waypoint {i % n} ({tx / Level.CellFx},{ty / Level.CellFx})";
					fromX = tx; fromY = ty;
				}
			}
			int sp = nav.NearestPassable(level.SpawnX, level.SpawnY);
			int ex = nav.NearestPassable(level.Exit.X + level.Exit.W / 2, level.Exit.Y + level.Exit.H / 2);
			if (bad.Length == 0 && (sp < 0 || ex < 0 || pf.Search(sp, ex, cells) < 0))
				bad = $"{name}: spawn to exit";
		}
		H.Check("every patrol leg and every spawn-to-exit is pathable", bad.Length == 0,
			bad.Length == 0 ? $"{legs} legs" : bad);

		// Every body starts in a cell it can stand in. A start wedged between two
		// walls overlaps both, and MoveSlide then refuses every step in every
		// direction: Relay Nine's guard f stood frozen in a one-cell gap from the
		// port until this test found him. Against ONE wall is fine, since the
		// first step away clears it.
		string wedged = "";
		foreach (string name in Shipped)
		{
			var level = Level.FromText(Program.ReadLevel(name));
			var nav = level.Nav;
			int sc = nav.CellAt(level.SpawnX, level.SpawnY);
			if (sc < 0 || !nav.Passable[sc]) wedged = $"{name}: the player spawn";
			foreach (var gd in level.Guards)
			{
				int gc = nav.CellAt(gd.X, gd.Y);
				if (wedged.Length == 0 && (gc < 0 || !nav.Passable[gc]))
					wedged = $"{name}: guard {gd.Id} at {gd.X / Level.CellFx},{gd.Y / Level.CellFx}";
			}
		}
		H.Check("every guard and the spawn start where a body can stand", wedged.Length == 0, wedged);

		// Behaviour. The player is removed from play so nobody breaks off to
		// hunt; what is measured is only whether the route can be walked.
		string stuck = "";
		int laps = 0;
		foreach (string name in Shipped)
		{
			var w = new SimWorld(Level.FromText(Program.ReadLevel(name)), 7);
			w.Player.X = -100000 * Fx.One;
			w.Player.Y = -100000 * Fx.One;
			var advances = new int[w.Guards.Count];
			var lastWp = new int[w.Guards.Count];
			for (int i = 0; i < w.Guards.Count; i++) lastWp[i] = w.Guards[i].WaypointIndex;

			for (int t = 0; t < 60 * 120; t++)
			{
				w.Step(Idle);
				for (int i = 0; i < w.Guards.Count; i++)
				{
					if (w.Guards[i].WaypointIndex != lastWp[i]) { advances[i]++; lastWp[i] = w.Guards[i].WaypointIndex; }
				}
			}
			for (int i = 0; i < w.Guards.Count && stuck.Length == 0; i++)
			{
				var g = w.Guards[i];
				if (g.PathX == null) continue;
				// A one-point route is a walk to a post: the index never moves,
				// so what counts is having arrived.
				bool done = g.PathX.Length == 1
					? Fx.Dist(g.X, g.Y, g.PathX[0], g.PathY![0]) < Tune.WaypointReach
					: advances[i] >= g.PathX.Length;
				if (done) laps++;
				else stuck = $"{name} guard {g.Id}: {advances[i]} of {g.PathX.Length} waypoints in 120 s, " +
					$"at {g.X / Level.CellFx},{g.Y / Level.CellFx} heading for waypoint {g.WaypointIndex}, {g.State}";
			}
		}
		H.Check("every patrolling guard completes a lap of his route", stuck.Length == 0,
			stuck.Length == 0 ? $"{laps} guards" : stuck);
	}

	// --------------------------------------------------------------- backpedal

	/// <summary>
	/// MoveTo leaves facing alone, and walking away from where you look costs
	/// BackpedalQ8. The "one watches back" sweep (Guard_AI.md §6.2) is built on
	/// exactly this.
	/// </summary>
	private static void Backpedal()
	{
		const int ticks = 30;
		int per = Fx.PerTick(Tune.SpeedPatrol, Fx.ScaleDen);

		var g = SealedRoom();
		Put(g, 24, 14, 'a');
		var w = new SimWorld(Level.FromText(Text(g)), 3);
		var e = w.Guards[0];
		int x0 = e.X;

		e.Facing = 0;   // east
		for (int t = 0; t < ticks; t++) w.MoveTo(e, Centre(4), Centre(14), Tune.SpeedPatrol, Fx.ScaleDen);
		H.Eq("MoveTo does not turn the guard", e.Facing, 0);
		int back = x0 - e.X;
		H.Near("walking away from facing is at the backpedal rate",
			back, (double)ticks * ((per * Tune.BackpedalQ8) >> Fx.Shift), ticks);

		int x1 = e.X;
		e.Facing = Brad.Half;   // west, the way he is going
		for (int t = 0; t < ticks; t++) w.MoveTo(e, Centre(4), Centre(14), Tune.SpeedPatrol, Fx.ScaleDen);
		H.Near("and facing the way he goes is full speed", x1 - e.X, (double)ticks * per, ticks);
	}

	// ------------------------------------------------------------------ budget

	/// <summary>
	/// Relay Nine's 26 guards all hunting at once, to LKPs across the level:
	/// the per-tick A* budget holds, and the whole thing stays affordable.
	/// </summary>
	private static void Budget()
	{
		var w = new SimWorld(Level.FromText(Program.ReadLevel("relay_nine.txt")), 11);
		w.Player.X = -100000 * Fx.One;
		w.Player.Y = -100000 * Fx.One;
		var nav = w.Level.Nav;
		for (int i = 0; i < w.Guards.Count; i++)
		{
			var e = w.Guards[i];
			int far = nav.NearestPassable(w.Level.WidthFx - e.X, w.Level.HeightFx - e.Y);
			if (far < 0) continue;
			e.State = GuardState.Combat;
			e.Task = GuardTask.Converge;
			e.SetAwareness(Tune.AwEngage);
			e.SetLkp(nav.NodeX[far], nav.NodeY[far]);
		}

		int worst = 0, total = 0;
		var sw = Stopwatch.StartNew();
		const int ticks = 600;
		for (int t = 0; t < ticks; t++)
		{
			w.Step(Idle);
			if (w.NavSearchesLastTick > worst) worst = w.NavSearchesLastTick;
			total += w.NavSearchesLastTick;
		}
		sw.Stop();
		double msPerTick = sw.Elapsed.TotalMilliseconds / ticks;

		H.Check("no tick runs more A* searches than the budget", worst <= Tune.NavSearchesPerTick,
			$"worst {worst}, budget {Tune.NavSearchesPerTick}");
		H.Check("the searches actually happened", total >= w.Guards.Count / 2, $"{total} searches");
		// Generous on purpose: a timing bound that fails on a busy machine is
		// noise. Half a frame is the line; the measured figure is printed.
		H.Check("26 guards pathing cost well under a frame", msPerTick < 8.0,
			$"{msPerTick:F2} ms/tick over {ticks} ticks, {total} searches");
		Console.WriteLine($"  nav budget: relay_nine, 26 hunting guards - {msPerTick:F2} ms/tick, {total} A* searches");

		// And the compromised sweep on the biggest shipped floors: every guard
		// against every sweep node, every tick, is the new per-tick cost.
		foreach (string name in new[] { "relay_nine.txt", "terminal_twelve.txt", "zz_black_site.txt" })
		{
			var ws = new SimWorld(Level.FromText(Program.ReadLevel(name)), 13);
			ws.Player.X = -100000 * Fx.One;
			ws.Player.Y = -100000 * Fx.One;
			ws.Compromise(ws.Level.WidthFx / 2, ws.Level.HeightFx / 2);
			for (int t = 0; t < 60; t++) ws.Step(Idle);        // groups pick their first nodes
			var sw2 = Stopwatch.StartNew();
			for (int t = 0; t < ticks; t++) ws.Step(Idle);
			sw2.Stop();
			double ms = sw2.Elapsed.TotalMilliseconds / ticks;
			H.Check($"a compromised {name} sweeps well under a frame", ms < 8.0,
				$"{ms:F2} ms/tick, {ws.Sweep?.Count} nodes, {ws.Net.Groups.Count} groups");
			Console.WriteLine($"  sweep budget: {name}, {ws.Guards.Count} guards, {ws.Sweep?.Count} nodes, {ws.Net.Groups.Count} groups - {ms:F2} ms/tick");
		}
	}

	// -------------------------------------------------------------- degenerate

	private static void Degenerate()
	{
		H.NoThrow("a nav grid builds for an empty level text", () => { _ = Level.FromText("").Nav; });

		var walls = new System.Text.StringBuilder("grid:\n");
		for (int r = 0; r < 14; r++) walls.Append(new string('#', 14)).Append('\n');
		var solid = Level.FromText(walls.ToString());
		NavGrid? sn = null;
		H.NoThrow("a nav grid builds for a level of solid wall", () => sn = solid.Nav);
		if (sn != null)
		{
			H.Eq("which has no standable cell", sn.NearestPassable(solid.SpawnX, solid.SpawnY), -1);
			var xs = new List<int>();
			var ys = new List<int>();
			H.Check("and plans nothing, without throwing",
				!new PathFinder(sn).Plan(0, 0, solid.WidthFx, solid.HeightFx, false, xs, ys, out _) && xs.Count == 0);
		}

		var open = Level.FromText("").Nav;
		var pf = new PathFinder(open);
		var cells = new List<int>();
		H.Eq("an out-of-range start is refused", pf.Search(-1, 5, cells), -1);
		H.Eq("an out-of-range goal is refused", pf.Search(5, open.W * open.H, cells), -1);
		H.Check("off-grid points still find a nearest cell or none",
			open.NearestPassable(-50000 * Fx.One, 50000 * Fx.One) >= -1);

		var islands = Room();
		for (int r = 1; r < GH - 1; r++) Put(islands, 24, r, '#');
		var isl = Level.FromText(Text(islands)).Nav;
		var ipf = new PathFinder(isl);
		H.Eq("a goal in a sealed region is refused", ipf.Search(14 * GW + 10, 14 * GW + 38, cells), -1);
		H.Eq("and refused before flooding the level", ipf.Expanded, 0);

		H.NoThrow("a nav grid builds at the size limit", () =>
		{
			var big = new System.Text.StringBuilder("grid:\n");
			for (int r = 0; r < 42; r++) big.Append(new string('.', Level.MaxDim)).Append('\n');
			_ = Level.FromText(big.ToString()).Nav;
		});
	}
}
