using System.Collections.Generic;

namespace Cognitohazard.Sim;

/// <summary>
/// Guard AI v2 (Guard_AI.md): postures, tasks, the radio net, and movement.
/// Split out of SimWorld.cs so the guards' half of the tick has one home.
///
/// POSTURE (<see cref="GuardState"/>) is how alert a guard is; TASK
/// (<see cref="GuardTask"/>) is what he is doing about it. Posture only rises
/// through perception and falls only by an explicit, timed rule: an
/// investigation that found nothing (Curious → Relaxed), or contact lost
/// (Combat → Hunting). Hunting never falls: a compromised level stays
/// compromised until the run ends.
/// </summary>
public sealed partial class SimWorld
{
	/// <summary>What the guards know together: the radio net (Guard_AI.md §5–§6). Hashed.</summary>
	public readonly GuardNet Net = new();

	private readonly List<int> _scratchX = new(), _scratchY = new();

	private PathFinder Paths => _paths ??= new PathFinder(Level.Nav);

	// =====================================================================
	// Stimulus routing (spec §8.3, as remapped by Guard_AI.md §4–§5)
	// =====================================================================

	/// <summary>
	/// A heard or half-seen something at (x, y): raise awareness to at least
	/// <paramref name="awarenessTenths"/> and, if that is curious, go and look.
	/// A guard already in a fight keeps his better information and ignores it.
	/// </summary>
	internal void Notice(Actor e, int x, int y, int awarenessTenths)
	{
		if (e.Prone) return;
		if (awarenessTenths * Actor.Mt > e.AwAcc) e.SetAwareness(awarenessTenths);
		// Suspicion lingers after a noise exactly as after a glimpse (spec §8.2).
		e.GraceMt = Tune.GraceTicks * Actor.Mt;
		if (e.State == GuardState.Combat) return;
		e.SetLkp(x, y);
		if (e.Awareness >= Tune.AwCurious) StartLook(e, false);
	}

	/// <summary>
	/// Something is off: a Relaxed guard turns Curious; a Curious or Hunting
	/// one goes (back) to looking. <paramref name="sighted"/> means the
	/// stimulus is in plain view, which stops even a guard already walking
	/// over to investigate; a mere noise only retargets him.
	/// </summary>
	private void StartLook(Actor e, bool sighted)
	{
		if (e.State == GuardState.Relaxed)
		{
			e.State = GuardState.Curious;
			Log.Add(SimEventKind.Notice, e.X, e.Y);
			SetTask(e, GuardTask.Look);
			return;
		}
		if (e.State != GuardState.Curious && e.State != GuardState.Hunting) return;

		if (e.Task == GuardTask.Look) e.TaskMt = 0;
		else if (e.Task != GuardTask.Investigate || sighted) SetTask(e, GuardTask.Look);
	}

	private static void SetTask(Actor e, GuardTask t)
	{
		e.Task = t;
		e.TaskMt = 0;
	}

	/// <summary>
	/// Route a gunshot as a stimulus. The radius is the WEAPON's, not a global:
	/// how far a shot carries is the stealth cost of firing it, and the main
	/// axis separating the weapons (RPG plan §3). Hearing one is Combat
	/// (Guard_AI.md §5.1), not the old awareness 92.
	/// </summary>
	private void GunshotHeard(int x, int y, int radius)
	{
		// CHANGED at milestone 9. The prototype raised the floor alarm on every
		// shot before checking the radius, so no weapon could ever be fired
		// unheard and subsonic ammo would have been decorative. The alarm now
		// rises only if somebody was actually within earshot.
		bool heard = false;
		// Who was at ease when the shot rang out is decided BEFORE anyone
		// reacts: a guard shouted into the fight by the first man to hear it
		// still heard the shot himself, as a relaxed man (Guard_AI.md §4.1).
		_relaxedAtShot.Clear();
		for (int i = 0; i < Guards.Count; i++)
			if (!Guards[i].Prone && Guards[i].State == GuardState.Relaxed
				&& Fx.Dist(Guards[i].X, Guards[i].Y, x, y) < radius) _relaxedAtShot.Add(i);
		for (int i = 0; i < Guards.Count; i++)
		{
			var e = Guards[i];
			if (e.Prone) continue;
			if (Fx.Dist(e.X, e.Y, x, y) >= radius) continue;
			heard = true;
			EnterCombat(e, x, y, false);
		}
		for (int k = 0; k < _relaxedAtShot.Count; k++)
			FearRoll(Guards[_relaxedAtShot[k]], Tune.FearGunfireQ8, 1);
		if (heard) Alarm.Raise(2);
	}

	private readonly List<int> _relaxedAtShot = new();

	/// <summary>
	/// AFRAID (Guard_AI.md §4.1): roll against <paramref name="chanceQ8"/>, and
	/// on a failure freeze for FearTicks. A guard already frozen does not roll
	/// again: fear does not stack. Draws from the sim's RNG, so only when a roll
	/// is actually owed.
	/// </summary>
	private void FearRoll(Actor e, int chanceQ8, int cause)
	{
		if (e.Prone || e.Afraid) return;
		if (Rng.NextInt(Fx.One) >= chanceQ8) return;
		e.FearMt = Tune.FearTicks * Actor.Mt;
		Log.Add(SimEventKind.Afraid, e.X, e.Y, 0, cause);
	}

	/// <summary>
	/// A round landed on a guard who survived it. Combat, with the shooter where
	/// he stood when it hit: the one stimulus a silenced weapon cannot hide from
	/// its own target. A guard shot mid-call starts the call again.
	/// </summary>
	private void ShotAt(Actor e)
	{
		bool relaxed = e.State == GuardState.Relaxed;
		EnterCombat(e, Player.X, Player.Y, false);
		if (e.Task == GuardTask.Radio) e.RadioMt = 0;
		if (relaxed) FearRoll(e, Tune.FearGunfireQ8, 1);
	}

	/// <summary>
	/// A body is found (Guard_AI.md §7). Nothing tells the finder where the
	/// player is, so there is no fight to join: he radios it IN, regardless of
	/// allies, and when the report completes the level is compromised. Kill
	/// him mid-call and the body stays unreported.
	/// </summary>
	private void BodyFound(Actor finder, Actor body)
	{
		body.Found = true;
		Log.Add(SimEventKind.BodyFound, body.X, body.Y);

		// Nothing new to tell anyone: the floor is already hunting, or he is
		// already fighting. He goes and looks, or carries on.
		if (Net.Compromised)
		{
			if (finder.State != GuardState.Combat)
			{
				if (finder.AwAcc < Tune.AwHunt * Actor.Mt) finder.SetAwareness(Tune.AwHunt);
				finder.SetLkp(body.X, body.Y);
				StartLook(finder, true);
			}
			return;
		}
		if (finder.State == GuardState.Combat) return;

		finder.State = GuardState.Combat;
		if (finder.AwAcc < Tune.AwEngage * Actor.Mt) finder.SetAwareness(Tune.AwEngage);
		finder.SetLkp(body.X, body.Y);
		finder.SquadId = -1;
		BeginRadio(finder, RadioPurpose.Report);
	}

	/// <summary>
	/// A guard is shot or subdued. Any call he was making dies with him, and a
	/// kill during a fight brings one more responder to the next call.
	/// </summary>
	private void OnGuardDown(Actor e, bool killed)
	{
		if (e.Task == GuardTask.Radio)
			Log.Add(SimEventKind.RadioCut, e.X, e.Y, 0, (int)e.Radio);
		e.Radio = RadioPurpose.None;
		e.RadioMt = 0;
		e.Task = GuardTask.None;
		e.SquadId = -1;
		e.RouteX.Clear(); e.RouteY.Clear(); e.RouteIndex = 0; e.WaitMt = 0;
		LeaveGroup(e);
		e.FearMt = 0;
		if (killed && Net.Active) Net.IncidentKills++;

		// Anyone who SAW him die (in his cone, in range, a clear line) makes a
		// fear roll; a man already in the fight mostly keeps his head.
		if (!killed) return;
		for (int i = 0; i < Guards.Count; i++)
		{
			var g = Guards[i];
			if (g == e || g.Prone || g.Afraid) continue;
			int range = Perception.RangeFor(g, Alarm.ConeRangeBonus);
			if (Perception.SeesPoint(Opaque, g, e.X, e.Y, range, Perception.HalfAngleFor(g)) <= 0) continue;
			FearRoll(g, g.State == GuardState.Combat ? Tune.FearAllyDeathCombatQ8 : Tune.FearAllyDeathQ8, 2);
		}
	}

	// =====================================================================
	// Combat entry, allies and the radio (Guard_AI.md §5.1–§5.4)
	// =====================================================================

	/// <summary>
	/// Put a guard in the fight with the threat at (x, y). Unless he was pulled
	/// in by an ally's shout, he first asks "am I alone?": allies within
	/// walking range are shouted in with him; a guard with none radios.
	/// </summary>
	internal void EnterCombat(Actor e, int x, int y, bool viaShout)
	{
		if (e.Prone) return;
		if (!Net.Active) Net.BeginIncident();
		// Only FRESH information ages the intel back to zero. A shout or a
		// dispatch passes on what the net already knows, however old: letting
		// it refresh the age let a searching guard's stale shouts keep a lost
		// fight alive forever.
		if (!viaShout || !Net.HasIntel) Net.SetIntel(x, y);
		e.SetLkp(x, y);
		if (e.AwAcc < Tune.AwEngage * Actor.Mt) e.SetAwareness(Tune.AwEngage);
		if (e.State == GuardState.Combat) return;

		LeaveGroup(e);
		e.State = GuardState.Combat;
		SetTask(e, GuardTask.Converge);
		e.SearchMt = 0;
		e.HasSearchPt = false;
		e.SnapMt = 0;
		e.Radio = RadioPurpose.None;
		e.RadioMt = 0;
		e.SquadId = -1;
		if (viaShout) return;

		bool allies = ShoutToAllies(e);
		if (!allies && !Net.Called && !BackupCallPending(e)) BeginRadio(e, RadioPurpose.Backup);
	}

	/// <summary>
	/// Every living guard within <see cref="Tune.AllyPathCost"/> of PATH is an
	/// ally: a guard behind a wall 50 px away but 600 px round is not. Allies
	/// not yet fighting are shouted in. True if there was anyone at all.
	/// </summary>
	private bool ShoutToAllies(Actor e)
	{
		var nav = Level.Nav;
		int cell = nav.NearestPassable(e.X, e.Y);
		if (cell < 0) return false;
		Paths.Flood(cell, Tune.AllyPathCost);

		bool any = false;
		for (int i = 0; i < Guards.Count; i++)
		{
			var o = Guards[i];
			if (o == e || o.Prone) continue;
			if (Paths.FloodCost(nav.NearestPassable(o.X, o.Y)) < 0) continue;
			any = true;
		}
		if (!any) return false;

		// A second pass, because EnterCombat below may itself flood and would
		// overwrite the costs the first pass read.
		Alarm.Raise(2);
		_scratchX.Clear();
		for (int i = 0; i < Guards.Count; i++)
		{
			var o = Guards[i];
			if (o == e || o.Prone || o.State == GuardState.Combat) continue;
			if (Paths.FloodCost(nav.NearestPassable(o.X, o.Y)) >= 0) _scratchX.Add(i);
		}
		for (int k = 0; k < _scratchX.Count; k++)
			EnterCombat(Guards[_scratchX[k]], e.LkpX, e.LkpY, true);
		return true;
	}

	/// <summary>Another guard is already keying a backup call: one is enough.</summary>
	private bool BackupCallPending(Actor except)
	{
		for (int i = 0; i < Guards.Count; i++)
		{
			var o = Guards[i];
			if (o != except && !o.Prone && o.Radio == RadioPurpose.Backup) return true;
		}
		return false;
	}

	private void BeginRadio(Actor e, RadioPurpose purpose)
	{
		e.Radio = purpose;
		e.RadioMt = 0;
		SetTask(e, GuardTask.Radio);
		Log.Add(SimEventKind.RadioStart, e.X, e.Y, 0, (int)purpose);
	}

	/// <summary>The call went through (Guard_AI.md §5.3, §7).</summary>
	private void CompleteRadio(Actor e)
	{
		var purpose = e.Radio;
		e.Radio = RadioPurpose.None;
		e.RadioMt = 0;
		Log.Add(SimEventKind.RadioSent, e.X, e.Y, 0, (int)purpose);

		if (purpose == RadioPurpose.Report)
		{
			Compromise(e.LkpX, e.LkpY);
			EnterHunting(e, true);
			return;
		}

		Net.Called = true;
		Alarm.Raise(2);
		DispatchBackup(e);
	}

	/// <summary>
	/// Choose and send the responders (Guard_AI.md §5.3): 2 + one per kill this
	/// incident, capped at 5, nearest by PATH from the caller, patrollers before
	/// sentries, ties to the lower index. They gather at the caller's hold
	/// point; he goes and waits there.
	/// </summary>
	private void DispatchBackup(Actor caller)
	{
		int ci = Guards.IndexOf(caller);
		FindHoldPoint(caller);

		var sq = new Squad
		{
			Id = Net.NextSquadId++,
			Anchor = ci,
			RallyX = caller.TaskX,
			RallyY = caller.TaskY,
		};
		sq.Members.Add(ci);
		caller.SquadId = sq.Id;
		Net.Squads.Add(sq);

		int want = Tune.ResponderBase + Net.IncidentKills * Tune.ResponderPerKill;
		if (want > Tune.ResponderCap) want = Tune.ResponderCap;

		var nav = Level.Nav;
		int cc = nav.NearestPassable(caller.X, caller.Y);
		var picked = new List<(int Sentry, int Cost, int Index)>();
		if (cc >= 0)
		{
			Paths.Flood(cc, int.MaxValue);
			for (int i = 0; i < Guards.Count; i++)
			{
				var g = Guards[i];
				if (i == ci || g.Prone || g.SquadId >= 0) continue;
				if (g.Task == GuardTask.Engage || g.Task == GuardTask.Radio) continue;   // busy
				if (g.Afraid) continue;                                                // frozen
				int cost = Paths.FloodCost(nav.NearestPassable(g.X, g.Y));
				if (cost < 0) continue;                                                // cannot get there
				var key = (g.PathX == null ? 1 : 0, cost, i);
				int at = picked.Count;
				while (at > 0 && Before(key, picked[at - 1])) at--;
				picked.Insert(at, key);
			}
		}

		int n = picked.Count < want ? picked.Count : want;
		for (int k = 0; k < n; k++)
		{
			int i = picked[k].Index;
			var g = Guards[i];
			EnterCombat(g, Net.IntelX, Net.IntelY, true);
			SetTask(g, GuardTask.Rally);
			g.SquadId = sq.Id;
			sq.Members.Add(i);
		}
		sq.Expected = n;
		SetTask(caller, GuardTask.HoldForBackup);
	}

	private static bool Before((int Sentry, int Cost, int Index) a, (int Sentry, int Cost, int Index) b)
		=> a.Sentry != b.Sentry ? a.Sentry < b.Sentry
		 : a.Cost != b.Cost ? a.Cost < b.Cost
		 : a.Index < b.Index;

	/// <summary>
	/// Where a lone caller waits (Guard_AI.md §5.4, the simple version of
	/// cover): the nearest reachable cell within HoldSearchCells with NO line
	/// of sight to the LKP, preferring one against a wall. He faces the way the
	/// threat would have to come: the first leg of a path from there to the LKP.
	/// Sets TaskX/TaskY/TaskFacing.
	/// </summary>
	private void FindHoldPoint(Actor e)
	{
		var nav = Level.Nav;
		int tx = Net.HasIntel ? Net.IntelX : e.LkpX, ty = Net.HasIntel ? Net.IntelY : e.LkpY;
		e.TaskX = e.X; e.TaskY = e.Y;
		e.TaskFacing = Brad.Atan2(ty - e.Y, tx - e.X);

		int start = nav.NearestPassable(e.X, e.Y);
		if (start < 0) return;
		Paths.Flood(start, Tune.HoldSearchCells * NavGrid.CostDiagonal);

		int sc = start % nav.W, sr = start / nav.W;
		int best = -1, bestWall = 2, bestCost = int.MaxValue;
		for (int r = sr - Tune.HoldSearchCells; r <= sr + Tune.HoldSearchCells; r++)
		{
			for (int c = sc - Tune.HoldSearchCells; c <= sc + Tune.HoldSearchCells; c++)
			{
				if (!nav.InBounds(c, r)) continue;
				int i = r * nav.W + c;
				int cost = Paths.FloodCost(i);
				if (cost < 0) continue;
				if (Geometry.ClearLine(Opaque, nav.NodeX[i], nav.NodeY[i], tx, ty)) continue;
				int wall = AgainstWall(nav, c, r) ? 0 : 1;
				// Row-major scan: an equal cell later in the scan never wins.
				if (wall < bestWall || (wall == bestWall && cost < bestCost))
				{
					best = i; bestWall = wall; bestCost = cost;
				}
			}
		}
		if (best < 0) return;

		e.TaskX = nav.NodeX[best]; e.TaskY = nav.NodeY[best];
		if (Paths.Plan(e.TaskX, e.TaskY, tx, ty, true, _scratchX, _scratchY, out _) && _scratchX.Count > 0)
			e.TaskFacing = Brad.Atan2(_scratchY[0] - e.TaskY, _scratchX[0] - e.TaskX);
		else
			e.TaskFacing = Brad.Atan2(ty - e.TaskY, tx - e.TaskX);
	}

	private static bool AgainstWall(NavGrid nav, int c, int r)
		=> !nav.InBounds(c - 1, r) || !nav.Passable[r * nav.W + c - 1]
		|| !nav.InBounds(c + 1, r) || !nav.Passable[r * nav.W + c + 1]
		|| !nav.InBounds(c, r - 1) || !nav.Passable[(r - 1) * nav.W + c]
		|| !nav.InBounds(c, r + 1) || !nav.Passable[(r + 1) * nav.W + c];

	// =====================================================================
	// Contact lost and the compromised level (Guard_AI.md §5.6, §6)
	// =====================================================================

	/// <summary>
	/// A Combat guard has searched the LKP and found nothing. If anyone on the
	/// net saw the player recently, he goes there instead. Otherwise the fight
	/// is over and the player is missing: the level is compromised, if the net
	/// already knows (a backup call went out), or he radios it in first.
	/// </summary>
	private void ContactLost(Actor e)
	{
		if (Net.HasIntel && Net.IntelAgeMt < Tune.ContactLostTicks * Actor.Mt
			&& Fx.Dist(Net.IntelX, Net.IntelY, e.LkpX, e.LkpY) >= Tune.LkpReach)
		{
			SetTask(e, GuardTask.Converge);
			return;
		}
		if (Net.Compromised || Net.Called)
		{
			CombatTarget(e, out int fx, out int fy);
			Compromise(fx, fy);
			EnterHunting(e, true);
			return;
		}
		BeginRadio(e, RadioPurpose.Report);
	}

	/// <summary>The sweep's nodes, built the moment the level is first
	/// compromised (Guard_AI.md §6.3). Null before. Hashed when it exists.</summary>
	public SweepMap? Sweep { get; private set; }

	/// <summary>
	/// The level knows it has an intruder, for the rest of the run, and the hunt
	/// starts from (<paramref name="fx"/>, <paramref name="fy"/>). Sentries hold
	/// their posts. Patrollers pair up (Guard_AI.md §6.1): greedy by PATH from
	/// the lowest index, an odd man out joins the nearest pair as a trio, and
	/// with enough of them the pair nearest the exit is kept there. Guards
	/// still fighting join the hunt as each loses contact.
	/// </summary>
	internal void Compromise(int fx, int fy)
	{
		if (Net.Compromised) return;
		Net.Compromised = true;
		Net.HasFocus = true;
		Net.FocusX = fx; Net.FocusY = fy;
		Alarm.Compromise();
		Log.Add(SimEventKind.Compromised, fx, fy);
		Sweep ??= new SweepMap(Level);

		var mobile = new List<int>();
		for (int i = 0; i < Guards.Count; i++)
		{
			var g = Guards[i];
			if (g.Prone || g.State == GuardState.Combat) continue;
			EnterHunting(g, false, assign: false);
			if (g.PathX != null) mobile.Add(i);
		}
		FormGroups(mobile);
		for (int k = 0; k < mobile.Count; k++) SetTask(Guards[mobile[k]], HuntingTask(Guards[mobile[k]]));
	}

	private GuardTask HuntingTask(Actor e)
	{
		var grp = Net.GroupById(e.GroupId);
		if (grp != null) return grp.Exit ? GuardTask.WatchExit : GuardTask.Sweep;
		return e.PathX != null ? GuardTask.Sweep : GuardTask.HoldPost;
	}

	/// <summary>
	/// Start hunting. A guard who walked (a patroller) or who has just been
	/// fighting is MOBILE and sweeps in a group; a sentry holds his post.
	/// </summary>
	private void EnterHunting(Actor e, bool fromCombat, bool assign = true)
	{
		e.State = GuardState.Hunting;
		e.SquadId = -1;
		e.RouteX.Clear(); e.RouteY.Clear(); e.RouteIndex = 0; e.WaitMt = 0;
		e.Radio = RadioPurpose.None;
		e.RadioMt = 0;
		e.HasSearchPt = false;
		if (assign && e.GroupId < 0 && Net.Compromised && (e.PathX != null || fromCombat)) AssignToGroup(e);
		SetTask(e, HuntingTask(e));
	}

	// =====================================================================
	// Sweep groups (Guard_AI.md §6.1–6.2)
	// =====================================================================

	private SweepGroup NewGroup()
	{
		var grp = new SweepGroup { Id = Net.NextGroupId++ };
		Net.Groups.Add(grp);
		return grp;
	}

	private void Join(SweepGroup grp, int index)
	{
		grp.Members.Add(index);
		Guards[index].GroupId = grp.Id;
	}

	private void DropGroup(SweepGroup grp)
	{
		Sweep?.Release(grp.Id);
		Net.Groups.Remove(grp);
	}

	/// <summary>Path cost from a guard to every cell; false if he stands nowhere walkable.</summary>
	private bool FloodFrom(Actor e)
	{
		int cell = Level.Nav.NearestPassable(e.X, e.Y);
		if (cell < 0) return false;
		Paths.Flood(cell, int.MaxValue);
		return true;
	}

	private int CostTo(Actor e) => Paths.FloodCost(Level.Nav.NearestPassable(e.X, e.Y));

	private void FormGroups(List<int> mobile)
	{
		var free = new List<int>(mobile);
		while (free.Count > 0)
		{
			int a = free[0];
			free.RemoveAt(0);
			var grp = NewGroup();
			Join(grp, a);
			if (!FloodFrom(Guards[a])) continue;
			int best = -1, bestCost = int.MaxValue;
			for (int k = 0; k < free.Count; k++)
			{
				int cost = CostTo(Guards[free[k]]);
				if (cost >= 0 && cost < bestCost) { bestCost = cost; best = k; }
			}
			if (best < 0) continue;
			Join(grp, free[best]);
			free.RemoveAt(best);
		}

		// The odd man out makes a trio of the nearest pair.
		for (int gi = Net.Groups.Count - 1; gi >= 0; gi--)
		{
			var solo = Net.Groups[gi];
			if (solo.Members.Count == 1) TryMerge(solo);
		}

		// With enough men, the group nearest the exit watches it. On a level
		// with several, that is the one nearest the objective (Level.WatchedExit).
		if (mobile.Count >= Tune.ExitWatchMinMobile && Net.Groups.Count >= 2)
		{
			int exitCell = Level.Nav.NearestPassable(Level.WatchedExit.X + Level.WatchedExit.W / 2, Level.WatchedExit.Y + Level.WatchedExit.H / 2);
			if (exitCell >= 0)
			{
				Paths.Flood(exitCell, int.MaxValue);
				SweepGroup? near = null;
				int nearCost = int.MaxValue;
				for (int gi = 0; gi < Net.Groups.Count; gi++)
				{
					int cost = CostTo(Guards[Net.Groups[gi].Members[0]]);
					if (cost >= 0 && cost < nearCost) { nearCost = cost; near = Net.Groups[gi]; }
				}
				if (near != null) near.Exit = true;
			}
		}
	}

	/// <summary>
	/// A guard joining the hunt late (he was fighting) pairs with the nearest
	/// man sweeping alone, or failing that makes a trio of the nearest pair, or
	/// failing that sweeps alone until someone joins him.
	/// </summary>
	private void AssignToGroup(Actor e)
	{
		int me = Guards.IndexOf(e);
		SweepGroup? best = null;
		int bestSize = int.MaxValue, bestCost = int.MaxValue;
		if (FloodFrom(e))
		{
			for (int gi = 0; gi < Net.Groups.Count; gi++)
			{
				var grp = Net.Groups[gi];
				if (grp.Members.Count >= 3) continue;
				int cost = CostTo(Guards[grp.Members[0]]);
				if (cost < 0) continue;
				int size = grp.Members.Count;
				if (size < bestSize || (size == bestSize && cost < bestCost)) { best = grp; bestSize = size; bestCost = cost; }
			}
		}
		Join(best ?? NewGroup(), me);
	}

	/// <summary>A group left with one man folds him into the nearest group with room.</summary>
	private void TryMerge(SweepGroup solo)
	{
		if (solo.Members.Count != 1) return;
		int s0 = solo.Members[0];
		if (!FloodFrom(Guards[s0])) return;
		SweepGroup? best = null;
		int bestCost = int.MaxValue;
		for (int gi = 0; gi < Net.Groups.Count; gi++)
		{
			var grp = Net.Groups[gi];
			if (grp == solo || grp.Members.Count >= 3) continue;
			int cost = CostTo(Guards[grp.Members[0]]);
			if (cost >= 0 && cost < bestCost) { bestCost = cost; best = grp; }
		}
		if (best == null) return;
		DropGroup(solo);
		Join(best, s0);
		if (Guards[s0].State == GuardState.Hunting && Guards[s0].Task != GuardTask.Look
			&& Guards[s0].Task != GuardTask.Investigate && Guards[s0].Task != GuardTask.LookAround)
			SetTask(Guards[s0], HuntingTask(Guards[s0]));
	}

	/// <summary>Out of his group: down, dead, or back in a fight.</summary>
	private void LeaveGroup(Actor e)
	{
		var grp = Net.GroupById(e.GroupId);
		e.GroupId = -1;
		if (grp == null) return;
		grp.Members.Remove(Guards.IndexOf(e));
		if (grp.Members.Count == 0) { DropGroup(grp); return; }
		if (grp.Members.Count == 1) TryMerge(grp);
	}

	/// <summary>
	/// Pick the node a group sweeps next: unclaimed, reachable, and highest on
	/// staleness less walking cost, plus the focus and authored bonuses. The
	/// exit group only takes nodes near the exit, and with none there it
	/// stands on the exit itself (Target -2).
	/// </summary>
	private bool PickNode(SweepGroup grp)
	{
		var map = Sweep;
		if (map == null || !FloodFrom(Guards[grp.Members[0]])) return false;
		int ex = Level.WatchedExit.X + Level.WatchedExit.W / 2, ey = Level.WatchedExit.Y + Level.WatchedExit.H / 2;
		int exitR = Tune.ExitWatchCells * Level.CellFx;
		int focusR = Tune.SweepFocusCells * Level.CellFx;

		int best = -1;
		long bestScore = long.MinValue;
		for (int k = 0; k < map.Count; k++)
		{
			if (map.ClaimedBy[k] >= 0) continue;
			if (grp.Exit && Fx.Dist(map.X[k], map.Y[k], ex, ey) > exitR) continue;
			int cost = Paths.FloodCost(map.Cell[k]);
			if (cost < 0) continue;
			long score = map.Stale[k] - (long)cost * Tune.SweepDistWeight * Actor.Mt;
			if (map.Authored[k]) score += (long)Tune.AuthoredNodeBonusTicks * Actor.Mt;
			if (Net.HasFocus)
			{
				int d = Fx.Dist(map.X[k], map.Y[k], Net.FocusX, Net.FocusY);
				if (d < focusR) score += (long)Tune.SweepFocusBonusTicks * Actor.Mt * (focusR - d) / focusR;
			}
			if (score > bestScore) { bestScore = score; best = k; }
		}
		if (best >= 0)
		{
			map.ClaimedBy[best] = grp.Id;
			grp.Target = best;
			return true;
		}
		if (grp.Exit) { grp.Target = -2; return true; }
		return false;
	}

	private bool SweepTarget(SweepGroup grp, out int x, out int y)
	{
		x = 0; y = 0;
		if (grp.Target >= 0 && Sweep != null) { x = Sweep.X[grp.Target]; y = Sweep.Y[grp.Target]; return true; }
		if (grp.Target == -2) { x = Level.WatchedExit.X + Level.WatchedExit.W / 2; y = Level.WatchedExit.Y + Level.WatchedExit.H / 2; return true; }
		return false;
	}

	/// <summary>
	/// The sweep map's tick: nodes go stale, any guard with one in his cone and
	/// in range makes it fresh, and each group walks to its node, looks round,
	/// and picks the next.
	/// </summary>
	private void StepSweep(int w)
	{
		var map = Sweep;
		if (map == null) return;

		for (int k = 0; k < map.Count; k++)
		{
			map.Stale[k] += w;
			if (map.Stale[k] > SweepMap.StaleCap) map.Stale[k] = SweepMap.StaleCap;
		}
		long rr = (long)Tune.SweepSeeRange * Tune.SweepSeeRange;
		for (int i = 0; i < Guards.Count; i++)
		{
			var g = Guards[i];
			if (g.Prone || (g.State != GuardState.Hunting && g.State != GuardState.Combat)) continue;
			int half = Perception.HalfAngleFor(g);
			for (int k = 0; k < map.Count; k++)
			{
				if (map.Stale[k] == 0 || Fx.DistSq(g.X, g.Y, map.X[k], map.Y[k]) > rr) continue;
				if (Perception.SeesPoint(Opaque, g, map.X[k], map.Y[k], Tune.SweepSeeRange, half) > 0) map.Stale[k] = 0;
			}
		}

		for (int gi = 0; gi < Net.Groups.Count; gi++)
		{
			var grp = Net.Groups[gi];
			var lead = Guards[grp.Members[0]];
			if (lead.Task != GuardTask.Sweep && lead.Task != GuardTask.WatchExit) continue;   // he is off looking at something
			grp.PhaseMt += w;
			if (grp.Dwelling)
			{
				if (grp.PhaseMt < Tune.NodeDwellTicks * Actor.Mt) continue;
				grp.Dwelling = false;
				map.Release(grp.Id);
				grp.Target = -1;
				grp.PhaseMt = 0;
			}
			if (grp.Target == -1)
			{
				grp.PhaseMt = 0;
				// Nowhere left to go: stand and look round, then ask again.
				if (!PickNode(grp)) grp.Dwelling = true;
				continue;
			}
			if (SweepTarget(grp, out int tx, out int ty) && Fx.Dist(lead.X, lead.Y, tx, ty) < Tune.NodeReach)
			{
				grp.Dwelling = true;
				grp.PhaseMt = 0;
			}
			else if (grp.PhaseMt >= Tune.NodeTravelMaxTicks * Actor.Mt)
			{
				map.Release(grp.Id);        // could not get there: give it up
				grp.Target = -1;
				grp.PhaseMt = 0;
			}
		}
	}

	/// <summary>
	/// The net's own tick, after every guard has moved: intel ages, squads
	/// gather and go, and a fight with nobody left in it ends.
	/// </summary>
	private void StepNet(int w)
	{
		if (Net.HasIntel) Net.IntelAgeMt += w;
		StepSweep(w);

		for (int s = 0; s < Net.Squads.Count; s++)
		{
			var sq = Net.Squads[s];
			if (sq.Go) continue;
			sq.WaitMt += w;

			int arrived = 0, live = 0;
			bool engaged = false;
			for (int m = 0; m < sq.Members.Count; m++)
			{
				var g = Guards[sq.Members[m]];
				if (g.Prone || g.SquadId != sq.Id) continue;
				if (g.Task == GuardTask.Engage) engaged = true;
				if (sq.Members[m] == sq.Anchor) continue;
				live++;
				if (Fx.Dist(g.X, g.Y, sq.RallyX, sq.RallyY) < Tune.RallyRadius) arrived++;
			}

			int need = sq.Expected < 2 ? sq.Expected : 2;
			bool timeUp = sq.WaitMt >= Tune.BackupWaitMaxTicks * Actor.Mt;
			// Nobody is coming: the call went out and nobody could, or every
			// responder died on the way. The anchor holds until contact is lost.
			if (live == 0 && !engaged) { sq.Expected = 0; continue; }
			if (engaged || (sq.Expected > 0 && arrived >= need) || (timeUp && arrived > 0))
			{
				sq.Go = true;
				// Synchronised only from a standing start: once one of them is
				// already shooting, nobody waits.
				PlanAssault(sq, !engaged);
			}
		}

		// A squad on the move re-plans when the LKP it is going for moves, at
		// most once per FlankReplanTicks.
		for (int s = 0; s < Net.Squads.Count; s++)
		{
			var sq = Net.Squads[s];
			if (!sq.Go) continue;
			if (sq.ReplanMt > 0) { sq.ReplanMt -= w; continue; }
			if (Net.HasIntel && Fx.Dist(Net.IntelX, Net.IntelY, sq.PlannedX, sq.PlannedY) > Tune.FlankReplanDist)
				PlanAssault(sq, false);
		}

		bool anyCombat = false;
		for (int i = 0; i < Guards.Count; i++)
			if (!Guards[i].Prone && Guards[i].State == GuardState.Combat) { anyCombat = true; break; }
		if (Net.Active && !anyCombat)
		{
			Net.EndIncident();
			for (int i = 0; i < Guards.Count; i++) Guards[i].SquadId = -1;
		}
	}

	// =====================================================================
	// The guard tick (spec §8, Guard_AI.md §2)
	// =====================================================================

	private void StepGuards(int wScale)
	{
		if (wScale <= 0) return;

		for (int i = 0; i < Guards.Count; i++) StepGuard(Guards[i], wScale);
		StepNet(wScale);
		ServePaths();
	}

	/// <summary>
	/// Is <paramref name="t"/> something a guard in posture <paramref name="s"/>
	/// can be doing (Guard_AI.md §2)? Down and Dead fit anything: nothing about
	/// a body is read.
	/// </summary>
	internal static bool TaskFits(GuardState s, GuardTask t) => s switch
	{
		GuardState.Relaxed => t == GuardTask.Patrol || t == GuardTask.Post,
		GuardState.Curious => t == GuardTask.Look || t == GuardTask.Investigate
			|| t == GuardTask.LookAround || t == GuardTask.Return,
		GuardState.Combat => t == GuardTask.Engage || t == GuardTask.Converge || t == GuardTask.SearchLkp
			|| t == GuardTask.Radio || t == GuardTask.HoldForBackup || t == GuardTask.Rally
			|| t == GuardTask.Assault,
		GuardState.Hunting => t == GuardTask.Look || t == GuardTask.Investigate || t == GuardTask.LookAround
			|| t == GuardTask.Sweep || t == GuardTask.HoldPost || t == GuardTask.WatchExit,
		GuardState.Down or GuardState.Dead => true,
		_ => false,
	};

	/// <summary>
	/// How many times <see cref="NormaliseGuard"/> has had to repair a guard.
	/// Diagnostic, NOT hashed. Normal play never needs a repair, and
	/// tests/Fuzz.cs asserts this stays at zero, so the repair cannot quietly
	/// cover for a real bug; it exists for state injected from outside the rules
	/// (tests, a future debug tool) and for tests/Exhaustive.cs to prove the sim
	/// total over guard state, as the parsers are total over text.
	/// </summary>
	public int Normalised { get; private set; }

	/// <summary>
	/// Bring a guard back inside the rules before he acts: a posture/task pair
	/// that cannot occur, a squad or group id that names nothing (or a squad
	/// out of Combat, a group out of Hunting), a radio purpose on a guard not
	/// fighting, or an index outside the list it indexes. Each repair takes the
	/// posture's own default, so the guard carries on rather than freezing.
	/// </summary>
	private void NormaliseGuard(Actor e)
	{
		bool repaired = false;
		int me = Guards.IndexOf(e);

		if (e.State < GuardState.Relaxed || e.State > GuardState.Dead) { e.State = GuardState.Relaxed; repaired = true; }
		if (e.Prone) { if (repaired) Normalised++; return; }

		if (e.SquadId >= 0)
		{
			var sq = Net.SquadById(e.SquadId);
			if (e.State != GuardState.Combat || sq == null || !sq.Members.Contains(me)) { e.SquadId = -1; repaired = true; }
		}
		if (e.GroupId >= 0)
		{
			var grp = Net.GroupById(e.GroupId);
			if (e.State != GuardState.Hunting || grp == null || !grp.Members.Contains(me))
			{
				if (grp != null && grp.Members.Contains(me)) LeaveGroup(e);
				else e.GroupId = -1;
				repaired = true;
			}
		}
		if (e.Radio < RadioPurpose.None || e.Radio > RadioPurpose.Report
			|| (e.Radio != RadioPurpose.None && e.State != GuardState.Combat))
		{
			e.Radio = RadioPurpose.None; e.RadioMt = 0; repaired = true;
		}
		if (e.RadioMt < 0 || e.RadioMt > Tune.RadioTicks * Actor.Mt) { e.RadioMt = 0; repaired = true; }
		if (e.FearMt < 0 || e.FearMt > Tune.FearTicks * Actor.Mt) { e.FearMt = 0; repaired = true; }
		if ((int)e.Weapon < 0 || (int)e.Weapon >= WeaponCatalog.Count) { e.Weapon = WeaponId.Glock; repaired = true; }
		{
			var spec = WeaponCatalog.Get(e.Weapon);
			if (e.Mag < 0 || e.Mag > spec.Magazine) { e.Mag = spec.Magazine; repaired = true; }
			if (e.BurstShots < 0 || e.BurstShots >= GuardBurst(in spec)) { e.BurstShots = 0; repaired = true; }
			if (e.ReloadMt < 0 || e.ReloadMt > spec.ReloadTicks * Actor.Mt) { e.ReloadMt = 0; repaired = true; }
			int spinCap = spec.SpinUpTicks * Actor.Mt;
			if (e.SpinMt < 0 || e.SpinMt > spinCap) { e.SpinMt = 0; repaired = true; }
		}

		bool needsSquad = e.Task == GuardTask.HoldForBackup || e.Task == GuardTask.Rally || e.Task == GuardTask.Assault;
		if (!TaskFits(e.State, e.Task)
			|| (e.Task == GuardTask.Radio && e.Radio == RadioPurpose.None)
			|| (needsSquad && e.SquadId < 0)
			|| (e.Task == GuardTask.WatchExit && e.GroupId < 0))
		{
			SetTask(e, e.State switch
			{
				GuardState.Relaxed => e.PathX != null ? GuardTask.Patrol : GuardTask.Post,
				GuardState.Curious => GuardTask.Look,
				GuardState.Combat => GuardTask.Converge,
				_ => HuntingTask(e),
			});
			repaired = true;
		}

		if (e.NavIndex < 0 || e.NavIndex > e.NavX.Count || e.NavX.Count != e.NavY.Count)
		{
			e.NavX.Clear(); e.NavY.Clear(); e.NavIndex = 0; repaired = true;
		}
		if (e.RouteIndex < 0 || e.RouteIndex > e.RouteX.Count || e.RouteX.Count != e.RouteY.Count)
		{
			e.RouteX.Clear(); e.RouteY.Clear(); e.RouteIndex = 0; repaired = true;
		}
		if (e.PathX != null && (e.PathY == null || e.PathY.Length != e.PathX.Length || e.PathX.Length == 0))
		{
			e.PathX = null; e.PathY = null; repaired = true;
		}
		if (e.PathX != null && (e.WaypointIndex < 0 || e.WaypointIndex >= e.PathX.Length))
		{
			e.WaypointIndex = 0; repaired = true;
		}
		if (repaired) Normalised++;
	}

	private void StepGuard(Actor e, int w)
	{
		NormaliseGuard(e);
		if (e.Prone) return;

		var p = Player;
		int range = Perception.RangeFor(e, Alarm.ConeRangeBonus);
		int half = Perception.HalfAngleFor(e);
		bool alerted = Perception.IsAlerted(e.State);

		// ---- perception (spec §8.2, unchanged) ---------------------------
		int stim = 0;
		int q = p.Alive ? Perception.SeesPoint(Opaque, e, p.X, p.Y, range, half) : 0;
		// Light (lighting plan §3.1): darkness at the player shortens how far he
		// can make them out and slows him inside that. Identity when lit.
		if (q > 0 && Light != null)
			q = Perception.InLight(q, Fx.Dist(p.X, p.Y, e.X, e.Y), range, PlayerLightQ8);
		if (q > 0)
		{
			stim = Perception.StimulusPerTick(q, p.MovedFx > 0, p.MoveTier, alerted);
			// A lit torch is a beacon. This is the flashlight's cost, and
			// the only place gear makes the player easier to detect.
			int mul = Loadout.DetectionMul;
			if (mul != Fx.One) stim = (int)(((long)stim * mul) >> Fx.Shift);
		}

		if (p.NoiseRadius > 0 && e.Awareness < Tune.NoiseCap)
		{
			int d = Fx.Dist(p.X, p.Y, e.X, e.Y);
			int ns = Perception.NoiseStimulusPerTick(d, p.NoiseRadius);
			if (ns > stim) stim = ns;
		}

		for (int b = 0; b < Guards.Count && !e.Afraid; b++)
		{
			var body = Guards[b];
			if (body == e || body.Found || !body.Prone) continue;
			// A body in the dark is found late (plan §3.2): only as close as
			// the light on it allows, or his own torch.
			int bodyRange = Light == null ? Tune.BodyRange
				: Perception.DarkReach(Tune.BodyRange, BodyLight(e, body), Tune.BodyDarkRange);
			if (Perception.SeesPoint(Opaque, e, body.X, body.Y, bodyRange, half) > 0)
			{
				BodyFound(e, body);
				break;
			}
		}

		if (stim > 0)
		{
			e.AwAcc += (int)((long)stim * w / Fx.ScaleDen);
			if (e.AwAcc > Tune.AwCap * Actor.Mt) e.AwAcc = Tune.AwCap * Actor.Mt;
			e.GraceMt = Tune.GraceTicks * Actor.Mt;
			if (p.Alive) e.SetLkp(p.X, p.Y);
		}
		else if (e.GraceMt > 0)
		{
			// A partial sighting does not evaporate the instant it breaks.
			e.GraceMt -= w;
			if (e.GraceMt < 0) e.GraceMt = 0;
		}
		else
		{
			int floorAcc = Alarm.AwarenessFloor * Actor.Mt;
			e.AwAcc -= (int)((long)Tune.AwDecayPerSec * Actor.Mt * w
				/ (Fx.TicksPerSecond * Fx.ScaleDen));
			if (e.AwAcc < floorAcc) e.AwAcc = floorAcc;
		}

		bool hasLos = q > 0;

		// ---- afraid (Guard_AI.md §4.1) -----------------------------------
		// Frozen: he still sees and hears (the meter above keeps filling, the
		// LKP keeps updating), but he does not move, turn, shout, radio or
		// shoot, and nothing he perceives changes his posture until it passes.
		if (e.Afraid)
		{
			e.FearMt -= w;
			if (e.FearMt < 0) e.FearMt = 0;
			e.SnapMt = 0;
			if (e.Heat > 0)
			{
				e.Heat -= Fx.PerTick(WeaponCatalog.Get(e.Weapon).HeatDecayPerSec, w);
				if (e.Heat < 0) e.Heat = 0;
			}
			if (e.RecoilQ8 > 0) { e.RecoilQ8 -= Fx.PerTick(Fx.One * 7, w); if (e.RecoilQ8 < 0) e.RecoilQ8 = 0; }
			return;
		}

		// ---- snap sight (Guard_AI.md §5.1) -------------------------------
		// Close, in the cone and in plain view: recognised, not accumulated.
		// In the dark the snap range shrinks with the light, down to
		// DarkSeeRange (plan §8), or snap sight would undo the dark.
		int snapRange = Light == null ? Tune.SnapSightRange
			: Perception.DarkReach(Tune.SnapSightRange, PlayerLightQ8, Tune.DarkSeeRange);
		if (hasLos && e.State != GuardState.Combat
			&& Fx.Dist(p.X, p.Y, e.X, e.Y) <= snapRange)
		{
			e.SnapMt += w;
			if (e.SnapMt >= Tune.SnapReactTicks * Actor.Mt && e.AwAcc < Tune.AwEngage * Actor.Mt)
				e.SetAwareness(Tune.AwEngage);
		}
		else e.SnapMt = 0;

		// ---- posture transitions -----------------------------------------
		if (e.State != GuardState.Combat)
		{
			if (e.Awareness >= Tune.AwEngage && hasLos) EnterCombat(e, p.X, p.Y, false);
			else if (stim > 0 && e.Awareness >= Tune.AwCurious) StartLook(e, hasLos);
		}

		if (e.State == GuardState.Combat)
		{
			if (hasLos)
			{
				if (!Net.Active) Net.BeginIncident();
				Net.SetIntel(p.X, p.Y);
				e.SetLkp(p.X, p.Y);
				if (e.Task != GuardTask.Engage)
				{
					// Self-defence comes first, even mid-call: a call owed is
					// remembered (e.Radio) and re-keyed once he is out of sight.
					SetTask(e, GuardTask.Engage);
					e.AimMt = Tune.AimDelayTicks * Actor.Mt;
					e.HasSearchPt = false;
					Log.Add(SimEventKind.Alert, e.X, e.Y);
				}
			}
			else if (e.Task == GuardTask.Engage)
			{
				if (e.Radio != RadioPurpose.None) { SetTask(e, GuardTask.Radio); e.RadioMt = 0; }
				else SetTask(e, GuardTask.Converge);
			}
		}

		// ---- voice callouts (spec §8.3) ------------------------------------
		// Guards in a fight shout, and anyone who can hear it and see the
		// shouter joins in. A body being reported is a radio job, not a shout.
		// Only while the intel is fresh: a fight gone cold recruits nobody.
		e.CallMt -= w;
		if (e.State == GuardState.Combat && e.Radio != RadioPurpose.Report && e.CallMt <= 0
			&& Net.HasIntel && Net.IntelAgeMt < Tune.ContactLostTicks * Actor.Mt)
		{
			e.CallMt = Tune.CalloutTicks * Actor.Mt;
			Alarm.Raise(e.Task == GuardTask.Engage ? 2 : 1);
			int cx = Net.HasIntel ? Net.IntelX : e.LkpX;
			int cy = Net.HasIntel ? Net.IntelY : e.LkpY;
			for (int o = 0; o < Guards.Count; o++)
			{
				var other = Guards[o];
				if (other == e || other.Prone || other.State == GuardState.Combat) continue;
				if (Fx.Dist(other.X, other.Y, e.X, e.Y) < Tune.CalloutRange
					&& Geometry.ClearLine(Opaque, e.X, e.Y, other.X, other.Y))
					EnterCombat(other, cx, cy, true);
			}
		}

		// ---- behaviour ---------------------------------------------------
		int wasFacing = e.Facing, wasX = e.X, wasY = e.Y;

		bool held = false;
		switch (e.Task)
		{
			case GuardTask.Engage: held = Behave_Engage(e, w, hasLos); break;
			case GuardTask.Converge: Behave_Converge(e, w); break;
			case GuardTask.Assault: Behave_Assault(e, w); break;
			case GuardTask.SearchLkp: Behave_SearchLkp(e, w); break;
			case GuardTask.Radio: Behave_Radio(e, w); break;
			case GuardTask.HoldForBackup: Behave_Hold(e, w); break;
			case GuardTask.Rally: Behave_Rally(e, w); break;
			case GuardTask.Look: Behave_Look(e, w, stim > 0, hasLos); break;
			case GuardTask.Investigate: Behave_Investigate(e, w); break;
			case GuardTask.LookAround: Behave_LookAround(e, w); break;
			case GuardTask.Return: Behave_Return(e, w); break;
			case GuardTask.Sweep: Behave_Sweep(e, w); break;
			case GuardTask.WatchExit: Behave_Sweep(e, w); break;
			case GuardTask.HoldPost: Behave_HoldPost(e, w); break;
			case GuardTask.Post: Behave_Sentry(e, w); break;
			default: Behave_Patrol(e, w); break;
		}

		// Firing error, guard side. Measured AFTER the behaviour, from what the
		// guard actually did this tick: a guard who has just spun to face a
		// noise, or who is strafing while he shoots, has a disturbed weapon for
		// the same reason the player does.
		StepSway(e, Brad.Norm(e.Facing - wasFacing), Fx.Hypot(e.X - wasX, e.Y - wasY), w);

		// And the weapon: heat cools, reloads finish, a minigun winds down.
		StepGuardWeapon(e, w, held);

		if (e.RecoilQ8 > 0)
		{
			e.RecoilQ8 -= Fx.PerTick(Fx.One * 7, w);
			if (e.RecoilQ8 < 0) e.RecoilQ8 = 0;
		}
	}

	// =====================================================================
	// Curious (Guard_AI.md §4), and Hunting's side trips to a noise
	// =====================================================================

	/// <summary>
	/// Stop and stare. While the stimulus is still there he keeps staring, and
	/// a guard past AwHunt with the player in view closes in to HuntCloseDist,
	/// exactly as spec §8.1's Hunt-with-LOS did; that is what holds the §8.6
	/// curve. Once it has been gone CuriousLookTicks he walks over.
	/// </summary>
	private void Behave_Look(Actor e, int w, bool perceiving, bool hasLos)
	{
		var p = Player;
		if (perceiving) e.TaskMt = 0;
		else e.TaskMt += w;

		if (hasLos && e.Awareness >= Tune.AwHunt)
		{
			LookAt(e, Brad.Atan2(p.Y - e.Y, p.X - e.X), Tune.TurnHunt, w);
			if (Fx.Dist(p.X, p.Y, e.X, e.Y) > Tune.HuntCloseDist)
				Steer(e, p.X, p.Y, Tune.SpeedHunt, w);
		}
		else
		{
			int tx = e.HasLkp ? e.LkpX : e.HomeX;
			int ty = e.HasLkp ? e.LkpY : e.HomeY;
			LookAt(e, Brad.Atan2(ty - e.Y, tx - e.X), Tune.TurnCurious, w);
		}

		if (e.TaskMt >= Tune.CuriousLookTicks * Actor.Mt)
		{
			SetTask(e, GuardTask.Investigate);
			e.Hurry = e.Awareness >= Tune.AwHunt;
		}
	}

	/// <summary>Walk to the stimulus, hurrying if it was very suspicious when
	/// he set off. Decided once: the meter decays on the way, and a guard who
	/// ran at a gunshot-loud crash does not slow to a stroll halfway there.</summary>
	private void Behave_Investigate(Actor e, int w)
	{
		e.TaskMt += w;
		int tx = e.HasLkp ? e.LkpX : e.HomeX;
		int ty = e.HasLkp ? e.LkpY : e.HomeY;
		if (Fx.Dist(tx, ty, e.X, e.Y) < Tune.LkpReach || e.TaskMt >= Tune.InvestigateMaxTicks * Actor.Mt)
		{
			TryRestoreLights(e);
			SetTask(e, GuardTask.LookAround);
			e.TaskFacing = e.Facing;
			return;
		}
		int speed = e.Hurry ? Tune.SpeedHunt : Tune.SpeedInvestigate;
		Steer(e, tx, ty, speed, w);
	}

	/// <summary>Three headings: one side, the other, and back to where he came in facing.</summary>
	private void Behave_LookAround(Actor e, int w)
	{
		e.TaskMt += w;
		int third = Tune.LookAroundTicks * Actor.Mt / 3;
		int target = e.TaskMt < third ? e.TaskFacing + Tune.LookAroundArc
			: (e.TaskMt < 2 * third ? e.TaskFacing - Tune.LookAroundArc : e.TaskFacing);
		LookAt(e, target & Brad.Mask, Tune.TurnCurious, w);
		if (e.TaskMt < Tune.LookAroundTicks * Actor.Mt) return;

		e.HasLkp = false;
		if (e.State == GuardState.Hunting) { SetTask(e, HuntingTask(e)); return; }

		// Nothing there. Back to the route (its nearest waypoint) or the post.
		SetTask(e, GuardTask.Return);
		if (e.PathX != null && e.PathX.Length > 0)
		{
			int best = 0;
			long bestD = long.MaxValue;
			for (int i = 0; i < e.PathX.Length; i++)
			{
				long d = Fx.DistSq(e.X, e.Y, e.PathX[i], e.PathY![i]);
				if (d < bestD) { bestD = d; best = i; }
			}
			e.WaypointIndex = best;
			e.TaskX = e.PathX[best]; e.TaskY = e.PathY![best];
		}
		else
		{
			e.TaskX = e.HomeX; e.TaskY = e.HomeY;
		}
		e.TaskFacing = e.PostFacing;
	}

	/// <summary>
	/// Walk back and stand down. A patroller is Relaxed the moment he rejoins
	/// his route; a sentry once he is back on his post facing his old way.
	/// </summary>
	private void Behave_Return(Actor e, int w)
	{
		if (Fx.Dist(e.TaskX, e.TaskY, e.X, e.Y) >= Tune.WaypointReach)
		{
			Steer(e, e.TaskX, e.TaskY, Alarm.PatrolSpeed, w);
			return;
		}
		if (e.PathX != null)
		{
			e.State = GuardState.Relaxed;
			SetTask(e, GuardTask.Patrol);
			return;
		}
		LookAt(e, e.TaskFacing, Tune.TurnCurious, w);
		int off = Brad.Norm(e.Facing - e.TaskFacing);
		if (off < 0) off = -off;
		if (off < ReturnFacingSlack)
		{
			e.State = GuardState.Relaxed;
			SetTask(e, GuardTask.Post);
		}
	}

	/// <summary>About two degrees: TurnToward closes a gap proportionally and
	/// never quite lands.</summary>
	private const int ReturnFacingSlack = 400;

	// =====================================================================
	// Combat tasks (Guard_AI.md §5)
	// =====================================================================

	/// <summary>Where the fight is: the net's intel if it has any, else his own LKP.</summary>
	private void CombatTarget(Actor e, out int x, out int y)
	{
		if (Net.HasIntel) { x = Net.IntelX; y = Net.IntelY; }
		else if (e.HasLkp) { x = e.LkpX; y = e.LkpY; }
		else { x = e.HomeX; y = e.HomeY; }
	}

	/// <summary>Move on the LKP at hunt speed; reaching it without the player
	/// in view begins the search.</summary>
	private void Behave_Converge(Actor e, int w)
	{
		CombatTarget(e, out int tx, out int ty);
		if (Fx.Dist(tx, ty, e.X, e.Y) < Tune.LkpReach) { BeginSearch(e, tx, ty); return; }
		Steer(e, tx, ty, Tune.SpeedHunt, w);
	}

	private static void BeginSearch(Actor e, int tx, int ty)
	{
		e.SetLkp(tx, ty);
		SetTask(e, GuardTask.SearchLkp);
		e.SearchMt = Tune.SearchTicks * Actor.Mt;
		e.HasSearchPt = false;
		e.RouteX.Clear(); e.RouteY.Clear(); e.RouteIndex = 0; e.WaitMt = 0;
	}

	// =====================================================================
	// The squad assault: flank and synchronise (Guard_AI.md §5.5, P3)
	// =====================================================================

	private int[]? _penalty;
	private readonly List<int> _flankCells = new();
	private readonly List<(int Index, int Eta)> _etas = new();

	/// <summary>
	/// Plan a squad's move on the LKP. Members are planned in the order they
	/// joined, each by A* with <see cref="Tune.FlankPenalty"/> on every cell
	/// near an earlier member's route, so where the map offers a second way in
	/// the next man takes it; where it offers one door, they stack through it.
	/// With <paramref name="sync"/>, members on shorter routes hold back
	/// (at most EtaSyncMaxTicks) so the squad breaks in together. A member
	/// already shooting keeps shooting and is not planned for.
	/// </summary>
	private void PlanAssault(Squad sq, bool sync)
	{
		var nav = Level.Nav;
		var anchor = Guards[sq.Anchor];
		CombatTarget(anchor, out int tx, out int ty);
		sq.PlannedX = tx; sq.PlannedY = ty;
		sq.ReplanMt = Tune.FlankReplanTicks * Actor.Mt;

		int goal = nav.NearestPassable(tx, ty);
		int n = nav.W * nav.H;
		if (_penalty == null || _penalty.Length != n) _penalty = new int[n];
		else System.Array.Clear(_penalty);
		int gc = goal >= 0 ? goal % nav.W : 0, gr = goal >= 0 ? goal / nav.W : 0;

		_etas.Clear();
		int maxEta = 0;
		for (int m = 0; m < sq.Members.Count; m++)
		{
			int i = sq.Members[m];
			var g = Guards[i];
			if (g.Prone || g.SquadId != sq.Id || g.State != GuardState.Combat) continue;
			if (g.Task == GuardTask.Engage || g.Task == GuardTask.Radio) continue;

			g.RouteX.Clear(); g.RouteY.Clear(); g.RouteIndex = 0; g.WaitMt = 0;
			int start = nav.NearestPassable(g.X, g.Y);
			if (goal < 0 || start < 0 || Paths.Search(start, goal, _flankCells, _penalty) < 0)
			{
				SetTask(g, GuardTask.Converge);     // nothing to plan: go straight at it
				continue;
			}

			// The walked length, not the penalised cost, decides his ETA.
			long units = 0;
			for (int k = 1; k < _flankCells.Count; k++)
			{
				int a = _flankCells[k - 1], b = _flankCells[k];
				units += (a % nav.W != b % nav.W && a / nav.W != b / nav.W)
					? NavGrid.CostDiagonal : NavGrid.CostStraight;
			}
			long lenFx = units * Level.CellFx / NavGrid.CostStraight;
			int eta = (int)(lenFx * Fx.TicksPerSecond / Tune.SpeedHunt);
			_etas.Add((i, eta));
			if (eta > maxEta) maxEta = eta;

			// Smooth AFTER measuring, so the route and the flank it was planned
			// round are the same way in.
			Paths.Smooth(_flankCells, g.X, g.Y, tx, ty, g.RouteX, g.RouteY);
			SetTask(g, GuardTask.Assault);

			// Mark this route for the ones still to plan.
			for (int k = 0; k < _flankCells.Count; k++)
			{
				int c0 = _flankCells[k] % nav.W, r0 = _flankCells[k] / nav.W;
				for (int dr = -Tune.FlankPenaltyCells; dr <= Tune.FlankPenaltyCells; dr++)
					for (int dc = -Tune.FlankPenaltyCells; dc <= Tune.FlankPenaltyCells; dc++)
					{
						int c = c0 + dc, r = r0 + dr;
						if (!nav.InBounds(c, r)) continue;
						if (Fx.Max(Fx.Abs(c - gc), Fx.Abs(r - gr)) <= Tune.FlankGoalFreeCells) continue;
						_penalty[r * nav.W + c] = Tune.FlankPenalty;
					}
			}
		}

		if (!sync) return;
		for (int k = 0; k < _etas.Count; k++)
		{
			int wait = maxEta - _etas[k].Eta;
			if (wait > Tune.EtaSyncMaxTicks) wait = Tune.EtaSyncMaxTicks;
			Guards[_etas[k].Index].WaitMt = wait * Actor.Mt;
		}
	}

	/// <summary>
	/// Hold off while the others get into position, then walk his own route in.
	/// Reaching the LKP without the player in view begins the search, as a
	/// converging guard's does.
	/// </summary>
	private void Behave_Assault(Actor e, int w)
	{
		CombatTarget(e, out int tx, out int ty);
		if (e.WaitMt > 0)
		{
			e.WaitMt -= w;
			if (e.WaitMt < 0) e.WaitMt = 0;
			// Ready, facing the way he will go.
			int fx = e.RouteX.Count > 0 ? e.RouteX[0] : tx, fy = e.RouteY.Count > 0 ? e.RouteY[0] : ty;
			if (fx != e.X || fy != e.Y) LookAt(e, Brad.Atan2(fy - e.Y, fx - e.X), Tune.TurnHunt, w);
			return;
		}
		if (Fx.Dist(tx, ty, e.X, e.Y) < Tune.LkpReach) { BeginSearch(e, tx, ty); return; }

		int last = e.RouteX.Count - 1;
		while (e.RouteIndex < last
			&& Fx.Dist(e.RouteX[e.RouteIndex], e.RouteY[e.RouteIndex], e.X, e.Y) < Tune.WaypointReach)
			e.RouteIndex++;
		// The final leg goes for the live LKP, which may have moved since the plan.
		if (e.RouteIndex >= last) Steer(e, tx, ty, Tune.SpeedHunt, w);
		else Steer(e, e.RouteX[e.RouteIndex], e.RouteY[e.RouteIndex], Tune.SpeedHunt, w);
	}

	/// <summary>
	/// Sweep the LKP's surroundings for SearchTicks. The points are still
	/// random (spec §10.3), but only points the guard could actually reach:
	/// a point across a wall is refused, rather than walked into.
	/// </summary>
	private void Behave_SearchLkp(Actor e, int w)
	{
		e.SearchMt -= w;
		if (e.SearchMt <= 0) { ContactLost(e); return; }

		if (!e.HasSearchPt || Fx.Dist(e.SearchX, e.SearchY, e.X, e.Y) < Tune.SearchPtReach)
		{
			int bx = e.HasLkp ? e.LkpX : e.HomeX;
			int by = e.HasLkp ? e.LkpY : e.HomeY;
			var nav = Level.Nav;
			int mine = nav.NearestPassable(e.X, e.Y);
			int region = mine >= 0 ? nav.Region[mine] : -1;
			e.HasSearchPt = false;
			// Twelve attempts, as the prototype, so the RNG draw count per
			// search point is fixed whatever the geometry.
			for (int i = 0; i < 12; i++)
			{
				int a = Rng.NextBrad();
				int d = Rng.NextRange(Tune.SearchRadiusMin, Tune.SearchRadiusMax);
				if (e.HasSearchPt) continue;
				Brad.SinCos(a, out int sin, out int cos);
				int nx = bx + (int)(((long)d * cos) >> Brad.UnitShift);
				int ny = by + (int)(((long)d * sin) >> Brad.UnitShift);
				// SOLID for both: a sweep point on the far side of a window
				// is a point the guard can see and cannot reach.
				if (Geometry.HitsWall(Solid, nx, ny, 14 * Fx.One)) continue;
				if (!Geometry.ClearLine(Solid, bx, by, nx, ny)) continue;
				int cell = nav.NearestPassable(nx, ny);
				if (cell < 0 || nav.Region[cell] != region) continue;
				e.SearchX = nx; e.SearchY = ny; e.HasSearchPt = true;
			}
			if (!e.HasSearchPt) { e.SearchX = bx; e.SearchY = by; e.HasSearchPt = true; }
		}

		Steer(e, e.SearchX, e.SearchY, Tune.SpeedSearch, w);
	}

	/// <summary>Stand still, face the trouble, and key the radio.</summary>
	private void Behave_Radio(Actor e, int w)
	{
		CombatTarget(e, out int tx, out int ty);
		if (e.Radio == RadioPurpose.Report) { tx = e.LkpX; ty = e.LkpY; }
		LookAt(e, Brad.Atan2(ty - e.Y, tx - e.X), Tune.TurnCurious, w);
		e.RadioMt += w;
		if (e.RadioMt >= Tune.RadioTicks * Actor.Mt) CompleteRadio(e);
	}

	/// <summary>Get to the hold point and watch the approach until the squad
	/// goes. With nobody coming, hold until contact is lost.</summary>
	private void Behave_Hold(Actor e, int w)
	{
		var sq = Net.SquadById(e.SquadId);
		if (sq == null || sq.Go) { SetTask(e, GuardTask.Converge); return; }

		if (Fx.Dist(e.TaskX, e.TaskY, e.X, e.Y) >= Tune.WaypointReach)
			Steer(e, e.TaskX, e.TaskY, Tune.SpeedHunt, w);
		else
			LookAt(e, e.TaskFacing, Tune.TurnHunt, w);

		if (sq.Expected == 0 && Net.IntelAgeMt >= Tune.ContactLostTicks * Actor.Mt) ContactLost(e);
	}

	/// <summary>A responder heads for the caller's hold point.</summary>
	private void Behave_Rally(Actor e, int w)
	{
		var sq = Net.SquadById(e.SquadId);
		if (sq == null || sq.Go) { SetTask(e, GuardTask.Converge); return; }

		if (Fx.Dist(sq.RallyX, sq.RallyY, e.X, e.Y) >= Tune.RallyRadius / 2)
			Steer(e, sq.RallyX, sq.RallyY, Tune.SpeedHunt, w);
		else
		{
			CombatTarget(e, out int tx, out int ty);
			LookAt(e, Brad.Atan2(ty - e.Y, tx - e.X), Tune.TurnHunt, w);
		}
	}

	// =====================================================================
	// Hunting (Guard_AI.md §6). SOLO until P4 builds pairs and the sweep map:
	// a patroller walks his route at search pace with alerted eyes, a
	// sentry holds his post and sweeps his sector.
	// =====================================================================

	/// <summary>Sweep and WatchExit: his place in a sweep group decides what he does.</summary>
	private void Behave_Sweep(Actor e, int w)
	{
		var grp = Net.GroupById(e.GroupId);
		if (grp == null)
		{
			// Not in a group (there was nobody to pair with and nowhere to
			// sweep): walk the old route at search pace, or hold.
			if (e.PathX == null || e.PathX.Length == 0) { SetTask(e, GuardTask.HoldPost); return; }
			int nx = e.PathX[e.WaypointIndex];
			int ny = e.PathY![e.WaypointIndex];
			if (Fx.Dist(nx, ny, e.X, e.Y) < Tune.WaypointReach)
				e.WaypointIndex = (e.WaypointIndex + 1) % e.PathX.Length;
			Steer(e, nx, ny, Tune.SpeedSearch, w);
			return;
		}
		int role = grp.Members.IndexOf(Guards.IndexOf(e));
		if (role <= 0) SweepLead(e, grp, w);
		else SweepFollow(e, grp, role, w);
	}

	/// <summary>A watcher's slow sweep across his sector, phase-offset per guard.</summary>
	private int WatchSway(Actor e)
	{
		int phase = (int)((Tick * Tune.SentrySwayRate + e.Id * 9001) & Brad.Mask);
		return (int)(((long)Brad.Sin(phase) * Tune.WatchSwayArc) >> Brad.UnitShift);
	}

	/// <summary>Anyone in the group off looking at something, or fallen behind.</summary>
	private bool GroupWaiting(SweepGroup grp, Actor lead)
	{
		for (int m = 1; m < grp.Members.Count; m++)
		{
			var g = Guards[grp.Members[m]];
			if (g.Task == GuardTask.Look || g.Task == GuardTask.Investigate || g.Task == GuardTask.LookAround) return true;
			if (Fx.Dist(g.X, g.Y, lead.X, lead.Y) > Tune.GroupWaitDist) return true;
		}
		return false;
	}

	/// <summary>
	/// The leader walks to the node at SpeedSweep facing the way he goes, and
	/// at the node looks round. Alone, he also turns to check behind him, since
	/// nobody else is.
	/// </summary>
	private void SweepLead(Actor e, SweepGroup grp, int w)
	{
		int sway = WatchSway(e);
		if (grp.Dwelling || GroupWaiting(grp, e) || !SweepTarget(grp, out int tx, out int ty))
		{
			int look = grp.Heading + sway;
			if (grp.Dwelling)
			{
				int dwell = Tune.NodeDwellTicks * Actor.Mt;
				int phase = (int)((long)grp.PhaseMt * Brad.Full / dwell);
				look = grp.Heading + (int)(((long)Brad.Sin(phase) * Tune.LookAroundArc) >> Brad.UnitShift);
				if (grp.Members.Count == 1 && grp.PhaseMt > dwell / 2) look += Brad.Half;
			}
			LookAt(e, look & Brad.Mask, Tune.TurnCurious, w);
			return;
		}

		int ox = e.X, oy = e.Y;
		int heading = MoveTo(e, tx, ty, Tune.SpeedSweep, w);
		if (e.X != ox || e.Y != oy) grp.Heading = heading;
		LookAt(e, (grp.Heading + sway) & Brad.Mask, Tune.TurnSteer, w);

		int n = grp.TrailX.Count;
		if (n == 0 || Fx.Dist(grp.TrailX[n - 1], grp.TrailY[n - 1], e.X, e.Y) >= Tune.TrailStep)
		{
			grp.TrailX.Add(e.X); grp.TrailY.Add(e.Y);
			if (grp.TrailX.Count > Tune.TrailMax) { grp.TrailX.RemoveAt(0); grp.TrailY.RemoveAt(0); }
		}
	}

	/// <summary>
	/// Walk the leader's trail <paramref name="role"/> x PairSpacing behind him,
	/// watching the group's back ([1]) or a flank that swaps sides ([2]). Too
	/// far behind to be any use as a watcher, he faces where he is going and
	/// catches up first.
	/// </summary>
	private void SweepFollow(Actor e, SweepGroup grp, int role, int w)
	{
		var lead = Guards[grp.Members[0]];
		TrailPoint(grp, lead, role * Tune.PairSpacing, out int tx, out int ty);
		if (Fx.Dist(tx, ty, e.X, e.Y) > FollowSlack)
		{
			int h = MoveTo(e, tx, ty, Tune.SpeedSearch, w);
			if (Fx.Dist(e.X, e.Y, lead.X, lead.Y) > Tune.GroupWaitDist)
			{
				LookAt(e, h, Tune.TurnSteer, w);
				return;
			}
		}
		int face = role == 1 ? grp.Heading + Brad.Half
			: grp.Heading + (((Tick / Tune.FlankSwapTicks) & 1) == 0 ? Brad.Quarter : -Brad.Quarter);
		LookAt(e, (face + WatchSway(e)) & Brad.Mask, Tune.TurnSteer, w);
	}

	private const int FollowSlack = 4 * Fx.One;

	/// <summary>The point <paramref name="back"/> along the leader's trail behind him.</summary>
	private static void TrailPoint(SweepGroup grp, Actor lead, int back, out int x, out int y)
	{
		int px = lead.X, py = lead.Y;
		long acc = 0;
		for (int k = grp.TrailX.Count - 1; k >= 0; k--)
		{
			int qx = grp.TrailX[k], qy = grp.TrailY[k];
			int seg = Fx.Dist(px, py, qx, qy);
			if (seg > 0 && acc + seg >= back)
			{
				long need = back - acc;
				x = px + (int)((qx - px) * need / seg);
				y = py + (int)((qy - py) * need / seg);
				return;
			}
			acc += seg;
			px = qx; py = qy;
		}
		x = px; y = py;
	}

	private void Behave_HoldPost(Actor e, int w)
	{
		if (Fx.Dist(e.HomeX, e.HomeY, e.X, e.Y) >= Tune.WaypointReach)
		{
			Steer(e, e.HomeX, e.HomeY, Tune.SpeedSearch, w);
			return;
		}
		int phase = (int)((Tick * Tune.SentrySwayRate + e.HomeX / Fx.One * 180) & Brad.Mask);
		int sweep = (int)(((long)Brad.Sin(phase) * Tune.HoldSwayAmp) >> Brad.UnitShift);
		LookAt(e, (e.PostFacing + sweep) & Brad.Mask, Tune.TurnCurious, w);
	}

	// =====================================================================
	// Relaxed (spec §8.1's Patrol and Sentry, unchanged)
	// =====================================================================

	private void Behave_Patrol(Actor e, int w)
	{
		if (e.PathX == null || e.PathX.Length == 0) { SetTask(e, GuardTask.Post); return; }
		int nx = e.PathX[e.WaypointIndex];
		int ny = e.PathY![e.WaypointIndex];
		if (Fx.Dist(nx, ny, e.X, e.Y) < Tune.WaypointReach)
			e.WaypointIndex = (e.WaypointIndex + 1) % e.PathX.Length;
		Steer(e, nx, ny, Alarm.PatrolSpeed, w);
	}

	// =====================================================================
	// Engage (spec §8.5, unchanged) and the sentry's sway
	// =====================================================================

	/// <summary>
	/// A guard's cone: his WEAPON's three terms -- base, sustained fire, and
	/// how hard it is being swung -- plus <see cref="Tune.GuardSpreadPenalty"/>,
	/// the marksmanship a guard does not have. The penalty is set so a cold,
	/// still guard with an AK draws exactly the flat cone every guard used to.
	/// </summary>
	private static int GuardSpreadWidth(Actor e, in WeaponSpec spec)
		=> spec.SpreadBase + Tune.GuardSpreadPenalty
		 + (int)(((long)e.Heat * spec.SpreadPerHeat) >> Fx.Shift)
		 + (int)(((long)e.SwayQ8 * spec.SpreadPerSway) >> Fx.Shift);

	/// <summary>Rounds in one burst: as many as the weapon cycles in
	/// <see cref="Tune.GuardBurstTicks"/>, and never fewer than one. An AK
	/// fires three, an MP7 six, a Vulcan twelve; a pump gun or a bolt gun one.</summary>
	public static int GuardBurst(in WeaponSpec spec)
		=> spec.FireCooldownTicks <= 0 || Tune.GuardBurstTicks < spec.FireCooldownTicks ? 1
			: Tune.GuardBurstTicks / spec.FireCooldownTicks;

	/// <summary>
	/// Returns whether the guard held his trigger this tick -- on target, in
	/// sight and not reloading -- which is what keeps a rotary weapon spinning
	/// and a burst going (the tail of StepGuard reads it).
	/// </summary>
	private bool Behave_Engage(Actor e, int w, bool hasLos)
	{
		var p = Player;
		var spec = WeaponCatalog.Get(e.Weapon);
		int toP = Brad.Atan2(p.Y - e.Y, p.X - e.X);
		e.Facing = Brad.TurnToward(e.Facing, toP, Tune.TurnEngage * w / Fx.ScaleDen, Tune.TurnDen);

		e.AimMt -= w;
		e.CooldownMt -= w;

		// A man with grenades keeps back past his own blast; everyone else
		// holds the band the spec asks for.
		int near = spec.Grenade ? Tune.GuardGrenadeMinDist : Tune.EngageNearDist;
		int far = near + (Tune.EngageFarDist - Tune.EngageNearDist);
		int dist = Fx.Dist(p.X, p.Y, e.X, e.Y);
		int strafe = dist > far ? 1 : (dist < near ? -1 : 0);
		if (strafe != 0)
		{
			Brad.SinCos(e.Facing, out int sin, out int cos);
			int step = Fx.PerTick(Tune.EngageStrafeSpeed, w) * strafe;
			Geometry.MoveSlide(Solid, ref e.X, ref e.Y,
				(int)(((long)step * cos) >> Brad.UnitShift),
				(int)(((long)step * sin) >> Brad.UnitShift), e.Radius);
		}

		bool held = e.AimMt <= 0 && hasLos && e.ReloadMt <= 0
			&& !(spec.Grenade && dist < Tune.GuardGrenadeMinDist);
		if (!held) return false;

		// Spooling costs nothing and fires nothing, exactly as for the player.
		if (spec.SpinUpTicks > 0)
		{
			int cap = spec.SpinUpTicks * Actor.Mt;
			e.SpinMt += w;
			if (e.SpinMt > cap) e.SpinMt = cap;
			if (e.SpinMt < cap) return true;
		}
		if (e.CooldownMt > 0) return true;

		if (e.Mag <= 0)
		{
			e.ReloadMt = spec.ReloadTicks * Actor.Mt;
			e.BurstShots = 0;
			return false;
		}
		GuardFire(e, in spec);
		return true;
	}

	/// <summary>
	/// One round (or shell, or bolt, or grenade) from a guard's own weapon,
	/// through the same projectile machinery as the player's. A shell is
	/// CHOKED like the player's; a bolt carries its arc and a penetrator its
	/// walls; a grenade is thrown from his centre and blasts on its fuse, and
	/// its fragments are no kinder to his friends than yours are. What he does
	/// not get is a headshot or an aim lock: those are the player's.
	/// </summary>
	private void GuardFire(Actor e, in WeaponSpec spec)
	{
		e.Mag--;
		// Sustained fire costs a guard his accuracy before the shot leaves,
		// exactly as it does the player's (ruling #4's ordering).
		e.Heat += spec.HeatPerShot;
		if (e.Heat > Tune.HeatMax) e.Heat = Tune.HeatMax;

		int width = GuardSpreadWidth(e, in spec);
		Brad.SinCos(e.Facing, out int sin, out int cos);
		int mx = e.X + (int)(((long)spec.MuzzleOffset * cos) >> Brad.UnitShift);
		int my = e.Y + (int)(((long)spec.MuzzleOffset * sin) >> Brad.UnitShift);

		if (spec.Grenade)
		{
			int heading = (e.Facing + Rng.NextSigned(width / 2)) & Brad.Mask;
			var g = Bullets.Spawn(e.X, e.Y, heading, spec.BulletSpeed, spec.BulletTicks, false,
				spec.Damage, spec.ArmourPierce);
			g.Kind = BulletKind.Grenade;
			Log.Add(SimEventKind.GrenadeThrown, e.X, e.Y, heading);
		}
		else
		{
			int pellets = spec.Pellets < 1 ? 1 : spec.Pellets;
			int slice = pellets > 1 ? width / pellets : 0;
			int first = -(slice * (pellets - 1)) / 2;
			int jitter = pellets > 1 ? (slice * Tune.ChokeJitterQ8) >> Fx.Shift : width / 2;
			for (int i = 0; i < pellets; i++)
			{
				int heading = (e.Facing + first + slice * i + Rng.NextSigned(jitter)) & Brad.Mask;
				var b = Bullets.Spawn(mx, my, heading, spec.BulletSpeed, spec.BulletTicks, false,
					spec.Damage, spec.ArmourPierce);
				if (spec.ArcTargets > 0) { b.Kind = BulletKind.Arc; b.Arc = spec.ArcTargets; }
				else if (spec.WallPierce > 0) { b.Kind = BulletKind.Pierce; b.WallsLeft = spec.WallPierce; }
			}
		}
		// Value is the weapon, so presentation can tell a pistol from a minigun.
		Log.Add(SimEventKind.GuardShot, mx, my, e.Facing, (int)e.Weapon);
		e.RecoilQ8 = Fx.One;

		e.BurstShots++;
		if (e.BurstShots >= GuardBurst(in spec))
		{
			// The burst is over: a pause, then back on target.
			e.BurstShots = 0;
			int pause = spec.FireCooldownTicks > Tune.EngageCooldownTicks
				? spec.FireCooldownTicks : Tune.EngageCooldownTicks;
			e.CooldownMt = pause * Actor.Mt;
			e.AimMt = Tune.ReaimTicks * Actor.Mt;
		}
		else
		{
			e.CooldownMt = spec.FireCooldownTicks * Actor.Mt;
		}
	}

	/// <summary>
	/// A guard's weapon between shots, every tick after his behaviour: heat
	/// bleeds at the weapon's own rate, a reload runs out and refills the
	/// magazine, a rotary weapon winds down unless the trigger was held, a
	/// burst ends when the trigger is let go, and a guard out of the fight
	/// tops up a part-spent magazine.
	/// </summary>
	private static void StepGuardWeapon(Actor e, int w, bool held)
	{
		var spec = WeaponCatalog.Get(e.Weapon);
		if (e.Heat > 0)
		{
			e.Heat -= Fx.PerTick(spec.HeatDecayPerSec, w);
			if (e.Heat < 0) e.Heat = 0;
		}
		if (e.ReloadMt > 0)
		{
			e.ReloadMt -= w;
			if (e.ReloadMt <= 0) { e.ReloadMt = 0; e.Mag = spec.Magazine; }
		}
		else if (e.State != GuardState.Combat && e.Mag < spec.Magazine)
		{
			e.ReloadMt = spec.ReloadTicks * Actor.Mt;
		}
		if (!held)
		{
			e.BurstShots = 0;
			if (e.SpinMt > 0)
			{
				e.SpinMt -= (int)(((long)w * Tune.SpinDownQ8) >> Fx.Shift);
				if (e.SpinMt < 0) e.SpinMt = 0;
			}
		}
	}

	private void Behave_Sentry(Actor e, int w)
	{
		// A slow sway, phase-offset by position so sentries do not sweep in
		// lockstep. Driven by the tick counter, not a wall clock.
		int phase = (int)((Tick * Tune.SentrySwayRate + e.HomeX / Fx.One * 180) & Brad.Mask);
		int sway = (int)(((long)Brad.Sin(phase) * Tune.SentrySwayAmp) >> Brad.UnitShift);
		e.Facing = (e.Facing + sway * w / Fx.ScaleDen / Fx.TicksPerSecond) & Brad.Mask;
	}

	// =====================================================================
	// Movement (Guard_AI.md §8). Facing and travel are separate: MoveTo walks
	// a path and leaves facing alone, LookAt turns and moves nothing, and
	// Steer is the two together for a guard who looks where he walks.
	// =====================================================================

	/// <summary>
	/// Walk toward (tx, ty) along a planned path, without touching facing.
	/// Returns the BRAD heading travelled, so a caller can face it or not.
	/// </summary>
	internal int MoveTo(Actor e, int tx, int ty, int speed, int w)
	{
		NavWaypoint(e, tx, ty, w, out int wx, out int wy);
		int heading = e.X == wx && e.Y == wy ? e.Facing : Brad.Atan2(wy - e.Y, wx - e.X);
		Advance(e, wx, wy, heading, speed, w);
		return heading;
	}

	/// <summary>Turn facing toward a BRAD angle at kNum/TurnDen of the gap per
	/// tick, scaled by the world clock. Moves nothing.</summary>
	internal static void LookAt(Actor e, int angle, int kNum, int w)
		=> e.Facing = Brad.TurnToward(e.Facing, angle, kNum * w / Fx.ScaleDen, Tune.TurnDen);

	/// <summary>
	/// Walk toward (tx, ty) looking where you are going: what every guard did
	/// before pathfinding. The turn comes FIRST, as it always did, so a guard
	/// reversing on his route pays the backpedal only for the ticks he is
	/// genuinely still facing the wrong way.
	/// </summary>
	private void Steer(Actor e, int tx, int ty, int speed, int w)
	{
		NavWaypoint(e, tx, ty, w, out int wx, out int wy);
		int heading = e.X == wx && e.Y == wy ? e.Facing : Brad.Atan2(wy - e.Y, wx - e.X);
		LookAt(e, heading, Tune.TurnSteer, w);
		Advance(e, wx, wy, heading, speed, w);
	}

	/// <summary>
	/// Path bookkeeping for one tick: ask for a new path if there is none or
	/// the goal has moved cell (once the cooldown allows), skip waypoints
	/// already reached, and name the point to head for. The final waypoint is
	/// always the LIVE goal, so a target that drifts within its cell, or
	/// across cells while the cooldown runs, is still followed.
	/// </summary>
	private void NavWaypoint(Actor e, int tx, int ty, int w, out int wx, out int wy)
	{
		if (e.RepathMt > 0) e.RepathMt -= w;
		e.NavGoalX = tx; e.NavGoalY = ty;

		int goalCell = Level.Nav.CellAt(tx, ty);
		bool hasPath = e.NavX.Count > 0;
		if (!hasPath || (goalCell != e.NavGoalCell && e.RepathMt <= 0)) e.NavPending = true;

		if (!hasPath) { wx = tx; wy = ty; return; }     // nothing planned yet: straight at it

		int last = e.NavX.Count - 1;
		while (e.NavIndex < last
			&& Fx.DistSq(e.X, e.Y, e.NavX[e.NavIndex], e.NavY[e.NavIndex]) < (long)Tune.NavReach * Tune.NavReach)
			e.NavIndex++;

		if (e.NavIndex >= last) { wx = tx; wy = ty; }
		else { wx = e.NavX[e.NavIndex]; wy = e.NavY[e.NavIndex]; }
	}

	/// <summary>
	/// The physical step: toward (wx, wy) on <paramref name="heading"/>, never
	/// past it, slower when travelling more than 90 degrees off facing, sliding
	/// along walls. A guard making no progress for UnstickTicks drops his path
	/// and asks for one planned round the obstacle rather than through it.
	/// </summary>
	private void Advance(Actor e, int wx, int wy, int heading, int speed, int w)
	{
		int dist = Fx.Dist(e.X, e.Y, wx, wy);
		int step = Fx.PerTick(speed, w);
		int off = Brad.Norm(heading - e.Facing);
		if (off < 0) off = -off;
		if (off > Brad.Quarter) step = (int)(((long)step * Tune.BackpedalQ8) >> Fx.Shift);
		if (step > dist) step = dist;
		if (step <= 0) { e.StuckMt = 0; return; }

		Brad.SinCos(heading, out int sin, out int cos);
		OpenDoorAhead(e, sin, cos, step);
		int ox = e.X, oy = e.Y;
		Geometry.MoveSlide(Solid, ref e.X, ref e.Y,
			(int)(((long)step * cos) >> Brad.UnitShift),
			(int)(((long)step * sin) >> Brad.UnitShift), e.Radius);

		int moved = Fx.Hypot(e.X - ox, e.Y - oy);
		if (moved < step * 35 / 100)
		{
			e.StuckMt += w;
			if (e.StuckMt > Tune.UnstickTicks * Actor.Mt)
			{
				e.NavX.Clear(); e.NavY.Clear(); e.NavIndex = 0;
				e.NavNoShortcut = true;
				e.NavPending = true;
				e.RepathMt = 0;
				e.StuckMt = 0;
			}
		}
		else if (e.StuckMt > 0) e.StuckMt = 0;
	}

	private PathFinder? _paths;

	/// <summary>Guard index path service resumes from next tick. Hashed: it
	/// decides who is planned for first, which is state the next tick reads.</summary>
	private int _navCursor;

	/// <summary>A* searches run by the last guard pass. Diagnostic, not hashed.</summary>
	public int NavSearchesLastTick { get; private set; }

	/// <summary>
	/// Serve this tick's path requests, round-robin from <see cref="_navCursor"/>,
	/// at most <see cref="Tune.NavSearchesPerTick"/> A* searches. Runs AFTER
	/// every guard has moved, so a plan starts from where the guard ended the
	/// tick and every guard sees the same world. A straight-line plan costs no
	/// budget. A guard left unserved keeps his old path and asks again next
	/// tick, and the cursor moves past whoever was last served, so nobody
	/// waits behind the same four guards twice.
	/// </summary>
	private void ServePaths()
	{
		int n = Guards.Count;
		NavSearchesLastTick = 0;
		if (n == 0) return;

		_paths ??= new PathFinder(Level.Nav);
		int budget = Tune.NavSearchesPerTick;
		int start = _navCursor % n;

		for (int k = 0; k < n; k++)
		{
			int i = (start + k) % n;
			var e = Guards[i];
			if (!e.NavPending) continue;

			bool shortcut = !e.NavNoShortcut;
			bool clear = shortcut && Level.Nav.SegmentClear(e.X, e.Y, e.NavGoalX, e.NavGoalY);
			if (!clear && budget <= 0) { e.NavPending = false; continue; }

			bool ok, searched = false;
			if (clear)
			{
				e.NavX.Clear(); e.NavY.Clear();
				e.NavX.Add(e.NavGoalX); e.NavY.Add(e.NavGoalY);
				ok = true;
			}
			else ok = _paths.Plan(e.X, e.Y, e.NavGoalX, e.NavGoalY, false, e.NavX, e.NavY, out searched);
			if (searched)
			{
				budget--;
				NavSearchesLastTick++;
				_navCursor = (i + 1) % n;
			}
			if (!ok)
			{
				// Unreachable: head straight for it, as guards always did.
				e.NavX.Clear(); e.NavY.Clear();
				e.NavX.Add(e.NavGoalX); e.NavY.Add(e.NavGoalY);
			}
			e.NavIndex = 0;
			e.NavGoalCell = Level.Nav.CellAt(e.NavGoalX, e.NavGoalY);
			e.RepathMt = Tune.RepathCooldownTicks * Actor.Mt;
			e.NavNoShortcut = false;
			e.NavPending = false;
		}
	}
}
