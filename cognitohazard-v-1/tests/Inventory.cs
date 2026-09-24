using System.Collections.Generic;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// The mission pack, looting fallen guards, and the secondary weapon.
///
/// All three are sim state that rides in replays, so what matters here is not
/// only that they behave but that they behave IDENTICALLY every time.
/// </summary>
public static class Inventory
{
	public static void Run()
	{
		PackGeometry();
		PackHashing();
		LoadoutDefaults();
		SecondaryWeapon();
		SwapTiming();
		GuardKits();
		Looting();
		CarriedIn();
		EventOrdinals();
	}

	private static Level Ref() => Level.FromText(Program.ReadLevel("substation_4.txt"));

	// ------------------------------------------------------------ pack grid

	private static void PackGeometry()
	{
		H.Group("pack grid");

		var g = new PackGrid(6, 4);
		H.Eq("a fresh pack is empty", g.UsedCells(), 0);
		H.Eq("and all of it is free", g.FreeCells(), 24);
		H.Eq("capacity is the cell count", g.Capacity, 24);

		// 102 is the AK-47, 4x2.
		int pi = g.Place(102, 0, 0);
		H.Check("a 4x2 rifle fits at the origin", pi != PackGrid.None);
		H.Eq("it covers eight cells", g.UsedCells(), 8);
		H.Eq("the origin reports it", g.PlacementAt(0, 0), pi);
		H.Eq("the cell past its span is free", g.PlacementAt(4, 0), PackGrid.None);

		H.Eq("overlapping it is refused", g.Place(100, 1, 1), PackGrid.None);
		H.Eq("and nothing was consumed", g.UsedCells(), 8);
		H.Eq("running off the edge is refused", g.Place(102, 4, 0), PackGrid.None);
		H.Eq("a negative origin is refused", g.Place(100, -1, 0), PackGrid.None);

		H.Check("it removes", g.Remove(pi));
		H.Eq("and gives the cells back", g.UsedCells(), 0);
		H.Check("removing twice is refused", !g.Remove(pi));

		// Rotation: a 4x2 turned is 2x4, which fits a 2-wide pack.
		var narrow = new PackGrid(2, 4);
		H.Eq("a 4x2 will not lie down in a 2-wide pack", narrow.Place(102, 0, 0), PackGrid.None);
		int up = narrow.Place(102, 0, 0, PackGrid.Rot90);
		H.Check("but it stands up", up != PackGrid.None);
		H.Check("turning it back is refused, there is no room", !narrow.Rotate(up));
		H.Eq("and it did not move", narrow.RotOf(up), PackGrid.Rot90);
		H.Eq("nor did the pack change", narrow.UsedCells(), 8);

		var roomy = new PackGrid(4, 4);
		int r = roomy.Place(303, 0, 0);         // scope, 2x1
		H.Check("with room it turns", roomy.Rotate(r));
		H.Eq("turning about the top-left keeps the origin", roomy.XOf(r), 0);

		// A pack cannot hold more placements than it has cells.
		var tiny = new PackGrid(2, 1);
		H.Check("two 1x1s fill a 2x1 pack",
			tiny.AutoPlace(301) != PackGrid.None && tiny.AutoPlace(302) != PackGrid.None);
		H.Eq("a third is refused", tiny.AutoPlace(311), PackGrid.None);
		H.Check("and nothing would fit", !tiny.WouldFit(311));

		// A degenerate pack -- no backpack worn -- must not crash anything.
		var none = new PackGrid(0, 0);
		H.Eq("a pack with no bag holds nothing", none.FreeCells(), 0);
		H.Eq("and refuses everything", none.AutoPlace(301), PackGrid.None);
		H.Eq("and reads empty out of bounds", none.PlacementAt(0, 0), PackGrid.None);

		// Fixed scan order, so the same loot always packs the same way.
		var a = new PackGrid(5, 5);
		var b = new PackGrid(5, 5);
		for (int i = 0; i < 4; i++) { a.AutoPlace(201); b.AutoPlace(201); }
		var ha = Hash64.New(); a.HashInto(ref ha);
		var hb = Hash64.New(); b.HashInto(ref hb);
		H.Eq("packing is reproducible", (long)ha.Value, (long)hb.Value);

		H.Eq("an unknown item id is a harmless 1x1",
			GearCatalog.Get(999999).Cells, 1);
	}

	private static void PackHashing()
	{
		H.Group("pack hashing");

		// Two packs holding the same items in different places must not hash
		// alike, or a replay could not tell them apart.
		var left = new PackGrid(4, 4);
		var right = new PackGrid(4, 4);
		left.Place(301, 0, 0);
		right.Place(301, 3, 3);
		var hl = Hash64.New(); left.HashInto(ref hl);
		var hr = Hash64.New(); right.HashInto(ref hr);
		H.Check("position reaches the hash", hl.Value != hr.Value);

		// And neither may a turned item hash like an unturned one.
		var flat = new PackGrid(4, 4);
		var stood = new PackGrid(4, 4);
		flat.Place(303, 0, 0, PackGrid.Rot0);
		stood.Place(303, 0, 0, PackGrid.Rot90);
		var hf = Hash64.New(); flat.HashInto(ref hf);
		var hs = Hash64.New(); stood.HashInto(ref hs);
		H.Check("rotation reaches the hash", hf.Value != hs.Value);

		var sized = new PackGrid(4, 4);
		var other = new PackGrid(4, 5);
		var h4 = Hash64.New(); sized.HashInto(ref h4);
		var h5 = Hash64.New(); other.HashInto(ref h5);
		H.Check("pack size reaches the hash", h4.Value != h5.Value);
	}

	// -------------------------------------------------------------- loadout

	/// <summary>
	/// default(Loadout) skips the constructor and zeroes every field, so every
	/// sentinel in that struct has to mean "empty" at zero. This caught a real
	/// bug: an empty holster encoded as -1 came out of default(Loadout) as 0 and
	/// read as a holstered Glock, which made a replay diverge from the very run
	/// that recorded it. These assertions are that bug's tripwire.
	/// </summary>
	private static void LoadoutDefaults()
	{
		H.Group("loadout defaults");

		var zero = default(Loadout);
		var made = Loadout.Default;

		H.Check("default(Loadout) holsters nothing", !zero.HasSecondary);
		H.Check("and neither does Loadout.Default", !made.HasSecondary);
		H.Eq("both hold the primary", (int)zero.Held, (int)made.Held);
		H.Eq("both carry no pack", zero.Backpack, made.Backpack);
		H.Eq("both start on the primary hand", zero.ActiveIndex, made.ActiveIndex);

		var hz = Hash64.New(); zero.HashInto(ref hz);
		var hm = Hash64.New(); made.HashInto(ref hm);
		H.Eq("and the two hash identically", (long)hz.Value, (long)hm.Value);

		// The text form has to survive a round trip, or a replay's loadout line
		// would not rebuild the run that recorded it.
		var kit = new Loadout(WeaponId.Ak47, ArmourId.HeavyPlate, 1, 2, 0, 1, 0, 0,
			secondary: (int)WeaponId.Glock, active: 1, backpack: 502);
		var back = Loadout.FromText(kit.ToText());
		H.Eq("round trip keeps the primary", (int)back.Weapon, (int)kit.Weapon);
		H.Eq("round trip keeps the holster", (int)back.Secondary, (int)kit.Secondary);
		H.Eq("round trip keeps the drawn hand", back.ActiveIndex, kit.ActiveIndex);
		H.Eq("round trip keeps the backpack", back.Backpack, kit.Backpack);
		var hk = Hash64.New(); kit.HashInto(ref hk);
		var hb2 = Hash64.New(); back.HashInto(ref hb2);
		H.Eq("and hashes the same after it", (long)hk.Value, (long)hb2.Value);

		H.Check("an empty holster round-trips as empty",
			!Loadout.FromText(Loadout.Default.ToText()).HasSecondary);
		H.Check("garbage text still yields a usable loadout",
			Loadout.FromText("nonsense=??? secondary=zzz").Held == WeaponId.Glock);
	}

	private static void SecondaryWeapon()
	{
		H.Group("secondary weapon");

		var solo = new Loadout(WeaponId.Ak47);
		H.Check("no holster by default", !solo.HasSecondary);
		H.Eq("drawing nothing keeps the rifle in hand", (int)solo.WithActive(1).Held, (int)WeaponId.Ak47);
		H.Eq("and the active hand stays the primary", solo.WithActive(1).ActiveIndex, 0);

		var pair = solo.WithSecondary((int)WeaponId.Glock);
		H.Check("a holstered pistol registers", pair.HasSecondary);
		H.Eq("the rifle is still in hand", (int)pair.Held, (int)WeaponId.Ak47);

		var drawn = pair.Swapped();
		H.Eq("swapping draws the pistol", (int)drawn.Held, (int)WeaponId.Glock);
		H.Eq("the primary slot still holds the rifle", (int)drawn.Weapon, (int)WeaponId.Ak47);
		H.Eq("swapping back re-draws the rifle", (int)drawn.Swapped().Held, (int)WeaponId.Ak47);

		// The weapon spec must follow the hand, or the pistol would fire rifle
		// rounds.
		H.Eq("the spec follows the drawn weapon",
			drawn.Spec.Magazine, WeaponCatalog.Get(WeaponId.Glock).Magazine);
		H.Check("and differs from the rifle's",
			pair.Spec.Magazine != drawn.Spec.Magazine);

		// KNOWN SIMPLIFICATION, asserted so it is a decision and not a surprise:
		// one attachment set, masked by whichever weapon is in hand.
		var scoped = pair.WithAttachment(AttachSlot.Grip, 2);
		H.Eq("the rifle takes the grip", scoped.Attachment(AttachSlot.Grip), 2);
		H.Check("the Glock has no grip slot", !WeaponCatalog.HasSlot(WeaponId.Glock, AttachSlot.Grip));
		H.Eq("so drawing it masks the grip away", scoped.Swapped().Attachment(AttachSlot.Grip), 0);

		H.Check("clearing the holster empties it", !pair.WithSecondary(-1).HasSecondary);
		H.Eq("and drops back to the primary hand", pair.Swapped().WithSecondary(-1).ActiveIndex, 0);
	}

	private static void SwapTiming()
	{
		H.Group("weapon swap");

		var kit = new Loadout(WeaponId.Ak47, secondary: (int)WeaponId.Glock);
		var w = new SimWorld(Ref(), 99UL, kit);

		int rifleMag = w.Player.Mag;
		int pistolMag = w.Player.MagStowed;
		H.Eq("the rifle starts loaded", rifleMag, WeaponCatalog.Get(WeaponId.Ak47).Magazine);
		H.Eq("the holstered pistol carries its own magazine",
			pistolMag, WeaponCatalog.Get(WeaponId.Glock).Magazine);

		var swap = new InputFrame(0, 0, 0, InputFrame.FSwap);
		var idle = new InputFrame(0, 0, 0, 0);

		w.Step(swap);
		H.Eq("the rifle is still in hand one tick in", (int)w.Loadout.Held, (int)WeaponId.Ak47);
		H.Check("but a swap is under way", w.Player.SwapMt > 0);

		// Firing is blocked mid-swap, the way it is mid-reload.
		int shotsBefore = w.Shots;
		w.Step(new InputFrame(0, 0, 0, InputFrame.FFire));
		H.Eq("firing mid-swap does nothing", w.Shots, shotsBefore);

		for (int i = 0; i < Tune.SwapTicks + 2; i++) w.Step(idle);
		H.Eq("the pistol comes up", (int)w.Loadout.Held, (int)WeaponId.Glock);
		H.Eq("the swap timer cleared", w.Player.SwapMt, 0);
		H.Eq("each weapon kept its own magazine", w.Player.Mag, pistolMag);
		H.Eq("and the rifle's went to the holster", w.Player.MagStowed, rifleMag);

		// Nothing to swap to means nothing happens at all.
		var alone = new SimWorld(Ref(), 99UL, new Loadout(WeaponId.Ak47));
		alone.Step(swap);
		H.Eq("swapping with an empty holster is a no-op", alone.Player.SwapMt, 0);
		H.Eq("and leaves the rifle in hand", (int)alone.Loadout.Held, (int)WeaponId.Ak47);
	}

	// --------------------------------------------------------------- looting

	private static void GuardKits()
	{
		H.Group("guard kits");

		var one = new SimWorld(Ref(), 4242UL);
		var two = new SimWorld(Ref(), 4242UL);

		H.Check("the level has guards to strip", one.Guards.Count > 0);

		bool sameKits = one.Guards.Count == two.Guards.Count;
		int emptyKits = 0;
		int unknownItems = 0;
		for (int i = 0; sameKits && i < one.Guards.Count; i++)
		{
			var a = one.Guards[i];
			var b = two.Guards[i];
			if (a.Kit.Count != b.Kit.Count) { sameKits = false; break; }
			if (a.Kit.Count == 0) emptyKits++;
			for (int k = 0; k < a.Kit.Count; k++)
			{
				if (a.Kit[k] != b.Kit[k]) { sameKits = false; break; }
				if (!GearCatalog.Exists(a.Kit[k])) unknownItems++;
			}
		}
		H.Check("the same seed rolls the same kits", sameKits);
		H.Eq("every guard carries something", emptyKits, 0);
		H.Eq("and every item rolled is a real one", unknownItems, 0);

		// A different seed should not produce an identical world.
		var other = new SimWorld(Ref(), 777UL);
		bool anyDifference = false;
		for (int i = 0; i < one.Guards.Count && i < other.Guards.Count; i++)
		{
			if (one.Guards[i].Kit.Count != other.Guards[i].Kit.Count) anyDifference = true;
			else for (int k = 0; k < one.Guards[i].Kit.Count; k++)
				if (one.Guards[i].Kit[k] != other.Guards[i].Kit[k]) anyDifference = true;
		}
		H.Check("a different seed rolls different kits", anyDifference);

		// Rolling kits must not have disturbed the combat stream.
		H.Eq("the combat RNG is untouched by kit rolling", (long)one.Rng.Draws, 0L);
	}

	private static void Looting()
	{
		H.Group("looting the fallen");

		// A guard put down right next to the player, with a known kit so the tests
		// can name exactly which item a pick should take.
		var kit = new Loadout(WeaponId.Ak47, backpack: 503);   // large pack, 8x5
		var w = new SimWorld(Ref(), 5UL, kit);
		H.Eq("the worn pack sizes the grid", w.Pack.W * w.Pack.H, 40);

		var g = w.Guards[0];
		g.State = GuardState.Down;
		g.X = w.Player.X;
		g.Y = w.Player.Y;
		g.Kit.Clear();
		g.Kit.Add(402);      // combat helmet, 2x2
		g.Kit.Add(201);      // light weave, 2x2
		g.Kit.Add(301);      // red dot, 1x1

		var idle = new InputFrame(0, 0, 0, 0);
		var look = new InputFrame(0, 0, 0, InputFrame.FLoot);

		// Standing over a body takes nothing at all now: there is no dwell to fill,
		// and merely looking is not taking.
		for (int i = 0; i < 300; i++) w.Step(look);
		H.Eq("standing over a body takes nothing", w.Pack.LiveCount(), 0);
		H.Eq("and leaves the kit whole", g.Kit.Count, 3);

		// A pick takes exactly the item named, not the first one that fits.
		w.Step(new InputFrame(0, 0, 0, 0, 2));       // the SECOND item
		H.Eq("a pick takes one item", w.Pack.LiveCount(), 1);
		H.Eq("the chosen one", w.Pack.ItemOf(0), 201);
		H.Eq("and the body is one lighter", g.Kit.Count, 2);
		H.Eq("the kit closed the gap", g.Kit[1], 301);

		// One pick per tick, not one per tick held.
		H.Eq("the pack still holds one", w.Pack.LiveCount(), 1);
		for (int i = 0; i < 60; i++) w.Step(idle);
		H.Eq("idling takes nothing more", w.Pack.LiveCount(), 1);

		w.Step(new InputFrame(0, 0, 0, 0, 2));       // now the red dot
		H.Eq("a second pick takes a second item", w.Pack.LiveCount(), 2);
		H.Eq("again the chosen one", w.Pack.ItemOf(1), 301);

		// A pick past the end of the kit is ignored rather than grabbing whatever
		// slid into that position. The kit shrinks under the cursor, so a click on
		// a row that has since gone must do nothing.
		w.Step(new InputFrame(0, 0, 0, 0, 9));
		H.Eq("a stale pick takes nothing", w.Pack.LiveCount(), 2);
		H.Eq("and the last item is untouched", g.Kit.Count, 1);
		w.Step(new InputFrame(0, 0, 0, 0, 0));
		H.Eq("a pick of zero is no pick", w.Pack.LiveCount(), 2);

		// The event names what was taken, which is what the HUD reads.
		int reportedId = -1;
		w.Step(new InputFrame(0, 0, 0, 0, 1));
		for (int e = 0; e < w.Log.Events.Count; e++)
			if (w.Log.Events[e].Kind == SimEventKind.Looted)
				reportedId = w.Log.Events[e].Value;
		H.Eq("the loot event names the item taken", reportedId, 402);
		H.Check("and it resolves to a real name",
			GearCatalog.NameOf(reportedId) == "combat helmet", GearCatalog.NameOf(reportedId));
		H.Eq("the body is stripped clean", g.Kit.Count, 0);
		H.Eq("and everything reached the pack", w.Pack.LiveCount(), 3);

		// The pack already holds three, so "nothing" means the count does not move.
		H.Eq("picking from a stripped body changes nothing", PickFrom(w, 1), 3);

		// Out of reach, nothing happens however hard you click.
		var far = new SimWorld(Ref(), 5UL, kit);
		var fg = far.Guards[0];
		fg.State = GuardState.Down;
		fg.X = far.Player.X + Tune.LootReach * 4;
		fg.Y = far.Player.Y;
		H.Eq("a body out of reach cannot be picked from", PickFrom(far, 1), 0);

		// A guard still on his feet cannot be stripped, however close.
		var up = new SimWorld(Ref(), 5UL, kit);
		var ug = up.Guards[0];
		ug.State = GuardState.Relaxed; ug.Task = GuardTask.Post;
		ug.X = up.Player.X;
		ug.Y = up.Player.Y;
		H.Eq("a standing guard cannot be picked from", PickFrom(up, 1), 0);

		// No backpack means no room for anything.
		var bagless = new SimWorld(Ref(), 5UL, new Loadout(WeaponId.Ak47));
		var bg = bagless.Guards[0];
		bg.State = GuardState.Dead;
		bg.X = bagless.Player.X;
		bg.Y = bagless.Player.Y;
		int had = bg.Kit.Count;
		H.Eq("with no pack worn nothing can be carried", PickFrom(bagless, 1), 0);
		H.Eq("and the body keeps its kit", bg.Kit.Count, had);

		// Picking something that will not fit is refused and reported, and the rest
		// of the body stays available. This is the decision the grid exists for.
		var small = new SimWorld(Ref(), 5UL, new Loadout(WeaponId.Ak47, backpack: 501));
		var sg = small.Guards[0];
		sg.State = GuardState.Down;
		sg.X = small.Player.X;
		sg.Y = small.Player.Y;
		sg.Kit.Clear();
		sg.Kit.Add(104);                    // SAW, 5x3 -- cannot fit a 4x3 satchel
		sg.Kit.Add(301);                    // red dot, 1x1 -- fits

		bool refused = false;
		small.Step(new InputFrame(0, 0, 0, 0, 1));
		for (int e = 0; e < small.Log.Events.Count; e++)
			if (small.Log.Events[e].Kind == SimEventKind.PackFull) refused = true;
		H.Check("picking something oversized reports PackFull", refused);
		H.Eq("it took nothing", small.Pack.LiveCount(), 0);
		H.Eq("and it is still on the body", sg.Kit.Count, 2);

		small.Step(new InputFrame(0, 0, 0, 0, 2));
		H.Eq("but the small item can still be taken", small.Pack.LiveCount(), 1);
		H.Eq("leaving the oversized one behind", sg.Kit.Count, 1);
		H.Eq("which is the one that would not fit", sg.Kit[0], 104);

		// A pick has to survive a replay, or choosing what to loot could not be
		// recorded at all.
		var rec = new Replay { Seed = 7UL, LevelText = Program.ReadLevel("substation_4.txt") };
		rec.Inputs.Add(new InputFrame(1, 0, 1234, InputFrame.FLoot, 3));
		rec.Inputs.Add(new InputFrame(0, 0, 0, 0, 0));
		rec.Inputs.Add(new InputFrame(0, 1, 99, InputFrame.FFire, 11));
		var back = Replay.FromText(rec.ToText());
		H.Eq("a replay keeps every frame", back.Inputs.Count, rec.Inputs.Count);
		H.Eq("and the pick on the first", back.Inputs[0].LootPick, 3);
		H.Eq("and the absence of one on the second", back.Inputs[1].LootPick, 0);
		H.Eq("and the pick on the third", back.Inputs[2].LootPick, 11);
		H.Eq("with the flags intact", back.Inputs[2].Flags, (byte)InputFrame.FFire);
		H.Eq("and the aim intact", back.Inputs[0].AimBrad, (ushort)1234);

		// Frames that differ only by their pick must not be run-length collapsed
		// into one, or two clicks would replay as one.
		var twice = new Replay { Seed = 1UL, LevelText = "x" };
		twice.Inputs.Add(new InputFrame(0, 0, 0, 0, 1));
		twice.Inputs.Add(new InputFrame(0, 0, 0, 0, 1));
		twice.Inputs.Add(new InputFrame(0, 0, 0, 0, 2));
		var twiceBack = Replay.FromText(twice.ToText());
		H.Eq("repeated picks survive the run-length packing", twiceBack.Inputs.Count, 3);
		H.Eq("the first is kept", twiceBack.Inputs[0].LootPick, 1);
		H.Eq("the second too", twiceBack.Inputs[1].LootPick, 1);
		H.Eq("and the third differs", twiceBack.Inputs[2].LootPick, 2);
	}

	/// <summary>Steps one pick and answers how many items ended up in the pack.</summary>
	private static int PickFrom(SimWorld w, int pick)
	{
		w.Step(new InputFrame(0, 0, 0, 0, pick));
		return w.Pack.LiveCount();
	}

	/// <summary>
	/// game/main.gd mirrors these ordinals by hand as EV_LOOTED, EV_PACK_FULL and
	/// EV_WEAPON_SWAPPED, because the bridge hands events over as plain ints. The
	/// count is already checked at runtime, but a count check cannot catch a kind
	/// INSERTED mid-enum -- that keeps the count and silently shifts every
	/// ordinal, remapping the loot pop onto some other event. These pin them.
	/// </summary>
	/// <summary>
	/// THE KIT THE PLAYER WALKS IN WITH: Loadout.Carried, placed in the pack at
	/// Restart.
	///
	/// It is hashed and it rides in the replay, because it is the pack -- and
	/// the pack is sim state. A run that began with a plate in the bag and one
	/// that did not are different runs from tick zero.
	/// </summary>
	private static void CarriedIn()
	{
		H.Group("the kit walked in with");

		var bare = new Loadout(WeaponId.Glock, ArmourId.None, backpack: 503);
		var w0 = new SimWorld(Ref(), 7UL, bare);
		H.Eq("with nothing carried the pack starts empty", w0.Pack.UsedCells(), 0);

		var packed = bare.WithCarried(new[] { 203, 301, 101 });
		H.Eq("the kit records what is in it", packed.CarriedCount, 3);
		var w = new SimWorld(Ref(), 7UL, packed);

		int found = 0;
		for (int pi = 0; pi < w.Pack.Capacity; pi++)
			if (w.Pack.IsLive(pi)) found++;
		H.Eq("and Restart puts every one of them in the pack", found, 3);
		H.Check("the pack is a valid packing", w.Pack.UsedCells() > 0);

		// ---- it is part of the run, not decoration ----
		var a = new SimWorld(Ref(), 7UL, packed);
		var b = new SimWorld(Ref(), 7UL, bare);
		H.Check("a run that carries something hashes differently from one that does not",
			a.StateHash() != b.StateHash(),
			$"{a.StateHash():x} vs {b.StateHash():x}");

		// ORDER matters, because the pack is packed in it.
		var reordered = bare.WithCarried(new[] { 101, 301, 203 });
		H.Check("and the order it was packed in is part of it",
			new SimWorld(Ref(), 7UL, reordered).StateHash() != a.StateHash());

		// ---- the text format carries it ----
		var back = Loadout.FromText(packed.ToText());
		H.Eq("the text format carries the count", back.CarriedCount, 3);
		for (int i = 0; i < 3; i++)
			H.Eq($"and item {i}", back.CarriedAt(i), packed.CarriedAt(i));
		H.Eq("a kit written before it existed carries nothing",
			Loadout.FromText("weapon=2 armour=1").CarriedCount, 0);

		// ---- total, like every other parser here ----
		var junk = bare.WithCarried(new[] { 99999, 203, -4, 0 });
		H.Eq("an item that does not exist is dropped, not thrown over",
			junk.CarriedCount, 1);
		H.Eq("leaving the one that does", junk.CarriedAt(0), 203);

		var flood = new int[Loadout.MaxCarried * 4];
		for (int i = 0; i < flood.Length; i++) flood[i] = 301;
		H.Eq("and a flooded save is capped",
			bare.WithCarried(flood).CarriedCount, Loadout.MaxCarried);
		H.Eq("clearing it clears it", packed.WithCarried(null).CarriedCount, 0);

		// ---- a bag that will not hold the kit simply does not hold it ----
		// The screen refuses to stage more than fits; this is the floor under
		// that, and it must not be a throw.
		var tiny = new Loadout(WeaponId.Glock, ArmourId.None, backpack: 501)
			.WithCarried(new[] { 203, 203, 203, 203, 203 });
		var wt = new SimWorld(Ref(), 7UL, tiny);
		int fit = 0;
		for (int pi = 0; pi < wt.Pack.Capacity; pi++)
			if (wt.Pack.IsLive(pi)) fit++;
		H.Check("a satchel takes what it can and no more",
			fit >= 1 && fit < 5, $"{fit} of 5");

		// ---- and none of it needs a bag to be survivable ----
		var bagless = new Loadout(WeaponId.Glock).WithCarried(new[] { 203 });
		var wb = new SimWorld(Ref(), 7UL, bagless);
		H.Eq("with no bag, nothing is carried at all", wb.Pack.UsedCells(), 0);
	}

	private static void EventOrdinals()
	{
		H.Group("event ordinals mirrored by game/");

		H.Eq("Looted is 21", (int)SimEventKind.Looted, 21);
		H.Eq("PackFull is 22", (int)SimEventKind.PackFull, 22);
		H.Eq("WeaponSwapped is 23", (int)SimEventKind.WeaponSwapped, 23);
		// Appended with armoured guards, after WeaponSwapped.
		H.Eq("GuardArmourHit is 24", (int)SimEventKind.GuardArmourHit, 24);
		H.Eq("GuardArmourBroken is 25", (int)SimEventKind.GuardArmourBroken, 25);
		// Appended when gear could be dropped.
		H.Eq("Dropped is 26", (int)SimEventKind.Dropped, 26);
		// Appended with the developer menu's mid-run spawn.
		H.Eq("Spawned is 27", (int)SimEventKind.Spawned, 27);
		// Appended when gear could be put on mid-mission.
		H.Eq("Equipped is 28", (int)SimEventKind.Equipped, 28);
		// Appended with glass and doors.
		H.Eq("GlassBroken is 29", (int)SimEventKind.GlassBroken, 29);
		H.Eq("DoorOpened is 30", (int)SimEventKind.DoorOpened, 30);
		H.Eq("DoorClosed is 31", (int)SimEventKind.DoorClosed, 31);
		H.Eq("DoorBlocked is 32", (int)SimEventKind.DoorBlocked, 32);
		// Appended with the specialist weapons.
		H.Eq("WallPierced is 33", (int)SimEventKind.WallPierced, 33);
		H.Eq("ArcJump is 34", (int)SimEventKind.ArcJump, 34);
		H.Eq("GrenadeThrown is 35", (int)SimEventKind.GrenadeThrown, 35);
		H.Eq("GrenadeBounce is 36", (int)SimEventKind.GrenadeBounce, 36);
		H.Eq("Blast is 37", (int)SimEventKind.Blast, 37);
		// Appended with Guard AI v2's radio.
		H.Eq("RadioStart is 38", (int)SimEventKind.RadioStart, 38);
		H.Eq("RadioSent is 39", (int)SimEventKind.RadioSent, 39);
		H.Eq("RadioCut is 40", (int)SimEventKind.RadioCut, 40);
		H.Eq("Compromised is 41", (int)SimEventKind.Compromised, 41);
		// Appended with fear.
		H.Eq("Afraid is 42", (int)SimEventKind.Afraid, 42);
		// Appended with lighting.
		H.Eq("LampBroken is 43", (int)SimEventKind.LampBroken, 43);
		H.Eq("LightsOn is 44", (int)SimEventKind.LightsOn, 44);
		H.Eq("LightsOff is 45", (int)SimEventKind.LightsOff, 45);
		H.Eq("and that is the last kind",
			System.Enum.GetValues(typeof(SimEventKind)).Length, 46);

		// The ones already mirrored, so an insertion anywhere above is caught too.
		H.Eq("Headshot is still 20", (int)SimEventKind.Headshot, 20);
		H.Eq("PlayerShot is still 0", (int)SimEventKind.PlayerShot, 0);
	}

}
