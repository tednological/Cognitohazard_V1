using System.Collections.Generic;

namespace Cognitohazard.Sim;

/// <summary>
/// Weapon identity. The ordinal is part of the replay format and the state
/// hash, so values are fixed forever: append, never reorder or reuse.
/// </summary>
public enum WeaponId
{
	Glock = 0,      // pistol
	Mp7 = 1,        // submachine gun
	Ak47 = 2,       // assault rifle
	Remington = 3,  // shotgun
	Saw = 4,        // machine gun

	// APPENDED. Ordinals are fixed forever; a reorder would silently rewrite
	// every saved stash and every recorded replay.
	Welrod = 5,     // silenced pistol
	Vss = 6,        // silenced marksman rifle
	Photon = 7,     // laser carbine
	ArcLance = 8,   // heavy laser
	Vulcan = 9,     // minigun

	// APPENDED with the three specialists. Same rule: on the END, forever.
	Tesla = 10,     // lightning gun: one-shot kills that chain
	Frag = 11,      // hand grenades: thrown, bounce, burst into shrapnel
	Awm = 12,       // bolt-action sniper rifle: goes through walls
}

/// <summary>Attachment slots (RPG plan §3A). Ordinals are hashed; append only.</summary>
public enum AttachSlot
{
	Sight = 0,
	Grip = 1,
	Rail = 2,
	Magazine = 3,
	Ammo = 4,
	Stock = 5,
}

/// <summary>
/// Everything the weapon path needs, as plain integers. A BASE spec comes from
/// the weapon; attachments fold their deltas in to produce the EFFECTIVE spec
/// the sim actually fires with.
///
/// Units match the rest of sim/: distances in 1/256 px, angles in BRAD,
/// durations in ticks, speeds in 1/256 px per second.
/// </summary>
public readonly struct WeaponSpec
{
	/// <summary>Damage per projectile. The shotgun's figure is PER PELLET.</summary>
	public readonly int Damage;

	/// <summary>Fraction of damage that ignores armour, Q8. AP ammo raises it.</summary>
	public readonly int ArmourPierce;

	public readonly int Magazine;
	public readonly int FireCooldownTicks;
	public readonly int ReloadTicks;
	public readonly int DryFireTicks;

	/// <summary>Total cone WIDTH at zero heat; a shot draws +/- half of it.</summary>
	public readonly int SpreadBase;

	/// <summary>Additional cone width at maximum heat.</summary>
	public readonly int SpreadPerHeat;

	public readonly int HeatPerShot;
	public readonly int HeatDecayPerSec;

	public readonly int BulletSpeed;
	public readonly int BulletTicks;
	public readonly int Pellets;

	/// <summary>How far the report carries as a stimulus (spec §8.3). The axis
	/// that actually separates these weapons.</summary>
	public readonly int GunshotRadius;

	public readonly int MuzzleOffset;

	/// <summary>Change to the carrier's movement speed. Negative for anything
	/// heavier than a sidearm.</summary>
	public readonly int SpeedDelta;

	/// <summary>Spread multiplier while aiming, Q8. Below 256 means aiming
	/// tightens the cone. Hip-fire uses the base spread unchanged, so aiming is
	/// a bonus paid for with mobility rather than a nerf to firing from the hip.</summary>
	public readonly int AimSpreadQ8;

	/// <summary>Movement multiplier while aiming, Q8. The cost of aiming.</summary>
	public readonly int AimMoveQ8;

	/// <summary>
	/// Additional cone width at maximum SWAY -- how badly this weapon is upset
	/// by being swung onto a target or fired on the move. A long, heavy weapon
	/// is punished here far harder than a sidearm.
	/// </summary>
	public readonly int SpreadPerSway;

	/// <summary>
	/// How fast the aim swings toward the cursor, as a per-tick fraction over
	/// <see cref="Tune.TurnDen"/> -- the same proportional lerp the guards use
	/// (ruling #3). This is the weight of the weapon: a Glock is on target in
	/// three ticks, a SAW takes four times that.
	/// </summary>
	public readonly int TurnNum;

	/// <summary>
	/// Ticks of held trigger before the FIRST round leaves. Zero for everything
	/// that is not a rotary weapon.
	///
	/// This is what a minigun is: 300 rounds at thirty a second is not a
	/// decision, and the commitment you make before the first one is. It winds
	/// down faster than it winds up, so tapping is punished and holding is not.
	/// </summary>
	public readonly int SpinUpTicks;

	/// <summary>
	/// How many bodies one discharge can take, the first included: a LIGHTNING
	/// weapon. Zero for everything else. The bolt kills what it strikes and
	/// jumps to the nearest living guard within <see cref="Tune.ArcReach"/>,
	/// and on until the charge is spent -- but a shooter within
	/// <see cref="Tune.ArcPlayerReach"/> of any node is a better conductor than
	/// any guard, and it comes back down the line to them (SimWorld.Discharge).
	/// </summary>
	public readonly int ArcTargets;

	/// <summary>
	/// How many walls a round goes THROUGH before one stops it. Zero for
	/// everything that is not a penetrator. Each wall costs
	/// <see cref="Tune.WallPierceLossQ8"/> of what the round was carrying, so
	/// the second wall is a worse shot than the first.
	/// </summary>
	public readonly int WallPierce;

	/// <summary>
	/// THROWN, not fired. The projectile is a grenade: it bounces off walls,
	/// rolls to a stop, and when BulletTicks (its fuse) runs out it bursts into
	/// <see cref="Tune.FragCount"/> fragments of Damage each, which hit guards
	/// and the thrower alike. BulletSpeed is how hard it leaves the hand.
	/// </summary>
	public readonly bool Grenade;

	public WeaponSpec(int damage, int armourPierce, int magazine, int fireCooldownTicks,
		int reloadTicks, int dryFireTicks, int spreadBase, int spreadPerHeat,
		int heatPerShot, int heatDecayPerSec, int bulletSpeed, int bulletTicks,
		int pellets, int gunshotRadius, int muzzleOffset, int speedDelta,
		int aimSpreadQ8 = 90, int aimMoveQ8 = 140,
		int spreadPerSway = 730, int turnNum = 165, int spinUpTicks = 0,
		int arcTargets = 0, int wallPierce = 0, bool grenade = false)
	{
		SpinUpTicks = spinUpTicks;
		ArcTargets = arcTargets;
		WallPierce = wallPierce;
		Grenade = grenade;
		AimSpreadQ8 = aimSpreadQ8;
		AimMoveQ8 = aimMoveQ8;
		SpreadPerSway = spreadPerSway;
		TurnNum = turnNum;
		Damage = damage;
		ArmourPierce = armourPierce;
		Magazine = magazine;
		FireCooldownTicks = fireCooldownTicks;
		ReloadTicks = reloadTicks;
		DryFireTicks = dryFireTicks;
		SpreadBase = spreadBase;
		SpreadPerHeat = spreadPerHeat;
		HeatPerShot = heatPerShot;
		HeatDecayPerSec = heatDecayPerSec;
		BulletSpeed = bulletSpeed;
		BulletTicks = bulletTicks;
		Pellets = pellets;
		GunshotRadius = gunshotRadius;
		MuzzleOffset = muzzleOffset;
		SpeedDelta = speedDelta;
	}

	/// <summary>Fold one attachment in. Additive throughout except the magazine,
	/// which scales by an exact integer ratio because a flat "+10 rounds" means
	/// something very different on a 17-round Glock and a 200-round belt.</summary>
	public WeaponSpec With(in AttachmentSpec a)
	{
		int mag = MagDen(a) == 0 ? Magazine : (int)((long)Magazine * a.MagNum / a.MagDen);
		return new WeaponSpec(
			damage: Clamp(Damage + a.Damage, 5, 9999),
			armourPierce: Clamp(ArmourPierce + a.ArmourPierce, 0, 256),
			magazine: Clamp(mag + a.MagazineFlat, 1, 9999),
			fireCooldownTicks: Clamp(FireCooldownTicks + a.FireCooldown, 1, 600),
			reloadTicks: Clamp(ReloadTicks + a.Reload, 12, 900),
			dryFireTicks: DryFireTicks,
			spreadBase: Clamp(SpreadBase + a.SpreadBase, 0, 20000),
			spreadPerHeat: Clamp(SpreadPerHeat + a.SpreadPerHeat, 0, 20000),
			heatPerShot: Clamp(HeatPerShot + a.HeatPerShot, 4, 256),
			heatDecayPerSec: Clamp(HeatDecayPerSec + a.HeatDecay, 32, 4096),
			bulletSpeed: Clamp(BulletSpeed + a.BulletSpeed, 50 * Fx.One, 6000 * Fx.One),
			bulletTicks: Clamp(BulletTicks + a.BulletTicks, 6, 600),
			pellets: Pellets,
			gunshotRadius: Clamp(GunshotRadius + a.GunshotRadius, 60 * Fx.One, 2000 * Fx.One),
			muzzleOffset: MuzzleOffset,
			speedDelta: SpeedDelta + a.SpeedDelta,
			aimSpreadQ8: Clamp(AimSpreadQ8 + a.AimSpread, 16, 256),
			aimMoveQ8: Clamp(AimMoveQ8 + a.AimMove, 32, 256),
			spreadPerSway: Clamp(SpreadPerSway + a.SpreadPerSway, 0, 20000),
			turnNum: Clamp(TurnNum + a.TurnRate, Tune.TurnAimMin, Tune.TurnAimMax),
			// No attachment shortens a spin-up. It is the weapon's identity,
			// not a stat to buy off.
			spinUpTicks: SpinUpTicks,
			// Nor the three specialist traits, for the same reason: they are
			// what the weapon IS. Carried through untouched.
			arcTargets: ArcTargets,
			wallPierce: WallPierce,
			grenade: Grenade);
	}

	private static int MagDen(in AttachmentSpec a) => a.MagDen;
	private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
}

/// <summary>
/// One attachment's contribution. Everything is a DELTA onto the base weapon,
/// applied in fixed slot order and then clamped (RPG plan §3A). No floats, no
/// percentages, so the result is identical on every machine.
/// </summary>
public readonly struct AttachmentSpec
{
	public readonly int Damage, ArmourPierce;
	public readonly int MagNum, MagDen, MagazineFlat;
	public readonly int FireCooldown, Reload;
	public readonly int SpreadBase, SpreadPerHeat, HeatPerShot, HeatDecay;
	public readonly int BulletSpeed, BulletTicks, GunshotRadius;
	public readonly int SpeedDelta;
	public readonly int AimSpread, AimMove;

	/// <summary>Delta onto the weapon's sway sensitivity and its aim turn rate:
	/// the handling half of an attachment, as opposed to the ballistic half.</summary>
	public readonly int SpreadPerSway, TurnRate;

	/// <summary>Extra reach on the player's visibility polygon. The flashlight.</summary>
	public readonly int VisionRadius;

	/// <summary>Bonus to how readily guards pick the player up, Q8 added onto
	/// 256. The flashlight's cost.</summary>
	public readonly int DetectionBonus;

	public AttachmentSpec(
		int damage = 0, int armourPierce = 0,
		int magNum = 1, int magDen = 1, int magazineFlat = 0,
		int fireCooldown = 0, int reload = 0,
		int spreadBase = 0, int spreadPerHeat = 0, int heatPerShot = 0, int heatDecay = 0,
		int bulletSpeed = 0, int bulletTicks = 0, int gunshotRadius = 0,
		int speedDelta = 0, int visionRadius = 0, int detectionBonus = 0,
		int aimSpread = 0, int aimMove = 0,
		int spreadPerSway = 0, int turnRate = 0)
	{
		AimSpread = aimSpread; AimMove = aimMove;
		SpreadPerSway = spreadPerSway; TurnRate = turnRate;
		Damage = damage; ArmourPierce = armourPierce;
		MagNum = magNum; MagDen = magDen; MagazineFlat = magazineFlat;
		FireCooldown = fireCooldown; Reload = reload;
		SpreadBase = spreadBase; SpreadPerHeat = spreadPerHeat;
		HeatPerShot = heatPerShot; HeatDecay = heatDecay;
		BulletSpeed = bulletSpeed; BulletTicks = bulletTicks; GunshotRadius = gunshotRadius;
		SpeedDelta = speedDelta; VisionRadius = visionRadius; DetectionBonus = detectionBonus;
	}
}

public static class WeaponCatalog
{
	public const int Count = 13;

	// Slot masks: which attachment slots each weapon actually has.
	private const int S = 1 << (int)AttachSlot.Sight;
	private const int G = 1 << (int)AttachSlot.Grip;
	private const int R = 1 << (int)AttachSlot.Rail;
	private const int M = 1 << (int)AttachSlot.Magazine;
	private const int A = 1 << (int)AttachSlot.Ammo;
	private const int K = 1 << (int)AttachSlot.Stock;

	/// <summary>Quiet, accurate, and the reason the pistol is the stealth
	/// default: 400 px of report against the Remington's 950.</summary>
	private static readonly WeaponSpec GlockSpec = new WeaponSpec(
		damage: 55, armourPierce: 0, magazine: 17,
		fireCooldownTicks: 10, reloadTicks: 84, dryFireTicks: 13,
		spreadBase: 146, spreadPerHeat: 887,
		heatPerShot: 87, heatDecayPerSec: Tune.HeatDecayPerSec,
		bulletSpeed: Tune.PlayerBulletSpeed, bulletTicks: Tune.PlayerBulletTicks, pellets: 1,
		gunshotRadius: 400 * Fx.One, muzzleOffset: Tune.MuzzleOffset, speedDelta: 0,
		aimSpreadQ8: 90, aimMoveQ8: 150,    // a sidearm settles fast and barely slows you
		spreadPerSway: 522, turnNum: 165);  // and comes onto a target in three ticks

	private static readonly WeaponSpec Mp7Spec = new WeaponSpec(
		damage: 50, armourPierce: 0, magazine: 40,
		fireCooldownTicks: 4, reloadTicks: 120, dryFireTicks: 13,
		spreadBase: 522, spreadPerHeat: 1252,
		heatPerShot: 56, heatDecayPerSec: Tune.HeatDecayPerSec,
		bulletSpeed: 614400, bulletTicks: 35, pellets: 1,    // 2400 px/s, ~1400 px
		gunshotRadius: 700 * Fx.One, muzzleOffset: Tune.MuzzleOffset, speedDelta: -4 * Fx.One,
		aimSpreadQ8: 110, aimMoveQ8: 140,    // compact, still mobile
		spreadPerSway: 730, turnNum: 126);   // and quick enough to swing indoors

	/// <summary>
	/// 70 damage, not the 100 the plan first wrote down. At 100 the AK one-shot
	/// every unarmoured target while carrying thirty rounds at seven a second,
	/// which made every other weapon pointless. 70 keeps it at two rounds
	/// unarmoured and four through heavy plate.
	/// </summary>
	private static readonly WeaponSpec Ak47Spec = new WeaponSpec(
		damage: 70, armourPierce: 0, magazine: 30,
		fireCooldownTicks: 8, reloadTicks: 126, dryFireTicks: 13,
		spreadBase: 313, spreadPerHeat: 1565,
		heatPerShot: 95, heatDecayPerSec: Tune.HeatDecayPerSec,
		bulletSpeed: 742400, bulletTicks: 42, pellets: 1,   // 2900 px/s, ~2030 px
		gunshotRadius: 800 * Fx.One, muzzleOffset: Tune.MuzzleOffset, speedDelta: -10 * Fx.One,
		aimSpreadQ8: 80, aimMoveQ8: 110,    // the most it gains from aiming, and it plants you
		spreadPerSway: 1043, turnNum: 90);  // a rifle punishes a flick

	/// <summary>
	/// Seven pellets, and rounds that expire at about 570 px. Range falloff is
	/// emergent rather than a curve.
	///
	/// CHOKED, on request. The cone was 3129 BRAD wide with 1043 more per heat,
	/// and because a shot's own heat lands before its spread is drawn, every
	/// single shell opened at 0.378 rad -- a wall of buckshot you could not miss
	/// with at five paces and could not hit with at twenty. It is now 1565 plus
	/// 522, so a shell patterns at about 0.19 rad, and the pellets inside that
	/// cone are laid out evenly rather than drawn seven times at random
	/// (Tune.ChokeJitterQ8). A choke that tight is a real mid-range weapon.
	/// </summary>
	private static readonly WeaponSpec RemingtonSpec = new WeaponSpec(
		damage: 22, armourPierce: 0, magazine: 8,
		fireCooldownTicks: 54, reloadTicks: 156, dryFireTicks: 13,
		spreadBase: 1565, spreadPerHeat: 522,
		// A pump gun cycles slower (54 ticks) than the shared 1.5/s decay clears
		// a full head of heat (43 ticks), so at the stock decay the term reset
		// between every shell and sustained fire did literally nothing to a
		// shotgun. Less heat per shell against a much slower bleed instead: the
		// first shell of a tube patterns tight, and walking through all eight as
		// fast as it will cycle opens the pattern by about a fifth.
		heatPerShot: 90, heatDecayPerSec: 64,
		bulletSpeed: 486400, bulletTicks: 18, pellets: 7,   // 1900 px/s, ~570 px
		gunshotRadius: 950 * Fx.One, muzzleOffset: Tune.MuzzleOffset, speedDelta: -14 * Fx.One,
		aimSpreadQ8: 190, aimMoveQ8: 120,    // a shotgun cone barely cares about aiming
		spreadPerSway: 730, turnNum: 78);    // but it is long, and slow to bring round

	/// <summary>Belt-fed. Separated from the MP7 by capacity, noise and a heavy
	/// movement penalty rather than by damage.</summary>
	private static readonly WeaponSpec SawSpec = new WeaponSpec(
		damage: 50, armourPierce: 0, magazine: 200,
		fireCooldownTicks: 3, reloadTicks: 300, dryFireTicks: 13,
		spreadBase: 730, spreadPerHeat: 2086,
		heatPerShot: 40, heatDecayPerSec: Tune.HeatDecayPerSec,
		bulletSpeed: 716800, bulletTicks: 38, pellets: 1,   // 2800 px/s, ~1770 px
		gunshotRadius: 1000 * Fx.One, muzzleOffset: Tune.MuzzleOffset, speedDelta: -34 * Fx.One,
		aimSpreadQ8: 150, aimMoveQ8: 60,     // braced fire: accurate-ish, nearly stationary
		spreadPerSway: 1565, turnNum: 51);   // swung onto a target it hits nothing at all

	// ==================================================================
	// APPENDED WEAPONS
	//
	// Three families that did not exist: SILENCED, ENERGY and ROTARY. Each
	// exists to be a different answer to the same room, not a bigger number
	// than the last one.
	//   - silenced trades damage and cadence for a report that does not
	//     summon the floor (GunshotRadius is the stealth axis, spec §8.3);
	//   - energy trades magazine and cooling for armour PIERCE and a bolt
	//     that arrives before the target has moved;
	//   - rotary trades everything for volume, and pays for it with a
	//     spin-up you have to commit to.
	// ==================================================================

	/// <summary>
	/// The assassination pistol. 130 px of report against the Glock's 400 —
	/// quiet enough to kill in a room next to a patrol — and bolt-action slow,
	/// so a miss is a genuine disaster rather than a second trigger pull.
	/// </summary>
	private static readonly WeaponSpec WelrodSpec = new WeaponSpec(
		damage: 60, armourPierce: 0, magazine: 6,
		fireCooldownTicks: 34, reloadTicks: 100, dryFireTicks: 13,
		spreadBase: 120, spreadPerHeat: 500,
		heatPerShot: 120, heatDecayPerSec: Tune.HeatDecayPerSec,
		bulletSpeed: 430080, bulletTicks: 60, pellets: 1,   // 1680 px/s, subsonic
		gunshotRadius: 130 * Fx.One, muzzleOffset: Tune.MuzzleOffset, speedDelta: 0,
		aimSpreadQ8: 80, aimMoveQ8: 150,
		spreadPerSway: 460, turnNum: 160);

	/// <summary>
	/// Quiet at rifle range. 85 damage puts a bare guard down in one, which is
	/// the entire argument for carrying something that holds ten rounds and
	/// takes a third of a second between them.
	/// </summary>
	private static readonly WeaponSpec VssSpec = new WeaponSpec(
		damage: 85, armourPierce: 0, magazine: 10,
		fireCooldownTicks: 22, reloadTicks: 140, dryFireTicks: 13,
		spreadBase: 90, spreadPerHeat: 900,
		heatPerShot: 140, heatDecayPerSec: Tune.HeatDecayPerSec,
		bulletSpeed: 460800, bulletTicks: 70, pellets: 1,   // 1800 px/s, subsonic
		gunshotRadius: 260 * Fx.One, muzzleOffset: Tune.MuzzleOffset,
		speedDelta: -8 * Fx.One,
		aimSpreadQ8: 60, aimMoveQ8: 90,      // the most any weapon gains from aiming
		spreadPerSway: 1200, turnNum: 74);   // and it is long, and slow to bring round

	/// <summary>
	/// The energy answer to armour. Half of every bolt ignores plate, which is
	/// what makes it worth carrying against a floor of heavies — and it
	/// OVERHEATS, so the cost is paid in bursts rather than in ammunition.
	///
	/// RETUNED UP: 52 damage over 30 cells, against 38 over 24. Half of 38 got
	/// through a heavy plate, which is four bolts to a kill through a barrel
	/// that will not take four, so the pierce it is built around never paid.
	/// </summary>
	private static readonly WeaponSpec PhotonSpec = new WeaponSpec(
		damage: 52, armourPierce: 128, magazine: 30,
		fireCooldownTicks: 7, reloadTicks: 150, dryFireTicks: 13,
		spreadBase: 60, spreadPerHeat: 1700,               // pinpoint cold, wild hot
		heatPerShot: 100, heatDecayPerSec: 300,            // and slow to cool
		bulletSpeed: 1408000, bulletTicks: 22, pellets: 1, // 5500 px/s
		gunshotRadius: 300 * Fx.One, muzzleOffset: Tune.MuzzleOffset,
		speedDelta: -6 * Fx.One,
		aimSpreadQ8: 70, aimMoveQ8: 120,
		spreadPerSway: 800, turnNum: 110);

	/// <summary>
	/// Three quarters of a bolt goes through anything. Eight shots, two thirds
	/// of a second between them, and a bolt that crosses the field in a sixth
	/// of a second — a weapon for one considered shot at a time.
	/// </summary>
	private static readonly WeaponSpec ArcLanceSpec = new WeaponSpec(
		damage: 95, armourPierce: 192, magazine: 8,
		fireCooldownTicks: 40, reloadTicks: 200, dryFireTicks: 13,
		spreadBase: 40, spreadPerHeat: 1400,
		heatPerShot: 210, heatDecayPerSec: 260,
		bulletSpeed: 1484800, bulletTicks: 24, pellets: 1, // 5800 px/s
		gunshotRadius: 520 * Fx.One, muzzleOffset: Tune.MuzzleOffset,
		speedDelta: -20 * Fx.One,
		aimSpreadQ8: 55, aimMoveQ8: 80,
		spreadPerSway: 1500, turnNum: 62);

	/// <summary>
	/// Three hundred rounds at thirty a second, and the loudest thing on any
	/// floor at 1200 px — firing it once tells everyone where you are.
	///
	/// RETUNED UP: 58 damage on a 0.6s spool, against 45 on 0.75s. At the first
	/// numbers it cost more to bring than it returned — the loudest weapon in
	/// the game has to be worth having told everyone where you are.
	///
	/// The SPIN-UP is the weapon. Three quarters of a second of held trigger
	/// before the first round, which you cannot take back: it turns "shoot
	/// that" into a decision made before the target is in front of you. It
	/// also roots you — 45 px/s of the walk speed left while aiming — so the
	/// answer to a minigun is to not be where it is pointing.
	/// </summary>
	private static readonly WeaponSpec VulcanSpec = new WeaponSpec(
		damage: 58, armourPierce: 0, magazine: 300,
		fireCooldownTicks: 2, reloadTicks: 420, dryFireTicks: 13,
		spreadBase: 900, spreadPerHeat: 2400,
		heatPerShot: 26, heatDecayPerSec: Tune.HeatDecayPerSec,
		bulletSpeed: 691200, bulletTicks: 40, pellets: 1,  // 2700 px/s
		gunshotRadius: 1200 * Fx.One, muzzleOffset: Tune.MuzzleOffset,
		speedDelta: -40 * Fx.One,
		aimSpreadQ8: 170, aimMoveQ8: 45,
		spreadPerSway: 1900, turnNum: 38, spinUpTicks: 36);

	// ==================================================================
	// THE SPECIALISTS
	//
	// Each breaks one rule every other weapon obeys: a round kills ONE thing,
	// a round stops at a WALL, and a weapon fires where it is POINTED. Each
	// pays for it in the currency that rule was protecting.
	// ==================================================================

	/// <summary>
	/// The lightning gun. Whatever it strikes dies -- 999 through full pierce,
	/// plate or no plate -- and the discharge jumps on to up to three more
	/// guards within 200 px of each other. Three charges and six seconds to
	/// recharge the bank, so it is a weapon for the moment a room bunches up.
	///
	/// THE CATCH is the arc rule: the shooter is the best conductor on the
	/// floor. Any node of the chain within 110 px of you and it comes back down
	/// the line, and a bolt that kills anything kills you too. Fire it into a
	/// guard at arm's length, or into the wall beside you, and you are the
	/// second body. It has to be used from a distance, which is what makes it
	/// a decision rather than a win button.
	/// </summary>
	private static readonly WeaponSpec TeslaSpec = new WeaponSpec(
		damage: 999, armourPierce: 256, magazine: 3,
		fireCooldownTicks: 45, reloadTicks: 360, dryFireTicks: 13,
		spreadBase: 80, spreadPerHeat: 900,
		heatPerShot: 120, heatDecayPerSec: Tune.HeatDecayPerSec,
		bulletSpeed: 1536000, bulletTicks: 9, pellets: 1,   // 6000 px/s, ~900 px
		gunshotRadius: 900 * Fx.One, muzzleOffset: Tune.MuzzleOffset,
		speedDelta: -12 * Fx.One,
		aimSpreadQ8: 90, aimMoveQ8: 110,
		spreadPerSway: 900, turnNum: 80, arcTargets: 4);

	/// <summary>
	/// Hand grenades, three to a bandolier. Thrown at 800 px/s, they bounce off
	/// walls at half speed and roll to a stop about 270 px out -- aim held
	/// makes it an underhand lob of half that. After 1.6 s the fuse goes and
	/// 28 fragments of 40 fly out evenly in every direction.
	///
	/// Shrapnel is a PATTERN, not a radius: it is walls that protect, so a
	/// guard round a corner is safe and one standing on the grenade takes most
	/// of the 28. It hits whoever is standing there -- you included. The blast
	/// is heard 1100 px out and everyone who hears it comes to LOOK AT THE
	/// BLAST, not at you, so one thrown the other way is also a distraction.
	/// </summary>
	private static readonly WeaponSpec FragSpec = new WeaponSpec(
		damage: 40, armourPierce: 0, magazine: 3,
		fireCooldownTicks: 50, reloadTicks: 240, dryFireTicks: 13,
		spreadBase: 400, spreadPerHeat: 300,
		heatPerShot: 60, heatDecayPerSec: Tune.HeatDecayPerSec,
		bulletSpeed: 204800, bulletTicks: 96, pellets: 1,   // 800 px/s throw, 1.6 s fuse
		// The throw itself: a pin and a grunt. The BLAST has its own radius.
		gunshotRadius: 60 * Fx.One, muzzleOffset: Tune.MuzzleOffset,
		speedDelta: -4 * Fx.One,
		aimSpreadQ8: 200, aimMoveQ8: 150,
		spreadPerSway: 600, turnNum: 150, grenade: true);

	/// <summary>
	/// The penetrator. 160 damage with half of it through plate kills anything
	/// on the floor in one, and the round does not stop at a wall: it goes
	/// through up to three, losing 30% at each, so a guard behind one wall is
	/// dead unless he is in heavy plate and one behind two is a coin flip.
	///
	/// Paid for everywhere else. A 1.4 s bolt between shots, five in the
	/// magazine, a report that carries 1100 px, and it is the most sway-
	/// sensitive thing in the game: hip-fired it is 0.04 rad wide, aimed and
	/// settled it is a needle. A miss is a very long time to wait.
	/// </summary>
	private static readonly WeaponSpec AwmSpec = new WeaponSpec(
		damage: 160, armourPierce: 128, magazine: 5,
		fireCooldownTicks: 84, reloadTicks: 200, dryFireTicks: 13,
		spreadBase: 420, spreadPerHeat: 1200,
		heatPerShot: 160, heatDecayPerSec: Tune.HeatDecayPerSec,
		bulletSpeed: 972800, bulletTicks: 44, pellets: 1,   // 3800 px/s, ~2790 px
		gunshotRadius: 1100 * Fx.One, muzzleOffset: Tune.MuzzleOffset,
		speedDelta: -16 * Fx.One,
		aimSpreadQ8: 20, aimMoveQ8: 70,      // aiming is the whole weapon
		spreadPerSway: 2000, turnNum: 52, wallPierce: 3);

	public static WeaponSpec Get(WeaponId id) => id switch
	{
		WeaponId.Tesla => TeslaSpec,
		WeaponId.Frag => FragSpec,
		WeaponId.Awm => AwmSpec,
		WeaponId.Welrod => WelrodSpec,
		WeaponId.Vss => VssSpec,
		WeaponId.Photon => PhotonSpec,
		WeaponId.ArcLance => ArcLanceSpec,
		WeaponId.Vulcan => VulcanSpec,
		WeaponId.Mp7 => Mp7Spec,
		WeaponId.Ak47 => Ak47Spec,
		WeaponId.Remington => RemingtonSpec,
		WeaponId.Saw => SawSpec,
		_ => GlockSpec,
	};

	/// <summary>Which slots this weapon accepts. The Glock has no stock and no
	/// grip; the SAW's grip is an integral bipod.</summary>
	public static int SlotMask(WeaponId id) => id switch
	{
		WeaponId.Glock => S | R | M | A,
		WeaponId.Mp7 => S | G | R | M | A | K,
		WeaponId.Ak47 => S | G | R | M | A | K,
		WeaponId.Remington => S | G | R | M | A | K,
		WeaponId.Saw => S | R | M | A | K,
		// A bolt-action sidearm takes no grip and no stock.
		WeaponId.Welrod => S | R | M | A,
		WeaponId.Vss => S | G | R | M | A | K,
		// Energy weapons have no barrel to hang a grip on and no recoil to
		// brace against, so they take optics, rails and cells only.
		WeaponId.Photon => S | R | M | A,
		WeaponId.ArcLance => S | R | M | A | K,
		// You do not fit a scope to a minigun.
		WeaponId.Vulcan => R | M | A,
		// A capacitor bank is not a magazine and a bolt is not a bullet: optics
		// and a rail, nothing that would change what it IS.
		WeaponId.Tesla => S | R,
		// A bigger bandolier, and that is all you can do to a grenade.
		WeaponId.Frag => M,
		// A rifle, all six rails.
		WeaponId.Awm => S | G | R | M | A | K,
		_ => S | R | M | A,
	};

	/// <summary>
	/// What a player needs to know about a weapon that breaks a rule, as short
	/// lines joined by newlines (one tooltip row each), or "" for one that does
	/// not. Here rather than in the tooltip so the
	/// figures it quotes come from the spec and the Tune it names, not from a
	/// second copy of them in GDScript.
	/// </summary>
	public static string TraitOf(WeaponId id)
	{
		var w = Get(id);
		if (w.ArcTargets > 0)
			return "kills what it strikes, through any plate\n"
				+ $"arcs to {w.ArcTargets - 1} more within {Tune.ArcReach / Fx.One}px\n"
				+ $"and to YOU within {Tune.ArcPlayerReach / Fx.One}px of any of them";
		if (w.Grenade)
			return string.Create(System.Globalization.CultureInfo.InvariantCulture,
				$"thrown: bounces, {w.BulletTicks * 10 / Fx.TicksPerSecond / 10.0:0.0}s fuse\n")
				+ $"{Tune.FragCount} fragments, and they hit you too\n"
				+ "hold aim to lob it short";
		if (w.WallPierce > 0)
			return $"goes through up to {w.WallPierce} walls\n"
				+ $"losing {Tune.WallPierceLossQ8 * 100 / Fx.One}% of its damage at each";
		return "";
	}

	public static bool HasSlot(WeaponId id, AttachSlot slot)
		=> (SlotMask(id) & (1 << (int)slot)) != 0;

	public static WeaponId Clamp(int raw)
		=> (raw >= 0 && raw < Count) ? (WeaponId)raw : WeaponId.Glock;

	public static string NameOf(WeaponId id) => id switch
	{
		WeaponId.Tesla => "Tesla",
		WeaponId.Frag => "Frag",
		WeaponId.Awm => "AWM",
		WeaponId.Welrod => "Welrod",
		WeaponId.Vss => "VSS",
		WeaponId.Photon => "Photon",
		WeaponId.ArcLance => "Arc Lance",
		WeaponId.Vulcan => "Vulcan",
		WeaponId.Mp7 => "MP7",
		WeaponId.Ak47 => "AK-47",
		WeaponId.Remington => "Remington",
		WeaponId.Saw => "SAW",
		_ => "Glock",
	};

	public static string ClassOf(WeaponId id) => id switch
	{
		WeaponId.Tesla => "lightning gun",
		WeaponId.Frag => "grenades",
		WeaponId.Awm => "sniper rifle",
		WeaponId.Welrod => "silenced pistol",
		WeaponId.Vss => "silenced marksman rifle",
		WeaponId.Photon => "laser carbine",
		WeaponId.ArcLance => "heavy laser",
		WeaponId.Vulcan => "minigun",
		WeaponId.Mp7 => "submachine gun",
		WeaponId.Ak47 => "assault rifle",
		WeaponId.Remington => "shotgun",
		WeaponId.Saw => "machine gun",
		_ => "pistol",
	};
}

/// <summary>
/// Attachment options, per slot. Each slot has its own 0-based id space, and 0
/// is always "nothing fitted" so a zeroed loadout is a bare weapon.
/// </summary>
public static class AttachmentCatalog
{
	public const int SlotCount = 6;

	private static readonly AttachmentSpec None = new AttachmentSpec();

	// ---- sight: tightens the base cone ----
	private static readonly AttachmentSpec[] Sights =
	{
		None,                                                  // iron sights
		new AttachmentSpec(spreadBase: -30, aimSpread: -8),    // red dot
		new AttachmentSpec(spreadBase: -50, aimSpread: -14),   // holographic
		// A scope is glass on a mount: tighter than anything, and the one sight
		// heavy enough to cost you the swing.
		new AttachmentSpec(spreadBase: -90, speedDelta: -4 * Fx.One,
			aimSpread: -28, aimMove: -20, turnRate: -12),      // scope
	};

	// ---- grip: controls how fast accuracy decays under sustained fire, and
	//      how much of the weapon's weight you can actually steer ----
	private static readonly AttachmentSpec[] Grips =
	{
		None,
		new AttachmentSpec(spreadPerHeat: -150, spreadPerSway: -80),   // rubber
		new AttachmentSpec(spreadPerHeat: -250, reload: 4,
			spreadPerSway: -140, turnRate: 6),                         // tactical
		new AttachmentSpec(spreadPerHeat: -350, reload: 8,
			spreadPerSway: -220, turnRate: 10),                        // angled
	};

	// ---- rail ----
	private static readonly AttachmentSpec[] Rails =
	{
		None,
		new AttachmentSpec(spreadBase: -60),                   // laser
		// The flashlight is the plan's two-sided trade: it extends what you can
		// see and makes you markedly easier for guards to pick up.
		new AttachmentSpec(visionRadius: 150 * Fx.One, detectionBonus: 38),
		// A foregrip is the handling attachment: it buys back most of what a
		// long weapon loses to being swung.
		new AttachmentSpec(heatPerShot: -18, speedDelta: -2 * Fx.One,
			spreadPerSway: -260, turnRate: 14),                // foregrip
	};

	// ---- magazine: capacity against reload time ----
	private static readonly AttachmentSpec[] Magazines =
	{
		None,
		new AttachmentSpec(magNum: 3, magDen: 2, reload: 12),   // extended
		new AttachmentSpec(magNum: 5, magDen: 2, reload: 30, speedDelta: -4 * Fx.One,
			turnRate: -10),                                     // drum
		new AttachmentSpec(reload: -30),                        // quick-release
	};

	// ---- ammo: the tactical slot ----
	private static readonly AttachmentSpec[] Ammos =
	{
		None,
		// Subsonic is the only way to fire without waking the floor: it drops
		// the report from 400 px to 100 px on a Glock, and costs a round of
		// killing power to do it.
		// The bulletSpeed deltas are three times what they were, because muzzle
		// velocities are: a flat -40000 against 2520 px/s is not a subsonic
		// round, it is a rounding error.
		new AttachmentSpec(damage: -15, gunshotRadius: -300 * Fx.One, bulletSpeed: -120000),
		new AttachmentSpec(damage: 12, bulletSpeed: -45000),    // hollow point
		new AttachmentSpec(damage: 5, armourPierce: 128, gunshotRadius: 50 * Fx.One,
			bulletSpeed: 90000),                                // armour piercing
	};

	// ---- stock ----
	private static readonly AttachmentSpec[] Stocks =
	{
		None,
		new AttachmentSpec(spreadPerHeat: -100, speedDelta: -2 * Fx.One,
			spreadPerSway: -120, turnRate: -4),                 // light
		// A heavy stock is the steadiest thing you can fit and the one that
		// most plainly makes the weapon harder to turn with.
		new AttachmentSpec(spreadPerHeat: -250, speedDelta: -12 * Fx.One,
			aimSpread: -12, spreadPerSway: -300, turnRate: -18), // heavy
	};

	private static AttachmentSpec[] Options(AttachSlot slot) => slot switch
	{
		AttachSlot.Sight => Sights,
		AttachSlot.Grip => Grips,
		AttachSlot.Rail => Rails,
		AttachSlot.Magazine => Magazines,
		AttachSlot.Ammo => Ammos,
		_ => Stocks,
	};

	public static int CountFor(AttachSlot slot) => Options(slot).Length;

	/// <summary>Total: an unknown id is "nothing fitted", never an exception.</summary>
	public static int ClampId(AttachSlot slot, int raw)
		=> (raw >= 0 && raw < CountFor(slot)) ? raw : 0;

	public static AttachmentSpec Get(AttachSlot slot, int id)
		=> Options(slot)[ClampId(slot, id)];

	public static string SlotName(AttachSlot slot) => slot switch
	{
		AttachSlot.Sight => "sight",
		AttachSlot.Grip => "grip",
		AttachSlot.Rail => "rail",
		AttachSlot.Magazine => "magazine",
		AttachSlot.Ammo => "ammo",
		_ => "stock",
	};

	private static readonly string[][] Names =
	{
		new[] { "iron sights", "red dot", "holographic", "scope" },
		new[] { "no grip", "rubber grip", "tactical grip", "angled grip" },
		new[] { "empty rail", "laser", "flashlight", "foregrip" },
		new[] { "standard mag", "extended mag", "drum mag", "quick-release" },
		new[] { "standard ammo", "subsonic", "hollow point", "armour piercing" },
		new[] { "no stock", "light stock", "heavy stock" },
	};

	public static string NameOf(AttachSlot slot, int id)
		=> Names[(int)slot][ClampId(slot, id)];
}

public enum ArmourId
{
	None = 0,
	LightWeave = 1,
	MediumCarrier = 2,
	HeavyPlate = 3,
}

/// <summary>
/// Armour trades survivability against detectability (RPG plan §2). The speed
/// and noise deltas feed systems that already exist, so heavier plate is
/// genuinely a choice rather than a strict upgrade.
/// </summary>
public readonly struct ArmourSpec
{
	public readonly int Armour;
	public readonly int WalkSpeed;
	public readonly int SneakSpeed;
	public readonly int FootstepRadius;

	public ArmourSpec(int armour, int walkSpeed, int sneakSpeed, int footstepRadius)
	{
		Armour = armour;
		WalkSpeed = walkSpeed;
		SneakSpeed = sneakSpeed;
		FootstepRadius = footstepRadius;
	}
}

public static class ArmourCatalog
{
	public const int Count = 4;

	private static readonly ArmourSpec NoneSpec = new ArmourSpec(
		0, Tune.SpeedWalk, Tune.SpeedSneak, Tune.NoiseWalkRadius);

	private static readonly ArmourSpec LightSpec = new ArmourSpec(
		50, Tune.SpeedWalk, Tune.SpeedSneak, Tune.NoiseWalkRadius);

	private static readonly ArmourSpec MediumSpec = new ArmourSpec(
		100, 180 * Fx.One, 92 * Fx.One, 195 * Fx.One);

	private static readonly ArmourSpec HeavySpec = new ArmourSpec(
		150, 160 * Fx.One, 84 * Fx.One, 240 * Fx.One);

	public static ArmourSpec Get(ArmourId id) => id switch
	{
		ArmourId.LightWeave => LightSpec,
		ArmourId.MediumCarrier => MediumSpec,
		ArmourId.HeavyPlate => HeavySpec,
		_ => NoneSpec,
	};

	public static ArmourId Clamp(int raw)
		=> (raw >= 0 && raw < Count) ? (ArmourId)raw : ArmourId.None;

	public static string NameOf(ArmourId id) => id switch
	{
		ArmourId.LightWeave => "light weave",
		ArmourId.MediumCarrier => "medium carrier",
		ArmourId.HeavyPlate => "heavy plate",
		_ => "unarmoured",
	};
}

/// <summary>
/// ONE WEAPON'S attachment rails: the six slots, as option ids.
///
/// A struct rather than six loose ints on the Loadout, because there are now
/// two of them and every Loadout mutator has to carry every field through.
/// Twelve more ints in fourteen constructor calls is a bug waiting to be
/// written; one field each is not.
///
/// default(AttachSet) is an EMPTY set and must stay that way: the default
/// bypasses the constructor, and zero already means "nothing fitted" in every
/// slot. The same care Loadout._secondary1 needs, for the same reason.
/// </summary>
public readonly struct AttachSet
{
	private readonly int _sight, _grip, _rail, _mag, _ammo, _stock;

	public AttachSet(int sight = 0, int grip = 0, int rail = 0, int mag = 0,
		int ammo = 0, int stock = 0)
	{
		_sight = AttachmentCatalog.ClampId(AttachSlot.Sight, sight);
		_grip = AttachmentCatalog.ClampId(AttachSlot.Grip, grip);
		_rail = AttachmentCatalog.ClampId(AttachSlot.Rail, rail);
		_mag = AttachmentCatalog.ClampId(AttachSlot.Magazine, mag);
		_ammo = AttachmentCatalog.ClampId(AttachSlot.Ammo, ammo);
		_stock = AttachmentCatalog.ClampId(AttachSlot.Stock, stock);
	}

	public static AttachSet Empty => default;

	/// <summary>What is fitted, WITHOUT masking by any weapon. A set does not
	/// know which gun it is on; masking is the Loadout's job.</summary>
	public int Raw(AttachSlot slot) => slot switch
	{
		AttachSlot.Sight => _sight,
		AttachSlot.Grip => _grip,
		AttachSlot.Rail => _rail,
		AttachSlot.Magazine => _mag,
		AttachSlot.Ammo => _ammo,
		_ => _stock,
	};

	public AttachSet With(AttachSlot slot, int id) => new AttachSet(
		slot == AttachSlot.Sight ? id : _sight,
		slot == AttachSlot.Grip ? id : _grip,
		slot == AttachSlot.Rail ? id : _rail,
		slot == AttachSlot.Magazine ? id : _mag,
		slot == AttachSlot.Ammo ? id : _ammo,
		slot == AttachSlot.Stock ? id : _stock);
}

/// <summary>
/// What the player brought into the mission (RPG plan §1, §3A). Produced by the
/// campaign layer in game/, consumed here; sim/ never learns that an inventory
/// or a save file exists.
///
/// default(Loadout) is a bare Glock with no armour, so any call site that does
/// not pass one gets a sane, fully-specified weapon.
/// </summary>
public readonly struct Loadout
{
	/// <summary>The primary weapon. Still named Weapon so every existing call
	/// site keeps working; what is actually in the player's hands is Held.</summary>
	public readonly WeaponId Weapon;
	public readonly ArmourId Armour;

	/// <summary>
	/// The two attachment sets: the PRIMARY weapon's and the HOLSTERED one's.
	///
	/// One set per weapon, not one per player. A scope fitted to the rifle is
	/// on the rifle; drawing the pistol draws a pistol with its own rails,
	/// empty or otherwise. The shared set this used to carry made a kit out of
	/// whichever weapon happened to be in hand, which is not what an
	/// attachment is.
	///
	/// They travel as one field each rather than twelve loose ints, because
	/// every mutator below has to carry every field through and twelve more
	/// would make that impossible to get right by hand. See Copy.
	/// </summary>
	private readonly AttachSet _att, _att2;

	/// <summary>
	/// Secondary weapon as WeaponId PLUS ONE, so that 0 means an empty holster.
	///
	/// The offset is load-bearing. default(Loadout) bypasses this constructor
	/// and zeroes every field, so a sentinel of -1 would have come out as 0 and
	/// read as "holstering a Glock" -- which silently broke the invariant that
	/// default(Loadout) and Loadout.Default behave identically, and made a
	/// replay diverge from the run that recorded it. Zero has to mean empty.
	/// </summary>
	private readonly int _secondary1;

	/// <summary>The holster in the external form: a WeaponId, or -1 for empty.</summary>
	private int SecondaryRaw => _secondary1 > 0 ? _secondary1 - 1 : -1;

	/// <summary>0 primary, 1 secondary. Which hand is full.</summary>
	private readonly int _active;

	/// <summary>
	/// The worn backpack, as a GearCatalog item id, or 0 for none. This is the
	/// one apparel slot with a sim effect: it sizes the mission pack, so it has
	/// to reach the sim and be hashed. Helmet, footware, shirt and arms are
	/// carried by the campaign layer and never arrive here, because sim/ has no
	/// reader for them yet.
	/// </summary>
	public readonly int Backpack;

	/// <summary>
	/// The four APPAREL slots, as GearCatalog item ids, or 0 for bare.
	///
	/// They change NO stat — the spec has no reader for a helmet — but they are
	/// here, and hashed, because they have to be WEARABLE IN THE FIELD. An item
	/// moving out of the pack and onto the player is sim state whether or not it
	/// does anything, and gear that could be looted with nowhere to put it was
	/// the bug that brought them here.
	///
	/// Inert is not the same as absent: do not give them effects until there is
	/// a spec that reads them (CLAUDE.md), but do keep them recorded.
	/// </summary>
	public readonly int Helmet, Footware, Shirt, Arms;

	/// <summary>
	/// The fifth apparel slot, GearSlot.Legs, appended for the Kit revamp. It
	/// had no field here while trousers were already in the catalogue and the
	/// field view offered them the legs box, so equipping a pair took it out of
	/// the pack and put it nowhere. Hashed and written only when WORN, so every
	/// state and every kit text from before it is unchanged.
	/// </summary>
	public readonly int Legs;

	/// <summary>
	/// What the player walks in CARRYING, as GearCatalog item ids, auto-placed
	/// into the mission pack at Restart in this order.
	///
	/// Part of the loadout because that is exactly what it is: the kit you
	/// deploy with. It rides in the replay through ToText for the same reason
	/// the weapon does -- a run that began with a medkit in the bag and one
	/// that did not are different runs, and the pack feeds the state hash.
	///
	/// Null and empty mean the same thing, so default(Loadout) carries nothing.
	/// Capped at MaxCarried: a hand-edited or corrupt save must not be able to
	/// ask the sim to place ten thousand items, which is the lesson
	/// Level.MaxGuards already taught.
	/// </summary>
	private readonly int[]? _carried;

	public const int MaxCarried = 64;

	public Loadout(WeaponId weapon, ArmourId armour = ArmourId.None,
		int sight = 0, int grip = 0, int rail = 0, int mag = 0, int ammo = 0, int stock = 0,
		int secondary = -1, int active = 0, int backpack = 0,
		int helmet = 0, int footware = 0, int shirt = 0, int arms = 0, int legs = 0)
		: this(weapon, armour, new AttachSet(sight, grip, rail, mag, ammo, stock),
			AttachSet.Empty, secondary < 0 ? 0 : (int)WeaponCatalog.Clamp(secondary) + 1,
			active, backpack, helmet, footware, shirt, arms, legs, null)
	{
	}

	/// <summary>
	/// The real constructor: every field, in field form. Private because the
	/// holster arrives PLUS ONE here and -1-for-empty there, and because
	/// nothing outside should be building attachment sets positionally.
	/// </summary>
	private Loadout(WeaponId weapon, ArmourId armour, AttachSet att, AttachSet att2,
		int secondary1, int active, int backpack,
		int helmet, int footware, int shirt, int arms, int legs, int[]? carried)
	{
		Weapon = weapon;
		Armour = armour;
		_att = att;
		_att2 = att2;
		_secondary1 = secondary1 < 0 ? 0 : secondary1;
		_active = active == 1 ? 1 : 0;
		Backpack = backpack;
		Helmet = helmet;
		Footware = footware;
		Shirt = shirt;
		Arms = arms;
		Legs = legs;
		_carried = carried;
	}

	/// <summary>
	/// Everything, with the named fields replaced.
	///
	/// EVERY mutator goes through here. It used to be fourteen hand-written
	/// constructor calls listing every field each time, which is how
	/// SetAttachment once took a helmet back off: a field added without
	/// visiting all of them is silently dropped. Now a new field is one line
	/// in this function, and Exhaustive.LoadoutMutators still checks that
	/// every With* preserves everything it does not name.
	/// </summary>
	private Loadout Copy(WeaponId? weapon = null, ArmourId? armour = null,
		AttachSet? att = null, AttachSet? att2 = null, int? secondary1 = null,
		int? active = null, int? backpack = null, int? helmet = null,
		int? footware = null, int? shirt = null, int? arms = null, int? legs = null,
		int[]? carried = null)
		=> new Loadout(weapon ?? Weapon, armour ?? Armour, att ?? _att, att2 ?? _att2,
			secondary1 ?? _secondary1, active ?? _active, backpack ?? Backpack,
			helmet ?? Helmet, footware ?? Footware, shirt ?? Shirt, arms ?? Arms,
			legs ?? Legs, carried ?? _carried);

	public static Loadout Default => new Loadout(WeaponId.Glock);

	public bool HasSecondary => _secondary1 > 0;

	/// <summary>The holstered weapon, or the primary when the holster is empty.</summary>
	public WeaponId Secondary => _secondary1 > 0 ? (WeaponId)(_secondary1 - 1) : Weapon;

	/// <summary>
	/// Which weapon is in hand. Total: drawing a secondary that is not there
	/// resolves back to the primary rather than to nothing, because the sim
	/// models no empty-handed state.
	/// </summary>
	public int ActiveIndex => (_active == 1 && _secondary1 > 0) ? 1 : 0;

	public WeaponId Held => ActiveIndex == 1 ? (WeaponId)(_secondary1 - 1) : Weapon;

	/// <summary>The weapon in a HAND: 0 primary, 1 holster. Anything else is
	/// the primary, because there is no third hand.</summary>
	public WeaponId WeaponAt(int index) => index == 1 ? Secondary : Weapon;

	/// <summary>That hand's attachment set, unmasked.</summary>
	public AttachSet SetAt(int index) => index == 1 ? _att2 : _att;

	public Loadout WithSecondary(int rawId)
		=> Copy(secondary1: rawId < 0 ? 0 : (int)WeaponCatalog.Clamp(rawId) + 1,
			// A weapon leaving the holster takes its rails with it. Leaving
			// them for the next gun to inherit is the shared-set bug in
			// miniature: you would holster a pistol and find it scoped.
			att2: rawId < 0 ? AttachSet.Empty : (AttachSet?)null);

	/// <summary>Draws the other weapon. A no-op when the holster is empty.</summary>
	public Loadout WithActive(int index) => Copy(active: index);

	public Loadout Swapped() => WithActive(ActiveIndex == 1 ? 0 : 1);

	/// <summary>
	/// Swap the PRIMARY weapon, leaving the holster and everything else alone.
	///
	/// The rails stay with the HAND, not with the gun that left it. Clearing
	/// them would DESTROY the attachments: the pack holds a weapon as one item
	/// id with nothing fitted to it, so a scope taken off here would have
	/// nowhere to go. Keeping them creates nothing either -- one scope fitted
	/// before, one scope fitted after -- and what the two sets are actually
	/// for is that the rifle and the pistol no longer share one.
	/// </summary>
	public Loadout WithWeapon(WeaponId w) => Copy(weapon: w);

	/// <summary>
	/// Swap whichever weapon is IN HAND. Which of the two that is depends on
	/// ActiveIndex, so this is the one an equip should use: the player drags a
	/// rifle onto the slot they are holding and gets the rifle.
	/// </summary>
	public Loadout WithHeld(WeaponId w) =>
		ActiveIndex == 1 ? WithSecondary((int)w) : WithWeapon(w);

	public Loadout WithArmour(ArmourId a) => Copy(armour: a);

	/// <summary>By raw ordinal, clamped. What the bridge's setters take, since
	/// GDScript has no enums to hand across.</summary>
	public Loadout WithWeapon(int rawId) => WithWeapon(WeaponCatalog.Clamp(rawId));

	public Loadout WithArmour(int rawId) => WithArmour(ArmourCatalog.Clamp(rawId));

	public Loadout WithBackpack(int itemId) => Copy(backpack: itemId);

	/// <summary>What is worn in an apparel slot, as an item id, or 0.</summary>
	public int ApparelIn(GearSlot slot) => slot switch
	{
		GearSlot.Helmet => Helmet,
		GearSlot.Footware => Footware,
		GearSlot.Chest => Shirt,
		GearSlot.Arms => Arms,
		GearSlot.Legs => Legs,
		_ => 0,
	};

	/// <summary>Wear an item in an apparel slot. Any other slot is returned
	/// unchanged rather than guessed at.</summary>
	public Loadout WithApparel(GearSlot slot, int itemId) => slot switch
	{
		GearSlot.Helmet => Copy(helmet: itemId),
		GearSlot.Footware => Copy(footware: itemId),
		GearSlot.Chest => Copy(shirt: itemId),
		GearSlot.Arms => Copy(arms: itemId),
		GearSlot.Legs => Copy(legs: itemId),
		_ => this,
	};

	/// <summary>The mission pack this backpack provides. Zero by zero with no
	/// pack worn, which means nothing can be looted -- carry a bag.</summary>
	public int PackW => GearCatalog.Get(Backpack).PackW;
	public int PackH => GearCatalog.Get(Backpack).PackH;

	// ------------------------------------------------------ what is carried

	public int CarriedCount => _carried?.Length ?? 0;

	/// <summary>One carried item id, or 0 past the end. Total, like every other
	/// reader here.</summary>
	public int CarriedAt(int i)
		=> (_carried != null && i >= 0 && i < _carried.Length) ? _carried[i] : 0;

	/// <summary>
	/// Replace what the player walks in carrying. Unknown ids and anything past
	/// MaxCarried are dropped rather than throwing, because this arrives from a
	/// save file the player can edit.
	/// </summary>
	public Loadout WithCarried(int[]? items)
	{
		if (items == null || items.Length == 0) return Copy(carried: System.Array.Empty<int>());
		var keep = new List<int>(items.Length < MaxCarried ? items.Length : MaxCarried);
		for (int i = 0; i < items.Length && keep.Count < MaxCarried; i++)
			if (GearCatalog.Exists(items[i])) keep.Add(items[i]);
		return Copy(carried: keep.ToArray());
	}

	// ------------------------------------------------------- attachments

	/// <summary>
	/// The id fitted in a slot on the weapon IN HAND, or 0 when that weapon has
	/// no such slot. Asking through here rather than reading a field means a
	/// stock left in a save file for a weapon that cannot take one never takes
	/// effect.
	/// </summary>
	public int Attachment(AttachSlot slot) => AttachmentAt(ActiveIndex, slot);

	/// <summary>The id fitted in a slot on the weapon in a given HAND, masked
	/// by that weapon's own rails.</summary>
	public int AttachmentAt(int index, AttachSlot slot)
		=> WeaponCatalog.HasSlot(WeaponAt(index), slot) ? SetAt(index).Raw(slot) : 0;

	/// <summary>Fit something to the weapon IN HAND. What the field does: you
	/// fit a scope to the gun you are holding.</summary>
	public Loadout WithAttachment(AttachSlot slot, int id)
		=> WithAttachmentAt(ActiveIndex, slot, id);

	/// <summary>Fit something to the weapon in a named hand. What the stash
	/// does: both guns are in front of you there.</summary>
	public Loadout WithAttachmentAt(int index, AttachSlot slot, int id)
		=> index == 1
			? Copy(att2: _att2.With(slot, id))
			: Copy(att: _att.With(slot, id));

	/// <summary>
	/// The effective weapon: base spec with every fitted attachment folded in,
	/// in slot order. Order is fixed so the result is reproducible; clamping
	/// happens after each fold so no combination can drive a stat negative.
	/// </summary>
	public WeaponSpec Spec => SpecAt(ActiveIndex);

	/// <summary>
	/// The weapon NOT in hand. Equals Held when the holster is empty, so callers
	/// must check HasSecondary first — there is no third weapon to describe.
	///
	/// NOT the same as Secondary: when the secondary has been DRAWN, the stowed
	/// weapon is the primary. Conflating the two is what made the stowed
	/// magazine come from the gun the player was actually holding.
	/// </summary>
	public WeaponId Stowed => ActiveIndex == 1 ? Weapon : Secondary;

	/// <summary>The stowed weapon's spec, with the STOWED weapon's OWN rails.
	/// Replaces SpecFor(Stowed), which could only ever apply one set.</summary>
	public WeaponSpec StowedSpec => SpecAt(1 - ActiveIndex);

	/// <summary>A hand's weapon with that hand's attachments, masked by the
	/// weapon's own rails.</summary>
	public WeaponSpec SpecAt(int index)
		=> SpecWith(WeaponAt(index), SetAt(index));

	/// <summary>
	/// A given weapon's spec with THE PRIMARY'S attachments applied, masked by
	/// that weapon's own slots.
	///
	/// "What would this gun do with my kit on it" -- which is a question about
	/// a weapon that may be in neither hand, so it cannot take a set from one.
	/// SpecAt is what the sim fires with.
	/// </summary>
	public WeaponSpec SpecFor(WeaponId w) => SpecWith(w, _att);

	private static WeaponSpec SpecWith(WeaponId w, AttachSet att)
	{
		var spec = WeaponCatalog.Get(w);
		for (int i = 0; i < AttachmentCatalog.SlotCount; i++)
		{
			var slot = (AttachSlot)i;
			if (!WeaponCatalog.HasSlot(w, slot)) continue;
			spec = spec.With(AttachmentCatalog.Get(slot, att.Raw(slot)));
		}
		return spec;
	}

	public ArmourSpec ArmourSpec => ArmourCatalog.Get(Armour);

	/// <summary>Extra reach on the player's visibility polygon, from the rail.</summary>
	public int VisionRadiusBonus
	{
		get
		{
			int total = 0;
			for (int i = 0; i < AttachmentCatalog.SlotCount; i++)
				total += AttachmentCatalog.Get((AttachSlot)i,
					Attachment((AttachSlot)i)).VisionRadius;
			return total;
		}
	}

	/// <summary>How much more readily guards pick the player up, Q8 where 256 is
	/// unchanged. The flashlight's cost.</summary>
	public int DetectionMul
	{
		get
		{
			int total = Fx.One;
			for (int i = 0; i < AttachmentCatalog.SlotCount; i++)
				total += AttachmentCatalog.Get((AttachSlot)i,
					Attachment((AttachSlot)i)).DetectionBonus;
			return total;
		}
	}

	/// <summary>Walk speed after armour and the weapon's own handling penalty.</summary>
	public int WalkSpeed => Max(ArmourSpec.WalkSpeed + Spec.SpeedDelta, 40 * Fx.One);
	public int SneakSpeed => Max(ArmourSpec.SneakSpeed + Spec.SpeedDelta, 20 * Fx.One);

	private static int Max(int a, int b) => a > b ? a : b;

	public void HashInto(ref Hash64 h)
	{
		h.Add((int)Weapon);
		h.Add((int)Armour);
		h.Add(_secondary1);
		h.Add(ActiveIndex);
		h.Add(Backpack);
		// Inert, but HASHED: they can be put on mid-mission, so a replay that
		// did not carry them would diverge from the run that recorded it.
		h.Add(Helmet); h.Add(Footware); h.Add(Shirt); h.Add(Arms);
		// Only when worn, the rule the panels and the `u` token follow: every
		// state from before the slot existed hashes exactly as it did.
		if (Legs != 0) h.Add(Legs);
		// MASKED, and per weapon. A stock sitting in a save for a gun that
		// cannot take one changes nothing, so it must not change the hash
		// either -- which is what masking bought before there were two sets.
		for (int i = 0; i < AttachmentCatalog.SlotCount; i++)
			h.Add(AttachmentAt(0, (AttachSlot)i));
		for (int i = 0; i < AttachmentCatalog.SlotCount; i++)
			h.Add(HasSecondary ? AttachmentAt(1, (AttachSlot)i) : 0);
		// What is walked in with lands in the pack, which is hashed state.
		h.Add(CarriedCount);
		for (int i = 0; i < CarriedCount; i++) h.Add(CarriedAt(i));
	}

	public string ToText()
	{
		var sb = new System.Text.StringBuilder();
		// Invariant: secondary is -1 for an empty holster (see Invariant).
		var inv = System.Globalization.CultureInfo.InvariantCulture;
		sb.Append(inv, $"weapon={(int)Weapon} armour={(int)Armour} ");
		sb.Append(inv, $"sight={_att.Raw(AttachSlot.Sight)} grip={_att.Raw(AttachSlot.Grip)} ");
		sb.Append(inv, $"rail={_att.Raw(AttachSlot.Rail)} mag={_att.Raw(AttachSlot.Magazine)} ");
		sb.Append(inv, $"ammo={_att.Raw(AttachSlot.Ammo)} stock={_att.Raw(AttachSlot.Stock)} ");
		sb.Append(inv, $"secondary={SecondaryRaw} active={_active} backpack={Backpack} ");
		// Appended. A replay recorded before apparel existed simply has none of
		// these keys and parses them as 0, which is "bare" -- the same thing
		// that run actually was.
		sb.Append(inv, $"helmet={Helmet} footware={Footware} shirt={Shirt} arms={Arms}");
		// Appended likewise: the HOLSTERED weapon's own rails, and what is
		// carried. A kit written before either existed reads as an empty
		// holster set and an empty pack, which is what those runs were.
		sb.Append(inv, $" sight2={_att2.Raw(AttachSlot.Sight)} grip2={_att2.Raw(AttachSlot.Grip)}");
		sb.Append(inv, $" rail2={_att2.Raw(AttachSlot.Rail)} mag2={_att2.Raw(AttachSlot.Magazine)}");
		sb.Append(inv, $" ammo2={_att2.Raw(AttachSlot.Ammo)} stock2={_att2.Raw(AttachSlot.Stock)}");
		// One key PER ITEM rather than a list: FromText splits on commas as
		// well as spaces, so a comma-separated value would come apart in the
		// parser. Repeating the key cannot.
		for (int i = 0; i < CarriedCount; i++) sb.Append(inv, $" carry={CarriedAt(i)}");
		// Appended, and only when worn, so every kit text written before the
		// slot existed round-trips byte for byte.
		if (Legs != 0) sb.Append(inv, $" legs={Legs}");
		return sb.ToString();
	}

	/// <summary>Total parser: anything unrecognised falls back to the default.</summary>
	public static Loadout FromText(string text)
	{
		if (string.IsNullOrEmpty(text)) return Default;

		int weapon = 0, armour = 0, sight = 0, grip = 0, rail = 0, mag = 0, ammo = 0, stock = 0;
		int secondary = -1, active = 0, backpack = 0;
		int helmet = 0, footware = 0, shirt = 0, arms = 0, legs = 0;
		int sight2 = 0, grip2 = 0, rail2 = 0, mag2 = 0, ammo2 = 0, stock2 = 0;
		var carried = new List<int>();

		foreach (string part in text.Split(' ', ','))
		{
			string p = part.Trim();
			if (p.Length == 0) continue;
			int eq = p.IndexOf('=');
			if (eq <= 0 || eq == p.Length - 1) continue;
			if (!Invariant.TryInt(p.Substring(eq + 1), out int v)) continue;

			switch (p.Substring(0, eq))
			{
				case "weapon": weapon = v; break;
				case "armour": armour = v; break;
				case "sight": sight = v; break;
				case "grip": grip = v; break;
				case "rail": rail = v; break;
				case "mag": mag = v; break;
				case "ammo": ammo = v; break;
				case "stock": stock = v; break;
				case "secondary": secondary = v; break;
				case "active": active = v; break;
				case "backpack": backpack = v; break;
				case "helmet": helmet = v; break;
				case "footware": footware = v; break;
				case "shirt": shirt = v; break;
				case "arms": arms = v; break;
				case "legs": legs = v; break;
				case "sight2": sight2 = v; break;
				case "grip2": grip2 = v; break;
				case "rail2": rail2 = v; break;
				case "mag2": mag2 = v; break;
				case "ammo2": ammo2 = v; break;
				case "stock2": stock2 = v; break;
				case "carry":
					if (carried.Count < MaxCarried) carried.Add(v);
					break;
			}
		}

		var kit = new Loadout(WeaponCatalog.Clamp(weapon), ArmourCatalog.Clamp(armour),
			sight, grip, rail, mag, ammo, stock, secondary, active, backpack,
			helmet, footware, shirt, arms, legs);
		kit = kit.Copy(att2: new AttachSet(sight2, grip2, rail2, mag2, ammo2, stock2));
		return carried.Count == 0 ? kit : kit.WithCarried(carried.ToArray());
	}
}
