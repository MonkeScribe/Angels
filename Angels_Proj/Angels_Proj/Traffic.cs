using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// Other aircraft: loose V formations flying lazy curves at assorted altitudes. Hits damage their parts (Damage.cs)
/// and a damaged engine smokes, leaks and burns, but for now none of that changes how they fly: nothing shoots them
/// down (only crashing kills, and these don't crash). Planes are not full flight models, just constant-speed flyers.
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

    public sealed class Plane
    {
        public Vector2 Pos;                      // world px
        public float Altitude, Heading, Pitch, Bank;
        public float Mph, TurnRate;              // TurnRate: rad per tick
        public int Color;
        public readonly AircraftDamage Damage = new();
        public float[] Parts => Damage.Parts;    // hit points of each Part (see Damage.cs)
        public int Salt;                         // staggers its fire animation from the others'
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

    // Hit boxes: DamageTuning.Fighter (Damage.cs).

    private static readonly Vector2[] Slots =
    {
        new(0, 0), new(-110, 100), new(110, 100), new(-220, 200), new(220, 200),
    };

    private const int FormationCount = 6;
    private const float PxPerTickPerMph = 1.4667f * World.PxPerFoot / 60f; // mph -> ft/s -> world px/s -> per tick

    private readonly List<Formation> _formations = new();
    private readonly List<Plane> _all = new();
    private readonly Random _rng;
    private readonly Fx _fx;

    /// <summary>Rounds that have hit something.</summary>
    public int Hits { get; private set; }
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

        _all.Clear();
        foreach (var f in _formations) _all.AddRange(f.Planes);

        // Damage runs on (a fire burns on, its throttle taken as up) and a damaged engine trails smoke and leaking fuel.
        foreach (var p in _all)
        {
            p.Damage.Update(1f / 60f, 1f, false);
            var d = p.Damage;
            if (d.SmokeStrength <= 0f && !d.OnFire && d.Leaks == 0) continue;
            World.Basis(p.Heading, p.Pitch, p.Bank, out _, out var u, out var fwd);
            _fx.EngineTrail(EngineAt(p, fwd, u) - fwd * 4f, -fwd, d.SmokeStrength, d.FireStrength, d.Leaks, false);
        }
    }

    /// <summary>Where a plane's engine is (world feet): in the nose, ahead of its origin and a little below its centre line.</summary>
    public static Vector3 EngineAt(Plane p, Vector3 fwd, Vector3 up) => World.ToFt(p.Pos, p.Altitude) + fwd * 11f - up * 0.5f;

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
            f.Planes.Add(new Plane { Formation = f, Slot = i, Color = f.Color, Mph = f.Mph, TurnRate = f.TurnRate, Altitude = alt, Heading = f.Heading, Salt = _rng.Next(64) });
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
            f.Planes.Add(new Plane { Formation = f, Slot = i, Color = f.Color, Mph = mph, Altitude = altitude, Heading = heading, Salt = _rng.Next(64) });
        if (_formations.Count >= FormationCount + 1) _formations.RemoveAt(0);
        _formations.Add(f);
    }

    /// <summary>A plane's velocity, ft/s in the gunsight's world frame.</summary>
    public static Vector3 Velocity(Plane p)
    {
        World.Basis(p.Heading, p.Pitch, p.Bank, out _, out _, out var f);
        return f * p.Mph * 1.4667f;
    }

    /// <summary>Hit scan: the nearest plane whose hit boxes the ray passes through within maxRange feet, and the part hit.</summary>
    public bool RayHit(Vector3 origin, Vector3 dir, float maxRange, out Plane hit, out float distance, out Part part)
    {
        hit = null; distance = maxRange; part = Part.Fuselage;
        foreach (var p in _all)
        {
            World.Basis(p.Heading, p.Pitch, p.Bank, out var r, out var u, out var f);
            var centre = World.ToFt(p.Pos, p.Altitude);
            var rel = origin - centre;
            if (rel.LengthSquared() > (maxRange + 40f) * (maxRange + 40f)) continue;
            // Into the plane's frame (local +z is backwards, so forward is -z).
            Vector3 o = new(Vector3.Dot(rel, r), Vector3.Dot(rel, u), Vector3.Dot(rel, -f));
            Vector3 d = new(Vector3.Dot(dir, r), Vector3.Dot(dir, u), Vector3.Dot(dir, -f));
            foreach (var b in DamageTuning.Fighter)
                if (p.Parts[(int)b.Part] > 0f && Slab(o, d, b.Min, b.Max, out var t) && t < distance)
                {
                    distance = t; hit = p; part = b.Part;
                }
        }
        return hit != null;
    }

    /// <summary>The twelve edges of every hit box of a plane, as pairs of world points (feet), coloured by the state of
    /// the part the box belongs to. For the debug hit box view.</summary>
    public static void HitBoxEdges(Plane p, List<(Vector3 a, Vector3 b, Color color)> into)
    {
        var boxes = new List<(Vector3[] c, Part part)>();
        HitBoxCorners(p, boxes);
        foreach (var (c, part) in boxes)
        {
            var color = DamageModel.StateColor(DamageModel.StateOf(p.Parts[(int)part]));
            for (var i = 0; i < 8; i++)
                for (var bit = 1; bit <= 4; bit <<= 1)
                    if ((i & bit) == 0) into.Add((c[i], c[i | bit], color));
        }
    }

    /// <summary>The eight world corners (feet) of each of a plane's hit boxes, with the part each box belongs to.</summary>
    public static void HitBoxCorners(Plane p, List<(Vector3[] corners, Part part)> into)
    {
        World.Basis(p.Heading, p.Pitch, p.Bank, out var r, out var u, out var f);
        var centre = World.ToFt(p.Pos, p.Altitude);
        foreach (var bx in DamageTuning.Fighter)
        {
            var c = new Vector3[8];
            for (var i = 0; i < 8; i++)
            {
                float x = (i & 1) == 0 ? bx.Min.X : bx.Max.X, y = (i & 2) == 0 ? bx.Min.Y : bx.Max.Y, z = (i & 4) == 0 ? bx.Min.Z : bx.Max.Z;
                c[i] = centre + r * x + u * y + (-f) * z;   // local +z is backwards
            }
            into.Add((c, bx.Part));
        }
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

    /// <summary>A round hits a part of a plane (see AircraftDamage.Hit). Returns the hit points the part lost.</summary>
    public float Hit(Plane p, Part part, DamageTuning.Weapon weapon, float impactSpeedFtS)
    {
        Hits++;
        return p.Damage.Hit(part, weapon, impactSpeedFtS, _rng);
    }
}
