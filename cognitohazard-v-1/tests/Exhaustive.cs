using System;
using System.Collections.Generic;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// The whole cross product, rather than the handful of combinations anyone
/// thought to write a test for.
///
/// Every weapon against every attachment set is 39,936 loadouts. Nobody is
/// going to hand-check those, and a single one of them producing a zero
/// magazine or a zero fire cooldown is a divide-by-zero or an infinite rate of
/// fire that would only ever be found by a player. The catalogues are small
/// enough that there is no excuse for sampling them.
///
/// Reported as one assertion per property with the first offending combination
/// named, because thirty thousand PASS lines would bury everything else here.
/// </summary>
public static class Exhaustive
{
	private static readonly Dictionary<string, string> Bad = new();

	private static void Fail(string property, string where)
	{
		if (!Bad.ContainsKey(property)) Bad[property] = where;
	}

	private static void Verdict(string property, int combos)
	{
		H.Check(property + $" ({combos} combinations)", !Bad.ContainsKey(property),
			Bad.TryGetValue(property, out var w) ? w : "");
	}

	private static void Weapons()
	{
		H.Group("exhaustive / weapons and attachments");

		int combos = 0;
		for (int wi = 0; wi < WeaponCatalog.Count; wi++)
		for (int sight = 0; sight < 4; sight++)
		for (int grip = 0; grip < 4; grip++)
		for (int rail = 0; rail < 4; rail++)
		for (int mag = 0; mag < 4; mag++)
		for (int ammo = 0; ammo < 4; ammo++)
		for (int stock = 0; stock < 3; stock++)
		{
			var weapon = (WeaponId)wi;
			var kit = new Loadout(weapon, ArmourId.None, sight, grip, rail,
				mag, ammo, stock);
			var spec = kit.Spec;
			string at = $"{weapon} sight={sight} grip={grip} rail={rail}"
				+ $" mag={mag} ammo={ammo} stock={stock}";
			combos++;

			// Anything that divides, counts down, or sizes an array.
			if (spec.Magazine <= 0)
				Fail("every weapon keeps a magazine of at least one", $"{at}: {spec.Magazine}");
			if (spec.FireCooldownTicks <= 0)
				Fail("no attachment gives an instant rate of fire",
					$"{at}: cooldown {spec.FireCooldownTicks}");
			if (spec.ReloadTicks <= 0)
				Fail("no attachment gives an instant reload", $"{at}: {spec.ReloadTicks}");
			if (spec.DryFireTicks <= 0)
				Fail("the dry-fire delay stays positive", $"{at}: {spec.DryFireTicks}");
			if (spec.Pellets < 1)
				Fail("every shot fires at least one projectile", $"{at}: {spec.Pellets}");
			if (spec.BulletSpeed <= 0)
				Fail("every round moves", $"{at}: speed {spec.BulletSpeed}");
			if (spec.BulletTicks <= 0)
				Fail("every round has a lifetime", $"{at}: {spec.BulletTicks}");
			if (spec.TurnNum <= 0)
				Fail("every weapon can still be turned", $"{at}: turn {spec.TurnNum}");

			// Cones and radii are magnitudes; a negative one would mirror the
			// spread or make a shot audible at a negative distance.
			if (spec.SpreadBase < 0) Fail("spread is never negative", $"{at}: {spec.SpreadBase}");
			if (spec.SpreadPerHeat < 0)
				Fail("sustained-fire spread is never negative", $"{at}: {spec.SpreadPerHeat}");
			if (spec.SpreadPerSway < 0)
				Fail("sway spread is never negative", $"{at}: {spec.SpreadPerSway}");
			if (spec.GunshotRadius < 0)
				Fail("a gunshot is never heard at negative range", $"{at}: {spec.GunshotRadius}");
			if (spec.HeatPerShot < 0) Fail("heat per shot is never negative", $"{at}: {spec.HeatPerShot}");
			if (spec.HeatDecayPerSec < 0)
				Fail("heat decay is never negative", $"{at}: {spec.HeatDecayPerSec}");
			if (spec.Damage < 0) Fail("damage is never negative", $"{at}: {spec.Damage}");
			if (spec.ArmourPierce < 0 || spec.ArmourPierce > 256)
				Fail("armour pierce stays a fraction of 256", $"{at}: {spec.ArmourPierce}");
			if (spec.SpinUpTicks < 0)
				Fail("spin-up is never negative", $"{at}: {spec.SpinUpTicks}");

			// Movement has to stay positive or the player is frozen or walks
			// backwards. This is the floor Loadout.WalkSpeed promises.
			if (kit.WalkSpeed <= 0) Fail("a loadout never freezes the player", $"{at}: walk {kit.WalkSpeed}");
			if (kit.SneakSpeed <= 0) Fail("nor while sneaking", $"{at}: sneak {kit.SneakSpeed}");
			if (kit.SneakSpeed > kit.WalkSpeed)
				Fail("sneaking is never faster than walking",
					$"{at}: sneak {kit.SneakSpeed} > walk {kit.WalkSpeed}");

			// SpecFor is the same function Spec is, for the held weapon. If
			// these ever disagree the stowed magazine is wrong again.
			if (kit.SpecFor(weapon).Magazine != spec.Magazine)
				Fail("SpecFor agrees with Spec for the weapon in hand", at);
		}

		Verdict("every weapon keeps a magazine of at least one", combos);
		Verdict("no attachment gives an instant rate of fire", combos);
		Verdict("no attachment gives an instant reload", combos);
		Verdict("the dry-fire delay stays positive", combos);
		Verdict("every shot fires at least one projectile", combos);
		Verdict("every round moves", combos);
		Verdict("every round has a lifetime", combos);
		Verdict("every weapon can still be turned", combos);
		Verdict("spread is never negative", combos);
		Verdict("sustained-fire spread is never negative", combos);
		Verdict("sway spread is never negative", combos);
		Verdict("a gunshot is never heard at negative range", combos);
		Verdict("heat per shot is never negative", combos);
		Verdict("heat decay is never negative", combos);
		Verdict("damage is never negative", combos);
		Verdict("armour pierce stays a fraction of 256", combos);
		Verdict("spin-up is never negative", combos);
		Verdict("a loadout never freezes the player", combos);
		Verdict("nor while sneaking", combos);
		Verdict("sneaking is never faster than walking", combos);
		Verdict("SpecFor agrees with Spec for the weapon in hand", combos);
	}

	/// <summary>
	/// Every With* preserves every OTHER field, and the text format carries all
	/// of them.
	///
	/// This is the general form of a bug that has now been shipped twice. A
	/// Loadout is a readonly struct, so every mutator rebuilds the whole thing
	/// through the constructor — and a field added without visiting all NINE of
	/// them is silently dropped by the ones that were missed. It cost apparel:
	/// SetAttachment ran after SetApparel and took the helmet straight back off.
	///
	/// Comparing field-by-field rather than by hash, so a failure names the
	/// field that was lost instead of only saying two numbers differ.
	/// </summary>
	private static void LoadoutMutators()
	{
		H.Group("exhaustive / loadout mutators");

		// Every field distinct and non-default, so a dropped one shows up as a
		// change rather than coinciding with the value it should have kept.
		//
		// BOTH hands' rails, and something carried: there are two attachment
		// sets now, and a mutator that preserved the primary's and dropped the
		// holster's would otherwise sail through this.
		var full = new Loadout(WeaponId.Ak47, ArmourId.MediumCarrier,
			sight: 2, grip: 3, rail: 1, mag: 2, ammo: 3, stock: 1,
			secondary: (int)WeaponId.Mp7, active: 1, backpack: 503,
			helmet: 601, footware: 602, shirt: 702, arms: 701)
			.WithAttachmentAt(1, AttachSlot.Sight, 1)
			.WithAttachmentAt(1, AttachSlot.Grip, 2)
			.WithAttachmentAt(1, AttachSlot.Rail, 3)
			.WithAttachmentAt(1, AttachSlot.Magazine, 1)
			.WithAttachmentAt(1, AttachSlot.Ammo, 2)
			.WithAttachmentAt(1, AttachSlot.Stock, 1)
			.WithCarried(new[] { 101, 203, 301 });

		var mutators = new List<(string Name, Func<Loadout, Loadout> Apply, string Changes)>
		{
			("WithWeapon(WeaponId)", l => l.WithWeapon(WeaponId.Vss), "weapon"),
			("WithWeapon(int)", l => l.WithWeapon((int)WeaponId.Vss), "weapon"),
			("WithArmour(ArmourId)", l => l.WithArmour(ArmourId.HeavyPlate), "armour"),
			("WithArmour(int)", l => l.WithArmour((int)ArmourId.HeavyPlate), "armour"),
			("WithSecondary", l => l.WithSecondary((int)WeaponId.Saw), "secondary"),
			("WithActive", l => l.WithActive(0), "active"),
			("Swapped", l => l.Swapped(), "active"),
			("WithBackpack", l => l.WithBackpack(501), "backpack"),
			("WithHeld", l => l.WithHeld(WeaponId.Photon), "secondary"),
			("WithApparel(Helmet)", l => l.WithApparel(GearSlot.Helmet, 0), "helmet"),
			("WithApparel(Footware)", l => l.WithApparel(GearSlot.Footware, 0), "footware"),
			("WithApparel(Chest)", l => l.WithApparel(GearSlot.Chest, 0), "shirt"),
			("WithApparel(Arms)", l => l.WithApparel(GearSlot.Arms, 0), "arms"),
			// WithAttachment fits the weapon IN HAND, and `full` has the
			// holster drawn -- so these land on the SECOND set. That is the
			// whole point of the change: a scope fitted while holding the
			// submachine gun is on the submachine gun.
			("WithAttachment(Sight)", l => l.WithAttachment(AttachSlot.Sight, 3), "sight2"),
			("WithAttachment(Grip)", l => l.WithAttachment(AttachSlot.Grip, 1), "grip2"),
			("WithAttachment(Rail)", l => l.WithAttachment(AttachSlot.Rail, 2), "rail2"),
			("WithAttachment(Magazine)", l => l.WithAttachment(AttachSlot.Magazine, 2), "mag2"),
			("WithAttachment(Ammo)", l => l.WithAttachment(AttachSlot.Ammo, 1), "ammo2"),
			("WithAttachment(Stock)", l => l.WithAttachment(AttachSlot.Stock, 2), "stock2"),
			// And the explicit form, which the stash uses: both guns are in
			// front of you there, so neither hand is privileged.
			("WithAttachmentAt(0, Sight)", l => l.WithAttachmentAt(0, AttachSlot.Sight, 3), "sight"),
			("WithAttachmentAt(0, Stock)", l => l.WithAttachmentAt(0, AttachSlot.Stock, 2), "stock"),
			("WithAttachmentAt(1, Sight)", l => l.WithAttachmentAt(1, AttachSlot.Sight, 3), "sight2"),
			("WithAttachmentAt(1, Stock)", l => l.WithAttachmentAt(1, AttachSlot.Stock, 2), "stock2"),
			("WithCarried", l => l.WithCarried(new[] { 102, 501 }), "carry"),
		};

		foreach (var m in mutators)
		{
			var after = m.Apply(full);
			foreach (var field in Fields(full).Keys)
			{
				if (field == m.Changes) continue;
				if (Fields(full)[field] != Fields(after)[field])
					Fail("every mutator preserves every field it does not name",
						$"{m.Name} also changed {field}: "
						+ $"{Fields(full)[field]} -> {Fields(after)[field]}");
			}
		}

		// The text format has to carry all of it, or a replay reconstructs a
		// different kit than the one that was recorded.
		var back = Loadout.FromText(full.ToText());
		foreach (var field in Fields(full).Keys)
			if (Fields(full)[field] != Fields(back)[field])
				Fail("the loadout text format carries every field",
					$"{field}: {Fields(full)[field]} -> {Fields(back)[field]}");

		// And a kit written before apparel existed still reads as bare rather
		// than as garbage.
		var old = Loadout.FromText("weapon=2 armour=1 sight=0 grip=0 rail=0 mag=0 "
			+ "ammo=0 stock=0 secondary=-1 active=0 backpack=502");
		if (old.Helmet != 0 || old.Footware != 0 || old.Shirt != 0 || old.Arms != 0)
			Fail("a kit recorded before apparel reads as bare",
				$"helmet {old.Helmet} footware {old.Footware}");
		if ((int)old.Weapon != 2 || old.Backpack != 502)
			Fail("and still reads everything it did carry",
				$"weapon {(int)old.Weapon} backpack {old.Backpack}");

		Verdict("every mutator preserves every field it does not name", mutators.Count);
		Verdict("the loadout text format carries every field", 1);
		Verdict("a kit recorded before apparel reads as bare", 1);
		Verdict("and still reads everything it did carry", 1);
	}

	/// <summary>
	/// Every STORED field of a Loadout, by name, read out of ToText.
	///
	/// Parsed from the text rather than read through the properties, because
	/// Attachment() masks by the weapon in HAND: changing the secondary changes
	/// what that accessor reports while the stored value is untouched, which
	/// would report a preserved field as a lost one. ToText emits the raw
	/// fields, which is what "preserved" has to mean here.
	///
	/// It also means a field added to Loadout but not to ToText is caught by
	/// the round-trip assertion beside this one rather than silently skipped.
	/// </summary>
	private static Dictionary<string, int> Fields(in Loadout l)
	{
		var map = new Dictionary<string, int>();
		foreach (var part in l.ToText().Split(' '))
		{
			int eq = part.IndexOf('=');
			if (eq <= 0) continue;
			if (!int.TryParse(part.Substring(eq + 1), out int v)) continue;
			string key = part.Substring(0, eq);
			// `carry` repeats, one key per item. Folded in ORDER rather than
			// overwritten, or a kit that lost an item -- or reordered one, and
			// the pack is packed in this order -- would read as unchanged.
			map[key] = key == "carry" && map.TryGetValue(key, out int prior)
				? prior * 31 + v
				: v;
		}
		return map;
	}

	private static void Gear()
	{
		H.Group("exhaustive / the gear catalogue");
		int n = GearCatalog.Count;

		for (int i = 0; i < n; i++)
		{
			var g = GearCatalog.At(i);
			string at = $"{g.Id} \"{g.Name}\"";

			if (string.IsNullOrWhiteSpace(g.Name)) Fail("every item is named", at);
			if (g.W <= 0 || g.H <= 0) Fail("every item has a real footprint", $"{at}: {g.W}x{g.H}");
			if (g.Price < 0) Fail("no item has a negative price", $"{at}: {g.Price}");
			if (GearCatalog.IndexOf(g.Id) != i) Fail("every id resolves to its own row", at);
			if (!GearCatalog.Exists(g.Id)) Fail("every listed item exists", at);

			// Ids must be unique, or IndexOf silently resolves to the first.
			for (int j = i + 1; j < n; j++)
				if (GearCatalog.At(j).Id == g.Id)
					Fail("no two items share an id", $"{at} and index {j}");

			// A weapon's SimA must name a real weapon, and the reverse lookup
			// must come back to this very item -- that lookup is what StepEquip
			// uses to put the displaced weapon into the pack.
			if (g.Kind == GearKind.Weapon)
			{
				if (g.SimA < 0 || g.SimA >= WeaponCatalog.Count)
					Fail("every weapon item names a real weapon", $"{at}: SimA {g.SimA}");
				else if (GearCatalog.WeaponItemId(g.SimA) != g.Id)
					Fail("every weapon round-trips through WeaponItemId",
						$"{at}: came back as {GearCatalog.WeaponItemId(g.SimA)}");
			}
			if (g.Kind == GearKind.Armour)
			{
				if (g.SimA <= 0 || g.SimA > 3)
					Fail("every armour item names a real vest", $"{at}: SimA {g.SimA}");
				else if (GearCatalog.ArmourItemId(g.SimA) != g.Id)
					Fail("every vest round-trips through ArmourItemId",
						$"{at}: came back as {GearCatalog.ArmourItemId(g.SimA)}");
			}
			if (g.Kind == GearKind.Pack && (g.PackW <= 0 || g.PackH <= 0))
				Fail("every backpack provides a grid", $"{at}: {g.PackW}x{g.PackH}");

			// Anything that can be looted has to FIT the biggest pack in the
			// game, or it is an item that can exist and never be carried.
			int biggest = 0, bw = 0, bh = 0;
			for (int k = 0; k < n; k++)
			{
				var p = GearCatalog.At(k);
				if (p.Kind == GearKind.Pack && p.PackW * p.PackH > biggest)
				{ biggest = p.PackW * p.PackH; bw = p.PackW; bh = p.PackH; }
			}
			bool fits = (g.W <= bw && g.H <= bh) || (g.H <= bw && g.W <= bh);
			if (!fits)
				Fail("every item fits the largest backpack",
					$"{at}: {g.W}x{g.H} into {bw}x{bh}");
		}

		Verdict("every item is named", n);
		Verdict("every item has a real footprint", n);
		Verdict("no item has a negative price", n);
		Verdict("every id resolves to its own row", n);
		Verdict("every listed item exists", n);
		Verdict("no two items share an id", n);
		Verdict("every weapon item names a real weapon", n);
		Verdict("every weapon round-trips through WeaponItemId", n);
		Verdict("every armour item names a real vest", n);
		Verdict("every vest round-trips through ArmourItemId", n);
		Verdict("every backpack provides a grid", n);
		Verdict("every item fits the largest backpack", n);
	}

	/// <summary>
	/// Every item into every slot, THROUGH THE SIM. CanEquipMidRun is a second
	/// statement of StepEquip's rule, and a screen that asks one while the tick
	/// obeys the other is a screen that lies. The only way to know they agree is
	/// to run both.
	/// </summary>
	private static void EquipAgreement()
	{
		H.Group("exhaustive / equip rules agree");

		string levelText = System.IO.File.ReadAllText(
			System.IO.Path.Combine(Fuzz.LevelsDir(), "substation_4.txt"));
		int checks = 0;

		for (int i = 0; i < GearCatalog.Count; i++)
		for (int slot = 0; slot < 8; slot++)
		{
			var g = GearCatalog.At(i);
			// The rule, stated INDEPENDENTLY of both the screen and the sim.
			// Three statements of one rule is the point: if any two drift, this
			// fails rather than the player discovering it.
			bool predicted = (g.Kind == GearKind.Weapon
					&& (slot == (int)GearSlot.Primary || slot == (int)GearSlot.Secondary))
				|| (g.Kind == GearKind.Armour && slot == (int)GearSlot.Vest)
				|| (g.Kind == GearKind.Pack && slot == (int)GearSlot.Backpack)
				|| (g.Kind == GearKind.Apparel && slot == (int)g.Slot)
				// An attachment names its own destination, so either weapon
				// slot offers it.
				|| (g.Kind == GearKind.Attachment
					&& (slot == (int)GearSlot.Primary || slot == (int)GearSlot.Secondary));

			// A fresh world with a big pack holding exactly this item.
			var w = new SimWorld(Level.FromText(levelText), 3,
				new Loadout(WeaponId.Glock, ArmourId.None, backpack: 503));
			int pi = w.Pack.AutoPlace(g.Id);
			if (pi == PackGrid.None) continue;

			var beforeHeld = w.Loadout.Held;
			var beforeArmour = w.Loadout.Armour;
			int beforePack = w.Loadout.Backpack;
			// The HOLSTER has to be watched separately. Filling an empty one
			// leaves Held alone -- the primary is still in hand -- so a
			// "did anything change" check built only from Held reports a
			// successful equip as a refusal.
			bool beforeHasSec = w.Loadout.HasSecondary;
			var beforeSec = w.Loadout.Secondary;
			int beforeHelmet = w.Loadout.Helmet, beforeBoots = w.Loadout.Footware;
			int beforeShirt = w.Loadout.Shirt, beforeArms = w.Loadout.Arms;
			// The ATTACHMENTS, read raw. Without these the cross-check was
			// blind to attachment equips entirely and passed for the wrong
			// reason the moment they became possible.
			var beforeFit = Fields(w.Loadout);

			w.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 0,
				InputFrame.PackEquip(pi, slot)));
			checks++;

			bool changed = w.Loadout.Held != beforeHeld
				|| w.Loadout.Armour != beforeArmour
				|| w.Loadout.Backpack != beforePack
				|| w.Loadout.HasSecondary != beforeHasSec
				|| (w.Loadout.HasSecondary && w.Loadout.Secondary != beforeSec)
				|| w.Loadout.Helmet != beforeHelmet
				|| w.Loadout.Footware != beforeBoots
				|| w.Loadout.Shirt != beforeShirt
				|| w.Loadout.Arms != beforeArms
				|| FitChanged(beforeFit, Fields(w.Loadout));

			// A weapon equipped into the slot it is already in changes nothing
			// visible, so "predicted and unchanged" is only a failure when the
			// item would actually have differed.
			// Equipping a weapon into the slot that already holds it is a
			// real acceptance with nothing to observe, so it is excluded
			// rather than counted as a refusal.
			// Fitting the attachment already fitted changes nothing visible.
			bool sameFit = g.Kind == GearKind.Attachment
				&& w.Loadout.Attachment((AttachSlot)g.SimA) == g.SimB;
			bool sameAlready = sameFit
				|| (g.Kind == GearKind.Weapon
					&& (((WeaponId)g.SimA == beforeHeld && slot == (int)GearSlot.Primary)
						|| (beforeHasSec && (WeaponId)g.SimA == beforeSec
							&& slot == (int)GearSlot.Secondary)))
				|| (g.Kind == GearKind.Pack && g.Id == beforePack);
			bool wouldDiffer = !sameAlready;

			if (!predicted && changed)
				Fail("the sim changes nothing the screen would refuse",
					$"{g.Name} into slot {slot}");
			if (predicted && wouldDiffer && !changed)
				Fail("the sim accepts everything the screen would offer",
					$"{g.Name} into slot {slot}");
		}

		Verdict("the sim changes nothing the screen would refuse", checks);
		Verdict("the sim accepts everything the screen would offer", checks);
	}

	/// <summary>Whether any attachment field differs. Compared through the text
	/// form for the same reason Fields reads it: Attachment() masks by the
	/// weapon in hand.</summary>
	private static bool FitChanged(Dictionary<string, int> a, Dictionary<string, int> b)
	{
		foreach (var key in new[] { "sight", "grip", "rail", "mag", "ammo", "stock" })
			if (a.TryGetValue(key, out int x) && b.TryGetValue(key, out int y) && x != y)
				return true;
		return false;
	}

	private static void Tiers()
	{
		H.Group("exhaustive / movement tiers");
		for (int t = 0; t < InputFrame.TierCount; t++)
		{
			string at = $"tier {t}";
			if (Tune.TierSpeedQ8(t) <= 0) Fail("every tier moves", at);
			if (Tune.TierDetectQ8(t) <= 0) Fail("every tier is detectable", at);
			if (Tune.TierTurnQ8(t) <= 0) Fail("every tier can turn", at);
			if (Tune.TierNoise(t) < 0) Fail("no tier has negative noise", at);
			if (t > 0 && Tune.TierSpeedQ8(t) <= Tune.TierSpeedQ8(t - 1))
				Fail("each tier is faster than the last", at);
		}
		Verdict("every tier moves", InputFrame.TierCount);
		Verdict("every tier is detectable", InputFrame.TierCount);
		Verdict("every tier can turn", InputFrame.TierCount);
		Verdict("no tier has negative noise", InputFrame.TierCount);
		Verdict("each tier is faster than the last", InputFrame.TierCount);
	}

	public static void Run()
	{
		Weapons();
		LoadoutMutators();
		Gear();
		EquipAgreement();
		Tiers();
		GuardStates();
	}

	/// <summary>
	/// Every posture x every task x every radio purpose x a squad id that is
	/// none, real or dangling x a group id that is none, real or dangling, on a
	/// patroller and on a sentry, on a calm floor and a compromised one --
	/// including values outside every enum. Guard_AI.md P6: the sim must be
	/// TOTAL over guard state as the parsers are over text. Each combination is
	/// injected, the world stepped, and after ONE tick the guard must be back
	/// inside the rules; after three nothing may have thrown and the rest of
	/// the floor must be untouched by the repair.
	/// </summary>
	private static void GuardStates()
	{
		H.Group("exhaustive / guard postures and tasks");
		var clock = System.Diagnostics.Stopwatch.StartNew();
		var g = new char[48 * 28];
		for (int r = 0; r < 28; r++)
			for (int c = 0; c < 48; c++)
				g[r * 48 + c] = (r == 0 || r == 27 || c == 0 || c == 47) ? '#' : '.';
		g[2 * 48 + 2] = '@';
		g[26 * 48 + 46] = 'X';
		g[14 * 48 + 10] = 'a';
		g[14 * 48 + 30] = 'b';
		g[20 * 48 + 20] = 'c';
		var text = new System.Text.StringBuilder("name: states\ngrid:\n");
		for (int r = 0; r < 28; r++) { text.Append(g, r * 48, 48); text.Append('\n'); }
		text.Append("> a 10,14 16,14\n> b 30,14 36,14\n");
		var level = Level.FromText(text.ToString());

		var states = new List<int>();
		for (int v = (int)GuardState.Relaxed; v <= (int)GuardState.Dead; v++) states.Add(v);
		states.Add(-1); states.Add(99);
		var tasks = new List<int>();
		for (int v = (int)GuardTask.None; v <= (int)GuardTask.WatchExit; v++) tasks.Add(v);
		tasks.Add(-1); tasks.Add(99);
		int[] radios = { 0, 1, 2, 7 };
		int[] squadIds = { -1, 42 };                // no squad exists here: 42 is dangling
		int[] ids = { -1, 0, 42 };                  // group 0 is real once compromised

		int combos = 0;
		foreach (bool compromised in new[] { false, true })
		foreach (int who in new[] { 0, 2 })          // a patroller, the sentry
		foreach (int st in states)
		foreach (int tk in tasks)
		foreach (int rd in radios)
		foreach (int sq in squadIds)
		foreach (int gr in ids)
		{
			combos++;
			string where = $"{(compromised ? "compromised" : "calm")} guard {who}: state {st} task {tk} radio {rd} squad {sq} group {gr}";
			try
			{
				var w = new SimWorld(level, 3);
				w.Player.X = -100000 * Fx.One;              // nobody to see: the guard alone decides
				w.Player.Y = -100000 * Fx.One;
				if (compromised) w.Compromise(Fx.One * 400, Fx.One * 280);
				var e = w.Guards[who];
				e.State = (GuardState)st;
				e.Task = (GuardTask)tk;
				e.Radio = (RadioPurpose)rd;
				e.RadioMt = Tune.RadioTicks * Actor.Mt / 2;
				e.SquadId = sq;
				if (gr != 0 || !compromised || w.Net.GroupById(0) == null) e.GroupId = gr;

				w.Step(new InputFrame(0, 0, 0, 0));
				if (!e.Prone)
				{
					if (!SimWorld.TaskFits(e.State, e.Task) || (e.Task == GuardTask.Radio && e.Radio == RadioPurpose.None))
						Fail("an injected guard is back inside the rules after one tick", where + $" -> {e.State}/{e.Task}/{e.Radio}");
					if (e.SquadId >= 0 && w.Net.SquadById(e.SquadId) == null)
						Fail("an injected guard is back inside the rules after one tick", where + $" -> dangling squad {e.SquadId}");
					var grp = w.Net.GroupById(e.GroupId);
					if (e.GroupId >= 0 && (grp == null || !grp.Members.Contains(who) || e.State != GuardState.Hunting))
						Fail("an injected guard is back inside the rules after one tick", where + $" -> group {e.GroupId}");
				}
				for (int k = 0; k < 2; k++) w.Step(new InputFrame(0, 0, 0, 0));
				for (int i = 0; i < w.Guards.Count; i++)
				{
					if (i == who || w.Guards[i].Prone) continue;
					if (!SimWorld.TaskFits(w.Guards[i].State, w.Guards[i].Task))
						Fail("repairing one guard leaves the others inside the rules", where + $" -> guard {i} {w.Guards[i].State}/{w.Guards[i].Task}");
				}
			}
			catch (Exception ex)
			{
				Fail("no injected guard state makes the sim throw", where + $": {ex.GetType().Name} {ex.Message}");
			}
		}
		// And the indices: a path, a route and a patrol index pointing before
		// the start and past the end of their lists, under every posture/task.
		foreach (int st in states)
		foreach (int tk in tasks)
		foreach (int bad in new[] { -5, 99 })
		{
			combos++;
			string where = $"state {st} task {tk}, indices {bad}";
			try
			{
				var w = new SimWorld(level, 3);
				w.Player.X = -100000 * Fx.One;
				w.Player.Y = -100000 * Fx.One;
				var e = w.Guards[0];
				e.State = (GuardState)st;
				e.Task = (GuardTask)tk;
				e.NavX.Add(e.X + 40 * Fx.One); e.NavY.Add(e.Y);
				e.NavX.Add(e.X + 80 * Fx.One); e.NavY.Add(e.Y);
				e.NavIndex = bad;
				e.RouteX.Add(e.X); e.RouteY.Add(e.Y + 40 * Fx.One);
				e.RouteIndex = bad;
				e.WaypointIndex = bad;
				for (int k = 0; k < 3; k++) w.Step(new InputFrame(0, 0, 0, 0));
			}
			catch (Exception ex)
			{
				Fail("no injected guard state makes the sim throw", where + $": {ex.GetType().Name} {ex.Message}");
			}
		}

		Console.WriteLine($"  guard state cross product: {combos} combinations in {clock.Elapsed.TotalSeconds:F1} s");
		Verdict("no injected guard state makes the sim throw", combos);
		Verdict("an injected guard is back inside the rules after one tick", combos);
		Verdict("repairing one guard leaves the others inside the rules", combos);
	}
}
