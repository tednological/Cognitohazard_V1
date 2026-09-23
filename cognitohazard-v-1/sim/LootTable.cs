using System.Collections.Generic;

namespace Cognitohazard.Sim;

/// <summary>
/// What is lying around on a floor, bought with money rather than rolled from
/// tables.
///
/// ONE rule stocks both chests and bodies: a POINT BUY. A container is handed
/// a budget in the shop's dollars (<see cref="GearCatalog.LootValue"/>) and
/// spends it on items -- a rarity tier chosen by weight, then an affordable
/// item within it -- until nothing more is affordable or it is full. What a
/// floor is worth is therefore a number a designer sets and a player can be
/// told, instead of whatever a stack of percentages happens to average out to.
///
/// - CHESTS share one level budget (<see cref="Level.ChestBudget"/>), split
///   unevenly so some chests are rich and some poor, with what one chest cannot
///   spend rolling on to the next. LUCK, rolled once per run, tilts the rarity
///   weights: the same dollars buy a few rare things or a pile of common ones.
/// - GUARDS each buy their own kit with their own points
///   (<see cref="Level.PointsFor"/>): a gun first, then maybe a vest, which he
///   WEARS, then apparel and attachments. No luck: a guard's kit is his job.
///
/// Every draw is from the caller's DetRng and every scan is by catalogue index,
/// so the same seed stocks the same floor on every machine (spec §3.3).
/// </summary>
public static class LootTable
{
	/// <summary>What a pool may contain, as a mask over GearKind.</summary>
	public const int KindWeapon = 1 << (int)GearKind.Weapon;
	public const int KindArmour = 1 << (int)GearKind.Armour;
	public const int KindAttachment = 1 << (int)GearKind.Attachment;
	public const int KindPack = 1 << (int)GearKind.Pack;
	public const int KindApparel = 1 << (int)GearKind.Apparel;

	/// <summary>What a chest holds: gear worth walking across a floor for. No
	/// apparel -- a chest full of shirts is a chest nobody opens twice.</summary>
	public const int ChestPool = KindWeapon | KindArmour | KindAttachment | KindPack;

	/// <summary>What a guard carries besides his gun and his vest.</summary>
	public const int GuardExtras = KindApparel | KindPack | KindAttachment;

	public const int NeutralLuck = 100;

	/// <summary>This run's luck, percent: two draws summed, so 100 is typical
	/// and the extremes are rare.</summary>
	public static int RollLuck(DetRng rng)
		=> Tune.LuckMin + rng.NextInt(Tune.LuckSpread + 1) + rng.NextInt(Tune.LuckSpread + 1);

	/// <summary>
	/// The weight of one rarity tier at a given luck. Luck above 100 moves
	/// weight up the tiers, below 100 down them, pivoting on Rare, never below a
	/// tenth of the base so nothing becomes impossible.
	/// </summary>
	public static int TierWeight(int tier, int luck)
	{
		int b = Tune.RarityWeight[tier];
		long pct = 100 + (long)(luck - NeutralLuck) * (tier - 2) * Tune.LuckTiltPerTier / 100;
		if (pct < 10) pct = 10;
		return (int)(b * pct);
	}

	/// <summary>
	/// Spend up to <paramref name="budget"/> on items from <paramref name="pool"/>
	/// into <paramref name="into"/>, at most <paramref name="maxItems"/> of them.
	/// Returns what was spent.
	///
	/// One of each item per container, and one per body SLOT for apparel and
	/// packs -- a guard does not wear two helmets, and a chest of four identical
	/// red dots reads as a bug. Items with no loot value (the objective) are
	/// never bought.
	/// </summary>
	public static int Buy(DetRng rng, int budget, int luck, int pool, List<int> into,
		int maxItems)
	{
		int spent = 0;
		var tierW = new int[GearCatalog.RarityCount];

		while (into.Count < maxItems)
		{
			int remaining = budget - spent;

			// PACING. Each pick must cost at least half its fair share of what is
			// left, so a container spends its money rather than filling its slots
			// with the cheapest things first and walking away with most of the
			// budget unspent. When nothing meets the floor it HALVES rather than
			// vanishing: a guard with a fortune and one slot for a gun buys the
			// best gun he can reach, not whatever the common weights land on.
			int floor = remaining / ((maxItems - into.Count) * 2);
			int total = Weigh(tierW, remaining, floor, luck, pool, into);
			while (total == 0 && floor > 0)
			{
				floor /= 2;
				total = Weigh(tierW, remaining, floor, luck, pool, into);
			}
			if (total <= 0) break;

			int roll = rng.NextInt(total);
			int tier = 0;
			while (roll >= tierW[tier]) { roll -= tierW[tier]; tier++; }

			int n = CountAffordable(tier, remaining, floor, pool, into);
			int pick = rng.NextInt(n);
			int id = NthAffordable(tier, remaining, floor, pool, into, pick);
			into.Add(id);
			spent += GearCatalog.LootValue(id);
		}
		return spent;
	}

	/// <summary>Fill the tier weights for what is affordable at or above a
	/// floor, and return their total: zero when nothing qualifies.</summary>
	private static int Weigh(int[] tierW, int remaining, int floor, int luck, int pool,
		List<int> into)
	{
		int total = 0;
		for (int t = 0; t < tierW.Length; t++)
		{
			tierW[t] = CountAffordable(t, remaining, floor, pool, into) > 0 ? TierWeight(t, luck) : 0;
			total += tierW[t];
		}
		return total;
	}

	/// <summary>The cheapest item a pool could ever buy, or 0 for an empty pool.</summary>
	public static int Cheapest(int pool)
	{
		int best = 0;
		for (int i = 0; i < GearCatalog.Count; i++)
		{
			var it = GearCatalog.At(i);
			if (!Eligible(it, pool)) continue;
			int v = GearCatalog.LootValue(it.Id);
			if (best == 0 || v < best) best = v;
		}
		return best;
	}

	/// <summary>The cheapest item in a pool, by catalogue order on ties.</summary>
	public static int CheapestId(int pool)
	{
		int best = 0, bestV = 0;
		for (int i = 0; i < GearCatalog.Count; i++)
		{
			var it = GearCatalog.At(i);
			if (!Eligible(it, pool)) continue;
			int v = GearCatalog.LootValue(it.Id);
			if (best == 0 || v < bestV) { best = it.Id; bestV = v; }
		}
		return best;
	}

	/// <summary>What a list of items is worth, in loot dollars.</summary>
	public static int ValueOf(List<int> items)
	{
		int v = 0;
		for (int i = 0; i < items.Count; i++) v += GearCatalog.LootValue(items[i]);
		return v;
	}

	// ------------------------------------------------------------ chests

	/// <summary>
	/// Stock every supply chest from one budget. Objective sites hold the
	/// objective and draw NOTHING from the stream, so adding one to a level
	/// cannot change what the supply chests hold.
	/// </summary>
	public static void StockChests(DetRng rng, List<ChestRuntime> chests, int budget, int luck)
	{
		var supply = new List<ChestRuntime>();
		for (int i = 0; i < chests.Count; i++)
		{
			if (chests[i].Objective) chests[i].Kit.Add(GearCatalog.ObjectiveId);
			else supply.Add(chests[i]);
		}
		if (supply.Count == 0) return;

		// Uneven shares: each chest draws a weight of 1 to 4, so one chest on a
		// floor is worth four of another. Found treasure is only treasure if
		// the chest next to it had less.
		var weight = new int[supply.Count];
		int sum = 0;
		for (int i = 0; i < supply.Count; i++) { weight[i] = 1 + rng.NextInt(4); sum += weight[i]; }

		int carry = 0, handed = 0;
		for (int i = 0; i < supply.Count; i++)
		{
			// The last chest takes the remainder of the split, so integer division
			// loses no dollars.
			int share = i == supply.Count - 1 ? budget - handed : (int)((long)budget * weight[i] / sum);
			handed += share;
			var kit = supply[i].Kit;
			int spent = Buy(rng, share + carry, luck, ChestPool, kit, Tune.ChestMaxItems);
			carry = share + carry - spent;

			// Never empty: a chest you crossed the floor for and opened to nothing
			// is worse than no chest. The cheapest thing, paid for from what is
			// carried on -- and a budget of zero is the one case where the level
			// is allowed to be generous.
			if (kit.Count == 0)
			{
				int id = CheapestId(ChestPool);
				kit.Add(id);
				carry -= GearCatalog.LootValue(id);
			}
		}

		// What one chest could not spend within its item cap goes round again,
		// so the floor holds its budget rather than losing it to the cap.
		for (int i = 0; i < supply.Count && carry >= Cheapest(ChestPool); i++)
			carry -= Buy(rng, carry, luck, ChestPool, supply[i].Kit, Tune.ChestMaxItems);
	}

	// ------------------------------------------------------------ guards

	/// <summary>
	/// A guard buys his kit: a gun with up to GuardGunSharePct of his points
	/// (the starter sidearm if he cannot afford better, since everyone who
	/// shoots at you carries SOMETHING), then perhaps a vest, which he wears,
	/// then apparel and attachments with the rest.
	/// </summary>
	public static void KitGuard(DetRng rng, Actor g, int points)
	{
		int spent = Buy(rng, points * Tune.GuardGunSharePct / 100, NeutralLuck, KindWeapon, g.Kit, 1);
		if (g.Kit.Count == 0)
		{
			g.Kit.Add(GearCatalog.WeaponItemId((int)WeaponId.Glock));
			spent += GearCatalog.StarterLootValue;
		}

		if (rng.NextInt(100) < Tune.GuardArmourChancePct)
		{
			int before = g.Kit.Count;
			spent += Buy(rng, points - spent, NeutralLuck, KindArmour, g.Kit, g.Kit.Count + 1);
			if (g.Kit.Count > before)
			{
				// One source of truth: the plate he wears IS the plate on his
				// body, from the same catalogue entry.
				var worn = ArmourCatalog.Get(ArmourCatalog.Clamp(GearCatalog.Get(g.Kit[before]).SimA));
				g.Armour = worn.Armour;
				g.ArmourMax = worn.Armour;
			}
		}

		Buy(rng, points - spent, NeutralLuck, GuardExtras, g.Kit, g.Kit.Count + Tune.GuardMaxExtras);
	}

	// ------------------------------------------------------------ helpers

	private static bool Eligible(in GearItem it, int pool)
		=> (pool & (1 << (int)it.Kind)) != 0 && GearCatalog.LootValue(it.Id) > 0;

	private static bool Usable(in GearItem it, int tier, int remaining, int floor, int pool,
		List<int> into)
	{
		if ((int)it.Rarity != tier || !Eligible(it, pool)) return false;
		int v = GearCatalog.LootValue(it.Id);
		if (v > remaining || v < floor) return false;
		for (int k = 0; k < into.Count; k++)
		{
			if (into[k] == it.Id) return false;
			// One per body slot for what is worn; attachments and weapons stack.
			if ((it.Kind == GearKind.Apparel || it.Kind == GearKind.Pack)
				&& GearCatalog.Get(into[k]).Slot == it.Slot
				&& GearCatalog.Get(into[k]).Kind == it.Kind) return false;
		}
		return true;
	}

	private static int CountAffordable(int tier, int remaining, int floor, int pool,
		List<int> into)
	{
		int n = 0;
		for (int i = 0; i < GearCatalog.Count; i++)
			if (Usable(GearCatalog.At(i), tier, remaining, floor, pool, into)) n++;
		return n;
	}

	private static int NthAffordable(int tier, int remaining, int floor, int pool,
		List<int> into, int nth)
	{
		for (int i = 0; i < GearCatalog.Count; i++)
		{
			var it = GearCatalog.At(i);
			if (!Usable(it, tier, remaining, floor, pool, into)) continue;
			if (nth-- == 0) return it.Id;
		}
		return GearCatalog.NoneId;
	}
}
