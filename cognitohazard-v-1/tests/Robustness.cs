using System;
using System.Collections.Generic;
using System.Text;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// Adversarial input. Every parser in sim/ documents itself as TOTAL — a bad
/// line is skipped, never fatal — and that claim is almost never tested against
/// real garbage, only against the tidy malformed cases someone thought of.
///
/// So: take the real files, break them in every mechanical way (truncate at
/// every offset, flip bytes, duplicate lines, drop lines, stretch numbers past
/// every integer boundary), and demand two things of the result. It must not
/// throw, and what comes back must be USABLE — a Level that can be built into a
/// SimWorld and stepped without blowing up. A parser that returns a Level which
/// then crashes the world is not total, it has just moved where it fails.
/// </summary>
public static class Robustness
{
	private static readonly List<string> Failures = new();

	private static void Note(string what, string detail)
	{
		if (Failures.Count < 200) Failures.Add(what + ": " + detail);
	}

	private static void Verdict(string name, int before)
	{
		H.Check(name, Failures.Count == before,
			Failures.Count > before ? Failures[before]
				+ (Failures.Count - before > 1
					? $"   (+{Failures.Count - before - 1} more)" : "")
				: "");
	}

	// ------------------------------------------------------------ level text

	/// <summary>Build a world from a level and step it. The point is that
	/// parsing is not the end of the obligation: the thing that comes out has
	/// to survive being played.</summary>
	private static void Exercise(string label, string text, bool guardAi = true)
	{
		Level level;
		try
		{
			level = Level.FromText(text);
		}
		catch (Exception e)
		{
			Note("Level.FromText threw", $"{label}: {e.GetType().Name} {e.Message}");
			return;
		}

		if (level.W <= 0 || level.H <= 0)
		{
			Note("a parsed level has a positive size", $"{label}: {level.W}x{level.H}");
			return;
		}
		if (level.Guards.Count > Level.MaxGuards)
			Note("a parsed level respects MaxGuards",
				$"{label}: {level.Guards.Count}");

		try
		{
			var w = new SimWorld(level, 7, new Loadout(WeaponId.Glock,
				ArmourId.None, backpack: 503));
			for (int i = 0; i < 20; i++)
				w.Step(new InputFrame(1, 1, i * 4096, InputFrame.FFire));
			if (w.Player.X < 0 || w.Player.Y < 0
				|| w.Player.X > level.WidthFx || w.Player.Y > level.HeightFx)
				Note("the player starts and stays inside a parsed level",
					$"{label}: at {w.Player.X},{w.Player.Y}");
		}
		catch (Exception e)
		{
			Note("a parsed level can be played",
				$"{label}: {e.GetType().Name} {e.Message}");
		}

		// The guard AI's whole ladder on the same broken level (Guard_AI.md P6):
		// a fight begun from the spawn, a compromised floor, a sweep. The nav
		// grid, the flood, the path service and the sweep map are all built
		// from whatever the parser made of the text, and none of them may throw.
		// Mostly fixed cost per level (the nav grid and sweep map are built
		// fresh for each), so the 400 random byte flips run it one in four;
		// every other kind of breakage runs it in full.
		if (!guardAi) return;
		try
		{
			var w = new SimWorld(level, 11);
			if (w.Guards.Count > 0) w.EnterCombat(w.Guards[0], w.Player.X, w.Player.Y, false);
			for (int i = 0; i < 10; i++) w.Step(new InputFrame(0, 0, 0, 0));
			w.Compromise(w.Player.X, w.Player.Y);
			for (int i = 0; i < 25; i++) w.Step(new InputFrame(1, 0, 0, 0));
			if (w.Normalised != 0)
				Note("the guard AI never needs a guard repaired on a parsed level", $"{label}: {w.Normalised}");
			for (int i = 0; i < w.Guards.Count; i++)
			{
				var g = w.Guards[i];
				if (!g.Prone && !SimWorld.TaskFits(g.State, g.Task))
					Note("the guard AI keeps its rules on a parsed level", $"{label}: guard {i} {g.State}/{g.Task}");
			}
		}
		catch (Exception e)
		{
			Note("the guard AI survives any parsed level",
				$"{label}: {e.GetType().Name} {e.Message}");
		}
	}

	private static void Levels()
	{
		H.Group("robustness / level text");
		string real = System.IO.File.ReadAllText(
			System.IO.Path.Combine(Fuzz.LevelsDir(), "substation_4.txt"));

		int before = Failures.Count;

		// Nothing, and almost nothing.
		foreach (var s in new[] { "", " ", "\n", "\n\n\n", "grid:", "grid:\n",
			"name: x", "\0", "\t\t\t", "grid:\n\n", "> a", "> a 1,1" })
			Exercise("literal " + Escape(s), s);

		// Truncated at 120 points across the file, which catches every
		// "half a line" and "cut in the middle of the grid" case.
		for (int i = 0; i <= 120; i++)
			Exercise($"truncated at {i}/120", real.Substring(0, real.Length * i / 120));

		// One byte changed, across the file. Turns floors into walls, glyphs
		// into unknown characters, digits into letters.
		var rng = new DetRng(0x5DEECE66DUL);
		for (int i = 0; i < 400; i++)
		{
			var b = new StringBuilder(real);
			int at = rng.NextInt(real.Length);
			b[at] = (char)rng.NextRange(32, 126);
			Exercise($"byte {at} changed", b.ToString(), guardAi: i % 4 == 0);
		}

		// Whole lines dropped and duplicated, which makes the grid ragged.
		var lines = new List<string>(real.Split('\n'));
		for (int i = 0; i < lines.Count; i += 3)
		{
			var cut = new List<string>(lines);
			cut.RemoveAt(i);
			Exercise($"line {i} dropped", string.Join("\n", cut));

			var dup = new List<string>(lines);
			dup.Insert(i, lines[i]);
			Exercise($"line {i} duplicated", string.Join("\n", dup));
		}

		Verdict("no level text makes the parser throw", before);
		before = Failures.Count;

		// Numbers at and past every integer boundary, in the route lines the
		// parser reads as coordinates.
		foreach (var n in new[] { "0", "-1", "2147483647", "-2147483648",
			"2147483648", "9223372036854775808", "99999999999999999999",
			"1e9", "0x10", "+5", "--5", "5.5", "NaN", "" })
			Exercise($"route coord {n}",
				"name: t\ngrid:\n####\n#@.#\n#.X#\n####\n> a " + n + "," + n + "\n");

		// A grid of every printable character, one row at a time.
		for (int c = 32; c < 127; c++)
		{
			string row = new string((char)c, 8);
			Exercise($"grid of '{(char)c}'",
				"name: t\ngrid:\n########\n#" + row.Substring(0, 6) + "#\n########\n");
		}

		// Sizes at the clamps.
		foreach (var dim in new[] { 1, 2, 3, Level.MinDim - 1, Level.MinDim,
			Level.MaxDim, Level.MaxDim + 1, 500 })
			Exercise($"a {dim}-wide grid", Box(dim, 6));
		foreach (var dim in new[] { 1, 2, 3, Level.MinDim, Level.MaxDim + 1 })
			Exercise($"a {dim}-tall grid", Box(8, dim));

		// More guard glyphs than MaxGuards allows.
		var many = new StringBuilder("name: t\ngrid:\n");
		many.Append(new string('#', 32)).Append('\n');
		for (int r = 0; r < 4; r++)       // 120 glyphs, past the 88 the alphabet has
		{
			many.Append('#');
			for (int c = 0; c < 30; c++)
				many.Append(Level.GuardGlyphs[(r * 30 + c) % Level.GuardGlyphs.Length]);
			many.Append("#\n");
		}
		many.Append(new string('#', 32)).Append('\n');
		Exercise("120 guard glyphs", many.ToString());

		Verdict("no adversarial level is parsed into an unplayable world", before);
	}

	private static string Box(int w, int h)
	{
		var b = new StringBuilder("name: t\ngrid:\n");
		for (int r = 0; r < h; r++)
		{
			for (int c = 0; c < w; c++)
				b.Append(r == 0 || r == h - 1 || c == 0 || c == w - 1 ? '#'
					: (r == 1 && c == 1 ? '@' : (r == h - 2 && c == w - 2 ? 'X' : '.')));
			b.Append('\n');
		}
		return b.ToString();
	}

	private static string Escape(string s)
		=> s.Replace("\n", "\\n").Replace("\t", "\\t").Replace("\0", "\\0");

	// ----------------------------------------------------------- replay text

	private static void Replays()
	{
		H.Group("robustness / replay text");
		int before = Failures.Count;

		string level = Box(12, 8);
		var rep = new Replay { LevelText = level, Seed = 3,
			Loadout = new Loadout(WeaponId.Ak47, ArmourId.LightWeave, backpack: 502) };
		var gen = new DetRng(99);
		for (int i = 0; i < 60; i++)
			rep.Inputs.Add(new InputFrame(gen.NextRange(-1, 1), gen.NextRange(-1, 1),
				gen.NextBrad(), (byte)gen.NextInt(256), gen.NextInt(9),
				gen.NextInt(4), gen.NextInt(12), 0, 0));
		string real = rep.ToText();

		void Try(string label, string text)
		{
			Replay r;
			try { r = Replay.FromText(text); }
			catch (Exception e)
			{
				Note("Replay.FromText threw", $"{label}: {e.GetType().Name} {e.Message}");
				return;
			}
			try
			{
				var lv = Level.FromText(r.LevelText.Length > 0 ? r.LevelText : level);
				var w = new SimWorld(lv, r.Seed, r.Loadout);
				for (int i = 0; i < r.Inputs.Count && i < 200; i++) w.Step(r.Inputs[i]);
			}
			catch (Exception e)
			{
				Note("a parsed replay can be played",
					$"{label}: {e.GetType().Name} {e.Message}");
			}
		}

		Try("null", null!);
		foreach (var s in new[] { "", "\n", "frames:", "frames:\n", "level:\n",
			"seed: x", "seed:", "frames:\nx y z w", "frames:\n1 1 1 1 xxx",
			"frames:\n1 1 1 1 x0", "frames:\n1 1 1 1 x-5", "frames:\n1 1 1 1 x999999999",
			"frames:\n1 1 1 1 p999 m999 d999 s999999 e999999" })
			Try("literal " + Escape(s), s);

		for (int i = 0; i <= 80; i++)
			Try($"truncated at {i}/80", real.Substring(0, real.Length * i / 80));

		var rng = new DetRng(0xABCDEF);
		for (int i = 0; i < 300; i++)
		{
			var b = new StringBuilder(real);
			b[rng.NextInt(real.Length)] = (char)rng.NextRange(32, 126);
			Try($"byte flip {i}", b.ToString());
		}

		Verdict("no replay text makes the parser throw or the world crash", before);

		// A billion-frame run length used to be ALLOCATED: 16 GB for a 30-byte
		// file. It passed where the OS would page it out and died of
		// OutOfMemory where it would not.
		var huge = Replay.FromText("frames:\n1 1 1 1 x999999999\n0 0 0 0 x999999999");
		H.Check("a run length past MaxFrames is capped, not allocated",
			huge.Inputs.Count == Replay.MaxFrames, $"{huge.Inputs.Count} frames");

		// The m token is omitted at walk, and a frame with no m token derives
		// its tier from the legacy FSneak bit. A WALK frame with that bit set
		// therefore came back as a stealth frame: a different input, hashed.
		var legacy = new Replay();
		legacy.Inputs.Add(new InputFrame(0, 1, 0, InputFrame.FSneak, moveTier: InputFrame.TierWalk));
		legacy.Inputs.Add(new InputFrame(0, 1, 0, InputFrame.FSneak));
		legacy.Inputs.Add(new InputFrame(0, 1, 0, 0, moveTier: InputFrame.TierSprint));
		var legacyBack = Replay.FromText(legacy.ToText());
		bool tiersKept = legacyBack.Inputs.Count == legacy.Inputs.Count;
		for (int i = 0; tiersKept && i < legacy.Inputs.Count; i++)
			tiersKept = legacyBack.Inputs[i].MoveTier == legacy.Inputs[i].MoveTier
				&& legacyBack.Inputs[i].Flags == legacy.Inputs[i].Flags;
		H.Check("every frame's tier survives a round trip, FSneak bit or not", tiersKept,
			"a frame came back on another tier");
	}

	// -------------------------------------------------------- the catalogues

	/// <summary>
	/// Every id anyone could ask for, including ones that do not exist. These
	/// lookups are called from GDScript, where a typo is a runtime value rather
	/// than a compile error, so they have to be total too.
	/// </summary>
	private static void Catalogues()
	{
		H.Group("robustness / catalogue lookups");
		int before = Failures.Count;

		foreach (int id in new[] { int.MinValue, -1, 0, 1, 99, 100, 999, 9999,
			int.MaxValue })
		{
			try
			{
				var g = GearCatalog.Get(id);
				if (g.W < 0 || g.H < 0)
					Note("a gear lookup never yields a negative size", $"id {id}");
				GearCatalog.Exists(id);
				GearCatalog.PriceOf(id);
				GearCatalog.IsObjective(id);
				GearCatalog.WeaponItemId(id);
				GearCatalog.ArmourItemId(id);
			}
			catch (Exception e)
			{
				Note("a gear lookup never throws", $"id {id}: {e.GetType().Name}");
			}
		}

		foreach (int id in new[] { int.MinValue, -5, -1, 0, 1,
			WeaponCatalog.Count - 1, WeaponCatalog.Count, 999, int.MaxValue })
		{
			try
			{
				var w = WeaponCatalog.Clamp(id);
				var spec = WeaponCatalog.Get(w);
				if (spec.Magazine <= 0)
					Note("every clamped weapon has a magazine", $"{id} -> {w}");
			}
			catch (Exception e)
			{
				Note("a weapon lookup never throws", $"id {id}: {e.GetType().Name}");
			}
		}

		foreach (int id in new[] { int.MinValue, -1, 0, 3, 4, 999, int.MaxValue })
			try { ArmourCatalog.Get(ArmourCatalog.Clamp(id)); }
			catch (Exception e)
			{ Note("an armour lookup never throws", $"id {id}: {e.GetType().Name}"); }

		Verdict("no catalogue lookup throws or yields nonsense", before);
	}

	// ------------------------------------------------------------ InputFrame

	private static void Inputs()
	{
		H.Group("robustness / input frames");
		int before = Failures.Count;

		int[] wild = { int.MinValue, -99999, -2, -1, 0, 1, 2, 255, 256, 65535,
			65536, 99999, int.MaxValue };

		foreach (int a in wild)
		foreach (int b in wild)
		{
			try
			{
				var f = new InputFrame(a, b, a, (byte)(b & 0xFF), a, b, a, b, a);
				if (f.MoveX < -1 || f.MoveX > 1 || f.MoveY < -1 || f.MoveY > 1)
					Note("movement is always clamped to -1..1", $"{a},{b}");
				if (f.MoveTier < 0 || f.MoveTier >= InputFrame.TierCount)
					Note("the tier is always a real tier", $"{a},{b} -> {f.MoveTier}");
				if (f.AimBrad > Brad.Mask)
					Note("the aim is always inside one turn", $"{a} -> {f.AimBrad}");
				if (f.EquipPick != 0 && f.EquipPlacement < -1)
					Note("an equip placement is never below -1",
						$"{a},{b} -> {f.EquipPlacement}");
			}
			catch (Exception e)
			{
				Note("building an input never throws", $"{a},{b}: {e.GetType().Name}");
			}
		}

		// The equip packing, over its whole declared range and past it.
		for (int placement = -3; placement <= 260; placement++)
		for (int slot = -2; slot <= 9; slot++)
		{
			int packed = InputFrame.PackEquip(placement, slot);
			var f = new InputFrame(0, 0, 0, 0, 0, -1, 0, 0, packed);
			if (packed == 0) continue;
			if (f.EquipPlacement != placement)
				Note("an equip pick round-trips its placement",
					$"{placement},{slot} -> {f.EquipPlacement}");
			if (f.EquipSlot != slot)
				Note("an equip pick round-trips its slot",
					$"{placement},{slot} -> {f.EquipSlot}");
		}

		Verdict("no input frame is built into an invalid state", before);
	}

	public static void Run()
	{
		Levels();
		Replays();
		Catalogues();
		Inputs();
		GuardAi();
		Cultures();
	}

	/// <summary>
	/// The file formats do not depend on the machine's culture. Several write a
	/// negative number without '-' (sv-SE uses U+2212), and a replay records a
	/// negative MoveX/MoveY on every left or up frame: written in one culture and
	/// read in another, those frame lines failed to parse and were dropped, so
	/// the replay diverged. Written in a hostile culture, read in the invariant
	/// one, and the other way round; every format must come back identical.
	/// The hostile culture is BUILT, not looked up, so the test has teeth on a
	/// machine with no locale data at all.
	/// </summary>
	private static void Cultures()
	{
		H.Group("robustness / cultures");

		var hostile = (System.Globalization.CultureInfo)
			System.Globalization.CultureInfo.InvariantCulture.Clone();
		hostile.NumberFormat.NegativeSign = "\u2212";

		var rep = new Replay { Seed = 99, LevelText = Program.ReadLevel("substation_4.txt") };
		rep.Loadout = new Loadout(WeaponId.Glock, ArmourId.None, backpack: 503);
		rep.Inputs.Add(new InputFrame(-1, -1, 100, 0));
		rep.Inputs.Add(new InputFrame(1, -1, 200, 0));
		rep.Inputs.Add(new InputFrame(-1, 0, 300, 0));
		rep.AddHash(3, 0xDEADBEEFUL);
		var level = Level.FromText(rep.LevelText);
		level.Routes['a'] = new List<(int C, int R)> { (-2, 3), (4, -5) };

		var saved = System.Globalization.CultureInfo.CurrentCulture;
		string repText, levelText, kitText;
		Replay back;
		try
		{
			System.Globalization.CultureInfo.CurrentCulture = hostile;
			repText = rep.ToText();
			levelText = level.ToText();
			kitText = rep.Loadout.ToText();
			System.Globalization.CultureInfo.CurrentCulture =
				System.Globalization.CultureInfo.InvariantCulture;
			back = Replay.FromText(repText);
		}
		finally { System.Globalization.CultureInfo.CurrentCulture = saved; }

		H.Check("a replay written in any culture writes '-'", !repText.Contains('\u2212'),
			"U+2212 in the replay text");
		H.Eq("and every frame of it reads back elsewhere", back.Inputs.Count, rep.Inputs.Count);
		bool same = back.Inputs.Count == rep.Inputs.Count;
		for (int i = 0; same && i < rep.Inputs.Count; i++)
			same = back.Inputs[i].MoveX == rep.Inputs[i].MoveX
				&& back.Inputs[i].MoveY == rep.Inputs[i].MoveY;
		H.Check("with its moves intact", same, "a frame came back with a different move");
		H.Check("and its checkpoint", back.TryGetHash(3, out ulong hv) && hv == 0xDEADBEEFUL,
			"checkpoint lost");
		H.Check("a level and a loadout written in any culture write '-'",
			!levelText.Contains('\u2212') && !kitText.Contains('\u2212'),
			"U+2212 in the level or loadout text");

		// And the other way: invariant text read on a hostile machine.
		string plain = rep.ToText();
		Replay there;
		Level levelThere;
		try
		{
			System.Globalization.CultureInfo.CurrentCulture = hostile;
			there = Replay.FromText(plain);
			levelThere = Level.FromText(level.ToText());
		}
		finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
		H.Eq("an invariant replay reads back in a hostile culture",
			there.Inputs.Count, rep.Inputs.Count);
		H.Check("as does a level's negative route point",
			levelThere.Routes.TryGetValue('a', out var pts) && pts.Count == 2
				&& pts[0] == (-2, 3) && pts[1] == (4, -5),
			"route points lost");
	}

	/// <summary>
	/// The guard AI's derived structures at their edges (Guard_AI.md P6): a
	/// flood from nowhere, a sweep map with no floor, a compromise with no
	/// guards, a floor of nothing but sweep nodes. Each must answer "nothing"
	/// rather than throw.
	/// </summary>
	private static void GuardAi()
	{
		H.Group("robustness / guard AI");
		int before = Failures.Count;

		var solidText = new StringBuilder("grid:\n");
		for (int r = 0; r < 14; r++) solidText.Append(new string('#', 14)).Append('\n');
		var solid = Level.FromText(solidText.ToString());
		try
		{
			var pf = new PathFinder(solid.Nav);
			foreach (int start in new[] { -1, 0, 7 * 14 + 7, 14 * 14, int.MaxValue })
			{
				pf.Flood(start, int.MaxValue);
				for (int c = 0; c < 14 * 14; c++)
					if (pf.FloodCost(c) >= 0) { Note("a flood from nowhere reaches nothing", $"start {start} reached {c}"); break; }
				if (pf.FloodCost(-1) != -1 || pf.FloodCost(int.MaxValue) != -1)
					Note("FloodCost of an off-grid cell is -1", $"start {start}");
			}
			var map = new SweepMap(solid);
			if (map.Count != 0) Note("a level with no floor has no sweep nodes", $"{map.Count}");
			var w = new SimWorld(solid, 3);
			w.Compromise(0, 0);
			for (int i = 0; i < 30; i++) w.Step(new InputFrame(0, 0, 0, 0));
		}
		catch (Exception e) { Note("the guard AI survives a level of solid wall", $"{e.GetType().Name} {e.Message}"); }

		// Nothing but floor and sweep nodes, and no guards at all.
		var starText = new StringBuilder("grid:\n");
		starText.Append(new string('#', 20)).Append('\n');
		for (int r = 0; r < 12; r++) starText.Append('#').Append(new string('*', 18)).Append("#\n");
		starText.Append(new string('#', 20)).Append('\n');
		try
		{
			var stars = Level.FromText(starText.ToString());
			if (stars.SweepNodes.Count != 18 * 12)
				Note("every '*' is a sweep node", $"{stars.SweepNodes.Count} of {18 * 12}");
			var map = new SweepMap(stars);
			int authored = 0;
			for (int k = 0; k < map.Count; k++) if (map.Authored[k]) authored++;
			if (authored == 0) Note("authored nodes survive on a floor of them", "none");
			var w = new SimWorld(stars, 3);
			w.Compromise(w.Player.X, w.Player.Y);
			for (int i = 0; i < 30; i++) w.Step(new InputFrame(1, 1, 0, 0));
			if (w.Net.Groups.Count != 0) Note("no guards make no sweep groups", $"{w.Net.Groups.Count}");
		}
		catch (Exception e) { Note("the guard AI survives a floor of sweep nodes and no guards", $"{e.GetType().Name} {e.Message}"); }

		// A compromise twice, and a fight on a guard walled into one cell.
		try
		{
			var t = "grid:\n#######\n#@....#\n#.....#\n#..#..#\n#.#a#.#\n#..#..#\n#....X#\n#######\n> a 3,4 3,4\n";
			var w = new SimWorld(Level.FromText(t), 3);
			w.EnterCombat(w.Guards[0], w.Player.X, w.Player.Y, false);
			w.Compromise(w.Player.X, w.Player.Y);
			w.Compromise(0, 0);
			for (int i = 0; i < 60 * 15; i++) w.Step(new InputFrame(0, 0, 0, 0));
			if (w.Alarm.Level != AlarmState.Compromised) Note("a second compromise changes nothing", $"alarm {w.Alarm.Level}");
		}
		catch (Exception e) { Note("the guard AI survives a walled-in guard", $"{e.GetType().Name} {e.Message}"); }

		Verdict("the guard AI answers 'nothing' at its edges, never throws", before);
	}
}
