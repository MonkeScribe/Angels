using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// The world: the aircraft in it (for now just the player's; the other traffic is still in Traffic), and the shared
/// conventions everything uses. The top-down map works in "world px" (x east, y south) and the 3D gunsight works in
/// feet with X = east, Y = up, Z = south (so north is -Z and everything is right-handed).
/// </summary>
public sealed class World
{
    /// <summary>Every aircraft in the world.</summary>
    public readonly List<Aircraft> Planes = new();

    /// <summary>The player's aircraft.</summary>
    public Aircraft Player { get; private set; }

    /// <summary>A fresh aircraft for the player at pos (world px), replacing any old one. arcade doubles the altitude rates.</summary>
    public Aircraft SpawnPlayer(Pilot pilot, Vector2 pos, bool arcade, SpriteSphere sphere)
    {
        if (Player != null) Planes.Remove(Player);
        Player = new Aircraft(new FlightModel { VerticalRateScale = arcade ? 2f : 1f }, pilot, sphere) { Pos = pos, IsPlayer = true };
        Planes.Add(Player);
        return Player;
    }

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
