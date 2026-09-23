using System;
using System.Collections.Generic;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// Loot as money: rarity on every item, chests stocked from a level budget that
/// this run's LUCK spends, and guards who POINT-BUY their kits (sim/LootTable).
/// </summary>
public static class Loot
{
	public static void Run()
	{
		Rarities();
		Budgets();
		Luck();
		PointBuy();
		Format();
	}

	private static readonly string[] Shipped =
		{ "substation_4.txt", "relay_nine.txt", "terminal_twelve.txt",
		  "meridian_glasshouse.txt", "vault_row.txt" };

	private static void Rarities()
	{
		H.Group("loot / rarity");

		// The anchors the tiers were named by.
		void Is(string name, int id, Rarity r)
			=> H.Eq($"{name} is {GearCatalog.RarityName(r)}", (int)GearCatalog.RarityOf(id), (int)r);
		Is("the Glock", 100, Rarity.Common);
		Is("the satchel", 501, Rarity.Common);
		Is("the MP7", 101, Rarity.Uncommon);
		Is("light armour", 201, Rarity.Uncommon);
		Is("the AK-47", 102, Rarity.Rare);
		Is("medium armour", 202, Rarity.Rare);
		Is("the SAW", 104, Rarity.Epic);
		Is("grenades", 111, Rarity.Epic);
		Is("the AWM", 112, Rarity.Legendary);
		Is("heavy armour", 203, Rarity.Legendary);
		Is("the Photon laser", 107, Rarity.Legendary);
		Is("the Arc Lance laser", 108, Rarity.Legendary);

		// Rarer is dearer, on average: a tier whose items were cheaper than the
		// one below it would make luck buy worse things.
		var sum = new long[GearCatalog.RarityCount];
		var n = new int[GearCatalog.RarityCount];
		for (int i = 0; i < GearCatalog.Count; i++)
		{
			var it = GearCatalog.At(i);
			if (it.Kind == GearKind.Objective) continue;
			sum[(int)it.Rarity] += GearCatalog.LootValue(it.Id);
			n[(int)it.Rarity]++;
		}
		bool climbs = true;
		string means = "";
		for (int t = 0; t < n.Length; t++)
		{
			if (n[t] == 0) { climbs = false; continue; }
			means += $"{sum[t] / n[t]} ";
			if (t > 0 && n[t - 1] > 0 && sum[t] / n[t] <= sum[t - 1] / n[t - 1]) climbs = false;
		}
		H.Check("every tier has items, and each costs more on average than the last",
			climbs, means);
		H.Check("nothing that can be found is worth nothing",
			GearCatalog.LootValue(100) == GearCatalog.StarterLootValue
			&& GearCatalog.LootValue(GearCatalog.ObjectiveId) == 0);
	}

	/// <summary>Supply chest value on a world.</summary>
	private static int ChestValue(SimWorld w, out bool allFull)
	{
		int v = 0;
		allFull = true;
		foreach (var c in w.Chests)
		{
			if (c.Objective) continue;
			v += LootTable.ValueOf(c.Kit);
			if (c.Kit.Count < Tune.ChestMaxItems) allFull = false;
		}
		return v;
	}

	private static void Budgets()
	{
		H.Group("loot / the level budget");

		int cheapest = LootTable.Cheapest(LootTable.ChestPool);
		string bad = "";
		foreach (var name in Shipped)
		{
			var L = Level.FromText(Program.ReadLevel(name));
			for (ulong seed = 1; seed <= 40 && bad.Length == 0; seed++)
			{
				var w = new SimWorld(L, seed);
				int v = ChestValue(w, out bool full);
				int budget = L.ChestBudget;
				if (v > budget)
					bad = $"{name} seed {seed}: ${v} spent of ${budget}";
				else if (budget - v >= cheapest && !full)
					bad = $"{name} seed {seed}: ${budget - v} left unspent with room in a chest";
			}
		}
		H.Check("every floor's chests hold its budget, give or take less than one item",
			bad.Length == 0, bad);

		var sub = Level.FromText(Program.ReadLevel("substation_4.txt"));
		H.Eq("an unauthored budget is per supply chest", sub.ChestBudget,
			sub.SupplyChests * Tune.LootPerChest);

		// A budget of nothing still leaves no chest empty.
		var poor = Level.FromText("loot: 0\n" + Program.ReadLevel("substation_4.txt"));
		var pw = new SimWorld(poor, 3);
		bool oneEach = true;
		foreach (var c in pw.Chests)
			if (!c.Objective && c.Kit.Count != 1) oneEach = false;
		H.Check("a floor with no budget still puts one thing in every chest", oneEach);

		// And a rich floor is rich.
		var rich = Level.FromText("loot: 30000\n" + Program.ReadLevel("substation_4.txt"));
		int rv = ChestValue(new SimWorld(rich, 3), out _);
		H.Check("an authored budget is what the chests hold",
			rv > ChestValue(new SimWorld(sub, 3), out _) && rv <= 30000, $"${rv}");
	}

	private static void Luck()
	{
		H.Group("loot / luck");

		var L = Level.FromText(Program.ReadLevel("relay_nine.txt"));
		int lo = int.MaxValue, hi = int.MinValue;
		long luckSum = 0;
		long rarLoN = 0, rarLoSum = 0, rarHiN = 0, rarHiSum = 0;
		long valLo = 0, valLoN = 0, valHi = 0, valHiN = 0;
		const int Runs = 160;
		for (ulong seed = 1; seed <= Runs; seed++)
		{
			var w = new SimWorld(L, seed * 7919);
			lo = Math.Min(lo, w.Luck);
			hi = Math.Max(hi, w.Luck);
			luckSum += w.Luck;
			int v = ChestValue(w, out _);
			foreach (var c in w.Chests)
			{
				if (c.Objective) continue;
				foreach (int id in c.Kit)
				{
					int r = (int)GearCatalog.RarityOf(id);
					if (w.Luck >= 120) { rarHiN++; rarHiSum += r; }
					else if (w.Luck <= 80) { rarLoN++; rarLoSum += r; }
				}
			}
			if (w.Luck >= 120) { valHi += v; valHiN++; }
			else if (w.Luck <= 80) { valLo += v; valLoN++; }
		}

		H.Check("luck stays in its range",
			lo >= Tune.LuckMin && hi <= Tune.LuckMin + 2 * Tune.LuckSpread, $"{lo}..{hi}");
		H.Check("and actually varies run to run", hi - lo >= 40, $"{lo}..{hi}");
		long mean = luckSum / Runs;
		H.Check("typical luck is about even", mean >= 92 && mean <= 108, $"mean {mean}");

		H.Check("fixture: both lucky and unlucky runs occurred", rarHiN > 0 && rarLoN > 0);
		double hiR = rarHiN == 0 ? 0 : (double)rarHiSum / rarHiN;
		double loR = rarLoN == 0 ? 0 : (double)rarLoSum / rarLoN;
		H.Check("a lucky run finds rarer things", hiR > loR + 0.4,
			$"mean tier {hiR:F2} lucky vs {loR:F2} unlucky");

		// Luck changes WHAT, not HOW MUCH: the floor's dollars are the level's.
		double hv = valHiN == 0 ? 0 : (double)valHi / valHiN;
		double lv = valLoN == 0 ? 0 : (double)valLo / valLoN;
		H.Check("but not how much money is on the floor",
			Math.Abs(hv - lv) <= L.ChestBudget * 0.05, $"${hv:F0} lucky vs ${lv:F0} unlucky");

		var a = new SimWorld(L, 42);
		var b = new SimWorld(L, 42);
		H.Eq("the same seed is the same luck", a.Luck, b.Luck);
	}

	private static void PointBuy()
	{
		H.Group("loot / guard point buy");

		string text = Program.ReadLevel("substation_4.txt");
		var L = Level.FromText(text);
		var w = new SimWorld(L, 9);
		bool oneGun = true, withinPoints = true, plateWorn = true;
		string detail = "";
		foreach (var g in w.Guards)
		{
			int guns = 0, worn = 0;
			foreach (int id in g.Kit)
			{
				var it = GearCatalog.Get(id);
				if (it.Kind == GearKind.Weapon) guns++;
				if (it.Kind == GearKind.Armour)
					worn = ArmourCatalog.Get(ArmourCatalog.Clamp(it.SimA)).Armour;
			}
			if (guns != 1) { oneGun = false; detail = $"guard {g.Id} carries {guns} guns"; }
			int value = LootTable.ValueOf(g.Kit);
			if (value > L.PointsFor(g.Id) + GearCatalog.StarterLootValue)
			{ withinPoints = false; detail = $"guard {g.Id}: ${value} of {L.PointsFor(g.Id)}"; }
			if (worn != g.ArmourMax) plateWorn = false;
		}
		H.Check("every guard carries exactly one gun", oneGun, detail);
		H.Check("and spends no more than his points", withinPoints, detail);
		H.Check("the vest he bought is the vest he wears", plateWorn);

		// The ASSIGNMENT: one guard given nothing, one given a fortune. Found by
		// id: Guards is in grid scan order, so index 0 need not be 'a'.
		static Actor A(SimWorld sw) => sw.Guards.Find(x => x.Id == 'a')!;
		var poor = Level.FromText(text + "kit: a 0\n");
		var pg = A(new SimWorld(poor, 9));
		H.Check("a guard with no points carries only a sidearm",
			pg.Kit.Count == 1 && pg.Kit[0] == 100 && pg.ArmourMax == 0,
			string.Join(",", pg.Kit));

		var rich = Level.FromText(text + "kit: a 12000\n");
		int richValue = 0, plainValue = 0;
		for (ulong s = 1; s <= 20; s++)
		{
			richValue += LootTable.ValueOf(A(new SimWorld(rich, s)).Kit);
			plainValue += LootTable.ValueOf(A(new SimWorld(L, s)).Kit);
		}
		H.Check("a guard assigned more points is worth robbing",
			richValue > plainValue * 2, $"${richValue / 20} vs ${plainValue / 20} on average");

		H.Eq("the floor's guard total is the sum of the assignments",
			rich.GuardLootTotal, L.GuardLootTotal - L.PointsFor('a') + 12000);

		var cheap = Level.FromText("guard_loot: 500\n" + text);
		H.Eq("guard_loot sets every guard's points", cheap.GuardLootTotal, 500 * cheap.Guards.Count);
	}

	private static void Format()
	{
		H.Group("loot / level format");

		string text = Program.ReadLevel("substation_4.txt");
		H.Check("a level with no loot lines still round-trips byte for byte",
			Level.FromText(text).ToText() == text);

		var L = Level.FromText(text);
		L.LootBudget = 9000;
		L.GuardLoot = 1200;
		L.KitPoints['c'] = 4000;
		L.Build();
		string saved = L.ToText();
		var back = Level.FromText(saved);
		H.Check("loot, guard_loot and kit lines round-trip",
			back.LootBudget == 9000 && back.GuardLoot == 1200 && back.PointsFor('c') == 4000
			&& back.ToText() == saved);
		H.Eq("and the grid is untouched by them", back.W * back.H, L.W * L.H);

		// Total: garbage clamps or is ignored, and never throws.
		H.NoThrow("absurd amounts clamp", () =>
		{
			var x = Level.FromText("loot: 99999999999999999\nguard_loot: -40\n" + text);
			if (x.LootBudget != Level.MaxLoot || x.GuardLoot != 0)
				throw new Exception($"{x.LootBudget} / {x.GuardLoot}");
		});
		H.NoThrow("malformed kit lines are ignored", () =>
		{
			var x = Level.FromText(text + "kit: zz 5\nkit: a\nkit: 7 100\nkit: b lots\n");
			if (x.KitPoints.Count != 0) throw new Exception($"{x.KitPoints.Count} parsed");
			new SimWorld(x, 1);
		});
	}
}
