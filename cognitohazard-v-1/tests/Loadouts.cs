using System;
using System.Collections.Generic;
using System.IO;
using Cognitohazard.Sim;

namespace Cognitohazard.Tests;

/// <summary>
/// Milestone 7, the loadout spine (RPG extension plan §1, §5).
///
/// The load-bearing assertion in this file is PARITY: the default loadout must
/// reproduce the constants the game shipped with before loadouts existed. If
/// that holds, every pre-existing test in the suite is still testing what it
/// was written to test, and the refactor is provably behaviour-preserving.
/// </summary>
public static class Loadouts
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
	private static int CellCentre(int cell) => cell * Level.CellFx + Level.CellFx / 2;

	private static string LevelsDir()
	{
		var d = new DirectoryInfo(AppContext.BaseDirectory);
		while (d != null && !Directory.Exists(Path.Combine(d.FullName, "levels"))) d = d.Parent;
		return d == null ? "levels" : Path.Combine(d.FullName, "levels");
	}

	private static string RefLevel() => File.ReadAllText(Path.Combine(LevelsDir(), "substation_4.txt"));

	// ---------------------------------------------------------------- parity

	private static void Parity()
	{
		H.Group("loadout parity");

		H.Eq("default(Loadout) is the Glock", (int)default(Loadout).Weapon, (int)WeaponId.Glock);
		H.Eq("Loadout.Default is the Glock", (int)Loadout.Default.Weapon, (int)WeaponId.Glock);

		// The Glock inherits the handling of the pre-loadout pistol. These are
		// the fields milestone 7 promised would not move, and milestone 9 did
		// not move them.
		var p = WeaponCatalog.Get(WeaponId.Glock);
		H.Eq("glock fire cooldown", p.FireCooldownTicks, Tune.FireCooldownTicks);
		H.Eq("glock reload", p.ReloadTicks, Tune.ReloadTicks);
		H.Eq("glock dry fire", p.DryFireTicks, Tune.DryFireTicks);
		H.Eq("glock spread base", p.SpreadBase, Tune.SpreadBase);
		H.Eq("glock spread per heat", p.SpreadPerHeat, Tune.SpreadPerHeat);
		H.Eq("glock heat per shot", p.HeatPerShot, Tune.HeatPerShot);
		H.Eq("glock heat decay", p.HeatDecayPerSec, Tune.HeatDecayPerSec);
		H.Eq("glock bullet speed", p.BulletSpeed, Tune.PlayerBulletSpeed);
		H.Eq("glock bullet life", p.BulletTicks, Tune.PlayerBulletTicks);
		H.Eq("glock muzzle offset", p.MuzzleOffset, Tune.MuzzleOffset);
		H.Eq("glock fires one projectile", p.Pellets, 1);

		// TWO fields moved at milestone 9, both deliberately and both named in
		// the RPG plan: a Glock 17 carries seventeen rounds, and its report
		// drops from the old flat 640 px to 400 px so that weapon choice is a
		// stealth decision rather than a damage one.
		H.Eq("glock carries 17 rounds", p.Magazine, 17);
		H.Eq("glock report retuned to 400px", p.GunshotRadius, 400 * Fx.One);
		H.Check("which is quieter than the old flat radius", p.GunshotRadius < Tune.GunRange);

		var w = new SimWorld(Level.FromText(RefLevel()), 1);
		H.Eq("default world starts with a full Glock", w.Player.Mag, 17);

		var sg = new SimWorld(Level.FromText(RefLevel()), 1, new Loadout(WeaponId.Remington));
		H.Eq("a Remington world starts with 8 shells", sg.Player.Mag, 8);
	}

	// --------------------------------------------------------------- catalog

	private static void Catalog()
	{
		H.Group("weapon catalog");

		H.Eq("thirteen weapons", WeaponCatalog.Count, 13);

		// Ordinals are in the replay format and the state hash, so they are
		// fixed forever. Pinned individually: a reorder would silently rewrite
		// every saved stash and every recording on disk.
		H.Eq("Glock is still 0", (int)WeaponId.Glock, 0);
		H.Eq("SAW is still 4", (int)WeaponId.Saw, 4);
		H.Eq("Welrod is 5", (int)WeaponId.Welrod, 5);
		H.Eq("VSS is 6", (int)WeaponId.Vss, 6);
		H.Eq("Photon is 7", (int)WeaponId.Photon, 7);
		H.Eq("Arc Lance is 8", (int)WeaponId.ArcLance, 8);
		H.Eq("Vulcan is 9", (int)WeaponId.Vulcan, 9);
		H.Eq("Tesla is 10", (int)WeaponId.Tesla, 10);
		H.Eq("Frag is 11", (int)WeaponId.Frag, 11);
		H.Eq("AWM is 12", (int)WeaponId.Awm, 12);

		// Every weapon must be complete, or one of them is a trap: a zero
		// magazine cannot fire and a zero-speed round never arrives.
		for (int i = 0; i < WeaponCatalog.Count; i++)
		{
			var id = (WeaponId)i;
			var sp = WeaponCatalog.Get(id);
			string n = WeaponCatalog.NameOf(id);
			H.Check($"{n} is fully specified",
				sp.Damage > 0 && sp.Magazine > 0 && sp.BulletSpeed > 0
				&& sp.BulletTicks > 0 && sp.FireCooldownTicks > 0
				&& sp.ReloadTicks > 0 && sp.Pellets >= 1 && sp.GunshotRadius > 0
				&& sp.TurnNum >= Tune.TurnAimMin,
				$"dmg {sp.Damage} mag {sp.Magazine} spd {sp.BulletSpeed}");
			H.Check($"{n} has a name and a class",
				n.Length > 0 && WeaponCatalog.ClassOf(id).Length > 0);
			H.Check($"{n} accepts at least one attachment slot",
				WeaponCatalog.SlotMask(id) != 0);
		}

		// ---- the silenced family ----
		var welrod = WeaponCatalog.Get(WeaponId.Welrod);
		var vss = WeaponCatalog.Get(WeaponId.Vss);
		H.Check("the Welrod is quieter than any ballistic weapon",
			welrod.GunshotRadius < WeaponCatalog.Get(WeaponId.Glock).GunshotRadius / 2,
			$"{welrod.GunshotRadius / Fx.One}px");
		H.Check("and the VSS is quieter than any rifle",
			vss.GunshotRadius < WeaponCatalog.Get(WeaponId.Ak47).GunshotRadius / 2);
		H.Check("but silence costs cadence",
			welrod.FireCooldownTicks > WeaponCatalog.Get(WeaponId.Glock).FireCooldownTicks
			&& vss.FireCooldownTicks > WeaponCatalog.Get(WeaponId.Ak47).FireCooldownTicks);
		H.Check("and capacity", welrod.Magazine < WeaponCatalog.Get(WeaponId.Glock).Magazine
			&& vss.Magazine < WeaponCatalog.Get(WeaponId.Ak47).Magazine);
		H.Check("the VSS drops a bare guard in one", vss.Damage >= Tune.GuardHealth);

		// Subsonic ammo must still be the quietest thing available, or the
		// attachment has been made pointless by a weapon.
		int subsonicGlock = new Loadout(WeaponId.Glock, ArmourId.None, ammo: 1)
			.Spec.GunshotRadius;
		H.Check("subsonic ammo is still the quietest option in the game",
			subsonicGlock < welrod.GunshotRadius,
			$"subsonic {subsonicGlock / Fx.One}px vs Welrod {welrod.GunshotRadius / Fx.One}px");

		// ---- the energy family ----
		var photon = WeaponCatalog.Get(WeaponId.Photon);
		var lance = WeaponCatalog.Get(WeaponId.ArcLance);
		H.Check("energy weapons pierce armour",
			photon.ArmourPierce > 0 && lance.ArmourPierce > 0);
		H.Check("and nothing ballistic does",
			WeaponCatalog.Get(WeaponId.Ak47).ArmourPierce == 0
			&& WeaponCatalog.Get(WeaponId.Saw).ArmourPierce == 0);
		H.Check("the Arc Lance pierces more than the Photon",
			lance.ArmourPierce > photon.ArmourPierce);
		H.Check("a bolt outruns anything ballistic",
			photon.BulletSpeed > WeaponCatalog.Get(WeaponId.Ak47).BulletSpeed * 3 / 2,
			$"{photon.BulletSpeed / Fx.One} vs {WeaponCatalog.Get(WeaponId.Ak47).BulletSpeed / Fx.One} px/s");
		H.Check("but they overheat far faster than they cool",
			photon.HeatDecayPerSec < Tune.HeatDecayPerSec
			&& lance.HeatDecayPerSec < Tune.HeatDecayPerSec);
		H.Check("and an overheated bolt is wild",
			photon.SpreadPerHeat > WeaponCatalog.Get(WeaponId.Ak47).SpreadPerHeat);

		// ---- the rotary family ----
		var vulcan = WeaponCatalog.Get(WeaponId.Vulcan);
		H.Check("only the Vulcan has a spin-up", vulcan.SpinUpTicks > 0);
		bool othersInstant = true;
		for (int i = 0; i < WeaponCatalog.Count; i++)
			if ((WeaponId)i != WeaponId.Vulcan && WeaponCatalog.Get((WeaponId)i).SpinUpTicks != 0)
				othersInstant = false;
		H.Check("and nothing else does", othersInstant);
		H.Check("it holds more than anything else",
			vulcan.Magazine > WeaponCatalog.Get(WeaponId.Saw).Magazine);
		H.Check("cycles faster than anything else",
			vulcan.FireCooldownTicks < WeaponCatalog.Get(WeaponId.Saw).FireCooldownTicks);
		H.Check("is the loudest thing in the game",
			vulcan.GunshotRadius > WeaponCatalog.Get(WeaponId.Saw).GunshotRadius);
		H.Check("and roots you hardest",
			vulcan.SpeedDelta < WeaponCatalog.Get(WeaponId.Saw).SpeedDelta
			&& vulcan.AimMoveQ8 < WeaponCatalog.Get(WeaponId.Saw).AimMoveQ8);
		H.Check("and is the worst thing to turn with",
			vulcan.TurnNum < WeaponCatalog.Get(WeaponId.Saw).TurnNum);

		// Every weapon is buyable and has a gear item, or it exists in the sim
		// and nowhere a player can reach.
		for (int i = 0; i < WeaponCatalog.Count; i++)
		{
			var id = (WeaponId)i;
			int itemId = -1;
			for (int k = 0; k < GearCatalog.Count; k++)
			{
				var it = GearCatalog.At(k);
				if (it.Kind == GearKind.Weapon && it.SimA == i) itemId = it.Id;
			}
			H.Check($"{WeaponCatalog.NameOf(id)} has a gear item", itemId > 0);
			if (itemId > 0 && id != WeaponId.Glock)
				H.Check($"{WeaponCatalog.NameOf(id)} can be bought",
					GearCatalog.PriceOf(itemId) > 0);
		}

		// Unknown ids fall back rather than throwing, like the level parser.
		H.Eq("negative id clamps to pistol", (int)WeaponCatalog.Clamp(-1), (int)WeaponId.Glock);
		H.Eq("oversized id clamps to pistol", (int)WeaponCatalog.Clamp(99), (int)WeaponId.Glock);
		H.Eq("valid id passes through", (int)WeaponCatalog.Clamp(3), (int)WeaponId.Remington);
		H.Eq("the SAW is id 4", (int)WeaponCatalog.Clamp(4), (int)WeaponId.Saw);

		var pistol = WeaponCatalog.Get(WeaponId.Glock);
		var smg = WeaponCatalog.Get(WeaponId.Mp7);
		var rifle = WeaponCatalog.Get(WeaponId.Ak47);
		var shotgun = WeaponCatalog.Get(WeaponId.Remington);

		H.Eq("mp7 magazine", smg.Magazine, 40);
		H.Eq("ak47 magazine", rifle.Magazine, 30);
		H.Eq("remington magazine", shotgun.Magazine, 8);
		H.Eq("remington fires seven pellets", shotgun.Pellets, 7);
		H.Eq("saw belt", WeaponCatalog.Get(WeaponId.Saw).Magazine, 200);

		// Cadence ordering: smg fastest, shotgun slowest.
		H.Check("smg out-cycles the pistol", smg.FireCooldownTicks < pistol.FireCooldownTicks);
		H.Check("ak47 is slower than the mp7", rifle.FireCooldownTicks > smg.FireCooldownTicks);
		H.Check("remington is the slowest", shotgun.FireCooldownTicks > rifle.FireCooldownTicks);
		H.Check("the saw out-cycles everything",
			WeaponCatalog.Get(WeaponId.Saw).FireCooldownTicks < smg.FireCooldownTicks);

		// Accuracy ordering: rifle tightest, shotgun widest by a mile.
		H.Check("mp7 is wider than the glock", smg.SpreadBase > pistol.SpreadBase);
		H.Check("remington is the widest", shotgun.SpreadBase > smg.SpreadBase);

		// Noise ordering is the axis the plan actually cares about.
		H.Check("mp7 is louder than the glock", smg.GunshotRadius > pistol.GunshotRadius);
		H.Check("ak47 is louder than the mp7", rifle.GunshotRadius > smg.GunshotRadius);
		H.Check("remington is louder still", shotgun.GunshotRadius > rifle.GunshotRadius);
		H.Check("the saw is the loudest thing on the level",
			WeaponCatalog.Get(WeaponId.Saw).GunshotRadius > shotgun.GunshotRadius);

		// The shotgun's range falloff is emergent: short-lived rounds, not a curve.
		int reach = (int)((long)shotgun.BulletSpeed * shotgun.BulletTicks / Fx.TicksPerSecond);
		H.Check("shotgun rounds expire at roughly 560px",
			Math.Abs(reach / Fx.One - 560) <= 20, $"{reach / Fx.One}px");
		H.Check("ak47 reaches much further than the remington",
			(long)rifle.BulletSpeed * rifle.BulletTicks > (long)shotgun.BulletSpeed * shotgun.BulletTicks * 3);

		// Heavier weapons cost mobility, which is how the SAW is paid for.
		H.Eq("the glock has no handling penalty", pistol.SpeedDelta, 0);
		H.Check("the saw is the heaviest to carry",
			WeaponCatalog.Get(WeaponId.Saw).SpeedDelta < shotgun.SpeedDelta);

		H.Check("names are stable", WeaponCatalog.NameOf(WeaponId.Remington) == "Remington");
		H.Check("classes are stable", WeaponCatalog.ClassOf(WeaponId.Saw) == "machine gun");
	}

	// ------------------------------------------------------------ text form

	private static void TextForm()
	{
		H.Group("loadout text");

		foreach (WeaponId id in new[] { WeaponId.Glock, WeaponId.Mp7, WeaponId.Ak47,
			WeaponId.Remington, WeaponId.Saw })
		{
			var l = new Loadout(id);
			var back = Loadout.FromText(l.ToText());
			H.Eq($"{WeaponCatalog.NameOf(id)} round-trips", (int)back.Weapon, (int)id);
		}

		// Total parser, like every other parser in sim/.
		H.Eq("empty text is the default", (int)Loadout.FromText("").Weapon, (int)WeaponId.Glock);
		H.Eq("garbage is the default", (int)Loadout.FromText("!!! nonsense").Weapon, (int)WeaponId.Glock);
		H.Eq("missing value is the default", (int)Loadout.FromText("weapon=").Weapon, (int)WeaponId.Glock);
		H.Eq("non-numeric value is the default", (int)Loadout.FromText("weapon=zz").Weapon, (int)WeaponId.Glock);
		H.Eq("out-of-range value clamps", (int)Loadout.FromText("weapon=77").Weapon, (int)WeaponId.Glock);
		H.Eq("unknown keys are ignored", (int)Loadout.FromText("hat=3 weapon=2").Weapon, (int)WeaponId.Ak47);
	}

	// ------------------------------------------------------------- hashing

	private static void Hashing()
	{
		H.Group("loadout hashing");

		string level = RefLevel();

		var a = new SimWorld(Level.FromText(level), 5, new Loadout(WeaponId.Glock));
		var b = new SimWorld(Level.FromText(level), 5, new Loadout(WeaponId.Remington));

		// Caught before a single tick: the loadout is hashed first, so a replay
		// divergence points at the cause rather than at the first symptom.
		H.Check("different weapons hash differently at tick 0",
			a.StateHash() != b.StateHash());

		var c = new SimWorld(Level.FromText(level), 5, new Loadout(WeaponId.Glock));
		H.Check("the same weapon hashes identically", a.StateHash() == c.StateHash());

		var idle = new InputFrame(0, 0, 0, 0);
		for (int i = 0; i < 120; i++) { a.Step(idle); b.Step(idle); c.Step(idle); }
		H.Check("they stay different after 120 ticks", a.StateHash() != b.StateHash());
		H.Check("and identical loadouts stay identical", a.StateHash() == c.StateHash());
	}

	// -------------------------------------------------------------- replays

	private static void Replays()
	{
		H.Group("loadout in replays");

		string level = RefLevel();
		var inputs = new List<InputFrame>();
		var rng = new DetRng(4);
		for (int i = 0; i < 180; i++)
			inputs.Add(new InputFrame(rng.NextRange(-1, 1), rng.NextRange(-1, 1), i * 311, 0));

		// Record with the rifle.
		var world = new SimWorld(Level.FromText(level), 21, new Loadout(WeaponId.Ak47));
		var rec = new Replay { Seed = 21, LevelText = level, Loadout = new Loadout(WeaponId.Ak47) };
		foreach (var f in inputs)
		{
			world.Step(f);
			rec.Inputs.Add(f);
			int tick = (int)world.Tick;
			if (tick % Replay.HashEvery == 0) rec.AddHash(tick, world.StateHash());
		}

		string text = rec.ToText();
		H.Check("the loadout is written to the file", text.Contains("loadout: weapon=2"));

		var reloaded = Replay.FromText(text);
		H.Eq("the loadout survives the round-trip", (int)reloaded.Loadout.Weapon, (int)WeaponId.Ak47);
		H.Check("the replay verifies against itself", !reloaded.Verify().Found);

		// An older replay with no loadout line is read as the pistol, not as a
		// parse failure.
		var stripped = new System.Text.StringBuilder();
		foreach (string line in text.Split('\n'))
			if (!line.StartsWith("loadout:")) stripped.Append(line).Append('\n');
		var legacy = Replay.FromText(stripped.ToString());
		H.Eq("a replay with no loadout line defaults to pistol",
			(int)legacy.Loadout.Weapon, (int)WeaponId.Glock);
		H.Eq("and keeps every frame", legacy.Inputs.Count, inputs.Count);

		// MILESTONE 7 ACCEPTANCE: swapping the weapon diverges the recording, at
		// the first checkpoint, because the loadout is hashed before anything
		// else moves.
		var swapped = Replay.FromText(text);
		swapped.Loadout = new Loadout(WeaponId.Remington);
		var d = swapped.Verify();
		H.Check("swapping the weapon diverges the replay", d.Found);
		H.Eq("and is caught at the first checkpoint", d.Tick, Replay.HashEvery);
	}

	// -------------------------------------------------------------- weapons

	private static void Behaviour()
	{
		H.Group("weapon behaviour");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		string level = Text(g);

		// Pellets: one trigger pull, seven projectiles.
		var sg = new SimWorld(Level.FromText(level), 3, new Loadout(WeaponId.Remington));
		sg.Player.X = CellCentre(24);
		sg.Player.Y = CellCentre(14);
		sg.Step(new InputFrame(0, 0, 0, InputFrame.FFire));
		H.Eq("a shotgun blast spawns seven projectiles", sg.Bullets.Live.Count, 7);
		H.Eq("and consumes one shell", sg.Player.Mag, 7);

		var ps = new SimWorld(Level.FromText(level), 3, new Loadout(WeaponId.Glock));
		ps.Player.X = CellCentre(24);
		ps.Player.Y = CellCentre(14);
		ps.Step(new InputFrame(0, 0, 0, InputFrame.FFire));
		H.Eq("a pistol shot spawns one projectile", ps.Bullets.Live.Count, 1);

		// Pellets spread: they must not all share a heading.
		int distinct = 0;
		for (int i = 0; i < sg.Bullets.Live.Count; i++)
		{
			bool dup = false;
			for (int j = 0; j < i; j++)
				if (sg.Bullets.Live[j].Heading == sg.Bullets.Live[i].Heading) dup = true;
			if (!dup) distinct++;
		}
		H.Check("pellets do not share a single heading", distinct > 1, $"{distinct} distinct");

		// Magazine capacity per weapon, counted by firing until dry.
		foreach (var (id, want) in new[]
		{
			(WeaponId.Glock, 17), (WeaponId.Mp7, 40), (WeaponId.Ak47, 30),
			(WeaponId.Remington, 8), (WeaponId.Saw, 200),
		})
		{
			var w = new SimWorld(Level.FromText(level), 9, new Loadout(id));
			w.Player.X = CellCentre(24);
			w.Player.Y = CellCentre(14);
			int shots = 0;
			var fire = new InputFrame(0, 0, 0, InputFrame.FFire);
			for (int i = 0; i < 60 * 30; i++)
			{
				w.Step(fire);
				foreach (var ev in w.Log.Events)
					if (ev.Kind == SimEventKind.PlayerShot) shots++;
				if (w.Player.Mag == 0 && shots >= want) break;
			}
			H.Eq($"{WeaponCatalog.NameOf(id)} magazine holds {want}", shots, want);
		}
	}

	// ------------------------------------------------- per-weapon gunshot noise

	/// <summary>
	/// The mechanism the whole weapon table is built on: how far a shot carries.
	/// A guard parked between the pistol's radius and the shotgun's must receive
	/// the directed stimulus from exactly one of them.
	///
	/// Since milestone 9 the floor alarm is RADIUS-GATED: a shot nobody was in
	/// earshot of raises nothing at all. Before that it raised the alarm
	/// unconditionally, which would have made subsonic ammo decorative.
	/// </summary>
	private static void GunshotReach()
	{
		H.Group("per-weapon gunshot reach");

		var g = Room();
		Put(g, 1, 1, '@');
		Put(g, GW - 2, GH - 2, 'X');
		// Player at col 4, guard at col 41 -> 740px apart: outside the Glock's
		// 400px, inside the Remington's 950px.
		Put(g, 41, 14, 'a');
		string level = Text(g);

		(int Awareness, GuardState State) Alerted(WeaponId id)
		{
			var w = new SimWorld(Level.FromText(level), 13, new Loadout(id));
			w.Player.X = CellCentre(4);
			w.Player.Y = CellCentre(14);
			w.Guards[0].Facing = 0;                 // facing away down the row
			w.Step(new InputFrame(0, 0, Brad.Half, InputFrame.FFire));
			return (w.Guards[0].Awareness, w.Guards[0].State);
		}

		var probe = new SimWorld(Level.FromText(level), 13);
		int gap = Fx.Dist(probe.Guards[0].X, probe.Guards[0].Y,
			CellCentre(4), CellCentre(14));
		H.Check("fixture: the guard sits between the two radii",
			gap > WeaponCatalog.Get(WeaponId.Glock).GunshotRadius
			&& gap < WeaponCatalog.Get(WeaponId.Remington).GunshotRadius,
			$"{gap / Fx.One}px");

		var pistol = Alerted(WeaponId.Glock);
		var shotgun = Alerted(WeaponId.Remington);

		// Out of earshot of a Glock, he neither hears it nor learns the level is
		// hot: radius-gating means an unheard shot is genuinely unheard.
		H.Eq("a Glock shot beyond its radius does not reach him", pistol.Awareness, 0);
		H.Check("and he does not start hunting",
			pistol.State == GuardState.Relaxed,
			pistol.State.ToString());

		// The shotgun reaches him: full gunshot stimulus, minus the one tick of
		// decay applied by the guard step that follows the shot in the same tick.
		H.Check("a shotgun blast reaches him",
			shotgun.Awareness > Tune.GunAwareness - 30,
			$"awareness {shotgun.Awareness / 10.0:F1}, expected ~{Tune.GunAwareness / 10.0:F1}");
		H.Check("and puts him in the fight", shotgun.State == GuardState.Combat,
			shotgun.State.ToString());

		H.Check("the shotgun alerts him far more than the pistol",
			shotgun.Awareness > pistol.Awareness * 2,
			$"pistol {pistol.Awareness}, shotgun {shotgun.Awareness}");
	}

	/// <summary>
	/// The STOWED magazine, which the fuzzer caught being filled from the wrong
	/// weapon.
	///
	/// Two distinct faults in one line. It read Loadout.Secondary — the
	/// HOLSTERED weapon — when what it wanted was the weapon NOT in hand, and
	/// those differ the moment the secondary has been drawn. And it read the raw
	/// catalogue entry rather than the attachment-modified spec, so an extended
	/// magazine fitted to the stowed weapon did nothing until its first reload.
	/// </summary>
	private static void StowedMagazine()
	{
		H.Group("the stowed magazine");
		string level = RefLevel();

		// --- the everyday half: attachments on the stowed weapon count ---
		//
		// Fitted to the AK, which is the gun that will carry it. The rails are
		// PER WEAPON: a drum mag in the pistol's set is a drum mag on the
		// pistol, and reaching the rifle with it was exactly the thing that
		// made an attachment a property of the player rather than of the gun.
		var drummed = new Loadout(WeaponId.Glock, ArmourId.None,
			secondary: (int)WeaponId.Ak47)
			.WithAttachmentAt(1, AttachSlot.Magazine, 2);
		var w = new SimWorld(Level.FromText(level), 1, drummed);
		int wantStowed = drummed.StowedSpec.Magazine;
		H.Check("fixture: the drum mag actually changes the AK's magazine",
			wantStowed != WeaponCatalog.Get(WeaponId.Ak47).Magazine,
			$"{wantStowed} vs {WeaponCatalog.Get(WeaponId.Ak47).Magazine}");
		H.Eq("the stowed weapon starts on ITS modified magazine",
			w.Player.MagStowed, wantStowed);

		// And it survives the swap into the hand.
		for (int i = 0; i < 200 && w.Loadout.ActiveIndex == 0; i++)
			w.Step(new InputFrame(0, 0, 0, i == 0 ? InputFrame.FSwap : (byte)0));
		H.Eq("and is what is in hand after the swap", w.Player.Mag, wantStowed);
		H.Check("which the held weapon can hold",
			w.Player.Mag <= w.Loadout.Spec.Magazine,
			$"{w.Player.Mag} of {w.Loadout.Spec.Magazine}");

		// --- the active=1 half: stowed is the PRIMARY, not the secondary ---
		var drawn = new Loadout(WeaponId.Vss, ArmourId.None,
			secondary: (int)WeaponId.Vulcan, active: 1);
		H.Eq("fixture: the Vulcan is the one in hand", (int)drawn.Held,
			(int)WeaponId.Vulcan);
		H.Eq("so the STOWED weapon is the VSS", (int)drawn.Stowed,
			(int)WeaponId.Vss);
		var w2 = new SimWorld(Level.FromText(level), 1, drawn);
		H.Eq("and the stowed magazine is the VSS's",
			w2.Player.MagStowed, drawn.StowedSpec.Magazine);
		H.Check("which the VSS can actually hold",
			w2.Player.MagStowed <= drawn.StowedSpec.Magazine);

		// The swap must then leave the hand holding a count it can hold. This
		// is the exact assertion the fuzzer tripped: 300 rounds in a 15-round
		// marksman rifle.
		for (int i = 0; i < 200 && w2.Loadout.ActiveIndex == 1; i++)
			w2.Step(new InputFrame(0, 0, 0, i == 0 ? InputFrame.FSwap : (byte)0));
		H.Eq("after swapping, the VSS is in hand", (int)w2.Loadout.Held,
			(int)WeaponId.Vss);
		H.Check("holding no more rounds than it takes",
			w2.Player.Mag <= w2.Loadout.Spec.Magazine,
			$"{w2.Player.Mag} of {w2.Loadout.Spec.Magazine}");

		// --- an empty holster stows nothing ---
		var alone = new Loadout(WeaponId.Ak47);
		var w3 = new SimWorld(Level.FromText(level), 1, alone);
		H.Eq("an empty holster stows no rounds", w3.Player.MagStowed, 0);

		// --- SpecFor masks by the weapon it was ASKED about ---
		// The Vulcan has no sight rail. A scope in the loadout must not reach
		// it just because the weapon in hand does have one.
		var scoped = new Loadout(WeaponId.Ak47, ArmourId.None, sight: 3,
			secondary: (int)WeaponId.Vulcan);
		H.Eq("a scope does not fit a weapon with no sight rail",
			scoped.SpecFor(WeaponId.Vulcan).Magazine,
			WeaponCatalog.Get(WeaponId.Vulcan).Magazine);
		H.Eq("but Spec still describes the weapon in hand",
			scoped.Spec.Magazine, scoped.SpecFor(WeaponId.Ak47).Magazine);

		// --- ONE SET PER WEAPON, not one per player ---
		//
		// The old model had a single set masked by whichever gun was in hand,
		// so a drum mag bought for the rifle also fed the pistol the moment it
		// was drawn -- and both guns could never differ. Both of these weapons
		// take a magazine, which is what makes the assertion mean something:
		// the mask cannot be what is hiding it.
		var pair = new Loadout(WeaponId.Ak47, ArmourId.None,
			secondary: (int)WeaponId.Mp7)
			.WithAttachmentAt(0, AttachSlot.Magazine, 2);
		H.Check("fixture: both weapons take a magazine",
			WeaponCatalog.HasSlot(WeaponId.Ak47, AttachSlot.Magazine)
			&& WeaponCatalog.HasSlot(WeaponId.Mp7, AttachSlot.Magazine));
		H.Eq("the rifle has the drum", pair.AttachmentAt(0, AttachSlot.Magazine), 2);
		H.Eq("and the submachine gun does NOT",
			pair.AttachmentAt(1, AttachSlot.Magazine), 0);
		H.Eq("so drawing it draws a gun on its own magazine",
			pair.Swapped().Spec.Magazine, WeaponCatalog.Get(WeaponId.Mp7).Magazine);
		H.Check("which is not the rifle's",
			pair.Spec.Magazine != pair.Swapped().Spec.Magazine);

		// Each set survives the round trip independently.
		var back = Loadout.FromText(pair.ToText());
		H.Eq("the rifle's rails survive the text format",
			back.AttachmentAt(0, AttachSlot.Magazine), 2);
		H.Eq("and the holster's emptiness with them",
			back.AttachmentAt(1, AttachSlot.Magazine), 0);
		var both = pair.WithAttachmentAt(1, AttachSlot.Magazine, 1);
		var bothBack = Loadout.FromText(both.ToText());
		H.Eq("two different magazines, two different weapons",
			bothBack.AttachmentAt(0, AttachSlot.Magazine), 2);
		H.Eq("each one its own", bothBack.AttachmentAt(1, AttachSlot.Magazine), 1);

		// A kit written before the holster had rails of its own reads as an
		// empty second set, which is what those runs actually were.
		var legacy = Loadout.FromText("weapon=2 secondary=1 mag=2 active=0");
		H.Eq("an old kit keeps the primary's rails",
			legacy.AttachmentAt(0, AttachSlot.Magazine), 2);
		H.Eq("and gives the holster none",
			legacy.AttachmentAt(1, AttachSlot.Magazine), 0);
	}

	public static void Run()
	{
		StowedMagazine();
		Parity();
		Catalog();
		TextForm();
		Hashing();
		Replays();
		Behaviour();
		GunshotReach();
	}
}
