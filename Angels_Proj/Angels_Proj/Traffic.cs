using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// Brings other aircraft into the world and takes them away again: loose V formations of 2-5 Spitfires at assorted
/// altitudes near the player, each a full aircraft (Spitfire) flown by an AI pilot (FormationPilot) on the same flight model and
/// damage as the player's. Formations that drift out of the area where they could be seen are taken away and new ones
/// brought in somewhere visible.
/// </summary>
public sealed class Traffic
{
    private sealed class Formation
    {
        public readonly List<Aircraft> Planes = new();   // [0] the leader
    }

    // Slots in the V, world px off the leader: x toward its right wing, y back.
    private static readonly Vector2[] Slots =
    {
        new(0, 0), new(-110, 100), new(110, 100), new(-220, 200), new(220, 200),
    };

    private const int FormationCount = 6;

    private readonly List<Formation> _formations = new();
    private readonly Random _rng;
    private readonly SpriteSphere _sphere;

    public Traffic(SpriteSphere sphere, int seed = 5) { _sphere = sphere; _rng = new Random(seed); }

    private float Rand(float lo, float hi) => lo + (float)_rng.NextDouble() * (hi - lo);

    /// <param name="extentOfFactor">World-px half-extent of the screen for something at a given distance factor.</param>
    public void Update(World world, Vector2 playerPos, float playerAlt, Func<float, float> extentOfFactor, Func<float, float> factorOfAltitude)
    {
        while (_formations.Count < FormationCount)
            _formations.Add(NewFormation(world, playerPos, playerAlt, extentOfFactor, factorOfAltitude, true));

        for (var i = 0; i < _formations.Count; i++)
        {
            var f = _formations[i];
            f.Planes.RemoveAll(x => x.Crashed);
            var lead = f.Planes.Count > 0 ? f.Planes[0] : null;
            var factor = lead == null ? 0f : factorOfAltitude(lead.Altitude);
            var extent = extentOfFactor(MathF.Max(factor, 0.2f));
            if (lead == null || factor < 0.25f || factor > 1.8f || Vector2.Distance(lead.Pos, playerPos) > extent * 2.4f)
            {
                foreach (var a in f.Planes) world.Remove(a);
                _formations[i] = NewFormation(world, playerPos, playerAlt, extentOfFactor, factorOfAltitude, false);
            }
        }
    }

    private Formation NewFormation(World world, Vector2 playerPos, float playerAlt, Func<float, float> extentOfFactor, Func<float, float> factorOfAltitude, bool anywhere)
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
        var heading = anywhere ? Rand(-MathF.PI, MathF.PI) : MathHelper.WrapAngle(toPlayer + Rand(-0.9f, 0.9f));
        return Build(world, pos, alt, heading, _rng.Next(2, 6), Rand(-0.0025f, 0.0025f), Rand(0.5f, 0.7f), Rand(240f, 290f));
    }

    /// <summary>Puts a small formation dead ahead of the player at the same height (a debug aid for target practice).</summary>
    public void SpawnAhead(World world, Vector2 playerPos, float altitude, float heading, float distanceFt, float mph)
    {
        Vector2 dir = new(MathF.Sin(heading), -MathF.Cos(heading));
        var f = Build(world, playerPos + dir * distanceFt * World.PxPerFoot, altitude, heading, 3, 0f, 0.55f, mph);
        if (_formations.Count >= FormationCount + 1)
        {
            foreach (var a in _formations[0].Planes) world.Remove(a);
            _formations.RemoveAt(0);
        }
        _formations.Add(f);
    }

    /// <summary>A formation of count aircraft, leader at pos, all heading the same way at mph, added to the world.</summary>
    private Formation Build(World world, Vector2 pos, float alt, float heading, int count, float turnRate, float throttle, float mph)
    {
        var f = new Formation();
        Vector2 dir = new(MathF.Sin(heading), -MathF.Cos(heading)), right = new(MathF.Cos(heading), MathF.Sin(heading));
        for (var i = 0; i < count; i++)
        {
            var slotAlt = i == 0 ? 0f : (i % 2 == 0 ? 25f : -25f);
            var pilot = new FormationPilot
            {
                Leader = i == 0 ? null : f.Planes[0], Slot = Slots[i], SlotAltFt = slotAlt,
                CourseHeading = heading, TurnRate = turnRate, CruiseAltFt = alt + slotAlt, CruiseThrottle = throttle,
            };
            var a = new Spitfire(pilot, _sphere)
            {
                Pos = pos + right * Slots[i].X - dir * Slots[i].Y,
                Salt = _rng.Next(64),
            };
            var fm = a.Flight;
            fm.Altitude = alt + slotAlt;
            fm.Heading = heading;
            fm.Speed = mph / FlightModel.Mph;
            fm.SnapOnRelease = false;
            fm.Throttle = throttle;
            f.Planes.Add(a);
            world.Add(a);
        }
        return f;
    }
}
