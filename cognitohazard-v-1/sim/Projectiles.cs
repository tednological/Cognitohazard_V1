using System.Collections.Generic;

namespace Cognitohazard.Sim;

/// <summary>
/// What a projectile IS. Ordinals are hashed; append only. Round is zero, and a
/// Round hashes exactly as every projectile did before kinds existed, so the
/// specialists moved no golden hash on a run that never fires one.
/// </summary>
public enum BulletKind
{
	/// <summary>Stops at the first wall or body. Everything before the specialists.</summary>
	Round = 0,
	/// <summary>A lightning bolt: on impact it DISCHARGES (SimWorld.Discharge).</summary>
	Arc = 1,
	/// <summary>Thrown. Bounces, rolls, touches nobody, bursts when its life runs out.</summary>
	Grenade = 2,
	/// <summary>Shrapnel. Hits guards AND the player -- it has no side.</summary>
	Frag = 3,
	/// <summary>A penetrator: goes through WallsLeft walls before one stops it.</summary>
	Pierce = 4,
}

public sealed class Bullet
{
	public BulletKind Kind;

	/// <summary>Pierce: walls still to go through. Arc: bodies the discharge may take.</summary>
	public int WallsLeft;
	public int Arc;

	/// <summary>Pierce: the round is inside a wall right now, so the wall it is
	/// in is counted once, on the way in, not once per substep.</summary>
	public bool InWall;

	public int X, Y;                   // fixed-point
	public int VX, VY;                 // fixed-point px per second
	public int LifeMt;                 // milli-ticks
	public int Damage;
	public int ArmourPierce;
	public bool Headshot;
	public bool FromPlayer;
	public int Heading;                // BRAD, cached for impact effects

	public void HashInto(ref Hash64 h)
	{
		h.Add(X); h.Add(Y); h.Add(VX); h.Add(VY);
		h.Add(LifeMt); h.Add(Damage); h.Add(ArmourPierce); h.Add(Headshot);
		h.Add(FromPlayer); h.Add(Heading);
		// Only for the specialists. A Round hashes as every round did before
		// kinds existed, the rule the panels and the `s` token follow.
		if (Kind != BulletKind.Round)
		{
			h.Add((int)Kind); h.Add(WallsLeft); h.Add(Arc); h.Add(InWall);
		}
	}
}

/// <summary>
/// Bullets are simulated objects, not raycasts (spec §7.2). At WORLD_SLOW they
/// are visibly in flight, which is the entire point of the dilation mechanic.
///
/// Substepping is REQUIRED or they tunnel walls. The count is no longer the
/// flat three the spec names: at the retuned muzzle velocities a round covers
/// 48 px in a tick, and three samples 16 px apart walk straight through a
/// one-cell wall. It is now derived from the distance the round actually
/// travels this tick, so no sample is ever more than
/// <see cref="Tune.BulletSubstepFx"/> apart whatever the speed or the clock.
/// </summary>
public sealed class Projectiles
{
	public readonly List<Bullet> Live = new();

	public Bullet Spawn(int x, int y, int heading, int speed, int lifeTicks, bool fromPlayer,
		int damage, int armourPierce = 0, bool headshot = false)
	{
		Brad.SinCos(heading, out int sin, out int cos);
		var b = new Bullet
		{
			X = x,
			Y = y,
			VX = (int)(((long)speed * cos) >> Brad.UnitShift),
			VY = (int)(((long)speed * sin) >> Brad.UnitShift),
			LifeMt = lifeTicks * Actor.Mt,
			Damage = damage,
			ArmourPierce = armourPierce,
			Headshot = headshot,
			FromPlayer = fromPlayer,
			Heading = heading,
		};
		Live.Add(b);
		return b;
	}

	/// <summary>Glass is APPENDED: it is the one hit that does not end the
	/// round. Impact.GuardIndex carries the index into the glass array passed
	/// to <see cref="Step(Rect[], Rect[], bool[], List{Actor}, Actor, int, List{Impact})"/>.
	/// Blast and Bounce are APPENDED likewise: a grenade's fuse ran out at
	/// (X, Y), or it came off a wall there.</summary>
	public enum HitKind { None, Wall, Guard, Player, Glass, Blast, Bounce }

	public readonly struct Impact
	{
		public readonly HitKind Kind;
		public readonly int X, Y, Heading, GuardIndex, Damage, Pierce;
		public readonly bool Headshot;

		/// <summary>Non-zero for a lightning bolt: how many bodies the
		/// discharge at this impact may take.</summary>
		public readonly int Arc;

		/// <summary>A penetrator passing THROUGH a wall rather than stopping in
		/// it. Presentation only; the round goes on.</summary>
		public readonly bool Through;

		public Impact(HitKind k, int x, int y, int heading, int guardIndex, int damage,
			int pierce = 0, bool headshot = false, int arc = 0, bool through = false)
		{ Kind = k; X = x; Y = y; Heading = heading; GuardIndex = guardIndex;
		  Damage = damage; Pierce = pierce; Headshot = headshot; Arc = arc; Through = through; }
	}

	/// <summary>
	/// Advance every bullet by one world tick at <paramref name="worldScale"/>,
	/// reporting impacts in resolution order. Iterates backwards so removal is
	/// index-stable, and guards are tested by index so the order is fixed.
	/// </summary>
	public void Step(Rect[] walls, List<Actor> guards, Actor player, int worldScale,
		List<Impact> impacts)
		=> Step(walls, System.Array.Empty<Rect>(), System.Array.Empty<bool>(), guards, player,
			worldScale, impacts);

	/// <summary>
	/// As above, with GLASS: panes that stop nothing but shatter when touched.
	/// <paramref name="glassIntact"/> is parallel to <paramref name="glass"/>
	/// and is cleared HERE the moment a round touches a pane, so the second
	/// pellet of the same shotgun blast, or the next substep of the same round,
	/// does not break a pane that is already broken. The caller turns each Glass
	/// impact into the pane's new state.
	/// </summary>
	/// <param name="panes">How many of <paramref name="glass"/> a grenade can
	/// break: the panes come first and the ceiling lamps after them, and a
	/// grenade rolls along the floor.</param>
	public void Step(Rect[] walls, Rect[] glass, bool[] glassIntact, List<Actor> guards,
		Actor player, int worldScale, List<Impact> impacts, int panes = int.MaxValue)
	{
		if (worldScale <= 0) return;

		for (int i = Live.Count - 1; i >= 0; i--)
		{
			var b = Live[i];
			bool gone = false;

			if (b.Kind == BulletKind.Grenade)
			{
				StepGrenade(b, walls, glass, glassIntact, worldScale, impacts, panes);
				b.LifeMt -= worldScale;
				if (b.LifeMt <= 0)
				{
					impacts.Add(new Impact(HitKind.Blast, b.X, b.Y, b.Heading, -1, b.Damage,
						b.ArmourPierce));
					Live.RemoveAt(i);
				}
				continue;
			}

			int travelX = Fx.PerTick(b.VX, worldScale);
			int travelY = Fx.PerTick(b.VY, worldScale);

			// Manhattan length: never shorter than the true one, so it can only
			// ever over-subdivide, and it costs no square root per bullet.
			int span = Fx.Abs(travelX) + Fx.Abs(travelY);
			int steps = span / Tune.BulletSubstepFx + 1;
			if (steps < Tune.BulletSubsteps) steps = Tune.BulletSubsteps;
			if (steps > Tune.BulletSubstepCap) steps = Tune.BulletSubstepCap;

			// Interpolated from the tick's start rather than accumulated, so the
			// per-substep division cannot shorten the round's flight.
			int startX = b.X, startY = b.Y;

			for (int s = 1; s <= steps && !gone; s++)
			{
				b.X = startX + (int)((long)travelX * s / steps);
				b.Y = startY + (int)((long)travelY * s / steps);

				bool inWall = Geometry.HitsWall(walls, b.X, b.Y, Fx.One + Fx.Half);
				if (b.Kind == BulletKind.Pierce)
				{
					// A penetrator counts a wall ON THE WAY IN, once, and pays
					// for it then. Inside, it is simply travelling.
					if (inWall && !b.InWall)
					{
						if (b.WallsLeft <= 0)
						{
							impacts.Add(new Impact(HitKind.Wall, b.X, b.Y, b.Heading, -1, 0));
							gone = true;
							break;
						}
						b.WallsLeft--;
						b.Damage -= (int)(((long)b.Damage * Tune.WallPierceLossQ8) >> Fx.Shift);
						impacts.Add(new Impact(HitKind.Wall, b.X, b.Y, b.Heading, -1, 0,
							through: true));
					}
					b.InWall = inWall;
				}
				else if (inWall)
				{
					impacts.Add(new Impact(HitKind.Wall, b.X, b.Y, b.Heading, -1, 0,
						arc: b.Kind == BulletKind.Arc ? b.Arc : 0));
					gone = true;
					break;
				}

				// Glass does not stop the round. It is the pane that gives,
				// which is what makes a window both a sightline and a hazard:
				// you can shoot through it, and everyone hears you did.
				for (int k = 0; k < glass.Length; k++)
				{
					if (!glassIntact[k]) continue;
					if (!Geometry.CircleHitsRect(b.X, b.Y, Fx.One + Fx.Half, in glass[k])) continue;
					glassIntact[k] = false;
					impacts.Add(new Impact(HitKind.Glass, b.X, b.Y, b.Heading, k, 0));
				}

				// Shrapnel has no side: it tests the guards AND the player.
				bool frag = b.Kind == BulletKind.Frag;
				if (b.FromPlayer || frag)
				{
					for (int g = 0; g < guards.Count; g++)
					{
						var e = guards[g];
						if (e.Prone || !e.Alive) continue;
						long rr = (long)(e.Radius + Tune.HitPad) * (e.Radius + Tune.HitPad);
						if (Fx.DistSq(b.X, b.Y, e.X, e.Y) < rr)
						{
							impacts.Add(new Impact(HitKind.Guard, b.X, b.Y, b.Heading, g, b.Damage,
								b.ArmourPierce, b.Headshot,
								arc: b.Kind == BulletKind.Arc ? b.Arc : 0));
							gone = true;
							break;
						}
					}
				}
				if (!gone && (!b.FromPlayer || frag) && player.Alive)
				{
					long rr = (long)(player.Radius + Tune.HitPad) * (player.Radius + Tune.HitPad);
					if (Fx.DistSq(b.X, b.Y, player.X, player.Y) < rr)
					{
						impacts.Add(new Impact(HitKind.Player, b.X, b.Y, b.Heading, -1, b.Damage,
							b.ArmourPierce));
						gone = true;
					}
				}
			}

			b.LifeMt -= worldScale;
			if (gone || b.LifeMt <= 0) Live.RemoveAt(i);
		}
	}

	/// <summary>
	/// One tick of a thrown grenade: travel with substeps, BOUNCE off whatever
	/// stops a round, shatter glass it rolls through, and bleed speed to the
	/// floor. It touches no body -- a grenade is not a projectile you are hit
	/// by, it is one you are standing too near when it goes.
	///
	/// The bounce reflects whichever axis the wall is on: if moving along X
	/// alone would still be clear, the wall is across Y, and so on. The
	/// grenade stays where it was for the rest of the tick, which is cheaper
	/// than splitting the step and, at a bounce, indistinguishable from it.
	/// </summary>
	private static void StepGrenade(Bullet b, Rect[] walls, Rect[] glass, bool[] glassIntact,
		int worldScale, List<Impact> impacts, int panes)
	{
		int travelX = Fx.PerTick(b.VX, worldScale);
		int travelY = Fx.PerTick(b.VY, worldScale);
		int span = Fx.Abs(travelX) + Fx.Abs(travelY);
		int steps = span / Tune.BulletSubstepFx + 1;
		if (steps > Tune.BulletSubstepCap) steps = Tune.BulletSubstepCap;

		int startX = b.X, startY = b.Y;
		for (int s = 1; s <= steps; s++)
		{
			int nx = startX + (int)((long)travelX * s / steps);
			int ny = startY + (int)((long)travelY * s / steps);

			if (Geometry.HitsWall(walls, nx, ny, Tune.GrenadeRadius))
			{
				bool xClear = !Geometry.HitsWall(walls, nx, b.Y, Tune.GrenadeRadius);
				bool yClear = !Geometry.HitsWall(walls, b.X, ny, Tune.GrenadeRadius);
				if (xClear && !yClear) b.VY = -b.VY;
				else if (yClear && !xClear) b.VX = -b.VX;
				else { b.VX = -b.VX; b.VY = -b.VY; }
				b.VX = (int)(((long)b.VX * Tune.GrenadeBounceQ8) >> Fx.Shift);
				b.VY = (int)(((long)b.VY * Tune.GrenadeBounceQ8) >> Fx.Shift);
				b.Heading = Brad.Atan2(b.VY, b.VX);
				impacts.Add(new Impact(HitKind.Bounce, b.X, b.Y, b.Heading, -1, 0));
				break;
			}

			b.X = nx;
			b.Y = ny;

			for (int k = 0; k < glass.Length && k < panes; k++)
			{
				if (!glassIntact[k]) continue;
				if (!Geometry.CircleHitsRect(b.X, b.Y, Tune.GrenadeRadius, in glass[k])) continue;
				glassIntact[k] = false;
				impacts.Add(new Impact(HitKind.Glass, b.X, b.Y, b.Heading, k, 0));
			}
		}

		// Rolling friction, on the world clock like the travel it slows.
		int drag = Tune.GrenadeDragQ8 * worldScale;
		b.VX -= (int)((long)b.VX * drag / (Fx.One * Fx.ScaleDen));
		b.VY -= (int)((long)b.VY * drag / (Fx.One * Fx.ScaleDen));
		if (Fx.Abs(b.VX) + Fx.Abs(b.VY) < Tune.GrenadeRestSpeed) { b.VX = 0; b.VY = 0; }
	}

	public void HashInto(ref Hash64 h)
	{
		h.Add(Live.Count);
		for (int i = 0; i < Live.Count; i++) Live[i].HashInto(ref h);
	}
}
