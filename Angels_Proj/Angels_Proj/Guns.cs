using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// The Spitfire's armament as real projectiles. This is the Mk I / Mk II "A" wing: eight .303 Browning Mk II
/// machine guns, four in each wing, harmonised to converge ahead of the nose. Each round leaves its gun at muzzle
/// velocity plus the aircraft's own velocity, then slows under air drag and drops under gravity, and hits whatever
/// it passes through on the way. Every few rounds is a tracer, which is what the player sees.
///
/// Units are the gunsight's world feet (X east, Y up, Z south), seconds and ft/s.
/// </summary>
public sealed class Guns
{
    // ---- Armament data (Spitfire Mk I, .303 Browning Mk II). Kept here for the damage model to use later. ----

    public const int GunCount = 8;
    public const float CaliberIn = 0.303f;                // bore, inches (7.7 mm)
    public const float CaliberMm = 7.7f;
    public const float BulletMassGrains = 174f;           // Mk VII ball (11.3 g)
    public const float BulletMassKg = 0.0113f;
    public const float MuzzleVelocityFtS = 2440f;         // ~744 m/s
    public const float RoundsPerMinutePerGun = 1150f;
    public const int RoundsPerGun = 300;                  // about 15.7 s of fire
    public const float ConvergeFt = 750f;                 // 250 yd harmonisation
    public const int TracerEvery = 5;                     // one round in five is a tracer

    /// <summary>Whether the rounds are drawn: tracers, muzzle flashes and hit sparks. Off for now. Rounds are still
    /// fired, fly, drop and hit exactly the same; they just can't be seen.</summary>
    public const bool ShowEffects = false;

    /// <summary>Whether tracers are drawn in the gunsight's 3D view. Independent of ShowEffects, which covers the map's
    /// tracers, the muzzle flashes and the hit sparks.</summary>
    public const bool ShowSightTracers = true;

    /// <summary>Whether tracers are drawn on the 2D map (tiny red dashes).</summary>
    public const bool ShowMapTracers = true;

    /// <summary>Where each gun's round leaves the airframe, as a pixel of the level sprite (256 px, nose up): the tips of the
    /// barrel stubs on its wings. There are four stubs, two a wing, and each serves two of the eight guns (guns 0-3 are the
    /// left wing, 4-7 the right).</summary>
    public static readonly Vector2[] MuzzleSpritePx =
    {
        new(85.5f, 89f), new(85.5f, 89f), new(89.5f, 79f), new(89.5f, 79f),     // left wing: outer stub, then inner stub
        new(166.5f, 79f), new(166.5f, 79f), new(171f, 89f), new(171f, 89f),     // right wing: inner stub, then outer stub
    };

    private const float SpritePxPerFt = 204f / 37.2f;     // the sprite's wingspan in px over the real span in feet

    /// <summary>Gun muzzles in the aircraft frame (feet): x along the right wing, y up, z forward, from the sprite points
    /// above (the sprite's origin, 128,133, is the aircraft's origin), a little below the wing chord line.</summary>
    public static readonly Vector3[] Muzzles = BuildMuzzles();

    private static Vector3[] BuildMuzzles()
    {
        var m = new Vector3[GunCount];
        for (var g = 0; g < GunCount; g++)
            m[g] = new Vector3((MuzzleSpritePx[g].X - 128f) / SpritePxPerFt, -1f, (133f - MuzzleSpritePx[g].Y) / SpritePxPerFt);
        return m;
    }

    // ---- Ballistics. ----

    private const float G = 32.174f;
    // Quadratic drag, dv/dt = -k v^2, tuned so a round keeps about 2000 ft/s at 250 yd and about 1700 at 500 yd,
    // roughly what the .303 Mk VII does.
    private const float DragK = 0.00024f;
    private const float LifeS = 1.6f;                     // rounds are dropped after this (well past 1000 yd)
    private const float SpreadRad = 0.0035f;              // each gun's dispersion either way, about 3.5 mils
    private const float TracerStreakS = 0.05f;            // how much of its flight a tracer's glow streak covers

    /// <summary>Per-round damage scale, against the hit boxes' values, until damage uses calibre and mass. The boxes
    /// were tuned for one round in twelve a second; eight guns fire about 150.</summary>
    public const float DamageScale = 12f / (GunCount * RoundsPerMinutePerGun / 60f);

    public struct Round
    {
        public Vector3 Pos, Vel;
        public float Age;
        public bool Tracer;
        public int Gun;
        public Vector2 MapOffset;     // world px to add to the round's position on the 2D map, so it starts at the drawn muzzle
    }

    public readonly List<Round> Rounds = new();
    public readonly int[] Ammo = new int[GunCount];
    public int AmmoLeft { get { var n = 0; foreach (var a in Ammo) n += a; return n; } }

    private readonly float[] _phase = new float[GunCount];  // fraction of a round each gun has built up
    private readonly int[] _fired = new int[GunCount];
    private readonly Random _rng;

    public Guns(Random rng)
    {
        _rng = rng;
        Rearm();
    }

    public void Rearm()
    {
        Rounds.Clear();
        for (var i = 0; i < GunCount; i++)
        {
            Ammo[i] = RoundsPerGun;
            _fired[i] = _rng.Next(TracerEvery);       // belts don't all start on a tracer
            _phase[i] = (float)_rng.NextDouble();     // nor do the guns fire in lockstep
        }
    }

    private float Spread() => ((float)_rng.NextDouble() * 2f - 1f) * SpreadRad;

    /// <summary>One tick with the trigger held. Returns which wings fired this tick (bit 0 left, bit 1 right).</summary>
    public int Fire(Vector3 pos, Vector3 right, Vector3 up, Vector3 fwd, Vector3 aircraftVel, Vector2[] mapMuzzleWorld)
    {
        var wings = 0;
        var perTick = RoundsPerMinutePerGun / 60f / 60f;
        // Harmonised like the real guns: aimed a touch high, so the rounds have dropped onto the sight line by the
        // time they reach the convergence point.
        var tc = ConvergeFt / MuzzleVelocityFtS;
        var aim = pos + fwd * ConvergeFt + up * (0.5f * G * tc * tc);
        for (var g = 0; g < GunCount; g++)
        {
            _phase[g] += perTick;
            while (_phase[g] >= 1f && Ammo[g] > 0)
            {
                _phase[g] -= 1f;
                Ammo[g]--;
                var m = Muzzles[g];
                var muzzle = pos + right * m.X + up * m.Y + fwd * m.Z;
                var dir = Vector3.Normalize(aim - muzzle);
                dir = Vector3.Normalize(dir + right * Spread() + up * Spread());
                // Rounds fired within the same tick are spread along it, so a burst doesn't clump into tick steps.
                var lead = _phase[g] / perTick / 60f;
                var vel = dir * MuzzleVelocityFtS + aircraftVel;
                Rounds.Add(new Round
                {
                    Pos = muzzle + vel * lead, Vel = vel, Age = lead, Tracer = _fired[g]++ % TracerEvery == 0, Gun = g,
                    MapOffset = mapMuzzleWorld[g] - new Vector2(muzzle.X, muzzle.Z) * World.PxPerFoot,
                });
                wings |= m.X < 0f ? 1 : 2;
            }
            if (Ammo[g] == 0) _phase[g] = 0f;
        }
        return wings;
    }

    /// <summary>Moves every round one tick and resolves hits along the way.</summary>
    public void Update(Traffic traffic, Fx fx)
    {
        const float dt = 1f / 60f;
        for (var i = Rounds.Count - 1; i >= 0; i--)
        {
            var r = Rounds[i];
            var speed = r.Vel.Length();
            var vel = r.Vel - r.Vel * (DragK * speed * dt) - new Vector3(0f, G * dt, 0f);
            var step = (r.Vel + vel) * 0.5f * dt;
            var len = step.Length();
            if (len > 0.01f && traffic.RayHit(r.Pos, step / len, len, out var plane, out var dist, out var damage))
            {
                var at = r.Pos + step / len * dist;
                traffic.Damage(plane, damage * DamageScale);
                if (ShowEffects) fx.Spark(at);
                Rounds.RemoveAt(i);
                continue;
            }
            r.Pos += step;
            r.Vel = vel;
            r.Age += dt;
            if (r.Age > LifeS || r.Pos.Y <= 0f) { Rounds.RemoveAt(i); continue; }
            Rounds[i] = r;
        }
    }

    /// <summary>The glowing streak of each tracer in flight, for the gunsight.</summary>
    public void Tracers(List<Gunsight.Tracer> into)
    {
        into.Clear();
        if (!ShowEffects && !ShowSightTracers) return;
        foreach (var r in Rounds)
            if (r.Tracer)
                into.Add(new Gunsight.Tracer { A = r.Pos - r.Vel * TracerStreakS, B = r.Pos, Life = 1 });
    }
}
