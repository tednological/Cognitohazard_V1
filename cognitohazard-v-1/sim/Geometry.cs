namespace Cognitohazard.Sim;

/// <summary>
/// Static, integer-only geometry (spec §3.1). Directions arrive as Q16 unit
/// vectors from <see cref="Brad"/>; positions and returned distances are
/// fixed-point px.
/// </summary>
public static class Geometry
{
	/// <summary>Stand-in for "no hit". Larger than any distance on a 960x560 field.</summary>
	public const int Far = int.MaxValue;

	/// <summary>
	/// Slab test, ray vs axis-aligned rect. Returns the entry distance in
	/// fixed-point px, or <see cref="Far"/>. A ray starting inside returns 0,
	/// matching the prototype.
	/// </summary>
	public static int RayRect(int ox, int oy, int dx, int dy, in Rect r)
	{
		long tmin = long.MinValue / 4, tmax = long.MaxValue / 4;

		if (dx == 0)
		{
			if (ox < r.X || ox > r.X1) return Far;
		}
		else
		{
			long a = ((long)(r.X - ox) << Brad.UnitShift) / dx;
			long b = ((long)(r.X1 - ox) << Brad.UnitShift) / dx;
			if (a > b) (a, b) = (b, a);
			if (a > tmin) tmin = a;
			if (b < tmax) tmax = b;
		}

		if (dy == 0)
		{
			if (oy < r.Y || oy > r.Y1) return Far;
		}
		else
		{
			long a = ((long)(r.Y - oy) << Brad.UnitShift) / dy;
			long b = ((long)(r.Y1 - oy) << Brad.UnitShift) / dy;
			if (a > b) (a, b) = (b, a);
			if (a > tmin) tmin = a;
			if (b < tmax) tmax = b;
		}

		long lo = tmin > 0 ? tmin : 0;
		if (tmax < lo) return Far;
		long t = tmin > 0 ? tmin : 0;
		return t > int.MaxValue ? Far : (int)t;
	}

	/// <summary>Distance to the nearest wall along a BRAD heading, capped at maxD.</summary>
	public static int CastRay(Rect[] walls, int ox, int oy, int brad, int maxD)
	{
		Brad.SinCos(brad, out int dy, out int dx);
		int best = maxD;
		for (int i = 0; i < walls.Length; i++)
		{
			int t = RayRect(ox, oy, dx, dy, in walls[i]);
			if (t < best) best = t;
		}
		return best;
	}

	/// <summary>
	/// True when nothing blocks the segment a-b. The half-pixel slack matches
	/// the prototype's `&lt; d - 0.5`, which keeps a wall you are standing
	/// against from occluding yourself.
	/// </summary>
	public static bool ClearLine(Rect[] walls, int ax, int ay, int bx, int by)
	{
		int ddx = bx - ax, ddy = by - ay;
		int d = Fx.Hypot(ddx, ddy);
		if (d < 4) return true;                       // < 1/64 px apart

		int ux = (int)(((long)ddx << Brad.UnitShift) / d);
		int uy = (int)(((long)ddy << Brad.UnitShift) / d);
		int limit = d - (Fx.One / 2);

		for (int i = 0; i < walls.Length; i++)
			if (RayRect(ax, ay, ux, uy, in walls[i]) < limit) return false;

		return true;
	}

	/// <summary>Circle-vs-rect overlap against every wall.</summary>
	public static bool HitsWall(Rect[] walls, int x, int y, int radius)
	{
		long rr = (long)radius * radius;
		for (int i = 0; i < walls.Length; i++)
		{
			ref readonly Rect r = ref walls[i];
			int cx = x < r.X ? r.X : (x > r.X1 ? r.X1 : x);
			int cy = y < r.Y ? r.Y : (y > r.Y1 ? r.Y1 : y);
			long dx = x - cx, dy = y - cy;
			if (dx * dx + dy * dy < rr) return true;
		}
		return false;
	}

	/// <summary>Circle-vs-rect overlap against ONE rect: the same test
	/// <see cref="HitsWall"/> runs per wall, for callers that need to know
	/// which rect it was.</summary>
	public static bool CircleHitsRect(int x, int y, int radius, in Rect r)
	{
		int cx = x < r.X ? r.X : (x > r.X1 ? r.X1 : x);
		int cy = y < r.Y ? r.Y : (y > r.Y1 ? r.Y1 : y);
		long dx = x - cx, dy = y - cy;
		return dx * dx + dy * dy < (long)radius * radius;
	}

	/// <summary>Distance from a point to the nearest point of a rect, fixed-point
	/// px; 0 inside it.</summary>
	public static int DistToRect(int x, int y, in Rect r)
	{
		int cx = x < r.X ? r.X : (x > r.X1 ? r.X1 : x);
		int cy = y < r.Y ? r.Y : (y > r.Y1 ? r.Y1 : y);
		return Fx.Hypot(x - cx, y - cy);
	}

	public static bool InRect(int x, int y, in Rect r)
		=> x > r.X && x < r.X1 && y > r.Y && y < r.Y1;

	/// <summary>
	/// Axis-separated slide. Tries the full move, then each axis alone, so an
	/// actor grazing a wall keeps its tangential speed instead of sticking.
	/// </summary>
	public static void MoveSlide(Rect[] walls, ref int x, ref int y, int dx, int dy, int radius)
	{
		if (!HitsWall(walls, x + dx, y + dy, radius)) { x += dx; y += dy; return; }
		if (dx != 0 && !HitsWall(walls, x + dx, y, radius)) { x += dx; return; }
		if (dy != 0 && !HitsWall(walls, x, y + dy, radius)) { y += dy; }
	}
}
