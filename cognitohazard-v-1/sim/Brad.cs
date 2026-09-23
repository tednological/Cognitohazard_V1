namespace Cognitohazard.Sim;

/// <summary>
/// Binary-radian angles: 65536 BRAD == one full turn (spec §3.2).
///
/// Trigonometry is integer CORDIC, not Math.Sin. The spec forbids float from
/// reaching state (§3.3/§4.1), and libm's sin/atan2 are not guaranteed
/// bit-identical across platforms or runtime versions — which would silently
/// break replay hashes on a different machine. CORDIC is exact integer
/// arithmetic, so the hash is portable.
///
/// The quarter-wave sine table is built once via CORDIC at type init; runtime
/// SinCos is then a table lookup plus quadrant symmetry.
/// </summary>
public static class Brad
{
	public const int Full = 65536;
	public const int Half = 32768;
	public const int Quarter = 16384;
	public const int Mask = Full - 1;

	/// <summary>Scale of the values returned by Sin/Cos: 65536 == 1.0.</summary>
	public const int UnitShift = 16;
	public const int Unit = 1 << UnitShift;

	// atan(2^-i) in SIXTEENTHS of a BRAD. The angle accumulator needs finer
	// resolution than the output: in whole BRAD the table flattens to 1,1,0 by
	// iteration 13 and CORDIC stops converging, leaving ~2.4e-4 of residual.
	// Summed convergence range is 18183 BRAD, wider than the Quarter (16384) we
	// ever feed it.
	private const int ZScale = 16;
	private static readonly int[] AtanBrad =
	{
		131072, 77376, 40884, 20753, 10417, 5213, 2607, 1304, 652, 326,
		163, 81, 41, 20, 10, 5, 3, 1, 1, 0
	};

	private const int Iterations = 20;

	// 1 / product(sqrt(1 + 2^-2i)) — the CORDIC gain, pre-divided out. Held at
	// Q20 internally: the extra four fractional bits keep the per-iteration
	// shift truncation from accumulating into the Q16 result.
	private const int WorkShift = 20;
	private const int GainInvQ20 = 636751;

	private static readonly int[] SinQ = BuildQuarterTable();

	private static int[] BuildQuarterTable()
	{
		var t = new int[Quarter + 1];
		for (int a = 0; a <= Quarter; a++)
		{
			Rotate(a, out _, out int sin);
			t[a] = sin;
		}
		// Anchor the two exactly-known endpoints. CORDIC leaves a residual of a
		// few Q16 LSBs at the quadrant boundaries, and an inexact sin(0) would
		// give an actor aiming due east a small perpendicular drift every tick.
		t[0] = 0;
		t[Quarter] = Unit;
		return t;
	}

	/// <summary>CORDIC rotation mode. <paramref name="brad"/> must be in [-18183, 18183].</summary>
	private static void Rotate(int brad, out int cos, out int sin)
	{
		int x = GainInvQ20, y = 0, z = brad * ZScale;
		for (int i = 0; i < Iterations; i++)
		{
			int dx = y >> i, dy = x >> i;
			if (z >= 0) { x -= dx; y += dy; z -= AtanBrad[i]; }
			else        { x += dx; y -= dy; z += AtanBrad[i]; }
		}
		cos = ToUnit(x);
		sin = ToUnit(y);
	}

	/// <summary>Q20 working value down to the Q16 output scale, round-to-nearest.</summary>
	private static int ToUnit(int q20)
	{
		const int Drop = WorkShift - UnitShift;
		const int Bias = 1 << (Drop - 1);
		return (q20 + Bias) >> Drop;
	}

	private static int SinQuarter(int r) => SinQ[r];

	/// <summary>sin and cos of a BRAD angle, both scaled by 65536.</summary>
	public static void SinCos(int brad, out int sin, out int cos)
	{
		int a = brad & Mask;
		int quad = a >> 14;
		int r = a & (Quarter - 1);
		int s = SinQuarter(r);
		int c = SinQuarter(Quarter - r);
		switch (quad)
		{
			case 0:  sin =  s; cos =  c; break;
			case 1:  sin =  c; cos = -s; break;
			case 2:  sin = -s; cos = -c; break;
			default: sin = -c; cos =  s; break;
		}
	}

	public static int Sin(int brad) { SinCos(brad, out int s, out _); return s; }
	public static int Cos(int brad) { SinCos(brad, out _, out int c); return c; }

	/// <summary>
	/// Shortest signed difference a-b, in (-32768, 32768]. This is the integer
	/// equivalent of the prototype's norm(a-b).
	/// </summary>
	public static int Norm(int diff)
	{
		int d = diff & Mask;
		return d > Half ? d - Full : d;
	}

	/// <summary>CORDIC vectoring mode. Returns the angle of (x, y) in BRAD.</summary>
	public static int Atan2(int y, int x)
	{
		if (x == 0 && y == 0) return 0;

		// Pre-rotate into the right half plane, where vectoring converges.
		int z0 = 0;
		if (x < 0)
		{
			if (y >= 0) { int t = x; x = y;  y = -t; z0 =  Quarter; }
			else        { int t = x; x = -y; y = t;  z0 = -Quarter; }
		}

		// Keep magnitudes large enough that the shifts retain precision, but
		// small enough that x + (y >> i) cannot overflow.
		while (x > (1 << 24) || y > (1 << 24) || y < -(1 << 24)) { x >>= 1; y >>= 1; }
		while (x < (1 << 12) && x != 0 && Fx.Abs(y) < (1 << 12)) { x <<= 1; y <<= 1; }

		int z = 0;
		for (int i = 0; i < Iterations; i++)
		{
			int dx = y >> i, dy = x >> i;
			if (y < 0) { x -= dx; y += dy; z -= AtanBrad[i]; }
			else       { x += dx; y -= dy; z += AtanBrad[i]; }
		}
		// z accumulated in sixteenths; round to the nearest whole BRAD.
		int zb = (z + (z >= 0 ? ZScale / 2 : -(ZScale / 2))) / ZScale;
		return (zb + z0) & Mask;
	}

	/// <summary>
	/// Turn <paramref name="cur"/> toward <paramref name="target"/> by the
	/// proportional-lerp rule the prototype uses: ang += norm(delta) * min(1, dt*k).
	/// Ruling #3: ported as a lerp, NOT as the rad/s the spec's prose claims —
	/// spec §8.6's detection curve was measured against this curve.
	/// <paramref name="kNum"/>/<paramref name="kDen"/> is the per-tick fraction.
	/// </summary>
	public static int TurnToward(int cur, int target, int kNum, int kDen)
	{
		int d = Norm(target - cur);
		if (kNum >= kDen) return target & Mask;
		return (cur + (int)((long)d * kNum / kDen)) & Mask;
	}
}
