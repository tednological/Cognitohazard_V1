using System.Collections.Generic;
using System.Text;

namespace Cognitohazard.Sim;

/// <summary>Axis-aligned wall rect, in fixed-point px.</summary>
public readonly struct Rect
{
	public readonly int X, Y, W, H;
	public Rect(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
	public int X1 => X + W;
	public int Y1 => Y + H;
}

public sealed class CacheDef
{
	public int X, Y;                 // fixed-point centre
	public int[] Tiers = System.Array.Empty<int>();
}

public sealed class GuardDef
{
	public char Id;
	public int X, Y;                 // fixed-point centre
	public int[] Tiers = System.Array.Empty<int>();
	public int[]? PathX;             // fixed-point waypoints, null == stationary sentry
	public int[]? PathY;
}

/// <summary>
/// The text level format (spec §2.1) plus the greedy wall merge (§2.2).
///
/// The parser is TOTAL: any input at all yields a playable level. It never
/// throws. Missing '@' spawns at cell (1,1); missing 'X' puts a 2x2 exit at
/// (W-3, H-3).
///
/// A level carries its OWN dimensions. GW/GH are the defaults for a new or
/// empty level, not a fixed world size: <see cref="FromText"/> infers W and H
/// from the grid text it is handed, so a 48x28 file still parses to exactly
/// 48x28 and every replay recorded against one still verifies, while a larger
/// file simply describes a larger floor. Everything downstream — the wall
/// merge, the flood fill, the exit default — is written against the instance
/// fields, never the constants.
/// </summary>
/// <summary>
/// The two kinds of cell that change state during a run. Ordinals are hashed
/// and cross the bridge, so new kinds go on the END.
/// </summary>
public enum PanelKind
{
	/// <summary>Glyph '='. Blocks movement, NOT sight. A round that touches an
	/// intact pane shatters it and flies on; the pane is then open floor.</summary>
	Glass,
	/// <summary>Glyph '+'. Closed, it is a wall to everything: movement, sight
	/// and rounds. Open, it is floor. G toggles it; a guard walking into a shut
	/// one opens it.</summary>
	Door,
}

/// <summary>
/// One glass pane or one door leaf, as the level authored it. A run of the
/// same glyph is ONE panel up to <see cref="Level.GlassPaneCells"/> /
/// <see cref="Level.DoorLeafCells"/> long, so a two-cell doorway opens as one
/// door and a long window breaks in sections rather than all at once.
/// </summary>
public sealed class PanelDef
{
	public PanelKind Kind;
	public Rect Rect;
	/// <summary>The panel runs top-to-bottom, i.e. it sits in a vertical wall.
	/// Presentation only: which way the leaf is drawn and the shards fly.</summary>
	public bool Vertical;
}

/// <summary>Where a gear chest stands. What is in it is the sim's business,
/// except for an objective site, which by definition holds the objective.</summary>
public sealed class ChestDef
{
	public int X, Y;
	public bool Objective;
}

public sealed class Level
{
	/// <summary>Default grid for a blank level. One screen at the design
	/// resolution, which is what the whole game was authored against.</summary>
	public const int GW = 48;
	public const int GH = 28;
	public const int CellPx = 20;
	public const int CellFx = CellPx * Fx.One;

	/// <summary>
	/// Bounds on an inferred grid. The parser is total, so a malformed file
	/// claiming enormous dimensions must CLAMP rather than allocate — and one
	/// claiming tiny ones must clamp up, or the default exit at (W-3, H-3)
	/// lands outside the level.
	/// </summary>
	/// <summary>
	/// The guard glyph range. 'a' to 'z', not the 'a' to 'h' this shipped with:
	/// eight was the cap on how dangerous ANY level could be, whatever its size,
	/// and it made every floor rate the same on the mission-threat scale
	/// (missions.gd) because the guard term was constant across all of them.
	/// </summary>
	public const char GuardFirst = 'a';
	public const char GuardLast = 'z';
	/// <summary>
	/// The most guards one level may hold. ENFORCED by FromText, which skips
	/// glyphs past it — the count is otherwise bounded only by MaxCells.
	/// </summary>
	public const int MaxGuards = GuardLast - GuardFirst + 1;

	public static bool IsGuardGlyph(char ch) => ch >= GuardFirst && ch <= GuardLast;

	/// <summary>Glass pane and door glyphs. '=' reads as a window in a wall and
	/// '+' is the door every roguelike already taught the reader.</summary>
	public const char GlassGlyph = '=';
	public const char DoorGlyph = '+';

	/// <summary>
	/// A designer-marked place the guards should check when they sweep a
	/// compromised level (Guard_AI.md §6.3.1). Floor for every other purpose:
	/// walls, nav, movement and sight all ignore it.
	/// </summary>
	public const char SweepGlyph = '*';

	/// <summary>
	/// Longest run of glass that is ONE pane. A round shatters the pane it
	/// touches, so a twelve-cell window wall breaking end to end off one stray
	/// shot would be both wrong and a free corridor. Four cells is a window.
	/// </summary>
	public const int GlassPaneCells = 4;

	/// <summary>
	/// Longest run of door cells that swings as ONE door. An actor is 22 px
	/// across and a cell 20, so a one-cell door is a door nobody fits through:
	/// two is the doorway, three is the widest double door worth drawing.
	/// </summary>
	public const int DoorLeafCells = 3;

	public const int MinDim = 12;
	public const int MaxDim = 512;
	public const int MaxCells = GW * GH * 16;

	/// <summary>This level's own grid size, in cells.</summary>
	public readonly int W, H;

	public int WidthFx => W * CellFx;
	public int HeightFx => H * CellFx;

	public string Name = "untitled";
	public readonly char[] Grid;

	public Level() : this(GW, GH) { }

	public Level(int w, int h)
	{
		W = Clamp(w, MinDim, MaxDim);
		H = Clamp(h, MinDim, MaxDim);
		// Area bound last, so a pathological aspect ratio cannot slip past the
		// per-axis clamps and allocate a gigabyte.
		if ((long)W * H > MaxCells) H = Clamp(MaxCells / W, MinDim, MaxDim);
		Grid = new char[W * H];
	}

	private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

	/// <summary>Route waypoints in GRID cells, keyed by guard id. Sorted by id
	/// on serialise so round-trips are stable.</summary>
	public readonly SortedDictionary<char, List<(int C, int R)>> Routes = new();

	// Derived by Build().
	public Rect[] Walls = System.Array.Empty<Rect>();
	public int SpawnX, SpawnY;
	public Rect Exit;
	public List<CacheDef> Caches = new();
	public List<GuardDef> Guards = new();

	/// <summary>Gear containers, glyph 'C'. Contents are rolled by the sim from
	/// the loot stream, not authored here — a level says WHERE a chest is, not
	/// what is in it.</summary>
	public List<ChestDef> Chests = new();

	/// <summary>
	/// Glass panes and doors, in row-major order of their first cell, so the
	/// index a replay names (InputFrame.DoorPick) means the same panel on every
	/// machine. Walls never include these cells: what blocks what is decided per
	/// run by SimWorld, because these are the cells that change.
	/// </summary>
	public List<PanelDef> Panels = new();

	/// <summary>How many objective sites this level has, glyph '!'. Zero means
	/// a level with no objective, which extracts successfully on its own.</summary>
	public int Objectives;

	// ------------------------------------------------------------- loot
	//
	// What the floor is WORTH, in the same dollars the shop charges. -1 means
	// "not authored", which derives a figure from the level (see ChestBudget and
	// PointsFor) and, crucially, writes NOTHING on save -- so every level made
	// before loot budgets existed round-trips byte for byte.

	/// <summary><c>loot: N</c>. Dollars spread across every supply chest.</summary>
	public int LootBudget = -1;

	/// <summary><c>guard_loot: N</c>. Points each guard buys his kit with.</summary>
	public int GuardLoot = -1;

	/// <summary><c>kit: a N</c>. One guard's points, overriding guard_loot. The
	/// point-buy ASSIGNMENT: a designer makes one guard worth robbing.</summary>
	public readonly SortedDictionary<char, int> KitPoints = new();

	/// <summary>Largest budget a line may claim. The parser is total: a file
	/// asking for more clamps rather than overflowing a sum.</summary>
	public const int MaxLoot = 1_000_000;

	/// <summary>Supply chests: every chest that is not an objective site.</summary>
	public int SupplyChests => Chests.Count - Objectives;

	/// <summary>The dollars this level's chests hold between them.</summary>
	public int ChestBudget => LootBudget >= 0 ? LootBudget : SupplyChests * Tune.LootPerChest;

	/// <summary>The points one guard buys his kit with.</summary>
	public int PointsFor(char guardId)
		=> KitPoints.TryGetValue(guardId, out int pts) ? pts
			: GuardLoot >= 0 ? GuardLoot : Tune.GuardLootPoints;

	/// <summary>Every guard's points together: what the bodies on this floor
	/// are worth, as the mission select shows it.</summary>
	public int GuardLootTotal
	{
		get
		{
			long n = 0;
			foreach (var g in Guards) n += PointsFor(g.Id);
			return n > int.MaxValue ? int.MaxValue : (int)n;
		}
	}

	/// <summary>
	/// A loot line, recognised in ANY mode, the way name: is. Returns true when
	/// the line was one. Each has a colon, which no grid row can: column 0 of a
	/// real row is wall, and a garbage row that happened to spell one would only
	/// ever lose a row, never throw.
	/// </summary>
	private static bool ParseLootLine(Level? L, string line)
	{
		if (StartsWithNoCase(line, "loot:"))
		{
			if (L != null) L.LootBudget = ParseAmount(line.Substring(5));
			return true;
		}
		if (StartsWithNoCase(line, "guard_loot:"))
		{
			if (L != null) L.GuardLoot = ParseAmount(line.Substring(11));
			return true;
		}
		if (StartsWithNoCase(line, "kit:"))
		{
			if (L == null) return true;
			string[] bits = line.Substring(4).Trim()
				.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries);
			if (bits.Length >= 2 && bits[0].Length == 1 && IsGuardGlyph(bits[0][0]))
			{
				int pts = ParseAmount(bits[1]);
				if (pts >= 0) L.KitPoints[bits[0][0]] = pts;
			}
			return true;
		}
		return false;
	}

	/// <summary>A non-negative amount, clamped to MaxLoot; -1 for garbage, which
	/// the caller treats as "not authored".</summary>
	private static int ParseAmount(string s)
	{
		if (!long.TryParse(s.Trim(), out long v)) return -1;
		if (v < 0) return 0;
		return v > MaxLoot ? MaxLoot : (int)v;
	}

	/// <summary>Authored sweep nodes ('*'), as cells, in row-major order.</summary>
	public List<(int C, int R)> SweepNodes = new();

	/// <summary>
	/// Where a guard can walk (Guard_AI.md §8). Derived from the grid on first
	/// use and cached; <see cref="Build"/> drops the cache, since that is where
	/// an edited grid becomes walls. Lazy because most worlds never path.
	/// </summary>
	public NavGrid Nav => _nav ??= new NavGrid(this);
	private NavGrid? _nav;

	public char At(int c, int r) => Grid[r * W + c];
	public void Set(int c, int r, char ch) { Grid[r * W + c] = ch; }

	public bool InBounds(int c, int r) => c >= 0 && r >= 0 && c < W && r < H;

	public static Level Blank() => Blank(GW, GH);

	public static Level Blank(int w, int h)
	{
		var L = new Level(w, h);
		for (int r = 0; r < L.H; r++)
			for (int c = 0; c < L.W; c++)
				L.Set(c, r, (r == 0 || r == L.H - 1 || c == 0 || c == L.W - 1) ? '#' : '.');
		return L;
	}

	// ---------------------------------------------------------------- parsing

	public static Level FromText(string text)
	{
		if (text == null) { var blank = Blank(); blank.Build(); return blank; }

		string[] lines = text.Replace("\r", "").Split('\n');

		// Two passes. The first only measures: it walks the lines under exactly
		// the same rules as the second so that what counts as a grid row cannot
		// drift between them, and takes the widest row and the row count as the
		// level's dimensions. A file with no grid at all keeps the defaults,
		// which is what makes FromText("") a playable 48x28 room.
		Measure(lines, out int w, out int h);

		var L = Blank(w, h);
		string mode = "";
		int row = 0;

		foreach (string line in lines)
		{
			if (ParseLootLine(L, line)) continue;
			if (StartsWithNoCase(line, "name:"))
			{
				string n = line.Substring(5).Trim();
				L.Name = n.Length == 0 ? "untitled" : n;
				continue;
			}
			if (StartsWithNoCase(line, "grid:")) { mode = "grid"; row = 0; continue; }

			string t = line.Trim();
			if (t.StartsWith(">"))
			{
				mode = "routes";
				ParseRoute(L, t);
				continue;
			}
			if (t.StartsWith("#") && mode != "grid") continue;   // comment before the grid

			if (mode == "grid")
			{
				if (row >= L.H) continue;
				for (int c = 0; c < L.W; c++)
					L.Set(c, row, c < line.Length ? line[c] : '.');
				row++;
			}
		}
		L.Build();
		return L;
	}

	/// <summary>
	/// Grid dimensions implied by the text, or the defaults when it carries no
	/// grid. Trailing whitespace does not count toward the width: a row padded
	/// with spaces describes the same floor as one that is not, and letting it
	/// widen the level would make a round-trip lossy.
	/// </summary>
	private static void Measure(string[] lines, out int w, out int h)
	{
		string mode = "";
		int widest = 0, rows = 0, lastContentRow = 0;

		foreach (string line in lines)
		{
			if (StartsWithNoCase(line, "name:")) continue;
			if (ParseLootLine(null, line)) continue;
			if (StartsWithNoCase(line, "grid:"))
			{
				mode = "grid"; widest = 0; rows = 0; lastContentRow = 0; continue;
			}

			string t = line.Trim();
			if (t.StartsWith(">")) { mode = "routes"; continue; }
			if (t.StartsWith("#") && mode != "grid") continue;

			if (mode == "grid")
			{
				int len = line.TrimEnd().Length;
				if (len > widest) widest = len;
				rows++;
				// A file's closing newline leaves one empty trailing line that was
				// never a row of the level. Blank lines INSIDE the grid still
				// count, because a later row extends the height past them.
				if (len > 0) lastContentRow = rows;
			}
		}

		if (widest == 0) { w = GW; h = GH; return; }

		w = widest;
		h = lastContentRow;
	}

	private static bool StartsWithNoCase(string s, string prefix)
		=> s.Length >= prefix.Length
		   && string.Compare(s, 0, prefix, 0, prefix.Length,
			   System.StringComparison.OrdinalIgnoreCase) == 0;

	private static void ParseRoute(Level L, string trimmed)
	{
		string body = trimmed.Substring(1).Trim();
		if (body.Length == 0) return;
		string[] bits = body.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries);
		if (bits.Length == 0) return;

		char id = bits[0][0];
		var pts = new List<(int, int)>();
		for (int i = 1; i < bits.Length; i++)
		{
			string[] q = bits[i].Split(',');
			if (q.Length < 2) continue;
			if (!int.TryParse(q[0], out int cc)) continue;
			if (!int.TryParse(q[1], out int rr)) continue;
			pts.Add((cc, rr));
		}
		L.Routes[id] = pts;
	}

	// ------------------------------------------------------------ serialising

	/// <summary>The glyph key every saved level carries above its grid. A
	/// comment, so it changes no hash; it is here so a level opened in a text
	/// editor explains itself.</summary>
	public const string HeaderComment =
		"# glyphs  # wall  . floor  = glass  + door  @ spawn  X exit  $ records"
		+ "  C chest  ! objective  a-z guard start  * sweep node";

	public string ToText()
	{
		var sb = new StringBuilder();
		sb.Append("name: ").Append(Name).Append('\n');
		if (LootBudget >= 0) sb.Append("loot: ").Append(LootBudget).Append('\n');
		if (GuardLoot >= 0) sb.Append("guard_loot: ").Append(GuardLoot).Append('\n');
		sb.Append(HeaderComment).Append('\n');
		sb.Append("grid:\n");
		for (int r = 0; r < H; r++)
		{
			for (int c = 0; c < W; c++) sb.Append(At(c, r));
			sb.Append('\n');
		}
		foreach (var kv in Routes)          // SortedDictionary: stable id order
		{
			if (kv.Value.Count == 0) continue;
			sb.Append("> ").Append(kv.Key);
			foreach (var p in kv.Value) sb.Append(' ').Append(p.C).Append(',').Append(p.R);
			sb.Append('\n');
		}
		foreach (var kv in KitPoints)       // SortedDictionary: stable id order
			sb.Append("kit: ").Append(kv.Key).Append(' ').Append(kv.Value).Append('\n');
		return sb.ToString();
	}

	// -------------------------------------------------------------- deriving

	/// <summary>
	/// Greedy horizontal-run-then-vertical-extend merge (spec §2.2). Collapses
	/// the reference level's wall cells to a couple of dozen rects. Raycast cost
	/// is linear in rect count, so this is a requirement and not an
	/// optimisation — and it matters far more on a large floor than a small one.
	/// </summary>
	public static Rect[] MergeWalls(char[] grid, int w, int h)
	{
		var used = new bool[w * h];
		var outRects = new List<Rect>();

		for (int r = 0; r < h; r++)
		{
			for (int c = 0; c < w; c++)
			{
				int i = r * w + c;
				if (grid[i] != '#' || used[i]) continue;

				int c1 = c;
				while (c1 + 1 < w && grid[r * w + c1 + 1] == '#' && !used[r * w + c1 + 1]) c1++;

				int r1 = r;
				while (r1 + 1 < h)
				{
					bool ok = true;
					for (int cc = c; cc <= c1; cc++)
					{
						int j = (r1 + 1) * w + cc;
						if (grid[j] != '#' || used[j]) { ok = false; break; }
					}
					if (!ok) break;
					r1++;
				}

				for (int rr = r; rr <= r1; rr++)
					for (int cc = c; cc <= c1; cc++)
						used[rr * w + cc] = true;

				outRects.Add(new Rect(c * CellFx, r * CellFx,
					(c1 - c + 1) * CellFx, (r1 - r + 1) * CellFx));
			}
		}
		return outRects.ToArray();
	}

	/// <summary>
	/// Collapse runs of one panel glyph into panels no longer than
	/// <paramref name="cap"/> cells. Horizontal first, like the wall merge; a
	/// cell with no horizontal neighbour tries downward instead, so a window in a
	/// vertical wall is one pane rather than four single-cell ones. Row-major and
	/// greedy, so the result is a pure function of the grid.
	/// </summary>
	public static List<PanelDef> MergePanels(char[] grid, int w, int h, char glyph,
		PanelKind kind, int cap)
	{
		var used = new bool[w * h];
		var outp = new List<PanelDef>();
		for (int r = 0; r < h; r++)
		{
			for (int c = 0; c < w; c++)
			{
				int i = r * w + c;
				if (grid[i] != glyph || used[i]) continue;

				int c1 = c;
				while (c1 + 1 < w && c1 - c + 1 < cap
					&& grid[r * w + c1 + 1] == glyph && !used[r * w + c1 + 1]) c1++;

				int r1 = r;
				if (c1 == c)
					while (r1 + 1 < h && r1 - r + 1 < cap
						&& grid[(r1 + 1) * w + c] == glyph && !used[(r1 + 1) * w + c]) r1++;

				for (int rr = r; rr <= r1; rr++)
					for (int cc = c; cc <= c1; cc++)
						used[rr * w + cc] = true;

				// A lone cell takes its orientation from the wall it sits in:
				// wall above and below means a vertical wall.
				bool vertical = r1 > r;
				if (c1 == c && r1 == r)
				{
					bool up = r > 0 && grid[(r - 1) * w + c] == '#';
					bool down = r + 1 < h && grid[(r + 1) * w + c] == '#';
					bool left = c > 0 && grid[r * w + c - 1] == '#';
					bool right = c + 1 < w && grid[r * w + c + 1] == '#';
					vertical = up && down && !(left && right);
				}

				outp.Add(new PanelDef
				{
					Kind = kind,
					Rect = new Rect(c * CellFx, r * CellFx,
						(c1 - c + 1) * CellFx, (r1 - r + 1) * CellFx),
					Vertical = vertical,
				});
			}
		}
		return outp;
	}

	public void Build()
	{
		Walls = MergeWalls(Grid, W, H);

		// Glass first, then doors, each row-major. The order is part of the
		// replay format -- a DoorPick names a panel by index -- so it must never
		// depend on anything but the grid.
		Panels = MergePanels(Grid, W, H, GlassGlyph, PanelKind.Glass, GlassPaneCells);
		Panels.AddRange(MergePanels(Grid, W, H, DoorGlyph, PanelKind.Door, DoorLeafCells));
		_nav = null;
		Caches = new List<CacheDef>();
		Guards = new List<GuardDef>();
		Chests = new List<ChestDef>();
		Objectives = 0;
		SweepNodes = new List<(int C, int R)>();

		int ex0 = int.MaxValue, ey0 = int.MaxValue, ex1 = int.MinValue, ey1 = int.MinValue;
		bool gotSpawn = false;

		for (int r = 0; r < H; r++)
		{
			for (int c = 0; c < W; c++)
			{
				char ch = At(c, r);
				int px = c * CellFx + CellFx / 2;
				int py = r * CellFx + CellFx / 2;

				if (ch == '@') { SpawnX = px; SpawnY = py; gotSpawn = true; }
				else if (ch == SweepGlyph) SweepNodes.Add((c, r));
				else if (ch == 'X')
				{
					if (c * CellFx < ex0) ex0 = c * CellFx;
					if (r * CellFx < ey0) ey0 = r * CellFx;
					if (c * CellFx + CellFx > ex1) ex1 = c * CellFx + CellFx;
					if (r * CellFx + CellFx > ey1) ey1 = r * CellFx + CellFx;
				}
				else if (ch == '$')
				{
					Caches.Add(new CacheDef { X = px, Y = py,
						Tiers = new[] { Tune.CacheRecordTier } });
				}
				else if (ch == 'C')
				{
					Chests.Add(new ChestDef { X = px, Y = py });
				}
				else if (ch == '!')
				{
					// The objective site. A chest that happens to hold the one
					// thing the mission is about, so it loots through exactly
					// the machinery a chest already does.
					Chests.Add(new ChestDef { X = px, Y = py, Objective = true });
					Objectives++;
				}
				else if (ch >= GuardFirst && ch <= GuardLast)
				{
					// CAPPED. The glyph range allows 26 distinct guards but
					// nothing stopped a file from repeating them: MaxGuards was
					// a constant with no consumer, and a grid may hold MaxCells
					// characters, so a malformed or hand-edited level could
					// parse into thousands of guards in a list the sim walks
					// every tick. Extras are skipped rather than refused, the
					// way this parser treats everything else it cannot honour.
					if (Guards.Count < MaxGuards)
						Guards.Add(new GuardDef { Id = ch, X = px, Y = py });
				}
			}
		}

		if (!gotSpawn) { SpawnX = CellFx * 3 / 2; SpawnY = CellFx * 3 / 2; }

		Exit = ex1 > int.MinValue
			? new Rect(ex0, ey0, ex1 - ex0, ey1 - ey0)
			: new Rect((W - 3) * CellFx, (H - 3) * CellFx, CellFx * 2, CellFx * 2);

		// Tier sets are derived from the guard letter, and a guard with no
		// matching route line is a stationary sentry by design (prototype
		// parity).
		//
		// What is NOT prototype parity: most guards now carry nothing. See
		// Tune.GuardRecordEvery — records were made scarce on purpose, and a
		// body being empty of evidence is the main way that is felt.
		foreach (var g in Guards)
		{
			int slot = g.Id - 'a';
			if (slot >= 0 && slot % Tune.GuardRecordEvery == 0)
			{
				var ladder = new[] { 2, 1, 3 };
				g.Tiers = new[] { ladder[(slot / Tune.GuardRecordEvery) % 3] };
			}
			else
			{
				g.Tiers = System.Array.Empty<int>();
			}

			if (Routes.TryGetValue(g.Id, out var pts) && pts.Count > 0)
			{
				g.PathX = new int[pts.Count];
				g.PathY = new int[pts.Count];
				for (int i = 0; i < pts.Count; i++)
				{
					g.PathX[i] = pts[i].C * CellFx + CellFx / 2;
					g.PathY[i] = pts[i].R * CellFx + CellFx / 2;
				}
			}
		}
	}

	/// <summary>
	/// Four-way flood fill from spawn across every non-wall cell, asking whether
	/// the exit is reachable at all. Used by the editor to catch a sealed-off
	/// exit at authoring time, and by the harness to assert the reference level
	/// is completable. Guards steer rather than path-find (spec §10.1), so this
	/// is a lower bound on playability, not a guarantee.
	///
	/// Doors count as passable -- any door can be opened. Glass counts as
	/// passable too unless <paramref name="glassBlocks"/>: a pane can always be
	/// shot out, so a level whose only route is through a window IS completable,
	/// just loudly. The editor asks both ways and says which it is.
	/// </summary>
	public bool ExitReachable(bool glassBlocks = false)
	{
		int sc = SpawnX / CellFx, sr = SpawnY / CellFx;
		if (!InBounds(sc, sr)) return false;
		if (Grid[sr * W + sc] == '#') return false;

		int ec0 = Exit.X / CellFx, er0 = Exit.Y / CellFx;
		int ec1 = (Exit.X + Exit.W) / CellFx, er1 = (Exit.Y + Exit.H) / CellFx;

		var seen = new bool[W * H];
		var queue = new Queue<int>();
		int start = sr * W + sc;
		seen[start] = true;
		queue.Enqueue(start);

		while (queue.Count > 0)
		{
			int cur = queue.Dequeue();
			int c = cur % W, r = cur / W;
			if (c >= ec0 && c < ec1 && r >= er0 && r < er1) return true;

			TryPush(queue, seen, c + 1, r, glassBlocks);
			TryPush(queue, seen, c - 1, r, glassBlocks);
			TryPush(queue, seen, c, r + 1, glassBlocks);
			TryPush(queue, seen, c, r - 1, glassBlocks);
		}
		return false;
	}

	private void TryPush(Queue<int> queue, bool[] seen, int c, int r, bool glassBlocks)
	{
		if (!InBounds(c, r)) return;
		int i = r * W + c;
		if (seen[i] || Grid[i] == '#') return;
		if (glassBlocks && Grid[i] == GlassGlyph) return;
		seen[i] = true;
		queue.Enqueue(i);
	}

	public void HashInto(ref Hash64 h)
	{
		for (int i = 0; i < Grid.Length; i++) h.Add(Grid[i]);
		h.Add(Walls.Length);
		h.Add(SpawnX); h.Add(SpawnY);
		h.Add(Exit.X); h.Add(Exit.Y); h.Add(Exit.W); h.Add(Exit.H);
	}
}
