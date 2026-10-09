using System;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>Ways an aircraft can be past flying (see Aircraft.CheckLosses), in rising order of how badly: when two
/// happen the worse one wins.</summary>
public enum FlightFailure { None, DeadPilot, Spin, NoseDive }

/// <summary>
/// Point-mass flight model, in real units (feet, slugs, lb, ft/s), stepped at a fixed 60 ticks/s. It flies whatever
/// Airframe it is given (Aircraft.cs): performance is not scripted but falls out of that airframe's thrust, drag, lift
/// and limits and the air density, so the same control inputs do different things in different aircraft. The
/// airframe comes from the aircraft type (see Spitfire).
/// Horizontal steering (bank/heading) keeps Skyward's arcade turn model.
/// </summary>
public sealed class FlightModel
{
    // The aircraft's airframe, engine and limits (weights, wing, drag, power, g and speed limits) are in Airframe
    // (Aircraft.cs): each flight model flies the airframe it was given, so different types fly differently.
    public readonly Airframe Airframe;
    private const float G = 32.174f;

    private const float WheelSlewDeg = 5f;         // the wheel swings the nose this fast at most (deg per tick), ignoring the g limit
    private const float WheelEase = 0.15f;         // fraction of the gap to the command the nose closes each tick: it eases in and settles
    private const float PointSlewDeg = 2f, PointEase = 0.06f; // the pointing assist swings the nose more gently than the wheel does
    private const float WheelLeadDeg = 25f;        // and never lets the command get further than this ahead of the nose
    private const float PitchDetentDeg = 10f;

    // Arcade steering (ported from Skyward's Spitfire).
    public const float MaxBank = 0.66f;
    private const float TurnCoeff = 0.058f;
    private const float MphPerUnit = 68f;          // Skyward's speed unit, used by the turn-rate curve

    public const float Mph = 0.681818f;            // ft/s -> mph
    private const float Rho0 = 0.0023769f;
    private const float Dt = 1f / 60f;

    // State.
    public float Altitude = 5000f;                 // ft
    public float Speed;                            // true airspeed, ft/s
    public float Gamma;                            // flight path angle, rad (+ = climbing)
    public float PitchCmdDeg;                      // commanded flight-path angle, deg; settles to the nearest 10 on release
    private bool _pointSwing;                      // the swing is the pointing assist's, so it uses the gentler rates
    private bool _wheelSteered;                    // the command came from the wheel: the nose goes straight to it and locks there
    /// <summary>The pilot's inputs (Controls.cs): all the flight model takes from whoever is flying it.</summary>
    public readonly ControlInputs Controls = new();
    public float Throttle { get => Controls.Throttle; set => Controls.Throttle = value; }
    /// <summary>Fraction of the engine's power available, 0-1: set each tick from the engine's damage and fuel (Damage.cs).</summary>
    public float EnginePower = 1f;
    private bool _pitchHeld;
    public bool Aiming;                            // the aimer is up: the aim assist flies the pitch
    public float AimElevationDeg;                  // while aiming: the elevation the assist points the nose at
    public float AimTrimDeg;                       // while aiming: the player's fine adjustment on top of it
    private const float AimTrimMaxDeg = 90f;       // how far the player can move the pitch off the assist's aim point: anywhere
    private const float AimKeyRateDeg = 0.25f;     // W/S trim rate while aiming, deg per tick
    private bool _keysBlocked;                     // a pitch key held when the aimer came up is ignored until it is let go
    public float Bank, Heading;
    public bool SnapOnRelease = true;               // let go of the stick: the pitch settles at the nearest 10 degrees
    public float VerticalRateScale = 1f;           // 1 = realistic; 2 = arcade (altitude changes twice as fast)
    public float ThrustScale = 1f;                 // 1 = realistic; 2 = the debug menu's 2X THRUST (player only)

    // What damage leaves of the controls, set each tick by the aircraft (Aircraft.UpdateControls). Authorities are the
    // share of each maximum rate still there (1 = all of it): pitch (elevator), roll (ailerons), yaw (rudder) and turn.
    public float PitchAuthority = 1f, RollAuthority = 1f, YawAuthority = 1f, TurnAuthority = 1f;
    public float RollBias;                         // uncommanded roll from uneven lift, fraction of MaxBank (+ = right)
    public float LiftLoss;                         // share of the wings' lift lost
    public float NoseDropDegS;                     // the nose sinks this fast with nothing holding it up
    /// <summary>Out of control (Aircraft.CheckLosses): the controls do nothing and the aircraft falls its own way.</summary>
    public FlightFailure Failure;
    public float SpinDir = 1f;                     // which way it rolls and turns when out of control (+1 right)

    // Read-outs.
    public float Rho, SoundSpeed, LoadFactor = 1f;
    public float YawRate, SlipBall;               // turn-and-slip feeds: rad/s (+ = right), -1..1 (+ = ball right)
    public bool GroundHit { get; private set; }
    public float ImpactSinkFpm, ImpactSpeedMph, ImpactAngleDeg;
    public bool AccelStall { get; private set; }  // wing overloaded by a hard turn
    public bool Stalled => Speed < StallSpeed(1f) || AccelStall;
    public float TasMph => Speed * Mph;
    public float IasMph => Speed * MathF.Sqrt(Rho / Rho0) * Mph;
    public float Mach => Speed / SoundSpeed;
    public float VerticalSpeedFpm => Speed * MathF.Sin(Gamma) * 60f * VerticalRateScale;
    public float GroundSpeed => Speed * MathF.Cos(Gamma); // ft/s
    public bool Overspeed => IasMph > Airframe.VneMph;

    public FlightModel(Airframe airframe)
    {
        Airframe = airframe;
        Atmosphere(Altitude, out Rho, out SoundSpeed);
        Speed = 270f / Mph; // roughly level cruise at 55% throttle
    }

    public static void Atmosphere(float altFt, out float rho, out float sound)
    {
        var h = altFt * 0.3048f;
        float t, density;
        if (h < 11000f) { t = 288.15f - 0.0065f * h; density = 1.225f * MathF.Pow(t / 288.15f, 4.2559f); }
        else { t = 216.65f; density = 0.3639f * MathF.Exp(-(h - 11000f) / 6341.6f); }
        rho = density * 0.0019403f;
        sound = MathF.Sqrt(1.4f * 287.05f * t) / 0.3048f;
    }

    private float PowerHp(float alt)
    {
        if (alt <= Airframe.PowerTable[0].alt) return Airframe.PowerTable[0].hp;
        for (var i = 1; i < Airframe.PowerTable.Length; i++)
            if (alt <= Airframe.PowerTable[i].alt)
            {
                var (a0, p0) = Airframe.PowerTable[i - 1];
                var (a1, p1) = Airframe.PowerTable[i];
                return MathHelper.Lerp(p0, p1, (alt - a0) / (a1 - a0));
            }
        return 0f;
    }

    /// <summary>The wheel moves the nose by this many degrees of flight path, straight away.</summary>
    public void WheelPitch(float deg)
    {
        if (deg == 0f) return;
        if (Aiming) { AimTrimDeg = MathHelper.Clamp(AimTrimDeg + deg, -AimTrimMaxDeg, AimTrimMaxDeg); return; }
        var g = MathHelper.ToDegrees(Gamma);
        float lo = MathF.Max(-Airframe.MaxDiveDeg, g - WheelLeadDeg), hi = MathF.Min(Airframe.MaxClimbDeg, g + WheelLeadDeg);
        PitchCmdDeg = MathHelper.Clamp(PitchCmdDeg + deg, MathF.Min(lo, hi), hi);
        _wheelSteered = true; _pointSwing = false;
        _pitchHeld = false;
    }

    /// <summary>The aimer has just come up and the assist takes the pitch: drop whatever the wheel or a held key
    /// was still asking for, start with no trim, and ignore a held key until it is let go.</summary>
    public void AimAcquired()
    {
        AimTrimDeg = 0f;
        _wheelSteered = true; _pointSwing = false;
        _pitchHeld = false;
        _keysBlocked = true;
    }

    /// <summary>Mouse aim while the aimer is up: move the pitch trim by this many degrees.</summary>
    public void AimTrimBy(float deg) => AimTrimDeg = MathHelper.Clamp(AimTrimDeg + deg, -AimTrimMaxDeg, AimTrimMaxDeg);

    /// <summary>Pointing assist: swing the nose to this flight-path angle the way the wheel does (eased, then held),
    /// within the climb and dive limits.</summary>
    public void PointPitch(float deg)
    {
        PitchCmdDeg = MathHelper.Clamp(deg, -Airframe.MaxDiveDeg, Airframe.MaxClimbDeg);
        _wheelSteered = true; _pointSwing = true;
        _pitchHeld = false;
    }

    /// <summary>Middle click: swing the nose back to level.</summary>
    public void WheelLevel()
    {
        if (Aiming) { AimTrimDeg = 0f; return; }
        PitchCmdDeg = 0f;
        _wheelSteered = true; _pointSwing = false;
        _pitchHeld = false;
    }

    /// <summary>Nearest 10-degree detent, within the climb/dive limits (6 -> 10, 3 -> 0, 16 -> 20), judged from the nose's actual angle.</summary>
    public float SnapPitch(float deg) =>
        MathHelper.Clamp(MathF.Round(deg / PitchDetentDeg, MidpointRounding.AwayFromZero) * PitchDetentDeg,
            -MathF.Floor(Airframe.MaxDiveDeg / PitchDetentDeg) * PitchDetentDeg, MathF.Floor(Airframe.MaxClimbDeg / PitchDetentDeg) * PitchDetentDeg);

    /// <summary>Moves up or down along the flight path for a tick, and reports touching the ground (the game decides
    /// whether it was a crash landing or a wreck).</summary>
    private void Climb()
    {
        Altitude += Speed * MathF.Sin(Gamma) * Dt * VerticalRateScale;
        if (Altitude > 0f) return;
        GroundHit = true;
        ImpactSinkFpm = MathF.Max(0f, -VerticalSpeedFpm);
        ImpactSpeedMph = Speed * Mph;
        ImpactAngleDeg = MathHelper.ToDegrees(-Gamma);
        Altitude = 0f;
        Gamma = MathF.Max(Gamma, 0f);
    }

    /// <summary>One tick out of control (Failure): the controls do nothing but the throttle, and the aircraft falls its own way.
    ///   NoseDive  - the airframe has broken: the nose goes over into a steep dive, it rolls and corkscrews, gathering speed.
    ///   Spin      - a wing is gone: it rolls over and over toward the missing wing, nose well down, turning fast.
    ///   DeadPilot - nobody flying: it holds its heading, the wings drift level, the nose sinks and it dives in.</summary>
    private void StepOutOfControl()
    {
        Atmosphere(Altitude, out Rho, out SoundSpeed);
        var turn = 0f;
        switch (Failure)
        {
            case FlightFailure.NoseDive:
                Gamma += (-0.95f - Gamma) * 0.02f;
                Bank += (SpinDir * 1.2f - Bank) * 0.03f;
                turn = SpinDir * 0.012f;
                Speed = MathF.Min(380f / Mph, Speed + 0.2f / Mph);
                break;
            case FlightFailure.Spin:
                Gamma += (-1.25f - Gamma) * 0.03f;
                Bank = MathHelper.WrapAngle(Bank + SpinDir * 0.12f);
                turn = SpinDir * 0.035f;
                Speed += (230f / Mph - Speed) * 0.01f;
                break;
            case FlightFailure.DeadPilot:
                Bank *= 0.995f;
                if (Gamma > -MathHelper.PiOver2 * 0.9f) Gamma -= MathHelper.ToRadians(NoseDropDegS) * Dt;
                // Gravity along the path, against a drag that holds it near cruise when level.
                Speed = MathHelper.Clamp(Speed + (-G * MathF.Sin(Gamma) - (Speed - 270f / Mph) * 0.05f) * Dt, 80f / Mph, 480f / Mph);
                break;
        }
        Heading = MathHelper.WrapAngle(Heading + turn);
        PitchCmdDeg = MathHelper.ToDegrees(Gamma);
        AccelStall = false;
        LoadFactor = 1f;
        YawRate = turn * 60f;
        SlipBall = 0f;
        Climb();
    }

    public float StallSpeed(float n) =>
        MathF.Sqrt(2f * n * Airframe.WeightLb / (Rho * Airframe.WingArea * Airframe.CLmax));

    /// <summary>One tick, flying on Controls: throttle sets the power, pitch moves the nose, roll the bank (and so the
    /// turn), yaw swings the nose flat with the rudder.</summary>
    public void Step(float bankResponse, Random rng)
    {
        Controls.Clamp();
        if (Failure != FlightFailure.None) { StepOutOfControl(); return; }
        var pitchKey = Controls.Pitch;
        var targetBank = Controls.Roll * MaxBank;
        if (pitchKey == 0f) _keysBlocked = false;
        if (_keysBlocked) pitchKey = 0f;
        if (Aiming)
        {
            // Aim assist flies the pitch: the nose goes to the target's elevation, plus whatever trim the player
            // has dialled in with the keys or wheel. When the aimer drops, the command stays where it was, so the
            // nose holds there.
            AimTrimDeg = MathHelper.Clamp(AimTrimDeg + pitchKey * AimKeyRateDeg, -AimTrimMaxDeg, AimTrimMaxDeg);
            PitchCmdDeg = MathHelper.Clamp(AimElevationDeg + AimTrimDeg, -Airframe.MaxDiveDeg, Airframe.MaxClimbDeg);
            _wheelSteered = true; _pointSwing = false;
            _pitchHeld = false;
        }
        else if (pitchKey != 0f)
        {
            // While the stick is held the command runs ahead of the nose, as fast as the stick is pushed (full stick,
            // 3 degrees a tick).
            PitchCmdDeg = MathHelper.Clamp(PitchCmdDeg + pitchKey * 3f, -Airframe.MaxDiveDeg, Airframe.MaxClimbDeg);
            _pitchHeld = true;
            _wheelSteered = false; // the keys take over from the wheel
        }
        else if (_pitchHeld)
        {
            // On release, settle at the detent nearest where the nose actually is, not where the command got to
            // (a help for the player; an AI pilot's stick isn't snapped).
            if (SnapOnRelease) PitchCmdDeg = SnapPitch(MathHelper.ToDegrees(Gamma));
            _pitchHeld = false;
        }
        // The ailerons roll to the bank asked for, as fast as what's left of them allows. A wing short of lift rolls the
        // aircraft toward it: the bank settles off what was asked for, and if the ailerons can't hold it, it keeps going.
        var bankTarget = MathHelper.Clamp(targetBank + RollBias * MaxBank, -MaxBank, MaxBank);
        Bank += (bankTarget - Bank) * bankResponse * RollAuthority;
        Bank += RollBias * DamageTuning.WingRollDriftRadS * (1f - RollAuthority) * Dt;
        Bank = MathHelper.Clamp(Bank, -MathHelper.PiOver2, MathHelper.PiOver2);

        Atmosphere(Altitude, out Rho, out SoundSpeed);
        var v = MathF.Max(Speed, 20f);
        var q = 0.5f * Rho * v * v;
        var mach = v / SoundSpeed;

        // Wing capability: the most load factor (g) the wing can generate right now. Above about Mach 0.45 shock
        // stall (buffet) starts eating into maximum lift, so hard pulls at high speed stall the wing early.
        var clMaxEff = Airframe.CLmax * (1f - MathHelper.Clamp((mach - 0.45f) / 0.2f, 0f, 0.6f)) * (1f - 0.5f * LiftLoss);
        var nWing = clMaxEff * q * Airframe.WingArea / Airframe.WeightLb;

        // Turn load. A hard mouse turn (full bank) pulls g, and elevator authority grows with dynamic
        // pressure, so the same deflection pulls more g the faster you are. The wing shares its g between
        // the turn and any pitch change; if the turn asks for more than it has, the wing stalls.
        var bankFrac = MathF.Min(1f, MathF.Abs(Bank) / MaxBank);
        var nTurnCmd = MathF.Min(Airframe.NStruct, 1f + (Airframe.NTurnMax - 1f) * bankFrac * MathHelper.Clamp(q / Airframe.QRef, 0.35f, 2.8f));
        var nTurn = MathF.Min(nTurnCmd, MathF.Max(1f, nWing));
        var stallDepth = MathHelper.Clamp((nTurnCmd - nWing) / nTurnCmd, 0f, 1f);
        AccelStall = nTurnCmd > nWing && bankFrac > 0.05f;
        var nAvail = MathF.Min(Airframe.MaxNPos, MathF.Max(0f, nWing - (nTurnCmd - 1f)));

        // Pitch stick commands a flight-path angle; the wing's load factor decides how fast we get there.
        // High Mach stiffens the controls (compressibility); a low-energy wing can't pull at all.
        var gammaTarget = MathHelper.ToRadians(PitchCmdDeg);
        // Out of airspeed the wing and tail stop flying the nose: it drops through the horizon under its own weight
        // (a vertical climb that runs out of speed falls over rather than hanging there). 0 at stall speed, 1 at a standstill.
        var stallFrac = MathHelper.Clamp(1f - Speed / StallSpeed(1f), 0f, 1f);
        var rateWanted = MathHelper.Clamp((gammaTarget - Gamma) * 3f, -0.9f, 0.9f) * PitchAuthority;
        rateWanted *= MathHelper.Clamp(1f - (mach - 0.8f) / 0.1f, 0.2f, 1f);
        var nReq = MathF.Cos(Gamma) + rateWanted * v / G;
        var n = MathHelper.Clamp(nReq, -MathF.Min(Airframe.MaxNNeg, nWing), nAvail);
        if (_wheelSteered)
        {
            // The wheel points the nose: it eases to the command, whatever the wing could pull, quickly when the gap
            // is large and gently as it closes, and locks on it. (n above still sets the drag, so a hard swing costs speed.)
            var gap = gammaTarget - Gamma;
            var slew = MathHelper.ToRadians(_pointSwing ? PointSlewDeg : WheelSlewDeg) * PitchAuthority;   // a damaged tail slows it
            var ease = _pointSwing ? PointEase : WheelEase;
            // Below stall speed the elevator loses its bite, so the wheel can't hold the nose up.
            var bite = 1f - stallFrac;
            Gamma += MathF.Abs(gap) < MathHelper.ToRadians(0.02f) ? gap * bite * PitchAuthority : MathHelper.Clamp(gap * ease, -slew, slew) * bite;
            if (MathF.Abs(gammaTarget - Gamma) > MathHelper.ToRadians(1f)) n = gammaTarget > Gamma ? nAvail : -MathF.Min(Airframe.MaxNNeg, nWing);
        }
        else Gamma += G * (n - MathF.Cos(Gamma)) / v * Dt;
        if (NoseDropDegS > 0f && Gamma > -MathHelper.PiOver2 * 0.9f) Gamma -= MathHelper.ToRadians(NoseDropDegS) * Dt;
        if (stallFrac > 0f && Gamma > -MathHelper.PiOver2 * 0.9f)
            Gamma -= MathHelper.ToRadians(Airframe.StallNoseDropDegS) * stallFrac * Dt;
        Gamma = MathHelper.Clamp(Gamma, -MathHelper.PiOver2 * 0.995f, MathHelper.PiOver2 * 0.995f);
        LoadFactor = n + nTurn - 1f;

        // Drag: parasitic + induced (load factor, increased by banking) + compressibility + overspeed + idle prop.
        var nDrag = MathF.Max(0.3f, MathF.Abs(n) + nTurn - 1f);
        var cl = nDrag * Airframe.WeightLb / (q * Airframe.WingArea);
        var cd = Airframe.CD0 + cl * cl / (MathF.PI * Airframe.OswaldE * Airframe.AspectRatio) + Airframe.IdlePropDragCD * (1f - Throttle)
                 + Airframe.StallDragCD * stallDepth; // separated flow when the wing is over-pulled
        if (mach > 0.75f) cd += Airframe.WaveDragK * (mach - 0.75f) * (mach - 0.75f);
        var ias = v * MathF.Sqrt(Rho / Rho0) * Mph;
        if (ias > Airframe.VneMph) { var o = (ias - Airframe.VneMph) / 50f; cd += Airframe.OverspeedDragK * o * o; }
        cd += Airframe.RudderSlipDragCD * MathF.Abs(Controls.Yaw);   // a skid presents the fuselage's side to the air
        var drag = cd * q * Airframe.WingArea;

        // Propeller thrust: power / speed, with efficiency falling off at low speed.
        var eta = Airframe.PropEff * (1f - MathF.Exp(-v / 60f));
        var thrust = MathF.Min(Airframe.StaticThrustCapLb * EnginePower, eta * Throttle * EnginePower * PowerHp(Altitude) * 550f / v)
                     * ThrustScale;

        Speed = MathF.Max(0f, Speed + G * ((thrust - drag) / Airframe.WeightLb - MathF.Sin(Gamma)) * Dt);
        Climb();

        // Arcade turning: tighter at low airspeed, softer as the wing runs out of lift.
        var controlEff = MathHelper.Clamp(nWing, 0.15f, 1f);
        var units = Speed * Mph / MphPerUnit;
        var turnSpeedFactor = 3f / MathF.Max(units, 0.9f);
        // A stalled wing can't deliver the commanded turn: it only turns as hard as the g it can still give.
        var turnScale = nTurnCmd > 1.001f ? (nTurn - 1f) / (nTurnCmd - 1f) : 1f;
        var noise = 0f; // random yaw from buffeting; feeds the slip ball
        if (controlEff < 0.5f) noise += ((float)rng.NextDouble() - 0.5f) * 0.025f * (1f - controlEff);
        if (stallDepth > 0f)
        {
            noise += ((float)rng.NextDouble() - 0.5f) * 0.05f * stallDepth;
            Bank += ((float)rng.NextDouble() - 0.5f) * 0.08f * stallDepth; // wing drops as it lets go
        }
        var turnPart = Bank * TurnCoeff * controlEff * turnSpeedFactor * turnScale * TurnAuthority;
        // Rudder: a flat swing of the nose, stronger with airflow over the tail.
        var rudderPart = Controls.Yaw * Airframe.RudderRate * controlEff * YawAuthority;
        Heading = MathHelper.WrapAngle(Heading + noise + turnPart + rudderPart);

        // Instrument feeds: turn rate (rad/s), and the slip ball. In this turn model bank and turn rate
        // always agree (a coordinated turn), so the ball only leaves centre when the wing stops delivering
        // the turn the bank asked for (turnScale < 1) or the airframe is being buffeted.
        YawRate = (noise + turnPart + rudderPart) * 60f;
        var bankSign = MathF.Sign(Bank) * MathF.Min(1f, MathF.Abs(Bank) / MaxBank);
        SlipBall = MathHelper.Clamp(bankSign * (1f - turnScale) * 1.5f + noise * 25f - Controls.Yaw * 0.7f, -1f, 1f);
    }
}
