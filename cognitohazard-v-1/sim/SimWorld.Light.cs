namespace Cognitohazard.Sim;

/// <summary>
/// Light as a stealth axis (cognitohazard_lighting_plan.md). The map itself is
/// <see cref="LightMap"/>; this is everything that reads it or changes it:
/// the light at the player, torches, the muzzle flash, lamps shot out, and
/// switches thrown by the player or by a guard putting things right.
///
/// Every entry point is a no-op while <see cref="Light"/> is null, which it is
/// on any level without an <c>ambient:</c> line below 100. That is how a lit
/// level runs, and hashes, exactly as it did before any of this existed.
/// </summary>
public sealed partial class SimWorld
{
	/// <summary>
	/// Once a tick, before the guards look: the light the player is standing
	/// in, and the flash running down.
	/// </summary>
	private void StepLight(int w)
	{
		if (Light == null) return;
		PlayerLightQ8 = PlayerLight();
		if (PlayerFlashMt > 0)
		{
			PlayerFlashMt -= w;
			if (PlayerFlashMt < 0) PlayerFlashMt = 0;
		}
	}

	private int PlayerLight()
	{
		var p = Player;
		int l = Light!.LightAt(p.X, p.Y);
		if (PlayerFlashMt > 0 && l < Tune.FlashLightQ8) l = Tune.FlashLightQ8;
		if (l < Tune.TorchLightQ8)
			for (int i = 0; i < Guards.Count; i++)
				if (InBeam(Guards[i], p.X, p.Y)) { l = Tune.TorchLightQ8; break; }
		return l;
	}

	/// <summary>Light at a fixed-point point, Q8; 256 on a lit level.</summary>
	public int LightAt(int x, int y) => Light == null ? Fx.One : Light.LightAt(x, y);

	/// <summary>
	/// A guard carries a lit torch (plan §6): on a level with lighting, when he
	/// is fighting or hunting, and ALL the time on a level at or below
	/// <see cref="Tune.GuardTorchAmbient"/>. Derived from hashed state, so it
	/// needs no hashing of its own.
	/// </summary>
	public bool TorchOn(Actor e)
	{
		if (Light == null || e.Prone) return false;
		return e.State == GuardState.Combat || e.State == GuardState.Hunting
			|| Level.Ambient <= Tune.GuardTorchAmbient;
	}

	/// <summary>A point is in this guard's torch beam: on, in reach, inside
	/// the half-angle round his facing, and in plain view of the lens.</summary>
	public bool InBeam(Actor e, int x, int y)
	{
		if (!TorchOn(e)) return false;
		if (Fx.DistSq(e.X, e.Y, x, y) > (long)Tune.TorchReach * Tune.TorchReach) return false;
		int off = Brad.Norm(Brad.Atan2(y - e.Y, x - e.X) - e.Facing);
		if (off < 0) off = -off;
		if (off > Tune.TorchHalf) return false;
		return Geometry.ClearLine(Opaque, e.X, e.Y, x, y);
	}

	/// <summary>Light on a body as THIS guard sees it: the map, or his own beam.</summary>
	private int BodyLight(Actor looker, Actor body)
	{
		int l = Light!.LightAt(body.X, body.Y);
		if (l < Tune.TorchLightQ8 && InBeam(looker, body.X, body.Y)) l = Tune.TorchLightQ8;
		return l;
	}

	/// <summary>
	/// Light on a guard as the PLAYER sees him. His own torch gives him away,
	/// and so does his rifle going off (recoil stands in for the flash).
	/// </summary>
	public int GuardLightQ8(Actor e)
	{
		if (Light == null) return Fx.One;
		if (TorchOn(e) || e.RecoilQ8 > 0) return Fx.One;
		return Light.LightAt(e.X, e.Y);
	}

	/// <summary>
	/// How well the player can make this guard out, Q8, by the SAME rule a
	/// guard uses on the player (plan §3.3): 0 is not at all. No line, nothing;
	/// close, always; otherwise sight reaches down toward DarkSightRange as the
	/// light at him falls, and within it he is seen at VisQ8 of that light.
	/// <paramref name="sightRange"/> is the player's own reach (the vision
	/// polygon's radius).
	/// </summary>
	public int PlayerSeesQ8(Actor e, int sightRange)
	{
		var p = Player;
		if (!Geometry.ClearLine(Opaque, p.X, p.Y, e.X, e.Y)) return 0;
		if (Light == null) return Fx.One;
		int d = Fx.Dist(p.X, p.Y, e.X, e.Y);
		if (d <= Tune.DarkSeeRange) return Fx.One;
		int l = GuardLightQ8(e);
		if (d > Perception.DarkReach(sightRange, l, Tune.DarkSightRange)) return 0;
		return Perception.VisQ8(l);
	}

	/// <summary>
	/// The player fired (plan §4). In the dark that lights them for a few ticks,
	/// and every guard facing it with a line to the muzzle SEES the flash from
	/// further than he could see a man: he comes to look. The reach scales with
	/// the report, so a suppressor hides the flash as it hides the bang.
	/// </summary>
	private void MuzzleFlash(int x, int y, int report)
	{
		if (Light == null) return;
		PlayerFlashMt = Tune.FlashTicks * Actor.Mt;
		long reach = (long)report * 2;
		if (reach > Tune.FlashSeenRange) reach = Tune.FlashSeenRange;
		for (int i = 0; i < Guards.Count; i++)
		{
			var e = Guards[i];
			if (e.Prone || e.State == GuardState.Combat) continue;
			if (Fx.DistSq(e.X, e.Y, x, y) > reach * reach) continue;
			int off = Brad.Norm(Brad.Atan2(y - e.Y, x - e.X) - e.Facing);
			if (off < 0) off = -off;
			if (off > Brad.Full / 4) continue;
			if (!Geometry.ClearLine(Opaque, e.X, e.Y, x, y)) continue;
			Notice(e, x, y, Tune.FlashAwareness);
		}
	}

	/// <summary>
	/// A round shattered a lamp. It goes dark for good and it is heard: a
	/// smaller pop than a window, a lure all the same.
	/// </summary>
	private void BreakLamp(int lamp, int heading)
	{
		if (lamp < 0 || lamp >= Lamps.Count) return;
		var l = Lamps[lamp];
		if (l.Broken) return;
		l.Broken = true;
		Light?.SetLamp(lamp, false);
		Log.Add(SimEventKind.LampBroken, l.X, l.Y, heading, lamp);
		for (int i = 0; i < Guards.Count; i++)
		{
			var e = Guards[i];
			if (e.Prone) continue;
			if (Fx.Dist(e.X, e.Y, l.X, l.Y) >= Tune.LampNoiseRadius) continue;
			Notice(e, l.X, l.Y, Tune.LampAwareness);
		}
	}

	/// <summary>
	/// The player throws a switch: refused silently out of reach, like a door.
	/// Darkens the room if anything in it is lit, lights it otherwise.
	/// </summary>
	private void UseSwitch(int s)
	{
		if (s < 0 || s >= Switches.Count) return;
		var sw = Switches[s];
		if (Fx.Dist(Player.X, Player.Y, sw.X, sw.Y) > Tune.SwitchReach) return;
		SetRoom(s, !RoomLit(s), false);
	}

	/// <summary>Any lamp on this switch's circuit is lit.</summary>
	public bool RoomLit(int s)
	{
		var sw = Switches[s];
		for (int i = 0; i < sw.Lamps.Length; i++) if (Lamps[sw.Lamps[i]].Lit) return true;
		return false;
	}

	private void SetRoom(int s, bool on, bool byGuard)
	{
		var sw = Switches[s];
		for (int i = 0; i < sw.Lamps.Length; i++)
		{
			int li = sw.Lamps[i];
			var l = Lamps[li];
			if (l.Broken) continue;
			l.On = on;
			Light?.SetLamp(li, l.Lit);
		}
		Log.Add(on ? SimEventKind.LightsOn : SimEventKind.LightsOff, sw.X, sw.Y,
			byGuard ? 1 : 0, s);
		if (byGuard) return;

		DoorHeard(sw.X, sw.Y, Tune.SwitchNoiseRadius);
		LightsNoticed(s);
	}

	/// <summary>
	/// A room's lights changed and a guard saw it: he is in the room, or has a
	/// line to one of its lamps. He comes to the switch (Curious, not Combat),
	/// and on arriving puts the lights back on (<see cref="TryRestoreLights"/>).
	/// Lamps change nothing visible on a fully lit level, so nobody notices there.
	/// </summary>
	private void LightsNoticed(int s)
	{
		if (Light == null) return;
		var sw = Switches[s];
		long rr = (long)Tune.LightsSeenRange * Tune.LightsSeenRange;
		for (int i = 0; i < Guards.Count; i++)
		{
			var e = Guards[i];
			if (e.Prone || e.State == GuardState.Combat) continue;
			int c = e.X / Level.CellFx, r = e.Y / Level.CellFx;
			bool saw = Level.InBounds(c, r) && sw.Room[r * Level.W + c];
			for (int k = 0; k < sw.Lamps.Length && !saw; k++)
			{
				var l = Lamps[sw.Lamps[k]];
				if (l.Broken) continue;
				if (Fx.DistSq(e.X, e.Y, l.X, l.Y) > rr) continue;
				saw = Geometry.ClearLine(Opaque, e.X, e.Y, l.X, l.Y);
			}
			if (saw) Notice(e, sw.X, sw.Y, Tune.LightsAwareness);
		}
	}

	/// <summary>
	/// A guard who has walked over to look finds a dark switch in reach and
	/// turns it back on (plan §5.2). A broken lamp stays broken: the only way
	/// to keep a room dark is to shoot it out, and that is loud.
	/// </summary>
	private void TryRestoreLights(Actor e)
	{
		if (Light == null) return;
		for (int s = 0; s < Switches.Count; s++)
		{
			var sw = Switches[s];
			if (Fx.Dist(e.X, e.Y, sw.X, sw.Y) > Tune.GuardSwitchReach) continue;
			if (RoomLit(s)) continue;
			bool any = false;
			for (int k = 0; k < sw.Lamps.Length; k++) if (!Lamps[sw.Lamps[k]].Broken) any = true;
			if (any) SetRoom(s, true, true);
		}
	}

	// ------------------------------------------------------- G: doors and switches

	/// <summary>
	/// What G would use: the nearest door in reach or switch in reach, as a
	/// USE index -- a panel index, or Panels.Count + a switch index -- or -1.
	/// One index space, so InputFrame.DoorPick and its `u` token needed no new
	/// field and no new Step argument: a replay with no switches is unchanged.
	/// </summary>
	public int NearestUse()
	{
		int best = NearestDoor();
		int bestDist = best < 0 ? 0 : DistToPanel(best);
		for (int s = 0; s < Switches.Count; s++)
		{
			int d = Fx.Dist(Player.X, Player.Y, Switches[s].X, Switches[s].Y);
			if (d > Tune.SwitchReach) continue;
			if (best < 0 || d < bestDist) { best = Panels.Count + s; bestDist = d; }
		}
		return best;
	}

	/// <summary>Distance from the player to a use index, or -1.</summary>
	public int DistToUse(int index)
	{
		if (index >= 0 && index < Panels.Count) return DistToPanel(index);
		int s = index - Panels.Count;
		if (s < 0 || s >= Switches.Count) return -1;
		return Fx.Dist(Player.X, Player.Y, Switches[s].X, Switches[s].Y);
	}
}
