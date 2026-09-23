using System.Collections.Generic;

namespace Cognitohazard.Sim;

/// <summary>
/// Where a guard's body can stand and which way it can step, derived from the
/// level grid (Guard_AI.md §8.1). Lifts spec §10.1: guards used to steer
/// straight at a target and grind into corners.
///
/// ONE rule decides everything here: the circle test <see cref="CircleHits"/>,
/// which is <see cref="Geometry.HitsWall"/> asked of the blocking cells near a
/// point instead of every merged rect. The two are equivalent because the walls
/// ARE the union of the '#' cells (<see cref="Level.MergeWalls"/>), and
/// tests/Navigation.cs asserts it on every shipped level. So the nav grid can
/// never claim a route that <see cref="Geometry.MoveSlide"/> then refuses.
/// Glass counts as wall here too (<see cref="BlocksNav"/>), which only ever
/// makes the nav MORE careful than the walls alone.
///
/// Derived from the level and never mutated, so it feeds no hash. Built
/// lazily by <see cref="Level.Nav"/>: most SimWorlds (every loadout in
/// Exhaustive, every parse in Robustness) never move a guard.
/// </summary>
public sealed class NavGrid
{
	public readonly int W, H;

	/// <summary>The body radius every node and edge was validated for.</summary>
	public readonly int Radius;

	/// <summary>
	/// Radius used for clearance checks: the body plus one pixel, so a guard
	/// following a validated segment has a pixel to spare against rounding and
	/// against the 4 px sampling in <see cref="SegmentClear"/>.
	/// </summary>
	public readonly int ClearR;

	private readonly bool[] _wall;

	/// <summary>A guard can stand on this cell's node point.</summary>
	public readonly bool[] Passable;

	/// <summary>
	/// Where a guard stands in each cell, fixed-point. The centre, pushed off an
	/// orthogonally adjacent wall: a cell centre is 10 px from its edge and a
	/// guard is 11 px wide, so without the push every cell touching a wall would
	/// be refused and a two-cell corridor, which a guard fits through with room
	/// to spare, would be sealed.
	/// </summary>
	public readonly int[] NodeX, NodeY;

	/// <summary>Bit d set: stepping in direction d (see <see cref="DC"/>) is legal.</summary>
	public readonly byte[] Edges;

	/// <summary>Connected region of each passable cell, -1 otherwise. A goal
	/// in another region is refused at once instead of flooding the level.</summary>
	public readonly int[] Region;

	/// <summary>East, then clockwise (y is down). Even d orthogonal, odd diagonal.</summary>
	public static readonly int[] DC = { 1, 1, 0, -1, -1, -1, 0, 1 };
	public static readonly int[] DR = { 0, 1, 1, 1, 0, -1, -1, -1 };

	public const int CostStraight = 10;
	public const int CostDiagonal = 14;

	/// <summary>Spacing of the circle samples along a segment.</summary>
	public const int SampleStep = 4 * Fx.One;

	public NavGrid(Level level) : this(level, Tune.ActorRadius) { }

	public NavGrid(Level level, int radius)
	{
		W = level.W; H = level.H;
		Radius = radius;
		ClearR = radius + Fx.One;

		int n = W * H;
		_wall = new bool[n];
		for (int i = 0; i < n; i++) _wall[i] = BlocksNav(level.Grid[i]);

		Passable = new bool[n];
		NodeX = new int[n];
		NodeY = new int[n];
		Edges = new byte[n];
		Region = new int[n];

		// Past half a cell the push below no longer describes the geometry.
		// Nothing in the game is that large; refuse loudly rather than build a
		// grid that quietly lies.
		int push = ClearR - Level.CellFx / 2;
		if (push < 0) push = 0;
		if (push >= Level.CellFx / 2) throw new System.ArgumentOutOfRangeException(nameof(radius));

		for (int r = 0; r < H; r++)
		{
			for (int c = 0; c < W; c++)
			{
				int i = r * W + c;
				int cx = c * Level.CellFx + Level.CellFx / 2;
				int cy = r * Level.CellFx + Level.CellFx / 2;
				NodeX[i] = cx; NodeY[i] = cy;
				if (_wall[i]) continue;

				bool wW = IsWall(c - 1, r), wE = IsWall(c + 1, r);
				bool wN = IsWall(c, r - 1), wS = IsWall(c, r + 1);
				if ((wW && wE) || (wN && wS)) continue;   // a one-cell gap: 20 px, and a guard is 22

				int px = cx + (wW ? push : (wE ? -push : 0));
				int py = cy + (wN ? push : (wS ? -push : 0));
				if (!CircleHits(px, py, ClearR)) { NodeX[i] = px; NodeY[i] = py; Passable[i] = true; }
				else if (!CircleHits(cx, cy, ClearR)) Passable[i] = true;
			}
		}

		// Edges are decided once per pair, from the lower-indexed cell (d = 0..3
		// all point to a higher index), and mirrored. The graph is therefore
		// symmetric by construction; sampling a segment from each end could
		// otherwise round differently and make A to B legal but B to A not.
		for (int r = 0; r < H; r++)
		{
			for (int c = 0; c < W; c++)
			{
				int i = r * W + c;
				if (!Passable[i]) continue;
				for (int d = 0; d < 4; d++)
				{
					int nc = c + DC[d], nr = r + DR[d];
					if (!InBounds(nc, nr)) continue;
					int j = nr * W + nc;
					if (!Passable[j]) continue;
					// No corner cutting: a diagonal needs both cells it squeezes past.
					if ((d & 1) == 1 && (!Passable[r * W + nc] || !Passable[nr * W + c])) continue;
					if (!SegmentClear(NodeX[i], NodeY[i], NodeX[j], NodeY[j])) continue;
					Edges[i] |= (byte)(1 << d);
					Edges[j] |= (byte)(1 << (d + 4));
				}
			}
		}

		for (int i = 0; i < n; i++) Region[i] = -1;
		int label = 0;
		var queue = new Queue<int>();
		for (int s = 0; s < n; s++)
		{
			if (!Passable[s] || Region[s] >= 0) continue;
			Region[s] = label;
			queue.Enqueue(s);
			while (queue.Count > 0)
			{
				int cur = queue.Dequeue();
				for (int d = 0; d < 8; d++)
				{
					if ((Edges[cur] & (1 << d)) == 0) continue;
					int nb = Step(cur, d);
					if (Region[nb] >= 0) continue;
					Region[nb] = label;
					queue.Enqueue(nb);
				}
			}
			label++;
		}
	}

	/// <summary>
	/// What a guard plans round. Walls, and GLASS ('='): a pane blocks movement,
	/// and one broken mid-run stays out of the plan for now rather than making
	/// the nav grid mutable state. A DOOR ('+') is walkable, because a guard
	/// opens a door he walks into.
	/// </summary>
	public static bool BlocksNav(char glyph) => glyph == '#' || glyph == '=';

	public bool InBounds(int c, int r) => c >= 0 && r >= 0 && c < W && r < H;

	/// <summary>Out of bounds is NOT a wall, matching HitsWall: no rect lives there.</summary>
	private bool IsWall(int c, int r) => InBounds(c, r) && _wall[r * W + c];

	public int Step(int cell, int d) => cell + DC[d] + DR[d] * W;

	public static int CostOf(int d) => (d & 1) == 0 ? CostStraight : CostDiagonal;

	private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

	/// <summary>Cell index under a fixed-point point, or -1 off the grid.</summary>
	public int CellAt(int x, int y)
	{
		int c = FloorDiv(x, Level.CellFx), r = FloorDiv(y, Level.CellFx);
		return InBounds(c, r) ? r * W + c : -1;
	}

	/// <summary>
	/// <see cref="Geometry.HitsWall"/> against only the '#' cells a circle could
	/// reach. Same closest-point clamp, same strict comparison, so the two agree
	/// exactly, not approximately.
	/// </summary>
	public bool CircleHits(int x, int y, int radius)
	{
		long rr = (long)radius * radius;
		int c0 = FloorDiv(x - radius, Level.CellFx), c1 = FloorDiv(x + radius, Level.CellFx);
		int r0 = FloorDiv(y - radius, Level.CellFx), r1 = FloorDiv(y + radius, Level.CellFx);
		if (c0 < 0) c0 = 0;
		if (r0 < 0) r0 = 0;
		if (c1 >= W) c1 = W - 1;
		if (r1 >= H) r1 = H - 1;

		for (int r = r0; r <= r1; r++)
		{
			for (int c = c0; c <= c1; c++)
			{
				if (!_wall[r * W + c]) continue;
				int x0 = c * Level.CellFx, y0 = r * Level.CellFx;
				int x1 = x0 + Level.CellFx, y1 = y0 + Level.CellFx;
				int cx = x < x0 ? x0 : (x > x1 ? x1 : x);
				int cy = y < y0 ? y0 : (y > y1 ? y1 : y);
				long dx = x - cx, dy = y - cy;
				if (dx * dx + dy * dy < rr) return true;
			}
		}
		return false;
	}

	/// <summary>
	/// A body of <see cref="ClearR"/> can travel the segment: circles sampled
	/// every <see cref="SampleStep"/>, both ends included. Between samples the
	/// swept circle can graze a corner by well under the one-pixel margin.
	/// </summary>
	public bool SegmentClear(int ax, int ay, int bx, int by)
	{
		int len = Fx.Dist(ax, ay, bx, by);
		int n = len / SampleStep + 1;
		long dx = bx - ax, dy = by - ay;
		for (int i = 0; i <= n; i++)
		{
			int x = ax + (int)(dx * i / n);
			int y = ay + (int)(dy * i / n);
			if (CircleHits(x, y, ClearR)) return false;
		}
		return true;
	}

	/// <summary>
	/// The passable cell a point should path from or to: its own cell if a
	/// guard can stand there, else the nearest passable node within three
	/// rings (ties to the lower index). -1 when there is none, and the caller
	/// falls back to steering straight, as guards did before this existed.
	/// </summary>
	public int NearestPassable(int x, int y)
	{
		int c = FloorDiv(x, Level.CellFx), r = FloorDiv(y, Level.CellFx);
		if (c < 0) c = 0; else if (c >= W) c = W - 1;
		if (r < 0) r = 0; else if (r >= H) r = H - 1;
		int own = r * W + c;
		if (Passable[own]) return own;

		int best = -1;
		long bestD = long.MaxValue;
		for (int rr = r - 3; rr <= r + 3; rr++)
		{
			for (int cc = c - 3; cc <= c + 3; cc++)
			{
				if (!InBounds(cc, rr)) continue;
				int i = rr * W + cc;
				if (!Passable[i]) continue;
				long d = Fx.DistSq(x, y, NodeX[i], NodeY[i]);
				if (d < bestD) { bestD = d; best = i; }   // row-major scan: first wins ties
			}
		}
		return best;
	}
}
