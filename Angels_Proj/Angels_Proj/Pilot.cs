using Microsoft.Xna.Framework.Input;

namespace Angels_Proj;

/// <summary>Whoever flies an aircraft: each tick it sets the aircraft's control inputs (throttle, pitch, roll, yaw) and
/// nothing else. The player and every AI are pilots.</summary>
public abstract class Pilot
{
    public abstract void Fly(Aircraft plane);
}

/// <summary>
/// The player. The keys are read here: Shift/Z move the throttle lever, W/S the elevator, A/D the rudder. The bank the
/// player wants comes from the mouse (and the aim and pointing assists), which the game works out and hands over in
/// Bank each tick.
/// </summary>
public sealed class PlayerPilot : Pilot
{
    public const float ThrottleRate = 0.012f;   // lever travel per tick with the key held

    public float ThrottleKey, PitchKey, RudderKey;   // -1, 0 or +1 from the keys
    public float Bank;                               // -1..1 of the aircraft's steepest bank, from the mouse

    public void ReadKeys(KeyboardState kb)
    {
        ThrottleKey = (kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift)) ? 1f : kb.IsKeyDown(Keys.Z) ? -1f : 0f;
        PitchKey = (kb.IsKeyDown(Keys.S) ? 1f : 0f) - (kb.IsKeyDown(Keys.W) ? 1f : 0f);
        RudderKey = (kb.IsKeyDown(Keys.D) ? 1f : 0f) - (kb.IsKeyDown(Keys.A) ? 1f : 0f);
    }

    public override void Fly(Aircraft plane)
    {
        var c = plane.Controls;
        c.Throttle += ThrottleKey * ThrottleRate;
        c.Pitch = PitchKey;
        c.Roll = Bank;
        c.Yaw = RudderKey;
    }
}
