using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Cognitohazard.Sim;

namespace Cognitohazard.Game;

/// <summary>
/// The sim/game boundary (spec §1.2). This is the ONLY place where sim types
/// and Godot types meet.
///
/// It owns the SimWorld, takes one InputFrame per physics tick, and flattens the
/// snapshot into packed int arrays. Packed arrays rather than Dictionaries
/// because this crosses the marshalling boundary 60 times a second and a
/// per-field Variant box per guard per frame is exactly the kind of cost that
/// does not show up until the level is full.
///
/// All positions leave here in sim fixed-point (1/256 px). game/ divides by 256.
/// Nothing in here decides game logic; it only reads.
/// </summary>
[GlobalClass]
public partial class SimBridge : RefCounted
{
	public const int Fixed = Fx.One;

	private SimWorld _world = null!;
	private Level _level = null!;
	private SimSnapshot _snap = null!;
	private string _levelText = "";
	private ulong _seed;

	/// <summary>Set by the campaign layer before a mission starts. sim/ never
	/// learns that an inventory exists (RPG plan §1).</summary>
	private Loadout _loadout = Loadout.Default;

	// Recording is always on during live play: a run you have to decide to
	// record before it starts is a run you will not have recorded when
	// something interesting happens.
	private Replay _recording = new();

	private Replay? _playback;
	private int _playbackIndex;
	private int _divergedTick = -1;
	private ulong _divergedExpected, _divergedActual;

	// ---------------------------------------------------------------- setup

	/// <summary>Equip before Load or Restart; unknown ids fall back to the
	/// pistol rather than throwing.</summary>
	public void SetWeapon(int weaponId) => _loadout = _loadout.WithWeapon(weaponId);

	public int CurrentWeaponId => (int)_loadout.Weapon;
	public string CurrentWeaponName => WeaponCatalog.NameOf(_loadout.Weapon);
	public int WeaponCount => WeaponCatalog.Count;
	/// <summary>
	/// A bare weapon's headline figures, for a comparison screen that has not
	/// been built yet and for the harness:
	/// damage, magazine, cooldown, bulletSpeed, gunshotRadius, pierce, spinUp.
	/// </summary>
	public int[] WeaponStats(int weaponId)
	{
		var w = WeaponCatalog.Get(WeaponCatalog.Clamp(weaponId));
		return new[] { w.Damage, w.Magazine, w.FireCooldownTicks, w.BulletSpeed,
			w.GunshotRadius, w.ArmourPierce, w.SpinUpTicks };
	}
	public int MagazineSizeOf(int weaponId)
		=> WeaponCatalog.Get(WeaponCatalog.Clamp(weaponId)).Magazine;
	public int GunshotRadiusOf(int weaponId)
		=> WeaponCatalog.Get(WeaponCatalog.Clamp(weaponId)).GunshotRadius;
	public int PelletsOf(int weaponId)
		=> WeaponCatalog.Get(WeaponCatalog.Clamp(weaponId)).Pellets;

	/// <summary>The one line a rule-breaking weapon needs (arcs, pierces walls,
	/// is thrown), or "" for an ordinary one. Composed in sim/ from the spec and
	/// Tune, so the tooltip never quotes a figure of its own.</summary>
	public string WeaponTraitOf(int weaponId)
		=> WeaponCatalog.TraitOf(WeaponCatalog.Clamp(weaponId));

	public void Load(string levelText, ulong seed)
	{
		_levelText = levelText;
		_seed = seed;
		_level = Level.FromText(levelText);
		_world = new SimWorld(_level, seed, _loadout);
		_snap = _world.Snapshot();
		ResetRecording();
	}

	public void Restart(ulong seed)
	{
		_seed = seed;
		_world = new SimWorld(_level, seed, _loadout);
		_snap = _world.Snapshot();
		_playback = null;
		_playbackIndex = 0;
		_divergedTick = -1;
		ResetRecording();
	}

	private void ResetRecording()
	{
		_recording = new Replay { Seed = _seed, LevelText = _levelText, Loadout = _loadout };
	}

	// ----------------------------------------------------------------- tick

	/// <summary>
	/// Advance exactly one tick from live input, recording as it goes.
	/// `lootPick` is 0 for none, else the index into the kit of the loot target
	/// in reach PLUS ONE -- the item the player clicked. It is recorded with the
	/// rest of the frame, so a replay takes the same thing.
	/// </summary>
	/// <remarks>
	/// The C# default on lootPick does NOT reach GDScript: Godot registers the
	/// method with every parameter required, so a four-argument call from a .gd
	/// file stops resolving the moment a fifth is added and fails at runtime
	/// with "Nonexistent function 'Step'". Every GDScript caller passes every
	/// argument, and editor_check.gd:_check_step_arity counts them at each call
	/// site. The defaults are here for the C# side only.
	/// </remarks>
	/// <param name="moveTier">
	/// 0 stealth, 1 walk, 2 fast walk, 3 sprint. Defaults to -1, which derives
	/// it from the sneak flag — every caller written before tiers existed, the
	/// harnesses included, keeps working unchanged.
	/// </param>
	/// <param name="dropPick">
	/// 0 for none, else the pack PLACEMENT index plus one. Dropping is recorded
	/// intent like looting is; a menu may not move sim state behind the sim's
	/// back or the replay will not reproduce it.
	/// </param>
	/// <param name="doorPick">
	/// 0 for none, else the Level.Panels index of a DOOR plus one: open it if
	/// shut, shut it if open. GetDoorTarget names the one in reach.
	/// </param>
	public void Step(int moveX, int moveY, int aimBrad, int flags, int lootPick = 0,
		int moveTier = -1, int dropPick = 0, int spawnItem = 0, int equipPick = 0,
		int doorPick = 0)
	{
		// Whether the run had already ended BEFORE this tick. The tick that ends
		// it is still recorded; everything after is not.
		bool wasOver = _world.Over != null;

		var f = new InputFrame(moveX, moveY, aimBrad, (byte)flags, lootPick, moveTier,
			dropPick, spawnItem, equipPick, doorPick);
		_world.Step(f);
		_snap = _world.Snapshot();

		// Recording stops when the run does. Otherwise a death screen left open
		// grows the buffer forever, and an F9 afterwards saves a replay that is
		// mostly post-mortem idling rather than the run you wanted to look at.
		if (wasOver) return;

		_recording.Inputs.Add(f);
		int tick = (int)_world.Tick;
		bool periodic = tick % Replay.HashEvery == 0;

		// A closing checkpoint on the tick the run ends, so a run shorter than
		// the checkpoint interval is still verifiable. Without it a 56-tick
		// death recorded nothing to check and verified vacuously.
		bool closing = _world.Over != null;

		if (periodic || closing) _recording.AddHash(tick, _world.StateHash());
	}

	/// <summary>True once the recording has been closed off by the run ending.</summary>
	public bool RecordingClosed => _world.Over != null;

	// -------------------------------------------------------------- replay

	public string GetReplayText() => _recording.ToText();
	public int RecordedTicks => _recording.Inputs.Count;

	public bool IsPlayback => _playback != null;
	public int ReplayLength => _playback?.Inputs.Count ?? 0;
	public int ReplayIndex => _playbackIndex;
	public bool ReplayFinished => _playback != null && _playbackIndex >= _playback.Inputs.Count;

	/// <summary>Tick of the first hash mismatch, or -1. A non-negative value
	/// means the sim no longer reproduces the recorded run.</summary>
	public int DivergedTick => _divergedTick;
	public string DivergedDetail => _divergedTick < 0
		? "" : $"expected {_divergedExpected:X16}, got {_divergedActual:X16}";

	/// <summary>Load a replay and rewind to its first tick. Returns false if the
	/// text carried no frames.</summary>
	public bool LoadReplay(string text)
	{
		var r = Replay.FromText(text);
		if (r.Inputs.Count == 0) return false;

		_playback = r;
		_playbackIndex = 0;
		_divergedTick = -1;
		_levelText = r.LevelText.Length > 0 ? r.LevelText : _levelText;
		_seed = r.Seed;
		// Adopt the recording's loadout: replaying a shotgun run with a pistol
		// equipped would diverge immediately and blame the wrong thing.
		_loadout = r.Loadout;
		_level = Level.FromText(_levelText);
		_world = new SimWorld(_level, _seed, _loadout);
		_snap = _world.Snapshot();
		ResetRecording();
		return true;
	}

	/// <summary>Advance one recorded tick. Live input is ignored. Verifies the
	/// state hash at every checkpoint the recording carries.</summary>
	public void StepPlayback()
	{
		if (_playback == null || _playbackIndex >= _playback.Inputs.Count) return;

		_world.Step(_playback.Inputs[_playbackIndex]);
		_playbackIndex++;
		_snap = _world.Snapshot();

		int tick = (int)_world.Tick;
		// The periodic checkpoints, and the CLOSING one Step writes on the tick
		// the run ends -- rarely a multiple of HashEvery, so a gate on the
		// modulo alone skipped it, and a run shorter than the interval was
		// played back with nothing checked at all.
		bool last = _playbackIndex == _playback.Inputs.Count;
		if (tick % Replay.HashEvery != 0 && !last) return;
		if (!_playback.TryGetHash(tick, out ulong want)) return;

		ulong got = _world.StateHash();
		if (got != want && _divergedTick < 0)
		{
			_divergedTick = tick;
			_divergedExpected = want;
			_divergedActual = got;
		}
	}

	/// <summary>
	/// Jump to an absolute tick by rebuilding and fast-forwarding. There is no
	/// cheaper way: the sim has no rewind, and re-simulating is exact by
	/// construction, which is the property that makes scrubbing trustworthy.
	/// </summary>
	public void SeekTo(int tick)
	{
		if (_playback == null) return;
		if (tick < 0) tick = 0;
		if (tick > _playback.Inputs.Count) tick = _playback.Inputs.Count;

		_world = new SimWorld(_level, _seed, _loadout);
		_playbackIndex = 0;
		_divergedTick = -1;
		for (int i = 0; i < tick; i++) StepPlayback();
		_snap = _world.Snapshot();
	}

	public ulong StateHash() => _world.StateHash();

	// ------------------------------------------------------------ static geometry

	/// <summary>
	/// What is in a level file, without loading it: cols, rows, guards, caches,
	/// merged wall rects, chests, objectives, glass panes, doors, then the loot:
	/// the chests' dollar budget and every guard's points summed, then the
	/// ambient light and the lamp count. Parsed through the REAL parser on a
	/// throwaway Level so the start screen's summary cannot drift from what
	/// actually deploys — a second glyph-counter written in GDScript would be
	/// exactly that drift.
	/// Touches no live state.
	/// </summary>
	public int[] LevelSummary(string text)
	{
		var L = Level.FromText(text);
		int glass = 0, doors = 0;
		for (int i = 0; i < L.Panels.Count; i++)
			if (L.Panels[i].Kind == PanelKind.Door) doors++; else glass++;
		// Glass and doors APPENDED: every reader checks for at least seven
		// fields and reads them by position, so the first seven cannot move.
		// Lighting APPENDED the same way: [11] ambient percent (-1 fully lit,
		// not authored) and [12] lamps.
		return new[] { L.W, L.H, L.Guards.Count, L.Caches.Count, L.Walls.Length,
			L.Chests.Count, L.Objectives, glass, doors, L.ChestBudget, L.GuardLootTotal,
			L.Ambient, L.Lamps.Count };
	}

	/// <summary>The `name:` a level file declares, for listing it by title.</summary>
	public string LevelTitle(string text) => Level.FromText(text).Name;

	/// <summary>Wall rects, stride 4: x, y, w, h. Fetch once.</summary>
	public int[] GetWalls()
	{
		var w = _level.Walls;
		var outp = new int[w.Length * 4];
		for (int i = 0; i < w.Length; i++)
		{
			outp[i * 4 + 0] = w[i].X;
			outp[i * 4 + 1] = w[i].Y;
			outp[i * 4 + 2] = w[i].W;
			outp[i * 4 + 3] = w[i].H;
		}
		return outp;
	}

	/// <summary>
	/// Glass and doors as they stand NOW, stride 6: kind (0 glass, 1 door), x,
	/// y, w, h, flags (bit 0 open -- a door swung or a pane broken; bit 1 the
	/// panel runs vertically). Read every frame, unlike GetWalls: these change.
	/// </summary>
	public int[] GetPanels()
	{
		var pv = _snap.Panels;
		var outp = new int[pv.Count * 6];
		for (int i = 0; i < pv.Count; i++)
		{
			var p = pv[i];
			outp[i * 6 + 0] = (int)p.Kind;
			outp[i * 6 + 1] = p.Rect.X;
			outp[i * 6 + 2] = p.Rect.Y;
			outp[i * 6 + 3] = p.Rect.W;
			outp[i * 6 + 4] = p.Rect.H;
			outp[i * 6 + 5] = (p.Open ? 1 : 0) | (p.Vertical ? 2 : 0);
		}
		return outp;
	}

	/// <summary>
	/// The door G would move: panel index, centre x, centre y, open (1/0), and
	/// its distance from the player, fixed-point. Empty when no door is in
	/// reach. Resolved by the sim, so the prompt and the toggle cannot disagree
	/// about which door it is.
	/// </summary>
	/// <remarks>
	/// A LIGHT SWITCH shares the slot (lighting plan §5.2): its index is
	/// Panels.Count + the switch index, which is exactly the DoorPick it takes,
	/// and a sixth field says which it is: 0 door, 1 switch. For a switch the
	/// "open" field is whether its room is lit.
	/// </remarks>
	public int[] GetDoorTarget()
	{
		int i = _world.NearestUse();
		if (i < 0) return System.Array.Empty<int>();
		if (i >= _world.Panels.Count)
		{
			var sw = _world.Switches[i - _world.Panels.Count];
			return new[] { i, sw.X, sw.Y, _world.RoomLit(i - _world.Panels.Count) ? 1 : 0,
				_world.DistToUse(i), 1 };
		}
		var d = _world.Panels[i];
		return new[] { i, d.Rect.X + d.Rect.W / 2, d.Rect.Y + d.Rect.H / 2,
			d.Open ? 1 : 0, _world.DistToPanel(i), 0 };
	}

	/// <summary>Distance from the player to a loot target, fixed-point, or -1.
	/// For choosing between a door and a body when both are in reach of G.</summary>
	public int LootTargetDist(int index)
	{
		if (!_world.TryLootTarget(index, out int x, out int y, out _)) return -1;
		return Fx.Dist(x, y, _world.Player.X, _world.Player.Y);
	}

	/// <summary>Exit rect: x, y, w, h. The FIRST exit; see GetExits.</summary>
	public int[] GetExit()
		=> new[] { _level.Exit.X, _level.Exit.Y, _level.Exit.W, _level.Exit.H };

	/// <summary>Every exit (Level.Exits), stride 4: x, y, w, h. Reaching any
	/// one of them ends the run.</summary>
	public int[] GetExits()
	{
		var o = new int[_level.Exits.Count * 4];
		for (int i = 0; i < _level.Exits.Count; i++)
		{
			var e = _level.Exits[i];
			o[i * 4] = e.X; o[i * 4 + 1] = e.Y; o[i * 4 + 2] = e.W; o[i * 4 + 3] = e.H;
		}
		return o;
	}

	/// <summary>The size of the level actually loaded, not a fixed world size.
	/// game/ reads its world bounds from here and never from Level.GW/GH.</summary>
	public int GridWidthPx => _level.W * Level.CellPx;
	public int GridHeightPx => _level.H * Level.CellPx;
	public int GridCols => _level.W;
	public int GridRows => _level.H;

	/// <summary>The LIVE level's grid as glyph bytes, row-major (GridCols per
	/// row) -- what game/level_art.gd dresses. Static for a run: glass and doors
	/// keep their glyphs, and their state comes from GetPanels. Read-only.</summary>
	public byte[] GetGrid()
	{
		var g = new byte[_level.Grid.Length];
		for (int i = 0; i < g.Length; i++) g[i] = (byte)_level.Grid[i];
		return g;
	}

	/// <summary>The live level's `theme:` token, "" when not authored. Art only:
	/// never hashed, never read by the sim.</summary>
	public string LevelTheme => _level.Theme;

	/// <summary>The level open in the EDITOR, which may differ in size from the
	/// one being played.</summary>
	public int EditorCols => _edit == null ? Level.GW : _edit.W;
	public int EditorRows => _edit == null ? Level.GH : _edit.H;


	// ------------------------------------------------------- gear and pack

	/// <summary>
	/// Equip the holstered weapon, or -1 to carry nothing in it. Like the other
	/// setters, this takes effect at the next Load or Restart.
	/// </summary>
	public void SetSecondary(int weaponId) => _loadout = _loadout.WithSecondary(weaponId);

	public int CurrentSecondaryId => _loadout.HasSecondary ? (int)_loadout.Secondary : -1;
	public bool HasSecondary => _loadout.HasSecondary;
	public string CurrentSecondaryName
		=> _loadout.HasSecondary ? WeaponCatalog.NameOf(_loadout.Secondary) : "empty";

	/// <summary>Wear a backpack, by gear item id, or 0 for none. This sizes the
	/// mission pack, so nothing can be looted without one.</summary>
	public void SetBackpack(int itemId) => _loadout = _loadout.WithBackpack(itemId);

	/// <summary>
	/// Stage an apparel slot. Inert — no spec reads a helmet — but it has to
	/// reach the sim, or gear worn at base would not be there in the field and
	/// the field view would show an empty head while you were wearing a helmet.
	/// </summary>
	public void SetApparel(int slot, int itemId)
		=> _loadout = _loadout.WithApparel((GearSlot)slot, itemId);

	public int CurrentApparel(int slot) => _loadout.ApparelIn((GearSlot)slot);
	public int CurrentBackpackId => _loadout.Backpack;

	/// <summary>Which weapon is in hand right now: 0 primary, 1 secondary.</summary>
	public int ActiveWeaponIndex => _world.Loadout.ActiveIndex;

	// ---- what is IN YOUR HANDS, as opposed to what is STAGED ----
	//
	// _loadout is the kit the NEXT run starts with; _world.Loadout is the kit
	// this run is being played with. They were always identical while every
	// equip restarted, so the HUD read the staged one and nobody noticed. They
	// now diverge in two ordinary cases — after an X swap, and after equipping
	// without redeploying — and a HUD that reads the staged one tells you the
	// weapon you are not holding and the magazine you do not have.
	public string HeldWeaponName => WeaponCatalog.NameOf(_world.Loadout.Held);
	public int HeldMagazineSize => _world.Loadout.Spec.Magazine;

	/// <summary>The primary the NEXT run would start with.</summary>
	public string StagedWeaponName => WeaponCatalog.NameOf(_loadout.Weapon);

	/// <summary>
	/// True when the staged kit differs from the one in play — i.e. restarting
	/// would change what you are carrying.
	///
	/// Compares only what the equipment screen and the loadout menu can change.
	/// ActiveIndex is deliberately excluded: swapping weapons with X changes the
	/// live loadout and nothing about what is staged, and reporting that as a
	/// pending change would make the indicator permanent noise.
	/// </summary>
	public bool LoadoutStaged
	{
		get
		{
			var live = _world.Loadout;
			if (_loadout.Weapon != live.Weapon) return true;
			if (_loadout.HasSecondary != live.HasSecondary) return true;
			if (_loadout.HasSecondary && _loadout.Secondary != live.Secondary) return true;
			if (_loadout.Armour != live.Armour) return true;
			if (_loadout.Backpack != live.Backpack) return true;
			// BOTH hands. Comparing only the held one meant a scope staged for
			// the holstered rifle raised no indicator at all.
			for (int hand = 0; hand < 2; hand++)
				for (int i = 0; i < AttachmentCatalog.SlotCount; i++)
					if (_loadout.AttachmentAt(hand, (AttachSlot)i)
							!= live.AttachmentAt(hand, (AttachSlot)i))
						return true;
			// And the kit staged to be carried in, which the pack is built
			// from at Restart.
			if (_loadout.CarriedCount != live.CarriedCount) return true;
			for (int i = 0; i < _loadout.CarriedCount; i++)
				if (_loadout.CarriedAt(i) != live.CarriedAt(i)) return true;
			return false;
		}
	}

	/// <summary>Q8 progress through a holster-and-draw, 0 when not swapping.</summary>
	public int SwapProgressQ8
	{
		get
		{
			int total = Tune.SwapTicks * Actor.Mt;
			if (total <= 0 || _world.Player.SwapMt <= 0) return 0;
			int done = total - _world.Player.SwapMt;
			return done * Fx.One / total;
		}
	}

	/// <summary>The magazine of the weapon not in hand.</summary>
	public int StowedMag => _world.Player.MagStowed;

	// ---- the gear catalog, so game/ never has to duplicate the table ----

	public int GearCount => GearCatalog.Count;
	public int GearIdAt(int index) => GearCatalog.At(index).Id;
	public bool GearExists(int itemId) => GearCatalog.Exists(itemId);
	public string GearName(int itemId) => GearCatalog.NameOf(itemId);
	public int GearWidth(int itemId) => GearCatalog.Get(itemId).W;
	public int GearHeight(int itemId) => GearCatalog.Get(itemId).H;
	public int GearSlotOf(int itemId) => (int)GearCatalog.Get(itemId).Slot;
	public int GearKindOf(int itemId) => (int)GearCatalog.Get(itemId).Kind;
	public int GearSimA(int itemId) => GearCatalog.Get(itemId).SimA;
	public int GearSimB(int itemId) => GearCatalog.Get(itemId).SimB;
	public int GearPackW(int itemId) => GearCatalog.Get(itemId).PackW;
	public int GearPackH(int itemId) => GearCatalog.Get(itemId).PackH;
	public bool GearFitsSlot(int itemId, int slot)
		=> GearCatalog.FitsSlot(itemId, (GearSlot)slot);
	public string GearSlotName(int slot) => GearCatalog.SlotName((GearSlot)slot);
	public int GearSlotCount => GearCatalog.SlotCount;

	// ---- the live mission pack, read-only ----

	public int PackWidth => _world.Pack.W;
	public int PackHeight => _world.Pack.H;
	public int PackUsedCells => _world.Pack.UsedCells();
	public int PackFreeCells => _world.Pack.FreeCells();

	/// <summary>
	/// Everything in the mission pack. Stride 5: itemId, x, y, spanW, spanH.
	/// Read-only by design: the pack is sim state that rides in replays, and
	/// InputFrame has no spare bit to record a drag, so rearranging it mid-run
	/// would desynchronise every replay. The spatial decisions happen in the
	/// stash between missions; in a mission the sim auto-places what you take.
	/// </summary>
	public int[] GetPack()
	{
		var pack = _world.Pack;
		int n = pack.LiveCount();
		var outp = new int[n * 5];
		int w = 0;
		for (int pi = 0; pi < pack.Capacity; pi++)
		{
			if (!pack.IsLive(pi)) continue;
			int id = pack.ItemOf(pi);
			GearCatalog.SpanOf(id, pack.RotOf(pi), out int sw, out int sh);
			outp[w * 5 + 0] = id;
			outp[w * 5 + 1] = pack.XOf(pi);
			outp[w * 5 + 2] = pack.YOf(pi);
			outp[w * 5 + 3] = sw;
			outp[w * 5 + 4] = sh;
			w++;
		}
		return outp;
	}

	/// <summary>
	/// The body, chest or ground pile in reach, or an empty array: x, y, item
	/// count, whether anything fits the pack, the loot index, and 1 when it is
	/// not a body.
	/// Resolved by the sim so the panel and the pick can never disagree about
	/// which thing is being rummaged.
	/// </summary>
	public int[] GetLootTarget()
	{
		int best = _world.NearestLootTarget();
		if (best < 0) return System.Array.Empty<int>();
		if (!_world.TryLootTarget(best, out int x, out int y, out var kit) || kit == null)
			return System.Array.Empty<int>();

		int fits = 0;
		for (int k = 0; k < kit.Count; k++)
			if (_world.Pack.WouldFit(kit[k])) { fits = 1; break; }

		int isChest = best >= _world.Guards.Count ? 1 : 0;
		return new[] { x, y, kit.Count, fits, best, isChest };
	}

	/// <summary>What a given loot target still holds, as gear item ids. The
	/// index is the unified one: guards first, then chests.</summary>
	public int[] GetLootKit(int index)
	{
		if (!_world.TryLootTarget(index, out _, out _, out var kit) || kit == null)
			return System.Array.Empty<int>();
		var outp = new int[kit.Count];
		for (int i = 0; i < kit.Count; i++) outp[i] = kit[i];
		return outp;
	}

	public int LootReachPx => Tune.LootReach / Fx.One;

	/// <summary>
	/// Stride 3: x, y, items. An emptied chest still draws, so a searched room
	/// reads as searched. An OBJECTIVE site reports its count NEGATED and
	/// offset by one (-1 empty, -2 holding one), which is how one array carries
	/// both without a fourth column: `items &lt; 0` is the objective.
	/// </summary>
	public int[] GetChests()
	{
		var c = _snap.Chests;
		var outp = new int[c.Count * 3];
		for (int i = 0; i < c.Count; i++)
		{
			outp[i * 3 + 0] = c[i].X;
			outp[i * 3 + 1] = c[i].Y;
			outp[i * 3 + 2] = c[i].Objective ? -c[i].Items - 1 : c[i].Items;
		}
		return outp;
	}

	/// <summary>Stride 3: x, y, items. Gear the player put down.</summary>
	public int[] GetGround()
	{
		var g = _snap.Ground;
		var outp = new int[g.Count * 3];
		for (int i = 0; i < g.Count; i++)
		{
			outp[i * 3 + 0] = g[i].X;
			outp[i * 3 + 1] = g[i].Y;
			outp[i * 3 + 2] = g[i].Items;
		}
		return outp;
	}

	/// <summary>
	/// The mission pack as flat placements: stride 5 of
	/// placementIndex, itemId, x, y, rotated.
	///
	/// The PLACEMENT INDEX is what matters — it is what a drop names, and it is
	/// stable while other items are taken and dropped around it.
	/// </summary>
	public int[] GetPackPlacements()
	{
		var outp = new System.Collections.Generic.List<int>();
		var pack = _world.Pack;
		for (int pi = 0; pi < pack.Capacity; pi++)
		{
			if (!pack.IsLive(pi)) continue;
			outp.Add(pi);
			outp.Add(pack.ItemOf(pi));
			outp.Add(pack.XOf(pi));
			outp.Add(pack.YOf(pi));
			outp.Add(pack.RotOf(pi));
		}
		return outp.ToArray();
	}

	// ------------------------------------------------------------- prices

	/// <summary>What an item costs to buy, and what it fetches when sold. Zero
	/// means it is not for sale.</summary>
	public int GearPrice(int itemId) => GearCatalog.PriceOf(itemId);

	/// <summary>0 common .. 4 legendary. game/ owns the COLOURS
	/// (item_catalog.gd RARITY_COLOURS); the sim owns which item is which.</summary>
	public int GearRarity(int itemId) => (int)GearCatalog.RarityOf(itemId);
	public string RarityName(int rarity)
		=> GearCatalog.RarityName((Rarity)System.Math.Clamp(rarity, 0, GearCatalog.RarityCount - 1));
	public int RarityCount => GearCatalog.RarityCount;

	/// <summary>What an item is worth as loot (its price; the starter sidearm's
	/// stand-in value). What the budgets on the mission select are spent in.</summary>
	public int GearLootValue(int itemId) => GearCatalog.LootValue(itemId);

	/// <summary>This run's luck, percent. Rolled once at mission start.</summary>
	public int Luck => _world.Luck;

	/// <summary>What is actually on this floor right now, in loot dollars:
	/// supply chests, then bodies. Falls as the player strips them.</summary>
	public int[] LootOnFloor()
	{
		int chests = 0, guards = 0;
		foreach (var c in _world.Chests) if (!c.Objective) chests += LootTable.ValueOf(c.Kit);
		foreach (var g in _world.Guards) guards += LootTable.ValueOf(g.Kit);
		return new[] { chests, guards };
	}

	public bool GearIsObjective(int itemId) => GearCatalog.IsObjective(itemId);

	// ---------------------------------------------------------- objectives

	/// <summary>How many objective items this mission wants, and how many are
	/// in the pack. Extracting with fewer is allowed; it simply does not pay.</summary>
	public int ObjectivesTotal => _snap.ObjectivesTotal;
	public int ObjectivesCarried => _snap.ObjectivesCarried;
	public bool ObjectivesMet => _snap.ObjectivesCarried >= _snap.ObjectivesTotal;

	/// <summary>What is in the mission pack right now, as item ids — what a
	/// successful extraction hands to the campaign layer.</summary>
	public int[] GetPackItems()
	{
		var ids = new System.Collections.Generic.List<int>();
		var pack = _world.Pack;
		for (int pi = 0; pi < pack.Capacity; pi++)
			if (pack.IsLive(pi)) ids.Add(pack.ItemOf(pi));
		return ids.ToArray();
	}

	/// <summary>Everything the catalogue sells, cheapest first. The shop's
	/// stock list, straight from the one item table.</summary>
	public int[] GetShopStock()
	{
		var ids = new System.Collections.Generic.List<int>();
		for (int i = 0; i < GearCatalog.Count; i++)
		{
			int id = GearCatalog.At(i).Id;
			// Price alone would be enough today, but an objective must never
			// appear in a shop whatever it is priced at.
			if (GearCatalog.PriceOf(id) > 0 && !GearCatalog.IsObjective(id)) ids.Add(id);
		}
		ids.Sort((a, b) => GearCatalog.PriceOf(a).CompareTo(GearCatalog.PriceOf(b)));
		return ids.ToArray();
	}

	/// <summary>Whether one named item on a body would fit the pack as it is.</summary>
	public bool PackWouldFit(int itemId) => _world.Pack.WouldFit(itemId);

	/// <summary>
	/// How full the pack is, 0-100. Shown on the looting screen so a bag can be
	/// judged at a glance rather than by counting squares. Rounded to nearest,
	/// and a pack with no bag worn reads 0 — nothing carried is not "full".
	/// </summary>
	public int PackFullPercent
	{
		get
		{
			int cells = _world.Pack.W * _world.Pack.H;
			if (cells <= 0) return 0;
			return (_world.Pack.UsedCells() * 100 + cells / 2) / cells;
		}
	}

	/// <summary>
	/// Build an InputFrame.EquipPick from a pack placement and a GearSlot. The
	/// packing lives in the sim so game/ never has to know it.
	/// </summary>
	public int MakeEquipPick(int placement, int slot)
		=> InputFrame.PackEquip(placement, slot);

	/// <summary>
	/// Whether an item could be put in a slot MID-MISSION. The screen asks so it
	/// can refuse with a reason instead of staging an intent the sim will
	/// silently ignore. Mirrors StepEquip's rules exactly — if these two ever
	/// disagree, the screen is lying.
	/// </summary>
	public bool CanEquipMidRun(int itemId, int slot)
	{
		if (!GearCatalog.Exists(itemId)) return false;
		var item = GearCatalog.Get(itemId);
		if (item.Kind == GearKind.Weapon)
			return slot == (int)GearSlot.Primary || slot == (int)GearSlot.Secondary;
		if (item.Kind == GearKind.Armour)
			return slot == (int)GearSlot.Vest;
		if (item.Kind == GearKind.Pack)
			return slot == (int)GearSlot.Backpack;
		// Apparel goes in its own slot and nowhere else.
		if (item.Kind == GearKind.Apparel)
			return slot == (int)item.Slot;
		// An attachment names its OWN destination, so any weapon slot offers
		// it -- the sim fits it where the item says, not where it was dropped.
		if (item.Kind == GearKind.Attachment)
			return slot == (int)GearSlot.Primary || slot == (int)GearSlot.Secondary;
		return false;
	}

	/// <summary>
	/// Whether a backpack would actually take what is being carried. The field
	/// view asks so it can say "your kit will not fit that bag" instead of
	/// letting the tick refuse silently — the one equip that can fail for a
	/// reason the player could not have predicted from the item alone.
	/// </summary>
	public bool BackpackWouldHold(int itemId)
	{
		if (!GearCatalog.Exists(itemId)) return false;
		var item = GearCatalog.Get(itemId);
		if (item.Kind != GearKind.Pack) return false;

		var scratch = new PackGrid(item.PackW, item.PackH);
		int worn = _world.Loadout.Backpack;
		for (int i = 0; i < _world.Pack.Capacity; i++)
		{
			if (!_world.Pack.IsLive(i)) continue;
			if (_world.Pack.ItemOf(i) == itemId) continue;   // the bag itself
			if (scratch.AutoPlace(_world.Pack.ItemOf(i)) == PackGrid.None) return false;
		}
		return worn == 0 || scratch.AutoPlace(worn) != PackGrid.None;
	}

	/// <summary>
	/// What the player is WEARING according to the sim, as gear item ids indexed
	/// by GearSlot, 0 for an empty slot: every slot, the five cosmetic ones
	/// included, since all of them can be changed in the field.
	///
	/// The in-mission inventory reads this rather than the stash, because
	/// mid-run the sim's Loadout is the truth — the stash is what you left at
	/// base, and after an equip the two deliberately disagree.
	/// </summary>
	public int[] GetWornSim()
	{
		// Sized by the catalogue, not by hand: this was new int[8] when Legs
		// was appended as the ninth slot, so the field view never saw legs.
		var w = new int[GearCatalog.SlotCount];
		var l = _world.Loadout;
		w[(int)GearSlot.Primary] = GearCatalog.WeaponItemId((int)l.Weapon);
		w[(int)GearSlot.Secondary] = l.HasSecondary
			? GearCatalog.WeaponItemId((int)l.Secondary) : 0;
		w[(int)GearSlot.Vest] = GearCatalog.ArmourItemId((int)l.Armour);
		w[(int)GearSlot.Backpack] = l.Backpack;
		w[(int)GearSlot.Helmet] = l.Helmet;
		w[(int)GearSlot.Footware] = l.Footware;
		w[(int)GearSlot.Chest] = l.Shirt;
		w[(int)GearSlot.Arms] = l.Arms;
		w[(int)GearSlot.Legs] = l.Legs;
		return w;
	}

	/// <summary>
	/// What is FITTED right now, as gear item ids by attachment sub-slot, 0 for
	/// an empty rail. The counterpart of GetWornSim, and read for the same
	/// reason: attachments can be fitted in the field, so mid-run the sim's set
	/// is the truth and the stash's is what you left base with.
	///
	/// `hand` is 0 for the primary and 1 for the holster, and the answer is
	/// masked by THAT weapon's own rails — a stock fitted to the rifle is not
	/// on the pistol, and the screen must not draw it as though it were.
	/// </summary>
	public int[] GetFittedSim(int hand)
	{
		var outp = new int[AttachmentCatalog.SlotCount];
		int h = hand == 1 ? 1 : 0;
		for (int i = 0; i < outp.Length; i++)
			outp[i] = GearCatalog.AttachmentItemId(i,
				_world.Loadout.AttachmentAt(h, (AttachSlot)i));
		return outp;
	}

	/// <summary>
	/// What is on a hand's rails in the sim, UNMASKED: item ids by sub-slot, 0
	/// for an empty rail. GetFittedSim hides a rail the weapon lacks, which is
	/// right for drawing and wrong for settling -- the rails stay with the
	/// HAND, so a scope on a hand whose rifle was swapped for a pistol mid-run
	/// is still owned, and extraction must bring it home.
	/// </summary>
	public int[] GetRailsSim(int hand)
	{
		var outp = new int[AttachmentCatalog.SlotCount];
		var set = _world.Loadout.SetAt(hand == 1 ? 1 : 0);
		for (int i = 0; i < outp.Length; i++)
			outp[i] = GearCatalog.AttachmentItemId(i, set.Raw((AttachSlot)i));
		return outp;
	}

	/// <summary>Which hand is full: 0 primary, 1 secondary. The in-mission
	/// screen marks it, because equipping trades against the slot you point
	/// at, not the one you happen to be holding.</summary>
	public int ActiveWeaponSlot => _world.Loadout.ActiveIndex;

	/// <summary>A mission is under way: alive and not yet over. It used to
	/// demand a pack as well, so a run with no backpack read as NO run and E
	/// opened the base stash in the middle of a mission. The field view already
	/// draws the no-bag case ("no backpack worn").</summary>
	public bool RunLive => _world.Player.Alive && _world.Over == null;

	// ------------------------------------------------------------- snapshot

	/// <summary>
	/// x, y, facing, alive, mag, heat, reloading, sneaking, recoil,
	/// health, armour, armourMax, baseHealth,
	/// aiming, aimTarget, aimLockMt, aimLockFullMt,
	/// spreadHalfBrad, sway, moveTier, readyQ8, spinQ8.
	/// Indices are APPEND-ONLY: main.gd reads them positionally.
	/// </summary>
	public int[] GetPlayer() => new[]
	{
		_snap.PlayerX, _snap.PlayerY, _snap.PlayerFacing,
		_snap.PlayerAlive ? 1 : 0, _snap.PlayerMag, _snap.PlayerHeat,
		_snap.PlayerReloading ? 1 : 0, _snap.PlayerSneaking ? 1 : 0,
		_world.Player.RecoilQ8,
		_snap.PlayerHealth, _snap.PlayerArmour, _snap.PlayerArmourMax, Tune.BaseHealth,
		_snap.PlayerAiming ? 1 : 0, _snap.PlayerAimTarget,
		_snap.PlayerAimLockMt, _snap.PlayerAimLockFullMt,
		_snap.PlayerSpreadHalf, _snap.PlayerSway,
		_snap.PlayerMoveTier, _snap.PlayerReadyQ8, _snap.PlayerSpinQ8,
	};

	/// <summary>Names for the four movement tiers, for the HUD. Here rather
	/// than in GDScript so the sim's tier ordinals have exactly one set of
	/// labels.</summary>
	public string MoveTierName(int tier) => tier switch
	{
		InputFrame.TierStealth => "STEALTH",
		InputFrame.TierFast => "FAST",
		InputFrame.TierSprint => "SPRINT",
		_ => "WALK",
	};

	public int MoveTierCount => InputFrame.TierCount;

	/// <summary>Walk speed at a tier, in whole px/s, so the HUD and the loadout
	/// screen can show what a tier actually costs or buys.</summary>
	public int MoveTierSpeedPx(int tier)
		=> tier == InputFrame.TierStealth
			? _loadout.SneakSpeed / Fx.One
			: (int)(((long)_loadout.WalkSpeed * Tune.TierSpeedQ8(tier)) >> Fx.Shift) / Fx.One;

	/// <summary>Aimed spread as a Q8 fraction of hip-fire, for the reticle.</summary>
	public int AimSpreadQ8 => _loadout.Spec.AimSpreadQ8;

	/// <summary>Denominator the weapon turn rate is expressed over, so the
	/// loadout menu can show it as a percentage of an instant swing.</summary>
	public int TurnDen => Tune.TurnDen;

	public void SetArmour(int armourId) => _loadout = _loadout.WithArmour(armourId);
	public int CurrentArmourId => (int)_loadout.Armour;
	public string CurrentArmourName => ArmourCatalog.NameOf(_loadout.Armour);
	public int ArmourCount => ArmourCatalog.Count;
	public string ArmourNameOf(int id) => ArmourCatalog.NameOf(ArmourCatalog.Clamp(id));
	public int ArmourValueOf(int id) => ArmourCatalog.Get(ArmourCatalog.Clamp(id)).Armour;

	// ------------------------------------------------------- attachments

	public int SlotCount => AttachmentCatalog.SlotCount;
	public string SlotName(int slot) => AttachmentCatalog.SlotName(ClampSlot(slot));
	public int OptionCount(int slot) => AttachmentCatalog.CountFor(ClampSlot(slot));
	public string OptionName(int slot, int id) => AttachmentCatalog.NameOf(ClampSlot(slot), id);

	/// <summary>Whether the equipped weapon even has this slot. A Glock has no
	/// stock, and the menu must not offer one.</summary>
	public bool WeaponHasSlot(int weaponId, int slot)
		=> WeaponCatalog.HasSlot(WeaponCatalog.Clamp(weaponId), ClampSlot(slot));

	public int GetAttachment(int slot) => _loadout.Attachment(ClampSlot(slot));

	public void SetAttachment(int slot, int id)
		=> _loadout = _loadout.WithAttachment(ClampSlot(slot), id);

	// ---- per weapon, which is how they are actually fitted ----
	//
	// `hand` is 0 for the primary and 1 for the holster. The pair above act on
	// whichever weapon is IN HAND, which is what the field means by fitting
	// something; the stash has both guns in front of it and must say which.

	public int GetAttachmentFor(int hand, int slot)
		=> _loadout.AttachmentAt(hand == 1 ? 1 : 0, ClampSlot(slot));

	public void SetAttachmentFor(int hand, int slot, int id)
		=> _loadout = _loadout.WithAttachmentAt(hand == 1 ? 1 : 0, ClampSlot(slot), id);

	/// <summary>Which hand a GearSlot is: 0 primary, 1 secondary, -1 for
	/// anything that is not a weapon slot. The screens think in GearSlots and
	/// the rails are indexed by hand.</summary>
	public int HandOfSlot(int gearSlot)
		=> gearSlot == (int)GearSlot.Primary ? 0
			: gearSlot == (int)GearSlot.Secondary ? 1 : -1;

	private static AttachSlot ClampSlot(int raw)
		=> (raw >= 0 && raw < AttachmentCatalog.SlotCount) ? (AttachSlot)raw : AttachSlot.Sight;

	// ------------------------------------------------------- what is carried
	//
	// The kit the player walks in WITH, staged here and handed to the sim in
	// the Loadout, which places it in the pack at Restart.
	//
	// Item by item rather than as one array argument, for the reason the probe
	// below gives: Godot registers every C# parameter as required and this
	// project has been bitten four times by a signature that compiled and then
	// failed at runtime.

	private readonly List<int> _carry = new();

	public void ClearCarried()
	{
		_carry.Clear();
		_loadout = _loadout.WithCarried(System.Array.Empty<int>());
	}

	/// <summary>Stage one item to be carried in. False when it does not exist,
	/// when the list is full, or when the pack will not hold it alongside what
	/// is already staged — the caller says so rather than finding out at
	/// Restart, where the item would simply not be placed.</summary>
	public bool AddCarried(int itemId)
	{
		if (!GearCatalog.Exists(itemId)) return false;
		if (_carry.Count >= Loadout.MaxCarried) return false;
		if (!CarriedWouldFit(itemId)) return false;
		_carry.Add(itemId);
		_loadout = _loadout.WithCarried(_carry.ToArray());
		return true;
	}

	/// <summary>Unstage one, by its index in the carry list. False when there
	/// is no such entry.</summary>
	public bool RemoveCarried(int index)
	{
		if (index < 0 || index >= _carry.Count) return false;
		_carry.RemoveAt(index);
		_loadout = _loadout.WithCarried(_carry.ToArray());
		return true;
	}

	public int CarriedCount => _loadout.CarriedCount;
	public int CarriedAt(int i) => _loadout.CarriedAt(i);

	/// <summary>Whether one more item would still fit the bag alongside
	/// everything already staged. The whole list is re-packed into a scratch
	/// grid rather than counting cells: a pack is a PACKING, and four 1x1s and
	/// one 2x2 are not the same four cells.</summary>
	public bool CarriedWouldFit(int itemId)
	{
		if (!GearCatalog.Exists(itemId)) return false;
		var scratch = StagedPack();
		if (scratch.W <= 0 || scratch.H <= 0) return false;
		for (int i = 0; i < _carry.Count; i++)
			if (scratch.AutoPlace(_carry[i]) == PackGrid.None) return false;
		return scratch.AutoPlace(itemId) != PackGrid.None;
	}

	/// <summary>
	/// The staged kit laid out in the staged bag, in GetPackPlacements' own
	/// stride: placement, item, x, y, rot.
	///
	/// A scratch grid, because the sim's Pack belongs to the run that is
	/// loaded and is not rebuilt until Restart. This is what the stash draws
	/// between missions, and it is laid out by the SAME AutoPlace that will
	/// place it for real — so what the player is shown at base is where it
	/// actually lands.
	/// </summary>
	public int[] CarriedPlacements()
	{
		var outp = new List<int>();
		var scratch = StagedPack();
		if (scratch.W <= 0 || scratch.H <= 0) return outp.ToArray();
		for (int i = 0; i < _carry.Count; i++)
		{
			int pi = scratch.AutoPlace(_carry[i]);
			if (pi == PackGrid.None) continue;
			// The PLACEMENT the screen names back to us is the index into the
			// carry list, not the scratch grid's id: the list is what
			// RemoveCarried takes, and the scratch grid is thrown away here.
			outp.Add(i);
			outp.Add(_carry[i]);
			outp.Add(scratch.XOf(pi));
			outp.Add(scratch.YOf(pi));
			outp.Add(scratch.RotOf(pi));
		}
		return outp.ToArray();
	}

	/// <summary>An empty grid the size of the bag the player will DEPLOY with,
	/// which is the staged one, not the one the loaded run is using.</summary>
	private PackGrid StagedPack()
	{
		var bag = GearCatalog.Get(_loadout.Backpack);
		return new PackGrid(bag.PackW, bag.PackH);
	}

	/// <summary>The staged bag's grid size, for the screen that draws it.</summary>
	public int StagedPackW => GearCatalog.Get(_loadout.Backpack).PackW;
	public int StagedPackH => GearCatalog.Get(_loadout.Backpack).PackH;

	// ------------------------------------------------------- the item probe
	//
	// "What would THIS weapon, with THESE attachments, actually do?"
	//
	// The staged loadout answers that for the kit you are about to deploy in,
	// and the world's answers it for the one in your hands. A tooltip asks
	// about neither: it asks about a rifle lying in the stash, or an
	// attachment you have not fitted to anything. So it gets a scratch Loadout
	// of its own, which never reaches the sim, never touches _loadout, and
	// feeds no hash.
	//
	// Built call by call rather than from an array argument: Godot registers
	// every C# parameter as required and marshals collections by value, and
	// this project has already been bitten four times by a signature that
	// compiled, passed --check-only, and failed at runtime. Three int calls
	// cannot fail that way.
	private Loadout _probe = Loadout.Default;

	/// <summary>Start a probe on a bare weapon, armour and attachments cleared.
	/// Armour stays out of it: a weapon tooltip is about the weapon, and a vest
	/// would move walk speed underneath it for no stated reason.</summary>
	public void ProbeWeapon(int weaponId)
		=> _probe = new Loadout((WeaponId)WeaponCatalog.Clamp(weaponId));

	/// <summary>Fit one attachment onto the probe. A slot the weapon does not
	/// carry is IGNORED rather than clamped onto another, so a stock cannot
	/// quietly become a sight on a pistol.</summary>
	public void ProbeAttach(int slot, int optionId)
	{
		var s = ClampSlot(slot);
		if (!WeaponCatalog.HasSlot(_probe.Weapon, s)) return;
		_probe = _probe.WithAttachment(s, optionId);
	}

	/// <summary>The probe as fitted, in the same order and units as
	/// ResolvedStats -- so one table of labels reads all three.</summary>
	public int[] ProbeStats() => StatsOf(_probe);

	/// <summary>The same weapon with nothing on it, for the delta beside
	/// it.</summary>
	public int[] ProbeBareStats() => StatsOf(new Loadout(_probe.Weapon));

	/// <summary>Which weapon the probe is on, as a WeaponId ordinal.</summary>
	public int ProbeWeaponId => (int)_probe.Weapon;

	/// <summary>The first weapon in the catalogue carrying this attachment
	/// slot, so an attachment lying loose in the stash can still be described
	/// against something it would actually fit.</summary>
	public int FirstWeaponWithSlot(int slot)
	{
		var s = ClampSlot(slot);
		for (int i = 0; i < WeaponCatalog.Count; i++)
			if (WeaponCatalog.HasSlot((WeaponId)i, s)) return i;
		return 0;
	}

	public string WeaponNameOf(int id) => WeaponCatalog.NameOf(WeaponCatalog.Clamp(id));
	public string WeaponClassOf(int id) => WeaponCatalog.ClassOf(WeaponCatalog.Clamp(id));
	public string CurrentWeaponClass => WeaponCatalog.ClassOf(_loadout.Weapon);

	/// <summary>
	/// Resolved stats for the CURRENT loadout, so the menu can show what the
	/// attachments actually did rather than what they claim:
	/// damage, pierce, magazine, cadence, reload, spreadBase, spreadPerHeat,
	/// heatPerShot, bulletSpeed, bulletTicks, pellets, gunshotRadius,
	/// walkSpeed, sneakSpeed, visionBonus, detectionMul.
	/// </summary>
	public int[] ResolvedStats() => StatsOf(_loadout);

	/// <summary>The same stats for the bare weapon, so the menu can show deltas.</summary>
	public int[] BaseStats() => StatsOf(new Loadout(_loadout.Weapon, _loadout.Armour));

	private static int[] StatsOf(Loadout l)
	{
		var w = l.Spec;
		return new[]
		{
			w.Damage, w.ArmourPierce, w.Magazine, w.FireCooldownTicks, w.ReloadTicks,
			w.SpreadBase, w.SpreadPerHeat, w.HeatPerShot, w.BulletSpeed, w.BulletTicks,
			w.Pellets, w.GunshotRadius, l.WalkSpeed, l.SneakSpeed,
			l.VisionRadiusBonus, l.DetectionMul,
			// Append-only: loadout_menu.gd indexes this positionally.
			w.SpreadPerSway, w.TurnNum,
		};
	}

	public string LoadoutText() => _loadout.ToText();

	/// <summary>
	/// tick, worldScale, playerScale, dilating, alarm, exposure, overCode,
	/// provable, unprovable, destroyed, fuelIndex, kills, subdues, shots.
	/// overCode: 0 running, 1 escaped, 2 dead.
	/// </summary>
	public int[] GetWorld()
	{
		int over = _snap.Over == null ? 0 : (_snap.Over == "out" ? 1 : 2);
		return new[]
		{
			(int)_snap.Tick, _snap.WorldScale, _snap.PlayerScale,
			_snap.Dilating ? 1 : 0, _snap.AlarmLevel, _snap.Exposure, over,
			_snap.Provable, _snap.Unprovable, _snap.DestroyedScore,
			_snap.FuelIndex, _snap.Kills, _snap.Subdues, _snap.Shots,
		};
	}

	/// <summary>Stride 13: x, y, facing, state, awareness, deadFacing, deadRoll,
	/// visible, armour, armourMax, task, radioQ8, afraid. Indexed by hand in
	/// main.gd, footsteps.gd and the AI overlay: append, never reorder.</summary>
	public int[] GetGuards()
	{
		const int Stride = 13;   // mirrored by main.gd GUARD_STRIDE
		var g = _snap.Guards;
		var outp = new int[g.Count * Stride];
		for (int i = 0; i < g.Count; i++)
		{
			var a = g[i];
			outp[i * Stride + 0] = a.X;
			outp[i * Stride + 1] = a.Y;
			outp[i * Stride + 2] = a.Facing;
			outp[i * Stride + 3] = (int)a.State;
			outp[i * Stride + 4] = a.Awareness;
			outp[i * Stride + 5] = a.DeadFacing;
			outp[i * Stride + 6] = a.DeadRoll;
			// Player concealment comes from walls only (spec §9): a guard the
			// player has no line to is not drawn at all, not dimmed. Walls and
			// SHUT DOORS -- the live opaque set, so a guard behind glass is seen
			// and one behind a closed door is not.
			outp[i * Stride + 7] = Geometry.ClearLine(_world.Opaque,
				_snap.PlayerX, _snap.PlayerY, a.X, a.Y) ? 1 : 0;
			outp[i * Stride + 8] = a.Armour;
			outp[i * Stride + 9] = a.ArmourMax;
			// Guard AI v2: what he is doing, and how far through a radio call
			// (Q8), so the player can see a call worth stopping.
			outp[i * Stride + 10] = (int)a.Task;
			outp[i * Stride + 11] = a.RadioQ8;
			outp[i * Stride + 12] = a.Afraid ? 1 : 0;   // frozen in fear
		}
		return outp;
	}

	// ------------------------------------------------ guard AI presentation

	/// <summary>Sim.GuardState names in ordinal order. main.gd's ST_* constants
	/// mirror these ordinals; editor_check.gd asserts they still agree.</summary>
	public string[] GuardStateNames() => Enum.GetNames(typeof(GuardState));

	/// <summary>Sim.GuardTask names in ordinal order, for the debug overlay's
	/// labels and for the TASK_* mirror check.</summary>
	public string[] GuardTaskNames() => Enum.GetNames(typeof(GuardTask));

	/// <summary>
	/// Everything the F3 AI debug overlay draws (Guard_AI.md §9.2), read
	/// straight off the world: a DEBUG view, asked for only while it is up, and
	/// never fed back. Sections, in order, each led by its count:
	///   guards: n, then per guard 14 ints -- x, y, facing, state, task,
	///     awareness, hasLkp, lkpX, lkpY, squadId, groupId, navLeft, routeLeft,
	///     afraid --
	///     followed by 2*navLeft ints of remaining nav path and 2*routeLeft of
	///     remaining assault route;
	///   net: hasIntel, intelX, intelY, intelAgeTicks, compromised, hasFocus,
	///     focusX, focusY;
	///   squads: n, then per squad anchor, go, rallyX, rallyY, m, m members;
	///   groups: n, then per group exit, target, heading, dwelling, m, m members;
	///   nodes: n, then per node x, y, staleTicks, claimedBy, authored.
	/// </summary>
	public int[] GetAiDebug()
	{
		var o = new List<int>();
		if (_world == null) return new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
		var gs = _world.Guards;
		o.Add(gs.Count);
		for (int i = 0; i < gs.Count; i++)
		{
			var e = gs[i];
			int navLeft = Math.Max(0, e.NavX.Count - e.NavIndex);
			int routeLeft = Math.Max(0, e.RouteX.Count - e.RouteIndex);
			o.Add(e.X); o.Add(e.Y); o.Add(e.Facing); o.Add((int)e.State); o.Add((int)e.Task);
			o.Add(e.Awareness); o.Add(e.HasLkp ? 1 : 0); o.Add(e.LkpX); o.Add(e.LkpY);
			o.Add(e.SquadId); o.Add(e.GroupId); o.Add(navLeft); o.Add(routeLeft); o.Add(e.Afraid ? 1 : 0);
			for (int k = e.NavIndex; k < e.NavX.Count; k++) { o.Add(e.NavX[k]); o.Add(e.NavY[k]); }
			for (int k = e.RouteIndex; k < e.RouteX.Count; k++) { o.Add(e.RouteX[k]); o.Add(e.RouteY[k]); }
		}

		var net = _world.Net;
		o.Add(net.HasIntel ? 1 : 0); o.Add(net.IntelX); o.Add(net.IntelY);
		o.Add(net.IntelAgeMt / Actor.Mt); o.Add(net.Compromised ? 1 : 0);
		o.Add(net.HasFocus ? 1 : 0); o.Add(net.FocusX); o.Add(net.FocusY);

		o.Add(net.Squads.Count);
		foreach (var sq in net.Squads)
		{
			o.Add(sq.Anchor); o.Add(sq.Go ? 1 : 0); o.Add(sq.RallyX); o.Add(sq.RallyY);
			o.Add(sq.Members.Count);
			foreach (int m in sq.Members) o.Add(m);
		}

		o.Add(net.Groups.Count);
		foreach (var grp in net.Groups)
		{
			o.Add(grp.Exit ? 1 : 0); o.Add(grp.Target); o.Add(grp.Heading); o.Add(grp.Dwelling ? 1 : 0);
			o.Add(grp.Members.Count);
			foreach (int m in grp.Members) o.Add(m);
		}

		var map = _world.Sweep;
		o.Add(map == null ? 0 : map.Count);
		if (map != null)
			for (int k = 0; k < map.Count; k++)
			{
				o.Add(map.X[k]); o.Add(map.Y[k]); o.Add(map.Stale[k] / Actor.Mt);
				o.Add(map.ClaimedBy[k]); o.Add(map.Authored[k] ? 1 : 0);
			}
		return o.ToArray();
	}

	/// <summary>Stride 7: x, y, heading, fromPlayer, speed, kind (a BulletKind
	/// ordinal), lifeTicks (a grenade's fuse).</summary>
	public const int BulletStride = 7;

	public int[] GetBullets()
	{
		var b = _snap.Bullets;
		var outp = new int[b.Count * BulletStride];
		for (int i = 0; i < b.Count; i++)
		{
			int o = i * BulletStride;
			outp[o + 0] = b[i].X;
			outp[o + 1] = b[i].Y;
			outp[o + 2] = b[i].Heading;
			outp[o + 3] = b[i].FromPlayer ? 1 : 0;
			outp[o + 4] = b[i].Speed;
			outp[o + 5] = b[i].Kind;
			outp[o + 6] = b[i].LifeTicks;
		}
		return outp;
	}

	/// <summary>Stride 3: x, y, taken.</summary>
	public int[] GetCaches()
	{
		var c = _snap.Caches;
		var outp = new int[c.Count * 3];
		for (int i = 0; i < c.Count; i++)
		{
			outp[i * 3 + 0] = c[i].X;
			outp[i * 3 + 1] = c[i].Y;
			outp[i * 3 + 2] = c[i].Taken ? 1 : 0;
		}
		return outp;
	}

	/// <summary>Stride 4: nameIndex, tier, charge, state (0 intact, 1 degraded, 2 gone).</summary>
	public int[] GetRecords()
	{
		var r = _snap.Records;
		var outp = new int[r.Count * 4];
		for (int i = 0; i < r.Count; i++)
		{
			outp[i * 4 + 0] = r[i].NameIndex;
			outp[i * 4 + 1] = r[i].Tier;
			outp[i * 4 + 2] = r[i].Charge;
			outp[i * 4 + 3] = r[i].State;
		}
		return outp;
	}

	/// <summary>Stride 5: kind, x, y, heading, value. Cleared every tick.</summary>
	public int[] GetEvents()
	{
		var e = _snap.Events;
		var outp = new int[e.Count * 5];
		for (int i = 0; i < e.Count; i++)
		{
			outp[i * 5 + 0] = (int)e[i].Kind;
			outp[i * 5 + 1] = e[i].X;
			outp[i * 5 + 2] = e[i].Y;
			outp[i * 5 + 3] = e[i].Heading;
			outp[i * 5 + 4] = e[i].Value;
		}
		return outp;
	}

	public string RecordName(int nameIndex)
		=> nameIndex >= 0 && nameIndex < RecordStore.Names.Length
			? RecordStore.Names[nameIndex] : "?";

	/// <summary>
	/// Number of SimEventKind values. game/ mirrors this enum's ORDINALS as
	/// GDScript constants, which is a hand-maintained coupling across two
	/// languages — inserting a kind in the middle would silently remap every
	/// sound and effect. game/ asserts against this at startup.
	/// </summary>
	public int EventKindCount => System.Enum.GetValues(typeof(SimEventKind)).Length;

	public int RecordStageTicks => Tune.RecordStageTicks;
	public int MagazineSize => _loadout.Spec.Magazine;

	/// <summary>Base visibility reach plus whatever the rail adds.</summary>
	public int VisionRadiusPx(int basePx) => basePx + _loadout.VisionRadiusBonus / Fx.One;

	// ------------------------------------------------------------- lighting
	// cognitohazard_lighting_plan.md §7. Everything here is READ from the sim:
	// what is drawn dark is what a guard sees as dark, by construction.

	/// <summary>The player's own sight reach, px: main.gd VISION_RADIUS. The
	/// player makes guards out by the same dark-shortened rule guards use.</summary>
	public const int PlayerSightPx = 430;

	/// <summary>This run has lighting at all. False on every lit level, which
	/// draws exactly as it did before.</summary>
	public bool HasLight => _world.Light != null;

	/// <summary>
	/// Moves whenever the light map changes; re-upload on a change. Salted with
	/// the WORLD, since every restart, replay seek and playtest builds a new
	/// map whose own version starts again from 1: a new level at version 1
	/// must not look like the last one at version 1.
	/// </summary>
	public int LightVersion
	{
		get
		{
			if (_world.Light == null) return 0;
			if (!ReferenceEquals(_lightWorld, _world)) { _lightWorld = _world; _lightSerial++; }
			return (_lightSerial << 20) + (_world.Light.Version & 0xFFFFF);
		}
	}
	private SimWorld? _lightWorld;
	private int _lightSerial;

	/// <summary>The map as a (2*cols) x (2*rows) luminance image, 0..255 (see
	/// LightMap.Texture2x). Empty on a lit level.</summary>
	public byte[] GetLightTexture()
		=> _world.Light == null ? System.Array.Empty<byte>() : _world.Light.Texture2x();

	/// <summary>
	/// The DARKNESS overlay as RGBA8 at Texture2x's size: the tint colour, with
	/// alpha rising as the light falls, up to <paramref name="maxAlpha"/> (the
	/// display floor, plan §7.1: the floor stays readable where the sim says
	/// pitch black). Drawn over the floor with an ordinary blend, black-ish
	/// at alpha a is a multiply by (1 - a), so this IS the plan's multiply
	/// layer with no material and no extra node. Built here because it is a
	/// loop over every texel, on every light change.
	/// </summary>
	public byte[] GetDarkness(int r, int g, int b, int maxAlpha)
		=> DarknessRgba(GetLightTexture(), r, g, b, maxAlpha);

	/// <summary>The editor's preview of the same overlay, from the edit buffer.</summary>
	public byte[] EditorDarkness(int r, int g, int b, int maxAlpha)
		=> DarknessRgba(EditorLightPreview(), r, g, b, maxAlpha);

	private static byte[] DarknessRgba(byte[] lum, int r, int g, int b, int maxAlpha)
	{
		var outp = new byte[lum.Length * 4];
		for (int i = 0; i < lum.Length; i++)
		{
			outp[i * 4] = (byte)r;
			outp[i * 4 + 1] = (byte)g;
			outp[i * 4 + 2] = (byte)b;
			outp[i * 4 + 3] = (byte)(maxAlpha * (255 - lum[i]) / 255);
		}
		return outp;
	}

	/// <summary>Light at the player, Q8, and how visible that makes them: the
	/// figure every guard's eye is scaled by this tick. The HUD meter reads the
	/// second and nothing else.</summary>
	public int PlayerLightQ8 => _world.PlayerLightQ8;
	public int PlayerVisQ8 => _world.PlayerVisQ8;

	/// <summary>Lamps, stride 3: x, y, flags (bit 0 lit, bit 1 broken).</summary>
	public int[] GetLamps()
	{
		var outp = new int[_world.Lamps.Count * 3];
		for (int i = 0; i < _world.Lamps.Count; i++)
		{
			var l = _world.Lamps[i];
			outp[i * 3] = l.X;
			outp[i * 3 + 1] = l.Y;
			outp[i * 3 + 2] = (l.Lit ? 1 : 0) | (l.Broken ? 2 : 0);
		}
		return outp;
	}

	/// <summary>Light switches, stride 3: x, y, room lit (1/0).</summary>
	public int[] GetSwitches()
	{
		var outp = new int[_world.Switches.Count * 3];
		for (int i = 0; i < _world.Switches.Count; i++)
		{
			var sw = _world.Switches[i];
			outp[i * 3] = sw.X;
			outp[i * 3 + 1] = sw.Y;
			outp[i * 3 + 2] = _world.RoomLit(i) ? 1 : 0;
		}
		return outp;
	}

	/// <summary>
	/// Per guard, parallel to GetGuards, stride 2: how well the player makes
	/// him out (Q8: 0 not at all, 256 plainly; SimWorld.PlayerSeesQ8, the rule
	/// guards use on the player) and whether his torch is lit (1/0). A separate
	/// array rather than two more fields on GetGuards, whose stride other
	/// readers (footsteps, the AI overlay) index by hand.
	/// </summary>
	public int[] GetGuardSight()
	{
		var g = _world.Guards;
		var outp = new int[g.Count * 2];
		int sight = PlayerSightPx * Fx.One + _world.Loadout.VisionRadiusBonus;
		for (int i = 0; i < g.Count; i++)
		{
			outp[i * 2] = g[i].Prone ? 0 : _world.PlayerSeesQ8(g[i], sight);
			outp[i * 2 + 1] = _world.TorchOn(g[i]) ? 1 : 0;
		}
		return outp;
	}

	/// <summary>
	/// Every lit torch as a fan, clipped by the opaque set: for each beam, the
	/// lens then <paramref name="rays"/> edge points, in px. Drawn whether or not
	/// the guard himself can be seen -- a beam sweeping a far wall is how you
	/// learn a search is coming (plan §6).
	/// </summary>
	public Vector2[] GetTorchBeams(int rays)
	{
		if (_world.Light == null) return System.Array.Empty<Vector2>();
		if (rays < 2) rays = 2;
		var outp = new List<Vector2>();
		for (int i = 0; i < _world.Guards.Count; i++)
		{
			var e = _world.Guards[i];
			if (!_world.TorchOn(e)) continue;
			var near = NearbyWalls(e.X, e.Y, Tune.TorchReach);
			outp.Add(new Vector2(e.X / (float)Fx.One, e.Y / (float)Fx.One));
			for (int k = 0; k < rays; k++)
			{
				int brad = e.Facing - Tune.TorchHalf + (int)((long)k * 2 * Tune.TorchHalf / (rays - 1));
				int d = Geometry.CastRay(near, e.X, e.Y, brad & Brad.Mask, Tune.TorchReach);
				Brad.SinCos(brad & Brad.Mask, out int sin, out int cos);
				outp.Add(new Vector2((e.X + (int)(((long)d * cos) >> Brad.UnitShift)) / (float)Fx.One,
					(e.Y + (int)(((long)d * sin) >> Brad.UnitShift)) / (float)Fx.One));
			}
		}
		return outp.ToArray();
	}

	// ------------------------------------------------------------- visibility

	/// <summary>
	/// The player's visibility polygon: 400 rays at 430 px (spec §9). Computed
	/// here rather than in GDScript because it is 400 casts against every wall
	/// rect, every frame — the one piece of presentation maths that genuinely
	/// needs the faster side of the boundary.
	/// </summary>
	public Vector2[] GetVisionPolygon(int rays, int radiusPx)
	{
		if (rays < 8) rays = 8;
		var pts = new Vector2[rays];
		int radius = radiusPx * Fx.One;
		int ox = _snap.PlayerX, oy = _snap.PlayerY;

		// Pre-filter ONCE, then cast against the survivors, instead of testing
		// every rect on the level 400 times over. A rect a ray could hit within
		// `radius` must intersect the bounding box of that radius, so this
		// changes no result — it only stops a large floor from charging the
		// frame for walls three rooms away. On a one-screen level nothing is
		// filtered out and this costs one pass over ~20 rects.
		var near = NearbyWalls(ox, oy, radius);

		for (int i = 0; i < rays; i++)
		{
			int brad = (int)((long)i * Brad.Full / rays);
			int d = Geometry.CastRay(near, ox, oy, brad, radius);
			Brad.SinCos(brad, out int sin, out int cos);
			int px = ox + (int)(((long)d * cos) >> Brad.UnitShift);
			int py = oy + (int)(((long)d * sin) >> Brad.UnitShift);
			pts[i] = new Vector2(px / (float)Fx.One, py / (float)Fx.One);
		}
		return pts;
	}

	/// <summary>
	/// The same polygon with the pre-filter skipped. Exists ONLY so the harness
	/// can assert that filtering changes no result; nothing in game/ calls it.
	/// </summary>
	public Vector2[] GetVisionPolygonUnfiltered(int rays, int radiusPx)
	{
		if (rays < 8) rays = 8;
		var pts = new Vector2[rays];
		int radius = radiusPx * Fx.One;
		int ox = _snap.PlayerX, oy = _snap.PlayerY;

		for (int i = 0; i < rays; i++)
		{
			int brad = (int)((long)i * Brad.Full / rays);
			int d = Geometry.CastRay(_world.Opaque, ox, oy, brad, radius);
			Brad.SinCos(brad, out int sin, out int cos);
			int px = ox + (int)(((long)d * cos) >> Brad.UnitShift);
			int py = oy + (int)(((long)d * sin) >> Brad.UnitShift);
			pts[i] = new Vector2(px / (float)Fx.One, py / (float)Fx.One);
		}
		return pts;
	}

	/// <summary>
	/// Wall rects whose bounds overlap the square of side 2*radius centred on
	/// (ox, oy). A conservative superset of what a ray of that length can
	/// reach, so the polygon it produces is identical to the brute-force one.
	/// </summary>
	private Rect[] NearbyWalls(int ox, int oy, int radius)
	{
		// The LIVE opaque set, not the level's walls: a shut door hides the room
		// behind it, and glass hides nothing.
		var all = _world.Opaque;
		int x0 = ox - radius, x1 = ox + radius;
		int y0 = oy - radius, y1 = oy + radius;

		int n = 0;
		for (int i = 0; i < all.Length; i++)
			if (Overlaps(in all[i], x0, y0, x1, y1)) n++;

		if (n == all.Length) return all;          // nothing to gain; skip the copy

		var near = new Rect[n];
		int k = 0;
		for (int i = 0; i < all.Length; i++)
			if (Overlaps(in all[i], x0, y0, x1, y1)) near[k++] = all[i];
		return near;
	}

	private static bool Overlaps(in Rect r, int x0, int y0, int x1, int y1)
		=> r.X < x1 && r.X + r.W > x0 && r.Y < y1 && r.Y + r.H > y0;


	// =====================================================================
	// Level editor (spec §11 milestone 6)
	//
	// The edit buffer is a real sim Level, so the text format has exactly one
	// implementation and the editor cannot drift from the parser. Everything
	// here operates on glyphs; nothing here knows how a level is drawn.
	// =====================================================================

	private Level _edit = Level.Blank();
	private int _selectedGuard;

	public void EditorBeginFrom(string text) { _edit = Level.FromText(text); _selectedGuard = 0; }
	public void EditorBeginBlank() { _edit = Level.Blank(); _edit.Name = "untitled"; _selectedGuard = 0; }
	public void EditorBeginFromCurrent() { EditorBeginFrom(_levelText); }

	public string EditorToText() => _edit.ToText();

	/// <summary>
	/// Resize the level being edited, keeping the overlapping region and every
	/// route. Returns how many NON-FLOOR cells fell outside the new bounds and
	/// were dropped, so the editor can say so out loud instead of quietly
	/// eating a room. Shrinking is allowed — it is undoable like any other edit
	/// — but it is never silent.
	/// </summary>
	public int EditorResize(int cols, int rows)
	{
		var next = Level.Blank(cols, rows);
		next.Name = _edit.Name;
		next.Theme = _edit.Theme;

		int dropped = 0;
		for (int r = 0; r < _edit.H; r++)
		{
			for (int c = 0; c < _edit.W; c++)
			{
				char ch = _edit.At(c, r);
				if (next.InBounds(c, r)) next.Set(c, r, ch);
				else if (ch != '.') dropped++;
			}
		}

		// The new border has to be wall, or a resized level leaks at the seam.
		for (int r = 0; r < next.H; r++)
			for (int c = 0; c < next.W; c++)
				if (r == 0 || r == next.H - 1 || c == 0 || c == next.W - 1)
					next.Set(c, r, '#');

		foreach (var kv in _edit.Routes) next.Routes[kv.Key] = new List<(int, int)>(kv.Value);

		next.Build();
		_edit = next;
		return dropped;
	}

	public string EditorName
	{
		get => _edit.Name;
		set => _edit.Name = string.IsNullOrWhiteSpace(value) ? "untitled" : value;
	}

	/// <summary>The edited level's theme token; set through the same cleaner
	/// the parser uses, so the editor cannot write one the parser would cut.</summary>
	public string EditorTheme
	{
		get => _edit.Theme;
		set => _edit.Theme = Level.CleanTheme(value);
	}

	public int EditorSelectedGuard => _selectedGuard;

	/// <summary><c>ambient:</c> of the level being edited: -1 fully lit (no
	/// line), else 0..100. Clamped the way the parser clamps.</summary>
	public int EditorAmbient
	{
		get => _edit.Ambient;
		set => _edit.Ambient = value < 0 ? -1 : (value > 100 ? 100 : value);
	}

	/// <summary>
	/// What the sim WILL light, for the editor's preview: the edit buffer at
	/// rest (doors shut, every lamp on), through the same LightMap a run
	/// builds. Texture2x layout; empty for a fully lit level.
	/// </summary>
	public byte[] EditorLightPreview()
	{
		var L = Level.FromText(_edit.ToText());
		return L.AmbientQ8 >= Fx.One ? System.Array.Empty<byte>() : LightMap.AtRest(L).Texture2x();
	}

	/// <summary>The whole grid as glyph bytes, row-major.</summary>
	public byte[] EditorGetGrid()
	{
		var g = new byte[_edit.Grid.Length];
		for (int i = 0; i < g.Length; i++) g[i] = (byte)_edit.Grid[i];
		return g;
	}

	public int EditorGetCell(int c, int r)
		=> !_edit.InBounds(c, r) ? 0 : _edit.At(c, r);

	/// <summary>
	/// Apply one editor tool at a cell. Tool codes match game/editor.gd's
	/// palette order. Returns the glyph now in that cell, or 0 if nothing
	/// changed. Semantics are the prototype's: spawn is unique, exit and cache
	/// toggle, clicking an existing guard selects it rather than replacing it.
	/// </summary>
	public int EditorPaint(int c, int r, int tool, bool erase)
	{
		if (!_edit.InBounds(c, r)) return 0;
		char cur = _edit.At(c, r);

		if (erase)
		{
			if (Level.IsGuardGlyph(cur))
			{
				_edit.Routes.Remove(cur);
				if (_selectedGuard == cur) _selectedGuard = 0;
			}
			_edit.Set(c, r, '.');
			return '.';
		}

		switch (tool)
		{
			case 0: _edit.Set(c, r, '#'); break;
			case 1: 
				if (Level.IsGuardGlyph(cur)) _edit.Routes.Remove(cur);
				_edit.Set(c, r, '.');
				break;
			case 2:
				// Spawn is unique: the parser takes the last one, so leaving
				// strays would make the level depend on scan order.
				for (int rr = 0; rr < _edit.H; rr++)
					for (int cc = 0; cc < _edit.W; cc++)
						if (_edit.At(cc, rr) == '@') _edit.Set(cc, rr, '.');
				_edit.Set(c, r, '@');
				break;
			case 3: _edit.Set(c, r, cur == 'X' ? '.' : 'X'); break;
			case 4: _edit.Set(c, r, cur == '$' ? '.' : '$'); break;
			case 5:
			{
				if (Level.IsGuardGlyph(cur)) { _selectedGuard = cur; break; }
				int id = EditorNextGuardId();
				if (id == 0) break;
				_edit.Set(c, r, (char)id);
				var pts = new List<(int C, int R)> { (c, r) };
				_edit.Routes[(char)id] = pts;
				_selectedGuard = id;
				break;
			}
			case 7: _edit.Set(c, r, cur == 'C' ? '.' : 'C'); break;
			case 8: PaintPanel(c, r, cur, Level.GlassGlyph); break;
			case 9: PaintPanel(c, r, cur, Level.DoorGlyph); break;
			case 10: _edit.Set(c, r, cur == '!' ? '.' : '!'); break;
			// Lighting (codes 12, 13): a lamp and a switch are FIXTURES on
			// floor, one per click, toggling like a chest. Like a sweep node
			// they never knock through a wall, glass or a door.
			case 12:
			case 13:
			{
				char glyph = tool == 12 ? Level.LampGlyph : Level.SwitchGlyph;
				if (cur == '#' || cur == Level.GlassGlyph || cur == Level.DoorGlyph) break;
				if (Level.IsGuardGlyph(cur))
				{
					_edit.Routes.Remove(cur);
					if (_selectedGuard == cur) _selectedGuard = 0;
				}
				_edit.Set(c, r, cur == glyph ? '.' : glyph);
				break;
			}
			case 11:
				// A sweep node marks FLOOR the guards should check (Guard_AI.md
				// §6.3.1): it does not knock through walls, glass or doors, and
				// a guard standing there loses his route, as with the floor tool.
				if (cur == '#' || cur == Level.GlassGlyph || cur == Level.DoorGlyph) break;
				if (Level.IsGuardGlyph(cur))
				{
					_edit.Routes.Remove(cur);
					if (_selectedGuard == cur) _selectedGuard = 0;
				}
				_edit.Set(c, r, cur == Level.SweepGlyph ? '.' : Level.SweepGlyph);
				break;
			case 6:
			{
				if (_selectedGuard == 0) break;
				char sel = (char)_selectedGuard;
				if (!_edit.Routes.TryGetValue(sel, out var route))
				{
					route = new List<(int C, int R)>();
					_edit.Routes[sel] = route;
				}
				route.Add((c, r));
				break;
			}
		}
		return _edit.At(c, r);
	}

	/// <summary>
	/// Glass and doors are painted INTO walls, the way they are built: over a
	/// wall or floor cell they replace it, and a guard standing there loses his
	/// route with him, as the floor tool does.
	/// </summary>
	private void PaintPanel(int c, int r, char cur, char glyph)
	{
		if (Level.IsGuardGlyph(cur))
		{
			_edit.Routes.Remove(cur);
			if (_selectedGuard == cur) _selectedGuard = 0;
		}
		_edit.Set(c, r, glyph);
	}

	/// <summary>
	/// The glyphs a brush, shape, fill or paste may lay down: everything that is
	/// STRUCTURE. Spawn and guards are actors — spawn is unique and a guard owns
	/// a letter and a route — so they are placed one click at a time through
	/// EditorPaint, never stamped in bulk.
	/// </summary>
	public bool EditorStampable(int glyph)
	{
		char ch = (char)glyph;
		return ch == '#' || ch == '.' || ch == 'X' || ch == '$' || ch == 'C' || ch == '!'
			|| ch == Level.GlassGlyph || ch == Level.DoorGlyph;
	}

	/// <summary>
	/// Set one cell to a structural glyph outright. Unlike EditorPaint nothing
	/// toggles: a brush dragged back over its own stroke must not undo it cell
	/// by cell. Overwriting a guard takes his route with him. Returns the glyph
	/// written, or 0 if the cell already held it or the glyph is not stampable.
	/// </summary>
	public int EditorStamp(int c, int r, int glyph)
	{
		if (!_edit.InBounds(c, r) || !EditorStampable(glyph)) return 0;
		char cur = _edit.At(c, r);
		if (cur == glyph) return 0;
		if (Level.IsGuardGlyph(cur))
		{
			_edit.Routes.Remove(cur);
			if (_selectedGuard == cur) _selectedGuard = 0;
		}
		_edit.Set(c, r, (char)glyph);
		return glyph;
	}

	/// <summary>
	/// Flood the 4-connected region of whatever glyph is at (c, r) with a
	/// structural glyph. Returns how many cells changed. Four-connected, not
	/// eight: a room sealed by a diagonal run of wall is sealed for a guard,
	/// so it must be sealed for the bucket too.
	/// </summary>
	public int EditorFlood(int c, int r, int glyph)
	{
		if (!_edit.InBounds(c, r) || !EditorStampable(glyph)) return 0;
		char from = _edit.At(c, r);
		if (from == glyph) return 0;

		var seen = new bool[_edit.W * _edit.H];
		var queue = new Queue<(int C, int R)>();
		queue.Enqueue((c, r));
		seen[r * _edit.W + c] = true;
		int changed = 0;
		while (queue.Count > 0)
		{
			var (qc, qr) = queue.Dequeue();
			if (EditorStamp(qc, qr, glyph) != 0) changed++;
			Visit(qc + 1, qr); Visit(qc - 1, qr); Visit(qc, qr + 1); Visit(qc, qr - 1);
		}
		return changed;

		void Visit(int vc, int vr)
		{
			if (!_edit.InBounds(vc, vr)) return;
			int i = vr * _edit.W + vc;
			if (seen[i] || _edit.Grid[i] != from) return;
			seen[i] = true;
			queue.Enqueue((vc, vr));
		}
	}

	// ------------------------------------------------ loot assignment

	/// <summary>The points a guard buys his kit with, as the level stands.</summary>
	public int EditorGuardPoints(int id)
		=> Level.IsGuardGlyph((char)id) ? _edit.PointsFor((char)id) : 0;

	/// <summary>
	/// Assign one guard's points: the point-buy ASSIGNMENT. Setting a guard back
	/// to the level's default removes his override rather than writing a line
	/// that says the same thing, so a level carries only the assignments that
	/// mean something.
	/// </summary>
	public void EditorSetGuardPoints(int id, int points)
	{
		if (!Level.IsGuardGlyph((char)id)) return;
		char g = (char)id;
		int p = System.Math.Clamp(points, 0, Level.MaxLoot);
		_edit.KitPoints.Remove(g);
		if (p != _edit.PointsFor(g)) _edit.KitPoints[g] = p;
	}

	/// <summary>The chests' dollar budget as the edit buffer stands -- through
	/// the parser, since an unauthored budget follows the chests painted.</summary>
	public int EditorChestBudget => Level.FromText(_edit.ToText()).ChestBudget;

	/// <summary>Author the chest budget. -1 goes back to deriving it.</summary>
	public void EditorSetChestBudget(int dollars)
		=> _edit.LootBudget = dollars < 0 ? -1 : System.Math.Min(dollars, Level.MaxLoot);

	/// <summary>Every guard's points together, as the mission select will show.</summary>
	public int EditorGuardLootTotal => Level.FromText(_edit.ToText()).GuardLootTotal;

	/// <summary>Whether a grid byte is a guard: 'a'..'z' or one of the Latin-1
	/// letters past them (Level.GuardGlyphs). game/ asks this rather than
	/// testing a range, since the alphabet is not one range.</summary>
	public bool IsGuardGlyph(int ch) => ch >= 0 && ch < 256 && Level.IsGuardGlyph((char)ch);

	/// <summary>The guard alphabet, for the editor's issue links.</summary>
	public string GuardGlyphs => Level.GuardGlyphs;

	/// <summary>Lowest unused guard letter, or 0 when every one is placed.</summary>
	public int EditorNextGuardId()
	{
		foreach (char ch in Level.GuardGlyphs)
		{
			bool used = false;
			for (int i = 0; i < _edit.Grid.Length && !used; i++)
				if (_edit.Grid[i] == ch) used = true;
			if (!used) return ch;
		}
		return 0;
	}

	public void EditorSelectGuard(int id) => _selectedGuard = Level.IsGuardGlyph((char)id) ? id : 0;

	public void EditorRouteClear(int id)
	{
		if (Level.IsGuardGlyph((char)id)) _edit.Routes.Remove((char)id);
	}

	public void EditorRouteUndoPoint(int id)
	{
		if (!Level.IsGuardGlyph((char)id)) return;
		if (_edit.Routes.TryGetValue((char)id, out var r) && r.Count > 0) r.RemoveAt(r.Count - 1);
	}

	/// <summary>
	/// Route editing by INDEX, so a waypoint in the middle of a patrol can be
	/// moved, inserted or removed without clearing the route and re-clicking
	/// every point after it. Each returns false, changing nothing, for a guard
	/// with no route, an index out of range or a cell outside the grid.
	/// Insert takes index == Count to append.
	/// </summary>
	public bool EditorRouteMovePoint(int id, int index, int c, int r)
	{
		if (!TryRoute(id, out var route) || index < 0 || index >= route.Count || !_edit.InBounds(c, r))
			return false;
		route[index] = (c, r);
		return true;
	}

	public bool EditorRouteInsertPoint(int id, int index, int c, int r)
	{
		if (!TryRoute(id, out var route) || index < 0 || index > route.Count || !_edit.InBounds(c, r))
			return false;
		route.Insert(index, (c, r));
		return true;
	}

	public bool EditorRouteRemovePoint(int id, int index)
	{
		if (!TryRoute(id, out var route) || index < 0 || index >= route.Count) return false;
		route.RemoveAt(index);
		return true;
	}

	private bool TryRoute(int id, [NotNullWhen(true)] out List<(int C, int R)>? route)
	{
		route = null;
		return Level.IsGuardGlyph((char)id) && _edit.Routes.TryGetValue((char)id, out route);
	}

	/// <summary>Flat c,r pairs for one guard's route.</summary>
	public int[] EditorGetRoute(int id)
	{
		if (!Level.IsGuardGlyph((char)id) || !_edit.Routes.TryGetValue((char)id, out var r))
			return System.Array.Empty<int>();
		var outp = new int[r.Count * 2];
		for (int i = 0; i < r.Count; i++) { outp[i * 2] = r[i].C; outp[i * 2 + 1] = r[i].R; }
		return outp;
	}

	/// <summary>Guard letters that exist on the grid, ascending.</summary>
	public byte[] EditorGuardIds()
	{
		var ids = new List<byte>();
		foreach (char ch in Level.GuardGlyphs)
			for (int i = 0; i < _edit.Grid.Length; i++)
				if (_edit.Grid[i] == ch) { ids.Add((byte)ch); break; }
		return ids.ToArray();
	}

	/// <summary>Wall rects after the greedy merge — the number that actually
	/// drives raycast cost (spec §2.2), shown live while painting.</summary>
	public int EditorMergedRectCount() => Level.MergeWalls(_edit.Grid, _edit.W, _edit.H).Length;

	public int EditorWallCellCount()
	{
		int n = 0;
		for (int i = 0; i < _edit.Grid.Length; i++) if (_edit.Grid[i] == '#') n++;
		return n;
	}

	/// <summary>
	/// Authoring-time problems, most severe first. Prefixed "error", "warn" or
	/// "info" so the editor can colour them. The parser is total, so none of
	/// these prevent play — they predict a level that plays badly.
	/// </summary>
	public string[] EditorValidate()
	{
		var outp = new List<string>();

		var built = Level.FromText(_edit.ToText());

		bool hasSpawn = false, hasExit = false;
		for (int i = 0; i < _edit.Grid.Length; i++)
		{
			if (_edit.Grid[i] == '@') hasSpawn = true;
			else if (_edit.Grid[i] == 'X') hasExit = true;
		}

		if (!hasSpawn) outp.Add("warn: no spawn - the parser will default to cell (1,1)");
		if (!hasExit) outp.Add("warn: no exit - the parser will default to a 2x2 at the far corner");

		if (!built.ExitReachable())
			outp.Add("error: the exit is not reachable from spawn");
		else if (!built.ExitReachable(glassBlocks: true))
			outp.Add("warn: the only way out is through glass - reachable, but loudly");
		// With several exits, the level is completable if ANY is reachable, so a
		// sealed second exit is a warning naming it, not an error.
		if (built.Exits.Count > 1 && built.ExitReachable())
		{
			for (int k = 0; k < built.Exits.Count; k++)
			{
				if (built.ExitReachable(which: k)) continue;
				var e = built.Exits[k];
				outp.Add($"warn: exit at ({e.X / Level.CellFx},{e.Y / Level.CellFx}) is not reachable from spawn");
			}
		}
		int blobs = CountExitBlobs(built);
		if (blobs > Level.MaxExits)
			outp.Add($"warn: {blobs} separate exits - only the first {Level.MaxExits} count, the rest are floor");

		// A door is a leaf in a WALL. One standing in open floor is a door you
		// walk round, and a one-cell door is one nobody fits through (22 px of
		// guard, 20 px of cell).
		for (int i = 0; i < built.Panels.Count; i++)
		{
			var pd = built.Panels[i];
			int pc = pd.Rect.X / Level.CellFx, pr = pd.Rect.Y / Level.CellFx;
			int cells = (pd.Rect.W / Level.CellFx) * (pd.Rect.H / Level.CellFx);
			if (pd.Kind != PanelKind.Door) continue;
			if (cells < 2)
				outp.Add($"warn: door at ({pc},{pr}) is one cell - too narrow to walk through");
			if (!InWall(built, pd))
				outp.Add($"warn: door at ({pc},{pr}) is not set in a wall");
		}

		var ids = EditorGuardIds();
		foreach (byte id in ids)
		{
			int[] route = EditorGetRoute(id);
			if (route.Length == 0)
			{
				outp.Add($"info: guard {(char)id} has no route - it will be a stationary sentry");
				continue;
			}
			for (int i = 0; i < route.Length; i += 2)
			{
				int c = route[i], r = route[i + 1];
				if (!_edit.InBounds(c, r))
					outp.Add($"error: guard {(char)id} waypoint ({c},{r}) is outside the grid");
				else if (_edit.At(c, r) == '#')
					outp.Add($"error: guard {(char)id} waypoint ({c},{r}) is inside a wall");
			}
		}

		if (ids.Length == 0) outp.Add("info: no guards placed");

		// Lighting. A lamp on a fully lit level lights nothing; a switch with
		// no lamp in its room does nothing; one in open floor is hard to find.
		if (built.AmbientQ8 >= Fx.One && (built.Lamps.Count > 0 || built.Switches.Count > 0))
			outp.Add("warn: lamps and switches do nothing on a fully lit level - set ambient (D)");
		foreach (var (c, r) in built.Switches)
		{
			var room = built.RoomOf(c, r);
			bool wired = false;
			foreach (var (lc, lr) in built.Lamps) if (room[lr * built.W + lc]) wired = true;
			if (!wired) outp.Add($"warn: switch at ({c},{r}) has no lamp in its room - it does nothing");
			bool wall = false;
			foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
				if (!built.InBounds(c + dc, r + dr) || built.At(c + dc, r + dr) == '#') wall = true;
			if (!wall) outp.Add($"info: switch at ({c},{r}) is not against a wall");
		}
		if (built.AmbientQ8 < Fx.One)
		{
			var map = LightMap.AtRest(built);
			foreach (var e in built.Exits)
			{
				int ex = e.X + e.W / 2, ey = e.Y + e.H / 2;
				if (map.LightAt(ex, ey) == 0)
					outp.Add($"info: the exit at ({e.X / Level.CellFx},{e.Y / Level.CellFx}) is in pitch dark");
			}
			foreach (var ch in built.Chests)
				if (ch.Objective && map.LightAt(ch.X, ch.Y) == 0)
					outp.Add($"info: the objective at ({ch.X / Level.CellFx},{ch.Y / Level.CellFx}) is in pitch dark");
		}

		// A sweep node nobody can walk to is a node the sweep ignores.
		if (built.SweepNodes.Count > 0)
		{
			var nav = built.Nav;
			var regions = new List<int>();
			foreach (var g in built.Guards)
			{
				int gc = nav.NearestPassable(g.X, g.Y);
				if (gc >= 0 && !regions.Contains(nav.Region[gc])) regions.Add(nav.Region[gc]);
			}
			if (regions.Count == 0)
			{
				int sc = nav.NearestPassable(built.SpawnX, built.SpawnY);
				if (sc >= 0) regions.Add(nav.Region[sc]);
			}
			foreach (var (c, r) in built.SweepNodes)
			{
				int cell = nav.NearestPassable(c * Level.CellFx + Level.CellFx / 2, r * Level.CellFx + Level.CellFx / 2);
				if (cell < 0 || !regions.Contains(nav.Region[cell]))
					outp.Add($"warn: sweep node at ({c},{r}) cannot be reached by any guard - it will be ignored");
			}
		}

		// Round-trip is the format's contract (spec §2.1); a level that fails it
		// would not survive being saved and reloaded.
		string once = _edit.ToText();
		if (Level.FromText(once).ToText() != once)
			outp.Add("error: this level does not survive a save/load round-trip");

		outp.Add($"info: {EditorWallCellCount()} wall cells merge to {EditorMergedRectCount()} rects");
		return outp.ToArray();
	}

	/// <summary>Separate 8-connected blobs of 'X', UNCAPPED -- Level.Exits
	/// stops at MaxExits, and the point here is to say what it dropped.</summary>
	private static int CountExitBlobs(Level L)
	{
		var seen = new bool[L.W * L.H];
		var stack = new Stack<int>();
		int blobs = 0;
		for (int i = 0; i < L.Grid.Length; i++)
		{
			if (L.Grid[i] != 'X' || seen[i]) continue;
			blobs++;
			seen[i] = true;
			stack.Push(i);
			while (stack.Count > 0)
			{
				int cur = stack.Pop();
				int c = cur % L.W, r = cur / L.W;
				for (int dr = -1; dr <= 1; dr++)
					for (int dc = -1; dc <= 1; dc++)
					{
						if (!L.InBounds(c + dc, r + dr)) continue;
						int n = (r + dr) * L.W + c + dc;
						if (seen[n] || L.Grid[n] != 'X') continue;
						seen[n] = true;
						stack.Push(n);
					}
			}
		}
		return blobs;
	}

	/// <summary>Both ends of the panel butt against wall: the cells just past
	/// each end along its length are '#'.</summary>
	private static bool InWall(Level L, PanelDef pd)
	{
		int c0 = pd.Rect.X / Level.CellFx, r0 = pd.Rect.Y / Level.CellFx;
		int c1 = (pd.Rect.X + pd.Rect.W) / Level.CellFx - 1;
		int r1 = (pd.Rect.Y + pd.Rect.H) / Level.CellFx - 1;
		bool Wall(int c, int r) => !L.InBounds(c, r) || L.At(c, r) == '#';
		return pd.Vertical
			? Wall(c0, r0 - 1) && Wall(c0, r1 + 1)
			: Wall(c0 - 1, r0) && Wall(c1 + 1, r0);
	}

	/// <summary>Load the edit buffer into the live world and start playing it.</summary>
	public void EditorPlaytest(ulong seed)
	{
		Load(_edit.ToText(), seed);
		_playback = null;
		_playbackIndex = 0;
		_divergedTick = -1;
	}

	/// <summary>Heading in BRAD from the player to a point, for aim.</summary>
	public static int BradFromVector(float dx, float dy)
		=> Brad.Atan2((int)(dy * Fx.One), (int)(dx * Fx.One));
}
