namespace Cognitohazard.Sim;

/// <summary>
/// Fixed-point scalar. One unit == 1/256 px (spec §4.1).
/// Every arithmetic helper here is integer-only and platform-independent.
/// Right shifts on negatives are arithmetic (floor), which is asymmetric but
/// deterministic — that is the property that matters for replay hashing.
/// </summary>
public static class Fx
{
	public const int Shift = 8;
	public const int One = 1 << Shift;          // 256 == 1.0 px
	public const int Half = One >> 1;

	/// <summary>Ticks per second. sim.step() advances exactly one of these.</summary>
	public const int TicksPerSecond = 60;

	/// <summary>
	/// Time scales are exact rationals over this denominator so that no float
	/// ever enters the movement path. 0.18 == 36/200, 0.62 == 124/200,
	/// 0.045 == 9/200, 1.0 == 200/200 (spec §5.2).
	/// </summary>
	public const int ScaleDen = 200;

	public static int FromInt(int px) => px << Shift;

	/// <summary>Exact construction from a decimal literal, e.g. FromRatio(196, 1).</summary>
	public static int FromRatio(int num, int den) => (int)(((long)num << Shift) / den);

	public static int Mul(int a, int b) => (int)(((long)a * b) >> Shift);

	public static int Div(int a, int b) => b == 0 ? 0 : (int)(((long)a << Shift) / b);

	/// <summary>Whole pixels, truncated toward negative infinity.</summary>
	public static int ToPxFloor(int f) => f >> Shift;

	public static int Abs(int v) => v < 0 ? -v : v;
	public static int Min(int a, int b) => a < b ? a : b;
	public static int Max(int a, int b) => a > b ? a : b;
	public static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

	/// <summary>
	/// Distance travelled in one tick at <paramref name="speedFx"/> px/s under a
	/// time scale of <paramref name="scaleNum"/>/200. Single division, no
	/// intermediate rounding.
	/// </summary>
	public static int PerTick(int speedFx, int scaleNum)
		=> (int)((long)speedFx * scaleNum / (ScaleDen * TicksPerSecond));

	/// <summary>floor(sqrt(v)) for v >= 0. Exact, bit-by-bit, no float.</summary>
	public static long SqrtL(long v)
	{
		if (v <= 0) return 0;
		long rem = 0, root = 0;
		for (int i = 0; i < 32; i++)
		{
			root <<= 1;
			rem = (rem << 2) | ((v >> 62) & 3);
			v <<= 2;
			if (root < rem)
			{
				root++;
				rem -= root;
				root++;
			}
		}
		return root >> 1;
	}

	/// <summary>
	/// Length of a fixed-point vector, in fixed-point. Because both components
	/// carry the same 1/256 scale, the square root of the squared sum lands back
	/// on that scale with no extra correction.
	/// </summary>
	public static int Hypot(int dxFx, int dyFx)
		=> (int)SqrtL((long)dxFx * dxFx + (long)dyFx * dyFx);

	public static long DistSq(int ax, int ay, int bx, int by)
	{
		long dx = ax - bx, dy = ay - by;
		return dx * dx + dy * dy;
	}

	public static int Dist(int ax, int ay, int bx, int by)
		=> (int)SqrtL(DistSq(ax, ay, bx, by));
}
