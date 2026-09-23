using System;
using System.Collections.Generic;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// Guard AI v2, phases P1 and P2 (Guard_AI.md §4–§7, §11): postures and
/// tasks, snap sight, the radio, responders, the hold, and the compromised
/// level. Every fixture seals the player in a pocket unless the test is about
/// seeing him, so what is measured is the guards, not a firefight.
/// </summary>
public static class GuardAI
{
	// ------------------------------------------------------------ fixtures

	private sealed class Grid
	{
		public readonly int W, H;
		public readonly char[] G;
		public readonly List<string> Routes = new();

		public Grid(int w, int h)
		{
			W = w; H = h;
			G = new char[w * h];
			for (int r = 0; r < h; r++)
				for (int c = 0; c < w; c++)
					G[r * w + c] = (r == 0 || r == h - 1 || c == 0 || c == w - 1) ? '#' : '.';
			// The player lives in a sealed 2x2 pocket, top-left.
			Put(3, 1, '#'); Put(3, 2, '#'); Put(3, 3, '#'); Put(1, 3, '#'); Put(2, 3, '#');
			Put(2, 2, '@');
			Put(w - 2, h - 2, 'X');
		}

		public void Put(int c, int r, char ch) => G[r * W + c] = ch;

		public void WallCol(int c, int r0, int r1) { for (int r = r0; r <= r1; r++) Put(c, r, '#'); }

		public SimWorld World(ulong seed = 31)
		{
			var sb = new System.Text.StringBuilder("name: fixture\ngrid:\n");
			for (int r = 0; r < H; r++)
			{
				for (int c = 0; c < W; c++) sb.Append(G[r * W + c]);
				sb.Append('\n');
			}
			foreach (var rt in Routes) sb.Append(rt).Append('\n');
			var w = new SimWorld(Level.FromText(sb.ToString()), seed);
			// Guards spawn with rolled vests; nothing here is about armour.
			for (int i = 0; i < w.Guards.Count; i++) { w.Guards[i].Armour = 0; w.Guards[i].ArmourMax = 0; }
			return w;
		}
	}

	private static int C(int cell) => cell * Level.CellFx + Level.CellFx / 2;

	private static readonly InputFrame Idle = new InputFrame(0, 0, 0, 0);

	private static Actor ById(SimWorld w, char id)
	{
		for (int i = 0; i < w.Guards.Count; i++) if (w.Guards[i].Id == id) return w.Guards[i];
		throw new Exception("no guard " + id);
	}

	private static bool Logged(SimWorld w, SimEventKind k)
	{
		foreach (var e in w.Log.Events) if (e.Kind == k) return true;
		return false;
	}

	private static int CountTask(SimWorld w, GuardTask t)
	{
		int n = 0;
		for (int i = 0; i < w.Guards.Count; i++) if (!w.Guards[i].Prone && w.Guards[i].Task == t) n++;
		return n;
	}

	public static void Run()
	{
		Curious();
		CuriousPatroller();
		SnapSight();
		ShotAtSilently();
		AlliesAndRadio();
		RadioCut();
		ResponderCount();
		RespondersByPath();
		PatrollersFirst();
		HoldAndLose();
		RallyAndGo();
		BodyReportCut();
		HuntingSideTrip();
		Flank();
		OneDoor();
		Replan();
		Groups();
		OddCounts();
		Formation();
		MemberLost();
		LateJoiner();
		Coverage();
		AuthoredNodes();
		FearOfGunfire();
		FearOfDeath();
	}

	// ======================================================= P1: postures

	/// <summary>
	/// Guard_AI.md §4: a noise makes a sentry Curious; he stares, walks over,
	/// looks round, walks back, and stands down on his post facing his old way.
	/// </summary>
	private static void Curious()
	{
		H.Group("guard AI / curious");
		var g = new Grid(48, 28);
		g.Put(30, 14, 'a');
		var w = g.World();
		var e = w.Guards[0];
		e.Facing = e.PostFacing = Brad.Quarter;       // posted facing south
		int lx = C(20), ly = C(14);

		w.Notice(e, lx, ly, 45 * 10);
		H.Check("a noise makes a relaxed guard curious",
			e.State == GuardState.Curious && e.Task == GuardTask.Look, $"{e.State}/{e.Task}");

		var seen = new List<GuardTask> { e.Task };
		int closest = int.MaxValue, t = 0;
		for (; t < 60 * 30 && e.State != GuardState.Relaxed; t++)
		{
			w.Step(Idle);
			if (seen[^1] != e.Task) seen.Add(e.Task);
			closest = Math.Min(closest, Fx.Dist(e.X, e.Y, lx, ly));
		}
		string trail = string.Join(" > ", seen);
		H.Check("he stares, walks over, looks round, and walks back",
			trail == "Look > Investigate > LookAround > Return > Post", trail);
		H.Check("he actually went to the noise", closest < Tune.LkpReach, $"{closest / Fx.One} px");
		H.Check("and stands down relaxed on his post",
			e.State == GuardState.Relaxed && Fx.Dist(e.X, e.Y, e.HomeX, e.HomeY) < Tune.WaypointReach,
			$"{e.State} at {(e.X - e.HomeX) / Fx.One},{(e.Y - e.HomeY) / Fx.One} from post, {t / 60.0:F1} s");
		int off = Math.Abs(Brad.Norm(e.Facing - e.PostFacing));
		H.Check("facing the way he was posted", off < 1000, $"{off} BRAD off");
		H.Check("and nothing compromised by a noise", !w.Net.Compromised && !w.Net.Active);

		// A noise under the curious threshold does nothing at all.
		var w2 = g.World();
		w2.Notice(w2.Guards[0], lx, ly, 20 * 10);
		H.Check("a faint noise leaves him relaxed", w2.Guards[0].State == GuardState.Relaxed);

		// A loud one (past AwHunt) sends him hurrying.
		var w3 = g.World();
		var e3 = w3.Guards[0];
		w3.Notice(e3, lx, ly, 70 * 10);
		for (int k = 0; k < 60 && e3.Task != GuardTask.Investigate; k++) w3.Step(Idle);
		int x0 = e3.X;
		for (int k = 0; k < 30; k++) w3.Step(Idle);
		int moved = Math.Abs(e3.X - x0);
		H.Check("a loud noise is investigated at a hurry",
			moved > 30 * Fx.PerTick(Tune.SpeedInvestigate, Fx.ScaleDen), $"{moved / Fx.One} px in 0.5 s");
	}

	/// <summary>A patroller who investigates rejoins his route and walks on.</summary>
	private static void CuriousPatroller()
	{
		H.Group("guard AI / curious patroller");
		var g = new Grid(48, 28);
		g.Put(10, 20, 'a');
		g.Routes.Add("> a 10,20 40,20");
		var w = g.World();
		var e = w.Guards[0];
		for (int k = 0; k < 30; k++) w.Step(Idle);

		w.Notice(e, C(20), C(8), 45 * 10);
		int t = 0;
		for (; t < 60 * 40 && e.State != GuardState.Relaxed; t++) w.Step(Idle);
		H.Check("the patroller stands down", e.State == GuardState.Relaxed && e.Task == GuardTask.Patrol,
			$"{e.State}/{e.Task} after {t / 60.0:F1} s");
		H.Check("back on his route", Math.Abs(e.Y - C(20)) < Tune.WaypointReach * 2,
			$"row {e.Y / Level.CellFx}");
		int wp = e.WaypointIndex, adv = 0;
		for (int k = 0; k < 60 * 15; k++) { w.Step(Idle); if (e.WaypointIndex != wp) { adv++; wp = e.WaypointIndex; } }
		H.Check("and walks it again", adv >= 1, $"{adv} waypoints");
	}

	/// <summary>
	/// Guard_AI.md §5.1: inside SnapSightRange, in the cone and in view, the
	/// player is recognised after SnapReactTicks whatever his stance. Behind the
	/// guard, nothing.
	/// </summary>
	private static void SnapSight()
	{
		H.Group("guard AI / snap sight");
		foreach (bool sneak in new[] { false, true })
		{
			var g = new Grid(48, 28);
			g.Put(20, 14, 'a');
			var w = g.World();
			var e = w.Guards[0];
			e.Facing = 0;
			w.Player.X = C(20) + 100 * Fx.One;
			w.Player.Y = C(14);
			var input = new InputFrame(0, 0, 0, sneak ? InputFrame.FSneak : (byte)0);
			int at = -1;
			for (int t = 0; t < 60; t++)
			{
				int kx = w.Player.X, ky = w.Player.Y;
				w.Step(input);
				w.Player.X = kx; w.Player.Y = ky; w.Player.Alive = true;
				if (e.State == GuardState.Combat) { at = t + 1; break; }
			}
			H.Check($"100 px in front, {(sneak ? "sneaking" : "still")}: combat in the reaction time",
				at == Tune.SnapReactTicks, $"tick {at}, expected {Tune.SnapReactTicks}");
		}

		var gb = new Grid(48, 28);
		gb.Put(20, 14, 'a');
		var wb = gb.World();
		var eb = wb.Guards[0];
		eb.Facing = Brad.Half;                        // facing away, west
		wb.Player.X = C(20) + 100 * Fx.One;
		wb.Player.Y = C(14);
		for (int t = 0; t < 120; t++)
		{
			int kx = wb.Player.X, ky = wb.Player.Y;
			wb.Step(Idle);
			wb.Player.X = kx; wb.Player.Y = ky;
		}
		H.Check("100 px behind him: nothing", eb.State == GuardState.Relaxed && eb.SnapMt == 0,
			$"{eb.State}, snap {eb.SnapMt}");
	}

	/// <summary>
	/// A silenced round nobody heard still tells its TARGET where it came from
	/// (Guard_AI.md §5.1, "shot at").
	/// </summary>
	private static void ShotAtSilently()
	{
		H.Group("guard AI / shot at");
		var g = new Grid(48, 28);
		g.Put(34, 14, 'a');
		var sb = new System.Text.StringBuilder("name: fixture\ngrid:\n");
		for (int r = 0; r < g.H; r++) { for (int c = 0; c < g.W; c++) sb.Append(g.G[r * g.W + c]); sb.Append('\n'); }
		var w = new SimWorld(Level.FromText(sb.ToString()), 31, new Loadout(WeaponId.Welrod));
		var e = w.Guards[0];
		e.Health = 10000; e.Armour = 0; e.ArmourMax = 0;
		e.Facing = 0;                                 // facing away, east
		w.Player.X = C(24); w.Player.Y = C(14);       // 200 px west: past a Welrod's report
		H.Check("fixture: out of earshot of the shot",
			Fx.Dist(e.X, e.Y, w.Player.X, w.Player.Y) > WeaponCatalog.Get(WeaponId.Welrod).GunshotRadius);

		bool hit = false;
		w.Step(new InputFrame(0, 0, 0, InputFrame.FFire));
		for (int t = 0; t < 30 && !hit; t++)
		{
			w.Step(Idle);
			hit = Logged(w, SimEventKind.GuardHurt);
		}
		H.Check("the round hits him", hit);
		H.Check("he is in the fight", e.State == GuardState.Combat, e.State.ToString());
		H.Check("with the shooter as his LKP",
			e.HasLkp && Fx.Dist(e.LkpX, e.LkpY, C(24), C(14)) < Level.CellFx, $"{e.LkpX / Fx.One},{e.LkpY / Fx.One}");
	}

	// ======================================================= P2: the radio

	/// <summary>§5.2: alone, he radios; with an ally in walking range, he shouts.</summary>
	private static void AlliesAndRadio()
	{
		H.Group("guard AI / allies and the radio");
		var g = new Grid(48, 28);
		g.Put(20, 14, 'a');
		var w = g.World();
		var e = w.Guards[0];
		w.EnterCombat(e, C(40), C(14), false);
		H.Check("a guard alone keys his radio",
			e.Task == GuardTask.Radio && e.Radio == RadioPurpose.Backup && Logged(w, SimEventKind.RadioStart),
			$"{e.Task}/{e.Radio}");

		var g2 = new Grid(48, 28);
		g2.Put(20, 14, 'a');
		g2.Put(26, 14, 'b');                          // 120 px away, open floor
		var w2 = g2.World();
		var a = ById(w2, 'a');
		var b = ById(w2, 'b');
		w2.EnterCombat(a, C(40), C(14), false);
		H.Check("a guard with an ally does not radio", a.Task != GuardTask.Radio && a.Radio == RadioPurpose.None,
			$"{a.Task}/{a.Radio}");
		H.Check("he shouts the ally into the fight", b.State == GuardState.Combat && b.Task != GuardTask.Radio,
			$"{b.State}/{b.Task}");

		// An ally behind a wall is not "nearby": 60 px as the crow flies, a long
		// walk round.
		var g3 = new Grid(48, 28);
		g3.WallCol(22, 1, 24);
		g3.Put(20, 14, 'a');
		g3.Put(24, 14, 'b');
		var w3 = g3.World();
		var a3 = ById(w3, 'a');
		w3.EnterCombat(a3, C(10), C(14), false);
		H.Check("an ally behind a wall does not count: he radios",
			a3.Task == GuardTask.Radio && ById(w3, 'b').State == GuardState.Relaxed,
			$"{a3.Task}, b {ById(w3, 'b').State}");

		// One call is enough.
		var g4 = new Grid(80, 40);
		g4.Put(10, 10, 'a');
		g4.Put(60, 30, 'b');
		var w4 = g4.World();
		w4.EnterCombat(ById(w4, 'a'), C(40), C(20), false);
		w4.EnterCombat(ById(w4, 'b'), C(40), C(20), false);
		H.Eq("two lone guards in one fight: only one calls", CountTask(w4, GuardTask.Radio), 1);
	}

	/// <summary>§5.3: take the caller down mid-call and nobody comes.</summary>
	private static void RadioCut()
	{
		H.Group("guard AI / radio cut");
		var g = new Grid(80, 40);
		g.Put(20, 20, 'a');
		g.Put(60, 10, 'b');
		g.Put(60, 30, 'c');
		var w = g.World();
		var e = ById(w, 'a');
		w.EnterCombat(e, C(10), C(20), false);        // trouble to the west: he faces it
		for (int t = 0; t < 30; t++) w.Step(Idle);
		H.Check("fixture: mid-call", e.Task == GuardTask.Radio && e.RadioMt > 0, $"{e.Task} {e.RadioMt}");

		// Walk up behind him (he is facing west, keying the radio) and take him.
		w.Player.X = e.X + 30 * Fx.One;
		w.Player.Y = e.Y;
		w.Step(new InputFrame(0, 0, Brad.Half, InputFrame.FSubdue));
		H.Check("subdued mid-call", e.State == GuardState.Down, e.State.ToString());
		H.Check("the call is logged as cut", Logged(w, SimEventKind.RadioCut));

		w.Player.X = C(2); w.Player.Y = C(2);          // back in the pocket
		for (int t = 0; t < Tune.RadioTicks * 3; t++) w.Step(Idle);
		H.Check("nobody was sent", CountTask(w, GuardTask.Rally) == 0 && !w.Net.Called,
			$"{CountTask(w, GuardTask.Rally)} rallying, called {w.Net.Called}");
		H.Check("and the others never heard", ById(w, 'b').State == GuardState.Relaxed
			&& ById(w, 'c').State == GuardState.Relaxed);
	}

	/// <summary>Seven guards far from the caller: 2 + kills responders, capped at 5.</summary>
	private static SimWorld CallerAndSeven(int kills, out Actor caller)
	{
		var g = new Grid(80, 40);
		g.Put(40, 20, 'a');
		// Every one beyond ally range (17.5 cells walked) and callout range (15).
		int[] cs = { 10, 70, 10, 70, 40, 40, 5 };
		int[] rs = { 5, 5, 35, 35, 1, 38, 20 };
		for (int i = 0; i < cs.Length; i++) g.Put(cs[i], rs[i], (char)('b' + i));
		var w = g.World();
		caller = ById(w, 'a');
		w.EnterCombat(caller, C(40), C(30), false);
		w.Net.IncidentKills = kills;
		for (int t = 0; t < Tune.RadioTicks + 2; t++) w.Step(Idle);
		return w;
	}

	private static void ResponderCount()
	{
		H.Group("guard AI / responders");
		foreach (var (kills, want) in new[] { (0, 2), (1, 3), (3, 5), (9, 5) })
		{
			var w = CallerAndSeven(kills, out var caller);
			H.Check($"{kills} kills this incident: {want} responders",
				CountTask(w, GuardTask.Rally) == want && w.Net.Called && Logged2(w),
				$"{CountTask(w, GuardTask.Rally)} rallying, caller {caller.Task}");
		}
	}

	private static bool Logged2(SimWorld w) => w.Net.Squads.Count == 1;

	/// <summary>
	/// Nearest by PATH, not by line: a guard 200 px away behind a long wall is
	/// passed over for two out in the open further off.
	/// </summary>
	private static void RespondersByPath()
	{
		H.Group("guard AI / responders by path");
		var g = new Grid(80, 40);
		g.WallCol(26, 1, 36);                         // open only at rows 37-38
		g.Put(20, 20, 'a');                           // caller
		g.Put(30, 20, 'b');                           // 200 px straight, ~880 px walked
		g.Put(20, 1, 'c');                            // 380 px, open
		g.Put(1, 6, 'd');                             // ~500 px, open
		var w = g.World();
		var caller = ById(w, 'a');
		int straightB = Fx.Dist(caller.X, caller.Y, ById(w, 'b').X, ById(w, 'b').Y);
		int straightC = Fx.Dist(caller.X, caller.Y, ById(w, 'c').X, ById(w, 'c').Y);
		H.Check("fixture: b is the nearest in a straight line", straightB < straightC);

		w.EnterCombat(caller, C(10), C(30), false);
		H.Check("fixture: the caller is alone", caller.Task == GuardTask.Radio, caller.Task.ToString());
		for (int t = 0; t < Tune.RadioTicks + 2; t++) w.Step(Idle);
		H.Check("the two nearest by path are sent",
			ById(w, 'c').Task == GuardTask.Rally && ById(w, 'd').Task == GuardTask.Rally,
			$"c {ById(w, 'c').Task}, d {ById(w, 'd').Task}");
		H.Check("the one behind the wall is not", ById(w, 'b').State == GuardState.Relaxed,
			$"b {ById(w, 'b').State}/{ById(w, 'b').Task}");
	}

	/// <summary>Patrollers answer before sentries leave their posts.</summary>
	private static void PatrollersFirst()
	{
		H.Group("guard AI / patrollers first");
		var g = new Grid(80, 40);
		g.Put(20, 20, 'a');                           // caller
		g.Put(20, 1, 'b');                            // sentry, 380 px
		g.Put(1, 6, 'c');                             // sentry, ~500 px
		g.Put(70, 20, 'd');                           // patroller, 1000 px
		g.Routes.Add("> d 70,20 70,10");
		var w = g.World();
		var caller = ById(w, 'a');
		w.EnterCombat(caller, C(10), C(30), false);
		for (int t = 0; t < Tune.RadioTicks + 2; t++) w.Step(Idle);
		H.Check("the patroller is sent, however far", ById(w, 'd').Task == GuardTask.Rally,
			ById(w, 'd').Task.ToString());
		H.Check("then the nearest sentry", ById(w, 'b').Task == GuardTask.Rally, ById(w, 'b').Task.ToString());
		H.Check("the other sentry keeps his post", ById(w, 'c').State == GuardState.Relaxed,
			ById(w, 'c').State.ToString());
	}

	/// <summary>
	/// §5.4: with nobody to send, the caller waits somewhere out of the LKP's
	/// sight, and when nobody has seen the player for ContactLostTicks the
	/// level is compromised and he hunts.
	/// </summary>
	private static void HoldAndLose()
	{
		H.Group("guard AI / hold and lose");
		var g = new Grid(48, 28);
		g.WallCol(24, 10, 18);
		g.Put(20, 14, 'a');
		var w = g.World();
		var e = w.Guards[0];
		int lx = C(40), ly = C(14);
		w.EnterCombat(e, lx, ly, false);
		for (int t = 0; t < Tune.RadioTicks + 2; t++) w.Step(Idle);
		H.Check("the call went out to nobody", w.Net.Called && e.Task == GuardTask.HoldForBackup,
			$"called {w.Net.Called}, {e.Task}");
		H.Check("he holds out of the LKP's line of sight",
			!Geometry.ClearLine(w.Opaque, e.TaskX, e.TaskY, lx, ly),
			$"hold {e.TaskX / Level.CellFx},{e.TaskY / Level.CellFx}");

		int t2 = 0;
		for (; t2 < 60 * 30 && !w.Net.Compromised; t2++) w.Step(Idle);
		H.Check("contact is lost and the level compromised", w.Net.Compromised, $"{t2 / 60.0:F1} s");
		H.Check("the caller hunts", e.State == GuardState.Hunting, $"{e.State}/{e.Task}");
		H.Eq("and the alarm is at 3", w.Alarm.Level, AlarmState.Compromised);

		bool relaxed = false;
		for (int t = 0; t < 60 * 60; t++)
		{
			w.Step(Idle);
			if (e.State == GuardState.Relaxed || e.State == GuardState.Curious) relaxed = true;
		}
		H.Check("and never stands down again", !relaxed && w.Alarm.Level == AlarmState.Compromised);
	}

	/// <summary>
	/// §5.5: responders gather at the caller's hold point, then the squad moves
	/// on the LKP; once it is searched and nobody has seen the player, every
	/// guard on the level is hunting.
	/// </summary>
	private static void RallyAndGo()
	{
		H.Group("guard AI / rally and go");
		var w = CallerAndSeven(0, out var caller);
		var sq = w.Net.Squads.Count > 0 ? w.Net.Squads[0] : null;
		H.Check("fixture: a squad formed", sq != null && sq.Expected == 2);
		if (sq == null) return;

		int t = 0;
		for (; t < 60 * 30 && !sq.Go; t++) w.Step(Idle);
		H.Check("the squad moves out once two have gathered", sq.Go, $"{t / 60.0:F1} s");
		int near = 0;
		for (int m = 0; m < sq.Members.Count; m++)
		{
			var g = w.Guards[sq.Members[m]];
			if (sq.Members[m] != sq.Anchor && Fx.Dist(g.X, g.Y, sq.RallyX, sq.RallyY) < Tune.RallyRadius) near++;
		}
		H.Check("having actually gathered", near >= 2, $"{near} at the rally point");
		H.Check("before the patience ran out", t < Tune.BackupWaitMaxTicks, $"{t} ticks");
		bool moving = true;
		for (int m = 0; m < sq.Members.Count; m++)
		{
			var task = w.Guards[sq.Members[m]].Task;
			if (task != GuardTask.Converge && task != GuardTask.Assault && task != GuardTask.SearchLkp) moving = false;
		}
		H.Check("all of them on the LKP", moving);

		for (t = 0; t < 60 * 60 && !w.Net.Compromised; t++) w.Step(Idle);
		H.Check("the player is never found: the level is compromised", w.Net.Compromised);
		// The ones still searching when it flipped join as their searches end.
		for (t = 0; t < 60 * 20; t++)
		{
			bool fighting = false;
			for (int i = 0; i < w.Guards.Count; i++) if (w.Guards[i].State == GuardState.Combat) fighting = true;
			if (!fighting) break;
			w.Step(Idle);
		}
		string notHunting = "";
		for (int i = 0; i < w.Guards.Count; i++)
			if (!w.Guards[i].Prone && w.Guards[i].State != GuardState.Hunting)
				notHunting += $" {w.Guards[i].Id}:{w.Guards[i].State}/{w.Guards[i].Task}";
		H.Check("and every guard is hunting", notHunting.Length == 0, notHunting);
		// Sentries hold; the squad came from a fight, so it sweeps (P4).
		int holding = CountTask(w, GuardTask.HoldPost);
		int sweeping = CountTask(w, GuardTask.Sweep) + CountTask(w, GuardTask.WatchExit);
		H.Check("sentries hold their posts, the squad sweeps",
			holding + sweeping == w.Guards.Count && sweeping >= sq.Members.Count,
			$"{holding} holding, {sweeping} sweeping");
	}

	/// <summary>§7: a body is reported, and taking the finder mid-report keeps it quiet.</summary>
	private static void BodyReportCut()
	{
		H.Group("guard AI / body report cut");
		var g = new Grid(48, 28);
		g.Put(20, 14, 'a');                           // the body
		g.Put(24, 14, 'b');                           // the finder
		var w = g.World();
		var body = ById(w, 'a');
		var finder = ById(w, 'b');
		body.State = GuardState.Dead; body.Alive = false;
		finder.Facing = Brad.Half;                    // looking straight at it

		w.Step(Idle);
		H.Check("the finder begins his report", finder.Task == GuardTask.Radio && finder.Radio == RadioPurpose.Report,
			$"{finder.Task}/{finder.Radio}");

		w.Player.X = finder.X + 30 * Fx.One;
		w.Player.Y = finder.Y;
		w.Step(new InputFrame(0, 0, Brad.Half, InputFrame.FSubdue));
		w.Player.X = C(2); w.Player.Y = C(2);
		for (int t = 0; t < Tune.RadioTicks * 3; t++) w.Step(Idle);
		H.Check("subduing him mid-report keeps the level quiet",
			finder.State == GuardState.Down && !w.Net.Compromised && w.Alarm.Level < AlarmState.Compromised,
			$"{finder.State}, compromised {w.Net.Compromised}, alarm {w.Alarm.Level}");
	}

	/// <summary>
	/// A hunting guard still investigates a noise, and goes back to hunting,
	/// never to relaxed.
	/// </summary>
	private static void HuntingSideTrip()
	{
		H.Group("guard AI / hunting side trip");
		// A body report nobody stops compromises the level.
		var g = new Grid(48, 28);
		g.Put(20, 14, 'a');
		g.Put(24, 14, 'b');
		var w2 = g.World();
		var body = ById(w2, 'b');
		body.State = GuardState.Dead; body.Alive = false;
		var h = ById(w2, 'a');
		h.Facing = 0;
		for (int t = 0; t < Tune.RadioTicks + 5; t++) w2.Step(Idle);
		H.Check("fixture: compromised, hunting", w2.Net.Compromised && h.State == GuardState.Hunting,
			$"{h.State}/{h.Task}");

		w2.Notice(h, C(30), C(20), 45 * 10);
		H.Check("a noise sends a hunting guard to look", h.Task == GuardTask.Look && h.State == GuardState.Hunting,
			$"{h.State}/{h.Task}");
		// He found the body and reported it: he came from a fight, so he is
		// mobile and sweeps (P4) rather than going back to his post.
		bool relaxed = false, backToHunt = false;
		for (int t = 0; t < 60 * 30; t++)
		{
			w2.Step(Idle);
			if (h.State != GuardState.Hunting) relaxed = true;
			if (h.Task == GuardTask.Sweep || h.Task == GuardTask.WatchExit || h.Task == GuardTask.HoldPost)
			{ backToHunt = true; break; }
		}
		H.Check("he returns to hunting, not to rest", backToHunt && !relaxed, $"{h.State}/{h.Task}");
	}

	// ======================================================= P3: the assault

	/// <summary>
	/// A room with an LKP inside and a door on the west, north and south (each
	/// two cells, so a guard fits). The squad gathers to the west, so every
	/// shortest route is the west door: anything else is the flank at work.
	/// </summary>
	private static Grid Room(bool threeDoors)
	{
		var g = new Grid(80, 40);
		for (int c = 50; c <= 70; c++) { g.Put(c, 10, '#'); g.Put(c, 30, '#'); }
		g.WallCol(50, 10, 30);
		g.WallCol(70, 10, 30);
		g.Put(50, 19, '.'); g.Put(50, 20, '.');                  // west
		if (threeDoors)
		{
			g.Put(59, 10, '.'); g.Put(60, 10, '.');              // north
			g.Put(59, 30, '.'); g.Put(60, 30, '.');              // south
		}
		g.Put(30, 20, 'a');                                      // the caller
		g.Put(30, 2, 'b');                                       // beyond ally range
		g.Put(30, 38, 'c');
		return g;
	}

	private static readonly (string Name, int C0, int R0, int C1, int R1)[] Doors =
	{
		("west", 50, 19, 50, 20), ("north", 59, 10, 60, 10), ("south", 59, 30, 60, 30),
	};

	/// <summary>The cells a route passes through, from where he stands, sampled every 4 px.</summary>
	private static HashSet<int> RouteCells(Actor g, int w)
	{
		var cells = new HashSet<int>();
		int px = g.X, py = g.Y;
		for (int k = 0; k < g.RouteX.Count; k++)
		{
			int qx = g.RouteX[k], qy = g.RouteY[k];
			int n = Fx.Dist(px, py, qx, qy) / (4 * Fx.One) + 1;
			for (int i = 0; i <= n; i++)
			{
				long x = px + (long)(qx - px) * i / n, y = py + (long)(qy - py) * i / n;
				cells.Add((int)(y / Level.CellFx) * w + (int)(x / Level.CellFx));
			}
			px = qx; py = qy;
		}
		return cells;
	}

	private static string DoorOf(HashSet<int> cells, int w)
	{
		foreach (var d in Doors)
			for (int r = d.R0; r <= d.R1; r++)
				for (int c = d.C0; c <= d.C1; c++)
					if (cells.Contains(r * w + c)) return d.Name;
		return "none";
	}

	/// <summary>Squad formed and gone, with the members that planned a route.</summary>
	private static List<Actor> Assaulting(SimWorld w, out Squad? sq)
	{
		w.EnterCombat(ById(w, 'a'), C(60), C(20), false);
		sq = null;
		for (int t = 0; t < 60 * 40; t++)
		{
			w.Step(Idle);
			if (w.Net.Squads.Count > 0 && w.Net.Squads[0].Go) { sq = w.Net.Squads[0]; break; }
		}
		var list = new List<Actor>();
		if (sq == null) return list;
		foreach (int i in sq.Members) if (w.Guards[i].Task == GuardTask.Assault) list.Add(w.Guards[i]);
		return list;
	}

	/// <summary>
	/// Guard_AI.md §5.5: the squad takes different ways in where the map offers
	/// them, and the ones with the shorter walk wait so they all arrive together.
	/// </summary>
	private static void Flank()
	{
		H.Group("guard AI / flank");
		var w = Room(true).World();
		var squad = Assaulting(w, out var sq);
		H.Eq("the whole squad plans an assault", squad.Count, 3);
		if (squad.Count < 2 || sq == null) return;

		var routes = new List<HashSet<int>>();
		var doors = new HashSet<string>();
		string used = "";
		foreach (var g in squad)
		{
			var cells = RouteCells(g, 80);
			routes.Add(cells);
			string d = DoorOf(cells, 80);
			doors.Add(d);
			used += $" {g.Id}:{d}";
		}
		H.Check("each takes a different door", doors.Count == squad.Count && !doors.Contains("none"), used);

		double worst = 0;
		for (int i = 0; i < routes.Count; i++)
			for (int j = i + 1; j < routes.Count; j++)
			{
				int shared = 0;
				foreach (int c in routes[i]) if (routes[j].Contains(c)) shared++;
				worst = Math.Max(worst, (double)shared / Math.Min(routes[i].Count, routes[j].Count));
			}
		H.Check("no two routes share 30% of their cells", worst < 0.30, $"worst overlap {worst:P0}");

		// The west door is the short way: whoever took it waits for the rest.
		Actor? westMan = null;
		for (int k = 0; k < squad.Count; k++) if (DoorOf(routes[k], 80) == "west") westMan = squad[k];
		H.Check("the man with the short walk holds back",
			westMan != null && westMan.WaitMt > 0, westMan == null ? "nobody west" : $"{westMan.WaitMt / Actor.Mt} ticks");
		int maxWait = 0;
		foreach (var g in squad) maxWait = Math.Max(maxWait, g.WaitMt);
		H.Check("but never longer than EtaSyncMaxTicks", maxWait <= Tune.EtaSyncMaxTicks * Actor.Mt,
			$"{maxWait / Actor.Mt} ticks");

		var arrived = new Dictionary<char, int>();
		for (int t = 0; t < 60 * 30 && arrived.Count < squad.Count; t++)
		{
			w.Step(Idle);
			foreach (var g in squad)
				if (!arrived.ContainsKey(g.Id) && (g.Task == GuardTask.SearchLkp
					|| Fx.Dist(g.X, g.Y, C(60), C(20)) < Tune.LkpReach))
					arrived[g.Id] = t;
		}
		H.Eq("every man gets there", arrived.Count, squad.Count);
		int first = int.MaxValue, lastIn = 0;
		foreach (var kv in arrived) { first = Math.Min(first, kv.Value); lastIn = Math.Max(lastIn, kv.Value); }
		H.Check("and they break in together", arrived.Count == squad.Count && lastIn - first <= Tune.EtaSyncMaxTicks,
			$"first {first}, last {lastIn}: {(lastIn - first) / 60.0:F2} s apart");
	}

	/// <summary>One way in: the flank has nothing to work with, and the squad
	/// stacks through the door rather than failing.</summary>
	private static void OneDoor()
	{
		H.Group("guard AI / one door");
		var w = Room(false).World();
		var squad = Assaulting(w, out _);
		H.Eq("the whole squad plans an assault", squad.Count, 3);
		string used = "";
		bool allWest = true;
		foreach (var g in squad)
		{
			string d = DoorOf(RouteCells(g, 80), 80);
			used += $" {g.Id}:{d}";
			if (d != "west") allWest = false;
		}
		H.Check("they all come through the only door", allWest, used);

		int reached = 0;
		var done = new HashSet<char>();
		for (int t = 0; t < 60 * 30 && reached < squad.Count; t++)
		{
			w.Step(Idle);
			foreach (var g in squad)
				if (!done.Contains(g.Id) && g.Task == GuardTask.SearchLkp) { done.Add(g.Id); reached++; }
		}
		H.Eq("and every one of them reaches the LKP", reached, squad.Count);
	}

	/// <summary>An LKP that moves re-plans the assault to the new one.</summary>
	private static void Replan()
	{
		H.Group("guard AI / re-plan");
		var w = Room(true).World();
		var squad = Assaulting(w, out var sq);
		if (squad.Count == 0 || sq == null) { H.Check("fixture: an assault began", false); return; }

		int nx = C(66), ny = C(26);
		w.Net.SetIntel(nx, ny);
		for (int t = 0; t < Tune.FlankReplanTicks + 5; t++) w.Step(Idle);
		H.Check("the squad re-plans to the new LKP", sq.PlannedX == nx && sq.PlannedY == ny,
			$"planned {sq.PlannedX / Level.CellFx},{sq.PlannedY / Level.CellFx}");
		string off = "";
		foreach (var g in squad)
			if (g.Task == GuardTask.Assault && g.RouteX.Count > 0
				&& (g.RouteX[^1] != nx || g.RouteY[^1] != ny))
				off += $" {g.Id}";
		H.Check("and every route now ends there", off.Length == 0, off);
		int wait = 0;
		foreach (var g in squad) wait = Math.Max(wait, g.WaitMt);
		H.Eq("and nobody waits on a re-plan", wait, 0);
	}

	// ======================================================= P4: the sweep

	/// <summary>A floor of <paramref name="patrollers"/> patrollers (short routes,
	/// spread out) and <paramref name="sentries"/> sentries; the player sealed.</summary>
	private static SimWorld Floor(int patrollers, int sentries, out List<Actor> mobile)
	{
		var g = new Grid(80, 40);
		(int C, int R)[] spots = { (15, 10), (65, 10), (15, 30), (65, 30), (40, 20), (40, 5), (40, 35) };
		for (int k = 0; k < patrollers; k++)
		{
			char id = (char)('a' + k);
			var (c, r) = spots[k];
			g.Put(c, r, id);
			g.Routes.Add($"> {id} {c},{r} {c + 4},{r}");
		}
		(int C, int R)[] posts = { (5, 20), (75, 20), (30, 38) };
		for (int k = 0; k < sentries; k++) g.Put(posts[k].C, posts[k].R, (char)('m' + k));
		var w = g.World();
		mobile = new List<Actor>();
		foreach (var a in w.Guards) if (a.PathX != null) mobile.Add(a);
		return w;
	}

	/// <summary>§6.1: sentries hold, patrollers pair up, the pair nearest the exit keeps it.</summary>
	private static void Groups()
	{
		H.Group("guard AI / sweep groups");
		var w = Floor(4, 2, out var mobile);
		w.Compromise(C(40), C(20));
		H.Eq("four patrollers make two pairs", w.Net.Groups.Count, 2);
		bool pairs = true;
		foreach (var grp in w.Net.Groups) if (grp.Members.Count != 2) pairs = false;
		H.Check("of two each", pairs);
		string loose = "";
		foreach (var a in mobile) if (a.GroupId < 0 || a.State != GuardState.Hunting) loose += $" {a.Id}";
		H.Check("every patroller is in a group, hunting", loose.Length == 0, loose);
		H.Eq("sentries hold their posts", CountTask(w, GuardTask.HoldPost), 2);

		int exits = 0;
		SweepGroup? exitGroup = null;
		foreach (var grp in w.Net.Groups) if (grp.Exit) { exits++; exitGroup = grp; }
		H.Eq("exactly one group keeps the exit", exits, 1);
		// The exit is in the bottom-right corner (78,38): the pair from (65,30).
		H.Check("and it is the pair nearest the exit",
			exitGroup != null && exitGroup.Members.Contains(w.Guards.IndexOf(ById(w, 'd'))),
			exitGroup == null ? "none" : string.Join(",", exitGroup.Members));
		H.Check("its members are on WatchExit, the rest on Sweep",
			CountTask(w, GuardTask.WatchExit) == 2 && CountTask(w, GuardTask.Sweep) == 2);

		var ex = w.Level.Exit;
		int exitX = ex.X + ex.W / 2, exitY = ex.Y + ex.H / 2;
		bool strayed = false;
		for (int t = 0; t < 60 * 60; t++)
		{
			w.Step(Idle);
			if (exitGroup == null || exitGroup.Target < 0 || w.Sweep == null) continue;
			if (Fx.Dist(w.Sweep.X[exitGroup.Target], w.Sweep.Y[exitGroup.Target], exitX, exitY)
				> Tune.ExitWatchCells * Level.CellFx) strayed = true;
		}
		H.Check("the exit group only ever sweeps near the exit", !strayed);
	}

	/// <summary>An odd man out makes a trio; too few men and nobody keeps the exit.</summary>
	private static void OddCounts()
	{
		H.Group("guard AI / odd counts");
		var w5 = Floor(5, 0, out _);
		w5.Compromise(C(40), C(20));
		var sizes = new List<int>();
		foreach (var grp in w5.Net.Groups) sizes.Add(grp.Members.Count);
		sizes.Sort();
		H.Check("five make a pair and a trio", sizes.Count == 2 && sizes[0] == 2 && sizes[1] == 3,
			string.Join(",", sizes));

		var w3 = Floor(3, 0, out _);
		w3.Compromise(C(40), C(20));
		H.Check("three make one trio", w3.Net.Groups.Count == 1 && w3.Net.Groups[0].Members.Count == 3,
			$"{w3.Net.Groups.Count} groups");
		H.Check("and with fewer than four, nobody is spared for the exit",
			w3.Net.Groups.Count == 1 && !w3.Net.Groups[0].Exit);
	}

	/// <summary>
	/// §6.2: on the move, the leader faces the way he walks, the man behind
	/// him faces BACK down the trail, and he keeps about PairSpacing behind.
	/// </summary>
	private static void Formation()
	{
		H.Group("guard AI / formation");
		var w = Floor(2, 0, out var mobile);
		w.Compromise(C(40), C(20));
		var grp = w.Net.Groups[0];
		var lead = w.Guards[grp.Members[0]];
		var back = w.Guards[grp.Members[1]];

		int samples = 0, leadOk = 0, backOk = 0, spaced = 0;
		int slack = Tune.WatchSwayArc + 2600;        // his sway, plus 0.25 rad of turning lag
		for (int t = 0; t < 60 * 90; t++)
		{
			int lx = lead.X, ly = lead.Y;
			w.Step(Idle);
			bool walking = !grp.Dwelling && (lead.X != lx || lead.Y != ly)
				&& Fx.Dist(back.X, back.Y, lead.X, lead.Y) < Tune.GroupWaitDist;
			if (!walking) continue;
			samples++;
			if (Math.Abs(Brad.Norm(lead.Facing - grp.Heading)) <= slack) leadOk++;
			if (Math.Abs(Brad.Norm(back.Facing - (grp.Heading + Brad.Half))) <= slack) backOk++;
			int gap = Fx.Dist(back.X, back.Y, lead.X, lead.Y);
			if (gap >= Tune.PairSpacing / 2 && gap <= Tune.PairSpacing * 2) spaced++;
		}
		H.Check("fixture: they walked", samples > 60 * 10, $"{samples} ticks walking");
		H.Check("the leader faces the way they go", leadOk * 10 >= samples * 9, $"{leadOk}/{samples}");
		H.Check("the man behind watches their backs", backOk * 10 >= samples * 8, $"{backOk}/{samples}");
		H.Check("about PairSpacing behind", spaced * 10 >= samples * 8, $"{spaced}/{samples}");
	}

	/// <summary>§6.1: a group that loses a man folds its survivor into the nearest group.</summary>
	private static void MemberLost()
	{
		H.Group("guard AI / member lost");
		var w = Floor(4, 0, out _);
		w.Compromise(C(40), C(20));
		var victimGroup = w.Net.Groups[0];
		var victim = w.Guards[victimGroup.Members[0]];
		var survivor = w.Guards[victimGroup.Members[1]];
		// Pulled into a fight (a shot, say): out of his group at once.
		w.EnterCombat(victim, victim.X + 200 * Fx.One, victim.Y, false);
		H.Check("a man pulled into a fight leaves his group", victim.GroupId < 0);
		var joined = w.Net.GroupById(survivor.GroupId);
		H.Check("his partner joins the nearest group, making a trio",
			w.Net.Groups.Count == 1 && joined != null && joined.Members.Count == 3,
			$"{w.Net.Groups.Count} groups, survivor in {(joined == null ? "none" : joined.Members.Count.ToString())}");
	}

	/// <summary>A guard who lost contact after the level was compromised pairs up rather than roaming alone.</summary>
	private static void LateJoiner()
	{
		H.Group("guard AI / late joiner");
		var w = Floor(1, 2, out var mobile);
		var sentry = ById(w, 'n');                    // the far post: nobody in ally range
		// Pulled in as if by a shout: no radio call, so nobody is sent and it is
		// his fight alone. He converges, searches, and loses the player.
		w.EnterCombat(sentry, sentry.X - 200 * Fx.One, sentry.Y, true);
		w.Compromise(C(40), C(20));
		H.Check("fixture: one patroller sweeping alone, the other guard still fighting",
			w.Net.Groups.Count == 1 && w.Net.Groups[0].Members.Count == 1 && sentry.State == GuardState.Combat);

		for (int t = 0; t < 60 * 60 && sentry.State == GuardState.Combat; t++) w.Step(Idle);
		H.Check("losing contact, he joins the hunt", sentry.State == GuardState.Hunting, $"{sentry.State}/{sentry.Task}");
		var grp = w.Net.GroupById(sentry.GroupId);
		H.Check("mobile now, though he was a sentry: he came from a fight",
			sentry.Task == GuardTask.Sweep || sentry.Task == GuardTask.WatchExit, sentry.Task.ToString());
		H.Check("and pairs with the man sweeping alone",
			grp != null && grp.Members.Count == 2 && grp.Members.Contains(w.Guards.IndexOf(mobile[0])),
			grp == null ? "no group" : $"{grp.Members.Count} in his group");
	}

	/// <summary>
	/// §6.3: on the reference level, with nobody to find, the sweep sees every
	/// node on the floor.
	/// </summary>
	private static void Coverage()
	{
		H.Group("guard AI / coverage");
		var w = new SimWorld(Level.FromText(Program.ReadLevel("substation_4.txt")), 5);
		w.Player.X = -100000 * Fx.One;
		w.Player.Y = -100000 * Fx.One;
		w.Compromise(w.Level.WidthFx / 2, w.Level.HeightFx / 2);
		var map = w.Sweep;
		H.Check("fixture: the sweep map exists", map != null && map.Count > 10, map == null ? "" : $"{map.Count} nodes");
		if (map == null) return;

		var seen = new bool[map.Count];
		int left = map.Count, at = -1;
		for (int t = 0; t < 60 * 300 && left > 0; t++)
		{
			w.Step(Idle);
			for (int k = 0; k < map.Count; k++)
				// Staleness climbs, then a sighting zeroes it within the same
				// tick: zero at the end of a tick means seen in it.
				if (!seen[k] && map.Stale[k] == 0) { seen[k] = true; left--; }
			if (left == 0) at = t;
		}
		string missed = "";
		for (int k = 0; k < map.Count; k++)
			if (!seen[k]) missed += $" ({map.Cell[k] % w.Level.W},{map.Cell[k] / w.Level.W})";
		H.Check("every node on Substation 4 is seen within five minutes", left == 0,
			left == 0 ? $"{at / 60.0:F0} s for {map.Count} nodes" : $"{left} never seen:{missed}");
		Console.WriteLine($"  sweep coverage: substation_4, {map.Count} nodes, all seen in {at / 60.0:F0} s");
	}

	/// <summary>
	/// §6.3.1: a '*' is parsed, survives a round trip, replaces the auto node of
	/// its block, and is checked before plain floor.
	/// </summary>
	private static void AuthoredNodes()
	{
		H.Group("guard AI / authored nodes");
		var g = new Grid(48, 28);
		g.Put(10, 14, 'a');
		g.Put(14, 14, 'b');
		g.Routes.Add("> a 10,14 10,16");
		g.Routes.Add("> b 14,14 14,16");
		g.Put(27, 14, '*');
		var w = g.World();
		H.Check("a '*' is parsed as a sweep node", w.Level.SweepNodes.Count == 1
			&& w.Level.SweepNodes[0] == (27, 14));
		string text = w.Level.ToText();
		var again = Level.FromText(text);
		H.Check("and survives a round trip", again.SweepNodes.Count == 1 && again.SweepNodes[0] == (27, 14)
			&& again.ToText() == text);
		H.Check("it is floor to the nav grid", w.Level.Nav.Passable[14 * 48 + 27]);

		// No focus, so only distance and the authored bonus decide. (Nodes are
		// first picked on the tick after the compromise, so this takes.)
		w.Compromise(C(46), C(26));
		w.Net.HasFocus = false;
		var map = w.Sweep!;
		int authored = -1, sameBlock = 0;
		int b = Tune.SweepNodeCells;
		for (int k = 0; k < map.Count; k++)
		{
			if (map.Authored[k]) authored = k;
			else
			{
				int c = map.Cell[k] % 48, r = map.Cell[k] / 48;
				if (c / b == 27 / b && r / b == 14 / b) sameBlock++;
			}
		}
		H.Check("the map carries it as authored", authored >= 0 && map.Cell[authored] == 14 * 48 + 27);
		H.Eq("and no auto node shares its block", sameBlock, 0);

		var grp = w.Net.Groups[0];
		w.Step(Idle);
		H.Check("the group checks it before the plain floor nearer to hand", grp.Target == authored,
			$"picked {grp.Target} at ({(grp.Target >= 0 ? map.Cell[grp.Target] % 48 : -1)},{(grp.Target >= 0 ? map.Cell[grp.Target] / 48 : -1)})");

		// A '*' walled in is ignored, never thrown on.
		var sealedG = new Grid(48, 28);
		sealedG.Put(30, 10, '#'); sealedG.Put(32, 10, '#'); sealedG.Put(31, 9, '#'); sealedG.Put(31, 11, '#');
		for (int r = 7; r <= 13; r++) for (int c = 28; c <= 34; c++) sealedG.Put(c, r, '#');
		sealedG.Put(31, 10, '*');
		var ws = sealedG.World();
		H.NoThrow("a walled-in '*' does not break the map", () => ws.Compromise(C(20), C(20)));
	}

	// ======================================================= fear (§4.1)

	private const int FearSeeds = 400;

	/// <summary>One guard at ease 280 px east of the player, facing away; the
	/// player fires a Glock (400 px report) west into the wall.</summary>
	private static (SimWorld W, Actor E) GunfireFixture(ulong seed)
	{
		var g = new Grid(48, 28);
		g.Put(24, 14, 'a');
		var sb = new System.Text.StringBuilder("name: fixture\ngrid:\n");
		for (int r = 0; r < g.H; r++) { for (int c = 0; c < g.W; c++) sb.Append(g.G[r * g.W + c]); sb.Append('\n'); }
		var w = new SimWorld(Level.FromText(sb.ToString()), seed);
		var e = w.Guards[0];
		e.Facing = 0;                                 // east, away from the player
		w.Player.X = C(10); w.Player.Y = C(14);
		w.Player.Facing = Brad.Half;
		return (w, e);
	}

	/// <summary>
	/// A relaxed guard who hears bullets start flying rolls FearGunfireQ8 to
	/// freeze for FearTicks: measured over many seeds. Frozen, he does not
	/// move, turn or key his radio; then he carries on with the fight he was
	/// already in underneath. A guard not at ease never freezes at a shot.
	/// </summary>
	private static void FearOfGunfire()
	{
		H.Group("guard AI / fear of gunfire");
		var shoot = new InputFrame(0, 0, Brad.Half, InputFrame.FFire);
		int afraid = 0;
		(SimWorld W, Actor E)? frozen = null;
		for (ulong s = 0; s < FearSeeds; s++)
		{
			var (w, e) = GunfireFixture(1000 + s);
			w.Step(shoot);
			if (e.Afraid) { afraid++; frozen ??= (w, e); }
		}
		double rate = (double)afraid / FearSeeds;
		double want = Tune.FearGunfireQ8 / 256.0;
		H.Check("a relaxed guard hearing gunfire freezes at FearGunfireQ8",
			Math.Abs(rate - want) < 0.08, $"{rate:P0} of {FearSeeds}, expected {want:P0}");
		Console.WriteLine($"  fear: at ease, gunfire {rate:P1} (target {want:P1}) over {FearSeeds} seeds");

		if (frozen is { } f)
		{
			var (w, e) = f;
			H.Check("and he is in the fight underneath", e.State == GuardState.Combat, $"{e.State}/{e.Task}");
			int x = e.X, y = e.Y, facing = e.Facing, radio = e.RadioMt;
			bool still = true;
			int t = 1;
			for (; e.Afraid && t < Tune.FearTicks + 5; t++)
			{
				w.Step(new InputFrame(0, 0, 0, 0));
				if (e.Afraid && (e.X != x || e.Y != y || e.Facing != facing || e.RadioMt != radio)) still = false;
			}
			H.Check("frozen: he does not move, turn or key his radio", still);
			H.Check("for FearTicks", Math.Abs(t - Tune.FearTicks) <= 1, $"{t} ticks");
			for (int k = 0; k < 30; k++) w.Step(new InputFrame(0, 0, 0, 0));
			H.Check("then he carries on with the fight", e.State == GuardState.Combat && !e.Afraid
				&& (e.X != x || e.Y != y || e.Facing != facing || e.RadioMt > radio),
				$"{e.State}/{e.Task}");
		}
		else H.Check("fixture: somebody froze", false);

		int notAtEase = 0;
		foreach (var (state, task) in new[] { (GuardState.Curious, GuardTask.Look), (GuardState.Hunting, GuardTask.HoldPost),
			(GuardState.Combat, GuardTask.Converge) })
			for (ulong s = 0; s < FearSeeds / 4; s++)
			{
				var (w, e) = GunfireFixture(5000 + s);
				e.State = state; e.Task = task; e.SetLkp(e.X, e.Y);
				w.Step(shoot);
				if (e.Afraid) notAtEase++;
			}
		H.Eq("a guard not at ease never freezes at a shot", notAtEase, 0);
	}

	/// <summary>
	/// Seeing an ally DIE: FearAllyDeathQ8 at ease, FearAllyDeathCombatQ8 once in
	/// the fight. The kill is a Welrod from outside everybody's earshot, so the
	/// only roll made is the one being measured. A witness looking the other
	/// way sees nothing and never freezes.
	/// </summary>
	private static void FearOfDeath()
	{
		H.Group("guard AI / fear of an ally's death");
		double Rate(GuardState state, GuardTask task, int witnessFacing, out int kills)
		{
			int afraid = 0;
			kills = 0;
			for (ulong s = 0; s < FearSeeds; s++)
			{
				var g = new Grid(48, 28);
				g.Put(24, 14, 'v');                   // the victim
				g.Put(24, 22, 'w');                   // the witness, 160 px south
				var sb = new System.Text.StringBuilder("name: fixture\ngrid:\n");
				for (int r = 0; r < g.H; r++) { for (int c = 0; c < g.W; c++) sb.Append(g.G[r * g.W + c]); sb.Append('\n'); }
				var w = new SimWorld(Level.FromText(sb.ToString()), 9000 + s, new Loadout(WeaponId.Welrod));
				var v = ById(w, 'v');
				var wit = ById(w, 'w');
				v.Health = 1; v.Armour = 0; v.ArmourMax = 0;
				v.Facing = 0;
				wit.Facing = witnessFacing;
				wit.State = state; wit.Task = task; wit.SetLkp(wit.X, wit.Y);
				w.Player.X = C(14); w.Player.Y = C(14);   // 200 px west of the victim
				w.Player.Facing = 0;
				int heard = Math.Min(Fx.Dist(w.Player.X, w.Player.Y, v.X, v.Y), Fx.Dist(w.Player.X, w.Player.Y, wit.X, wit.Y));
				if (heard <= WeaponCatalog.Get(WeaponId.Welrod).GunshotRadius) throw new Exception("fixture: the shot is heard");
				w.Step(new InputFrame(0, 0, 0, InputFrame.FFire));
				for (int k = 0; k < 20 && v.Alive; k++) w.Step(new InputFrame(0, 0, 0, 0));
				if (v.Alive) continue;
				kills++;
				if (wit.Afraid) afraid++;
			}
			return kills == 0 ? -1 : (double)afraid / kills;
		}

		double atEase = Rate(GuardState.Relaxed, GuardTask.Post, -Brad.Quarter, out int k1);
		double fighting = Rate(GuardState.Combat, GuardTask.Converge, -Brad.Quarter, out int k2);
		double blind = Rate(GuardState.Relaxed, GuardTask.Post, Brad.Quarter, out int k3);
		H.Check("fixture: the victim dies in most seeds", k1 > FearSeeds / 2 && k2 > FearSeeds / 2 && k3 > FearSeeds / 2,
			$"{k1}/{k2}/{k3} kills of {FearSeeds}");
		double wantEase = Tune.FearAllyDeathQ8 / 256.0, wantFight = Tune.FearAllyDeathCombatQ8 / 256.0;
		H.Check("seeing an ally die, a guard at ease freezes at FearAllyDeathQ8",
			Math.Abs(atEase - wantEase) < 0.08, $"{atEase:P0}, expected {wantEase:P0}");
		H.Check("one already in the fight at FearAllyDeathCombatQ8",
			Math.Abs(fighting - wantFight) < 0.06, $"{fighting:P0}, expected {wantFight:P0}");
		H.Check("so he is much more likely to keep his head", fighting * 3 < atEase,
			$"{fighting:P0} fighting against {atEase:P0} at ease");
		H.Check("a witness looking away sees nothing and never freezes", blind == 0, $"{blind:P0}");
		Console.WriteLine($"  fear: ally's death, at ease {atEase:P1} (target {wantEase:P1}), in the fight {fighting:P1} (target {wantFight:P1})");
	}
}
