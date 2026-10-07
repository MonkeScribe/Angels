using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// The world: every aircraft in it, the player's and the others (which Traffic brings in and takes away), the hits
/// on them, and the shared conventions everything uses. The top-down map works in "world px" (x east, y south) and the
/// 3D gunsight works in feet with X = east, Y = up, Z = south (so north is -Z and everything is right-handed).
/// </summary>
public sealed class World
{
    /// <summary>Every aircraft in the world.</summary>
    public readonly List<Aircraft> Planes = new();

    /// <summary>The player's aircraft.</summary>
    public Aircraft Player { get; private set; }

    /// <summary>A fresh aircraft for the player at pos (world px), replacing any old one. arcade doubles the altitude rates.</summary>
    public Aircraft SpawnPlayer(Pilot pilot, Vector2 pos, bool arcade)
    {
        if (Player != null) Planes.Remove(Player);
        Player = new Spitfire(pilot) { Pos = pos, IsPlayer = true };
        Player.Flight.VerticalRateScale = arcade ? 2f : 1f;
        Planes.Add(Player);
        Refresh();
        return Player;
    }

    private readonly List<Aircraft> _others = new();

    /// <summary>Every aircraft but the player's.</summary>
    public IReadOnlyList<Aircraft> Others => _others;

    /// <summary>Rounds that have hit something.</summary>
    public int Hits { get; private set; }

    public void Add(Aircraft a) { Planes.Add(a); Refresh(); }
    public void Remove(Aircraft a) { Planes.Remove(a); Refresh(); }

    private void Refresh()
    {
        _others.Clear();
        foreach (var a in Planes) if (a != Player) _others.Add(a);
    }

    /// <summary>Every aircraft but the player's flies a tick (the game steps the player itself, once it has worked out the
    /// player's inputs). Any that hit the ground are gone.</summary>
    public void UpdateOthers(Fx fx, Random rng)
    {
        for (var i = Planes.Count - 1; i >= 0; i--)
        {
            var a = Planes[i];
            if (a == Player) continue;
            a.Step(fx, rng);
            if (a.Crashed) Planes.RemoveAt(i);
        }
        Refresh();
    }

    // ---------------------------------------------------------------- hits

    /// <summary>Hit scan: the nearest aircraft (other than ignore) whose hit boxes the ray passes through within maxRange
    /// feet, and the part hit. A part already gone lets rounds through.</summary>
    public bool RayHit(Vector3 origin, Vector3 dir, float maxRange, Aircraft ignore, out Aircraft hit, out float distance, out Part part)
    {
        hit = null; distance = maxRange; part = Part.Fuselage;
        foreach (var p in Planes)
        {
            if (p == ignore) continue;
            Basis(p.Heading, p.Pitch, p.Bank, out var r, out var u, out var f);
            var rel = origin - p.PositionFt;
            if (rel.LengthSquared() > (maxRange + 40f) * (maxRange + 40f)) continue;
            // Into the plane's frame (local +z is backwards, so forward is -z).
            Vector3 o = new(Vector3.Dot(rel, r), Vector3.Dot(rel, u), Vector3.Dot(rel, -f));
            Vector3 d = new(Vector3.Dot(dir, r), Vector3.Dot(dir, u), Vector3.Dot(dir, -f));
            foreach (var pt in p.Parts)
            {
                if (pt.Gone) continue;
                foreach (var b in pt.Boxes)
                    if (Slab(o, d, b.Min, b.Max, out var t) && t < distance)
                    {
                        distance = t; hit = p; part = pt.Kind;
                    }
            }
        }
        return hit != null;
    }

    /// <summary>A round hits a part of an aircraft (see Aircraft.Hit). Returns the hit points the part lost.</summary>
    public float Hit(Aircraft a, Part part, DamageTuning.Weapon weapon, float impactSpeedFtS, Random rng)
    {
        Hits++;
        return a.Hit(part, weapon, impactSpeedFtS, rng);
    }

    /// <summary>The twelve edges of every hit box of an aircraft, as pairs of world points (feet), coloured by the state of
    /// the part the box belongs to. For the debug hit box view.</summary>
    public static void HitBoxEdges(Aircraft p, List<(Vector3 a, Vector3 b, Color color)> into)
    {
        var boxes = new List<(Vector3[] c, Part part)>();
        HitBoxCorners(p, boxes);
        foreach (var (c, part) in boxes)
        {
            var color = DamageModel.StateColor(p[part].State);
            for (var i = 0; i < 8; i++)
                for (var bit = 1; bit <= 4; bit <<= 1)
                    if ((i & bit) == 0) into.Add((c[i], c[i | bit], color));
        }
    }

    /// <summary>The eight world corners (feet) of each of an aircraft's hit boxes, with the part each box belongs to.</summary>
    public static void HitBoxCorners(Aircraft p, List<(Vector3[] corners, Part part)> into)
    {
        Basis(p.Heading, p.Pitch, p.Bank, out var r, out var u, out var f);
        var centre = p.PositionFt;
        foreach (var pt in p.Parts)
        foreach (var bx in pt.Boxes)
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

    // ---------------------------------------------------------------- conventions

    public const float PxPerFoot = 1.2f;

    /// <summary>
    /// The view box: how far the player can see in every direction (up, down, forward, sideways), in feet. It is
    /// the same distance the map's camera allows above the plane. Things near its edge go out of focus and fade;
    /// beyond it there is only sky.
    /// </summary>
    public const float ViewBoxFt = 3200f;

    public static float Smooth(float a, float b, float x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Deterministic integer hash used to scatter scenery and clouds without storing them.</summary>
    public static uint Hash(int x, int y, uint salt)
    {
        var h = (uint)(x * 374761393 + y * 668265263) ^ salt * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    public static Vector3 ToFt(Vector2 px, float altitudeFt) => new(px.X / PxPerFoot, altitudeFt, px.Y / PxPerFoot);

    /// <summary>Orientation of an aircraft: heading (rad clockwise from north), nose pitch and bank (+ = right wing down).</summary>
    public static void Basis(float heading, float pitch, float bank, out Vector3 right, out Vector3 up, out Vector3 forward)
    {
        float sh = MathF.Sin(heading), ch = MathF.Cos(heading), sp = MathF.Sin(pitch), cp = MathF.Cos(pitch);
        forward = new Vector3(sh * cp, sp, -ch * cp);
        var r0 = new Vector3(ch, 0f, sh);
        var u0 = Vector3.Cross(r0, forward);
        float sb = MathF.Sin(bank), cb = MathF.Cos(bank);
        right = r0 * cb - u0 * sb;
        up = u0 * cb + r0 * sb;
    }
}
