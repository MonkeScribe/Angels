using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// The clouds: an endless, deterministic scatter through the sky, in 1,000 ft altitude slices. They are
/// rare low down and get thicker with height, so as you climb out of sight of the ground the sky fills up.
/// The map and the gunsight both read the same field.
/// </summary>
public static class CloudField
{
    public const float SliceFt = 1000f;
    public const int Cell = 800;                    // world px

    public struct Puff
    {
        public Vector2 Pos;                         // world px
        public float Height;                        // ft
        public float Size;                          // multiplier on the 256 px sprite, in world px
        public int Variant;
    }

    private static float Chance(float heightFt) =>
        heightFt < 1200f ? 0f : 0.03f + 0.45f * Math.Clamp((heightFt - 1200f) / 26000f, 0f, 1f);

    public static bool TryGet(int cx, int cy, int slice, out Puff p)
    {
        p = default;
        var chance = Chance(slice * SliceFt);
        var salt = 400u + (uint)(slice * 7 + 1000);
        if (World.Hash(cx, cy, salt) % 1000 >= (uint)(chance * 1000f)) return false;
        p.Pos = new Vector2(
            (cx + (World.Hash(cx, cy, salt + 1) % 100) / 100f) * Cell,
            (cy + (World.Hash(cx, cy, salt + 2) % 100) / 100f) * Cell);
        p.Height = slice * SliceFt + (World.Hash(cx, cy, salt + 3) % 1000);
        p.Size = 2.2f + (World.Hash(cx, cy, salt + 4) % 30) / 10f;
        p.Variant = (int)(World.Hash(cx, cy, salt + 5) % 3);
        return true;
    }

    /// <summary>Every cloud within radiusPx horizontally of a point and boxFt vertically of an altitude.</summary>
    public static void Query(Vector2 centerPx, float radiusPx, float altitudeFt, float boxFt, List<Puff> into)
    {
        into.Clear();
        int s0 = (int)MathF.Floor(MathF.Max(0f, altitudeFt - boxFt) / SliceFt), s1 = (int)MathF.Floor((altitudeFt + boxFt) / SliceFt);
        int cx0 = (int)MathF.Floor((centerPx.X - radiusPx) / Cell), cx1 = (int)MathF.Floor((centerPx.X + radiusPx) / Cell);
        int cy0 = (int)MathF.Floor((centerPx.Y - radiusPx) / Cell), cy1 = (int)MathF.Floor((centerPx.Y + radiusPx) / Cell);
        for (var s = s0; s <= s1; s++)
            for (var cy = cy0; cy <= cy1; cy++)
                for (var cx = cx0; cx <= cx1; cx++)
                    if (TryGet(cx, cy, s, out var p) && MathF.Abs(p.Height - altitudeFt) <= boxFt)
                        into.Add(p);
    }
}
