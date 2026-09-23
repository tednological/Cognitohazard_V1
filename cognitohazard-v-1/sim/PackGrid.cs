namespace Cognitohazard.Sim;

/// <summary>
/// The spatial container the player loots into: a rectangular grid of cells that
/// multi-cell gear occupies, with a 90-degree turn available, so a full pack is a
/// decision about what to leave behind rather than a number.
///
/// This is SIM state. It feeds the state hash and rides inside replays, so
/// everything here is integer, every scan is by index, and nothing consults a
/// hash-ordered collection (spec §3.3).
///
/// Capacity is bounded by the worn backpack, and the smallest footprint is 1x1,
/// so a grid can never hold more placements than it has cells. The placement
/// arrays are therefore allocated once at W*H and never grow -- no reallocation
/// mid-mission, and no growth policy that could differ between machines.
/// </summary>
public sealed class PackGrid
{
	/// <summary>
	/// A rectangle has only two distinct footprints, so these are the entire
	/// rotation space: 180 degrees is indistinguishable from 0, and 270 from 90.
	/// That is a property of rectangles, not a corner cut.
	/// </summary>
	public const int Rot0 = 0;
	public const int Rot90 = 1;

	/// <summary>"Nothing here", for both an empty cell and a missing placement.</summary>
	public const int None = -1;

	public int W { get; private set; }
	public int H { get; private set; }

	private int[] _cells = System.Array.Empty<int>();
	private int[] _pItem = System.Array.Empty<int>();
	private int[] _pX = System.Array.Empty<int>();
	private int[] _pY = System.Array.Empty<int>();
	private int[] _pRot = System.Array.Empty<int>();
	private int[] _pLive = System.Array.Empty<int>();

	public PackGrid() { Resize(0, 0); }

	public PackGrid(int w, int h) { Resize(w, h); }

	/// <summary>Capacity, in placements. Equal to the cell count.</summary>
	public int Capacity => _pLive.Length;

	/// <summary>Drops every placement: a grid of another size cannot keep them honestly.</summary>
	public void Resize(int w, int h)
	{
		W = w < 0 ? 0 : w;
		H = h < 0 ? 0 : h;
		int cells = W * H;
		_cells = new int[cells];
		for (int i = 0; i < cells; i++) _cells[i] = None;
		_pItem = new int[cells];
		_pX = new int[cells];
		_pY = new int[cells];
		_pRot = new int[cells];
		_pLive = new int[cells];
	}

	public void Clear() => Resize(W, H);

	/// <summary>
	/// `ignore` exempts one placement from the overlap test, which is what lets a
	/// move or a turn be checked against a position overlapping where the item
	/// already sits.
	/// </summary>
	public bool CanPlace(int itemId, int x, int y, int rot, int ignore = None)
	{
		GearCatalog.SpanOf(itemId, rot, out int sw, out int sh);
		if (sw <= 0 || sh <= 0) return false;
		if (x < 0 || y < 0 || x + sw > W || y + sh > H) return false;
		for (int ry = y; ry < y + sh; ry++)
		{
			for (int rx = x; rx < x + sw; rx++)
			{
				int occ = _cells[ry * W + rx];
				if (occ != None && occ != ignore) return false;
			}
		}
		return true;
	}

	/// <summary>The new placement id, or None if it does not fit.</summary>
	public int Place(int itemId, int x, int y, int rot = Rot0)
	{
		if (!CanPlace(itemId, x, y, rot)) return None;
		int pi = ClaimSlot();
		if (pi == None) return None;
		_pItem[pi] = itemId;
		_pX[pi] = x;
		_pY[pi] = y;
		_pRot[pi] = rot;
		_pLive[pi] = 1;
		Stamp(pi, pi);
		return pi;
	}

	/// <summary>
	/// The first position an item fits, scanned top-left first and preferring the
	/// unturned footprint at each cell. Scan order is fixed, so the same pack
	/// always fills the same way on every machine.
	/// </summary>
	public bool FindFit(int itemId, out int fx, out int fy, out int frot)
	{
		for (int y = 0; y < H; y++)
		{
			for (int x = 0; x < W; x++)
			{
				for (int rot = Rot0; rot <= Rot90; rot++)
				{
					if (CanPlace(itemId, x, y, rot))
					{
						fx = x; fy = y; frot = rot;
						return true;
					}
				}
			}
		}
		fx = 0; fy = 0; frot = Rot0;
		return false;
	}

	public int AutoPlace(int itemId)
		=> FindFit(itemId, out int x, out int y, out int rot) ? Place(itemId, x, y, rot) : None;

	/// <summary>Whether an item would fit anywhere, without placing it.</summary>
	public bool WouldFit(int itemId) => FindFit(itemId, out _, out _, out _);

	public bool Remove(int pi)
	{
		if (!IsLive(pi)) return false;
		Stamp(pi, None);
		_pLive[pi] = 0;
		return true;
	}

	/// <summary>
	/// Turns in place about the item's top-left cell. Fails, changing nothing, if
	/// the turned footprint would not fit there.
	/// </summary>
	public bool Rotate(int pi)
	{
		if (!IsLive(pi)) return false;
		int want = _pRot[pi] == Rot90 ? Rot0 : Rot90;
		return Move(pi, _pX[pi], _pY[pi], want);
	}

	public bool Move(int pi, int x, int y, int rot)
	{
		if (!IsLive(pi)) return false;
		if (!CanPlace(_pItem[pi], x, y, rot, pi)) return false;
		Stamp(pi, None);
		_pX[pi] = x;
		_pY[pi] = y;
		_pRot[pi] = rot;
		Stamp(pi, pi);
		return true;
	}

	public int PlacementAt(int x, int y)
	{
		if (x < 0 || y < 0 || x >= W || y >= H) return None;
		return _cells[y * W + x];
	}

	public bool IsLive(int pi) => pi >= 0 && pi < _pLive.Length && _pLive[pi] == 1;

	public int ItemOf(int pi) => IsLive(pi) ? _pItem[pi] : None;
	public int XOf(int pi) => IsLive(pi) ? _pX[pi] : 0;
	public int YOf(int pi) => IsLive(pi) ? _pY[pi] : 0;
	public int RotOf(int pi) => IsLive(pi) ? _pRot[pi] : Rot0;

	public int LiveCount()
	{
		int n = 0;
		for (int i = 0; i < _pLive.Length; i++) if (_pLive[i] == 1) n++;
		return n;
	}

	public int UsedCells()
	{
		int n = 0;
		for (int i = 0; i < _cells.Length; i++) if (_cells[i] != None) n++;
		return n;
	}

	public int FreeCells() => W * H - UsedCells();

	/// <summary>
	/// Every placement, live or dead, in id order. Dead holes are hashed too, as
	/// a zero: their positions in the array are part of how the next Place
	/// behaves, so leaving them out would let two differently-shaped packs hash
	/// alike.
	/// </summary>
	public void HashInto(ref Hash64 h)
	{
		h.Add(W); h.Add(H);
		for (int pi = 0; pi < _pLive.Length; pi++)
		{
			h.Add(_pLive[pi]);
			if (_pLive[pi] != 1) continue;
			h.Add(_pItem[pi]); h.Add(_pX[pi]); h.Add(_pY[pi]); h.Add(_pRot[pi]);
		}
	}

	/// <summary>First dead hole, else None when the grid is at capacity.</summary>
	private int ClaimSlot()
	{
		for (int pi = 0; pi < _pLive.Length; pi++) if (_pLive[pi] == 0) return pi;
		return None;
	}

	private void Stamp(int pi, int value)
	{
		GearCatalog.SpanOf(_pItem[pi], _pRot[pi], out int sw, out int sh);
		for (int ry = _pY[pi]; ry < _pY[pi] + sh; ry++)
		{
			for (int rx = _pX[pi]; rx < _pX[pi] + sw; rx++)
			{
				_cells[ry * W + rx] = value;
			}
		}
	}
}
