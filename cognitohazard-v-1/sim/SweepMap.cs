using System.Collections.Generic;

namespace Cognitohazard.Sim;

/// <summary>
/// Where a compromised level gets searched (Guard_AI.md §6.3): a set of NODES
/// covering the floor, each with a STALENESS that climbs until a guard has it
/// in view and a CLAIM so two groups do not walk to the same one. Replaces
/// spec §10.3's uniformly random search for the level-wide sweep.
///
/// Nodes come from two places:
/// - AUTO: the floor is cut into SweepNodeCells-square blocks; every block at
///   least half walkable gets one node, on its most OPEN cell (furthest from
///   any wall; ties to the block centre, then the lower index), so a node is
///   somewhere a guard can stand and see from.
/// - AUTHORED: every '*' in the level (<see cref="Level.SweepNodes"/>). A
///   designer knows where a player hides; an authored node suppresses the auto
///   node of its block and carries AuthoredNodeBonusTicks when scored.
///
/// Positions are derived from the level; staleness and claims are sim state
/// and hashed. Built when the level is first compromised, never before: a run
/// that never gets that far pays nothing for it.
/// </summary>
public sealed class SweepMap
{
	public readonly int Count;
	public readonly int[] Cell, X, Y;
	public readonly bool[] Authored;

	/// <summary>Milli-ticks since any guard had the node in view.</summary>
	public readonly int[] Stale;

	/// <summary>Sweep group id walking to or standing at the node, or -1.</summary>
	public readonly int[] ClaimedBy;

	/// <summary>Staleness stops climbing here: an hour, far past any score that
	/// matters, and nowhere near overflow.</summary>
	public const int StaleCap = 3600 * Fx.TicksPerSecond * Actor.Mt;

	public SweepMap(Level level)
	{
		var nav = level.Nav;
		int w = nav.W, h = nav.H;
		int b = Tune.SweepNodeCells;

		// Openness: 8-neighbour steps from the nearest unwalkable cell or edge.
		var open = new int[w * h];
		var queue = new Queue<int>();
		for (int i = 0; i < w * h; i++)
		{
			open[i] = int.MaxValue;
			int c = i % w, r = i / w;
			if (!nav.Passable[i]) { open[i] = 0; queue.Enqueue(i); }
			else if (c == 0 || r == 0 || c == w - 1 || r == h - 1) { open[i] = 1; queue.Enqueue(i); }
		}
		while (queue.Count > 0)
		{
			int cur = queue.Dequeue();
			int c = cur % w, r = cur / w;
			for (int d = 0; d < 8; d++)
			{
				int nc = c + NavGrid.DC[d], nr = r + NavGrid.DR[d];
				if (!nav.InBounds(nc, nr)) continue;
				int j = nr * w + nc;
				if (open[j] <= open[cur] + 1) continue;
				open[j] = open[cur] + 1;
				queue.Enqueue(j);
			}
		}

		// Authored first, so their blocks are known before the auto pass.
		int bw = (w + b - 1) / b, bh = (h + b - 1) / b;
		var suppressed = new bool[bw * bh];
		var cells = new List<int>();
		var authored = new List<bool>();
		for (int k = 0; k < level.SweepNodes.Count; k++)
		{
			var (c, r) = level.SweepNodes[k];
			int cell = nav.NearestPassable(c * Level.CellFx + Level.CellFx / 2, r * Level.CellFx + Level.CellFx / 2);
			if (cell < 0) continue;                  // walled in: ignored, never thrown on
			suppressed[(r / b) * bw + (c / b)] = true;
			if (cells.Contains(cell)) continue;
			cells.Add(cell);
			authored.Add(true);
		}

		var auto = new List<int>();
		for (int br = 0; br < bh; br++)
		{
			for (int bc = 0; bc < bw; bc++)
			{
				if (suppressed[br * bw + bc]) continue;
				int walk = 0, total = 0, best = -1;
				long bestKey = long.MinValue;
				int cx2 = (bc * b * 2 + b - 1), cy2 = (br * b * 2 + b - 1);   // block centre x2, in cells
				for (int r = br * b; r < br * b + b && r < h; r++)
				{
					for (int c = bc * b; c < bc * b + b && c < w; c++)
					{
						total++;
						int i = r * w + c;
						if (!nav.Passable[i]) continue;
						walk++;
						long dc = 2 * c - cx2, dr = 2 * r - cy2;
						// Most open first, then nearest the centre; row-major
						// scan with a strict comparison keeps the lower index.
						long key = ((long)open[i] << 32) - (dc * dc + dr * dr);
						if (key > bestKey) { bestKey = key; best = i; }
					}
				}
				if (best >= 0 && walk * 2 >= total) auto.Add(best);
			}
		}

		// Auto nodes first in the list, then authored, each in its own order.
		Count = auto.Count + cells.Count;
		Cell = new int[Count]; X = new int[Count]; Y = new int[Count];
		Authored = new bool[Count];
		Stale = new int[Count];
		ClaimedBy = new int[Count];
		for (int k = 0; k < Count; k++)
		{
			bool isAuthored = k >= auto.Count;
			int cell = isAuthored ? cells[k - auto.Count] : auto[k];
			Cell[k] = cell;
			X[k] = nav.NodeX[cell]; Y[k] = nav.NodeY[cell];
			Authored[k] = isAuthored;
			ClaimedBy[k] = -1;
		}
	}

	public void Release(int groupId)
	{
		for (int k = 0; k < Count; k++) if (ClaimedBy[k] == groupId) ClaimedBy[k] = -1;
	}

	public void HashInto(ref Hash64 h)
	{
		h.Add(Count);
		for (int k = 0; k < Count; k++) { h.Add(Stale[k]); h.Add(ClaimedBy[k]); }
	}
}
