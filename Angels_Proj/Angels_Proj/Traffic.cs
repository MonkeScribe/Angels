using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// Other aircraft: loose V formations flying lazy curves at assorted altitudes, with a basic damage model.
/// A hit that gets through ignites a plane; it leaves the formation, burns, and spirals down at a steep
/// angle until it hits the ground. Planes are not full flight models, just constant-speed flyers.
/// Formations that drift out of the area where they could be seen are recycled somewhere visible.
/// </summary>
public sealed class Traffic
{
    public static readonly Color[] Colors =
    {
        new(60, 110, 215),   // blue
        new(70, 165, 85),    // green
        new(230, 195, 60),   // yellow
        new(150, 90, 200),   // purple
        new(235, 140, 50),   // orange
    };

    public enum State { Flying, Burning }

    public sealed class Plane
    {
        public Vector2 Pos;                      // world px
        public float Altitude, Heading, Pitch, Bank;
        public float Mph, TurnRate;              // TurnRate: rad per tick
        public int Color;
        public State State;
        public float Health = 100f;
        public float SpinDir = 1f;
        internal Formation Formation;
        internal int Slot;
    }

    internal sealed class Formation
    {
        public Vector2 Pos;
        public float Altitude, Heading, Mph, TurnRate;
        public int Color;
        public readonly List<Plane> Planes = new();
    }

    // Hit boxes in the plane's local frame (x right, y up, -z forward), feet, with the damage a round does there.
    private readonly struct Box
    {
        public readonly Vector3 Min, Max;
        public readonly float Damage;
        public Box(Vector3 min, Vector3 max, float damage) { Min = min; Max = max; Damage = damage; }
    }

    private const float IgniteHealth = 50f; // any hit below does >= 50 damage, so one hit sets a plane alight
    private static readonly Box[] Boxes =
    {
        new(new Vector3(-3f, -3f, -16f), new Vector3(3f, 3f, 17f), 100f),        // fuselage and engine
        new(new Vector3(-18.6f, -1f, -4.2f), new Vector3(18.6f, 1f, 4.7f), 60f), // wings
        new(new Vector3(-5.4f, -0.3f, 12.8f), new Vector3(5.4f, 7f, 16.8f), 80f),// tailplane and fin
    };

    private static readonly Vector2[] Slots =
    {
        new(0, 0), new(-110, 100), new(110, 100), new(-220, 200), new(220, 200),
    };

    private const int FormationCount = 6;
    private const float PxPerTickPerMph = 1.4667f * World.PxPerFoot / 60f; // mph -> ft/s -> world px/s -> per tick
    private const float FtPerTickPerMph = 1.4667f / 60f;

    private readonly List<Formation> _formations = new();
    private readonly List<Plane> _loose = new();   // burning planes that have left their formation
    private readonly List<Plane> _all = new();
    private readonly Random _rng;
    private readonly Fx _fx;

    public int Ignited { get; private set; }
    public IReadOnlyList<Plane> All => _all;

    public Traffic(Fx fx, int seed = 5) { _fx = fx; _rng = new Random(seed); }

    private float Rand(float lo, float hi) => lo + (float)_rng.NextDouble() * (hi - lo);

    /// <param name="extentOfFactor">World-px half-extent of the screen for something at a given distance factor.</param>
    public void Update(Vector2 playerPos, float playerAlt, Func<float, float> extentOfFactor, Func<float, float> factorOfAltitude)
    {
        while (_formations.Count < FormationCount)
            _formations.Add(NewFormation(playerPos, playerAlt, extentOfFactor, factorOfAltitude, true));

        for (var i = 0; i < _formations.Count; i++)
        {
            var f = _formations[i];
            f.Heading = MathHelper.WrapAngle(f.Heading + f.TurnRate);
            Vector2 dir = new(MathF.Sin(f.Heading), -MathF.Cos(f.Heading)), right = new(MathF.Cos(f.Heading), MathF.Sin(f.Heading));
            f.Pos += dir * f.Mph * PxPerTickPerMph;
            foreach (var p in f.Planes)
            {
                p.Pos = f.Pos + right * Slots[p.Slot].X - dir * Slots[p.Slot].Y;
                p.Altitude = f.Altitude + (p.Slot == 0 ? 0f : (p.Slot % 2 == 0 ? 25f : -25f));
                p.Heading = f.Heading;
                p.Bank = f.TurnRate * 120f; // lean into the curve
            }

            var factor = factorOfAltitude(f.Altitude);
            var extent = extentOfFactor(MathF.Max(factor, 0.2f));
            if (f.Planes.Count == 0 || factor < 0.25f || factor > 1.8f || Vector2.Distance(f.Pos, playerPos) > extent * 2.4f)
                _formations[i] = NewFormation(playerPos, playerAlt, extentOfFactor, factorOfAltitude, false);
        }

        for (var i = _loose.Count - 1; i >= 0; i--)
        {
            var p = _loose[i];
            UpdateBurning(p);
            if (p.Altitude <= 0f)
            {
                _fx.Explosion(World.ToFt(p.Pos, 0f));
                _loose.RemoveAt(i);
            }
            else if (Vector2.Distance(p.Pos, playerPos) > 30000f) _loose.RemoveAt(i);
        }

        _all.Clear();
        foreach (var f in _formations) _all.AddRange(f.Planes);
        _all.AddRange(_loose);
    }

    private void UpdateBurning(Plane p)
    {
        // Nose drops toward a steep dive, the plane rolls and spins as it falls, and it picks up speed.
        p.Pitch += (-0.95f - p.Pitch) * 0.02f;
        p.Bank += (p.SpinDir * 1.2f - p.Bank) * 0.03f;
        p.Heading = MathHelper.WrapAngle(p.Heading + p.SpinDir * 0.012f + p.TurnRate);
        p.Mph = MathF.Min(380f, p.Mph + 0.2f);
        var dir = new Vector2(MathF.Sin(p.Heading), -MathF.Cos(p.Heading));
        p.Pos += dir * p.Mph * PxPerTickPerMph * MathF.Cos(p.Pitch);
        p.Altitude += p.Mph * FtPerTickPerMph * MathF.Sin(p.Pitch);

        var at = World.ToFt(p.Pos, p.Altitude);
        _fx.Fire(at);
        _fx.Smoke(at);
        _fx.Smoke(at);
    }

    private Formation NewFormation(Vector2 playerPos, float playerAlt, Func<float, float> extentOfFactor, Func<float, float> factorOfAltitude, bool anywhere)
    {
        // Close to the player's altitude, mostly a little below, so they are in range of the guns.
        var alt = Math.Clamp(playerAlt + Rand(-1800f, 1000f), 800f, 45000f);
        var factor = MathF.Max(factorOfAltitude(alt), 0.3f);
        var extent = extentOfFactor(factor);
        var ang = Rand(0f, MathF.Tau);
        // First fill: scatter across the view. Later: appear near the edge, heading roughly across the screen.
        var radius = extent * (anywhere ? Rand(0.1f, 1.0f) : Rand(1.0f, 1.5f));
        var pos = playerPos + new Vector2(MathF.Sin(ang), -MathF.Cos(ang)) * radius;
        var toPlayer = MathF.Atan2(playerPos.X - pos.X, -(playerPos.Y - pos.Y));
        var f = new Formation
        {
            Pos = pos,
            Altitude = alt,
            Heading = anywhere ? Rand(-MathF.PI, MathF.PI) : MathHelper.WrapAngle(toPlayer + Rand(-0.9f, 0.9f)),
            Mph = Rand(170f, 290f),
            TurnRate = Rand(-0.0030f, 0.0030f),
            Color = _rng.Next(Colors.Length),
        };
        var count = _rng.Next(2, 6);
        for (var i = 0; i < count; i++)
            f.Planes.Add(new Plane { Formation = f, Slot = i, Color = f.Color, Mph = f.Mph, TurnRate = f.TurnRate, Altitude = alt, Heading = f.Heading });
        return f;
    }

    /// <summary>Puts a small formation dead ahead of the player at the same height (a debug aid for target practice).</summary>
    public void SpawnAhead(Vector2 playerPos, float altitude, float heading, float distanceFt, float mph)
    {
        Vector2 dir = new(MathF.Sin(heading), -MathF.Cos(heading));
        var f = new Formation
        {
            Pos = playerPos + dir * distanceFt * World.PxPerFoot,
            Altitude = altitude, Heading = heading, Mph = mph, TurnRate = 0f, Color = _rng.Next(Colors.Length),
        };
        for (var i = 0; i < 3; i++)
            f.Planes.Add(new Plane { Formation = f, Slot = i, Color = f.Color, Mph = mph, Altitude = altitude, Heading = heading });
        if (_formations.Count >= FormationCount + 1) _formations.RemoveAt(0);
        _formations.Add(f);
    }

    /// <summary>Hit scan: the nearest plane whose hit boxes the ray passes through within maxRange feet.</summary>
    public bool RayHit(Vector3 origin, Vector3 dir, float maxRange, out Plane hit, out float distance, out float damage)
    {
        hit = null; distance = maxRange; damage = 0f;
        foreach (var p in _all)
        {
            World.Basis(p.Heading, p.Pitch, p.Bank, out var r, out var u, out var f);
            var centre = World.ToFt(p.Pos, p.Altitude);
            var rel = origin - centre;
            if (rel.LengthSquared() > (maxRange + 40f) * (maxRange + 40f)) continue;
            // Into the plane's frame (local +z is backwards, so forward is -z).
            Vector3 o = new(Vector3.Dot(rel, r), Vector3.Dot(rel, u), Vector3.Dot(rel, -f));
            Vector3 d = new(Vector3.Dot(dir, r), Vector3.Dot(dir, u), Vector3.Dot(dir, -f));
            foreach (var b in Boxes)
                if (Slab(o, d, b.Min, b.Max, out var t) && t < distance)
                {
                    distance = t; hit = p; damage = b.Damage;
                }
        }
        return hit != null;
    }

    private static bool Slab(Vector3 o, Vector3 d, Vector3 min, Vector3 max, out float t)
    {
        float t0 = 0f, t1 = float.MaxValue;
        for (var a = 0; a < 3; a++)
        {
            float oa = a == 0 ? o.X : a == 1 ? o.Y : o.Z, da = a == 0 ? d.X : a == 1 ? d.Y : d.Z;
            float lo = a == 0 ? min.X : a == 1 ? min.Y : min.Z, hi = a == 0 ? max.X : a == 1 ? max.Y : max.Z;
            if (MathF.Abs(da) < 1e-6f)
            {
                if (oa < lo || oa > hi) { t = 0; return false; }
            }
            else
            {
                float ta = (lo - oa) / da, tb = (hi - oa) / da;
                if (ta > tb) (ta, tb) = (tb, ta);
                t0 = MathF.Max(t0, ta); t1 = MathF.Min(t1, tb);
                if (t0 > t1) { t = 0; return false; }
            }
        }
        t = t0;
        return true;
    }

    /// <summary>Applies a hit. One that does enough damage sets the plane alight and takes it out of its formation.</summary>
    public void Damage(Plane p, float amount)
    {
        p.Health -= amount;
        if (p.State != State.Flying || p.Health > IgniteHealth) return;
        p.State = State.Burning;
        p.SpinDir = _rng.Next(2) == 0 ? -1f : 1f;
        p.Formation?.Planes.Remove(p);
        p.Formation = null;
        _loose.Add(p);
        Ignited++;
    }
}
