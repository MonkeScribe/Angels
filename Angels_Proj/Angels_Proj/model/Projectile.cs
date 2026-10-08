using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// One round in flight, from any gun. A projectile is made by the gun that fires it, which hands over its caliber (mm),
/// tracer colour and what it does on hitting; everything else follows from those. The caliber sets its mass, and its mass
/// and frontal area set how fast the air slows it, so a 20 mm shell keeps its speed better than a rifle-calibre bullet.
/// It leaves the muzzle at muzzle velocity plus the firing aircraft's own, then slows under drag, drops under gravity,
/// and hits whatever it passes through on the way.
///
/// Units are the gunsight's world feet (X east, Y up, Z south), seconds and ft/s.
/// </summary>
public sealed class Projectile
{
    // ---- Display switches ----

    /// <summary>Whether the rounds are drawn: tracers, muzzle flashes and hit sparks. Off for now. Rounds are still
    /// fired, fly, drop and hit exactly the same; they just can't be seen.</summary>
    public const bool ShowEffects = false;

    /// <summary>Whether tracers are drawn in the gunsight's 3D view. Independent of ShowEffects, which covers the map's
    /// tracers, the muzzle flashes and the hit sparks.</summary>
    public const bool ShowSightTracers = true;

    /// <summary>Whether tracers are drawn on the 2D map (tiny dashes).</summary>
    public const bool ShowMapTracers = true;

    // ---- Ballistics ----

    public const float G = 32.174f;

    // The reference round is the .303 Mk VII ball (7.7 mm, 11.3 g). Its drag is quadratic, dv/dt = -k v^2, tuned so it keeps
    // about 2000 ft/s at 250 yd and about 1700 at 500 yd. Every other caliber is scaled from it.
    private const float RefCaliberMm = 7.7f;
    private const float RefMassKg = 0.0113f;
    private const float RefDragK = 0.00024f;

    /// <summary>How fast a round's mass grows with its caliber. A solid scales with the cube; real shells are a little
    /// lighter than that for their size (a 20 mm Hispano shell is about 130 g, not 200), hence a bit under.</summary>
    private const float MassExponent = 2.7f;

    private const float LifeS = 1.6f;                         // rounds are dropped after this (well past 1000 yd)
    private const float ThroughFt = 60f;                      // how far past where it strikes a round can still be inside the aircraft
    private const float TracerStreakS = 0.028f;               // how much of its flight a tracer's streak covers: about what the eye smears, 60 ft or so
    private const float IgniteS = 0.05f, FullBrightS = 0.12f; // a tracer ignites a little way down the range, not at the muzzle, and rises to full
    private const float BurnoutS = 1.05f, DarkS = 1.5f;       // then burns steadily and fades out as its compound is used up

    /// <summary>The mass of a round of the given caliber, kg.</summary>
    public static float MassOf(float caliberMm) => RefMassKg * MathF.Pow(caliberMm / RefCaliberMm, MassExponent);

    /// <summary>The drag constant k of a round of the given caliber (deceleration = k v^2): the reference round's, scaled by
    /// frontal area over mass, so a heavier round for its size slows less.</summary>
    public static float DragOf(float caliberMm)
    {
        var area = caliberMm * caliberMm / (RefCaliberMm * RefCaliberMm);
        return RefDragK * area / (MassOf(caliberMm) / RefMassKg);
    }

    /// <summary>How brightly a tracer round burns at this age (seconds in flight), 0 to 1: dark at the muzzle, igniting
    /// after about 100 ft, steady, then dying away toward the end of its flight.</summary>
    public static float TracerGlow(float age)
    {
        if (age <= IgniteS) return 0f;
        if (age < FullBrightS) return (age - IgniteS) / (FullBrightS - IgniteS);
        if (age < BurnoutS) return 1f;
        return MathF.Max(0f, 1f - (age - BurnoutS) / (DarkS - BurnoutS));
    }

    // ---- What it is (from the gun) ----

    public readonly float CaliberMm;
    public readonly float MassKg;
    public readonly float DragK;
    public readonly bool Tracer;
    public readonly Color TracerColor;
    public readonly DamageTuning.Weapon Weapon;     // what it does on hitting (Damage.cs)
    public readonly Aircraft Shooter;               // never hit by its own rounds

    // ---- Where it is ----

    public Vector3 Pos, Vel;
    public float Age;
    public Vector2 MapOffset;     // world px to add to the round's position on the 2D map, so it starts at the drawn muzzle
    public float Damage = -1f;    // the damage it still carries: rolled when it first hits, less each part's armour as it
                                  // goes through (negative: not rolled yet)

    public Projectile(Gun gun, bool tracer, Aircraft shooter, Vector3 pos, Vector3 vel, float age, Vector2 mapOffset)
    {
        CaliberMm = gun.Spec.CaliberMm;
        MassKg = MassOf(CaliberMm);
        DragK = DragOf(CaliberMm);
        TracerColor = gun.Spec.TracerColor;
        Weapon = gun.Spec.Weapon;
        Tracer = tracer;
        Shooter = shooter;
        Pos = pos; Vel = vel; Age = age; MapOffset = mapOffset;
    }

    /// <summary>Moves it one tick and resolves hits along the way. Returns false once it is spent (it hit something, fell to
    /// the ground or flew too long) and should be dropped. hits is scratch space for the world's ray tests.</summary>
    public bool Update(World world, Fx fx, Random rng, List<World.RayEntry> hits)
    {
        const float dt = 1f / 60f;
        var speed = Vel.Length();
        var vel = Vel - Vel * (DragK * speed * dt) - new Vector3(0f, G * dt, 0f);
        var step = (Vel + vel) * 0.5f * dt;
        var len = step.Length();
        if (len > 0.01f)
        {
            // Penetration: the round's damage is rolled when it hits an aircraft (by calibre and its speed relative to
            // it), then it goes through that aircraft's parts in its way, nearest first. Each part's armour comes off
            // the damage: if the armour stops it all, the round stops there; if not, what's left lands on the part and
            // carries on, less that armour, into the next part behind it. When there are no more parts in its way the
            // round stops: it doesn't come out the far side.
            var dir = step / len;
            world.RayHits(Pos, dir, len, Shooter, hits);
            if (hits.Count > 0)
            {
                // Every part of the aircraft first hit along the round's line, however far through it reaches.
                var plane = hits[0].Plane;
                var firstHit = hits[0].Distance;
                world.RayHits(Pos, dir, firstHit + ThroughFt, Shooter, hits);
                if (Damage < 0f) Damage = DamageModel.Roll(Weapon, (Vel - plane.VelocityFt).Length(), rng);
                foreach (var h in hits)
                {
                    if (h.Plane != plane) continue;
                    var left = Damage - plane[h.Part].Armor;
                    if (left <= 0f) break;   // stopped by the armour
                    world.Hit(plane, h.Part, Weapon, left, rng);
                    Damage = left;
                }
                if (ShowEffects) fx.Spark(Pos + dir * firstHit);
                return false;
            }
        }
        Pos += step;
        Vel = vel;
        Age += dt;
        return Age <= LifeS && Pos.Y > 0f;
    }

    /// <summary>The glowing streak of this round for the gunsight, if it is a tracer and alight.</summary>
    public bool TryGetStreak(out Gunsight.Tracer streak)
    {
        streak = default;
        if (!Tracer) return false;
        var glow = TracerGlow(Age);
        if (glow <= 0.01f) return false;
        streak = new Gunsight.Tracer { A = Pos - Vel * TracerStreakS, B = Pos, Life = 1, Glow = glow, Tint = TracerColor.ToVector3() };
        return true;
    }
}
