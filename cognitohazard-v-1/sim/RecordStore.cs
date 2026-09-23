using System.Collections.Generic;

namespace Cognitohazard.Sim;

public enum RecordState { Intact, Degraded, Gone }

/// <summary>
/// A unit of information: fuel, score, and loss condition at once (spec §2.3).
/// Charge is an integer FRAME count, not a float second count, so the burn
/// ladder replays exactly.
/// </summary>
public sealed class Record
{
	public int NameIndex;              // into RecordStore.Names
	public int Tier;                   // 1..3
	public int Charge;                 // frames remaining in the current stage
	public RecordState State = RecordState.Intact;

	public string Name => RecordStore.Names[NameIndex];

	public void HashInto(ref Hash64 h)
	{
		h.Add(NameIndex); h.Add(Tier); h.Add(Charge); h.Add((int)State);
	}
}

/// <summary>
/// The record economy (spec §2.3, §6). Acquisition order is semantically
/// meaningful — this array is never sorted or compacted, and Gone records stay
/// in it as spent husks so the run's damage stays legible.
/// </summary>
public sealed class RecordStore
{
	public static readonly string[] Names =
	{
		"duty roster", "gate log", "survey plat", "payroll stub", "keycard trace",
		"incident memo", "bonding slip", "sealed writ", "ledger page", "manifest",
		"shift note", "licence stub", "containment plan", "floor plat", "audit trail",
		"assay report", "transfer note", "waiver copy", "custody chain", "seal cert"
	};

	/// <summary>Round-robin cursor, reset per level build so naming is
	/// deterministic and independent of the RNG stream.</summary>
	private int _cursor;

	public readonly List<Record> Held = new();

	/// <summary>Index into <see cref="Held"/> of the record currently being
	/// burned, or -1. Reselected ONLY when the current one becomes Gone — the
	/// selection has to be stable enough to display.</summary>
	public int FuelIndex = -1;

	public int Degraded;
	public int Destroyed;
	public int LostToGunfire;          // sum of tiers burned with a corpse

	public void ResetNaming() => _cursor = 0;

	public Record Mint(int tier)
	{
		var r = new Record
		{
			NameIndex = _cursor % Names.Length,
			Tier = tier,
			Charge = Tune.RecordStageTicks,
			State = RecordState.Intact,
		};
		_cursor++;
		return r;
	}

	public void Take(List<Record> incoming)
	{
		for (int i = 0; i < incoming.Count; i++) Held.Add(incoming[i]);
	}

	/// <summary>
	/// Ruling: ship Newest-first (spec §6.2). The last non-Gone entry burns
	/// first, so cost escalates naturally and late-run looting becomes
	/// self-punishing. Cheapest and Random stay documented but unshipped.
	/// </summary>
	public int PickFuel()
	{
		for (int i = Held.Count - 1; i >= 0; i--)
			if (Held[i].State != RecordState.Gone) return i;
		return -1;
	}

	public bool FuelValid
		=> FuelIndex >= 0 && FuelIndex < Held.Count && Held[FuelIndex].State != RecordState.Gone;

	/// <summary>
	/// Scoring (spec §6.3). Three separate figures, deliberately not collapsed:
	/// Degraded is meant to fail at the filing desk, and that system does not
	/// exist yet, so it needs somewhere to attach.
	/// </summary>
	public void Score(out int provable, out int unprovable, out int destroyed)
	{
		provable = 0; unprovable = 0;
		for (int i = 0; i < Held.Count; i++)
		{
			var r = Held[i];
			if (r.State == RecordState.Intact) provable += r.Tier;
			else if (r.State == RecordState.Degraded) unprovable += r.Tier;
		}
		destroyed = Destroyed + LostToGunfire;
	}

	public void HashInto(ref Hash64 h)
	{
		h.Add(_cursor); h.Add(FuelIndex); h.Add(Degraded); h.Add(Destroyed);
		h.Add(LostToGunfire); h.Add(Held.Count);
		for (int i = 0; i < Held.Count; i++) Held[i].HashInto(ref h);
	}
}
