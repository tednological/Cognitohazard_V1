using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Cognitohazard.Sim;

/// <summary>
/// A recorded input stream plus everything needed to reproduce it exactly
/// (spec §4.2): the seed, the level text, and periodic state hashes.
///
/// The level is embedded rather than referenced by name. A replay whose level
/// has since been edited would otherwise replay against different geometry and
/// diverge for reasons that have nothing to do with the bug being chased.
///
/// The hash checkpoints are what make this a debugging tool rather than a
/// recording: replaying a stream against changed sim code reports the FIRST
/// tick where behaviour diverged, which is usually the bug.
///
/// Text format so replays are diffable and version-controlled:
///
///     # cognitohazard replay v1
///     seed: 20260917
///     ticks: 1390
///     hash: 60 7B3E7D932CA4B857
///     level:
///     name: Substation 4
///     ...
///     endlevel
///     frames:
///     0 1 12345 0 x30
/// </summary>
public sealed class Replay
{
	public const int HashEvery = 60;

	/// <summary>The most frames <see cref="FromText"/> will expand, summed over
	/// every run-length token: a day of play at 60 Hz. One line of text can ask
	/// for a billion frames, and a total parser that allocates them is not total;
	/// a replay longer than this plays back its first day and verifies what it
	/// reached.</summary>
	public const int MaxFrames = 60 * 60 * 60 * 24;

	public ulong Seed;
	public string LevelText = "";

	/// <summary>What the player carried. A replay recorded with a different
	/// weapon is a different run, so this has to travel with the inputs.</summary>
	public Loadout Loadout = Loadout.Default;
	public readonly List<InputFrame> Inputs = new();

	/// <summary>Checkpoint ticks and their state hashes, in ascending tick order.
	/// Parallel lists rather than a Dictionary: sim/ forbids iteration over a
	/// hash-ordered collection whose order affects state (§3.3).</summary>
	public readonly List<int> HashTicks = new();
	public readonly List<ulong> HashValues = new();

	public void AddHash(int tick, ulong value)
	{
		HashTicks.Add(tick);
		HashValues.Add(value);
	}

	public bool TryGetHash(int tick, out ulong value)
	{
		for (int i = 0; i < HashTicks.Count; i++)
		{
			if (HashTicks[i] == tick) { value = HashValues[i]; return true; }
			if (HashTicks[i] > tick) break;
		}
		value = 0;
		return false;
	}

	// ----------------------------------------------------------- serialising

	public string ToText()
	{
		var sb = new StringBuilder();
		// Invariant throughout: MoveX/MoveY are negative on every left/up frame,
		// and see Invariant for what the machine's culture made of that.
		var inv = CultureInfo.InvariantCulture;
		sb.Append("# cognitohazard replay v1\n");
		sb.Append(inv, $"seed: {Seed}\n");
		sb.Append(inv, $"ticks: {Inputs.Count}\n");
		sb.Append("loadout: ").Append(Loadout.ToText()).Append('\n');

		for (int i = 0; i < HashTicks.Count; i++)
			sb.Append(inv, $"hash: {HashTicks[i]} {HashValues[i]:X16}\n");

		sb.Append("level:\n");
		sb.Append(LevelText);
		if (!LevelText.EndsWith('\n')) sb.Append('\n');
		sb.Append("endlevel\n");

		sb.Append("frames:\n");
		int f = 0;
		while (f < Inputs.Count)
		{
			int run = 1;
			while (f + run < Inputs.Count && Same(Inputs[f], Inputs[f + run])) run++;
			var fr = Inputs[f];
			sb.Append(inv, $"{fr.MoveX} {fr.MoveY} {fr.AimBrad} {fr.Flags}");
			// Prefixed, and only when set, so it cannot be mistaken for the
			// run-length token and a replay written before looting existed
			// still parses byte for byte.
			if (fr.LootPick != 0) sb.Append(inv, $" p{fr.LootPick}");
			// Same rule: prefixed, and omitted at the default, so a replay
			// recorded before movement tiers existed still round-trips and a
			// walking frame costs no extra bytes. Not omitted when the frame also
			// carries the legacy FSneak bit, from which the parser would derive
			// STEALTH and hand back a different frame.
			if (fr.MoveTier != InputFrame.TierWalk || (fr.Flags & InputFrame.FSneak) != 0)
				sb.Append(inv, $" m{fr.MoveTier}");
			if (fr.DropPick != 0) sb.Append(inv, $" d{fr.DropPick}");
			if (fr.SpawnItem != 0) sb.Append(inv, $" s{fr.SpawnItem}");
			if (fr.EquipPick != 0) sb.Append(inv, $" e{fr.EquipPick}");
			if (fr.DoorPick != 0) sb.Append(inv, $" u{fr.DoorPick}");
			if (run > 1) sb.Append(inv, $" x{run}");
			sb.Append('\n');
			f += run;
		}
		return sb.ToString();
	}

	private static bool Same(in InputFrame a, in InputFrame b)
		=> a.MoveX == b.MoveX && a.MoveY == b.MoveY
		   && a.AimBrad == b.AimBrad && a.Flags == b.Flags
		   && a.LootPick == b.LootPick && a.MoveTier == b.MoveTier
		   && a.DropPick == b.DropPick && a.SpawnItem == b.SpawnItem
		   && a.EquipPick == b.EquipPick && a.DoorPick == b.DoorPick;

	/// <summary>Total parser, like the level parser: bad lines are skipped and a
	/// malformed file yields an empty-but-usable replay rather than an exception.</summary>
	public static Replay FromText(string text)
	{
		var r = new Replay();
		if (text == null) return r;

		var level = new StringBuilder();
		string mode = "";

		foreach (string raw in text.Replace("\r", "").Split('\n'))
		{
			if (mode == "level")
			{
				if (raw.Trim() == "endlevel") { mode = ""; continue; }
				level.Append(raw).Append('\n');
				continue;
			}

			string line = raw.Trim();
			if (line.Length == 0) continue;
			if (line.StartsWith('#')) continue;

			if (line.StartsWith("seed:", System.StringComparison.Ordinal))
			{
				Invariant.TryULong(line.Substring(5).Trim(), out ulong s);
				r.Seed = s;
				continue;
			}
			if (line.StartsWith("ticks:", System.StringComparison.Ordinal)) continue;
			if (line.StartsWith("loadout:", System.StringComparison.Ordinal))
			{
				r.Loadout = Loadout.FromText(line.Substring(8).Trim());
				continue;
			}
			if (line.StartsWith("hash:", System.StringComparison.Ordinal))
			{
				string[] hb = line.Substring(5).Trim()
					.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries);
				if (hb.Length >= 2
					&& Invariant.TryInt(hb[0], out int ht)
					&& Invariant.TryHex(hb[1], out ulong hv))
					r.AddHash(ht, hv);
				continue;
			}
			if (line.StartsWith("level:", System.StringComparison.Ordinal)) { mode = "level"; continue; }
			if (line.StartsWith("frames:", System.StringComparison.Ordinal)) { mode = "frames"; continue; }
			if (mode != "frames") continue;

			string[] bits = line.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries);
			if (bits.Length < 4) continue;
			if (!Invariant.TryInt(bits[0], out int mx)) continue;
			if (!Invariant.TryInt(bits[1], out int my)) continue;
			if (!Invariant.TryInt(bits[2], out int aim)) continue;
			if (!Invariant.TryInt(bits[3], out int flags)) continue;

			// Trailing tokens are order-independent and optional: xN is a run
			// length, pN a loot pick, mN a movement tier, dN a drop, sN a
			// developer spawn, eN an equip, uN a door used. Anything else is
			// ignored rather than fatal, the way the rest of this parser behaves.
			int run = 1;
			int pick = 0;
			int tier = -1;          // -1: derive from the FSneak bit, see InputFrame
			int drop = 0;
			int spawn = 0;
			int equip = 0;
			int door = 0;
			for (int b = 4; b < bits.Length; b++)
			{
				if (bits[b][0] == 'x') Invariant.TryInt(bits[b].Substring(1), out run);
				else if (bits[b][0] == 'p') Invariant.TryInt(bits[b].Substring(1), out pick);
				else if (bits[b][0] == 'm') Invariant.TryInt(bits[b].Substring(1), out tier);
				else if (bits[b][0] == 'd') Invariant.TryInt(bits[b].Substring(1), out drop);
				else if (bits[b][0] == 's') Invariant.TryInt(bits[b].Substring(1), out spawn);
				else if (bits[b][0] == 'e') Invariant.TryInt(bits[b].Substring(1), out equip);
				else if (bits[b][0] == 'u') Invariant.TryInt(bits[b].Substring(1), out door);
			}
			if (run < 1) run = 1;
			if (pick < 0) pick = 0;
			if (drop < 0) drop = 0;
			if (spawn < 0) spawn = 0;
			if (equip < 0) equip = 0;
			if (door < 0) door = 0;

			if (run > MaxFrames - r.Inputs.Count) run = MaxFrames - r.Inputs.Count;

			var fr = new InputFrame(mx, my, aim, (byte)flags, pick, tier, drop, spawn, equip,
				door);
			for (int i = 0; i < run; i++) r.Inputs.Add(fr);
		}

		r.LevelText = level.ToString();
		return r;
	}

	// ------------------------------------------------------------ verifying

	public readonly struct Divergence
	{
		public readonly bool Found;
		public readonly int Tick;
		public readonly ulong Expected, Actual;

		/// <summary>
		/// How many checkpoints were actually compared. ZERO means the verdict
		/// is vacuous, not clean — a replay shorter than the checkpoint interval
		/// carries nothing to check, and reporting that as "OK" is worse than
		/// useless because it is false confidence in a regression detector.
		/// </summary>
		public readonly int Compared;

		public Divergence(bool found, int tick, ulong expected, ulong actual, int compared)
		{ Found = found; Tick = tick; Expected = expected; Actual = actual; Compared = compared; }
	}

	/// <summary>
	/// Replay this stream headlessly and report the first checkpoint whose hash
	/// disagrees with the recording. This is the whole point of the format: it
	/// turns "the game feels different since that change" into a tick number.
	/// </summary>
	public Divergence Verify()
	{
		var level = Level.FromText(LevelText);
		var w = new SimWorld(level, Seed, Loadout);

		int compared = 0;
		for (int i = 0; i < Inputs.Count; i++)
		{
			w.Step(Inputs[i]);
			int tick = i + 1;
			if (!TryGetHash(tick, out ulong want)) continue;
			compared++;
			ulong got = w.StateHash();
			if (got != want) return new Divergence(true, tick, want, got, compared);
		}
		return new Divergence(false, 0, 0, 0, compared);
	}
}
