using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// The four inputs every pilot gives every aircraft, the player and the AI alike, whatever the type. They say what the
/// pilot wants; the aircraft's flight model (FlightModel) decides what it actually does with them, which depends on the
/// aircraft: the same full throttle is more thrust in a stronger engine, the same full pull a tighter turn in a better
/// turner, and a damaged engine gives less for the same lever.
///
/// The player's mouse, wheel and keys are turned into these each tick (Game1); an AI pilot sets them itself. The flight
/// model reads nothing else from the pilot.
/// </summary>
public sealed class ControlInputs
{
    /// <summary>Throttle lever, 0 (idle) to 1 (full). -> engine power -> thrust.</summary>
    public float Throttle = 0.55f;

    /// <summary>Elevator, -1 (stick forward, nose down) to +1 (stick back, nose up). Held, the nose keeps moving.</summary>
    public float Pitch;

    /// <summary>Ailerons, -1 (left) to +1 (right): how far to bank, as a fraction of the aircraft's steepest bank. Held, the
    /// aircraft rolls to that bank and turns.</summary>
    public float Roll;

    /// <summary>Rudder, -1 (left) to +1 (right): swings the nose without banking, at the cost of a skid and some drag.</summary>
    public float Yaw;

    /// <summary>Keeps every input in its range.</summary>
    public void Clamp()
    {
        Throttle = MathHelper.Clamp(Throttle, 0f, 1f);
        Pitch = MathHelper.Clamp(Pitch, -1f, 1f);
        Roll = MathHelper.Clamp(Roll, -1f, 1f);
        Yaw = MathHelper.Clamp(Yaw, -1f, 1f);
    }

    public void CopyFrom(ControlInputs o) { Throttle = o.Throttle; Pitch = o.Pitch; Roll = o.Roll; Yaw = o.Yaw; }
}
