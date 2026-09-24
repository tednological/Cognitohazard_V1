using System.Collections.Generic;

namespace Cognitohazard.Sim;

/// <summary>
/// The light on every cell of a level, Q8 0..256 (cognitohazard_lighting_plan.md
/// §2). Ambient plus every lit lamp, each lamp shadowcast against the OPAQUE
/// set -- walls and shut doors, exactly what stops an eye -- so glass passes
/// light and a shut door keeps a lit room's light to itself.
///
/// DERIVED, never hashed: a pure function of the level, which lamps are lit,
/// and which cells are opaque, all of which are hashed where they live. It is
/// rebuilt on the events that change those, never per tick:
/// <see cref="SetOpaque"/> re-casts only the lamps whose reach covers a cell
/// that changed, and <see cref="SetLamp"/> only re-sums. A test asserts that
/// the incremental map always equals one built from scratch.
///
/// Opaque cells carry 0 and are left out of <see cref="LightAt"/>'s
/// interpolation: a wall is not a place anyone stands, and letting its value
/// bleed into the floor beside it would light the dark side of a lit room's
/// wall. Presentation lights walls by their faces instead (<see cref="Texture2x"/>).
/// </summary>
public sealed class LightMap
{
	public readonly int W, H;
	public readonly int AmbientQ8;

	/// <summary>Light per cell, row-major, 0..256. 0 on an opaque cell.</summary>
	public readonly int[] Q8;

	/// <summary>Bumped on every change. Presentation re-uploads its texture
	/// when this moves, and not otherwise.</summary>
	public int Version { get; private set; }

	private readonly Level _level;
	private readonly bool[] _opaque;
	private readonly bool[] _lampOn;
	private readonly int[][] _lampCell;
	private readonly int[][] _lampVal;

	/// <summary>Cells either side of a lamp its light can reach.</summary>
	private static int ReachCells => (Tune.LampRadius + Level.CellFx - 1) / Level.CellFx;

	public LightMap(Level level, bool[] opaque, Rect[] opaqueRects, bool[] lampOn)
	{
		_level = level;
		W = level.W;
		H = level.H;
		AmbientQ8 = level.AmbientQ8;
		Q8 = new int[W * H];
		_opaque = (bool[])opaque.Clone();
		_lampOn = (bool[])lampOn.Clone();
		int n = level.Lamps.Count;
		_lampCell = new int[n][];
		_lampVal = new int[n][];
		for (int i = 0; i < n; i++) Cast(i, opaqueRects);
		Sum();
	}

	/// <summary>
	/// The level at rest: every door shut, every lamp lit. What a run starts
	/// with, and what the editor previews.
	/// </summary>
	public static LightMap AtRest(Level level)
	{
		var opaque = new bool[level.W * level.H];
		for (int r = 0; r < level.H; r++)
			for (int c = 0; c < level.W; c++)
				opaque[r * level.W + c] = level.OpaqueAtRest(c, r);
		var rects = new List<Rect>(level.Walls);
		foreach (var p in level.Panels)
			if (p.Kind == PanelKind.Door) rects.Add(p.Rect);
		var on = new bool[level.Lamps.Count];
		for (int i = 0; i < on.Length; i++) on[i] = true;
		return new LightMap(level, opaque, rects.ToArray(), on);
	}

	public bool IsOpaque(int c, int r) => _opaque[r * W + c];

	public bool LampOn(int i) => _lampOn[i];

	public int CellQ8(int c, int r)
		=> c < 0 || r < 0 || c >= W || r >= H ? 0 : Q8[r * W + c];

	/// <summary>
	/// The opaque set changed (a door moved). Re-cast every lamp whose reach
	/// covers a cell that flipped, then re-sum. Nothing changed, nothing done.
	/// </summary>
	public void SetOpaque(bool[] opaque, Rect[] opaqueRects)
	{
		var changed = new List<int>();
		for (int i = 0; i < _opaque.Length; i++)
			if (_opaque[i] != opaque[i]) { _opaque[i] = opaque[i]; changed.Add(i); }
		if (changed.Count == 0) return;

		int k = ReachCells + 1;
		for (int l = 0; l < _level.Lamps.Count; l++)
		{
			var (lc, lr) = _level.Lamps[l];
			for (int j = 0; j < changed.Count; j++)
			{
				int c = changed[j] % W, r = changed[j] / W;
				if (c < lc - k || c > lc + k || r < lr - k || r > lr + k) continue;
				Cast(l, opaqueRects);
				break;
			}
		}
		Sum();
	}

	/// <summary>A lamp went out (broken, switched) or came back on.</summary>
	public void SetLamp(int i, bool on)
	{
		if (i < 0 || i >= _lampOn.Length || _lampOn[i] == on) return;
		_lampOn[i] = on;
		Sum();
	}

	/// <summary>
	/// One lamp's disc: every non-opaque cell within LampRadius whose centre
	/// the lamp has a clear line to, at a linear falloff. Integer throughout,
	/// through the same <see cref="Geometry.ClearLine"/> sight uses, so light
	/// cannot reach anywhere an eye at the lamp could not.
	/// </summary>
	private void Cast(int lamp, Rect[] opaqueRects)
	{
		var (lc, lr) = _level.Lamps[lamp];
		int lx = lc * Level.CellFx + Level.CellFx / 2;
		int ly = lr * Level.CellFx + Level.CellFx / 2;
		int R = Tune.LampRadius;

		// Only what the disc can touch: the reason Terminal Twelve's 284 rects
		// cost nothing here.
		var near = new List<Rect>();
		for (int i = 0; i < opaqueRects.Length; i++)
		{
			ref readonly Rect q = ref opaqueRects[i];
			if (q.X < lx + R && q.X1 > lx - R && q.Y < ly + R && q.Y1 > ly - R) near.Add(q);
		}
		var nearArr = near.ToArray();

		var cells = new List<int>();
		var vals = new List<int>();
		int k = ReachCells;
		for (int r = lr - k; r <= lr + k; r++)
		{
			if (r < 0 || r >= H) continue;
			for (int c = lc - k; c <= lc + k; c++)
			{
				if (c < 0 || c >= W) continue;
				int idx = r * W + c;
				if (_opaque[idx]) continue;
				int cx = c * Level.CellFx + Level.CellFx / 2;
				int cy = r * Level.CellFx + Level.CellFx / 2;
				int d = Fx.Dist(lx, ly, cx, cy);
				if (d >= R) continue;
				if ((c != lc || r != lr) && !Geometry.ClearLine(nearArr, lx, ly, cx, cy)) continue;
				int v = (int)((long)Tune.LampIntensity * (R - d) / R);
				if (v <= 0) continue;
				cells.Add(idx);
				vals.Add(v);
			}
		}
		_lampCell[lamp] = cells.ToArray();
		_lampVal[lamp] = vals.ToArray();
	}

	private void Sum()
	{
		for (int i = 0; i < Q8.Length; i++) Q8[i] = _opaque[i] ? 0 : AmbientQ8;
		for (int l = 0; l < _lampCell.Length; l++)
		{
			if (!_lampOn[l]) continue;
			var cells = _lampCell[l];
			var vals = _lampVal[l];
			for (int j = 0; j < cells.Length; j++)
				if (!_opaque[cells[j]]) Q8[cells[j]] += vals[j];
		}
		for (int i = 0; i < Q8.Length; i++) if (Q8[i] > Fx.One) Q8[i] = Fx.One;
		Version++;
	}

	/// <summary>
	/// Light at a fixed-point POINT: bilinear over the four nearest cell
	/// centres, integer, with opaque cells left out and the rest re-weighted.
	/// A guard on the edge of a pool of light must not flicker between two
	/// values from one pixel of movement (plan §2.3); that is a fairness bug.
	/// </summary>
	public int LightAt(int x, int y)
	{
		int half = Level.CellFx / 2;
		int gx = x - half, gy = y - half;
		int c0 = FloorDiv(gx, Level.CellFx), r0 = FloorDiv(gy, Level.CellFx);
		long fx = gx - (long)c0 * Level.CellFx;
		long fy = gy - (long)r0 * Level.CellFx;
		long ix = Level.CellFx - fx, iy = Level.CellFx - fy;

		long acc = 0, wsum = 0;
		Tap(c0, r0, ix * iy, ref acc, ref wsum);
		Tap(c0 + 1, r0, fx * iy, ref acc, ref wsum);
		Tap(c0, r0 + 1, ix * fy, ref acc, ref wsum);
		Tap(c0 + 1, r0 + 1, fx * fy, ref acc, ref wsum);
		if (wsum == 0) return AmbientQ8;
		return (int)(acc / wsum);
	}

	private void Tap(int c, int r, long w, ref long acc, ref long wsum)
	{
		if (w <= 0 || c < 0 || r < 0 || c >= W || r >= H) return;
		int i = r * W + c;
		if (_opaque[i]) return;
		acc += w * Q8[i];
		wsum += w;
	}

	private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

	/// <summary>
	/// The map for DRAWING, at two texels per cell each way, 0..255. A floor
	/// cell's four texels are its own light. A wall's texels take the light of
	/// the floor they FACE -- the neighbour in that texel's direction -- so the
	/// lit face of a wall is lit and its far face is not. At one texel per cell
	/// a linearly filtered wall glowed on both faces, and a lit room showed
	/// through every wall around it.
	/// </summary>
	public byte[] Texture2x()
	{
		int tw = W * 2;
		var outp = new byte[tw * H * 2];
		for (int r = 0; r < H; r++)
			for (int c = 0; c < W; c++)
			{
				int i = r * W + c;
				for (int sy = 0; sy < 2; sy++)
					for (int sx = 0; sx < 2; sx++)
					{
						int v;
						if (!_opaque[i]) v = Q8[i];
						else
						{
							int nc = c + (sx == 0 ? -1 : 1), nr = r + (sy == 0 ? -1 : 1);
							v = 0;
							if (nc >= 0 && nc < W && !_opaque[r * W + nc]) v = Q8[r * W + nc];
							if (nr >= 0 && nr < H && !_opaque[nr * W + c] && Q8[nr * W + c] > v)
								v = Q8[nr * W + c];
						}
						outp[(r * 2 + sy) * tw + c * 2 + sx] = (byte)(v > 255 ? 255 : v);
					}
			}
		return outp;
	}

	/// <summary>Cell for cell, the same light. For the harness: an incremental
	/// rebuild must equal a fresh one.</summary>
	public bool SameAs(LightMap other)
	{
		if (other.W != W || other.H != H) return false;
		for (int i = 0; i < Q8.Length; i++) if (Q8[i] != other.Q8[i]) return false;
		return true;
	}
}
