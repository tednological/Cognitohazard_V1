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
	/// The guard ALPHABET, in order: 'a' to 'z', then the 62 Latin-1 letters
	/// 'À' to 'ÿ' (U+00C0..U+00FF without × and ÷). One glyph is one guard: his
	/// route line, his kit line and his records are keyed by it.
	///
	/// Twenty-six was the cap on how dangerous ANY level could be, as eight
	/// was before it. The extension stays inside ONE BYTE on purpose: the grid
	/// crosses to game/ as a byte per cell (SimBridge.GetGrid), and every glyph
	/// the level art and editor read is a byte value. Level files are UTF-8, so
	/// 'Ä' is two bytes on disk and one char here.
	/// </summary>
	public static readonly string GuardGlyphs = BuildGuardGlyphs();

	private static string BuildGuardGlyphs()
	{
		var sb = new StringBuilder();
		for (char c = 'a'; c <= 'z'; c++) sb.Append(c);
		for (int c = 0xC0; c <= 0xFF; c++)
			if (c != 0xD7 && c != 0xF7) sb.Append((char)c);
		return sb.ToString();
	}

	/// <summary>Glyph -> slot in GuardGlyphs, -1 for anything that is not a
	/// guard. 256 entries: no guard glyph is wider than a byte.</summary>
	private static readonly int[] GuardSlots = BuildGuardSlots();

	private static int[] BuildGuardSlots()
	{
		var slots = new int[256];
		for (int i = 0; i < slots.Length; i++) slots[i] = -1;
		for (int i = 0; i < GuardGlyphs.Length; i++) slots[GuardGlyphs[i]] = i;
		return slots;
	}

	/// <summary>
	/// The most guards one level may hold. ENFORCED by FromText, which skips
	/// glyphs past it — the count is otherwise bounded only by MaxCells.
	/// </summary>
	public static int MaxGuards => GuardGlyphs.Length;

	public static bool IsGuardGlyph(char ch) => ch < 256 && GuardSlots[ch] >= 0;

	/// <summary>A guard glyph's place in the alphabet ('a' is 0, 'z' 25, 'À'
	/// 26), or -1. What Tune.GuardRecordEvery counts in.</summary>
	public static int GuardSlot(char ch) => ch < 256 ? GuardSlots[ch] : -1;

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
	/// A ceiling lamp over this cell (cognitohazard_lighting_plan.md §5.1).
	/// FLOOR for walking, nav, sight and rounds; it only lights. A round that
	/// passes within Tune.LampHitRadius shatters it.
	/// </summary>
	public const char LampGlyph = 'L';

	/// <summary>
	/// A light switch on this floor cell (lighting plan §5.2). G in reach flips
	/// every lamp in its ROOM: the 4-connected region of floor bounded by walls,
	/// glass and doors. No wiring to author; see <see cref="RoomOf"/>.
	/// </summary>
	public const char SwitchGlyph = 'S';

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

	/// <summary>
	/// <c>theme: industrial</c>. Which map kit game/level_art.gd dresses the
	/// floor in. PRESENTATION ONLY and deliberately not hashed (see HashInto):
	/// it names art, never a rule, so re-theming a level moves no golden hash
	/// and replays recorded under another theme still verify. Empty means "not
	/// authored" and writes nothing on save, so an older level round-trips byte
	/// for byte; game/ picks its default. The sim only carries it so the editor
	/// can save it back.
	/// </summary>
	public string Theme = "";

	/// <summary>Longest theme token kept. The parser stays total: anything
	/// longer, or any character outside [a-z0-9_], is cut rather than refused.</summary>
	public const int MaxThemeLength = 32;

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

	/// <summary>The FIRST exit (see <see cref="Exits"/>). On a level with one
	/// exit this is the rect it always was: the bounding box of every 'X'.</summary>
	public Rect Exit;

	/// <summary>
	/// Every way out: one rect per 8-connected blob of 'X' cells, its bounding
	/// box, in row-major order of the blob's first cell. Reaching ANY of them
	/// ends the run. Derived from the grid, which is hashed, so the list needs
	/// no hashing of its own. Capped at <see cref="MaxExits"/>: the parser is
	/// total, and a grid sprayed with X must not become thousands of rects the
	/// player is tested against every tick. Blobs past the cap are floor to
	/// every rule, and the editor says so.
	/// </summary>
	public List<Rect> Exits = new();

	public const int MaxExits = 8;

	/// <summary>
	/// The exit the sweep's exit group keeps to (Guard_AI.md §6.3): the one
	/// nearest the first objective site as the crow flies, since that is the way
	/// out someone carrying it is likeliest to take. Ties go to the earlier
	/// exit. With one exit, or no objective, it is <see cref="Exit"/>.
	/// </summary>
	public Rect WatchedExit;
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

	// ------------------------------------------------------------ lighting
	//
	// cognitohazard_lighting_plan.md. -1 means "not authored", which is FULLY
	// LIT and writes nothing on save: every level made before lighting existed
	// is lit everywhere, and round-trips byte for byte.

	/// <summary><c>ambient: N</c>, percent 0..100. The level's base light.</summary>
	public int Ambient = -1;

	/// <summary>Ambient as the sim uses it: Q8, 0..256. Not authored is 256.</summary>
	public int AmbientQ8 => Ambient < 0 ? Fx.One : Ambient * Fx.One / 100;

	/// <summary>Lamps ('L'), as cells, row-major: that index is the lamp's
	/// identity in the state hash and the snapshot.</summary>
	public List<(int C, int R)> Lamps = new();

	/// <summary>Light switches ('S'), as cells, row-major. A switch's pick is
	/// Panels.Count + its index + 1 in InputFrame.DoorPick (the `u` token).</summary>
	public List<(int C, int R)> Switches = new();

	/// <summary>
	/// A cell light and sight cannot pass: wall, or a door as authored (shut).
	/// Panels are resolved per run by SimWorld; this is the level at rest.
	/// </summary>
	public bool OpaqueAtRest(int c, int r)
	{
		char ch = At(c, r);
		return ch == '#' || ch == DoorGlyph;
	}

	/// <summary>
	/// The ROOM a cell stands in, for a light switch: the 4-connected region of
	/// cells that are neither wall, glass nor door. Row-major BFS, so the result
	/// is a pure function of the grid. Returns a W*H mask; empty for a cell that
	/// is itself a boundary.
	/// </summary>
	public bool[] RoomOf(int c, int r)
	{
		var seen = new bool[W * H];
		if (!InBounds(c, r) || BoundsRoom(Grid[r * W + c])) return seen;
		var queue = new Queue<int>();
		seen[r * W + c] = true;
		queue.Enqueue(r * W + c);
		while (queue.Count > 0)
		{
			int cur = queue.Dequeue();
			int cc = cur % W, rr = cur / W;
			RoomPush(queue, seen, cc + 1, rr);
			RoomPush(queue, seen, cc - 1, rr);
			RoomPush(queue, seen, cc, rr + 1);
			RoomPush(queue, seen, cc, rr - 1);
		}
		return seen;
	}

	private static bool BoundsRoom(char ch) => ch == '#' || ch == GlassGlyph || ch == DoorGlyph;

	private void RoomPush(Queue<int> queue, bool[] seen, int c, int r)
	{
		if (!InBounds(c, r)) return;
		int i = r * W + c;
		if (seen[i] || BoundsRoom(Grid[i])) return;
		seen[i] = true;
		queue.Enqueue(i);
	}

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
		// Lighting shares the rule: a header line with a colon, any mode.
		if (StartsWithNoCase(line, "ambient:"))
		{
			if (L != null)
			{
				int a = ParseAmount(line.Substring(8));
				L.Ambient = a < 0 ? -1 : (a > 100 ? 100 : a);
			}
			return true;
		}
		return false;
	}

	/// <summary>A non-negative amount, clamped to MaxLoot; -1 for garbage, which
	/// the caller treats as "not authored".</summary>
	private static int ParseAmount(string s)
	{
		if (!Invariant.TryLong(s.Trim(), out long v)) return -1;
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
			if (StartsWithNoCase(line, "theme:"))
			{
				L.Theme = CleanTheme(line.Substring(6));
				continue;
			}
			if (StartsWithNoCase(line, "name:"))
			{
				string n = line.Substring(5).Trim();
				L.Name = n.Length == 0 ? "untitled" : n;
				continue;
			}
			if (StartsWithNoCase(line, "grid:")) { mode = "grid"; row = 0; continue; }

			string t = line.Trim();
			if (t.StartsWith('>'))
			{
				mode = "routes";
				ParseRoute(L, t);
				continue;
			}
			if (t.StartsWith('#') && mode != "grid") continue;   // comment before the grid

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
			if (StartsWithNoCase(line, "theme:")) continue;
			if (ParseLootLine(null, line)) continue;
			if (StartsWithNoCase(line, "grid:"))
			{
				mode = "grid"; widest = 0; rows = 0; lastContentRow = 0; continue;
			}

			string t = line.Trim();
			if (t.StartsWith('>')) { mode = "routes"; continue; }
			if (t.StartsWith('#') && mode != "grid") continue;

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

	/// <summary>A theme token as the parser keeps it: trimmed, lower case,
	/// [a-z0-9_] only, at most MaxThemeLength. Garbage becomes "", which is
	/// "not authored", never an exception.</summary>
	public static string CleanTheme(string? raw)
	{
		if (raw == null) return "";
		var sb = new StringBuilder();
		foreach (char ch0 in raw.Trim())
		{
			char ch = char.ToLowerInvariant(ch0);
			if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_')
			{
				sb.Append(ch);
				if (sb.Length >= MaxThemeLength) break;
			}
			else break;
		}
		return sb.ToString();
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
			if (!Invariant.TryInt(q[0], out int cc)) continue;
			if (!Invariant.TryInt(q[1], out int rr)) continue;
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
		+ "  C chest  ! objective  a-z guard start  * sweep node  L lamp  S switch";

	public string ToText()
	{
		var sb = new StringBuilder();
		var inv = System.Globalization.CultureInfo.InvariantCulture;   // see Invariant
		sb.Append("name: ").Append(Name).Append('\n');
		if (Theme.Length > 0) sb.Append("theme: ").Append(Theme).Append('\n');
		if (LootBudget >= 0) sb.Append(inv, $"loot: {LootBudget}\n");
		if (GuardLoot >= 0) sb.Append(inv, $"guard_loot: {GuardLoot}\n");
		if (Ambient >= 0) sb.Append(inv, $"ambient: {Ambient}\n");
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
			foreach (var p in kv.Value) sb.Append(inv, $" {p.C},{p.R}");
			sb.Append('\n');
		}
		foreach (var kv in KitPoints)       // SortedDictionary: stable id order
			sb.Append(inv, $"kit: {kv.Key} {kv.Value}\n");
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
		Lamps = new List<(int C, int R)>();
		Switches = new List<(int C, int R)>();

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
				else if (ch == LampGlyph) Lamps.Add((c, r));
				else if (ch == SwitchGlyph) Switches.Add((c, r));
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
				else if (IsGuardGlyph(ch))
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

		Exits = FindExits();
		if (Exits.Count == 0)
			Exits.Add(new Rect((W - 3) * CellFx, (H - 3) * CellFx, CellFx * 2, CellFx * 2));
		Exit = Exits[0];
		WatchedExit = Exit;
		foreach (var ch in Chests)
		{
			if (!ch.Objective) continue;
			long best = long.MaxValue;
			foreach (var e in Exits)
			{
				long dx = e.X + e.W / 2 - ch.X, dy = e.Y + e.H / 2 - ch.Y;
				long d = dx * dx + dy * dy;
				if (d < best) { best = d; WatchedExit = e; }
			}
			break;
		}

		// Tier sets are derived from the guard letter, and a guard with no
		// matching route line is a stationary sentry by design (prototype
		// parity).
		//
		// What is NOT prototype parity: most guards now carry nothing. See
		// Tune.GuardRecordEvery — records were made scarce on purpose, and a
		// body being empty of evidence is the main way that is felt.
		foreach (var g in Guards)
		{
			int slot = GuardSlot(g.Id);
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
	/// The 'X' blobs, 8-connected, as bounding rects in row-major order of
	/// each blob's first cell; at most MaxExits. The fill order inside a blob
	/// cannot matter: a bounding box is the same whichever cell is seen first.
	/// </summary>
	private List<Rect> FindExits()
	{
		var outp = new List<Rect>();
		var seen = new bool[W * H];
		var stack = new Stack<int>();
		for (int i = 0; i < Grid.Length && outp.Count < MaxExits; i++)
		{
			if (Grid[i] != 'X' || seen[i]) continue;
			int c0 = i % W, r0 = i / W, c1 = c0, r1 = r0;
			seen[i] = true;
			stack.Push(i);
			while (stack.Count > 0)
			{
				int cur = stack.Pop();
				int c = cur % W, r = cur / W;
				if (c < c0) c0 = c;
				if (c > c1) c1 = c;
				if (r < r0) r0 = r;
				if (r > r1) r1 = r;
				for (int dr = -1; dr <= 1; dr++)
				{
					for (int dc = -1; dc <= 1; dc++)
					{
						int nc = c + dc, nr = r + dr;
						if (!InBounds(nc, nr)) continue;
						int n = nr * W + nc;
						if (seen[n] || Grid[n] != 'X') continue;
						seen[n] = true;
						stack.Push(n);
					}
				}
			}
			outp.Add(new Rect(c0 * CellFx, r0 * CellFx, (c1 - c0 + 1) * CellFx, (r1 - r0 + 1) * CellFx));
		}
		return outp;
	}

	/// <summary>
	/// Four-way flood fill from spawn across every non-wall cell, asking whether
	/// ANY exit is reachable at all (<paramref name="which"/> -1), or one exit by
	/// its index in <see cref="Exits"/>. Used by the editor to catch a sealed-off
	/// exit at authoring time, and by the harness to assert the reference level
	/// is completable. Guards steer rather than path-find (spec §10.1), so this
	/// is a lower bound on playability, not a guarantee.
	///
	/// Doors count as passable -- any door can be opened. Glass counts as
	/// passable too unless <paramref name="glassBlocks"/>: a pane can always be
	/// shot out, so a level whose only route is through a window IS completable,
	/// just loudly. The editor asks both ways and says which it is.
	/// </summary>
	public bool ExitReachable(bool glassBlocks = false, int which = -1)
	{
		int sc = SpawnX / CellFx, sr = SpawnY / CellFx;
		if (!InBounds(sc, sr)) return false;
		if (Grid[sr * W + sc] == '#') return false;

		var goal = new bool[W * H];
		for (int k = 0; k < Exits.Count; k++)
		{
			if (which >= 0 && k != which) continue;
			var e = Exits[k];
			for (int r = e.Y / CellFx; r < e.Y1 / CellFx; r++)
				for (int c = e.X / CellFx; c < e.X1 / CellFx; c++)
					if (InBounds(c, r)) goal[r * W + c] = true;
		}

		var seen = new bool[W * H];
		var queue = new Queue<int>();
		int start = sr * W + sc;
		seen[start] = true;
		queue.Enqueue(start);

		while (queue.Count > 0)
		{
			int cur = queue.Dequeue();
			if (goal[cur]) return true;
			int c = cur % W, r = cur / W;

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

	/// <summary>Theme is NOT hashed: it is art, and a level re-dressed in the
	/// other kit is the same level to every rule.</summary>
	public void HashInto(ref Hash64 h)
	{
		for (int i = 0; i < Grid.Length; i++) h.Add(Grid[i]);
		h.Add(Walls.Length);
		h.Add(SpawnX); h.Add(SpawnY);
		h.Add(Exit.X); h.Add(Exit.Y); h.Add(Exit.W); h.Add(Exit.H);
	}
}
