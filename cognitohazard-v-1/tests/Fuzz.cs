using System;
using System.Collections.Generic;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// Bug HUNTING, as opposed to the regression suites beside it.
///
/// Everything else here pins behaviour someone already believed was correct.
/// This drives thousands of RANDOM input streams through the sim and checks, on
/// every single tick, a set of things that must be true of any state the sim can
/// reach — whatever the player did to get there. A regression test asks "does
/// this still do what it did"; these ask "can the sim be put in a state that is
/// wrong at all".
///
/// Failures are reported ONCE per invariant, naming the seed, the tick and the
/// input that produced it, because ten thousand identical FAIL lines are not a
/// bug report. Every run uses fixed seeds, so a failure here is reproducible by
/// re-running the suite rather than by luck.
///
/// The randomness is DetRng — the sim's own generator, used from outside sim/
/// where it is allowed — so the streams are identical on every machine and
/// every .NET version.
/// </summary>
public static class Fuzz
{
	private const int Streams = 240;
	private const int TicksPerStream = 260;

	/// <summary>One violation, kept with enough context to reproduce it.</summary>
	private sealed class Bug
	{
		public string What = "";
		public ulong Seed;
		public long Tick;
		public string Detail = "";
		public override string ToString()
			=> $"seed {Seed} tick {Tick}: {Detail}";
	}

	private static readonly Dictionary<string, Bug> Found = new();

	/// <summary>
	/// Record a violation. The FIRST one for each invariant is kept — the first
	/// is the one with the shortest path to it, and later ones are usually the
	/// same bug still going.
	/// </summary>
	private static void Violation(string what, ulong seed, long tick, string detail)
	{
		if (!Found.ContainsKey(what))
			Found[what] = new Bug { What = what, Seed = seed, Tick = tick, Detail = detail };
	}

	private static void Verdict(string what)
	{
		H.Check(what, !Found.ContainsKey(what),
			Found.TryGetValue(what, out var b) ? b.ToString() : "");
	}

	// ------------------------------------------------------------ the levels

	internal static string LevelsDir()
	{
		var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
		while (d != null && !System.IO.Directory.Exists(
			System.IO.Path.Combine(d.FullName, "levels"))) d = d.Parent;
		return d == null ? "levels" : System.IO.Path.Combine(d.FullName, "levels");
	}

	internal static string[] LevelTexts()
	{
		var names = new[] { "substation_4.txt", "relay_nine.txt", "terminal_twelve.txt",
			"meridian_glasshouse.txt", "vault_row.txt", "vault_row_night.txt", "zz_black_site.txt" };
		var found = new List<string>();
		foreach (var n in names)
		{
			string full = System.IO.Path.Combine(LevelsDir(), n);
			if (System.IO.File.Exists(full)) found.Add(System.IO.File.ReadAllText(full));
		}
		return found.ToArray();
	}

	// ------------------------------------------------------- the input stream

	/// <summary>
	/// A random but PLAUSIBLE tick of input: mostly moving and aiming, with
	/// fire, reload, dilate, subdue, swap and loot sprinkled in, and the three
	/// state-moving fields fired rarely enough that the world survives long
	/// enough to be interesting.
	/// </summary>
	internal static InputFrame RandomInputFor(DetRng r, SimWorld w) => RandomInput(r, w);

	private static InputFrame RandomInput(DetRng r, SimWorld w)
	{
		byte flags = 0;
		if (r.NextInt(4) == 0) flags |= InputFrame.FFire;
		if (r.NextInt(20) == 0) flags |= InputFrame.FReload;
		if (r.NextInt(30) == 0) flags |= InputFrame.FDilate;
		if (r.NextInt(40) == 0) flags |= InputFrame.FSubdue;
		if (r.NextInt(50) == 0) flags |= InputFrame.FSwap;
		if (r.NextInt(6) == 0) flags |= InputFrame.FAim;
		if (r.NextInt(8) == 0) flags |= InputFrame.FLoot;

		int loot = 0;
		if ((flags & InputFrame.FLoot) != 0 && r.NextInt(3) == 0)
			loot = r.NextRange(0, 8);

		int drop = 0;
		if (r.NextInt(60) == 0) drop = r.NextRange(0, w.Pack.Capacity);

		int spawn = 0;
		if (r.NextInt(70) == 0)
			spawn = GearCatalog.At(r.NextInt(GearCatalog.Count)).Id;

		int equip = 0;
		if (r.NextInt(45) == 0)
			equip = InputFrame.PackEquip(r.NextRange(0, w.Pack.Capacity - 1),
				r.NextInt(GearCatalog.SlotCount));

		// Doors: mostly the one in reach, sometimes any panel at all -- glass,
		// or a door across the room -- which the sim must refuse. Drawn only on
		// a level that HAS panels, so the streams on every older level are the
		// same streams they always were.
		// Switches share the index space (after the panels) and the rule, so a
		// level with neither draws exactly the streams it always did.
		int door = 0;
		int uses = w.Panels.Count + w.Switches.Count;
		if (uses > 0 && r.NextInt(10) == 0)
		{
			int near = w.NearestUse();
			door = near >= 0 && r.NextInt(4) != 0 ? near + 1 : r.NextRange(0, uses);
		}

		return new InputFrame(
			r.NextRange(-1, 1), r.NextRange(-1, 1), r.NextBrad(), flags,
			loot, r.NextInt(InputFrame.TierCount), drop, spawn, equip, door);
	}

	// ---------------------------------------------------------- the invariants

	private static long _litTicks;

	private static void CheckState(SimWorld w, ulong seed, int guards, int packCap)
	{
		long t = w.Tick;
		var p = w.Player;

		// --- the player is somewhere real ---
		if (p.X < 0 || p.Y < 0 || p.X > w.Level.WidthFx || p.Y > w.Level.HeightFx)
			Violation("the player never leaves the level", seed, t,
				$"at {p.X},{p.Y} in a {w.Level.WidthFx}x{w.Level.HeightFx} world");

		if (p.Health < 0 || p.Health > Tune.BaseHealth)
			Violation("player health stays in range", seed, t, $"health {p.Health}");

		if (p.Armour < 0 || p.Armour > w.Loadout.ArmourSpec.Armour)
			Violation("player armour never exceeds the vest", seed, t,
				$"armour {p.Armour} of {w.Loadout.ArmourSpec.Armour}");

		if (p.Mag < 0 || p.Mag > w.Loadout.Spec.Magazine)
			Violation("the magazine holds no more than the weapon takes", seed, t,
				$"{p.Mag} of {w.Loadout.Spec.Magazine}");

		if (p.MagStowed < 0)
			Violation("the stowed magazine is never negative", seed, t,
				$"{p.MagStowed}");

		// --- the world keeps its shape ---
		if (w.Guards.Count != guards)
			Violation("guards are never created or destroyed", seed, t,
				$"{w.Guards.Count} of {guards}");

		if (w.Pack.Capacity != packCap)
			Violation("the pack never resizes mid-run", seed, t,
				$"{w.Pack.Capacity} of {packCap}");

		for (int i = 0; i < w.Guards.Count; i++)
		{
			var g = w.Guards[i];
			if (g.Health > Tune.GuardHealth)
				Violation("no guard heals past full", seed, t,
					$"guard {i} at {g.Health}");
			if (g.Armour < 0 || g.Armour > g.ArmourMax)
				Violation("guard armour stays in range", seed, t,
					$"guard {i} armour {g.Armour} of {g.ArmourMax}");

			// --- navigation (Guard_AI.md P0) ---
			if (g.NavX.Count != g.NavY.Count || g.NavIndex < 0
				|| (g.NavX.Count > 0 && g.NavIndex >= g.NavX.Count))
				Violation("a guard's nav path is well formed", seed, t,
					$"guard {i}: {g.NavX.Count}/{g.NavY.Count} points, index {g.NavIndex}");
			// Every waypoint but the last (which is the live goal, wherever
			// that is) is a place a guard can stand.
			for (int k = 0; k + 1 < g.NavX.Count; k++)
				if (Geometry.HitsWall(w.Level.Walls, g.NavX[k], g.NavY[k], Tune.ActorRadius))
				{
					Violation("nav waypoints are places a guard can stand", seed, t,
						$"guard {i} waypoint {k} at {g.NavX[k]},{g.NavY[k]}");
					break;
				}
		}

		// --- postures and the radio (Guard_AI.md P1-P2) ---
		for (int i = 0; i < w.Guards.Count; i++)
		{
			var g = w.Guards[i];
			if (g.Prone) continue;
			if (w.Net.Compromised && (g.State == GuardState.Relaxed || g.State == GuardState.Curious))
				Violation("a compromised level never stands down", seed, t,
					$"guard {i} {g.State}/{g.Task}");
			if (g.RadioMt < 0 || g.RadioMt > Tune.RadioTicks * Actor.Mt
				|| (g.Task == GuardTask.Radio && g.Radio == RadioPurpose.None))
				Violation("radio calls are well formed", seed, t,
					$"guard {i} {g.Task} purpose {g.Radio} at {g.RadioMt}");
			if (g.RouteX.Count != g.RouteY.Count || g.RouteIndex < 0
				|| (g.RouteX.Count > 0 && g.RouteIndex >= g.RouteX.Count)
				|| g.WaitMt < 0 || g.WaitMt > Tune.EtaSyncMaxTicks * Actor.Mt
				|| (g.Task == GuardTask.Assault && g.SquadId < 0))
				Violation("an assault route is well formed", seed, t,
					$"guard {i} {g.Task} route {g.RouteX.Count}/{g.RouteY.Count} at {g.RouteIndex}, wait {g.WaitMt}, squad {g.SquadId}");
			if (g.SquadId >= 0)
			{
				var sq = w.Net.SquadById(g.SquadId);
				if (sq == null || !sq.Members.Contains(i) || g.State != GuardState.Combat)
					Violation("a squad member is a fighting guard on its roll", seed, t,
						$"guard {i} squad {g.SquadId} {g.State}");
			}
		}
		// --- posture and task agree (Guard_AI.md P6) ---
		for (int i = 0; i < w.Guards.Count; i++)
		{
			var g = w.Guards[i];
			if (g.Prone) continue;
			if (!SimWorld.TaskFits(g.State, g.Task)
				|| (g.Task == GuardTask.Radio && g.Radio == RadioPurpose.None))
				Violation("a guard's task fits his posture", seed, t, $"guard {i} {g.State}/{g.Task}/{g.Radio}");
		}
		// The sim can repair a guard injected outside the rules; real play must
		// never need it, or the repair is hiding a bug.
		if (w.Normalised != 0)
			Violation("normal play never needs a guard repaired", seed, t, $"{w.Normalised} repairs");

		// --- the sweep (Guard_AI.md P4) ---
		for (int i = 0; i < w.Guards.Count; i++)
		{
			var g = w.Guards[i];
			if (g.GroupId < 0) continue;
			var grp = w.Net.GroupById(g.GroupId);
			if (g.Prone || g.State != GuardState.Hunting || grp == null || !grp.Members.Contains(i))
				Violation("a sweep group member is a hunting guard on its roll", seed, t,
					$"guard {i} group {g.GroupId} {g.State}");
		}
		for (int gi = 0; gi < w.Net.Groups.Count; gi++)
		{
			var grp = w.Net.Groups[gi];
			bool bad = grp.Members.Count < 1 || grp.Members.Count > 3;
			for (int m = 0; m < grp.Members.Count && !bad; m++)
				if (grp.Members[m] < 0 || grp.Members[m] >= w.Guards.Count
					|| w.Guards[grp.Members[m]].GroupId != grp.Id) bad = true;
			if (bad)
				Violation("sweep groups hold one to three of their own", seed, t,
					$"group {grp.Id}: {string.Join(",", grp.Members)}");
			if (grp.Target >= 0 && (w.Sweep == null || w.Sweep.ClaimedBy[grp.Target] != grp.Id))
				Violation("a group's node is claimed by it and only it", seed, t,
					$"group {grp.Id} target {grp.Target}");
		}
		if (w.Sweep != null)
			for (int k = 0; k < w.Sweep.Count; k++)
			{
				int by = w.Sweep.ClaimedBy[k];
				if (by < 0) continue;
				var grp = w.Net.GroupById(by);
				if (grp == null || grp.Target != k)
					Violation("a group's node is claimed by it and only it", seed, t,
						$"node {k} claimed by {by}");
			}

		if (w.Net.Compromised && w.Alarm.Level != AlarmState.Compromised)
			Violation("a compromised level holds the alarm at 3", seed, t, $"alarm {w.Alarm.Level}");

		if (w.NavSearchesLastTick > Tune.NavSearchesPerTick)
			Violation("the path service keeps to its budget", seed, t,
				$"{w.NavSearchesLastTick} searches, budget {Tune.NavSearchesPerTick}");

		// --- nobody stands inside a shut door or a whole pane ---
		// A door refuses to close on anyone and glass never mends, so the only
		// way a body could end up inside one is a bug in how they block.
		for (int k = 0; k < w.Panels.Count; k++)
		{
			var pn = w.Panels[k];
			if (!pn.Solid) continue;
			if (p.Alive && Geometry.CircleHitsRect(p.X, p.Y, p.Radius, in pn.Rect))
				Violation("nobody stands inside a shut door or a whole pane", seed, t,
					$"the player inside {pn.Kind} {k}");
			for (int i = 0; i < w.Guards.Count; i++)
			{
				var g = w.Guards[i];
				if (Geometry.CircleHitsRect(g.X, g.Y, g.Radius, in pn.Rect))
					Violation("nobody stands inside a shut door or a whole pane", seed, t,
						$"guard {i} inside {pn.Kind} {k}");
			}
		}

		// --- lighting (cognitohazard_lighting_plan.md L7) ---
		if (w.Light != null)
		{
			_litTicks++;
			if (w.PlayerLightQ8 < 0 || w.PlayerLightQ8 > Fx.One)
				Violation("light at the player stays in range", seed, t, $"{w.PlayerLightQ8}");
			// Every sixteenth tick: a fresh build is a full shadowcast.
			if (t % 16 == 0)
			{
				var fresh = w.FreshLight();
				if (fresh == null || !w.Light.SameAs(fresh))
					Violation("the light map always equals a fresh build", seed, t, "diverged");
			}
			for (int k = 0; k < w.Lamps.Count; k++)
				if (w.Lamps[k].Broken && w.Light.LampOn(k))
					Violation("a broken lamp never lights", seed, t, $"lamp {k}");
		}
		else if (w.PlayerLightQ8 != Fx.One)
			Violation("light at the player stays in range", seed, t, "a lit level is not full light");

		// --- the pack is a valid packing ---
		CheckPack(w, seed, t);

		// --- the loadout always names real gear ---
		if ((int)w.Loadout.Held < 0 || (int)w.Loadout.Held >= WeaponCatalog.Count)
			Violation("the held weapon is always a real weapon", seed, t,
				$"held {(int)w.Loadout.Held}");
		if (w.Loadout.Spec.Magazine <= 0)
			Violation("the held weapon always has a magazine", seed, t,
				$"magazine {w.Loadout.Spec.Magazine}");

		// --- the hash is a pure function of the state ---
		if (w.StateHash() != w.StateHash())
			Violation("the state hash is pure", seed, t, "two reads disagreed");
	}

	/// <summary>
	/// The pack is a real packing: every live placement sits inside the grid,
	/// no two overlap, and the cell map agrees with the placement list. A
	/// packing bug is invisible until an item vanishes or two occupy one cell,
	/// which is exactly the sort of thing no regression test would notice.
	/// </summary>
	private static void CheckPack(SimWorld w, ulong seed, long t)
	{
		var pack = w.Pack;
		if (pack.W <= 0 || pack.H <= 0) return;

		var seen = new int[pack.W * pack.H];
		for (int i = 0; i < seen.Length; i++) seen[i] = -1;

		for (int pi = 0; pi < pack.Capacity; pi++)
		{
			if (!pack.IsLive(pi)) continue;
			int itemId = pack.ItemOf(pi);
			if (!GearCatalog.Exists(itemId))
			{
				Violation("every packed item is in the catalogue", seed, t,
					$"placement {pi} holds item {itemId}");
				continue;
			}

			var g = GearCatalog.Get(itemId);
			bool turned = pack.RotOf(pi) != PackGrid.Rot0;
			int iw = turned ? g.H : g.W;
			int ih = turned ? g.W : g.H;
			int x = pack.XOf(pi), y = pack.YOf(pi);

			if (x < 0 || y < 0 || x + iw > pack.W || y + ih > pack.H)
			{
				Violation("no packed item hangs off the grid", seed, t,
					$"placement {pi} ({iw}x{ih}) at {x},{y} in {pack.W}x{pack.H}");
				continue;
			}

			for (int r = y; r < y + ih; r++)
				for (int c = x; c < x + iw; c++)
				{
					int cell = r * pack.W + c;
					if (seen[cell] != -1)
						Violation("no two packed items share a cell", seed, t,
							$"placements {seen[cell]} and {pi} both hold {c},{r}");
					seen[cell] = pi;
				}
		}
	}

	// ------------------------------------------------------------------- runs

	/// <summary>
	/// The main sweep. Every stream is a fresh world and a fresh input stream;
	/// every tick is checked.
	/// </summary>
	private static readonly List<int> _frozenX = new(), _frozenY = new();

	private static void Streaming(string[] levels)
	{
		H.Group("fuzz / reachable states");

		int ticks = 0, swept = 0;
		for (int s = 0; s < Streams; s++)
		{
			ulong seed = 0x9E3779B97F4A7C15UL ^ (ulong)(s * 2654435761L);
			var r = new DetRng(seed);
			var level = Level.FromText(levels[s % levels.Length]);
			var kit = RandomLoadout(r);
			var w = new SimWorld(level, seed, kit);

			int guards = w.Guards.Count;
			int cap = w.Pack.Capacity;

			long lastTick = w.Tick;
			bool wasCompromised = false;
			for (int i = 0; i < TicksPerStream; i++)
			{
				// A compromised level is reachable, but only after a fight, a
				// search and a lost player: far longer than a stream. Every third
				// stream is compromised a quarter of the way in, so the sweep
				// invariants below get random play to check rather than nothing.
				if (s % 3 == 0 && i == TicksPerStream / 4) w.Compromise(w.Player.X, w.Player.Y);

				var input = RandomInput(r, w);
				_frozenX.Clear(); _frozenY.Clear();
				for (int gi = 0; gi < w.Guards.Count; gi++)
				{
					_frozenX.Add(w.Guards[gi].Afraid ? w.Guards[gi].X : int.MinValue);
					_frozenY.Add(w.Guards[gi].Y);
				}
				try
				{
					w.Step(input);
				}
				catch (Exception e)
				{
					Violation("the sim never throws", seed, w.Tick,
						e.GetType().Name + ": " + e.Message);
					break;
				}

				if (w.Tick != lastTick + 1)
					Violation("every Step advances exactly one tick", seed, w.Tick,
						$"{lastTick} -> {w.Tick}");
				lastTick = w.Tick;

				CheckState(w, seed, guards, cap);
				// A guard frozen in fear through a whole tick does not move.
				for (int gi = 0; gi < w.Guards.Count && gi < _frozenX.Count; gi++)
					if (_frozenX[gi] != int.MinValue && w.Guards[gi].Afraid && !w.Guards[gi].Prone
						&& (w.Guards[gi].X != _frozenX[gi] || w.Guards[gi].Y != _frozenY[gi]))
						Violation("a guard frozen in fear does not move", seed, w.Tick, $"guard {gi}");
				if (wasCompromised && !w.Net.Compromised)
					Violation("compromise is permanent", seed, w.Tick, "the level un-compromised itself");
				wasCompromised = w.Net.Compromised;
				if (w.Net.Groups.Count > 0) swept++;
				ticks++;
			}
		}

		H.Check("the fuzzer actually ran", ticks > 10000, $"{ticks} ticks");
		// The sweep invariants below would pass vacuously if no stream ever got
		// a level compromised; this is what makes them worth something.
		H.Check("the fuzzer reaches compromised levels with sweep groups", swept > 0,
			$"{swept} ticks with groups sweeping");
		Verdict("the sim never throws");
		Verdict("every Step advances exactly one tick");
		Verdict("the player never leaves the level");
		Verdict("player health stays in range");
		Verdict("player armour never exceeds the vest");
		Verdict("the magazine holds no more than the weapon takes");
		Verdict("the stowed magazine is never negative");
		Verdict("guards are never created or destroyed");
		Verdict("the pack never resizes mid-run");
		Verdict("no guard heals past full");
		Verdict("guard armour stays in range");
		Verdict("a guard's nav path is well formed");
		Verdict("nav waypoints are places a guard can stand");
		Verdict("the path service keeps to its budget");
		Verdict("a compromised level never stands down");
		Verdict("radio calls are well formed");
		Verdict("a squad member is a fighting guard on its roll");
		Verdict("a compromised level holds the alarm at 3");
		Verdict("compromise is permanent");
		Verdict("an assault route is well formed");
		Verdict("a sweep group member is a hunting guard on its roll");
		Verdict("sweep groups hold one to three of their own");
		Verdict("a group's node is claimed by it and only it");
		Verdict("a guard's task fits his posture");
		Verdict("normal play never needs a guard repaired");
		Verdict("a guard frozen in fear does not move");
		Verdict("every packed item is in the catalogue");
		Verdict("no packed item hangs off the grid");
		Verdict("no two packed items share a cell");
		Verdict("the held weapon is always a real weapon");
		Verdict("the held weapon always has a magazine");
		Verdict("the state hash is pure");
		Verdict("nobody stands inside a shut door or a whole pane");
		Verdict("light at the player stays in range");
		Verdict("the light map always equals a fresh build");
		Verdict("a broken lamp never lights");
		// The lighting invariants would pass vacuously with no dark level in
		// the list; this is what makes them worth something.
		H.Check("the fuzzer reaches levels with lighting", _litTicks > 0, $"{_litTicks} ticks");
	}

	internal static Loadout RandomLoadoutFor(DetRng r) => RandomLoadout(r);

	private static Loadout RandomLoadout(DetRng r)
	{
		int[] packs = { 0, 501, 502, 503 };
		return new Loadout(
			(WeaponId)r.NextInt(WeaponCatalog.Count),
			(ArmourId)r.NextInt(4),
			sight: r.NextInt(4), grip: r.NextInt(4), rail: r.NextInt(4),
			mag: r.NextInt(4), ammo: r.NextInt(4), stock: r.NextInt(3),
			secondary: r.NextInt(3) == 0 ? -1 : r.NextInt(WeaponCatalog.Count),
			active: r.NextInt(2),
			backpack: packs[r.NextInt(packs.Length)]);
	}

	/// <summary>
	/// The same stream, twice, into two worlds — and then through a REPLAY FILE
	/// and back.
	///
	/// Determinism is the property the whole architecture exists to buy, so it
	/// is worth attacking with random streams rather than the one scripted
	/// sequence the regression suite uses. The replay leg is the stronger test:
	/// it makes every input survive being written to text and parsed back, which
	/// is where an unrecorded field shows up as a divergence.
	/// </summary>
	private static void Reproducible(string[] levels)
	{
		H.Group("fuzz / determinism");

		for (int s = 0; s < 40; s++)
		{
			ulong seed = 0xD1B54A32D192ED03UL ^ (ulong)(s * 40503L);
			var gen = new DetRng(seed);
			string levelText = levels[s % levels.Length];
			var kit = RandomLoadout(gen);

			// Build the stream against a throwaway world, so the two runs below
			// see identical inputs rather than inputs that depend on the world.
			var scratch = new SimWorld(Level.FromText(levelText), seed, kit);
			var stream = new List<InputFrame>();
			for (int i = 0; i < 200; i++)
			{
				var f = RandomInput(gen, scratch);
				stream.Add(f);
				scratch.Step(f);
			}

			var a = new SimWorld(Level.FromText(levelText), seed, kit);
			var b = new SimWorld(Level.FromText(levelText), seed, kit);
			for (int i = 0; i < stream.Count; i++)
			{
				a.Step(stream[i]);
				b.Step(stream[i]);
				if (a.StateHash() != b.StateHash())
				{
					Violation("the same inputs give the same world", seed, a.Tick,
						$"0x{a.StateHash():X16} vs 0x{b.StateHash():X16}");
					break;
				}
			}

			// Now the same stream through the replay format.
			var rep = new Replay { LevelText = levelText, Seed = seed, Loadout = kit };
			rep.Inputs.AddRange(stream);
			Replay back;
			try
			{
				back = Replay.FromText(rep.ToText());
			}
			catch (Exception e)
			{
				Violation("a replay round-trips", seed, 0,
					e.GetType().Name + ": " + e.Message);
				continue;
			}

			if (back.Inputs.Count != stream.Count)
			{
				Violation("a replay keeps every tick", seed, 0,
					$"{back.Inputs.Count} of {stream.Count}");
				continue;
			}

			for (int i = 0; i < stream.Count; i++)
			{
				if (!SameFrame(stream[i], back.Inputs[i]))
				{
					Violation("every input field survives the replay format", seed, i,
						Describe(stream[i]) + "  became  " + Describe(back.Inputs[i]));
					break;
				}
			}

			var c = new SimWorld(Level.FromText(levelText), seed, kit);
			for (int i = 0; i < back.Inputs.Count; i++) c.Step(back.Inputs[i]);
			if (c.StateHash() != a.StateHash())
				Violation("a replayed run ends in the same world", seed, c.Tick,
					$"0x{c.StateHash():X16} vs 0x{a.StateHash():X16}");
		}

		Verdict("the same inputs give the same world");
		Verdict("a replay round-trips");
		Verdict("a replay keeps every tick");
		Verdict("every input field survives the replay format");
		Verdict("a replayed run ends in the same world");
	}

	private static bool SameFrame(in InputFrame a, in InputFrame b)
		=> a.MoveX == b.MoveX && a.MoveY == b.MoveY && a.AimBrad == b.AimBrad
		   && a.Flags == b.Flags && a.LootPick == b.LootPick
		   && a.MoveTier == b.MoveTier && a.DropPick == b.DropPick
		   && a.SpawnItem == b.SpawnItem && a.EquipPick == b.EquipPick
		   && a.DoorPick == b.DoorPick;

	private static string Describe(in InputFrame f)
		=> $"({f.MoveX},{f.MoveY}) aim={f.AimBrad} flags={f.Flags} p={f.LootPick}"
		   + $" m={f.MoveTier} d={f.DropPick} s={f.SpawnItem} e={f.EquipPick} u={f.DoorPick}";

	/// <summary>
	/// Items are CONSERVED. With nothing conjured, every item in the world is
	/// worn, in the pack, on a body, in a chest or on the floor, and looting,
	/// dropping and EQUIPPING only move it between those. An item that
	/// evaporates is invisible to every other test here — the pack stays valid,
	/// the hash stays pure, the run plays on — right up until a player loses a
	/// rifle to a bug.
	///
	/// Equips used to be stripped out here, on the grounds that they change the
	/// count; an equip is a TRADE and changes nothing, once what is worn is
	/// counted as a place items live (cognitohazard_loot_flow.md §7).
	/// </summary>
	private static void Conservation(string[] levels)
	{
		H.Group("fuzz / conservation");
		int moved = 0;

		for (int s = 0; s < 60; s++)
		{
			ulong seed = 0x2545F4914F6CDD1DUL ^ (ulong)(s * 7919L);
			var r = new DetRng(seed);
			var level = Level.FromText(levels[s % levels.Length]);
			// A real backpack, or there is nothing to move items into -- and
			// something IN it. This stream used to start with an empty pack,
			// and in 220 random ticks the player never reached a body or a
			// chest: it looted, dropped and equipped nothing at all, so the
			// invariant held vacuously. Carried gear is what gets dropped,
			// picked back up off the floor and put on.
			var carried = new int[2 + r.NextInt(7)];
			for (int k = 0; k < carried.Length; k++)
				carried[k] = GearCatalog.At(r.NextInt(GearCatalog.Count)).Id;
			var kit = new Loadout((WeaponId)r.NextInt(WeaponCatalog.Count),
				ArmourId.None, backpack: 503).WithCarried(carried);
			var w = new SimWorld(level, seed, kit);

			int start = TotalItems(w);
			for (int i = 0; i < 220; i++)
			{
				// Deliberately NO spawn: conjuring an item is the one thing that
				// legitimately changes the count.
				var f = RandomInput(r, w);
				// RandomInput's equips name a random placement and a random
				// slot, so almost none is an item into a slot it could go in:
				// every trade path was run a handful of times a suite. Most
				// ticks here name a LIVE item and the slot it belongs in
				// instead, so the trades themselves are what gets fuzzed.
				int equip = f.EquipPick;
				if (r.NextInt(6) == 0) equip = PlausibleEquip(r, w);
				var clean = new InputFrame(f.MoveX, f.MoveY, f.AimBrad, f.Flags,
					f.LootPick, f.MoveTier, f.DropPick, 0, equip, f.DoorPick);
				w.Step(clean);
				for (int e = 0; e < w.Log.Events.Count; e++)
				{
					var k = w.Log.Events[e].Kind;
					if (k == SimEventKind.Looted || k == SimEventKind.Dropped
						|| k == SimEventKind.Equipped) moved++;
				}

				int now = TotalItems(w);
				if (now != start)
				{
					Violation("looting, dropping and equipping only MOVE items", seed,
						w.Tick, $"{start} items became {now} after {Describe(clean)}");
					break;
				}
			}
		}

		Verdict("looting, dropping and equipping only MOVE items");
		// Not vacuous: the streams above have to have moved things, or the
		// invariant is true of nothing (it was, for as long as this suite ran).
		H.Check("and the streams really did loot, drop and equip", moved >= 200,
			$"{moved} items moved");
	}

	private static int TotalItems(SimWorld w)
	{
		int n = CarriedItems(w);
		for (int i = 0; i < w.Guards.Count; i++) n += w.Guards[i].Kit.Count;
		for (int i = 0; i < w.Chests.Count; i++) n += w.Chests[i].Kit.Count;
		for (int i = 0; i < w.Ground.Count; i++) n += w.Ground[i].Kit.Count;
		return n;
	}

	/// <summary>A live pack item into the slot it would go in (either hand for
	/// a weapon or an attachment), or 0 with nothing in the pack.</summary>
	private static int PlausibleEquip(DetRng r, SimWorld w)
	{
		int live = 0;
		for (int i = 0; i < w.Pack.Capacity; i++) if (w.Pack.IsLive(i)) live++;
		if (live == 0) return 0;
		int pick = r.NextInt(live);
		for (int i = 0; i < w.Pack.Capacity; i++)
		{
			if (!w.Pack.IsLive(i) || pick-- > 0) continue;
			var item = GearCatalog.Get(w.Pack.ItemOf(i));
			int slot = item.Kind switch
			{
				GearKind.Weapon or GearKind.Attachment => r.NextInt(2) == 0
					? (int)GearSlot.Primary : (int)GearSlot.Secondary,
				GearKind.Armour => (int)GearSlot.Vest,
				GearKind.Pack => (int)GearSlot.Backpack,
				_ => (int)item.Slot,
			};
			return InputFrame.PackEquip(i, slot);
		}
		return 0;
	}

	/// <summary>Every item ON the player: what is worn and what is in the pack.
	/// An equip moves items between the two and must never change the sum.</summary>
	internal static int CarriedItems(SimWorld w)
	{
		int n = WornItems(w.Loadout);
		for (int i = 0; i < w.Pack.Capacity; i++) if (w.Pack.IsLive(i)) n++;
		return n;
	}

	/// <summary>
	/// What is WORN, as a count of items: both weapons, the vest, the bag, the
	/// apparel, and every attachment in either hand's set. RAW, not masked by
	/// the gun: a stock on a rail the gun in hand lacks is still an item the
	/// player owns, and it comes home at settle (stash.reconcile_worn reads the
	/// rails unmasked for the same reason).
	/// </summary>
	internal static int WornItems(in Loadout l)
	{
		int n = 1;                                  // the primary: no unarmed state
		if (l.HasSecondary) n++;
		if (l.Armour != ArmourId.None) n++;
		if (l.Backpack != 0) n++;
		foreach (var slot in new[] { GearSlot.Helmet, GearSlot.Footware, GearSlot.Chest,
			GearSlot.Arms, GearSlot.Legs })
			if (l.ApparelIn(slot) != 0) n++;
		for (int hand = 0; hand < 2; hand++)
			for (int i = 0; i < AttachmentCatalog.SlotCount; i++)
				if (l.SetAt(hand).Raw((AttachSlot)i) != 0) n++;
		return n;
	}

	public static void Run()
	{
		var levels = LevelTexts();
		if (levels.Length == 0)
		{
			H.Check("fuzz found levels to run on", false, "no level files readable");
			return;
		}
		Streaming(levels);
		Reproducible(levels);
		Conservation(levels);
	}
}
