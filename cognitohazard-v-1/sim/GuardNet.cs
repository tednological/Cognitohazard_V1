using System.Collections.Generic;

namespace Cognitohazard.Sim;

/// <summary>
/// A guard waiting for backup and the responders sent to him (Guard_AI.md
/// §5.5). Members are guard INDICES, in the order they joined; the anchor is
/// the guard who called.
/// </summary>
public sealed class Squad
{
	public int Id;
	public int Anchor;
	public readonly List<int> Members = new();

	/// <summary>How many responders were sent. Zero means the call went out and
	/// nobody could come.</summary>
	public int Expected;

	/// <summary>Where the responders gather: the anchor's hold point.</summary>
	public int RallyX, RallyY;

	/// <summary>False while gathering; true once the squad moves on the LKP.</summary>
	public bool Go;

	/// <summary>Milli-ticks spent gathering, against BackupWaitMaxTicks.</summary>
	public int WaitMt;

	/// <summary>Where the assault was planned to, and how long before a moved
	/// LKP may re-plan it.</summary>
	public int PlannedX, PlannedY;
	public int ReplanMt;

	public void HashInto(ref Hash64 h)
	{
		h.Add(Id); h.Add(Anchor); h.Add(Expected); h.Add(RallyX); h.Add(RallyY);
		h.Add(Go); h.Add(WaitMt); h.Add(PlannedX); h.Add(PlannedY); h.Add(ReplanMt);
		h.Add(Members.Count);
		for (int i = 0; i < Members.Count; i++) h.Add(Members[i]);
	}
}

/// <summary>
/// Guards sweeping a compromised level together (Guard_AI.md §6.2). Members
/// are guard indices; [0] leads and faces the way they go, [1] follows at
/// PairSpacing and watches their backs, [2] (a trio) watches a flank. The
/// trail is where the leader has been, so the others follow him round
/// corners instead of cutting them.
/// </summary>
public sealed class SweepGroup
{
	public int Id;
	public readonly List<int> Members = new();

	/// <summary>Kept near the exit rather than roaming the level.</summary>
	public bool Exit;

	/// <summary>The sweep node claimed and being walked to, or -1.</summary>
	public int Target = -1;

	/// <summary>Standing at the node looking round, rather than walking to it.</summary>
	public bool Dwelling;

	/// <summary>Milli-ticks in the current walk or dwell.</summary>
	public int PhaseMt;

	/// <summary>The leader's heading of travel, BRAD: what "back" means.</summary>
	public int Heading;

	public readonly List<int> TrailX = new(), TrailY = new();

	public void HashInto(ref Hash64 h)
	{
		h.Add(Id); h.Add(Exit); h.Add(Target); h.Add(Dwelling); h.Add(PhaseMt); h.Add(Heading);
		h.Add(Members.Count);
		for (int i = 0; i < Members.Count; i++) h.Add(Members[i]);
		h.Add(TrailX.Count);
		for (int i = 0; i < TrailX.Count; i++) { h.Add(TrailX[i]); h.Add(TrailY[i]); }
	}
}

/// <summary>
/// What the guards know TOGETHER (Guard_AI.md §5–§6): the radio net. Every
/// living guard is on it, so what one Combat guard sees, all of them fighting
/// know. What it never holds is where the player actually is: squads act on
/// <see cref="IntelX"/>/<see cref="IntelY"/> and its age, and a player who
/// moves unseen is not followed.
///
/// An INCIDENT is one fight: it begins when a guard first enters Combat and
/// ends when none are left in it. <see cref="Compromised"/> outlives every
/// incident; it is monotonic and ends only with the run.
/// </summary>
public sealed class GuardNet
{
	/// <summary>The level knows it has an intruder. Never cleared.</summary>
	public bool Compromised;

	/// <summary>A fight is on.</summary>
	public bool Active;

	/// <summary>A backup call has gone out during this incident, so the net
	/// already knows; a second lone guard joins rather than calls again, and
	/// losing the player compromises the level with no further report.</summary>
	public bool Called;

	/// <summary>Guards the player has killed during this incident. Each one
	/// brings another responder (Guard_AI.md §0: responders scale with threat).</summary>
	public int IncidentKills;

	/// <summary>The newest sighting, gunshot or hit, shared by everyone in the
	/// fight, and how long ago it was.</summary>
	public bool HasIntel;
	public int IntelX, IntelY;
	public int IntelAgeMt;

	public readonly List<Squad> Squads = new();
	public int NextSquadId;

	/// <summary>Sweep groups on a compromised level. Never cleared: the
	/// hunt outlives every incident.</summary>
	public readonly List<SweepGroup> Groups = new();
	public int NextGroupId;

	/// <summary>Where the level was compromised from (the last sighting, or
	/// the body): the sweep checks round there first.</summary>
	public bool HasFocus;
	public int FocusX, FocusY;

	public SweepGroup? GroupById(int id)
	{
		if (id < 0) return null;
		for (int i = 0; i < Groups.Count; i++) if (Groups[i].Id == id) return Groups[i];
		return null;
	}

	public void SetIntel(int x, int y)
	{
		HasIntel = true;
		IntelX = x; IntelY = y;
		IntelAgeMt = 0;
	}

	public void BeginIncident()
	{
		Active = true;
		Called = false;
		IncidentKills = 0;
		HasIntel = false;
		IntelAgeMt = 0;
	}

	public void EndIncident()
	{
		Active = false;
		Called = false;
		IncidentKills = 0;
		HasIntel = false;
		IntelAgeMt = 0;
		Squads.Clear();
	}

	public Squad? SquadById(int id)
	{
		if (id < 0) return null;
		for (int i = 0; i < Squads.Count; i++) if (Squads[i].Id == id) return Squads[i];
		return null;
	}

	public void HashInto(ref Hash64 h)
	{
		h.Add(Compromised); h.Add(Active); h.Add(Called); h.Add(IncidentKills);
		h.Add(HasIntel); h.Add(IntelX); h.Add(IntelY); h.Add(IntelAgeMt);
		h.Add(NextSquadId);
		h.Add(Squads.Count);
		for (int i = 0; i < Squads.Count; i++) Squads[i].HashInto(ref h);
		h.Add(NextGroupId); h.Add(HasFocus); h.Add(FocusX); h.Add(FocusY);
		h.Add(Groups.Count);
		for (int i = 0; i < Groups.Count; i++) Groups[i].HashInto(ref h);
	}
}
