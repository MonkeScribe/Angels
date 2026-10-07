using System;
using Microsoft.Xna.Framework;
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

/// <summary>
/// An AI pilot flying in a loose V. The leader cruises: a steady throttle, a set altitude, and a course that curves
/// gently. Each wingman holds a slot off the leader: it steers onto the line of its slot, works the throttle to close up
/// or drop back along it, and climbs or descends to the slot's height. If the leader is gone the wingman flies the
/// formation's course on its own. Like any pilot it only sets the four control inputs; the aircraft does the flying, so a
/// damaged engine that can't keep up falls out of formation.
/// </summary>
public sealed class FormationPilot : Pilot
{
    public Aircraft Leader;            // null for the leader itself
    public Vector2 Slot;               // world px off the leader: x toward its right wing, y back
    public float SlotAltFt;            // feet above (+) or below (-) the leader
    public float CourseHeading;        // rad: the course flown with no leader to follow
    public float TurnRate;             // rad per tick the course curves by
    public float CruiseAltFt;
    public float CruiseThrottle = 0.6f;

    // How hard it flies at the slot (tune here).
    private const float SteerLeadFt = 600f;       // steers back onto the slot's line over about this distance
    private const float MaxSteerRad = 0.5f;       // never more than this off the leader's heading to get there
    private const float RollGain = 2.5f;          // full bank at 0.4 rad of heading error
    private const float ThrottlePerFt = 0.0015f;  // throttle added per foot behind the slot
    private const float ThrottlePerFtS = 0.01f;   // and per ft/s slower than the leader
    private const float ClimbLeadS = 8f;          // climbs or descends to its height over about this many seconds
    private const float MaxClimbDeg = 12f;

    public override void Fly(Aircraft me)
    {
        var f = me.Flight;
        var c = me.Controls;
        float wantHeading, wantAlt, throttle;
        if (Leader == null || Leader.Crashed)
        {
            CourseHeading = MathHelper.WrapAngle(CourseHeading + TurnRate);
            wantHeading = CourseHeading;
            wantAlt = CruiseAltFt;
            throttle = CruiseThrottle;
        }
        else
        {
            var lh = Leader.Heading;
            Vector2 dir = new(MathF.Sin(lh), -MathF.Cos(lh)), right = new(MathF.Cos(lh), MathF.Sin(lh));
            var slot = Leader.Pos + right * Slot.X - dir * Slot.Y;
            var err = (slot - me.Pos) / World.PxPerFoot;                  // feet to the slot
            float along = Vector2.Dot(err, dir), cross = Vector2.Dot(err, right);
            wantHeading = lh + MathHelper.Clamp(MathF.Atan2(cross, SteerLeadFt), -MaxSteerRad, MaxSteerRad);
            wantAlt = Leader.Altitude + SlotAltFt;
            throttle = Leader.Controls.Throttle + ThrottlePerFt * along + ThrottlePerFtS * (Leader.Flight.Speed - f.Speed);
        }
        c.Roll = MathHelper.Clamp(MathHelper.WrapAngle(wantHeading - f.Heading) * RollGain, -1f, 1f);
        var gammaWant = MathHelper.Clamp(MathHelper.ToDegrees(MathF.Atan2(wantAlt - f.Altitude, MathF.Max(f.Speed, 100f) * ClimbLeadS)),
            -MaxClimbDeg, MaxClimbDeg);
        c.Pitch = MathHelper.Clamp((gammaWant - f.PitchCmdDeg) / 3f, -1f, 1f);   // a full stick moves the command 3 deg a tick
        c.Throttle = MathHelper.Clamp(throttle, 0f, 1f);
        c.Yaw = 0f;
    }
}
