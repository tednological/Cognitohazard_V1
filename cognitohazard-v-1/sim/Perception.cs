namespace Cognitohazard.Sim;

/// <summary>
/// Awareness accumulation and cone/LOS queries (spec §8.2). Graded, not a
/// detection boolean.
/// </summary>
public static class Perception
{
	/// <summary>
	/// Cone range by posture and task (Guard_AI.md §2.2), reusing the spec's
	/// own five ranges. A curious guard past AwHunt watches with the old Hunt
	/// cone, which is what keeps the spec §8.6 curve where it was: that guard
	/// used to BE in Hunt.
	/// </summary>
	public static int RangeFor(Actor e, int alarmBonus)
	{
		int b = e.State switch
		{
			GuardState.Curious => e.Awareness >= Tune.AwHunt ? Tune.RangeHunt : Tune.RangeCurious,
			GuardState.Combat => e.Task == GuardTask.Engage ? Tune.RangeEngage
				: (e.Task == GuardTask.SearchLkp ? Tune.RangeSearch : Tune.RangeHunt),
			GuardState.Hunting => Tune.RangeSearch,
			_ => Tune.RangePatrol,
		};
		return b + alarmBonus;
	}

	public static int HalfAngleFor(Actor e) => e.State switch
	{
		GuardState.Curious => e.Awareness >= Tune.AwHunt ? Tune.HalfHunt : Tune.HalfCurious,
		GuardState.Combat => e.Task == GuardTask.Engage ? Tune.HalfEngage
			: (e.Task == GuardTask.SearchLkp ? Tune.HalfSearch : Tune.HalfHunt),
		GuardState.Hunting => Tune.HalfSearch,
		_ => Tune.HalfPatrol,
	};

	/// <summary>
	/// Stimulus quality in Q8, or 0 when the point is out of range, outside the
	/// cone, or occluded:
	///   q = clamp(1 - offset/half, 0.30, 1) * clamp(1 - dist/range, 0.15, 1)
	/// </summary>
	public static int SeesPoint(Rect[] walls, Actor e, int x, int y, int range, int half)
	{
		int dist = Fx.Dist(e.X, e.Y, x, y);
		if (dist > range) return 0;

		int toTarget = Brad.Atan2(y - e.Y, x - e.X);
		int offset = Brad.Norm(toTarget - e.Facing);
		if (offset < 0) offset = -offset;
		if (offset > half) return 0;

		if (!Geometry.ClearLine(walls, e.X, e.Y, x, y)) return 0;

		int centre = Fx.One - (int)((long)offset * Fx.One / half);
		if (centre < Tune.QCentreFloor) centre = Tune.QCentreFloor;
		if (centre > Fx.One) centre = Fx.One;

		int near = Fx.One - (int)((long)dist * Fx.One / range);
		if (near < Tune.QNearFloor) near = Tune.QNearFloor;
		if (near > Fx.One) near = Fx.One;

		return (centre * near) >> Fx.Shift;
	}

	// ------------------------------------------------------------ light
	// cognitohazard_lighting_plan.md §3. Every function here is the IDENTITY at
	// full light (256), exactly, so a lit level and the spec §8.6 curve are
	// untouched: a test pins it.

	/// <summary>
	/// How visible a figure standing in <paramref name="lightQ8"/> is, Q8:
	///   vis = VisFloor + (256 - VisFloor) * light / 256
	/// Pitch dark is VisFloor (0.28), never zero.
	/// </summary>
	public static int VisQ8(int lightQ8)
	{
		if (lightQ8 >= Fx.One) return Fx.One;
		if (lightQ8 < 0) lightQ8 = 0;
		return Tune.VisFloor + (((Fx.One - Tune.VisFloor) * lightQ8) >> Fx.Shift);
	}

	/// <summary>
	/// A sight range shortened by darkness at the target: linear from
	/// <paramref name="range"/> in full light down to <paramref name="darkRange"/>
	/// in none. A range already inside the dark one is left alone.
	/// </summary>
	public static int DarkReach(int range, int lightQ8, int darkRange)
	{
		if (lightQ8 >= Fx.One || range <= darkRange) return range;
		if (lightQ8 < 0) lightQ8 = 0;
		return darkRange + (int)(((long)(range - darkRange) * lightQ8) >> Fx.Shift);
	}

	/// <summary>
	/// A sight quality from <see cref="SeesPoint"/>, corrected for the light
	/// at the target. Inside DarkSeeRange light does not matter. Beyond the
	/// darkness-shortened range he cannot make the figure out at all. Between,
	/// the quality is scaled by VisQ8, before the movement, stance and alert
	/// multipliers, so creeping through a dark room compounds with the stealth
	/// tier the way a player expects. Never rounds a sighting down to nothing.
	/// </summary>
	public static int InLight(int q, int dist, int range, int lightQ8)
	{
		if (q <= 0 || lightQ8 >= Fx.One) return q;
		if (dist <= Tune.DarkSeeRange) return q;
		if (dist > DarkReach(range, lightQ8, Tune.DarkSightRange)) return 0;
		int v = (q * VisQ8(lightQ8)) >> Fx.Shift;
		return v < 1 ? 1 : v;
	}

	/// <summary>A guard already looking for something confirms it 1.9x faster
	/// (spec §8.2). Any posture above Relaxed is looking.</summary>
	public static bool IsAlerted(GuardState s)
		=> s == GuardState.Curious || s == GuardState.Combat || s == GuardState.Hunting;

	/// <summary>
	/// Per-tick awareness gain in tenths x 200, before the world clock scales
	/// it. Multipliers compose exactly as spec §8.2 states:
	///   mul = (moved ? 1.35 : 0.50) * (sneaking ? 0.60 : 1) * (alerted ? 1.9 : 1)
	/// </summary>
	/// <param name="tier">
	/// The player's movement tier. A MOVING player is picked up at that tier's
	/// own rate — the "more stealth the slower you move" axis. A player holding
	/// still is judged by stance rather than by speed, so the tier does not
	/// enter except that creeping still helps.
	/// </param>
	public static int StimulusPerTick(int q, bool playerMoved, int tier, bool alerted)
	{
		if (q <= 0) return 0;

		long mul = playerMoved ? Tune.TierDetectQ8(tier) : Tune.MulStill;
		if (!playerMoved && tier == InputFrame.TierStealth)
			mul = (mul * Tune.MulSneak) >> Fx.Shift;
		if (alerted) mul = (mul * Tune.MulAlerted) >> Fx.Shift;

		// gain(tenths/s) * q(Q8) * mul(Q8) / (256*256) / 60 ticks, then x200 for
		// the accumulator's resolution. The x200 and the /60 are folded so the
		// intermediate never loses precision.
		long v = (long)Tune.AwGainPerSec * q * mul * Actor.Mt;
		return (int)(v / (256L * 256L * Fx.TicksPerSecond));
	}

	/// <summary>Footstep hearing: no LOS required, and capped so noise alone can
	/// never reach Engage (spec §8.3).</summary>
	public static int NoiseStimulusPerTick(int dist, int noiseRadius)
	{
		if (noiseRadius <= 0 || dist >= noiseRadius) return 0;
		int falloff = Fx.One - (int)((long)dist * Fx.One / noiseRadius);
		long v = (long)Tune.NoiseGainPerSec * falloff * Actor.Mt;
		return (int)(v / (256L * Fx.TicksPerSecond));
	}
}
