using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// Other aircraft: loose V formations flying lazy curves at assorted altitudes. They are not full flight
/// models, just constant-speed, constant-altitude flyers (as in Skyward). Formations that drift out of
/// the area where they could be seen are recycled somewhere visible, so the sky stays populated.
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

    public struct Craft
    {
        public Vector2 Pos;
        public float Altitude, Heading, Bank;
        public int Color;
    }

    private sealed class Formation
    {
        public Vector2 Pos;
        public float Altitude, Heading, Speed, TurnRate; // Speed in world px per tick, TurnRate rad per tick
        public int Color, Count;
    }

    // Slots in (right, back) world px behind the leader.
    private static readonly Vector2[] Slots =
    {
        new(0, 0), new(-110, 100), new(110, 100), new(-220, 200), new(220, 200),
    };

    private const int FormationCount = 6;
    private const float PxPerTickPerMph = 1.4667f * 1.2f / 60f; // mph -> ft/s -> world px/s -> per tick

    private readonly List<Formation> _formations = new();
    private readonly Random _rng;

    public Traffic(int seed = 5) => _rng = new Random(seed);

    private float Rand(float lo, float hi) => lo + (float)_rng.NextDouble() * (hi - lo);

    /// <param name="extentOfFactor">World-px half-extent of the screen for something at a given distance factor.</param>
    public void Update(Vector2 playerPos, float playerAlt, Func<float, float> extentOfFactor, Func<float, float> factorOfAltitude)
    {
        while (_formations.Count < FormationCount) _formations.Add(NewFormation(playerPos, playerAlt, extentOfFactor, factorOfAltitude, true));
        for (var i = 0; i < _formations.Count; i++)
        {
            var f = _formations[i];
            f.Heading = MathHelper.WrapAngle(f.Heading + f.TurnRate);
            f.Pos += new Vector2(MathF.Sin(f.Heading), -MathF.Cos(f.Heading)) * f.Speed;

            var factor = factorOfAltitude(f.Altitude);
            var extent = extentOfFactor(MathF.Max(factor, 0.2f));
            if (factor < 0.25f || Vector2.Distance(f.Pos, playerPos) > extent * 2.4f)
                _formations[i] = NewFormation(playerPos, playerAlt, extentOfFactor, factorOfAltitude, false);
        }
    }

    private Formation NewFormation(Vector2 playerPos, float playerAlt, Func<float, float> extentOfFactor, Func<float, float> factorOfAltitude, bool anywhere)
    {
        // Mostly below or near the player, where the camera can see them.
        var alt = Math.Clamp(playerAlt + Rand(-12000f, 2500f), 800f, 45000f);
        var factor = MathF.Max(factorOfAltitude(alt), 0.3f);
        var extent = extentOfFactor(factor);
        var ang = Rand(0f, MathF.Tau);
        // First fill: scatter across the view. Later: appear near the edge, heading roughly across the screen.
        var radius = extent * (anywhere ? Rand(0.1f, 1.0f) : Rand(1.0f, 1.5f));
        var pos = playerPos + new Vector2(MathF.Sin(ang), -MathF.Cos(ang)) * radius;
        var toPlayer = MathF.Atan2(playerPos.X - pos.X, -(playerPos.Y - pos.Y));
        return new Formation
        {
            Pos = pos,
            Altitude = alt,
            Heading = anywhere ? Rand(-MathF.PI, MathF.PI) : MathHelper.WrapAngle(toPlayer + Rand(-0.9f, 0.9f)),
            Speed = Rand(200f, 330f) * PxPerTickPerMph,
            TurnRate = Rand(-0.0030f, 0.0030f),
            Color = _rng.Next(Colors.Length),
            Count = _rng.Next(2, 6),
        };
    }

    public void Collect(List<Craft> into)
    {
        into.Clear();
        foreach (var f in _formations)
        {
            Vector2 dir = new(MathF.Sin(f.Heading), -MathF.Cos(f.Heading));
            Vector2 right = new(MathF.Cos(f.Heading), MathF.Sin(f.Heading));
            for (var i = 0; i < f.Count; i++)
                into.Add(new Craft
                {
                    Pos = f.Pos + right * Slots[i].X - dir * Slots[i].Y,
                    Altitude = f.Altitude + (i == 0 ? 0f : (i % 2 == 0 ? 25f : -25f)),
                    Heading = f.Heading,
                    Bank = f.TurnRate * 120f, // lean into the curve
                    Color = f.Color,
                });
        }
    }
}
