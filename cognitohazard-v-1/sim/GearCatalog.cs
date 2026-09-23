namespace Cognitohazard.Sim;

/// <summary>
/// The eight equipment slots. This list supersedes rpg_extension_plan.md §4's
/// seven: Helmet, Chest, Arms and Secondary are new, and §4's Optics and Implant
/// are gone. Nothing was lost in the trade -- vision radius already comes from
/// the rail attachment, and the Implant ladder was never built.
///
/// The order is load-bearing. §4's rule still holds: two items touching one stat
/// resolve in slot order, and that order is this enum's.
/// </summary>
public enum GearSlot
{
	Helmet, Vest, Backpack, Footware, Chest, Arms, Primary, Secondary,
	// APPENDED. game/ mirrors these ordinals by hand and they are in save
	// files, so a new slot goes on the END -- inserting one above would
	// silently move every worn item one slot along.
	Legs,
}

/// <summary>How an item reaches the rest of the sim.</summary>
public enum GearKind
{
	/// <summary>Resolves to a WeaponId in Primary or Secondary.</summary>
	Weapon,
	/// <summary>Resolves to an ArmourId in Vest.</summary>
	Armour,
	/// <summary>Fits a weapon sub-slot; SimA is the AttachSlot, SimB the option.</summary>
	Attachment,
	/// <summary>Sizes the mission pack grid. PackW by PackH.</summary>
	Pack,
	/// <summary>Carried and displayed, no stat reader yet.</summary>
	Apparel,

	/// <summary>
	/// What the mission is FOR. Found in the level, carried in the pack, and
	/// handed in at extraction — it never reaches the stash and cannot be
	/// bought or sold. APPENDED: ordinals are mirrored by item_catalog.gd.
	/// </summary>
	Objective,
}

/// <summary>
/// How rare a find an item is: what the loot tables weigh by, and the colour
/// game/ draws its name in (grey, green, blue, purple, orange). Ordinals are
/// mirrored by game/item_catalog.gd: append, never reorder.
/// </summary>
public enum Rarity
{
	Common, Uncommon, Rare, Epic, Legendary,
}

public readonly struct GearItem
{
	public readonly int Id;
	public readonly string Name;

	/// <summary>Footprint in pack cells, unrotated.</summary>
	public readonly int W;
	public readonly int H;

	public readonly GearSlot Slot;
	public readonly GearKind Kind;

	/// <summary>WeaponId, ArmourId, or AttachSlot depending on Kind.</summary>
	public readonly int SimA;
	/// <summary>Attachment option id; 0 for everything else.</summary>
	public readonly int SimB;

	/// <summary>The grid a Pack provides when worn. Zero for everything else.</summary>
	public readonly int PackW;
	public readonly int PackH;

	/// <summary>
	/// What it costs between runs, and what it is worth if you walk out with
	/// one. Here rather than in the campaign layer because a price is a
	/// property of the item, and a second table in GDScript would drift from
	/// this one the first time an item was added.
	/// </summary>
	public readonly int Price;

	/// <summary>How rare a find it is. Drives the loot tables' weights and the
	/// colour of its name, nothing else: rarity is not a stat.</summary>
	public readonly Rarity Rarity;

	public GearItem(int id, string name, int w, int h, GearSlot slot, GearKind kind,
		int simA = 0, int simB = 0, int packW = 0, int packH = 0, int price = 0,
		Rarity rarity = Rarity.Common)
	{
		Id = id; Name = name; W = w; H = h;
		Slot = slot; Kind = kind; SimA = simA; SimB = simB;
		PackW = packW; PackH = packH; Price = price; Rarity = rarity;
	}

	public int Cells => W * H;
}

/// <summary>
/// Every item in the game, as integer data. This is the AUTHORITY: the mission
/// pack is sim state and feeds the state hash, so footprints had to move in here
/// where they can be hashed rather than living in a presentation script.
///
/// game/item_catalog.gd mirrors this table for the between-missions screens, and
/// tests/inventory_check.gd asserts the mirror still matches, so drift fails
/// loudly instead of quietly mislabelling gear.
///
/// FOOTPRINTS AND PACK SIZES ARE NEW NUMBERS. Nothing in the port spec sizes an
/// item -- the browser build had no inventory -- so these are invented and meant
/// to be tuned. Every other figure in sim/ is ported; these are not.
///
/// Lookup is a linear scan by index over a small constant table. A dictionary
/// would be faster and would also be a hash-ordered collection, which spec §3.3
/// forbids anywhere its order could reach state.
/// </summary>
public static class GearCatalog
{
	/// <summary>Every slot a body has. NINE since Legs was appended.</summary>
	public const int SlotCount = 9;

	/// <summary>Id reserved for "nothing here".</summary>
	public const int NoneId = 0;

	private static readonly GearItem[] Items =
	{
		// ---- primary and secondary weapons (SimA is the WeaponId) ----
		new GearItem(100, "Glock",    2, 2, GearSlot.Primary, GearKind.Weapon, 0, price: 0, rarity: Rarity.Common),
		new GearItem(101, "MP7",    3, 2, GearSlot.Primary, GearKind.Weapon, 1, price: 900, rarity: Rarity.Uncommon),
		new GearItem(102, "AK-47",    4, 2, GearSlot.Primary, GearKind.Weapon, 2, price: 1400, rarity: Rarity.Rare),
		new GearItem(103, "Remington",  4, 2, GearSlot.Primary, GearKind.Weapon, 3, price: 1200, rarity: Rarity.Rare),
		new GearItem(104, "SAW",    5, 3, GearSlot.Primary, GearKind.Weapon, 4, price: 2600, rarity: Rarity.Epic),

		// APPENDED weapons. Footprints are what they cost you in a pack: the
		// Vulcan at 5x3 will not fit a satchel at all, which is the first thing
		// carrying one has to make you decide.
		new GearItem(105, "Welrod",   2, 2, GearSlot.Primary, GearKind.Weapon, 5, price: 700, rarity: Rarity.Uncommon),
		new GearItem(106, "VSS",      4, 2, GearSlot.Primary, GearKind.Weapon, 6, price: 1900, rarity: Rarity.Epic),
		new GearItem(107, "Photon",   3, 2, GearSlot.Primary, GearKind.Weapon, 7, price: 2200, rarity: Rarity.Legendary),
		new GearItem(108, "Arc Lance",5, 2, GearSlot.Primary, GearKind.Weapon, 8, price: 3400, rarity: Rarity.Legendary),
		new GearItem(109, "Vulcan",   5, 3, GearSlot.Primary, GearKind.Weapon, 9, price: 4200, rarity: Rarity.Legendary),

		// The specialists. A bandolier of three grenades is the smallest weapon
		// in the game; the AWM is as long as the Arc Lance and nearly as dear.
		new GearItem(110, "Tesla",    4, 2, GearSlot.Primary, GearKind.Weapon, 10, price: 3800, rarity: Rarity.Legendary),
		new GearItem(111, "Frag",     2, 1, GearSlot.Primary, GearKind.Weapon, 11, price: 650, rarity: Rarity.Epic),
		new GearItem(112, "AWM",      5, 2, GearSlot.Primary, GearKind.Weapon, 12, price: 2800, rarity: Rarity.Legendary),

		// ---- vest (SimA is the ArmourId; armour 0 is "unarmoured", not an item) ----
		new GearItem(201, "light weave",  2, 2, GearSlot.Vest, GearKind.Armour, 1, price: 350, rarity: Rarity.Uncommon),
		new GearItem(202, "medium carrier", 3, 2, GearSlot.Vest, GearKind.Armour, 2, price: 800, rarity: Rarity.Rare),
		new GearItem(203, "heavy plate",  3, 3, GearSlot.Vest, GearKind.Armour, 3, price: 1500, rarity: Rarity.Legendary),

		// ---- attachments (SimA is the AttachSlot, SimB the option id) ----
		// Option 0 of each slot is the ABSENCE of an attachment, so it is not an
		// item and never appears here.
		new GearItem(301, "red dot",     1, 1, GearSlot.Primary, GearKind.Attachment, 0, 1, price: 220, rarity: Rarity.Common),
		new GearItem(302, "holographic",   1, 1, GearSlot.Primary, GearKind.Attachment, 0, 2, price: 420, rarity: Rarity.Uncommon),
		new GearItem(303, "scope",       2, 1, GearSlot.Primary, GearKind.Attachment, 0, 3, price: 700, rarity: Rarity.Rare),
		new GearItem(311, "rubber grip",   1, 1, GearSlot.Primary, GearKind.Attachment, 1, 1, price: 180, rarity: Rarity.Common),
		new GearItem(312, "tactical grip",   1, 1, GearSlot.Primary, GearKind.Attachment, 1, 2, price: 300, rarity: Rarity.Uncommon),
		new GearItem(313, "angled grip",   1, 1, GearSlot.Primary, GearKind.Attachment, 1, 3, price: 460, rarity: Rarity.Rare),
		new GearItem(321, "laser",       1, 1, GearSlot.Primary, GearKind.Attachment, 2, 1, price: 260, rarity: Rarity.Uncommon),
		new GearItem(322, "flashlight",     2, 1, GearSlot.Primary, GearKind.Attachment, 2, 2, price: 340, rarity: Rarity.Uncommon),
		new GearItem(323, "foregrip",     1, 1, GearSlot.Primary, GearKind.Attachment, 2, 3, price: 380, rarity: Rarity.Rare),
		new GearItem(331, "extended mag",   1, 2, GearSlot.Primary, GearKind.Attachment, 3, 1, price: 240, rarity: Rarity.Uncommon),
		new GearItem(332, "drum mag",     2, 2, GearSlot.Primary, GearKind.Attachment, 3, 2, price: 520, rarity: Rarity.Rare),
		new GearItem(333, "quick-release",   1, 1, GearSlot.Primary, GearKind.Attachment, 3, 3, price: 300, rarity: Rarity.Rare),
		new GearItem(341, "subsonic",     1, 1, GearSlot.Primary, GearKind.Attachment, 4, 1, price: 400, rarity: Rarity.Uncommon),
		new GearItem(342, "hollow point",   1, 1, GearSlot.Primary, GearKind.Attachment, 4, 2, price: 460, rarity: Rarity.Rare),
		new GearItem(343, "armour piercing", 1, 1, GearSlot.Primary, GearKind.Attachment, 4, 3, price: 640, rarity: Rarity.Epic),
		new GearItem(351, "light stock",   2, 1, GearSlot.Primary, GearKind.Attachment, 5, 1, price: 200, rarity: Rarity.Common),
		new GearItem(352, "heavy stock",   3, 1, GearSlot.Primary, GearKind.Attachment, 5, 2, price: 380, rarity: Rarity.Rare),

		// ---- helmet ----
		new GearItem(401, "field cap",    2, 1, GearSlot.Helmet, GearKind.Apparel, price: 60, rarity: Rarity.Common),
		new GearItem(402, "combat helmet",  2, 2, GearSlot.Helmet, GearKind.Apparel, price: 260, rarity: Rarity.Uncommon),

		// ---- backpack: the ONE apparel slot with a real sim effect, because it
		// ---- sizes the mission pack you loot into.
		new GearItem(501, "satchel",   2, 2, GearSlot.Backpack, GearKind.Pack, 0, 0, 4, 3, price: 180, rarity: Rarity.Common),
		new GearItem(502, "field pack",   3, 2, GearSlot.Backpack, GearKind.Pack, 0, 0, 6, 4, price: 480, rarity: Rarity.Uncommon),
		new GearItem(503, "large pack",   3, 3, GearSlot.Backpack, GearKind.Pack, 0, 0, 8, 5, price: 1050, rarity: Rarity.Rare),

		// ---- footware ----
		new GearItem(601, "canvas shoes", 2, 1, GearSlot.Footware, GearKind.Apparel, price: 40, rarity: Rarity.Common),
		new GearItem(602, "patrol boots", 2, 2, GearSlot.Footware, GearKind.Apparel, price: 140, rarity: Rarity.Uncommon),

		// ---- shirt / chest ----
		new GearItem(701, "fatigues",    2, 2, GearSlot.Chest, GearKind.Apparel, price: 40, rarity: Rarity.Common),
		new GearItem(702, "work shirt",    2, 1, GearSlot.Chest, GearKind.Apparel, price: 30, rarity: Rarity.Common),

		// ---- the objective ----
		//
		// Priced at zero and Kind.Objective, which is what keeps it out of the
		// shop, out of the stash, and out of the fence. It is worth exactly the
		// mission payout it unlocks and nothing else.
		//
		// 2x2 deliberately: it costs real room in a satchel, so the smallest
		// pack makes "carry the objective or carry the loot" a decision.
		new GearItem(900, "sealed case", 2, 2, GearSlot.Chest, GearKind.Objective,
			price: 0, rarity: Rarity.Common),

		// ---- arms ----
		// Legs. The slot nothing filled until a body had one.
		new GearItem(901, "work trousers", 2, 2, GearSlot.Legs, GearKind.Apparel, price: 45, rarity: Rarity.Common),
		new GearItem(902, "cargo trousers", 2, 2, GearSlot.Legs, GearKind.Apparel, price: 110, rarity: Rarity.Uncommon),
		new GearItem(903, "padded greaves", 2, 3, GearSlot.Legs, GearKind.Apparel, price: 240, rarity: Rarity.Rare),

		new GearItem(801, "work gloves",  1, 1, GearSlot.Arms, GearKind.Apparel, price: 50, rarity: Rarity.Common),
		new GearItem(802, "armguards",    2, 1, GearSlot.Arms, GearKind.Apparel, price: 120, rarity: Rarity.Uncommon),
	};

	/// <summary>Stands in for any id this build does not know.</summary>
	private static readonly GearItem Unknown =
		new GearItem(NoneId, "unknown item", 1, 1, GearSlot.Primary, GearKind.Apparel);

	public static int Count => Items.Length;

	public static Rarity RarityOf(int id) => Get(id).Rarity;

	public const int RarityCount = 5;

	public static string RarityName(Rarity r) => r switch
	{
		Rarity.Uncommon => "uncommon",
		Rarity.Rare => "rare",
		Rarity.Epic => "epic",
		Rarity.Legendary => "legendary",
		_ => "common",
	};

	/// <summary>
	/// What an item is WORTH as loot: its price, except the not-for-sale starter
	/// sidearm, which is still a gun somebody carried. Loot budgets are spent in
	/// this, so nothing that can be found is free. Zero for the objective, which
	/// is never bought with a budget.
	/// </summary>
	public static int LootValue(int id)
	{
		var it = Get(id);
		if (it.Kind == GearKind.Objective || !Exists(id)) return 0;
		return it.Price > 0 ? it.Price : StarterLootValue;
	}

	/// <summary>The Glock's worth as loot, since its price is "not for sale".</summary>
	public const int StarterLootValue = 250;

	/// <summary>What this item costs to buy, and what it fetches when sold.
	/// Zero means "not for sale" — the starting sidearm.</summary>
	public static int PriceOf(int id) => Get(id).Price;

	/// <summary>The one mission-objective item. A level marks WHERE one is; the
	/// sim decides what it is, so there is one id to check for at extraction.</summary>
	public const int ObjectiveId = 900;

	public static bool IsObjective(int id) => Get(id).Kind == GearKind.Objective;

	/// <summary>The table by position, for callers that want to walk all of it.</summary>
	public static GearItem At(int index)
		=> (index >= 0 && index < Items.Length) ? Items[index] : Unknown;

	public static int IndexOf(int id)
	{
		for (int i = 0; i < Items.Length; i++) if (Items[i].Id == id) return i;
		return -1;
	}

	public static bool Exists(int id) => IndexOf(id) >= 0;

	/// <summary>
	/// The item id that represents a given WeaponId, or 0 if none does.
	///
	/// The reverse of GearItem.SimA, needed because equipping mid-run displaces
	/// the weapon already in hand and it has to go into the pack AS AN ITEM.
	/// Scanned by index, never by a hash-ordered lookup, so the answer is the
	/// same on every machine (spec 3.3).
	/// </summary>
	public static int WeaponItemId(int simA)
	{
		for (int i = 0; i < Items.Length; i++)
			if (Items[i].Kind == GearKind.Weapon && Items[i].SimA == simA)
				return Items[i].Id;
		return 0;
	}

	/// <summary>
	/// The item id for a fitted attachment, or 0 if none represents it.
	/// Option 0 of every slot is the ABSENCE of an attachment, so it has no
	/// item — which is exactly the "nothing was displaced" case.
	/// </summary>
	public static int AttachmentItemId(int attachSlot, int option)
	{
		if (option <= 0) return 0;
		for (int i = 0; i < Items.Length; i++)
			if (Items[i].Kind == GearKind.Attachment
				&& Items[i].SimA == attachSlot && Items[i].SimB == option)
				return Items[i].Id;
		return 0;
	}

	/// <summary>The item id for an ArmourId, or 0 -- including for ArmourId.None,
	/// which is the absence of a vest and so has no item to put in the pack.</summary>
	public static int ArmourItemId(int simA)
	{
		if (simA == 0) return 0;
		for (int i = 0; i < Items.Length; i++)
			if (Items[i].Kind == GearKind.Armour && Items[i].SimA == simA)
				return Items[i].Id;
		return 0;
	}

	/// <summary>
	/// Total, the way Level.FromText and ArmourCatalog.Clamp are total: an id
	/// this build has never heard of resolves to a harmless 1x1 rather than
	/// throwing. A save file or a replay naming removed gear still loads.
	/// </summary>
	public static GearItem Get(int id)
	{
		int i = IndexOf(id);
		return i >= 0 ? Items[i] : Unknown;
	}

	public static string NameOf(int id) => Get(id).Name;

	/// <summary>
	/// The footprint an item covers once turned. A rectangle has only two
	/// distinct footprints, so rot is 0 or 1 and that is the whole rotation
	/// space -- 180 degrees is indistinguishable from 0.
	/// </summary>
	public static void SpanOf(int id, int rot, out int w, out int h)
	{
		var it = Get(id);
		if (rot == PackGrid.Rot90) { w = it.H; h = it.W; }
		else { w = it.W; h = it.H; }
	}

	/// <summary>Whether an item may be worn in a slot. Weapons take either hand.</summary>
	public static bool FitsSlot(int id, GearSlot slot)
	{
		var it = Get(id);
		if (!Exists(id)) return false;
		if (it.Kind == GearKind.Weapon)
			return slot == GearSlot.Primary || slot == GearSlot.Secondary;
		if (it.Kind == GearKind.Attachment) return false;  // fits a weapon, not a body slot
		return it.Slot == slot;
	}

	public static string SlotName(GearSlot slot) => slot switch
	{
		GearSlot.Helmet => "helmet",
		GearSlot.Vest => "vest",
		GearSlot.Backpack => "backpack",
		GearSlot.Footware => "footware",
		GearSlot.Chest => "shirt/chest",
		GearSlot.Arms => "arms",
		GearSlot.Primary => "primary weapon",
		GearSlot.Secondary => "secondary weapon",
		_ => "legs",
	};

}
