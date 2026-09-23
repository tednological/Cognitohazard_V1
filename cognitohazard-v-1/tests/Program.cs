using System;
using System.Collections.Generic;
using System.IO;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

public static class Program
{
	private static string LevelsDir()
	{
		// Walk up from the build output to the project root.
		var d = new DirectoryInfo(AppContext.BaseDirectory);
		while (d != null && !Directory.Exists(Path.Combine(d.FullName, "levels"))) d = d.Parent;
		return d == null ? "levels" : Path.Combine(d.FullName, "levels");
	}

	internal static string ReadLevel(string name)
		=> File.ReadAllText(Path.Combine(LevelsDir(), name));

	public static int Main(string[] args)
	{
		// Verify a recorded replay without launching the game. This is the
		// debugging entry point: it replays the stream against the CURRENT sim
		// and names the first tick that no longer matches the recording.
		int vi = Array.IndexOf(args, "--verify");
		if (vi >= 0 && vi + 1 < args.Length) return VerifyFile(args[vi + 1]);

		bool record = Array.IndexOf(args, "--record") >= 0;

		Console.WriteLine("Cognitohazard sim harness - milestone 0/1");
		Console.WriteLine();

		SimLint.Run();
		FixedPoint();
		Trig();
		Rng();
		LevelPipeline();
		Systems.Run();
		Navigation.Run();
		GuardAI.Run();
		Panels.Run();
		Loadouts.Run();
		Health.Run();
		Attachments.Run();
		Aiming.Run();
		Handling.Run();
		Inventory.Run();
		Economy.Run();
		Loot.Run();
		Fuzz.Run();
		Robustness.Run();
		Specialists.Run();
		Exhaustive.Run();
		Determinism(record);

		Console.WriteLine();
		return H.Report();
	}

	private static int VerifyFile(string path)
	{
		if (!File.Exists(path))
		{
			Console.WriteLine($"replay not found: {path}");
			return 2;
		}

		var replay = Replay.FromText(File.ReadAllText(path));
		Console.WriteLine($"replay: {path}");
		Console.WriteLine($"  seed        {replay.Seed}");
		Console.WriteLine($"  ticks       {replay.Inputs.Count}  ({replay.Inputs.Count / 60.0:F1}s)");
		Console.WriteLine($"  checkpoints {replay.HashTicks.Count}");

		if (replay.Inputs.Count == 0)
		{
			Console.WriteLine("  EMPTY - nothing to verify");
			return 2;
		}
		if (replay.LevelText.Trim().Length == 0)
		{
			Console.WriteLine("  NO LEVEL - the replay carries no geometry");
			return 2;
		}

		var d = replay.Verify();

		if (d.Compared == 0)
		{
			Console.WriteLine("  INCONCLUSIVE - no checkpoints could be compared");
			Console.WriteLine("  Nothing was verified. This is not a pass.");
			return 2;
		}

		if (!d.Found)
		{
			Console.WriteLine($"  OK - {d.Compared} checkpoints reproduce exactly");
			return 0;
		}

		Console.WriteLine($"  DIVERGED at tick {d.Tick} ({d.Tick / 60.0:F2}s), after {d.Compared - 1} clean checkpoints");
		Console.WriteLine($"    recorded 0x{d.Expected:X16}");
		Console.WriteLine($"    current  0x{d.Actual:X16}");
		Console.WriteLine("  The sim's behaviour changed after this tick was recorded.");
		return 1;
	}

	// ------------------------------------------------------------ fixed point

	private static void FixedPoint()
	{
		H.Group("fixed-point");

		H.Eq("One is 256", Fx.One, 256);
		H.Eq("FromInt(11)", Fx.FromInt(11), 2816);
		H.Eq("Mul(1.0, 1.0)", Fx.Mul(Fx.One, Fx.One), Fx.One);
		H.Eq("Div(1.0, 2.0)", Fx.Div(Fx.One, 2 * Fx.One), Fx.Half);

		// Exact integer sqrt over the whole range we use.
		bool sqrtOk = true;
		for (long v = 0; v < 100000; v += 7)
		{
			long r = Fx.SqrtL(v);
			if (r * r > v || (r + 1) * (r + 1) <= v) { sqrtOk = false; break; }
		}
		H.Check("SqrtL is exact floor over [0,100k)", sqrtOk);

		// 3-4-5 triangle in fixed point.
		H.Eq("Hypot(3,4) == 5", Fx.Hypot(Fx.FromInt(3), Fx.FromInt(4)), Fx.FromInt(5));

		// 196 px/s at full scale for 60 ticks should cover ~196 px.
		int per = Fx.PerTick(Tune.SpeedWalk, Tune.NormalScale);
		long covered = (long)per * 60;
		H.Check("walk covers ~196px in 60 ticks",
			Math.Abs(covered - Fx.FromInt(196)) <= 60,
			$"covered {covered / 256.0:F3} px");

		// Dilated player clock is 0.62x exactly.
		int perDil = Fx.PerTick(Tune.SpeedWalk, Tune.PlayerClock);
		H.Near("player clock ratio is 0.62", (double)perDil / per, 0.62, 0.002);

		// Relative advantage while dilating (spec 5.2): 0.62 / 0.18 ~= 3.4x.
		H.Near("dilation advantage ~3.44x",
			(double)Tune.PlayerClock / Tune.WorldSlow, 3.4444, 0.01);
	}

	// ------------------------------------------------------------------ trig

	private static void Trig()
	{
		H.Group("trig (integer CORDIC)");

		double worstSin = 0, worstCos = 0;
		for (int b = 0; b < Brad.Full; b += 37)
		{
			Brad.SinCos(b, out int s, out int c);
			double ang = b / 65536.0 * 2 * Math.PI;
			worstSin = Math.Max(worstSin, Math.Abs(s / 65536.0 - Math.Sin(ang)));
			worstCos = Math.Max(worstCos, Math.Abs(c / 65536.0 - Math.Cos(ang)));
		}
		H.Check("sin within 5e-5 of libm", worstSin < 5e-5, $"worst {worstSin:E3}");
		H.Check("cos within 5e-5 of libm", worstCos < 5e-5, $"worst {worstCos:E3}");

		// The four cardinals must be EXACT. An inexact sin(0) gives an actor
		// aiming due east a perpendicular drift of a few 1/256 px every tick,
		// which compounds over a run and silently breaks straight-line motion.
		H.Eq("sin(0) is exactly 0", Brad.Sin(0), 0);
		H.Eq("cos(0) is exactly 1", Brad.Cos(0), Brad.Unit);
		H.Eq("sin(quarter) is exactly 1", Brad.Sin(Brad.Quarter), Brad.Unit);
		H.Eq("cos(quarter) is exactly 0", Brad.Cos(Brad.Quarter), 0);
		H.Eq("sin(half) is exactly 0", Brad.Sin(Brad.Half), 0);
		H.Eq("cos(half) is exactly -1", Brad.Cos(Brad.Half), -Brad.Unit);
		H.Eq("sin(3 quarters) is exactly -1", Brad.Sin(Brad.Half + Brad.Quarter), -Brad.Unit);
		H.Eq("cos(3 quarters) is exactly 0", Brad.Cos(Brad.Half + Brad.Quarter), 0);

		// Atan2 round-trips a heading back to itself.
		int worstAtan = 0;
		for (int b = 0; b < Brad.Full; b += 149)
		{
			Brad.SinCos(b, out int s, out int c);
			int back = Brad.Atan2(s, c);
			int err = Math.Abs(Brad.Norm(back - b));
			if (err > worstAtan) worstAtan = err;
		}
		H.Check("atan2 round-trips within 4 BRAD", worstAtan <= 4, $"worst {worstAtan} BRAD");

		H.Eq("Norm wraps past half", Brad.Norm(Brad.Full - 1), -1);
		H.Eq("Norm of zero", Brad.Norm(0), 0);

		// Determinism: the table is identical on every construction path.
		H.Eq("sin table stable", Brad.Sin(12345), Brad.Sin(12345));
	}

	// ------------------------------------------------------------------- rng

	private static void Rng()
	{
		H.Group("DetRng");

		var a = new DetRng(12345);
		var b = new DetRng(12345);
		bool same = true;
		for (int i = 0; i < 1000; i++) if (a.NextU64() != b.NextU64()) { same = false; break; }
		H.Check("same seed yields same stream", same);

		var c = new DetRng(12346);
		var d = new DetRng(12345);
		H.Check("different seed diverges", c.NextU64() != d.NextU64());

		var e = new DetRng(7);
		bool inRange = true;
		for (int i = 0; i < 5000; i++) { int v = e.NextRange(-5, 5); if (v < -5 || v > 5) inRange = false; }
		H.Check("NextRange stays in bounds", inRange);

		var f = new DetRng(9);
		for (int i = 0; i < 10; i++) f.NextU64();
		H.Eq("draw count is tracked", (long)f.Draws, 10);

		// Draw count feeds the hash, so a skipped draw is caught even when the
		// values coincide.
		var g1 = new DetRng(3); var g2 = new DetRng(3);
		g1.NextU64();
		var h1 = Hash64.New(); g1.HashInto(ref h1);
		var h2 = Hash64.New(); g2.HashInto(ref h2);
		H.Check("hash distinguishes draw count", h1.Value != h2.Value);
	}

	// -------------------------------------------------------- level pipeline

	private static void LevelPipeline()
	{
		H.Group("level");

		string text = ReadLevel("substation_4.txt");
		var L = Level.FromText(text);

		H.Eq("name parsed", L.Name == "Substation 4" ? 1 : 0, 1);

		// These used to be magic numbers -- "222 wall cells", "18 rects" -- copied
		// out of the reference level at the time they were written. The level is
		// authored in the in-game editor and gets re-saved, so every edit broke
		// four assertions that were not actually about the parser. They now
		// check the parser AGAINST the file it was handed, which is both the
		// thing worth testing and immune to level design.
		int wallCells = 0;
		for (int i = 0; i < L.Grid.Length; i++) if (L.Grid[i] == '#') wallCells++;
		H.Eq("every wall glyph in the file survives parsing", wallCells, CountGlyph(text, '#'));
		H.Check("the level has walls to merge", wallCells > 0);

		// The merge is the thing with a bug in it, not the count. A rect that
		// covers a floor cell opens a hole nothing can shoot through; one that
		// misses a wall cell lets rounds pass. Assert an exact cover instead.
		H.Check("merged rects cover exactly the wall cells", MergeIsExact(L),
			$"{L.Walls.Length} rects over {wallCells} cells");
		H.Check("and merging actually merges", L.Walls.Length < wallCells,
			$"{L.Walls.Length} rects from {wallCells} cells");

		H.Check("round-trip is lossless", L.ToText() == text);

		// Round-trip is stable under a second pass too.
		var L2 = Level.FromText(L.ToText());
		H.Check("round-trip is idempotent", L2.ToText() == text);

		// Marker sanity (spec 4.3): every marker glyph still occupies its own
		// cell, i.e. nothing was clobbered into a wall by the grid pass.
		int markers = 0, guardGlyphs = 0, cacheGlyphs = 0, chestGlyphs = 0;
		int objectiveGlyphs = 0;
		for (int i = 0; i < L.Grid.Length; i++)
		{
			char ch = L.Grid[i];
			if (ch >= 'a' && ch <= 'z') { markers++; guardGlyphs++; }
			else if (ch == '$') { markers++; cacheGlyphs++; }
			else if (ch == 'C') { markers++; chestGlyphs++; }
			else if (ch == '!') { markers++; chestGlyphs++; objectiveGlyphs++; }
			else if (ch == '@' || ch == 'X') markers++;
		}
		H.Eq("every marker in the file survives parsing", markers, CountMarkers(text));
		H.Check("the level has markers at all", markers > 0);

		H.Eq("one guard per guard glyph", L.Guards.Count, guardGlyphs);
		H.Eq("one cache per cache glyph", L.Caches.Count, cacheGlyphs);
		// An objective site is a chest that holds the objective, so both glyphs
		// build a chest and only '!' counts as an objective.
		H.Eq("one chest per chest or objective glyph", L.Chests.Count, chestGlyphs);
		H.Eq("one objective per objective glyph", L.Objectives, objectiveGlyphs);
		// A guard with a route line patrols; one without is a sentry. Both are
		// legal, so what matters is that the two agree with the file.
		bool routesAgree = L.Guards.TrueForAll(g =>
			(L.Routes.ContainsKey(g.Id) && L.Routes[g.Id].Count > 0)
				== (g.PathX != null && g.PathX.Length > 0));
		H.Check("every guard patrols iff the file gave it a route", routesAgree);

		H.Check("spawn is inside the field",
			L.SpawnX > 0 && L.SpawnY > 0
			&& L.SpawnX < Level.GW * Level.CellFx && L.SpawnY < Level.GH * Level.CellFx);
		H.Check("exit has area", L.Exit.W > 0 && L.Exit.H > 0);
		H.Eq("exit is 3x3 cells", L.Exit.W / Level.CellFx, 3);

		// Parser totality (spec 2.1): never throws, always playable.
		H.NoThrow("empty input", () => { var x = Level.FromText(""); AssertPlayable(x); });
		H.NoThrow("garbage input", () =>
		{
			var x = Level.FromText("!!! nonsense\n> \n> z\nname:\ngrid:\n@@@@");
			AssertPlayable(x);
		});
		H.NoThrow("truncated grid", () => { var x = Level.FromText("grid:\n###"); AssertPlayable(x); });
		H.NoThrow("route with bad coords",
			() => { var x = Level.FromText("grid:\n> a 1,2 zz 4,nope 7,8"); AssertPlayable(x); });
		H.NoThrow("grid longer than GH", () =>
		{
			var sb = new System.Text.StringBuilder("grid:\n");
			for (int i = 0; i < 80; i++) sb.Append("################################################\n");
			AssertPlayable(Level.FromText(sb.ToString()));
		});

		// Defaults when markers are missing.
		var bare = Level.FromText("name: bare\ngrid:\n");
		H.Eq("missing @ spawns at cell (1,1)", bare.SpawnX, Level.CellFx * 3 / 2);
		H.Eq("missing X puts exit at GW-3", bare.Exit.X, (Level.GW - 3) * Level.CellFx);

		// ---- variable level size ----
		//
		// GW/GH are the DEFAULT for a blank level, not a fixed world size. A
		// file's own grid text decides how big its floor is.
		H.Eq("an empty file gets the default width", Level.FromText("").W, Level.GW);
		H.Eq("and the default height", Level.FromText("").H, Level.GH);
		H.Eq("the reference level is 48 wide", L.W, 48);
		H.Eq("and 28 tall", L.H, 28);

		string wide = MakeRoom(96, 28);
		var wideL = Level.FromText(wide);
		H.Eq("a wide level parses wide", wideL.W, 96);
		H.Eq("without growing taller", wideL.H, 28);
		H.Check("and round-trips losslessly", wideL.ToText() == wide);

		string tall = MakeRoom(48, 80);
		var tallL = Level.FromText(tall);
		H.Eq("a tall level parses tall", tallL.H, 80);
		H.Check("and round-trips losslessly", tallL.ToText() == tall);

		string big = MakeRoom(120, 90);
		var bigL = Level.FromText(big);
		H.Eq("a large level parses at size", bigL.W * bigL.H, 120 * 90);
		H.Check("and round-trips losslessly", bigL.ToText() == big);
		H.Check("its walls still merge exactly", MergeIsExact(bigL));
		H.Check("and its exit is reachable", bigL.ExitReachable());

		// The default exit and spawn follow the level, not the constants.
		var bareBig = Level.FromText("grid:\n" + StripMarkers(MakeRoom(96, 60)));
		H.Eq("a markerless level puts its exit at W-3",
			bareBig.Exit.X, (bareBig.W - 3) * Level.CellFx);
		H.Check("which is inside the level",
			bareBig.Exit.X + bareBig.Exit.W <= bareBig.WidthFx);
		H.Check("and its exit is not above the top edge", bareBig.Exit.Y >= 0);

		// Totality survives: the parser clamps rather than trusting or throwing.
		H.NoThrow("a one-character grid", () =>
		{
			var x = Level.FromText("grid:\n#");
			AssertPlayable(x);
			if (x.W < Level.MinDim || x.H < Level.MinDim)
				throw new Exception($"clamped below MinDim: {x.W}x{x.H}");
		});
		H.NoThrow("an absurdly wide grid", () =>
		{
			var x = Level.FromText("grid:\n" + new string('#', 5000));
			AssertPlayable(x);
			if (x.W > Level.MaxDim) throw new Exception($"W past MaxDim: {x.W}");
		});
		H.NoThrow("an absurdly tall grid", () =>
		{
			var sb = new System.Text.StringBuilder("grid:\n");
			for (int i = 0; i < 5000; i++) sb.Append("####\n");
			var x = Level.FromText(sb.ToString());
			AssertPlayable(x);
			if ((long)x.W * x.H > Level.MaxCells)
				throw new Exception($"area past MaxCells: {x.W}x{x.H}");
		});

		// Trailing whitespace describes the same floor, so it must not widen it.
		var padded = Level.FromText("grid:\n" + new string('#', 48) + "   \n"
			+ new string('#', 48) + "\n");
		H.Eq("trailing spaces do not widen a level", padded.W, 48);

		// A guard with no route line is a stationary sentry, by design.
		var sentryLevel = Level.FromText("grid:\n" + GridWithGuardNoRoute());
		H.Check("routeless guard is a sentry",
			sentryLevel.Guards.Count == 1 && sentryLevel.Guards[0].PathX == null);
	}

	/// <summary>Count a glyph in the grid SECTION of a level file, so the header
	/// comment's own '#' characters do not inflate the wall count.</summary>
	private static int CountGlyph(string text, char glyph)
	{
		int n = 0;
		bool inGrid = false;
		foreach (string raw in text.Replace("\r", "").Split('\n'))
		{
			if (raw.TrimStart().StartsWith("grid:")) { inGrid = true; continue; }
			if (!inGrid) continue;
			if (raw.TrimStart().StartsWith(">")) break;
			foreach (char c in raw) if (c == glyph) n++;
		}
		return n;
	}

	private static int CountMarkers(string text)
	{
		int n = 0;
		bool inGrid = false;
		foreach (string raw in text.Replace("\r", "").Split('\n'))
		{
			if (raw.TrimStart().StartsWith("grid:")) { inGrid = true; continue; }
			if (!inGrid) continue;
			if (raw.TrimStart().StartsWith(">")) break;
			foreach (char c in raw)
				if (c == '@' || c == 'X' || c == '$' || c == 'C' || c == '!'
					|| (c >= 'a' && c <= 'z')) n++;
		}
		return n;
	}

	/// <summary>
	/// Every wall cell is covered by exactly one merged rect, and no rect covers
	/// anything that is not a wall. This is what the merge actually promises;
	/// the rect COUNT is an implementation detail that changes with the level.
	/// </summary>
	private static bool MergeIsExact(Level L)
	{
		var covered = new int[L.W * L.H];
		foreach (var rect in L.Walls)
		{
			int c0 = rect.X / Level.CellFx, r0 = rect.Y / Level.CellFx;
			int c1 = (rect.X + rect.W) / Level.CellFx, r1 = (rect.Y + rect.H) / Level.CellFx;
			for (int r = r0; r < r1; r++)
				for (int c = c0; c < c1; c++)
				{
					if (!L.InBounds(c, r)) return false;
					covered[r * L.W + c]++;
				}
		}
		for (int i = 0; i < covered.Length; i++)
		{
			bool wall = L.Grid[i] == '#';
			if (wall && covered[i] != 1) return false;     // missed, or double-covered
			if (!wall && covered[i] != 0) return false;    // a rect over open floor
		}
		return true;
	}

	/// <summary>A bordered room of the given size, spawn top-left, exit inset
	/// from the bottom-right, as a level file.</summary>
	private static string MakeRoom(int w, int h)
	{
		// The same header ToText() writes, so a round-trip compares like with like.
		var sb = new System.Text.StringBuilder("name: fixture\n");
		sb.Append(Level.HeaderComment).Append('\n');
		sb.Append("grid:\n");
		for (int r = 0; r < h; r++)
		{
			for (int c = 0; c < w; c++)
			{
				bool edge = r == 0 || r == h - 1 || c == 0 || c == w - 1;
				sb.Append(edge ? '#' : (r == 2 && c == 2) ? '@'
					: (r == h - 3 && c == w - 3) ? 'X' : '.');
			}
			sb.Append('\n');
		}
		return sb.ToString();
	}

	private static string StripMarkers(string level)
	{
		int at = level.IndexOf("grid:\n", System.StringComparison.Ordinal);
		string grid = at < 0 ? level : level.Substring(at + 6);
		return grid.Replace('@', '.').Replace('X', '.');
	}

	private static void AssertPlayable(Level x)
	{
		if (x.Walls == null) throw new Exception("no walls array");
		if (x.Exit.W <= 0 || x.Exit.H <= 0) throw new Exception("degenerate exit");
		if (x.SpawnX < 0 || x.SpawnY < 0) throw new Exception("negative spawn");
	}

	private static string GridWithGuardNoRoute()
	{
		var rows = new List<string>();
		for (int r = 0; r < Level.GH; r++)
		{
			var chars = new char[Level.GW];
			for (int c = 0; c < Level.GW; c++)
				chars[c] = (r == 0 || r == Level.GH - 1 || c == 0 || c == Level.GW - 1) ? '#' : '.';
			rows.Add(new string(chars));
		}
		var mid = rows[10].ToCharArray();
		mid[10] = 'a';
		rows[10] = new string(mid);
		return string.Join("\n", rows);
	}

	// ----------------------------------------------------------- determinism

	private static SimWorld Build(ulong seed)
		=> new SimWorld(Level.FromText(ReadLevel("substation_4.txt")), seed);

	/// <summary>
	/// A synthetic but non-trivial input stream: the player wanders with the aim
	/// sweeping, so movement, wall slide and the stance paths all get exercised.
	/// Generated from a seeded DetRng so it is reproducible without needing a
	/// recorded file yet.
	/// </summary>
	private static List<InputFrame> ScriptedInputs(int count, ulong seed)
	{
		var rng = new DetRng(seed);
		var frames = new List<InputFrame>(count);
		int mx = 1, my = 0;
		for (int i = 0; i < count; i++)
		{
			if (i % 23 == 0) { mx = rng.NextRange(-1, 1); my = rng.NextRange(-1, 1); }
			int aim = (i * 421) & Brad.Mask;
			byte flags = (byte)((i % 97 < 20) ? InputFrame.FSneak : 0);
			frames.Add(new InputFrame(mx, my, aim, flags));
		}
		return frames;
	}

	/// <summary>
	/// The recorded-replay debugging loop: a replay carries periodic state
	/// hashes, and replaying it reports the FIRST tick that no longer matches.
	/// Both directions are checked — a clean replay must verify, and a
	/// deliberately corrupted one must be caught at the right tick.
	/// </summary>
	private static void ReplayVerification(List<InputFrame> inputs)
	{
		H.Group("replay verification");

		string levelText = ReadLevel("substation_4.txt");
		const ulong Seed = 0x12EF1A70BADUL;

		// Record a run the way the bridge does.
		var world = new SimWorld(Level.FromText(levelText), Seed);
		var rec = new Replay { Seed = Seed, LevelText = levelText };
		foreach (var f in inputs)
		{
			world.Step(f);
			rec.Inputs.Add(f);
			int tick = (int)world.Tick;
			if (tick % Replay.HashEvery == 0) rec.AddHash(tick, world.StateHash());
		}

		H.Check("checkpoints were recorded", rec.HashTicks.Count > 0,
			$"{rec.HashTicks.Count} checkpoints");
		H.Eq("one checkpoint per 60 ticks", rec.HashTicks.Count, inputs.Count / Replay.HashEvery);

		// A clean replay verifies.
		var clean = rec.Verify();
		H.Check("an untouched replay verifies", !clean.Found,
			clean.Found ? $"diverged at tick {clean.Tick}" : "");

		// Through text, too.
		var reloaded = Replay.FromText(rec.ToText());
		H.Eq("reloaded frame count", reloaded.Inputs.Count, rec.Inputs.Count);
		H.Eq("reloaded checkpoint count", reloaded.HashTicks.Count, rec.HashTicks.Count);
		var afterText = reloaded.Verify();
		H.Check("a replay verifies after a text round-trip", !afterText.Found,
			afterText.Found ? $"diverged at tick {afterText.Tick}" : "");

		// And a corrupted checkpoint is caught, at the tick it was corrupted.
		var broken = Replay.FromText(rec.ToText());
		int targetIdx = broken.HashTicks.Count / 2;
		int targetTick = broken.HashTicks[targetIdx];
		broken.HashValues[targetIdx] = 0xDEADBEEFDEADBEEFUL;
		var caught = broken.Verify();
		H.Check("a corrupted checkpoint is caught", caught.Found);
		H.Eq("caught at the corrupted tick", caught.Tick, targetTick);

		// A changed input stream diverges too — that is the "did my edit change
		// behaviour" case, which is the whole reason for the format.
		var altered = Replay.FromText(rec.ToText());
		altered.Inputs[10] = new InputFrame(-1, -1, 1234, 0);
		var drifted = altered.Verify();
		H.Check("an altered input stream diverges", drifted.Found,
			"identical hashes despite different input");

		// A replay too short to contain a checkpoint must report INCONCLUSIVE,
		// not OK. "Nothing contradicted me" is not the same as "verified", and
		// conflating them turns the regression detector into false confidence.
		var tiny = new Replay { Seed = 1, LevelText = ReadLevel("substation_4.txt") };
		for (int i = 0; i < 30; i++) tiny.Inputs.Add(new InputFrame(0, 0, 0, 0));
		var tinyResult = tiny.Verify();
		H.Eq("a checkpoint-less replay compares nothing", tinyResult.Compared, 0);
		H.Check("and does not claim to have found a divergence", !tinyResult.Found);

		// With a closing checkpoint it becomes verifiable.
		var world2 = new SimWorld(Level.FromText(tiny.LevelText), 1);
		var closed = new Replay { Seed = 1, LevelText = tiny.LevelText };
		for (int i = 0; i < 30; i++)
		{
			var f = new InputFrame(0, 0, 0, 0);
			world2.Step(f);
			closed.Inputs.Add(f);
		}
		closed.AddHash(30, world2.StateHash());
		var closedResult = closed.Verify();
		H.Eq("a closing checkpoint makes a short replay verifiable", closedResult.Compared, 1);
		H.Check("and it verifies", !closedResult.Found);

		// Coverage is reported honestly on a full-length replay too.
		H.Eq("a 900-tick replay compares every checkpoint",
			rec.Verify().Compared, rec.HashTicks.Count);

		// A malformed file must not throw.
		H.NoThrow("garbage replay text", () => Replay.FromText("nonsense\nhash: x y\nframes:\nzz"));
		H.NoThrow("empty replay text", () => Replay.FromText(""));
		H.Eq("empty replay has no frames", Replay.FromText("").Inputs.Count, 0);
	}

	private static void Determinism(bool record)
	{
		H.Group("determinism");

		const ulong Seed = 0xC067117AAD00DUL;
		var inputs = ScriptedInputs(900, 99);

		// Two independent worlds, same seed and inputs, must agree at every tick
		// (spec 4.2).
		var w1 = Build(Seed);
		var w2 = Build(Seed);
		var marks = new[] { 60, 300, 900 };
		var hashes = new Dictionary<int, ulong>();

		bool agree = true;
		for (int i = 0; i < inputs.Count; i++)
		{
			w1.Step(inputs[i]);
			w2.Step(inputs[i]);
			if (w1.StateHash() != w2.StateHash()) { agree = false; break; }
			int frame = i + 1;
			if (Array.IndexOf(marks, frame) >= 0) hashes[frame] = w1.StateHash();
		}
		H.Check("two worlds stay bit-identical for 900 ticks", agree);

		// A different seed must not produce the same hash. Movement is RNG-free
		// at milestone 0, so this is really checking that the RNG state is
		// folded into the hash at all.
		var w3 = Build(Seed + 1);
		foreach (var f in inputs) w3.Step(f);
		H.Check("different seed yields a different hash", w3.StateHash() != w1.StateHash());

		// Replay text survives a round-trip.
		var replay = new Replay { Seed = Seed, LevelText = ReadLevel("substation_4.txt") };
		replay.Inputs.AddRange(inputs);
		string rtext = replay.ToText();
		var parsed = Replay.FromText(rtext);
		H.Eq("replay round-trip frame count", parsed.Inputs.Count, inputs.Count);
		H.Eq("replay round-trip seed", (long)parsed.Seed, (long)Seed);
		bool framesMatch = true;
		for (int i = 0; i < inputs.Count; i++)
		{
			if (parsed.Inputs[i].MoveX != inputs[i].MoveX
				|| parsed.Inputs[i].MoveY != inputs[i].MoveY
				|| parsed.Inputs[i].AimBrad != inputs[i].AimBrad
				|| parsed.Inputs[i].Flags != inputs[i].Flags) { framesMatch = false; break; }
		}
		H.Check("replay round-trip preserves every frame", framesMatch);

		// Replaying the parsed stream reproduces the same hash.
		var w4 = Build(Seed);
		foreach (var f in parsed.Inputs) w4.Step(f);
		H.Check("parsed replay reproduces the hash", w4.StateHash() == w1.StateHash());

		H.Check("replay round-trip preserves the level", parsed.LevelText == replay.LevelText);

		ReplayVerification(inputs);
		H.Group("determinism");   // ReplayVerification changes the group; restore it

		// Golden values (spec 4.2). Regenerate with --record after an
		// INTENTIONAL sim change; a surprise failure here is a real regression.
		if (record)
		{
			Console.WriteLine();
			Console.WriteLine("  golden hashes - paste into Goldens.cs:");
			foreach (int m in marks)
				Console.WriteLine($"    {{ {m}, 0x{hashes[m]:X16}UL }},");
			Console.WriteLine($"    Final = 0x{w1.StateHash():X16}UL");
			Console.WriteLine();
		}
		else
		{
			foreach (int m in marks)
			{
				bool have = hashes.TryGetValue(m, out ulong got);
				bool want = Goldens.Frames.TryGetValue(m, out ulong exp);
				H.Check($"state hash matches golden @ frame {m}",
					have && want && got == exp,
					$"want 0x{exp:X16}, got 0x{got:X16}");
			}
			H.Check("state hash matches golden @ final",
				w1.StateHash() == Goldens.Final,
				$"want 0x{Goldens.Final:X16}, got 0x{w1.StateHash():X16}");
		}

		// Idle stability (spec 4.3): 30 s with the player at spawn leaves every
		// guard on its default behaviour and the alarm at zero.
		var idle = Build(Seed);
		var still = new InputFrame(0, 0, 0, 0);
		for (int i = 0; i < 30 * 60; i++) idle.Step(still);
		var snap = idle.Snapshot();
		H.Eq("idle: alarm stays 0", snap.AlarmLevel, 0);
		bool calm = true;
		foreach (var g in snap.Guards)
			if (g.State != GuardState.Relaxed) calm = false;
		H.Check("idle: all guards remain Patrol or Sentry", calm);
		H.Eq("idle: player has not drifted from spawn X", snap.PlayerX, idle.Level.SpawnX);
		H.Eq("idle: player has not drifted from spawn Y", snap.PlayerY, idle.Level.SpawnY);
		H.Eq("idle: 1800 ticks elapsed", snap.Tick, 1800);

		// Walls actually stop the player.
		var pen = Build(Seed);
		var west = new InputFrame(-1, 0, Brad.Half, 0);
		for (int i = 0; i < 600; i++) pen.Step(west);
		H.Check("player cannot tunnel out through a wall",
			pen.Player.X > 0
			&& !Geometry.HitsWall(pen.Level.Walls, pen.Player.X, pen.Player.Y, pen.Player.Radius),
			$"ended at x={pen.Player.X / 256.0:F2}");

		// Sneaking is exactly half speed and makes no noise.
		//
		// Measured in an OPEN room rather than from the reference level's spawn:
		// walking one direction from a hand-authored spawn measures whatever is
		// south of it, and a spawn room with its door in a corner fails this
		// while the movement path is perfectly healthy.
		var walk = Build(Seed);
		var sneak = Build(Seed);
		int midX = walk.Level.WidthFx / 2, midY = walk.Level.HeightFx / 2;
		walk.Player.X = midX; walk.Player.Y = midY;
		sneak.Player.X = midX; sneak.Player.Y = midY;
		for (int i = 0; i < 30; i++)
		{
			walk.Step(new InputFrame(0, 1, 0, 0));
			sneak.Step(new InputFrame(0, 1, 0, InputFrame.FSneak));
		}
		int walked = walk.Player.Y - midY;
		int snuck = sneak.Player.Y - midY;
		H.Check("sneak is half speed", walked == snuck * 2, $"walked {walked}, snuck {snuck}");
		H.Eq("sneaking makes no noise", sneak.Player.NoiseRadius, 0);
		H.Eq("walking makes noise", walk.Player.NoiseRadius, Tune.NoiseWalkRadius);
	}
}
