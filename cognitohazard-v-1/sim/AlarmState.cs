using System.Collections.Generic;

namespace Cognitohazard.Sim;

/// <summary>
/// Floor alarm (spec §8.4). Level 1 sets an awareness floor of 16, level 2
/// sets 38 and grants +60 cone range and faster patrols, and level 3 is the
/// compromised floor (Guard_AI.md §6): level 2's effects, and it never decays.
///
/// Kept as its own class because it is the seed of the eventual propagating
/// alarm subsystem, which needs somewhere to land.
/// </summary>
public sealed class AlarmState
{
	public int Level { get; private set; }
	private int _quietMt;

	public void Raise(int level)
	{
		if (level > Level) Level = level;
		_quietMt = 0;
	}

	/// <summary>
	/// Level 3: the floor knows it has an intruder (Guard_AI.md §6). Sticky: it
	/// never decays, because the guards never stand down.
	/// </summary>
	public const int Compromised = 3;

	public void Compromise() { Level = Compromised; _quietMt = 0; }

	/// <summary>Awareness floor this alarm level imposes, in tenths.</summary>
	public int AwarenessFloor => Level >= 2 ? Tune.AlarmFloor2 : (Level >= 1 ? Tune.AlarmFloor1 : 0);

	public int ConeRangeBonus => Level >= 2 ? Tune.RangeAlarmBonus : 0;

	public int PatrolSpeed => Level >= 1 ? Tune.SpeedPatrolAlarm : Tune.SpeedPatrol;

	public void Step(List<Actor> guards, int worldScale)
	{
		if (worldScale <= 0 || Level >= Compromised) return;

		bool hot = false;
		for (int i = 0; i < guards.Count; i++)
		{
			var s = guards[i].State;
			if (s == GuardState.Combat) hot = true;
			// Ruling #1: spec §8.4 counts Curious among the states that hold the
			// alarm up; the prototype's anyHot check omits it. Following spec.
			else if (Tune.CuriousHoldsAlarm && s == GuardState.Curious) hot = true;
		}

		if (hot) { _quietMt = 0; return; }

		// The quiet timer runs on the WORLD clock, matching the prototype: under
		// dilation the floor alarm cools off proportionally slower.
		_quietMt += worldScale;
		if (_quietMt > Tune.AlarmDecayTicks * Actor.Mt && Level > 0)
		{
			Level--;
			_quietMt = 0;
		}
	}

	public void HashInto(ref Hash64 h) { h.Add(Level); h.Add(_quietMt); }
}
