namespace Cognitohazard.Sim;

/// <summary>
/// The sole owner of every time-scale effect (spec §5.3).
///
/// This is the one place the spec asks us to improve on the prototype rather
/// than port it: the prototype gates hitstop off with `timeScale &lt;= 0.55`,
/// which is a patch that compounds badly as effects are added. Here each effect
/// registers a request and the authority resolves by priority.
///
/// No other subsystem may read or write a time scale directly.
/// </summary>
public sealed class TimeAuthority
{
	public const int PriJolt = 0;
	public const int PriHitstop = 1;
	public const int PriDilation = 2;
	public const int PriNormal = 3;

	private int _joltTicks;
	private int _hitstopTicks;
	private bool _dilating;

	/// <summary>Scale numerator over <see cref="Fx.ScaleDen"/> for world subsystems.</summary>
	public int WorldScale { get; private set; } = Tune.NormalScale;

	/// <summary>Scale numerator for the player's own clock.</summary>
	public int PlayerScale { get; private set; } = Tune.NormalScale;

	/// <summary>Presentation keeps running under hitstop; it never freezes.</summary>
	public bool PresentationRuns => true;

	public int ActivePriority { get; private set; } = PriNormal;
	public bool JoltActive => _joltTicks > 0;
	public bool HitstopActive => _hitstopTicks > 0;

	public void RequestJolt(int ticks) { if (ticks > _joltTicks) _joltTicks = ticks; }
	public void RequestHitstop(int ticks) { if (ticks > _hitstopTicks) _hitstopTicks = ticks; }
	public void RequestDilation(bool on) => _dilating = on;

	/// <summary>Resolve this tick's clocks, then age the timed requests.</summary>
	public void Resolve()
	{
		if (_joltTicks > 0)
		{
			ActivePriority = PriJolt;
			WorldScale = Tune.NormalScale;
			PlayerScale = Tune.NormalScale;
		}
		else if (_hitstopTicks > 0)
		{
			ActivePriority = PriHitstop;
			WorldScale = 0;
			PlayerScale = 0;
		}
		else if (_dilating)
		{
			ActivePriority = PriDilation;
			WorldScale = Tune.WorldSlow;
			PlayerScale = Tune.PlayerClock;
		}
		else
		{
			ActivePriority = PriNormal;
			WorldScale = Tune.NormalScale;
			PlayerScale = Tune.NormalScale;
		}

		if (_joltTicks > 0) _joltTicks--;
		else if (_hitstopTicks > 0) _hitstopTicks--;
	}

	public void HashInto(ref Hash64 h)
	{
		h.Add(_joltTicks); h.Add(_hitstopTicks); h.Add(_dilating);
		h.Add(WorldScale); h.Add(PlayerScale);
	}
}
