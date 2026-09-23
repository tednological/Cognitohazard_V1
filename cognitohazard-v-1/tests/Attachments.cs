using System;
using System.Collections.Generic;
using System.IO;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// Milestone 9: the attachment system (RPG extension plan §3A).
///
/// The contract is narrow and worth stating: every attachment is an additive
/// integer delta applied in fixed slot order and then clamped. So the tests
/// that matter are (a) each attachment moves the stat it claims and nothing
/// else, (b) slots a weapon does not have can never take effect, and (c) no
/// combination can drive a stat somewhere nonsensical.
/// </summary>
public static class Attachments
{
	private const int GW = Level.GW, GH = Level.GH;

	private static char[] Room()
	{
		var g = new char[GW * GH];
		for (int r = 0; r < GH; r++)
			for (int c = 0; c < GW; c++)
				g[r * GW + c] = (r == 0 || r == GH - 1 || c == 0 || c == GW - 1) ? '#' : '.';
		return g;
	}

	private static string Text(char[] g)
	{
		var sb = new System.Text.StringBuilder();
		sb.Append("name: fixture\ngrid:\n");
		for (int r = 0; r < GH; r++)
		{
			for (int c = 0; c < GW; c++) sb.Append(g[r * GW + c]);
			sb.Append('\n');
		}
		return sb.ToString();
	}

	private static void Put(char[] g, int c, int r, char ch) => g[r * GW + c] = ch;
	private static int CellCentre(int cell) => cell * Level.CellFx + Level.CellFx / 2;

	private static readonly AttachSlot[] AllSlots =
	{
		AttachSlot.Sight, AttachSlot.Grip, AttachSlot.Rail,
		AttachSlot.Magazine, AttachSlot.Ammo, AttachSlot.Stock,
	};

	private static readonly WeaponId[] AllWeapons =
	{
		WeaponId.Glock, WeaponId.Mp7, WeaponId.Ak47, WeaponId.Remington, WeaponId.Saw,
	};

	// ------------------------------------------------------------ catalogue

	private static void Catalogue()
	{
		H.Group("attachment catalogue");

		H.Eq("six slots", AttachmentCatalog.SlotCount, 6);

		foreach (var slot in AllSlots)
		{
			H.Check($"{AttachmentCatalog.SlotName(slot)} has options",
				AttachmentCatalog.CountFor(slot) >= 3, $"{AttachmentCatalog.CountFor(slot)}");

			// Id 0 is always "nothing fitted", so a zeroed loadout is a bare weapon.
			var bare = new Loadout(WeaponId.Ak47);
			var withZero = bare.WithAttachment(slot, 0);
			H.Check($"{AttachmentCatalog.SlotName(slot)} id 0 changes nothing",
				SameSpec(bare.Spec, withZero.Spec));

			// Total, like every other catalogue in sim/.
			H.Eq($"{AttachmentCatalog.SlotName(slot)} clamps a negative id",
				AttachmentCatalog.ClampId(slot, -5), 0);
			H.Eq($"{AttachmentCatalog.SlotName(slot)} clamps an oversized id",
				AttachmentCatalog.ClampId(slot, 999), 0);
			H.Check($"{AttachmentCatalog.SlotName(slot)} names every option",
				AttachmentCatalog.NameOf(slot, 1).Length > 0);
		}
	}

	private static bool SameSpec(in WeaponSpec a, in WeaponSpec b)
		=> a.Damage == b.Damage && a.ArmourPierce == b.ArmourPierce
		&& a.Magazine == b.Magazine && a.FireCooldownTicks == b.FireCooldownTicks
		&& a.ReloadTicks == b.ReloadTicks && a.SpreadBase == b.SpreadBase
		&& a.SpreadPerHeat == b.SpreadPerHeat && a.HeatPerShot == b.HeatPerShot
		&& a.BulletSpeed == b.BulletSpeed && a.BulletTicks == b.BulletTicks
		&& a.GunshotRadius == b.GunshotRadius && a.SpeedDelta == b.SpeedDelta;

	// ---------------------------------------------------------- slot masks

	private static void SlotMasks()
	{
		H.Group("slot masks");

		H.Check("the Glock has no stock", !WeaponCatalog.HasSlot(WeaponId.Glock, AttachSlot.Stock));
		H.Check("the Glock has no grip", !WeaponCatalog.HasSlot(WeaponId.Glock, AttachSlot.Grip));
		H.Check("but it does take a sight", WeaponCatalog.HasSlot(WeaponId.Glock, AttachSlot.Sight));
		H.Check("and ammo", WeaponCatalog.HasSlot(WeaponId.Glock, AttachSlot.Ammo));

		H.Check("the SAW has no grip (integral bipod)",
			!WeaponCatalog.HasSlot(WeaponId.Saw, AttachSlot.Grip));
		H.Check("the AK takes everything",
			WeaponCatalog.HasSlot(WeaponId.Ak47, AttachSlot.Stock)
			&& WeaponCatalog.HasSlot(WeaponId.Ak47, AttachSlot.Grip));

		// Every weapon takes ammo, or the tactical slot would be optional.
		foreach (var id in AllWeapons)
			H.Check($"{WeaponCatalog.NameOf(id)} takes ammo",
				WeaponCatalog.HasSlot(id, AttachSlot.Ammo));

		// A stock left in a save file must not take effect on a Glock.
		var glockWithStock = new Loadout(WeaponId.Glock, ArmourId.None, stock: 2);
		var glockBare = new Loadout(WeaponId.Glock);
		H.Eq("a stock fitted to a Glock reads back as nothing",
			glockWithStock.Attachment(AttachSlot.Stock), 0);
		H.Check("and changes none of its stats",
			SameSpec(glockWithStock.Spec, glockBare.Spec));

		// The same stock on an AK does apply.
		var akWithStock = new Loadout(WeaponId.Ak47, ArmourId.None, stock: 2);
		var akBare = new Loadout(WeaponId.Ak47);
		H.Eq("the same stock reads back on an AK", akWithStock.Attachment(AttachSlot.Stock), 2);
		H.Check("and does change its stats", !SameSpec(akWithStock.Spec, akBare.Spec));
	}

	// ------------------------------------------------- one slot at a time

	private static void EachSlotMovesItsStat()
	{
		H.Group("attachment effects");

		var bare = new Loadout(WeaponId.Ak47);
		var b = bare.Spec;

		// Sight tightens the base cone and nothing else.
		var dot = bare.WithAttachment(AttachSlot.Sight, 1).Spec;
		H.Check("a red dot tightens the cone", dot.SpreadBase < b.SpreadBase);
		H.Eq("and leaves damage alone", dot.Damage, b.Damage);
		H.Eq("and leaves the magazine alone", dot.Magazine, b.Magazine);

		var scope = bare.WithAttachment(AttachSlot.Sight, 3).Spec;
		H.Check("a scope is tighter than a red dot", scope.SpreadBase < dot.SpreadBase);
		H.Check("but costs mobility", scope.SpeedDelta < b.SpeedDelta);

		// Grip controls accuracy decay under fire.
		var grip = bare.WithAttachment(AttachSlot.Grip, 3).Spec;
		H.Check("an angled grip slows accuracy decay", grip.SpreadPerHeat < b.SpreadPerHeat);
		H.Check("but slows the reload", grip.ReloadTicks > b.ReloadTicks);

		// Rail.
		var laser = bare.WithAttachment(AttachSlot.Rail, 1).Spec;
		H.Check("a laser tightens the cone", laser.SpreadBase < b.SpreadBase);

		var foregrip = bare.WithAttachment(AttachSlot.Rail, 3).Spec;
		H.Check("a foregrip reduces heat per shot", foregrip.HeatPerShot < b.HeatPerShot);

		// Magazine scales by ratio, not by a flat count, so it means the same
		// thing on a 17-round Glock and a 200-round belt.
		var ext = bare.WithAttachment(AttachSlot.Magazine, 1).Spec;
		H.Check("an extended mag holds more", ext.Magazine > b.Magazine);
		H.Check("and reloads slower", ext.ReloadTicks > b.ReloadTicks);

		var drum = bare.WithAttachment(AttachSlot.Magazine, 2).Spec;
		H.Check("a drum holds more than extended", drum.Magazine > ext.Magazine);

		var quick = bare.WithAttachment(AttachSlot.Magazine, 3).Spec;
		H.Eq("quick-release keeps capacity", quick.Magazine, b.Magazine);
		H.Check("but reloads faster", quick.ReloadTicks < b.ReloadTicks);

		// The ratio is proportional across very different weapons.
		int glockExt = new Loadout(WeaponId.Glock, ArmourId.None, mag: 1).Spec.Magazine;
		int glockBase = new Loadout(WeaponId.Glock).Spec.Magazine;
		int sawExt = new Loadout(WeaponId.Saw, ArmourId.None, mag: 1).Spec.Magazine;
		int sawBase = new Loadout(WeaponId.Saw).Spec.Magazine;
		H.Eq("extended is +50% on a Glock", glockExt, glockBase * 3 / 2);
		H.Eq("and +50% on a SAW too", sawExt, sawBase * 3 / 2);

		// Stock.
		var heavyStock = bare.WithAttachment(AttachSlot.Stock, 2).Spec;
		H.Check("a heavy stock steadies the weapon",
			heavyStock.SpreadPerHeat < b.SpreadPerHeat);
		H.Check("at a mobility cost", heavyStock.SpeedDelta < b.SpeedDelta);
	}

	// --------------------------------------------------------------- ammo

	private static void Ammo()
	{
		H.Group("ammo types");

		var bare = new Loadout(WeaponId.Glock);
		var b = bare.Spec;

		// Subsonic: the stealth option, and the reason the alarm had to become
		// radius-gated.
		var sub = bare.WithAttachment(AttachSlot.Ammo, 1).Spec;
		H.Check("subsonic is much quieter", sub.GunshotRadius < b.GunshotRadius / 2,
			$"{sub.GunshotRadius / Fx.One}px vs {b.GunshotRadius / Fx.One}px");
		H.Check("and costs damage", sub.Damage < b.Damage);
		H.Check("and slows the round", sub.BulletSpeed < b.BulletSpeed);

		// Hollow point: more damage, no stealth benefit.
		var hp = bare.WithAttachment(AttachSlot.Ammo, 2).Spec;
		H.Check("hollow point hits harder", hp.Damage > b.Damage);
		H.Eq("but is no quieter", hp.GunshotRadius, b.GunshotRadius);
		H.Eq("and does not pierce", hp.ArmourPierce, 0);

		// AP: the armour answer.
		var ap = bare.WithAttachment(AttachSlot.Ammo, 3).Spec;
		H.Check("AP pierces armour", ap.ArmourPierce > 0);
		H.Check("and is louder", ap.GunshotRadius > b.GunshotRadius);

		// Piercing is worth exactly what it claims: against heavy plate, AP
		// takes a round off the kill.
		int Shots(int damage, int pierce, int armour)
		{
			var t = new Actor { Armour = armour };
			int n = 0;
			while (t.Alive && n < 100) { t.TakeDamage(damage, pierce, out _); n++; }
			return n;
		}
		int plain = Shots(b.Damage, b.ArmourPierce, 150);
		int piercing = Shots(ap.Damage, ap.ArmourPierce, 150);
		H.Check("AP kills a heavy target in fewer rounds", piercing < plain,
			$"standard {plain}, AP {piercing}");

		// But piercing is worthless against an unarmoured target beyond its
		// small damage bump, which is the trade.
		H.Eq("pierce does nothing without armour to pierce",
			Shots(b.Damage, 0, 0), Shots(b.Damage, 200, 0));
	}

	// -------------------------------------------------- subsonic in the world

	/// <summary>
	/// The payoff for the whole ammo slot: a subsonic Glock fired near a guard
	/// who is outside its shrunken radius must leave the floor alarm at zero,
	/// where standard ammo would have raised it.
	/// </summary>
	private static void SubsonicIsActuallyQuiet()
	{
		H.Group("subsonic (RPG plan §3A)");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		Put(g, 19, 14, 'a');           // ~300px east of the player at col 4:
		                               // inside a Glock's 400px, outside subsonic's 100px
		string level = Text(g);

		(int Alarm, int Awareness) Fire(int ammoId)
		{
			var w = new SimWorld(Level.FromText(level), 17,
				new Loadout(WeaponId.Glock, ArmourId.None, ammo: ammoId));
			w.Player.X = CellCentre(4);
			w.Player.Y = CellCentre(14);
			w.Guards[0].Facing = 0;
			w.Step(new InputFrame(0, 0, Brad.Half, InputFrame.FFire));
			return (w.Alarm.Level, w.Guards[0].Awareness);
		}

		var probe = new SimWorld(Level.FromText(level), 17);
		int gap = Fx.Dist(probe.Guards[0].X, probe.Guards[0].Y, CellCentre(4), CellCentre(14));
		int standardR = new Loadout(WeaponId.Glock).Spec.GunshotRadius;
		int subR = new Loadout(WeaponId.Glock, ArmourId.None, ammo: 1).Spec.GunshotRadius;
		H.Check("fixture: the guard sits between the two radii",
			gap < standardR && gap > subR,
			$"gap {gap / Fx.One}px, standard {standardR / Fx.One}px, subsonic {subR / Fx.One}px");

		var std = Fire(0);
		var quiet = Fire(1);

		H.Eq("standard ammo raises the alarm", std.Alarm, 2);
		H.Check("and sends the guard looking", std.Awareness >= Tune.GunAwareness - 30,
			$"{std.Awareness / 10.0:F1}");

		H.Eq("subsonic leaves the floor calm", quiet.Alarm, 0);
		H.Eq("and the guard none the wiser", quiet.Awareness, 0);
	}

	// ------------------------------------------------------- flashlight

	private static void Flashlight()
	{
		H.Group("flashlight trade");

		var bare = new Loadout(WeaponId.Ak47);
		var lit = bare.WithAttachment(AttachSlot.Rail, 2);

		H.Eq("no rail means no vision bonus", bare.VisionRadiusBonus, 0);
		H.Check("the flashlight extends vision", lit.VisionRadiusBonus > 0,
			$"{lit.VisionRadiusBonus / Fx.One}px");

		H.Eq("and nothing normally alters detection", bare.DetectionMul, Fx.One);
		H.Check("but a lit torch makes you easier to see", lit.DetectionMul > Fx.One,
			$"{lit.DetectionMul}");

		// The cost is real: a lit player is picked up measurably sooner.
		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		Put(g, 8, 14, 'a');
		string level = Text(g);

		int TicksToNotice(Loadout l)
		{
			var w = new SimWorld(Level.FromText(level), 23, l);
			var guard = w.Guards[0];
			guard.PathX = null; guard.PathY = null;
			guard.State = GuardState.Relaxed; guard.Task = GuardTask.Post;
			guard.Facing = 0;
			w.Player.X = guard.X + 300 * Fx.One;
			w.Player.Y = guard.Y;

			for (int i = 0; i < 60 * 30; i++)
			{
				int kx = w.Player.X, ky = w.Player.Y;
				w.Step(new InputFrame(0, 0, 0, 0));
				w.Player.X = kx; w.Player.Y = ky;
				w.Player.Alive = true;
				if (guard.Awareness >= Tune.AwCurious) return i + 1;
			}
			return -1;
		}

		int dark = TicksToNotice(bare);
		int torch = TicksToNotice(lit);
		H.Check("fixture: an unlit player is eventually noticed", dark > 0, $"{dark}");
		H.Check("a lit player is noticed sooner", torch > 0 && torch < dark,
			$"unlit {dark} ticks, lit {torch} ticks");
	}

	// ------------------------------------------------------ combinations

	private static void Combinations()
	{
		H.Group("attachment stacking");

		// Everything fitted at once must still resolve to something sane. This
		// is the clamp doing its job: no combination may produce a negative
		// cone, a zero magazine or a free reload.
		var loaded = new Loadout(WeaponId.Ak47, ArmourId.HeavyPlate,
			sight: 3, grip: 3, rail: 3, mag: 2, ammo: 3, stock: 2);
		var s = loaded.Spec;

		H.Check("spread stays non-negative", s.SpreadBase >= 0, $"{s.SpreadBase}");
		H.Check("spread-per-heat stays non-negative", s.SpreadPerHeat >= 0);
		H.Check("the magazine stays positive", s.Magazine > 0, $"{s.Magazine}");
		H.Check("reload stays above a floor", s.ReloadTicks >= 12, $"{s.ReloadTicks}");
		H.Check("heat per shot stays above a floor", s.HeatPerShot >= 4);
		H.Check("damage stays above a floor", s.Damage >= 5);
		H.Check("the report stays audible at all", s.GunshotRadius >= 60 * Fx.One);
		H.Check("the carrier can still move", loaded.WalkSpeed > 0, $"{loaded.WalkSpeed / Fx.One}");

		// Resolution is order-fixed, so the same loadout always resolves the same.
		var again = new Loadout(WeaponId.Ak47, ArmourId.HeavyPlate,
			sight: 3, grip: 3, rail: 3, mag: 2, ammo: 3, stock: 2);
		H.Check("resolution is deterministic", SameSpec(s, again.Spec));

		// And a fully-loaded heavy weapon in heavy plate is genuinely slow.
		var nimble = new Loadout(WeaponId.Glock);
		var lumbering = new Loadout(WeaponId.Saw, ArmourId.HeavyPlate, mag: 2, stock: 2);
		H.Check("a SAW in heavy plate is much slower than a bare Glock",
			lumbering.WalkSpeed < nimble.WalkSpeed * 3 / 4,
			$"{lumbering.WalkSpeed / Fx.One} vs {nimble.WalkSpeed / Fx.One} px/s");
		H.Check("but never immobile", lumbering.WalkSpeed >= 40 * Fx.One);
	}

	// -------------------------------------------------------------- text

	private static void TextAndHash()
	{
		H.Group("attachments in text and hash");

		var l = new Loadout(WeaponId.Remington, ArmourId.MediumCarrier,
			sight: 2, grip: 1, rail: 2, mag: 1, ammo: 3, stock: 1);
		string text = l.ToText();
		var back = Loadout.FromText(text);

		H.Eq("weapon round-trips", (int)back.Weapon, (int)l.Weapon);
		H.Eq("armour round-trips", (int)back.Armour, (int)l.Armour);
		foreach (var slot in AllSlots)
			H.Eq($"{AttachmentCatalog.SlotName(slot)} round-trips",
				back.Attachment(slot), l.Attachment(slot));

		// Every slot is hashed, so swapping any one of them is caught at once.
		string level = File.ReadAllText(Path.Combine(LevelsDir(), "substation_4.txt"));
		var baseWorld = new SimWorld(Level.FromText(level), 31, new Loadout(WeaponId.Ak47));
		foreach (var slot in AllSlots)
		{
			if (!WeaponCatalog.HasSlot(WeaponId.Ak47, slot)) continue;
			var swapped = new SimWorld(Level.FromText(level), 31,
				new Loadout(WeaponId.Ak47).WithAttachment(slot, 1));
			H.Check($"changing the {AttachmentCatalog.SlotName(slot)} changes the hash",
				swapped.StateHash() != baseWorld.StateHash());
		}

		// Older replays with only a weapon still parse.
		var legacy = Loadout.FromText("weapon=1");
		H.Eq("a legacy loadout line keeps its weapon", (int)legacy.Weapon, (int)WeaponId.Mp7);
		foreach (var slot in AllSlots)
			H.Eq($"and fits no {AttachmentCatalog.SlotName(slot)}", legacy.Attachment(slot), 0);

		H.Eq("garbage slot values clamp",
			Loadout.FromText("weapon=2 sight=99 ammo=-4").Attachment(AttachSlot.Sight), 0);
	}

	private static string LevelsDir()
	{
		var d = new DirectoryInfo(AppContext.BaseDirectory);
		while (d != null && !Directory.Exists(Path.Combine(d.FullName, "levels"))) d = d.Parent;
		return d == null ? "levels" : Path.Combine(d.FullName, "levels");
	}

	public static void Run()
	{
		Catalogue();
		SlotMasks();
		EachSlotMovesItsStat();
		Ammo();
		SubsonicIsActuallyQuiet();
		Flashlight();
		Combinations();
		TextAndHash();
		SmallerMagInTheField();
	}

	/// <summary>
	/// Regression, found by Fuzz: fitting a SMALLER magazine mid-run left the
	/// bigger one's rounds loaded, so the gun held more than it takes.
	/// </summary>
	private static void SmallerMagInTheField()
	{
		H.Group("attachments / a smaller mag fitted in the field");

		var kit = new Loadout(WeaponId.Ak47).WithAttachment(AttachSlot.Magazine, 2)   // drum
			.WithBackpack(503);
		var w = new SimWorld(Level.FromText(Program.ReadLevel("substation_4.txt")), 3UL, kit);
		int drum = w.Player.Mag;
		H.Check("the drum starts full", drum == w.Loadout.Spec.Magazine && drum > 0, $"{drum}");

		w.Step(new InputFrame(0, 0, 0, 0, 0, InputFrame.TierWalk, 0, 331));   // an extended mag
		int placement = -1;
		for (int i = 0; i < w.Pack.Capacity; i++)
			if (w.Pack.IsLive(i) && w.Pack.ItemOf(i) == 331) placement = i;
		H.Check("the extended mag is in the pack", placement >= 0);

		w.Step(new InputFrame(0, 0, 0, 0, 0, InputFrame.TierWalk, 0, 0,
			InputFrame.PackEquip(placement, (int)GearSlot.Primary)));
		H.Check("it is fitted", w.Loadout.Attachment(AttachSlot.Magazine) == 1);
		H.Check("and the gun holds no more than the extended mag takes",
			w.Player.Mag <= w.Loadout.Spec.Magazine && w.Loadout.Spec.Magazine < drum,
			$"{w.Player.Mag} of {w.Loadout.Spec.Magazine} (drum was {drum})");
	}
}
