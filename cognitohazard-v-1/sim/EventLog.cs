using System.Collections.Generic;

namespace Cognitohazard.Sim;

public enum SimEventKind
{
	PlayerShot, GuardShot, DryFire, Reload,
	WallHit, GuardKilled, PlayerKilled, Subdue,
	Pickup, Degrade, Destroy, Notice, Alert, BodyFound, Exit,

	// APPENDED at milestone 8. game/ mirrors these ordinals by hand, so new
	// kinds go on the END — inserting one above would silently remap every
	// sound and effect to the wrong event.
	PlayerHurt, ArmourBroken, GuardHurt,

	// APPENDED at the aiming pass. Same rule: new kinds go on the END.
	AimLocked, AimLost, Headshot,

	// APPENDED at the inventory pass. Same rule: new kinds go on the END.
	Looted, PackFull, WeaponSwapped,

	// APPENDED with armoured guards. Same rule: new kinds go on the END.
	// GuardArmourHit carries what the plate absorbed in Value, so game/ can
	// spark off the armour instead of spraying blood -- without that, rounds
	// that do nothing look exactly like rounds that are working.
	GuardArmourHit, GuardArmourBroken,

	// APPENDED when gear could be dropped. Same rule: new kinds go on the END.
	Dropped,

	// APPENDED with the developer menu's mid-run spawn. Same rule: on the END.
	Spawned,

	// APPENDED when gear could be put on mid-mission. Same rule: on the END.
	Equipped,

	// APPENDED with glass and doors. Same rule: on the END. Value carries the
	// Level.Panels index; for the door events Heading is 1 when a GUARD moved
	// it and 0 when the player did, so game/ can tell "you opened it" from
	// "something on the other side just did".
	GlassBroken, DoorOpened, DoorClosed,
	/// <summary>A close refused because someone is standing in the doorway.</summary>
	DoorBlocked,

	// APPENDED with the specialist weapons. Same rule: on the END.
	/// <summary>A penetrating round went INTO a wall and on through it.</summary>
	WallPierced,
	/// <summary>
	/// One node of a lightning discharge, in chain order. Value is the hop:
	/// 0 is the strike point, and each later node is joined to the one before
	/// it, so game/ draws the chain from the order alone. Heading is 1 when
	/// the node is the PLAYER -- the arc came back.
	/// </summary>
	ArcJump,
	GrenadeThrown,
	GrenadeBounce,
	/// <summary>A grenade went off. Value is the fragment count.</summary>
	Blast,

	// APPENDED with Guard AI v2's radio (Guard_AI.md §5.3, §7). Same rule: on
	// the END. Value is the RadioPurpose (1 backup, 2 report).
	/// <summary>A guard began keying his radio.</summary>
	RadioStart,
	/// <summary>The call went through: backup is on the way, or the report is in.</summary>
	RadioSent,
	/// <summary>The caller went down mid-call. Nobody heard it.</summary>
	RadioCut,
	/// <summary>The level knows it has an intruder, for the rest of the run.</summary>
	Compromised,

	// APPENDED with fear (Guard_AI.md §4.1). Same rule: on the END.
	/// <summary>A guard froze in fear. Value: 1 gunfire, 2 an ally's death.</summary>
	Afraid,
}

public readonly struct SimEvent
{
	public readonly SimEventKind Kind;
	public readonly int X, Y, Heading, Value;

	public SimEvent(SimEventKind kind, int x, int y, int heading = 0, int value = 0)
	{ Kind = kind; X = x; Y = y; Heading = heading; Value = value; }
}

/// <summary>
/// Append-only record of everything that happened (spec §3.1), cleared at the
/// top of each tick. This is how game/ learns it should throw a muzzle flash or
/// a blood spray without sim/ ever knowing that presentation exists.
///
/// Events do NOT feed the state hash: they are derived from state transitions
/// that are already hashed, so including them would only make the hash brittle.
/// </summary>
public sealed class EventLog
{
	public readonly List<SimEvent> Events = new();

	public void Clear() => Events.Clear();

	public void Add(SimEventKind kind, int x, int y, int heading = 0, int value = 0)
		=> Events.Add(new SimEvent(kind, x, y, heading, value));
}
