using System.Collections.Generic;

namespace Cognitohazard.Sim;

/// <summary>
/// A* over a <see cref="NavGrid"/> (Guard_AI.md §8.2). Integer costs 10/14,
/// octile heuristic, and an open list ordered by (f, h, cell index), so equal
/// paths are always broken the same way: the result is a function of the grid
/// and the two cells, never of allocation or iteration order.
///
/// Holds only scratch: per-cell arrays sized once and reused through a
/// generation stamp, so a search allocates nothing but its output. One per
/// SimWorld. Two worlds never share one, though sharing would still be
/// deterministic.
/// </summary>
public sealed class PathFinder
{
	private readonly NavGrid _nav;
	private readonly int[] _g, _parent, _seen, _closed;
	private int _gen;

	private int[] _hf = new int[256], _hh = new int[256], _hc = new int[256];
	private int _hn;

	private readonly List<int> _cells = new();
	private readonly List<int> _candX = new(), _candY = new();

	/// <summary>Nodes expanded by the last search. Diagnostic only.</summary>
	public int Expanded { get; private set; }

	/// <summary>
	/// The longest straight run <see cref="Smooth"/> will pull, in candidate
	/// points (about a cell each). Bounds the cost of smoothing a long corridor,
	/// where every check re-samples the whole run so far, at the price of a
	/// redundant waypoint every 16 cells, which a guard walks through without
	/// noticing.
	/// </summary>
	public const int SmoothMaxRun = 16;

	public PathFinder(NavGrid nav)
	{
		_nav = nav;
		int n = nav.W * nav.H;
		_g = new int[n]; _parent = new int[n]; _seen = new int[n]; _closed = new int[n];
	}

	public NavGrid Nav => _nav;

	private int Heuristic(int a, int b)
	{
		int dx = a % _nav.W - b % _nav.W, dy = a / _nav.W - b / _nav.W;
		if (dx < 0) dx = -dx;
		if (dy < 0) dy = -dy;
		int lo = dx < dy ? dx : dy, hi = dx < dy ? dy : dx;
		return NavGrid.CostStraight * hi + (NavGrid.CostDiagonal - NavGrid.CostStraight) * lo;
	}

	/// <summary>
	/// Cheapest cell path from <paramref name="start"/> to <paramref name="goal"/>,
	/// both inclusive, into <paramref name="outCells"/>. Returns its cost, or -1
	/// when there is none. <paramref name="penalty"/>, if given, is added to the
	/// cost of entering each cell (flanking, Guard_AI.md §5.5); it must not be
	/// negative or the heuristic stops being admissible.
	/// </summary>
	public int Search(int start, int goal, List<int> outCells, int[]? penalty = null)
	{
		outCells.Clear();
		Expanded = 0;
		int n = _nav.W * _nav.H;
		if (start < 0 || goal < 0 || start >= n || goal >= n) return -1;
		if (!_nav.Passable[start] || !_nav.Passable[goal]) return -1;
		if (_nav.Region[start] != _nav.Region[goal]) return -1;

		if (++_gen == int.MaxValue)
		{
			System.Array.Clear(_seen);
			System.Array.Clear(_closed);
			_gen = 1;
		}
		_hn = 0;

		_seen[start] = _gen; _g[start] = 0; _parent[start] = -1;
		int h0 = Heuristic(start, goal);
		Push(h0, h0, start);

		while (_hn > 0)
		{
			Pop(out _, out _, out int cur);
			if (_closed[cur] == _gen) continue;      // a stale duplicate
			_closed[cur] = _gen;
			Expanded++;

			if (cur == goal)
			{
				for (int c = goal; c >= 0; c = _parent[c]) outCells.Add(c);
				outCells.Reverse();
				return _g[goal];
			}

			byte edges = _nav.Edges[cur];
			for (int d = 0; d < 8; d++)
			{
				if ((edges & (1 << d)) == 0) continue;
				int nb = _nav.Step(cur, d);
				if (_closed[nb] == _gen) continue;
				int ng = _g[cur] + NavGrid.CostOf(d) + (penalty != null ? penalty[nb] : 0);
				if (_seen[nb] == _gen && ng >= _g[nb]) continue;
				_seen[nb] = _gen; _g[nb] = ng; _parent[nb] = cur;
				int h = Heuristic(nb, goal);
				Push(ng + h, h, nb);
			}
		}
		return -1;
	}

	/// <summary>
	/// Path cost from <paramref name="start"/> to every cell within
	/// <paramref name="maxCost"/> (Dijkstra, no goal), read back with
	/// <see cref="FloodCost"/> until the next search or flood. One flood answers
	/// "how far is every guard from here" (allies, responders, Guard_AI.md
	/// §5.2–5.3), where a straight-line distance would count a guard behind a
	/// wall as next door.
	/// </summary>
	public void Flood(int start, int maxCost)
	{
		Expanded = 0;
		if (++_gen == int.MaxValue)
		{
			System.Array.Clear(_seen);
			System.Array.Clear(_closed);
			_gen = 1;
		}
		_hn = 0;
		int n = _nav.W * _nav.H;
		if (start < 0 || start >= n || !_nav.Passable[start]) return;

		_seen[start] = _gen; _g[start] = 0; _parent[start] = -1;
		Push(0, 0, start);
		while (_hn > 0)
		{
			Pop(out int f, out _, out int cur);
			if (_closed[cur] == _gen) continue;
			if (f > maxCost) break;
			_closed[cur] = _gen;
			Expanded++;

			byte edges = _nav.Edges[cur];
			for (int d = 0; d < 8; d++)
			{
				if ((edges & (1 << d)) == 0) continue;
				int nb = _nav.Step(cur, d);
				if (_closed[nb] == _gen) continue;
				int ng = _g[cur] + NavGrid.CostOf(d);
				if (_seen[nb] == _gen && ng >= _g[nb]) continue;
				_seen[nb] = _gen; _g[nb] = ng; _parent[nb] = cur;
				Push(ng, 0, nb);
			}
		}
	}

	/// <summary>Cost to <paramref name="cell"/> from the last <see cref="Flood"/>,
	/// or -1 if it was not reached within the limit.</summary>
	public int FloodCost(int cell)
		=> cell >= 0 && cell < _closed.Length && _closed[cell] == _gen ? _g[cell] : -1;

	/// <summary>
	/// A path a guard can walk from (sx, sy) to (gx, gy), as fixed-point
	/// waypoints, the last being the goal itself. Straight when the straight
	/// line is clear; otherwise A* between the nearest passable cells, then
	/// string-pulled. Returns false, leaving the lists empty, when the goal is
	/// unreachable. The caller then steers straight at it, which is all guards
	/// ever did before.
	/// </summary>
	public bool Plan(int sx, int sy, int gx, int gy, bool allowShortcut,
		List<int> outX, List<int> outY, out bool searched)
	{
		outX.Clear(); outY.Clear();
		searched = false;

		if (allowShortcut && _nav.SegmentClear(sx, sy, gx, gy))
		{
			outX.Add(gx); outY.Add(gy);
			return true;
		}

		int sc = _nav.NearestPassable(sx, sy);
		int gc = _nav.NearestPassable(gx, gy);
		if (sc < 0 || gc < 0 || _nav.Region[sc] != _nav.Region[gc]) return false;

		searched = true;
		if (Search(sc, gc, _cells) < 0) return false;
		Smooth(_cells, sx, sy, gx, gy, outX, outY);
		return true;
	}

	/// <summary>
	/// String-pull a cell path. Candidates are every cell's node point, then the
	/// goal; from each anchor the run is extended while the straight segment to
	/// the next candidate stays clear, and the last clear one becomes a
	/// waypoint. Greedy and forward-only, so it costs one segment check per
	/// candidate rather than one per pair.
	/// </summary>
	public void Smooth(List<int> cells, int sx, int sy, int gx, int gy, List<int> outX, List<int> outY)
	{
		outX.Clear(); outY.Clear();
		_candX.Clear(); _candY.Clear();
		for (int i = 0; i < cells.Count; i++) { _candX.Add(_nav.NodeX[cells[i]]); _candY.Add(_nav.NodeY[cells[i]]); }
		_candX.Add(gx); _candY.Add(gy);

		int ax = sx, ay = sy;
		int k = 0;
		while (k < _candX.Count)
		{
			// Candidate k is taken even if it is not clear from the anchor. That
			// only happens on the first leg, from wherever the guard actually is
			// to its own cell's node, and MoveSlide handles a graze.
			int j = k;
			while (j + 1 < _candX.Count && j + 1 - k < SmoothMaxRun
				&& _nav.SegmentClear(ax, ay, _candX[j + 1], _candY[j + 1]))
				j++;
			outX.Add(_candX[j]); outY.Add(_candY[j]);
			ax = _candX[j]; ay = _candY[j];
			k = j + 1;
		}
	}

	// ------------------------------------------------------------ binary heap

	private static bool Less(int fa, int ha, int ca, int fb, int hb, int cb)
		=> fa != fb ? fa < fb : (ha != hb ? ha < hb : ca < cb);

	private void Push(int f, int h, int c)
	{
		if (_hn == _hf.Length)
		{
			System.Array.Resize(ref _hf, _hn * 2);
			System.Array.Resize(ref _hh, _hn * 2);
			System.Array.Resize(ref _hc, _hn * 2);
		}
		int i = _hn++;
		while (i > 0)
		{
			int p = (i - 1) >> 1;
			if (!Less(f, h, c, _hf[p], _hh[p], _hc[p])) break;
			_hf[i] = _hf[p]; _hh[i] = _hh[p]; _hc[i] = _hc[p];
			i = p;
		}
		_hf[i] = f; _hh[i] = h; _hc[i] = c;
	}

	private void Pop(out int f, out int h, out int c)
	{
		f = _hf[0]; h = _hh[0]; c = _hc[0];
		_hn--;
		if (_hn == 0) return;
		int lf = _hf[_hn], lh = _hh[_hn], lc = _hc[_hn];
		int i = 0;
		while (true)
		{
			int a = 2 * i + 1;
			if (a >= _hn) break;
			int b = a + 1;
			int m = (b < _hn && Less(_hf[b], _hh[b], _hc[b], _hf[a], _hh[a], _hc[a])) ? b : a;
			if (!Less(_hf[m], _hh[m], _hc[m], lf, lh, lc)) break;
			_hf[i] = _hf[m]; _hh[i] = _hh[m]; _hc[i] = _hc[m];
			i = m;
		}
		_hf[i] = lf; _hh[i] = lh; _hc[i] = lc;
	}
}
