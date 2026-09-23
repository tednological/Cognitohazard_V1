using System;
using System.Collections.Generic;
using System.IO;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// The spec §4.3 assertions that need combat, records and stealth AI, plus the
/// §8.6 measured detection curve and the §8.7 regression.
///
/// These build purpose-made levels rather than using the reference one, so each
/// assertion isolates the behaviour it names.
/// </summary>
public static class Systems
{
	// ------------------------------------------------------------ fixtures

	private const int GW = Level.GW, GH = Level.GH;

	/// <summary>An empty room with a spawn and an exit tucked in a far corner,
	/// so nothing interferes with the behaviour under test.</summary>
	private static char[] Room()
	{
		var g = new char[GW * GH];
		for (int r = 0; r < GH; r++)
			for (int c = 0; c < GW; c++)
				g[r * GW + c] = (r == 0 || r == GH - 1 || c == 0 || c == GW - 1) ? '#' : '.';
		return g;
	}

	private static string Text(char[] g, params string[] routes)
	{
		var sb = new System.Text.StringBuilder();
		sb.Append("name: fixture\ngrid:\n");
		for (int r = 0; r < GH; r++)
		{
			for (int c = 0; c < GW; c++) sb.Append(g[r * GW + c]);
			sb.Append('\n');
		}
		foreach (var rt in routes) sb.Append(rt).Append('\n');
		return sb.ToString();
	}

	private static void Put(char[] g, int c, int r, char ch) => g[r * GW + c] = ch;

	private static int CellCentre(int cell) => cell * Level.CellFx + Level.CellFx / 2;

	private static InputFrame Idle => new InputFrame(0, 0, 0, 0);

	private static Actor ById(SimWorld w, char id)
	{
		for (int i = 0; i < w.Guards.Count; i++) if (w.Guards[i].Id == id) return w.Guards[i];
		throw new Exception("no guard " + id);
	}

	// ------------------------------------------------------- detection curve

	/// <summary>
	/// Spec §8.6: time from entering an unobstructed sightline, with a sentry
	/// facing the player. Reports BOTH the moment the guard commits to Engage
	/// and the moment it actually fires; see DetectionCurve for why both matter.
	/// Returns seconds, or -1 if it never happened inside the window.
	/// </summary>
	private static void Measure(int rangePx, bool sneaking, bool moving,
		out double toEngage, out double toShot)
	{
		var g = Room();

		// Guard at a fixed cell; player placed rangePx to its east, on the same
		// row, with a clear line between them.
		int guardCell = 4;
		int guardX = CellCentre(guardCell);
		int row = 14;
		Put(g, guardCell, row, 'a');
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');

		var level = Level.FromText(Text(g));
		var w = new SimWorld(level, 4242);

		var guard = w.Guards[0];
		guard.Facing = 0;                       // due east, straight at the player
		guard.State = GuardState.Relaxed; guard.Task = GuardTask.Post;
		guard.PathX = null; guard.PathY = null;

		var p = w.Player;
		p.X = guardX + rangePx * Fx.One;
		p.Y = CellCentre(row);

		byte flags = sneaking ? InputFrame.FSneak : (byte)0;
		var input = new InputFrame(moving ? 1 : 0, 0, 0, flags);

		toEngage = -1; toShot = -1;

		for (int tick = 0; tick < 60 * 30; tick++)
		{
			// Hold the player at a constant range and alive: "moving" here means
			// the stance multiplier, not actually closing the distance, which
			// would change the measurement mid-run. Keeping the player alive
			// stops the guard's own bullet from ending the measurement early.
			int keepX = p.X, keepY = p.Y;
			w.Step(input);
			p.X = keepX; p.Y = keepY;
			p.Alive = true;

			if (toEngage < 0 && guard.State == GuardState.Combat)
				toEngage = (tick + 1) / 60.0;

			foreach (var ev in w.Log.Events)
				if (ev.Kind == SimEventKind.GuardShot)
				{
					toShot = (tick + 1) / 60.0;
					return;
				}
		}
	}

	private static void DetectionCurve()
	{
		H.Group("detection curve (spec 8.6)");

		// range -> (walking, sneaking, standing still)
		//
		// DELIBERATE DEVIATION (Guard_AI.md §10): the 120 px row is SNAP SIGHT.
		// Inside Tune.SnapSightRange a player in plain view in the cone is
		// recognised after SnapReactTicks, in every stance, instead of filling
		// the meter; the spec's row was 0.95 / 1.57 / 2.55. The 250 and 380 px
		// rows are the spec's own and must still hold within 10%.
		double snap = Tune.SnapReactTicks / 60.0;
		var table = new (int Range, double Walk, double Sneak, double Still)[]
		{
			(120, snap, snap, snap),
			(250, 1.63, 2.73, 4.40),
			(380, 4.12, 6.63, 10.53),
		};

		var measured = new List<(int, double, double, double)>();
		var shots = new List<(double, double, double)>();

		foreach (var row in table)
		{
			Measure(row.Range, false, true, out double walkE, out double walkS);
			Measure(row.Range, true, true, out double sneakE, out double sneakS);
			Measure(row.Range, false, false, out double stillE, out double stillS);
			measured.Add((row.Range, walkE, sneakE, stillE));
			shots.Add((walkS, sneakS, stillS));

			// The spec table is asserted against time-to-ENGAGE, not time-to-shot.
			// See the note printed below: §8.6's own numbers and §8.5's 0.45s aim
			// delay cannot both describe the same measurement.
			Within($"{row.Range}px walking", walkE, row.Walk);
			Within($"{row.Range}px sneaking", sneakE, row.Sneak);
			Within($"{row.Range}px standing still", stillE, row.Still);
		}

		// Whatever the table means, the gap between committing and firing must be
		// the §8.5 aim delay, and nothing else.
		double worstGap = 0;
		for (int i = 0; i < measured.Count; i++)
		{
			var (_, we, se, ste) = measured[i];
			var (ws, ss, sts) = shots[i];
			foreach (var (e2, s2) in new[] { (we, ws), (se, ss), (ste, sts) })
				if (e2 > 0 && s2 > 0) worstGap = Math.Max(worstGap, Math.Abs((s2 - e2) - 0.45));
		}
		H.Check("shot follows engage by the 0.45s aim delay", worstGap <= 0.05,
			$"worst deviation {worstGap:F3}s");

		// Monotonic in both distance and stance, with standing still stealthiest.
		bool monoDist = true, monoStance = true;
		for (int i = 0; i < measured.Count; i++)
		{
			var (_, wk, sn, st) = measured[i];
			if (wk > 0 && sn > 0 && st > 0 && !(wk <= sn && sn <= st)) monoStance = false;
			if (i > 0)
			{
				var prev = measured[i - 1];
				if (measured[i].Item2 > 0 && prev.Item2 > 0 && measured[i].Item2 < prev.Item2)
					monoDist = false;
			}
		}
		H.Check("monotonic in distance", monoDist);
		H.Check("monotonic in stance (still is stealthiest)", monoStance);

		Console.WriteLine();
		Console.WriteLine("  detection curve - time to ENGAGE (spec 8.6 target in brackets):");
		Console.WriteLine("    range   walking          sneaking         standing still");
		for (int i = 0; i < measured.Count; i++)
		{
			var (r, wk, sn, st) = measured[i];
			var t = table[i];
			Console.WriteLine($"    {r,4}px  {Fmt(wk, t.Walk)}  {Fmt(sn, t.Sneak)}  {Fmt(st, t.Still)}");
		}
		Console.WriteLine();
		Console.WriteLine("  time to FIRST SHOT (engage + the 8.5 aim delay of 0.45s):");
		Console.WriteLine("    range   walking          sneaking         standing still");
		for (int i = 0; i < shots.Count; i++)
		{
			var (ws, ss, sts) = shots[i];
			var t = table[i];
			Console.WriteLine($"    {table[i].Range,4}px  {Fmt(ws, t.Walk)}  {Fmt(ss, t.Sneak)}  {Fmt(sts, t.Still)}");
		}
		Console.WriteLine();
	}

	private static string Fmt(double got, double want)
		=> got < 0 ? $"never  [{want,5:F2}]" : $"{got,5:F2}s [{want,5:F2}]";

	private static void Within(string name, double got, double want)
	{
		if (got < 0) { H.Check(name, false, $"never fired; expected {want:F2}s"); return; }
		double err = Math.Abs(got - want) / want;
		H.Check(name, err <= 0.10, $"expected {want:F2}s +/-10%, got {got:F2}s ({err * 100:F1}% off)");
	}

	// ------------------------------------------------------ stimulus routing

	private static void GunshotPropagation()
	{
		H.Group("gunshot propagation (spec 4.3)");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		// One guard well inside 640px of the player, one well outside.
		Put(g, 6, 14, 'a');
		Put(g, GW - 3, 3, 'b');

		var w = new SimWorld(Level.FromText(Text(g)), 7);
		w.Player.X = CellCentre(14);
		w.Player.Y = CellCentre(14);

		// Level.Guards is filled by a row-major scan, so guard order follows grid
		// position, not letter. Look them up by id.
		var ga = ById(w, 'a');
		var gb = ById(w, 'b');
		int near = Fx.Dist(ga.X, ga.Y, w.Player.X, w.Player.Y);
		int far = Fx.Dist(gb.X, gb.Y, w.Player.X, w.Player.Y);
		H.Check("fixture: guard a is inside 640px", near < Tune.GunRange, $"{near / 256}px");
		H.Check("fixture: guard b is outside 640px", far > Tune.GunRange, $"{far / 256}px");

		w.Step(new InputFrame(0, 0, 0, InputFrame.FFire));

		H.Eq("alarm goes to 2", w.Alarm.Level, 2);
		// Guard_AI.md §5.1: a heard shot is Combat, not the old awareness 92.
		H.Check("guard within 640px enters Combat",
			ga.State == GuardState.Combat, ga.State.ToString());
		H.Check("guard beyond 640px is unaffected",
			gb.State == GuardState.Relaxed,
			gb.State.ToString());
		H.Check("hearing a shot is full awareness, with the shot as his LKP",
			ga.Awareness >= Tune.AwEngage && ga.HasLkp,
			$"got {ga.Awareness / 10.0:F1}");
	}

	private static void BodyDiscovery()
	{
		H.Group("body discovery (spec 4.3)");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		Put(g, 10, 14, 'a');    // will be the corpse
		Put(g, 14, 14, 'b');    // finder, within 300px and facing it

		var w = new SimWorld(Level.FromText(Text(g)), 11);
		var corpse = ById(w, 'a');
		var finder = ById(w, 'b');

		corpse.State = GuardState.Dead;
		corpse.Found = false;
		finder.State = GuardState.Relaxed; finder.Task = GuardTask.Post;
		finder.PathX = null; finder.PathY = null;
		finder.Facing = Brad.Half;              // due west, toward the corpse

		int d = Fx.Dist(corpse.X, corpse.Y, finder.X, finder.Y);
		H.Check("fixture: corpse within 300px", d < Tune.BodyRange, $"{d / 256}px");

		// Park the player far away so it contributes nothing.
		w.Player.X = CellCentre(GW - 3);
		w.Player.Y = CellCentre(GH - 3);

		for (int i = 0; i < 5; i++) w.Step(Idle);

		// Guard_AI.md §7: a body is a REPORT. Nothing tells the finder where the
		// player is, so he radios it in, and the floor learns nothing until the
		// call is through -- which is the window to stop it.
		H.Check("body is marked found", corpse.Found);
		H.Check("the finder radios it in",
			finder.State == GuardState.Combat && finder.Task == GuardTask.Radio
			&& finder.Radio == RadioPurpose.Report,
			$"{finder.State}/{finder.Task}/{finder.Radio}");
		H.Check("and nothing is compromised while he is still on the radio",
			!w.Net.Compromised && w.Alarm.Level < AlarmState.Compromised, $"alarm {w.Alarm.Level}");

		for (int i = 0; i < Tune.RadioTicks; i++) w.Step(Idle);
		H.Check("the completed report compromises the level", w.Net.Compromised);
		H.Eq("and the alarm goes to 3", w.Alarm.Level, AlarmState.Compromised);
		H.Check("and the finder starts hunting", finder.State == GuardState.Hunting, finder.State.ToString());
	}

	// ----------------------------------------------------------- burn ladder

	private static void BurnLadder()
	{
		H.Group("burn ladder (spec 4.3)");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		var w = new SimWorld(Level.FromText(Text(g)), 3);

		H.Eq("starts holding exactly one record", w.Records.Held.Count, 1);
		H.Eq("the briefing note is tier 1", w.Records.Held[0].Tier, 1);

		var hold = new InputFrame(0, 0, 0, InputFrame.FDilate);

		int dilatingTicks = 0, transitions = 0, joltTicks = 0;
		var seen = new List<RecordState>();
		var prev = w.Records.Held[0].State;

		for (int i = 0; i < 60 * 20; i++)
		{
			w.Step(hold);
			if (w.Clocks.ActivePriority == TimeAuthority.PriDilation) dilatingTicks++;
			if (w.Clocks.JoltActive) joltTicks++;

			var now = w.Records.Held[0].State;
			if (now != prev) { transitions++; seen.Add(now); prev = now; }
		}

		H.Eq("exactly two stage transitions", transitions, 2);
		H.Check("transitions are Intact -> Degraded -> Gone",
			seen.Count == 2 && seen[0] == RecordState.Degraded && seen[1] == RecordState.Gone,
			string.Join(" -> ", seen));

		// 8.0 s of dilation per record, split into two 4.0 s stages.
		double seconds = dilatingTicks / 60.0;
		H.Check("one record yields ~8.0s of dilation",
			Math.Abs(seconds - 8.0) < 0.15, $"got {seconds:F3}s");

		H.Check("the jolt interrupts (player cannot hold through)", joltTicks > 0,
			$"{joltTicks} jolt ticks");

		// The spent husk stays in the array so the run's damage stays legible.
		H.Eq("the husk is still held", w.Records.Held.Count, 1);
		H.Check("the husk is Gone", w.Records.Held[0].State == RecordState.Gone);
		H.Eq("no fuel remains", w.Records.PickFuel(), -1);

		w.Records.Score(out int prov, out int unprov, out int destroyed);
		H.Eq("Gone scores nothing as provable", prov, 0);
		H.Eq("Gone scores nothing as unprovable", unprov, 0);
		H.Eq("destroyed counts it", destroyed, 1);
	}

	private static void FuelSelection()
	{
		H.Group("fuel selection (spec 6.2)");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		var w = new SimWorld(Level.FromText(Text(g)), 5);

		w.Records.Held.Add(w.Records.Mint(3));
		w.Records.Held.Add(w.Records.Mint(2));
		H.Eq("newest-first picks the last entry", w.Records.PickFuel(), 2);

		w.Records.Held[2].State = RecordState.Gone;
		H.Eq("skips a Gone entry", w.Records.PickFuel(), 1);

		// Selection is stable: it reselects only when the current fuel is Gone.
		w.Records.FuelIndex = 0;
		w.Step(new InputFrame(0, 0, 0, InputFrame.FDilate));
		H.Eq("does not reselect while the current fuel is live", w.Records.FuelIndex, 0);
	}

	// ------------------------------------------------------------ 8.7 regression

	/// <summary>
	/// Spec §8.7, in the posture model: a guard in Combat who can SEE the player
	/// engages; he never turns away to search. Both halves of the old fix, ported:
	/// reaching the LKP with the player in front of him, and a search the player
	/// walks into.
	/// </summary>
	private static void HuntSearchRegression()
	{
		H.Group("8.7 regression");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		Put(g, 10, 14, 'a');

		var w = new SimWorld(Level.FromText(Text(g)), 13);
		var e = w.Guards[0];
		e.PathX = null; e.PathY = null;
		e.State = GuardState.Combat;
		e.Task = GuardTask.Converge;
		e.SetAwareness(Tune.AwEngage);

		// Player standing directly in front of the guard, at its own LKP: the
		// exact configuration that used to make it turn away and lose them.
		w.Player.X = e.X + 60 * Fx.One;
		w.Player.Y = e.Y;
		e.Facing = 0;
		e.SetLkp(w.Player.X, w.Player.Y);

		// The player is held alive and in place: if the guard's own bullet kills
		// them, the guard then loses a corpse and searches entirely legitimately,
		// which is not the bug this test is about.
		bool everSearched = false;
		for (int i = 0; i < 120; i++)
		{
			int kx = w.Player.X, ky = w.Player.Y;
			w.Step(Idle);
			w.Player.X = kx; w.Player.Y = ky;
			w.Player.Alive = true;
			if (e.Task == GuardTask.SearchLkp) everSearched = true;
		}

		H.Check("Combat with LOS never drops to a search", !everSearched,
			$"ended in {e.State}/{e.Task}");
		H.Check("the guard engages instead of losing the player",
			e.State == GuardState.Combat && e.Task == GuardTask.Engage, $"{e.State}/{e.Task}");

		// And the other half: a search the player walks into becomes a fight.
		var w2 = new SimWorld(Level.FromText(Text(g)), 17);
		var e2 = w2.Guards[0];
		e2.PathX = null; e2.PathY = null;
		e2.State = GuardState.Combat;
		e2.Task = GuardTask.SearchLkp;
		e2.SearchMt = Tune.SearchTicks * Actor.Mt;
		e2.SetAwareness(Tune.AwEngage);
		w2.Player.X = e2.X + 60 * Fx.One;
		w2.Player.Y = e2.Y;
		e2.Facing = 0;

		for (int i = 0; i < 30; i++)
		{
			int kx = w2.Player.X, ky = w2.Player.Y;
			w2.Step(Idle);
			w2.Player.X = kx; w2.Player.Y = ky;
			w2.Player.Alive = true;
		}
		H.Check("a search with LOS abandons its sweep point and engages",
			e2.Task == GuardTask.Engage && !e2.HasSearchPt, $"task {e2.Task}, hasPt {e2.HasSearchPt}");
	}

	// ------------------------------------------------------------ combat

	private static void Combat()
	{
		H.Group("combat (spec 7)");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		Put(g, 20, 14, 'a');

		var w = new SimWorld(Level.FromText(Text(g)), 23);
		var e = w.Guards[0];
		e.PathX = null; e.PathY = null;
		w.Player.X = e.X - 100 * Fx.One;
		w.Player.Y = e.Y;

		int carried = e.Carried.Count;
		H.Check("guard carries records", carried > 0, $"{carried}");

		var fire = new InputFrame(0, 0, 0, InputFrame.FFire);

		// Since milestone 8 a guard survives the first pistol round, and a
		// survivor immediately hunts. A test firing on a fixed heading would
		// miss every shot after the first, so track the target.
		bool killed = false;
		for (int i = 0; i < 600 && !killed; i++)
		{
			int kx = w.Player.X, ky = w.Player.Y;
			int aim = Brad.Atan2(e.Y - w.Player.Y, e.X - w.Player.X);
			w.Step(new InputFrame(0, 0, aim, InputFrame.FFire));
			w.Player.X = kx; w.Player.Y = ky;
			w.Player.Alive = true;
			w.Player.Health = Tune.BaseHealth;
			w.Player.Mag = Tune.Magazine;
			if (e.State == GuardState.Dead) killed = true;
		}

		// Was "one hit kills a guard" before milestone 8. Guards now carry 100
		// health and the pistol does 55, so it takes two rounds. Changed
		// deliberately, per RPG extension plan §6.
		H.Check("a guard dies to pistol fire", killed, e.State.ToString());
		H.Eq("kill is counted", w.Kills, 1);
		H.Eq("shooting destroys the records he carried", e.Carried.Count, 0);
		H.Check("those tiers are logged as destroyed", w.Records.LostToGunfire > 0,
			$"{w.Records.LostToGunfire}");

		// Magazine and dry fire, in a guard-free room: with a guard present the
		// gunfire draws him over and he kills the player before the test ends,
		// which freezes the weapon and looks like a magazine bug.
		var empty = Room();
		Put(empty, 1, 1, '@');
		Put(empty, GW - 2, GH - 2, 'X');
		var w2 = new SimWorld(Level.FromText(Text(empty)), 29);
		int shots = 0;
		for (int i = 0; i < 60 * 5; i++)
		{
			w2.Step(fire);
			foreach (var ev in w2.Log.Events) if (ev.Kind == SimEventKind.PlayerShot) shots++;
		}
		// The Glock carries 17 since milestone 9; read the capacity from the
		// loadout rather than the legacy Tune constant.
		int capacity = new Loadout(WeaponId.Glock).Spec.Magazine;
		H.Eq($"magazine holds exactly {capacity}", shots, capacity);

		bool dryFired = false;
		for (int i = 0; i < 60; i++)
		{
			w2.Step(fire);
			foreach (var ev in w2.Log.Events) if (ev.Kind == SimEventKind.DryFire) dryFired = true;
		}
		H.Check("an empty magazine dry-fires", dryFired);

		// Reload refills.
		for (int i = 0; i < 60 * 2; i++) w2.Step(new InputFrame(0, 0, 0, InputFrame.FReload));
		H.Eq("reload refills the magazine", w2.Player.Mag, capacity);
	}

	private static void Subdue()
	{
		H.Group("subdue (spec 2.4)");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		Put(g, 20, 14, 'a');

		// From behind: guard faces east, player stands to its west.
		var w = new SimWorld(Level.FromText(Text(g)), 31);
		var e = w.Guards[0];
		e.PathX = null; e.PathY = null;
		e.Facing = 0;
		w.Player.X = e.X - 30 * Fx.One;
		w.Player.Y = e.Y;

		int held = w.Records.Held.Count;
		w.Step(new InputFrame(0, 0, 0, InputFrame.FSubdue));

		H.Check("subdued from behind", e.State == GuardState.Down, e.State.ToString());
		H.Check("his records transfer intact", w.Records.Held.Count > held,
			$"{held} -> {w.Records.Held.Count}");
		H.Eq("subdue is counted", w.Subdues, 1);

		// From the front: refused.
		var w2 = new SimWorld(Level.FromText(Text(g)), 37);
		var e2 = w2.Guards[0];
		e2.PathX = null; e2.PathY = null;
		e2.Facing = Brad.Half;                  // facing west, straight at the player
		w2.Player.X = e2.X - 30 * Fx.One;
		w2.Player.Y = e2.Y;

		w2.Step(new InputFrame(0, 0, 0, InputFrame.FSubdue));
		H.Check("cannot subdue from the front", e2.State != GuardState.Down, e2.State.ToString());
	}

	// ------------------------------------------------------------ stress

	private static void Stress()
	{
		H.Group("stress (spec 4.3)");

		string levelText = File.ReadAllText(Path.Combine(LevelsDir(), "substation_4.txt"));
		var w = new SimWorld(Level.FromText(levelText), 0xBADC0FFEE);
		var rng = new DetRng(77);

		Exception? boom = null;
		try
		{
			for (int i = 0; i < 60 * 60; i++)
			{
				var f = new InputFrame(
					rng.NextRange(-1, 1), rng.NextRange(-1, 1),
					rng.NextBrad(),
					(byte)rng.NextInt(32));
				w.Step(f);
			}
		}
		catch (Exception ex) { boom = ex; }

		H.Check("60s of randomised input raises nothing",
			boom == null, boom?.GetType().Name + ": " + boom?.Message);

		if (boom != null) return;

		var s = w.Snapshot();
		bool sane = s.PlayerX > int.MinValue / 2 && s.PlayerX < int.MaxValue / 2
			&& s.PlayerY > int.MinValue / 2 && s.PlayerY < int.MaxValue / 2
			&& s.Exposure >= 0 && s.AlarmLevel >= 0 && s.AlarmLevel <= AlarmState.Compromised;
		H.Check("snapshot values stay in range", sane,
			$"x={s.PlayerX} y={s.PlayerY} exposure={s.Exposure} alarm={s.AlarmLevel}");

		bool inBounds = !Geometry.HitsWall(w.Level.Walls, w.Player.X, w.Player.Y, w.Player.Radius);
		H.Check("player never ends up inside a wall", inBounds);

		bool guardsSane = true;
		for (int i = 0; i < w.Guards.Count; i++)
		{
			var e = w.Guards[i];
			if (e.Prone) continue;
			if (Geometry.HitsWall(w.Level.Walls, e.X, e.Y, e.Radius)) guardsSane = false;
		}
		H.Check("no guard ends up inside a wall", guardsSane);
	}

	private static string LevelsDir()
	{
		var d = new DirectoryInfo(AppContext.BaseDirectory);
		while (d != null && !Directory.Exists(Path.Combine(d.FullName, "levels"))) d = d.Parent;
		return d == null ? "levels" : Path.Combine(d.FullName, "levels");
	}

	// ------------------------------------------------------------ time authority

	private static void TimeAuthorityTests()
	{
		H.Group("TimeAuthority (spec 5.3)");

		var t = new TimeAuthority();
		t.Resolve();
		H.Eq("idle resolves to Normal", t.ActivePriority, TimeAuthority.PriNormal);
		H.Eq("normal world scale", t.WorldScale, Tune.NormalScale);

		t.RequestDilation(true);
		t.Resolve();
		H.Eq("dilation wins over normal", t.ActivePriority, TimeAuthority.PriDilation);
		H.Eq("world slows to 0.18", t.WorldScale, Tune.WorldSlow);
		H.Eq("player clock is 0.62", t.PlayerScale, Tune.PlayerClock);

		t.RequestHitstop(3);
		t.Resolve();
		H.Eq("hitstop outranks dilation", t.ActivePriority, TimeAuthority.PriHitstop);
		H.Eq("hitstop freezes the world", t.WorldScale, 0);
		H.Check("presentation keeps running under hitstop", t.PresentationRuns);

		t.RequestJolt(18);
		t.Resolve();
		H.Eq("jolt outranks hitstop", t.ActivePriority, TimeAuthority.PriJolt);
		H.Eq("jolt forces real time", t.WorldScale, Tune.NormalScale);

		// This is the case the prototype patched with `timeScale <= 0.55`:
		// a kill landing during dilation still gets its hitstop.
		var t2 = new TimeAuthority();
		t2.RequestDilation(true);
		t2.RequestHitstop(Tune.HitstopGuardTicks);
		t2.Resolve();
		H.Eq("a kill during dilation still gets hitstop", t2.WorldScale, 0);
	}

	// ------------------------------------------------------- playability

	/// <summary>
	/// The reference level has to actually be a game: guards must walk their
	/// routes, and the exit must be reachable. Both are the kind of thing that
	/// silently breaks when steering or the exit rect regresses, and neither is
	/// covered by any spec assertion.
	/// </summary>
	private static void Playable()
	{
		H.Group("playability");

		string levelText = File.ReadAllText(Path.Combine(LevelsDir(), "substation_4.txt"));
		var w = new SimWorld(Level.FromText(levelText), 101);

		var startX = new int[w.Guards.Count];
		var startY = new int[w.Guards.Count];
		for (int i = 0; i < w.Guards.Count; i++) { startX[i] = w.Guards[i].X; startY[i] = w.Guards[i].Y; }

		// Player parked in a corner far from every route so nothing is alerted.
		for (int i = 0; i < 60 * 8; i++)
		{
			w.Step(Idle);
			w.Player.X = w.Level.SpawnX;
			w.Player.Y = w.Level.SpawnY;
		}

		// Routed guards walk; guards with no route line are stationary sentries
		// BY DESIGN, so asserting that all of them move would fail on any level
		// that uses the mix -- which every level does now that there are twenty
		// of them and only some patrol.
		int routed = 0, movedRouted = 0, stillSentries = 0, sentries = 0;
		for (int i = 0; i < w.Guards.Count; i++)
		{
			bool walks = w.Guards[i].PathX != null && w.Guards[i].PathX!.Length > 0;
			bool moved = Fx.Dist(w.Guards[i].X, w.Guards[i].Y, startX[i], startY[i])
				> 20 * Fx.One;
			if (walks) { routed++; if (moved) movedRouted++; }
			else { sentries++; if (!moved) stillSentries++; }
		}
		H.Check("the level has both patrols and sentries", routed > 0 && sentries > 0,
			$"{routed} routed, {sentries} sentries");
		H.Eq("every routed guard walks its patrol", movedRouted, routed);
		H.Eq("and every sentry holds its post", stillSentries, sentries);

		bool calm = true;
		for (int i = 0; i < w.Guards.Count; i++)
		{
			var st = w.Guards[i].State;
			if (st != GuardState.Relaxed) calm = false;
		}
		H.Check("guards do not self-alert", calm);

		// The exit must be reachable from spawn through open cells. This is a
		// flood fill rather than a walk: greedy steering jams on the first wall
		// it meets, which would test the walker, not the level.
		var lvl = Level.FromText(levelText);
		H.Check("the exit is reachable from spawn", Reachable(lvl), "flood fill found no route");

		// sim/ exposes its own reachability for the editor to validate against.
		// Cross-check it against this test's independent implementation rather
		// than trusting either alone.
		H.Check("Level.ExitReachable agrees with the test's own fill",
			lvl.ExitReachable() == Reachable(lvl),
			$"sim says {lvl.ExitReachable()}, test says {Reachable(lvl)}");

		// A sealed exit must be reported unreachable.
		var sealedGrid = Room();
		Put(sealedGrid, 2, 2, '@');
		for (int r = 0; r < GH; r++) Put(sealedGrid, 24, r, '#');   // wall across the map
		Put(sealedGrid, GW - 3, GH - 3, 'X');
		var sealedLvl = Level.FromText(Text(sealedGrid));
		H.Check("a walled-off exit is unreachable", !sealedLvl.ExitReachable());
		H.Check("the test's own fill agrees", !Reachable(sealedLvl));

		// Open one cell in that wall and it becomes reachable again.
		Put(sealedGrid, 24, 14, '.');
		var openedLvl = Level.FromText(Text(sealedGrid));
		H.Check("one gap makes it reachable again", openedLvl.ExitReachable());

		// Spawn buried in a wall is not a valid start.
		var buried = Room();
		Put(buried, 5, 5, '@');
		Put(buried, GW - 3, GH - 3, 'X');
		var buriedLvl = Level.FromText(Text(buried));
		H.Check("fixture: an open spawn is reachable", buriedLvl.ExitReachable());

		// And the player can in fact cover ground: 3 s of walking moves them
		// meaningfully, which catches a movement or collision regression.
		//
		// Measured in an OPEN room, not on the reference level. Walking one
		// direction from a hand-authored spawn measures whatever happens to be
		// south of it — a spawn room with a door in the corner fails this while
		// the movement path is perfectly healthy. The room is the fixture; the
		// reference level is not.
		var open = Room();
		Put(open, 6, 6, '@');
		Put(open, GW - 3, GH - 3, 'X');
		var w3 = new SimWorld(Level.FromText(Text(open)), 103);
		int sx = w3.Player.X, sy = w3.Player.Y;
		for (int i = 0; i < 180; i++) w3.Step(new InputFrame(0, 1, 0, 0));
		int covered = Fx.Dist(w3.Player.X, w3.Player.Y, sx, sy);
		H.Check("the player covers ground when walking", covered > 60 * Fx.One,
			$"moved {covered / 256.0:F1}px in 3s");
	}

	/// <summary>Four-way flood fill from spawn over every non-wall cell.</summary>
	private static bool Reachable(Level lvl)
	{
		int sc = lvl.SpawnX / Level.CellFx, sr = lvl.SpawnY / Level.CellFx;
		var seen = new bool[Level.GW * Level.GH];
		var queue = new Queue<int>();
		queue.Enqueue(sr * Level.GW + sc);
		seen[sr * Level.GW + sc] = true;

		int ec0 = lvl.Exit.X / Level.CellFx, er0 = lvl.Exit.Y / Level.CellFx;
		int ec1 = (lvl.Exit.X + lvl.Exit.W) / Level.CellFx, er1 = (lvl.Exit.Y + lvl.Exit.H) / Level.CellFx;

		int[] dc = { 1, -1, 0, 0 }, dr = { 0, 0, 1, -1 };
		while (queue.Count > 0)
		{
			int cur = queue.Dequeue();
			int c = cur % Level.GW, r = cur / Level.GW;
			if (c >= ec0 && c < ec1 && r >= er0 && r < er1) return true;

			for (int k = 0; k < 4; k++)
			{
				int nc = c + dc[k], nr = r + dr[k];
				if (nc < 0 || nr < 0 || nc >= Level.GW || nr >= Level.GH) continue;
				int ni = nr * Level.GW + nc;
				if (seen[ni] || lvl.Grid[ni] == '#') continue;
				seen[ni] = true;
				queue.Enqueue(ni);
			}
		}
		return false;
	}

	// ------------------------------------------------------------------ run

	public static void Run()
	{
		Playable();
		TimeAuthorityTests();
		BurnLadder();
		FuelSelection();
		Combat();
		Subdue();
		GunshotPropagation();
		BodyDiscovery();
		HuntSearchRegression();
		DetectionCurve();
		Stress();
	}
}
