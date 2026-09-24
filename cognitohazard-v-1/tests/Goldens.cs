using System.Collections.Generic;

namespace Cognitohazard.Tests;

/// <summary>
/// Golden state hashes for the milestone-0 scripted replay (spec §4.2).
///
/// These change ONLY when sim behaviour intentionally changes. Regenerate with
/// `dotnet run --project tests -- --record` and paste the output here, in the
/// same commit as the behaviour change, never separately. A surprise failure
/// here is the drift detector doing its job.
///
/// Rebaked when GUARDS BEGAN FIRING THE GUN THEY CARRY: Actor.Weapon (armed from
/// the loot roll by SimWorld.ArmGuard) and Actor.BurstShots entered the hash, a
/// guard's Mag is his weapon's magazine, and every figure of his fire -- damage,
/// pierce, cadence in bursts, reload, cone, pellets, spin-up, the specialists'
/// traits -- is that weapon's WeaponSpec rather than the flat guard rifle. The
/// guard alphabet grew past 'z' in the same change (Level.GuardGlyphs), which
/// moves nothing here: substation_4's guards are all letters. Replays recorded
/// before this diverge at the first checkpoint.
///
/// Previously: rebaked for FEAR (Guard_AI.md §4.1): Actor.FearMt entered the hash, and a
/// guard at ease who hears the scripted run's shots now rolls to freeze,
/// which draws from the sim's RNG and shifts every later draw.
///
/// Previously: Rebaked for LOOT AS MONEY (sim/LootTable.cs): guard kits are a POINT BUY
/// with each guard's points (Level.PointsFor), and chests share the level's
/// dollar budget (Level.ChestBudget), spent by rarity weights this run's luck
/// tilts. Both replaced hand-weighted roll tables and draw from the same loot
/// stream, so every body and chest on the reference level holds different
/// things. Combat, AI and the combat RNG stream are untouched; replays recorded
/// before this diverge at the first checkpoint because the kits are hashed.
///
/// Previously: rebaked for THE COMPROMISED SWEEP (Guard_AI.md P4): sweep groups, the
/// sweep map's staleness and claims, and each guard's group id entered the
/// hash, and hunting patrollers now sweep in pairs instead of walking their
/// routes.
///
/// Previously: rebaked for THE SQUAD ASSAULT (Guard_AI.md P3): each guard's flank route
/// and sync wait, and each squad's planned LKP and re-plan timer, entered the
/// hash, and backup squads now flank rather than all converging at once.
///
/// Previously: rebaked for GUARD POSTURES AND THE RADIO (Guard_AI.md P1-P2), in the same
/// bake as the specialist weapons' chest rolls (gun rolls 48-52 became the
/// Tesla, Frag and AWM). The guard state machine was replaced by posture +
/// task, a heard shot became Combat, and GuardNet (intel, squads, the
/// compromised flag) entered the hash. Replays recorded before it diverge.
///
/// Previously: rebaked for GUARD PATHFINDING (Guard_AI.md P0). Guards now walk A* paths
/// over a nav grid instead of steering straight at a target, travel toward a
/// waypoint rather than along their facing, and slow to BackpedalQ8 when
/// moving more than 90 degrees off it. The old unstick nudge and its RNG
/// draw are gone (a stuck guard now re-plans), which shifts every later draw.
/// Actor gained its path and SimWorld its path-service cursor, both hashed.
/// A behaviour change, deliberately: replays recorded before it will diverge
/// at the first tick a guard moves, and --verify names that tick.
///
/// Previously: rebaked when ATTACHMENTS WENT PER WEAPON and the player could pack a bag.
/// Loadout now carries two AttachSets instead of one -- the primary's and the
/// holster's, each masked by its own weapon's rails -- and a Carried list that
/// SimWorld auto-places into the pack at Restart. Both are hashed: what is on
/// a gun decides what it fires like, and what is in the bag at tick zero is
/// pack state. Older text parses as an empty second set and an empty bag,
/// which is exactly what those runs were, so replays recorded before this
/// still verify. The scripted run carries nothing and fits nothing, so the
/// move is the shape of the hash, not a change in what the run DOES.
///
/// Previously: rebaked when floors were halved to ten guards. Tune.GuardRecordEvery went
/// 5 -> 4 with them, so record scarcity stayed a decision rather than drifting
/// with the guard count. The Photon and the Vulcan were retuned up in the same
/// change; neither is in the scripted run, but both are in the chest tables.
///
/// Previously: rebaked for five new weapons: WeaponId gained Welrod, Vss, Photon, ArcLance
/// and Vulcan (ordinals 5-9, appended), Actor.SpinMt joined the hash for the
/// rotary spin-up, and the chest weapon roll was rewritten to include them —
/// which changes what every chest on every level holds.
///
/// Previously: rebaked when gear could be dropped: InputFrame.DropPick joined the input,
/// SimWorld.Ground joined the world, and both entered the hash. A replay
/// recorded before drops existed carries no `d` token and parses as dropping
/// nothing, so those still verify.
///
/// Previously: rebaked when guard counts went up two and a half times. The glyph range
/// widened from 'a'-'h' to 'a'-'z' (Level.GuardFirst/GuardLast) and every level
/// went from eight guards to twenty, so the reference level the golden run is
/// measured against is a different floor. Tune.GuardRecordEvery went 3 -> 5 in
/// the same change, or the extra guards would have undone record scarcity by
/// accident rather than by decision.
///
/// Previously: rebaked for mission objectives: the '!' glyph builds a chest carrying the
/// objective item, so Level.Objectives and ChestRuntime.Objective entered the
/// world and the hash, and every shipped level gained a '!'. Objective sites
/// draw NOTHING from the loot stream, so the supply chests on a level are
/// unchanged -- Economy.Objectives() asserts it.
///
/// Previously: rebaked for the campaign pass: records were made SCARCE (most guards now
/// carry none, a cache holds one), and gear CHESTS entered the world and the
/// state hash. Chest contents are rolled after the guard kits precisely so the
/// guard stream is unchanged -- Economy.Chests() asserts that a level gaining a
/// chest does not re-roll the bodies on it.
///
/// Previously: rebaked for movement tiers: the player's stance is no longer a bool but one
/// of four tiers, so Actor.MoveTier replaced Actor.Sneaking in the hash and
/// Actor.ReadyMt (the post-sprint recovery) joined it. Tiers 0 and 1 reproduce
/// the old sneak and walk exactly -- same speed, noise and detection, asserted
/// in Handling.MoveTiers() -- so nothing about the scripted run's BEHAVIOUR
/// moved; only what is folded into the hash did.
///
/// Previously: rebaked for armoured guards: guards spawn on Tune.GuardHealth rather
/// than the player's pool, and wear the vest the loot roll already put on their
/// body, so Actor.Armour and Actor.ArmourMax entered the state hash. The vest
/// roll itself is unchanged and still comes off LootRng, so the combat RNG
/// stream is untouched.
///
/// Previously: rebaked for the firearm pass: muzzle velocities roughly tripled (with round
/// lifetimes cut to match, so reach did not move), the player's aim gained a
/// per-weapon turn rate instead of snapping to the cursor, both the player and
/// guards gained a sway term in the firing cone, guards gained the
/// sustained-fire term, the shotgun was choked and its pellets patterned, and
/// StepPlayer finally reads the loadout's WalkSpeed/SneakSpeed/AimMoveQ8 rather
/// than the flat Tune constants. Actor.SwayQ8 entered the state hash.
///
/// RE-BAKED when the four APPAREL slots (helmet, footware, shirt, arms) entered
/// Loadout and its hash. They change no stat and never will until a spec reads
/// them — but they can now be put on MID-MISSION, and anything that can move
/// during a run has to be hashed or the replay of that run diverges from it.
///
/// NOTE: these values are also a function of levels/substation_4.txt, which the
/// in-game editor writes to. If that level is re-saved, these need re-baking
/// with it -- a golden failure right after a level edit is that, not a sim
/// regression.
///
/// Previously: rebaked when looting became a per-item choice: the loot dwell was
/// removed outright, so the per-guard dwell timer left the state hash. What each
/// guard still carries stays in it.
/// Previously: rebaked at the inventory pass: the mission pack, what each guard is still
/// carrying, the loot dwell, the swap timer, the stowed magazine and the
/// secondary-weapon fields all entered the state hash. No combat behaviour
/// changed -- guard kits are rolled from their own stream so the combat RNG
/// stream is untouched.
/// Previously: rebaked at milestones 7, 8 and 9. Milestone 9 renamed the weapons, retuned
/// the Glock to 17 rounds and a 400 px report, and made the floor alarm
/// radius-gated.
/// Previously: rebaked at milestone 7 (loadout entered the hash) and milestone 8
/// (health and armour entered it, and guards stopped dying to one round).
/// Originally rebaked at milestone 7, when the loadout entered the state hash
/// (RPG extension plan risk 9.4). Sim behaviour did not change: the pistol
/// carries the pre-loadout constants exactly, asserted in Loadouts.Parity().
/// </summary>
public static class Goldens
{
	public static readonly Dictionary<int, ulong> Frames = new()
	{
		{ 60, 0x28EAA863E7CE619BUL },
		{ 300, 0x46B43B22AEEAA288UL },
		{ 900, 0x1823916C9CFDA0A4UL },
	};

	public const ulong Final = 0x1823916C9CFDA0A4UL;
}
