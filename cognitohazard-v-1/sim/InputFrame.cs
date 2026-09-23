namespace Cognitohazard.Sim;

/// <summary>
/// One tick of player intent (spec §3.2). Everything continuous is already
/// quantised: aim is BRAD, not a float. That quantisation at the boundary is
/// what lets a recorded input stream replay bit-exactly.
/// </summary>
public readonly struct InputFrame
{
	public readonly sbyte MoveX;    // -1, 0, +1
	public readonly sbyte MoveY;    // -1, 0, +1
	public readonly ushort AimBrad; // 0..65535
	public readonly byte Flags;

	/// <summary>
	/// Which item to take off the body in reach this tick: 0 for none, else the
	/// index into that guard's kit PLUS ONE.
	/// </summary>
	/// <remarks>
	/// A field of its own rather than more Flags bits, because all eight are
	/// spoken for and an index needs four on its own. This is what lets the
	/// player CHOOSE what to loot: a click names one item, the choice is
	/// recorded here, and the replay reproduces it. Without it, per-item
	/// looting could not survive a replay at all.
	/// </remarks>
	public readonly byte LootPick;

	/// <summary>
	/// Which of the four movement tiers the player is on: 0 stealth, 1 walk,
	/// 2 fast walk, 3 sprint. Its own field for the same reason LootPick is —
	/// all eight Flags bits were already spoken for, and a tier needs two.
	/// </summary>
	public readonly byte MoveTier;

	/// <summary>
	/// Which item to drop on the ground this tick: 0 for none, else the pack
	/// PLACEMENT index plus one.
	///
	/// A field for the same reason LootPick is one. Dropping moves an item out
	/// of the pack and puts it in the world, both of which are sim state that
	/// rides in the replay — so it has to be recorded as intent, not performed
	/// by a menu behind the sim's back.
	/// </summary>
	public readonly byte DropPick;

	/// <summary>
	/// An item to CONJURE into the pack this tick: 0 for none, else a
	/// GearCatalog item id. The developer menu (F8) is the only thing that
	/// sets it.
	///
	/// A debug affordance, but not a debug BACKDOOR: the pack is sim state that
	/// feeds the hash and rides in replays, so an item appearing in it has to
	/// arrive as recorded intent like every other item, or the replay would
	/// diverge the moment it was spawned. Recording it is what makes the menu
	/// safe to use mid-run at all.
	///
	/// ushort, not byte: item ids run past 255 (the objective is 900), so this
	/// carries the ID rather than a catalogue INDEX -- ids are fixed forever,
	/// positions are not.
	/// </summary>
	public readonly ushort SpawnItem;

	/// <summary>
	/// Put something from the pack ON, this tick. 0 for none.
	///
	/// PACKED, because it needs two numbers and Step's argument list is already
	/// the widest thing crossing into GDScript: the low byte is the pack
	/// PLACEMENT index plus one, the high byte is the GearSlot to put it in.
	/// Use PackEquip to build one and EquipPlacement/EquipSlot to read it.
	///
	/// A recorded field for the same reason LootPick and DropPick are. Equipping
	/// changes SimWorld.Loadout, which decides damage, spread, speed and the
	/// size of the magazine — every one of those feeds the hash. A screen that
	/// re-armed the player behind the sim's back would diverge the replay on the
	/// very next shot.
	/// </summary>
	public readonly ushort EquipPick;

	/// <summary>
	/// Open or close a door this tick: 0 for none, else the Level.Panels INDEX
	/// plus one. An edge, one toggle per press of G.
	///
	/// A field for the reason every pick is one: a door is sim state -- it
	/// decides who can walk, see and shoot where -- so opening one has to be
	/// recorded intent or the replay diverges the moment it swings. It names the
	/// panel rather than asking the sim for "the nearest", so the door the prompt
	/// showed is the door that moves; the sim still refuses one out of reach.
	/// ushort because a large floor can hold more than 255 panels.
	/// </summary>
	public readonly ushort DoorPick;

	/// <summary>The pack placement an equip names, or -1 when there is none.</summary>
	public int EquipPlacement => EquipPick == 0 ? -1 : (EquipPick & 0xFF) - 1;

	/// <summary>The GearSlot an equip names. Meaningless when EquipPick is 0.</summary>
	public int EquipSlot => EquipPick >> 8;

	/// <summary>Build an EquipPick. Returns 0 — "no equip" — for anything that
	/// will not fit the packing, rather than silently wrapping.</summary>
	public static int PackEquip(int placement, int slot)
	{
		if (placement < 0 || placement > 254) return 0;
		if (slot < 0 || slot > 255) return 0;
		return ((placement + 1) & 0xFF) | (slot << 8);
	}

	public const byte TierStealth = 0;
	public const byte TierWalk = 1;
	public const byte TierFast = 2;
	public const byte TierSprint = 3;
	public const int TierCount = 4;

	public const byte FFire = 1 << 0;
	/// <summary>
	/// DEPRECATED as an input: MoveTier is authoritative now. Still read when
	/// PARSING, because every replay recorded before tiers existed carries the
	/// player's stance in this bit and nowhere else — a frame with no tier token
	/// and this bit set is a stealth frame, and without it is a walk frame.
	/// That is what lets those replays still verify.
	/// </summary>
	public const byte FSneak = 1 << 1;
	public const byte FDilate = 1 << 2;
	public const byte FSubdue = 1 << 3;   // edge, not level
	public const byte FReload = 1 << 4;   // edge, not level
	public const byte FAim = 1 << 5;      // level: aiming down sights
	public const byte FSwap = 1 << 6;     // edge: draw the other weapon
	public const byte FLoot = 1 << 7;     // level: looking at a fallen guard's kit

	/// <param name="moveTier">
	/// Leave at -1 to derive the tier from the FSneak bit, which is what every
	/// call site written before tiers existed does — and what keeps those call
	/// sites, and those replays, behaving exactly as they did.
	/// </param>
	public InputFrame(int moveX, int moveY, int aimBrad, byte flags, int lootPick = 0,
		int moveTier = -1, int dropPick = 0, int spawnItem = 0, int equipPick = 0,
		int doorPick = 0)
	{
		DoorPick = (ushort)(doorPick < 0 ? 0 : doorPick > 65535 ? 0 : doorPick);
		DropPick = (byte)(dropPick < 0 ? 0 : dropPick > 255 ? 0 : dropPick);
		SpawnItem = (ushort)(spawnItem < 0 ? 0 : spawnItem > 65535 ? 0 : spawnItem);
		EquipPick = (ushort)(equipPick < 0 ? 0 : equipPick > 65535 ? 0 : equipPick);
		MoveX = (sbyte)(moveX < 0 ? -1 : moveX > 0 ? 1 : 0);
		MoveY = (sbyte)(moveY < 0 ? -1 : moveY > 0 ? 1 : 0);
		AimBrad = (ushort)(aimBrad & Brad.Mask);
		Flags = flags;
		LootPick = (byte)(lootPick < 0 ? 0 : lootPick > 255 ? 0 : lootPick);

		int tier = moveTier < 0
			? ((flags & FSneak) != 0 ? TierStealth : TierWalk)
			: moveTier;
		MoveTier = (byte)(tier < 0 || tier >= TierCount ? TierWalk : tier);
	}

	public bool Fire => (Flags & FFire) != 0;
	/// <summary>Moving as quietly as the player can. Derived from the tier, not
	/// from the flag bit, so there is exactly one source of truth.</summary>
	public bool Sneak => MoveTier == TierStealth;
	public bool Dilate => (Flags & FDilate) != 0;
	public bool Subdue => (Flags & FSubdue) != 0;
	public bool Reload => (Flags & FReload) != 0;
	public bool Aim => (Flags & FAim) != 0;
	public bool Swap => (Flags & FSwap) != 0;
	public bool Loot => (Flags & FLoot) != 0;

	public void HashInto(ref Hash64 h)
	{
		h.Add(MoveX); h.Add(MoveY); h.Add(AimBrad); h.Add(Flags); h.Add(LootPick);
		h.Add(MoveTier); h.Add(DropPick); h.Add(SpawnItem); h.Add(EquipPick);
		if (DoorPick != 0) h.Add(DoorPick);
	}
}
