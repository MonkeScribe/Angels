using System;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// One aircraft, the player's or anyone else's: where it is, its flight model (which holds its control inputs), its
/// damage, how it is drawn, and the pilot flying it. Each tick the pilot sets the controls and the aircraft does what its
/// flight model and damage make of them.
/// </summary>
public sealed class Aircraft
{
    public Vector2 Pos;                                   // world px (map)
    public readonly FlightModel Flight;
    public readonly AircraftDamage Damage = new();
    public SpriteSphere Sphere;                           // its pictures (the type's look)
    public Pilot Pilot;
    public bool IsPlayer;
    /// <summary>How quickly it rolls to the bank its pilot asks for: the fraction of the gap closed each tick.</summary>
    public float BankResponse = 0.18f;

    public ControlInputs Controls => Flight.Controls;
    public Vector3 PositionFt => World.ToFt(Pos, Flight.Altitude);

    public Aircraft(FlightModel flight, Pilot pilot, SpriteSphere sphere = null)
    {
        Flight = flight;
        Pilot = pilot;
        Sphere = sphere;
    }

    /// <summary>Where the engine is (world feet): in the nose, ahead of the origin and a little below the centre line.</summary>
    public Vector3 EngineFt()
    {
        World.Basis(Flight.Heading, Flight.Gamma, Flight.Bank, out _, out var u, out var f);
        return PositionFt + f * 11f - u * 0.5f;
    }

    /// <summary>One tick of flight: the pilot sets the controls; the engine's damage and fuel set the power; a damaged
    /// engine trails smoke (and fire), a leak a thin mist; the flight model flies; the aircraft moves.</summary>
    public void Step(Fx fx, Random rng)
    {
        Pilot?.Fly(this);
        Damage.Update(1f / 60f, Flight.Throttle, true);
        Flight.EnginePower = Damage.EnginePower;
        if (Damage.SmokeStrength > 0f || Damage.OnFire || Damage.Leaks > 0)
        {
            World.Basis(Flight.Heading, Flight.Gamma, Flight.Bank, out _, out _, out var f);
            fx.EngineTrail(EngineFt() - f * 4f, -f, Damage.SmokeStrength, Damage.FireStrength, Damage.Leaks, IsPlayer);
        }
        Flight.Step(BankResponse, rng);
        var dir = new Vector2(MathF.Sin(Flight.Heading), -MathF.Cos(Flight.Heading));
        Pos += dir * Flight.GroundSpeed * World.PxPerFoot / 60f;
    }
}
