using System.Collections.Generic;

namespace Cognitohazard.Sim;

/// <summary>
/// EVERYTHING A BODY CARRIES: nine worn entries, the attachments fitted to its
/// weapons, and a pack that is itself a container.
///
/// One structure, used by the player AND by every guard, because they are the
/// same kind of thing — a guard you have put down is a kit lying on the floor,
/// and looting is reading another body's Kit rather than a special list that
/// only bodies have.
///
/// WHY IT IS A CLASS, where Loadout is a readonly struct: a Kit owns a PackGrid,
/// and a pack is a mutable container with identity. Two references to one kit
/// must see the same pack, or dropping a full bag would copy the loot.
///
/// The relationship to Loadout is deliberate layering, not duplication:
///   Kit     = WHAT IS WORN, as GearCatalog item ids. Containers, slots, loot.
///   Loadout = WHAT THAT MEANS, as resolved specs. Damage, spread, speed.
/// `ToLoadout()` derives the second from the first. Nothing in the spec layer
/// changed; it simply stopped being the thing that decides where items live.
/// </summary>
public sealed class Kit
{
	public const int SlotCount = GearCatalog.SlotCount;   // 9

	/// <summary>Item ids by GearSlot, 0 for empty.</summary>
	private readonly int[] _slots = new int[SlotCount];

	/// <summary>Attachment OPTION ids by AttachSlot, 0 for none. One set, applied
	/// to whichever weapon is in hand — the simplification Loadout documents.</summary>
	private readonly int[] _attach = new int[AttachmentCatalog.SlotCount];

	/// <summary>Which weapon is in hand: 0 primary, 1 secondary.</summary>
	public int Active;

	/// <summary>
	/// The worn pack's contents. Sized by whatever is in the Backpack slot, and
	/// EMPTY-BUT-PRESENT when nothing is worn, so callers never have to null
	/// check — a 0x0 grid simply refuses everything, which is the same answer
	/// "no bag" should give.
	/// </summary>
	public readonly PackGrid Pack = new();

	public Kit() { Pack.Resize(0, 0); }

	// ------------------------------------------------------------- the slots

	public int In(GearSlot slot) => _slots[(int)slot];

	public bool Has(GearSlot slot) => _slots[(int)slot] != 0;

	/// <summary>
	/// Put an item in a slot, returning what came out. Does NOT validate that
	/// the item belongs there — the caller decides that, because a guard's roll
	/// and a player's equip answer it differently.
	/// </summary>
	public int Put(GearSlot slot, int itemId)
	{
		int was = _slots[(int)slot];
		_slots[(int)slot] = itemId;
		if (slot == GearSlot.Backpack) ResizePack(itemId);
		return was;
	}

	public int Take(GearSlot slot) => Put(slot, 0);

	/// <summary>
	/// Re-grid the pack for a backpack item. CLEARS IT — the caller is
	/// responsible for re-placing contents, because only the caller knows
	/// whether this is a fresh kit or a swap that has to preserve loot.
	/// </summary>
	private void ResizePack(int itemId)
	{
		if (itemId == 0) { Pack.Resize(0, 0); return; }
		var g = GearCatalog.Get(itemId);
		Pack.Resize(g.PackW, g.PackH);
	}

	// ------------------------------------------------------- the attachments

	public int Attachment(AttachSlot slot) => _attach[(int)slot];

	public void Fit(AttachSlot slot, int optionId)
		=> _attach[(int)slot] = AttachmentCatalog.ClampId(slot, optionId);

	// ----------------------------------------------------------- the weapons

	/// <summary>The slot the weapon in hand is in.</summary>
	public GearSlot HeldSlot => Active == 1 && Has(GearSlot.Secondary)
		? GearSlot.Secondary : GearSlot.Primary;

	public GearSlot StowedSlot => HeldSlot == GearSlot.Primary
		? GearSlot.Secondary : GearSlot.Primary;

	public bool HasSecondary => Has(GearSlot.Secondary);

	/// <summary>Draw the other weapon. A no-op with an empty holster.</summary>
	public void Swap() { if (HasSecondary) Active = Active == 1 ? 0 : 1; }

	// ------------------------------------------------------------ the derived

	/// <summary>
	/// What this kit MEANS, as resolved specs. Built fresh each call rather than
	/// cached: a cache would be one more thing to invalidate every time an item
	/// moved, and this is integer work on nine entries.
	/// </summary>
	public Loadout ToLoadout()
	{
		int primary = _slots[(int)GearSlot.Primary];
		int secondary = _slots[(int)GearSlot.Secondary];
		int vest = _slots[(int)GearSlot.Vest];

		// The sim models no empty-handed state, so a bare primary resolves to
		// the default weapon rather than to nothing.
		int weapon = primary != 0 ? GearCatalog.Get(primary).SimA : 0;
		int armour = vest != 0 ? GearCatalog.Get(vest).SimA : 0;

		return new Loadout(
			WeaponCatalog.Clamp(weapon),
			ArmourCatalog.Clamp(armour),
			_attach[(int)AttachSlot.Sight], _attach[(int)AttachSlot.Grip],
			_attach[(int)AttachSlot.Rail], _attach[(int)AttachSlot.Magazine],
			_attach[(int)AttachSlot.Ammo], _attach[(int)AttachSlot.Stock],
			secondary != 0 ? GearCatalog.Get(secondary).SimA : -1,
			Active,
			_slots[(int)GearSlot.Backpack],
			_slots[(int)GearSlot.Helmet], _slots[(int)GearSlot.Footware],
			_slots[(int)GearSlot.Chest], _slots[(int)GearSlot.Arms]);
	}

	/// <summary>
	/// How full the pack is, in PERCENT. What the looting screen shows, so a
	/// player can tell at a glance whether a body's bag is worth opening.
	/// A kit with no pack reads 0, not 100: nothing carried is not "full".
	/// </summary>
	public int PackFullPercent()
	{
		int cells = Pack.W * Pack.H;
		if (cells <= 0) return 0;
		return (Pack.UsedCells() * 100 + cells / 2) / cells;
	}

	public bool PackEmpty()
	{
		for (int i = 0; i < Pack.Capacity; i++) if (Pack.IsLive(i)) return false;
		return true;
	}

	/// <summary>Everything worn AND everything in the pack. Index order, so the
	/// answer is the same on every machine.</summary>
	public List<int> AllItems()
	{
		var outp = new List<int>();
		for (int i = 0; i < SlotCount; i++) if (_slots[i] != 0) outp.Add(_slots[i]);
		for (int i = 0; i < Pack.Capacity; i++)
			if (Pack.IsLive(i)) outp.Add(Pack.ItemOf(i));
		return outp;
	}

	/// <summary>Nothing worn and nothing carried: a stripped body.</summary>
	public bool Empty()
	{
		for (int i = 0; i < SlotCount; i++) if (_slots[i] != 0) return false;
		return PackEmpty();
	}

	// --------------------------------------------------------------- copying

	public void CopyFrom(Kit other)
	{
		for (int i = 0; i < SlotCount; i++) _slots[i] = other._slots[i];
		for (int i = 0; i < _attach.Length; i++) _attach[i] = other._attach[i];
		Active = other.Active;
		Pack.Resize(other.Pack.W, other.Pack.H);
		// Placement order, not cell order: AutoPlace is deterministic, and
		// replaying the same items in the same order reproduces the packing.
		for (int i = 0; i < other.Pack.Capacity; i++)
			if (other.Pack.IsLive(i))
				Pack.Place(other.Pack.ItemOf(i), other.Pack.XOf(i), other.Pack.YOf(i),
					other.Pack.RotOf(i));
	}

	public void Clear()
	{
		for (int i = 0; i < SlotCount; i++) _slots[i] = 0;
		for (int i = 0; i < _attach.Length; i++) _attach[i] = 0;
		Active = 0;
		Pack.Resize(0, 0);
	}

	// --------------------------------------------------------------- hashing

	public void HashInto(ref Hash64 h)
	{
		for (int i = 0; i < SlotCount; i++) h.Add(_slots[i]);
		for (int i = 0; i < _attach.Length; i++) h.Add(_attach[i]);
		h.Add(Active);
		Pack.HashInto(ref h);
	}
}
