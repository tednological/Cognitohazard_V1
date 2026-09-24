using System;
using System.Collections.Generic;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// The campaign's supply side: how scarce burnable records are, what chests
/// hold, and that everything sellable has a price.
///
/// The money and the shop live in game/ and are covered by inventory_check.gd.
/// What is testable here is the half the sim owns — and the half that, if it
/// drifted, would quietly turn dilation back into a free resource.
/// </summary>
public static class Economy
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

	// ------------------------------------------------------ record scarcity

	private static void Scarcity()
	{
		H.Group("record scarcity");

		var L = Level.FromText(Program.ReadLevel("substation_4.txt"));

		int carried = 0, carriers = 0;
		foreach (var g in L.Guards)
		{
			carried += g.Tiers.Length;
			if (g.Tiers.Length > 0) carriers++;
		}
		int inCaches = 0;
		foreach (var c in L.Caches) inCaches += c.Tiers.Length;

		Console.WriteLine();
		Console.WriteLine($"  reference level: {L.Guards.Count} guards, {carriers} carrying"
			+ $" {carried} record(s); {L.Caches.Count} caches holding {inCaches}");
		Console.WriteLine();

		H.Check("most guards carry nothing", carriers * 2 < L.Guards.Count,
			$"{carriers} of {L.Guards.Count}");
		H.Check("and a carrier carries exactly one",
			L.Guards.TrueForAll(g => g.Tiers.Length <= 1));
		H.Check("a cache holds one record",
			L.Caches.TrueForAll(c => c.Tiers.Length == 1));

		// The headline number. Before this pass the reference level held about
		// twenty-three burnable records; dilation was effectively unlimited.
		var w = new SimWorld(Level.FromText(Program.ReadLevel("substation_4.txt")), 1);
		int reachable = 1;          // the briefing the player starts holding
		foreach (var g in w.Guards) reachable += g.Carried.Count;
		foreach (var c in w.Caches) reachable += c.Contents.Count;
		H.Check("a whole floor holds under a dozen burnable records", reachable < 12,
			$"{reachable} on the reference level");
		H.Check("but not zero, or the parasite is unusable", reachable >= 4,
			$"{reachable}");

		// The starting briefing survives: it is what stops the first room from
		// being the one place dilation is unavailable.
		H.Eq("the player still starts holding one", w.Records.Held.Count, 1);
		H.Eq("and it is a tier-one", w.Records.Held[0].Tier, 1);
	}

	// -------------------------------------------------------------- chests

	private static void Chests()
	{
		H.Group("chests");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		Put(g, 10, 10, 'C');
		Put(g, 20, 10, 'C');
		string text = Text(g);

		var L = Level.FromText(text);
		H.Eq("the C glyph parses as a chest", L.Chests.Count, 2);
		H.Check("and round-trips", Level.FromText(L.ToText()).Chests.Count == 2);

		var w = new SimWorld(L, 7);
		H.Eq("the world builds one runtime chest each", w.Chests.Count, 2);

		bool stocked = true, known = true;
		foreach (var c in w.Chests)
		{
			if (c.Kit.Count == 0) stocked = false;
			foreach (int id in c.Kit)
				if (!GearCatalog.Exists(id)) known = false;
		}
		H.Check("no chest is empty", stocked);
		H.Check("and everything in one is a real item", known);

		// Rolled from the loot stream, so the same seed stocks the same floor.
		var again = new SimWorld(Level.FromText(text), 7);
		bool same = again.Chests.Count == w.Chests.Count;
		for (int i = 0; i < w.Chests.Count && same; i++)
		{
			if (again.Chests[i].Kit.Count != w.Chests[i].Kit.Count) same = false;
			for (int k = 0; k < w.Chests[i].Kit.Count && same; k++)
				if (again.Chests[i].Kit[k] != w.Chests[i].Kit[k]) same = false;
		}
		H.Check("the same seed stocks the same chests", same);

		var other = new SimWorld(Level.FromText(text), 8);
		bool differs = false;
		for (int i = 0; i < w.Chests.Count; i++)
			if (other.Chests[i].Kit.Count != w.Chests[i].Kit.Count) differs = true;
		H.Check("a different seed stocks them differently", differs
			|| other.Chests[0].Kit[0] != w.Chests[0].Kit[0]);

		// Chests must NOT disturb the guard kits. They are rolled after the
		// guards for exactly this reason: a level gaining a chest would
		// otherwise re-roll every body on it.
		var noChests = Room();
		Put(noChests, 1, 1, '@');
		Put(noChests, GW - 2, GH - 2, 'X');
		Put(noChests, 24, 14, 'a');
		var withChest = (char[])noChests.Clone();
		Put(withChest, 10, 10, 'C');

		var a = new SimWorld(Level.FromText(Text(noChests)), 5);
		var b = new SimWorld(Level.FromText(Text(withChest)), 5);
		bool kitsMatch = a.Guards[0].Kit.Count == b.Guards[0].Kit.Count;
		for (int i = 0; i < a.Guards[0].Kit.Count && kitsMatch; i++)
			if (a.Guards[0].Kit[i] != b.Guards[0].Kit[i]) kitsMatch = false;
		H.Check("adding a chest does not re-roll the guards", kitsMatch);

		// Chests are hashed, so one being emptied is real state.
		var h1 = w.StateHash();
		w.Chests[0].Kit.Clear();
		H.Check("emptying a chest changes the state hash", w.StateHash() != h1);
	}

	// ------------------------------------------------------- looting a chest

	private static void LootingAChest()
	{
		H.Group("looting a chest");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		Put(g, 24, 14, 'a');
		Put(g, 10, 14, 'C');

		var w = new SimWorld(Level.FromText(Text(g)), 7,
			new Loadout(WeaponId.Glock, ArmourId.None, backpack: 503));
		var chest = w.Chests[0];

		// Bodies and chests share ONE index space, guards first.
		H.Eq("the index space covers both", w.LootTargetCount,
			w.Guards.Count + w.Chests.Count);
		H.Check("a live guard is not a loot target",
			!w.TryLootTarget(0, out _, out _, out _));
		H.Check("but a chest always is",
			w.TryLootTarget(w.Guards.Count, out _, out _, out var kit) && kit != null);

		// Out of reach, nothing is offered.
		w.Player.X = chest.X + 400 * Fx.One;
		w.Player.Y = chest.Y;
		w.Step(new InputFrame(0, 0, 0, 0));
		H.Eq("a chest across the room is not in reach", w.NearestLootTarget(), -1);

		// Stand over it and take an item.
		w.Player.X = chest.X;
		w.Player.Y = chest.Y;
		w.Step(new InputFrame(0, 0, 0, 0));
		H.Eq("standing on it makes it the target",
			w.NearestLootTarget(), w.Guards.Count);

		int before = chest.Kit.Count;
		int wanted = chest.Kit[0];
		H.Check("fixture: the chest has something in it", before > 0);
		H.Eq("fixture: the pack starts empty", w.Pack.UsedCells(), 0);

		w.Step(new InputFrame(0, 0, 0, 0, 1));      // LootPick 1 == first row
		H.Eq("taking an item removes it from the chest", chest.Kit.Count, before - 1);
		H.Check("and puts it in the pack", w.Pack.UsedCells() > 0);

		bool inPack = false;
		for (int pi = 0; pi < w.Pack.Capacity; pi++)
			if (w.Pack.IsLive(pi) && w.Pack.ItemOf(pi) == wanted) inPack = true;
		H.Check("and it is the item that was asked for", inPack);

		// A stale pick past the end of the kit is ignored, not a crash.
		int stable = chest.Kit.Count;
		H.NoThrow("an out-of-range pick is harmless",
			() => w.Step(new InputFrame(0, 0, 0, 0, 99)));
		H.Eq("and takes nothing", chest.Kit.Count, stable);

		// Emptied, it stops being a target.
		for (int i = 0; i < 20 && chest.Kit.Count > 0; i++)
			w.Step(new InputFrame(0, 0, 0, 0, 1));
		H.Eq("a chest can be stripped bare", chest.Kit.Count, 0);
		H.Eq("and an empty one is no longer a target", w.NearestLootTarget(), -1);
	}

	// -------------------------------------------------------------- prices

	private static void Prices()
	{
		H.Group("prices");

		int priced = 0, free = 0;
		for (int i = 0; i < GearCatalog.Count; i++)
		{
			int id = GearCatalog.At(i).Id;
			if (GearCatalog.PriceOf(id) > 0) priced++; else free++;
		}
		H.Check("almost everything has a price", priced > GearCatalog.Count - 3,
			$"{priced} priced, {free} free");

		// The starting sidearm is free ON PURPOSE -- buying a second Glock must
		// never be a thing a player considers.
		H.Eq("the starting pistol is not for sale", GearCatalog.PriceOf(100), 0);

		// Prices must order sensibly, or the shop is noise.
		H.Check("a rifle costs more than a submachine gun",
			GearCatalog.PriceOf(102) > GearCatalog.PriceOf(101));
		H.Check("the SAW is the most expensive weapon",
			GearCatalog.PriceOf(104) > GearCatalog.PriceOf(102));
		H.Check("heavy plate costs more than light weave",
			GearCatalog.PriceOf(203) > GearCatalog.PriceOf(201));
		H.Check("a large pack costs more than a satchel",
			GearCatalog.PriceOf(503) > GearCatalog.PriceOf(501));
		H.Check("and apparel is cheap next to a weapon",
			GearCatalog.PriceOf(601) < GearCatalog.PriceOf(101) / 4);

		// An unknown id answers rather than throwing.
		H.Eq("an unknown item is priceless, not fatal", GearCatalog.PriceOf(99999), 0);
	}

	// ---------------------------------------------------- mission objectives

	/// <summary>
	/// The thing the mission is FOR. Found in the level, carried in the pack,
	/// and checked at the exit. Extracting without it is allowed and pays
	/// nothing, so the contract that matters is that the sim reports honestly
	/// whether it is in the bag.
	/// </summary>
	private static void Objectives()
	{
		H.Group("mission objectives");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		Put(g, 10, 14, '!');
		Put(g, 20, 14, 'C');
		string text = Text(g);

		var L = Level.FromText(text);
		H.Eq("the ! glyph is an objective", L.Objectives, 1);
		H.Eq("and builds a chest to hold it", L.Chests.Count, 2);
		H.Check("one of which is the objective site",
			L.Chests.Exists(c => c.Objective));
		H.Check("and it round-trips", Level.FromText(L.ToText()).Objectives == 1);

		var w = new SimWorld(L, 7,
			new Loadout(WeaponId.Glock, ArmourId.None, backpack: 503));
		H.Eq("the world wants one objective", w.ObjectivesTotal, 1);
		H.Eq("and none is carried yet", w.ObjectivesCarried, 0);
		H.Check("so the mission is not met", !w.ObjectivesMet);

		// The objective site holds exactly the objective.
		ChestRuntime? site = null;
		ChestRuntime? supply = null;
		foreach (var c in w.Chests) { if (c.Objective) site = c; else supply = c; }
		H.Check("fixture: both chests exist", site != null && supply != null);
		H.Eq("the site holds exactly one thing", site!.Kit.Count, 1);
		H.Check("and it is the objective", GearCatalog.IsObjective(site.Kit[0]));
		H.Check("a supply chest holds no objective",
			!supply!.Kit.Exists(GearCatalog.IsObjective));

		// Taking it is ordinary looting.
		w.Player.X = site.X;
		w.Player.Y = site.Y;
		w.Step(new InputFrame(0, 0, 0, 0));
		w.Step(new InputFrame(0, 0, 0, 0, 1));
		H.Eq("taking it empties the site", site.Kit.Count, 0);
		H.Eq("and it is in the pack", w.ObjectivesCarried, 1);
		H.Check("so the mission is met", w.ObjectivesMet);

		// A level with no objective is met by default -- otherwise every level
		// built before objectives existed would be unpayable.
		var bare = Room();
		Put(bare, 1, 1, '@');
		Put(bare, GW - 2, GH - 2, 'X');
		var w2 = new SimWorld(Level.FromText(Text(bare)), 7);
		H.Eq("a level with no objective wants none", w2.ObjectivesTotal, 0);
		H.Check("and is met without carrying anything", w2.ObjectivesMet);

		// An objective site must NOT draw from the loot stream, or adding one
		// would re-roll every supply chest on the level.
		var noObj = Room();
		Put(noObj, 1, 1, '@');
		Put(noObj, GW - 2, GH - 2, 'X');
		Put(noObj, 20, 14, 'C');
		var withObj = (char[])noObj.Clone();
		Put(withObj, 10, 14, '!');

		var a = new SimWorld(Level.FromText(Text(noObj)), 5);
		var b = new SimWorld(Level.FromText(Text(withObj)), 5);
		var aKit = a.Chests[0].Kit;
		ChestRuntime bSupply = b.Chests[0].Objective ? b.Chests[1] : b.Chests[0];
		bool same = aKit.Count == bSupply.Kit.Count;
		for (int i = 0; i < aKit.Count && same; i++)
			if (aKit[i] != bSupply.Kit[i]) same = false;
		H.Check("adding an objective does not re-roll the supply chests", same);

		// The objective is not merchandise.
		H.Eq("the objective has no price", GearCatalog.PriceOf(GearCatalog.ObjectiveId), 0);
		H.Check("and is its own kind",
			GearCatalog.Get(GearCatalog.ObjectiveId).Kind == GearKind.Objective);
		H.Check("which nothing else is",
			!GearCatalog.IsObjective(100) && !GearCatalog.IsObjective(102));

		// Every shipped level states an objective, or it can never be completed.
		foreach (string name in new[] { "substation_4.txt", "relay_nine.txt",
			"terminal_twelve.txt" })
		{
			var lvl = Level.FromText(Program.ReadLevel(name));
			H.Check($"{name} has an objective to extract", lvl.Objectives > 0,
				$"{lvl.Objectives}");
		}
	}

	// ------------------------------------------------------ dropping gear

	/// <summary>
	/// Putting something down and picking it up again are the two halves of one
	/// interaction, so a dropped pile is a loot target like any other. The part
	/// that would break silently is the recording: a drop moves sim state, and
	/// a drop performed by a menu rather than recorded as intent desyncs every
	/// replay of the run it happened in.
	/// </summary>
	private static void Dropping()
	{
		H.Group("dropping gear");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		Put(g, 10, 14, 'C');

		var w = new SimWorld(Level.FromText(Text(g)), 7,
			new Loadout(WeaponId.Glock, ArmourId.None, backpack: 503));
		var chest = w.Chests[0];
		w.Player.X = chest.X;
		w.Player.Y = chest.Y;

		// Take two things out of the chest so there is something to put down.
		w.Step(new InputFrame(0, 0, 0, 0));
		w.Step(new InputFrame(0, 0, 0, 0, 1));
		w.Step(new InputFrame(0, 0, 0, 0, 1));
		H.Check("fixture: the pack has something in it", w.Pack.UsedCells() > 0);

		int pi = -1, itemId = -1;
		for (int i = 0; i < w.Pack.Capacity; i++)
			if (w.Pack.IsLive(i)) { pi = i; itemId = w.Pack.ItemOf(i); break; }
		H.Check("fixture: found a placement to drop", pi >= 0);

		int usedBefore = w.Pack.UsedCells();
		H.Eq("nothing is on the floor yet", w.Ground.Count, 0);

		// Step away from the chest first. Dropping on top of it would leave two
		// targets at the same distance, and the tie goes to the lower index --
		// which is the chest, so the test would be measuring tie-breaking.
		w.Player.X = chest.X + 120 * Fx.One;
		w.Step(new InputFrame(0, 0, 0, 0, 0, -1, pi + 1));
		H.Check("dropping frees pack space", w.Pack.UsedCells() < usedBefore);
		H.Eq("and puts a pile on the floor", w.Ground.Count, 1);
		H.Eq("holding what was dropped", w.Ground[0].Kit[0], itemId);
		H.Check("at the player's feet",
			Fx.Dist(w.Ground[0].X, w.Ground[0].Y, w.Player.X, w.Player.Y) < Fx.One);

		bool reported = false;
		foreach (var ev in w.Log.Events)
			if (ev.Kind == SimEventKind.Dropped) reported = true;
		H.Check("and it is reported", reported);

		// A second drop in the same spot JOINS the pile rather than starting
		// another -- six overlapping one-item heaps would mean rummaging six
		// times to undo one mistake.
		int pi2 = -1;
		for (int i = 0; i < w.Pack.Capacity; i++)
			if (w.Pack.IsLive(i)) { pi2 = i; break; }
		if (pi2 >= 0)
		{
			w.Step(new InputFrame(0, 0, 0, 0, 0, -1, pi2 + 1));
			H.Eq("a second drop joins the same pile", w.Ground.Count, 1);
			H.Eq("which now holds two", w.Ground[0].Kit.Count, 2);
		}

		// The pile is an ordinary loot target, so it comes back the same way
		// anything else does.
		H.Eq("the index space covers the floor too", w.LootTargetCount,
			w.Guards.Count + w.Chests.Count + w.Ground.Count);
		H.Eq("and the pile is the nearest target",
			w.NearestLootTarget(), w.Guards.Count + w.Chests.Count);

		int onFloor = w.Ground[0].Kit.Count;
		w.Step(new InputFrame(0, 0, 0, 0, 1));
		H.Eq("picking one back up takes it off the floor",
			w.Ground[0].Kit.Count, onFloor - 1);

		// Refusals, none of which may corrupt anything. Compared on the pack and
		// the floor, not the whole hash: a tick always advances the world.
		int packBefore = w.Pack.UsedCells();
		int pilesBefore = w.Ground.Count;
		w.Step(new InputFrame(0, 0, 0, 0, 0, -1, 250));
		H.Eq("dropping a placement that does not exist moves nothing",
			w.Pack.UsedCells(), packBefore);
		H.Eq("and adds no pile", w.Ground.Count, pilesBefore);
		H.NoThrow("and does not throw",
			() => w.Step(new InputFrame(0, 0, 0, 0, 0, -1, 255)));

		// Dead players drop nothing.
		var dead = new SimWorld(Level.FromText(Text(g)), 7,
			new Loadout(WeaponId.Glock, ArmourId.None, backpack: 503));
		dead.Player.X = dead.Chests[0].X;
		dead.Player.Y = dead.Chests[0].Y;
		dead.Step(new InputFrame(0, 0, 0, 0));
		dead.Step(new InputFrame(0, 0, 0, 0, 1));
		dead.Player.Alive = false;
		int deadPi = -1;
		for (int i = 0; i < dead.Pack.Capacity; i++)
			if (dead.Pack.IsLive(i)) { deadPi = i; break; }
		dead.Step(new InputFrame(0, 0, 0, 0, 0, -1, deadPi + 1));
		H.Eq("a dead player drops nothing", dead.Ground.Count, 0);

		// The drop is recorded, so a run containing one replays exactly.
		string level = Program.ReadLevel("substation_4.txt");
		var kit = new Loadout(WeaponId.Glock, ArmourId.None, backpack: 503);
		// The loadout has to go in the replay or Verify replays with a default
		// one, a different pack size, and diverges for a reason that has
		// nothing to do with dropping.
		var rec = new Replay { Seed = 31, LevelText = level, Loadout = kit };
		var live = new SimWorld(Level.FromText(level), 31, kit);
		for (int i = 0; i < 180; i++)
		{
			var f = new InputFrame(1, 0, i * 311, 0, i % 40 == 5 ? 1 : 0, -1,
				i % 60 == 30 ? 1 : 0);
			live.Step(f);
			rec.Inputs.Add(f);
			if ((i + 1) % Replay.HashEvery == 0) rec.AddHash(i + 1, live.StateHash());
		}
		var div = rec.Verify();
		H.Check("a run containing drops verifies", !div.Found,
			div.Found ? $"diverged at {div.Tick}" : "");

		var back = Replay.FromText(rec.ToText());
		bool kept = back.Inputs.Count == rec.Inputs.Count;
		for (int i = 0; i < back.Inputs.Count && kept; i++)
			if (back.Inputs[i].DropPick != rec.Inputs[i].DropPick) kept = false;
		H.Check("and the drop token round-trips through the text", kept);

		// A replay written before drops existed carries no d token and must
		// still parse as dropping nothing.
		var old = Replay.FromText(
			"seed: 1\nlevel:\n" + level + "\nendlevel\nframes:\n1 0 0 0\n");
		H.Eq("an old frame drops nothing", old.Inputs[0].DropPick, 0);
	}

	/// <summary>
	/// The developer menu's mid-run spawn (InputFrame.SpawnItem).
	///
	/// The point of the field is that a conjured item is RECORDED, so the two
	/// things worth pinning are that it obeys the pack's geometry exactly like
	/// a looted item — conjuring gear is allowed, conjuring ROOM is not — and
	/// that a replay carrying an `s` token reproduces it.
	/// </summary>
	private static void Spawning()
	{
		H.Group("developer spawn");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');

		var w = new SimWorld(Level.FromText(Text(g)), 11,
			new Loadout(WeaponId.Glock, ArmourId.None, backpack: 503));

		H.Eq("fixture: the pack starts empty", w.Pack.UsedCells(), 0);

		w.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 101));
		H.Check("spawning puts the item in the pack", w.Pack.UsedCells() > 0);
		H.Eq("and says so", CountKind(w, SimEventKind.Spawned), 1);

		int after = w.Pack.UsedCells();
		w.Step(new InputFrame(0, 0, 0, 0));
		H.Eq("a frame with no spawn conjures nothing", w.Pack.UsedCells(), after);

		// Total, like the parsers: an id that is not in the catalogue is
		// ignored rather than fatal.
		w.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 4242));
		H.Eq("an id that does not exist is ignored", w.Pack.UsedCells(), after);
		H.Eq("and is not reported as a spawn", CountKind(w, SimEventKind.Spawned), 0);

		// Geometry is NOT negotiable. Fill the pack, then ask for more.
		int guard = 0;
		while (w.Pack.WouldFit(109) && guard++ < 64)
			w.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 109));
		int full = w.Pack.UsedCells();
		w.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 109));
		H.Eq("a full pack refuses the spawn", w.Pack.UsedCells(), full);
		H.Eq("and reports the pack is full", CountKind(w, SimEventKind.PackFull), 1);

		// A finished run takes nothing more.
		var w2 = new SimWorld(Level.FromText(Text(g)), 11,
			new Loadout(WeaponId.Glock, ArmourId.None, backpack: 503));
		w2.Player.Alive = false;
		int dead = w2.Pack.UsedCells();
		w2.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 101));
		H.Eq("a dead player conjures nothing", w2.Pack.UsedCells(), dead);

		// ---- and it survives a replay, which is the whole reason for the field
		var rep = new Replay
		{
			LevelText = Text(g),
			Seed = 11,
			Loadout = new Loadout(WeaponId.Glock, ArmourId.None, backpack: 503),
		};
		for (int i = 0; i < 12; i++)
			rep.Inputs.Add(new InputFrame(0, 0, 0, 0, 0, -1, 0, i == 4 ? 101 : 0));

		string text = rep.ToText();
		H.Check("the replay writes an s token", text.Contains(" s101"));

		var back = Replay.FromText(text);
		H.Eq("and reads it back", back.Inputs.Count, rep.Inputs.Count);
		H.Eq("on the tick it was made", back.Inputs[4].SpawnItem, 101);
		H.Eq("and nowhere else", back.Inputs[5].SpawnItem, 0);

		// A run-length encoder that ignored SpawnItem would fold tick 4 into the
		// run around it and lose the spawn entirely.
		H.Eq("a spawn breaks the run-length encoding", back.Inputs[3].SpawnItem, 0);

		var live = new SimWorld(Level.FromText(Text(g)), 11,
			new Loadout(WeaponId.Glock, ArmourId.None, backpack: 503));
		for (int i = 0; i < back.Inputs.Count; i++) live.Step(back.Inputs[i]);
		H.Check("replaying the recording conjures the item again",
			live.Pack.UsedCells() > 0);
	}

	private static int CountKind(SimWorld w, SimEventKind kind)
	{
		int n = 0;
		for (int i = 0; i < w.Log.Events.Count; i++) if (w.Log.Events[i].Kind == kind) n++;
		return n;
	}

	/// <summary>
	/// Putting gear on MID-MISSION (InputFrame.EquipPick).
	///
	/// The rule worth defending is that an equip is a TRADE, never a gain: what
	/// comes off goes into the pack, and if it will not fit, nothing moves at
	/// all. The other is that the right magazine is cleared — Mag belongs to the
	/// weapon in hand, MagStowed to the other, and all four combinations of
	/// (which slot, which hand) have to land on the correct one.
	/// </summary>
	private static void Equipping()
	{
		H.Group("equipping in the field");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');

		// A large pack, a Glock in hand, no holster.
		SimWorld Fresh() => new SimWorld(Level.FromText(Text(g)), 5,
			new Loadout(WeaponId.Glock, ArmourId.None, backpack: 503));

		var w = Fresh();
		w.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 102));   // an AK into the pack
		int pi = FirstPlacement(w, out int carried);
		H.Eq("fixture: an AK is in the pack", carried, 102);
		H.Eq("fixture: the Glock is in hand", (int)w.Loadout.Held, (int)WeaponId.Glock);

		int held = LiveCount(w);
		w.Step(Equip(pi, GearSlot.Primary));
		H.Eq("equipping the AK puts it in hand", (int)w.Loadout.Held, (int)WeaponId.Ak47);
		H.Eq("and says so", CountKind(w, SimEventKind.Equipped), 1);
		H.Eq("the magazine comes empty", w.Player.Mag, 0);

		// The trade: the Glock is now in the pack, and the pack has not grown.
		bool glockBack = false;
		for (int i = 0; i < w.Pack.Capacity; i++)
			if (w.Pack.IsLive(i) && w.Pack.ItemOf(i) == 100) glockBack = true;
		H.Check("the Glock it displaced is in the pack", glockBack);
		// One item in, one item out. NOT the same number of CELLS -- an AK is
		// 4x2 and a Glock 2x2, so a trade legitimately changes how full the
		// pack is; what it must never do is change how many things are in it.
		H.Eq("one item went in and one came out", LiveCount(w), held);
		bool akGone = true;
		for (int i = 0; i < w.Pack.Capacity; i++)
			if (w.Pack.IsLive(i) && w.Pack.ItemOf(i) == 102) akGone = false;
		H.Check("and the AK is no longer in the pack", akGone);

		// ---- a trade that will not fit is refused WHOLE ----
		var w2 = new SimWorld(Level.FromText(Text(g)), 5,
			new Loadout(WeaponId.Vulcan, ArmourId.None, backpack: 501));
		// A satchel is 4x3. A Welrod is 2x2; the Vulcan it would displace is 5x3
		// and cannot fit a satchel at all.
		w2.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 105));
		int pi2 = FirstPlacement(w2, out int small);
		H.Eq("fixture: a Welrod is in the small pack", small, 105);
		int before2 = w2.Pack.UsedCells();
		w2.Step(Equip(pi2, GearSlot.Primary));
		H.Eq("a displaced weapon that will not fit refuses the equip",
			(int)w2.Loadout.Held, (int)WeaponId.Vulcan);
		H.Eq("and the item stays in the pack exactly as it was",
			w2.Pack.UsedCells(), before2);
		H.Eq("and it is reported as the pack being full",
			CountKind(w2, SimEventKind.PackFull), 1);

		// ---- an EMPTY holster displaces nothing ----
		var w3 = Fresh();
		w3.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 102));
		int pi3 = FirstPlacement(w3, out _);
		w3.Step(Equip(pi3, GearSlot.Secondary));
		H.Check("a weapon can be put in an empty holster", w3.Loadout.HasSecondary);
		H.Eq("which is the one it was given", (int)w3.Loadout.Secondary,
			(int)WeaponId.Ak47);
		H.Eq("the hand is untouched", (int)w3.Loadout.Held, (int)WeaponId.Glock);
		H.Eq("so the held magazine is untouched", w3.Player.Mag,
			WeaponCatalog.Get(WeaponId.Glock).Magazine);
		H.Eq("and the STOWED one is the empty one", w3.Player.MagStowed, 0);

		// ---- armour ----
		var w4 = Fresh();
		w4.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 203));   // heavy plate
		int pi4 = FirstPlacement(w4, out _);
		H.Eq("fixture: no vest to start", (int)w4.Loadout.Armour, (int)ArmourId.None);
		w4.Step(Equip(pi4, GearSlot.Vest));
		H.Eq("a vest can be put on", (int)w4.Loadout.Armour, (int)ArmourId.HeavyPlate);
		H.Eq("and it comes whole", w4.Player.Armour,
			ArmourCatalog.Get(ArmourId.HeavyPlate).Armour);

		// ---- what the field refuses ----
		var w5 = Fresh();
		w5.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 503));   // a large pack
		int pi5 = FirstPlacement(w5, out _);
		int pack5 = w5.Loadout.Backpack;
		w5.Step(Equip(pi5, GearSlot.Backpack));
		H.Eq("a backpack cannot be changed in the field", w5.Loadout.Backpack, pack5);
		H.Eq("and nothing is reported", CountKind(w5, SimEventKind.Equipped), 0);

		var w6 = Fresh();
		w6.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 301));   // a red dot
		int pi6 = FirstPlacement(w6, out _);
		w6.Step(Equip(pi6, GearSlot.Primary));
		H.Eq("an attachment is not a weapon", (int)w6.Loadout.Held,
			(int)WeaponId.Glock);

		// Armour into a weapon slot, and a weapon into the vest.
		var w7 = Fresh();
		w7.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 203));
		int pi7 = FirstPlacement(w7, out _);
		w7.Step(Equip(pi7, GearSlot.Primary));
		H.Eq("a vest does not go in a weapon slot", (int)w7.Loadout.Held,
			(int)WeaponId.Glock);
		w7.Step(Equip(pi7, GearSlot.Vest));
		H.Eq("but it does go in the vest", (int)w7.Loadout.Armour,
			(int)ArmourId.HeavyPlate);

		// A dead player equips nothing.
		var w8 = Fresh();
		w8.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 102));
		int pi8 = FirstPlacement(w8, out _);
		w8.Player.Alive = false;
		w8.Step(Equip(pi8, GearSlot.Primary));
		H.Eq("a dead player equips nothing", (int)w8.Loadout.Held,
			(int)WeaponId.Glock);

		// ---- the packing, and the replay ----
		H.Eq("an equip pick packs the placement", 
			new InputFrame(0, 0, 0, 0, 0, -1, 0, 0,
				InputFrame.PackEquip(3, (int)GearSlot.Vest)).EquipPlacement, 3);
		H.Eq("and the slot", 
			new InputFrame(0, 0, 0, 0, 0, -1, 0, 0,
				InputFrame.PackEquip(3, (int)GearSlot.Vest)).EquipSlot,
			(int)GearSlot.Vest);
		H.Eq("no equip reads back as no placement",
			new InputFrame(0, 0, 0, 0).EquipPlacement, -1);
		H.Eq("a placement that will not pack yields no equip",
			InputFrame.PackEquip(-1, 0), 0);

		var rep = new Replay
		{
			LevelText = Text(g),
			Seed = 5,
			Loadout = new Loadout(WeaponId.Glock, ArmourId.None, backpack: 503),
		};
		rep.Inputs.Add(new InputFrame(0, 0, 0, 0, 0, -1, 0, 102));
		for (int i = 0; i < 4; i++) rep.Inputs.Add(new InputFrame(0, 0, 0, 0));
		rep.Inputs.Add(Equip(0, GearSlot.Primary));
		for (int i = 0; i < 4; i++) rep.Inputs.Add(new InputFrame(0, 0, 0, 0));

		string text = rep.ToText();
		H.Check("the replay writes an e token", text.Contains(" e"));
		var back = Replay.FromText(text);
		H.Eq("and reads the whole stream back", back.Inputs.Count, rep.Inputs.Count);
		H.Eq("with the equip on its own tick", back.Inputs[5].EquipPick,
			rep.Inputs[5].EquipPick);
		H.Eq("and not smeared over the run", back.Inputs[6].EquipPick, 0);

		var live = new SimWorld(Level.FromText(Text(g)), 5,
			new Loadout(WeaponId.Glock, ArmourId.None, backpack: 503));
		for (int i = 0; i < back.Inputs.Count; i++) live.Step(back.Inputs[i]);
		H.Eq("replaying it arms the player again", (int)live.Loadout.Held,
			(int)WeaponId.Ak47);
	}

	/// <summary>
	/// The slots that were refused in the field until a player found themselves
	/// looting helmets and backpacks with nowhere to put either.
	///
	/// Apparel is inert — no spec reads a helmet — but wearable, because an item
	/// that can be picked up and never worn is just a hole in the pack.
	/// The BACKPACK is the interesting one: it is the container everything else
	/// is in, so it is test-fitted before it is committed, and refused whole
	/// when what is being carried will not fit the new bag.
	/// </summary>
	private static void EquippingTheRest()
	{
		H.Group("equipping apparel and bags");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		string text = Text(g);

		SimWorld With(int backpack) => new SimWorld(Level.FromText(text), 5,
			new Loadout(WeaponId.Glock, ArmourId.None, backpack: backpack));

		// ---- apparel ----
		var w = With(503);
		w.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 402));   // a combat helmet
		int pi = FirstPlacement(w, out int id);
		H.Eq("fixture: a helmet is in the pack", id, 402);
		H.Eq("fixture: nothing is worn on the head", w.Loadout.Helmet, 0);

		w.Step(Equip(pi, GearSlot.Helmet));
		H.Eq("a helmet can be put on in the field", w.Loadout.Helmet, 402);
		H.Eq("and it is reported", CountKind(w, SimEventKind.Equipped), 1);
		H.Eq("and it left the pack", LiveCount(w), 0);

		// Swapping one for another is a trade, like every other equip.
		w.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 401));   // a field cap
		int pi2 = FirstPlacement(w, out _);
		w.Step(Equip(pi2, GearSlot.Helmet));
		H.Eq("a second helmet displaces the first", w.Loadout.Helmet, 401);
		bool displaced = false;
		for (int i = 0; i < w.Pack.Capacity; i++)
			if (w.Pack.IsLive(i) && w.Pack.ItemOf(i) == 402) displaced = true;
		H.Check("and the one it replaced is in the pack", displaced);

		// Apparel goes in ITS slot and no other.
		var w2 = With(503);
		w2.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 602));   // patrol boots
		int pi3 = FirstPlacement(w2, out _);
		w2.Step(Equip(pi3, GearSlot.Helmet));
		H.Eq("boots do not go on your head", w2.Loadout.Helmet, 0);
		w2.Step(Equip(pi3, GearSlot.Footware));
		H.Eq("but they do go on your feet", w2.Loadout.Footware, 602);

		// LEGS, the slot appended for the Kit revamp. Trousers were in the
		// catalogue and the screen offered them the legs box, but Loadout had
		// no field for them: the tick took them out of the pack, logged them as
		// equipped, and WithApparel handed back the loadout unchanged. Gone.
		var wl = With(503);
		wl.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 902));   // cargo trousers
		int piL1 = FirstPlacement(wl, out _);
		wl.Step(Equip(piL1, GearSlot.Legs));
		H.Eq("trousers can be put on in the field", wl.Loadout.Legs, 902);
		wl.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 901));   // work trousers
		int piL2 = FirstPlacement(wl, out _);
		wl.Step(Equip(piL2, GearSlot.Legs));
		H.Eq("a second pair displaces the first", wl.Loadout.Legs, 901);
		bool trousersBack = false;
		for (int i = 0; i < wl.Pack.Capacity; i++)
			if (wl.Pack.IsLive(i) && wl.Pack.ItemOf(i) == 902) trousersBack = true;
		H.Check("and the pair it replaced is in the pack", trousersBack);
		H.Eq("and the legs ride in the loadout text",
			Loadout.FromText(wl.Loadout.ToText()).Legs, 901);

		// ---- the bag ----
		// Small bag, big bag in the pack: the swap must re-grid and keep
		// everything, including the bag that comes off.
		var w3 = With(501);                                   // a 4x3 satchel
		w3.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 503));   // a large pack, 3x3
		int pi4 = FirstPlacement(w3, out int bagId);
		H.Eq("fixture: a large pack is in the satchel", bagId, 503);
		int before = w3.Pack.W * w3.Pack.H;

		w3.Step(Equip(pi4, GearSlot.Backpack));
		H.Eq("a bigger bag can be put on in the field", w3.Loadout.Backpack, 503);
		H.Check("and the pack grew", w3.Pack.W * w3.Pack.H > before,
			$"{before} -> {w3.Pack.W * w3.Pack.H}");
		bool satchelKept = false;
		for (int i = 0; i < w3.Pack.Capacity; i++)
			if (w3.Pack.IsLive(i) && w3.Pack.ItemOf(i) == 501) satchelKept = true;
		H.Check("and the satchel it replaced is now inside it", satchelKept);

		// The pack must still be a valid packing after being re-gridded.
		for (int i = 0; i < w3.Pack.Capacity; i++)
		{
			if (!w3.Pack.IsLive(i)) continue;
			var it = GearCatalog.Get(w3.Pack.ItemOf(i));
			bool turned = w3.Pack.RotOf(i) != PackGrid.Rot0;
			int iw = turned ? it.H : it.W, ih = turned ? it.W : it.H;
			H.Check($"re-gridded item {i} sits inside the new bag",
				w3.Pack.XOf(i) >= 0 && w3.Pack.YOf(i) >= 0
				&& w3.Pack.XOf(i) + iw <= w3.Pack.W
				&& w3.Pack.YOf(i) + ih <= w3.Pack.H);
		}

		// A bag that cannot hold the kit is refused WHOLE.
		var w4 = With(503);                                   // 8x5
		for (int i = 0; i < 4; i++)
			w4.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 104));  // SAWs, 5x3
		w4.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 501));      // a satchel, 2x2
		int held = LiveCount(w4);
		int satchel = -1;
		for (int i = 0; i < w4.Pack.Capacity; i++)
			if (w4.Pack.IsLive(i) && w4.Pack.ItemOf(i) == 501) satchel = i;
		H.Check("fixture: a satchel is in the big pack", satchel >= 0);

		w4.Step(Equip(satchel, GearSlot.Backpack));
		H.Eq("a bag too small for the kit is refused", w4.Loadout.Backpack, 503);
		H.Eq("and nothing is lost to the attempt", LiveCount(w4), held);
		H.Eq("and it says the pack would be full",
			CountKind(w4, SimEventKind.PackFull), 1);

		// Putting on the bag already worn changes nothing and loses nothing.
		var w5 = With(502);
		w5.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 502));
		int pi5 = FirstPlacement(w5, out _);
		int same = LiveCount(w5);
		w5.Step(Equip(pi5, GearSlot.Backpack));
		H.Eq("wearing the bag you already wear is a no-op", w5.Loadout.Backpack, 502);
		H.Eq("and keeps what was in it", LiveCount(w5), same);
	}

	/// <summary>
	/// Fitting an ATTACHMENT in the field. It names its own sub-slot, but it
	/// still has to be dropped on a weapon — the cross-check in Exhaustive
	/// caught the sim accepting a red dot onto the helmet slot because it read
	/// the item and ignored the destination.
	/// </summary>
	private static void FittingInTheField()
	{
		H.Group("fitting attachments in the field");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		string text = Text(g);

		var w = new SimWorld(Level.FromText(text), 5,
			new Loadout(WeaponId.Ak47, ArmourId.None, backpack: 503));
		H.Eq("fixture: nothing is sighted", w.Loadout.Attachment(AttachSlot.Sight), 0);

		w.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 303));   // a scope
		int pi = FirstPlacement(w, out int id);
		H.Eq("fixture: a scope is in the pack", id, 303);

		w.Step(Equip(pi, GearSlot.Primary));
		H.Eq("a scope can be fitted in the field",
			w.Loadout.Attachment(AttachSlot.Sight), GearCatalog.Get(303).SimB);
		H.Eq("and it left the pack", LiveCount(w), 0);
		H.Eq("and it is reported", CountKind(w, SimEventKind.Equipped), 1);

		// A second one in the same sub-slot is a trade, like every other equip.
		w.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 301));   // a red dot
		int pi2 = FirstPlacement(w, out _);
		w.Step(Equip(pi2, GearSlot.Primary));
		H.Eq("a second sight replaces the first",
			w.Loadout.Attachment(AttachSlot.Sight), GearCatalog.Get(301).SimB);
		bool scopeBack = false;
		for (int i = 0; i < w.Pack.Capacity; i++)
			if (w.Pack.IsLive(i) && w.Pack.ItemOf(i) == 303) scopeBack = true;
		H.Check("and the scope it displaced is in the pack", scopeBack);

		// It has to be dropped on a WEAPON.
		var w2 = new SimWorld(Level.FromText(text), 5,
			new Loadout(WeaponId.Ak47, ArmourId.None, backpack: 503));
		w2.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 303));
		int pi3 = FirstPlacement(w2, out _);
		foreach (var wrong in new[] { GearSlot.Helmet, GearSlot.Vest,
			GearSlot.Backpack, GearSlot.Legs, GearSlot.Footware })
		{
			w2.Step(Equip(pi3, wrong));
			H.Eq($"a scope does not fit the {GearCatalog.SlotName(wrong)} slot",
				w2.Loadout.Attachment(AttachSlot.Sight), 0);
		}
		w2.Step(Equip(pi3, GearSlot.Primary));
		H.Eq("but it does fit a weapon", w2.Loadout.Attachment(AttachSlot.Sight),
			GearCatalog.Get(303).SimB);

		// A rail the gun in hand LACKS still holds what the last gun had on it:
		// the rails stay with the hand. Fitting over it has to hand THAT back.
		// It read the masked slot, saw nothing fitted, and overwrote the stock
		// the rifle had left there -- one item in, nothing out, one destroyed.
		var w3 = new SimWorld(Level.FromText(text), 5,
			new Loadout(WeaponId.Ak47, ArmourId.None, stock: 1, backpack: 503));
		w3.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0,
			GearCatalog.WeaponItemId((int)WeaponId.Glock)));
		w3.Step(Equip(FirstPlacement(w3, out _), GearSlot.Primary));
		H.Eq("fixture: a Glock in hand, which takes no stock", (int)w3.Loadout.Held,
			(int)WeaponId.Glock);
		H.Eq("fixture: the rifle's stock is still on the hand's rails",
			w3.Loadout.SetAt(0).Raw(AttachSlot.Stock), 1);
		for (int i = 0; i < w3.Pack.Capacity; i++)          // the AK, out of the way
			if (w3.Pack.IsLive(i)) w3.Pack.Remove(i);
		w3.Step(new InputFrame(0, 0, 0, 0, 0, -1, 0, 352));   // a heavy stock
		w3.Step(Equip(FirstPlacement(w3, out _), GearSlot.Primary));
		H.Eq("fitting over a masked rail fits the new one",
			w3.Loadout.SetAt(0).Raw(AttachSlot.Stock), GearCatalog.Get(352).SimB);
		bool lightBack = false;
		for (int i = 0; i < w3.Pack.Capacity; i++)
			if (w3.Pack.IsLive(i) && w3.Pack.ItemOf(i) == 351) lightBack = true;
		H.Check("and hands back the one the rail was holding", lightBack);
	}

	private static InputFrame Equip(int placement, GearSlot slot)
		=> new InputFrame(0, 0, 0, 0, 0, -1, 0, 0,
			InputFrame.PackEquip(placement, (int)slot));

	private static int LiveCount(SimWorld w)
	{
		int n = 0;
		for (int i = 0; i < w.Pack.Capacity; i++) if (w.Pack.IsLive(i)) n++;
		return n;
	}

	private static int FirstPlacement(SimWorld w, out int itemId)
	{
		for (int i = 0; i < w.Pack.Capacity; i++)
			if (w.Pack.IsLive(i)) { itemId = w.Pack.ItemOf(i); return i; }
		itemId = -1;
		return -1;
	}

	/// <summary>
	/// The pick names a ROW of the kit the panel is SHOWING (loot flow §6.3).
	/// game/ reads GetLootTarget before the tick and draws that target's kit;
	/// the tick used to re-resolve the nearest target AFTER the player's move,
	/// so a step in the same tick could hand the click to the next body over.
	/// </summary>
	private static void LootTargetHolds()
	{
		H.Group("the loot pick takes from the kit on screen");

		var g = Room();
		Put(g, 5, 5, 'C');
		Put(g, 6, 5, '@');
		Put(g, 7, 5, 'C');
		Put(g, GW - 2, GH - 2, 'X');
		var w = new SimWorld(Level.FromText(Text(g)), 11,
			new Loadout(WeaponId.Glock, ArmourId.None, backpack: 503));
		var a = w.Chests[0];
		var b = w.Chests[1];
		H.Check("fixture: the spawn is equidistant from two stocked chests",
			a.Kit.Count > 0 && b.Kit.Count > 0
			&& Fx.Dist(a.X, a.Y, w.Player.X, w.Player.Y)
				== Fx.Dist(b.X, b.Y, w.Player.X, w.Player.Y));
		int shown = w.NearestLootTarget();
		H.Eq("fixture: the tie shows the first chest", shown, w.Guards.Count);

		int aBefore = a.Kit.Count, bBefore = b.Kit.Count;
		// Sprinting toward the SECOND chest while clicking the first row.
		w.Step(new InputFrame(1, 0, 0, 0, 1, InputFrame.TierSprint));
		H.Check("fixture: the step made the other chest the nearer",
			w.NearestLootTarget() == w.Guards.Count + 1);
		H.Eq("the item comes out of the chest that was on screen", a.Kit.Count, aBefore - 1);
		H.Eq("and not out of the one the step moved toward", b.Kit.Count, bBefore);
	}

	public static void Run()
	{
		Scarcity();
		LootTargetHolds();
		Equipping();
		EquippingTheRest();
		FittingInTheField();
		Spawning();
		Dropping();
		Chests();
		LootingAChest();
		Objectives();
		Prices();
	}
}
