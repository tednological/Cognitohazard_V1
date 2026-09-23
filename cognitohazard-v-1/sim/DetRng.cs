namespace Cognitohazard.Sim;

/// <summary>
/// The only source of randomness in sim/ (spec §3.1, §4.1). xoshiro256** with
/// splitmix64 seeding — all integer, no platform dependency.
///
/// Draw order is part of the state, so callers must draw in a fixed order:
/// guards iterate by index, always.
/// </summary>
public sealed class DetRng
{
	private ulong _s0, _s1, _s2, _s3;

	/// <summary>Number of draws taken. Feeds the state hash so a desync in draw
	/// ORDER is caught even when the drawn values happen to coincide.</summary>
	public ulong Draws { get; private set; }

	public DetRng(ulong seed)
	{
		ulong z = seed;
		_s0 = SplitMix(ref z);
		_s1 = SplitMix(ref z);
		_s2 = SplitMix(ref z);
		_s3 = SplitMix(ref z);
	}

	private static ulong SplitMix(ref ulong z)
	{
		z += 0x9E3779B97F4A7C15UL;
		ulong r = z;
		r = (r ^ (r >> 30)) * 0xBF58476D1CE4E5B9UL;
		r = (r ^ (r >> 27)) * 0x94D049BB133111EBUL;
		return r ^ (r >> 31);
	}

	private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

	public ulong NextU64()
	{
		Draws++;
		ulong result = Rotl(_s1 * 5, 7) * 9;
		ulong t = _s1 << 17;
		_s2 ^= _s0;
		_s3 ^= _s1;
		_s1 ^= _s2;
		_s0 ^= _s3;
		_s2 ^= t;
		_s3 = Rotl(_s3, 45);
		return result;
	}

	/// <summary>Uniform in [0, bound). Rejection-sampled, so it is unbiased and
	/// its consumption of the stream is value-dependent but deterministic.</summary>
	public int NextInt(int bound)
	{
		if (bound <= 1) return 0;
		ulong b = (ulong)bound;
		ulong limit = ulong.MaxValue - (ulong.MaxValue % b) - 1;
		ulong r;
		do { r = NextU64(); } while (r > limit);
		return (int)(r % b);
	}

	/// <summary>Uniform in [lo, hi], inclusive.</summary>
	public int NextRange(int lo, int hi) => hi <= lo ? lo : lo + NextInt(hi - lo + 1);

	/// <summary>Uniform over the full turn.</summary>
	public int NextBrad() => (int)(NextU64() & Brad.Mask);

	/// <summary>Symmetric draw in [-half, +half], the integer analogue of
	/// (random() - 0.5) * width.</summary>
	public int NextSigned(int half) => NextRange(-half, half);

	public void HashInto(ref Hash64 h)
	{
		h.Add(_s0); h.Add(_s1); h.Add(_s2); h.Add(_s3); h.Add(Draws);
	}
}
