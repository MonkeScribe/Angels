using System;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// Point-mass flight model for a Supermarine Spitfire Mk IX, in real units (feet, slugs, lb, ft/s),
/// stepped at a fixed 60 ticks/s. Performance is not scripted: top speed, climb rate and dive speed all
/// fall out of thrust, drag, lift and air density, calibrated against the real aircraft:
///   level speed ~403 mph TAS at ~30,000 ft, ~4,100 ft/min climb at sea level falling to 0 at 51,550 ft,
///   stall ~86 mph, never-exceed 450 mph IAS, dive to ~Mach 0.87 from altitude.
/// Horizontal steering (bank/heading) keeps Skyward's arcade turn model.
/// </summary>
public sealed class FlightModel
{
    // Airframe.
    private const float WeightLb = 7400f, WingArea = 242f, Span = 36.83f;
    private const float CD0 = 0.0195f, OswaldE = 0.55f, CLmax = 1.6f;
    private const float PropEff = 0.82f, StaticThrustCapLb = 3500f;
    private const float IdlePropDragCD = 0.006f;
    private const float G = 32.174f;

    // Limits.
    public const float VneMph = 450f;              // never-exceed, indicated
    private const float WaveDragK = 3f;            // compressibility drag above Mach 0.75
    private const float OverspeedDragK = 0.3f;     // structural-limit drag above Vne
    private const float MaxNPos = 4.0f;                    // pitch-axis g limit (either direction: a dive is a pull through the inverted side)
    private const float NTurnMax = 7f, NStruct = 12f;          // g at full bank at reference speed; structural limit
    private const float QRef = 160f;                          // dynamic pressure (psf) of ~250 mph at sea level
    private const float StallDragCD = 0.12f;
    public const float MaxClimbDeg = 60f, MaxDiveDeg = 90f;
    private const float PitchDetentDeg = 10f;
    public const float CeilingFt = 51550f;

    // Arcade steering (ported from Skyward's Spitfire).
    public const float MaxBank = 0.66f;
    private const float TurnCoeff = 0.058f;
    private const float MphPerUnit = 68f;          // Skyward's speed unit, used by the turn-rate curve

    private const float Mph = 0.681818f;           // ft/s -> mph
    private const float Rho0 = 0.0023769f;
    private const float Dt = 1f / 60f;

    // Engine power (hp) at full throttle vs altitude (ft). Chosen so the climb-rate table matches the
    // real aircraft; see the notes above. Beyond the table power falls away quickly.
    private static readonly (float alt, float hp)[] PowerTable =
    {
        (0, 1491), (5000, 1477), (10000, 1440), (15000, 1408), (20000, 1353), (25000, 1290),
        (28000, 1231), (30000, 1180), (35000, 1036), (40000, 843), (43000, 767), (47000, 780),
        (51550, 840), (56000, 600), (65000, 150),
    };

    private static readonly float AspectRatio = Span * Span / WingArea;

    // State.
    public float Altitude = 5000f;                 // ft
    public float Speed;                            // true airspeed, ft/s
    public float Gamma;                            // flight path angle, rad (+ = climbing)
    public float PitchCmdDeg;                      // commanded flight-path angle, deg; settles to the nearest 10 on release
    public float Throttle = 0.55f;
    private bool _pitchHeld;
    public float Bank, Heading;
    public float VerticalRateScale = 1f;           // 1 = realistic; 2 = arcade (altitude changes twice as fast)

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
    public bool Overspeed => IasMph > VneMph;

    public FlightModel()
    {
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

    private static float PowerHp(float alt)
    {
        if (alt <= PowerTable[0].alt) return PowerTable[0].hp;
        for (var i = 1; i < PowerTable.Length; i++)
            if (alt <= PowerTable[i].alt)
            {
                var (a0, p0) = PowerTable[i - 1];
                var (a1, p1) = PowerTable[i];
                return MathHelper.Lerp(p0, p1, (alt - a0) / (a1 - a0));
            }
        return 0f;
    }

    /// <summary>Nearest 10-degree detent, within the climb/dive limits (6 -> 10, 3 -> 0, 16 -> 20), judged from the nose's actual angle.</summary>
    public static float SnapPitch(float deg) =>
        MathHelper.Clamp(MathF.Round(deg / PitchDetentDeg, MidpointRounding.AwayFromZero) * PitchDetentDeg,
            -MathF.Floor(MaxDiveDeg / PitchDetentDeg) * PitchDetentDeg, MathF.Floor(MaxClimbDeg / PitchDetentDeg) * PitchDetentDeg);

    public float StallSpeed(float n) =>
        MathF.Sqrt(2f * n * WeightLb / (Rho * WingArea * CLmax));

    /// <param name="pitchKey">-1 nose down, +1 nose up, 0 released.</param>
    /// <param name="throttleKey">+1 more, -1 less.</param>
    /// <param name="targetBank">Desired bank (arcade units), within +/-MaxBank.</param>
    public void Step(float pitchKey, float throttleKey, float targetBank, float bankResponse, Random rng)
    {
        Throttle = MathHelper.Clamp(Throttle + throttleKey * 0.012f, 0f, 1f);
        if (pitchKey != 0f)
        {
            // While a key is held the command runs ahead of the nose.
            PitchCmdDeg = pitchKey > 0 ? MathF.Min(MaxClimbDeg, PitchCmdDeg + 3f) : MathF.Max(-MaxDiveDeg, PitchCmdDeg - 3f);
            _pitchHeld = true;
        }
        else if (_pitchHeld)
        {
            // On release, settle at the detent nearest where the nose actually is, not where the command got to.
            PitchCmdDeg = SnapPitch(MathHelper.ToDegrees(Gamma));
            _pitchHeld = false;
        }
        Bank += (targetBank - Bank) * bankResponse;

        Atmosphere(Altitude, out Rho, out SoundSpeed);
        var v = MathF.Max(Speed, 20f);
        var q = 0.5f * Rho * v * v;
        var mach = v / SoundSpeed;

        // Wing capability: the most load factor (g) the wing can generate right now. Above about Mach 0.45 shock
        // stall (buffet) starts eating into maximum lift, so hard pulls at high speed stall the wing early.
        var clMaxEff = CLmax * (1f - MathHelper.Clamp((mach - 0.45f) / 0.2f, 0f, 0.6f));
        var nWing = clMaxEff * q * WingArea / WeightLb;

        // Turn load. A hard mouse turn (full bank) pulls g, and elevator authority grows with dynamic
        // pressure, so the same deflection pulls more g the faster you are. The wing shares its g between
        // the turn and any pitch change; if the turn asks for more than it has, the wing stalls.
        var bankFrac = MathF.Min(1f, MathF.Abs(Bank) / MaxBank);
        var nTurnCmd = MathF.Min(NStruct, 1f + (NTurnMax - 1f) * bankFrac * MathHelper.Clamp(q / QRef, 0.35f, 2.8f));
        var nTurn = MathF.Min(nTurnCmd, MathF.Max(1f, nWing));
        var stallDepth = MathHelper.Clamp((nTurnCmd - nWing) / nTurnCmd, 0f, 1f);
        AccelStall = nTurnCmd > nWing && bankFrac > 0.05f;
        var nAvail = MathF.Min(MaxNPos, MathF.Max(0f, nWing - (nTurnCmd - 1f)));

        // Pitch stick commands a flight-path angle; the wing's load factor decides how fast we get there.
        // High Mach stiffens the controls (compressibility); a low-energy wing can't pull at all.
        var gammaTarget = MathHelper.ToRadians(PitchCmdDeg);
        var rateWanted = MathHelper.Clamp((gammaTarget - Gamma) * 3f, -0.9f, 0.9f);
        rateWanted *= MathHelper.Clamp(1f - (mach - 0.8f) / 0.1f, 0.2f, 1f);
        var nReq = MathF.Cos(Gamma) + rateWanted * v / G;
        var n = MathHelper.Clamp(nReq, -MathF.Min(MaxNPos, nWing), nAvail);
        Gamma += G * (n - MathF.Cos(Gamma)) / v * Dt;
        Gamma = MathHelper.Clamp(Gamma, -MathHelper.PiOver2 * 0.995f, MathHelper.PiOver2 * 0.995f);
        LoadFactor = n + nTurn - 1f;

        // Drag: parasitic + induced (load factor, increased by banking) + compressibility + overspeed + idle prop.
        var nDrag = MathF.Max(0.3f, MathF.Abs(n) + nTurn - 1f);
        var cl = nDrag * WeightLb / (q * WingArea);
        var cd = CD0 + cl * cl / (MathF.PI * OswaldE * AspectRatio) + IdlePropDragCD * (1f - Throttle)
                 + StallDragCD * stallDepth; // separated flow when the wing is over-pulled
        if (mach > 0.75f) cd += WaveDragK * (mach - 0.75f) * (mach - 0.75f);
        var ias = v * MathF.Sqrt(Rho / Rho0) * Mph;
        if (ias > VneMph) { var o = (ias - VneMph) / 50f; cd += OverspeedDragK * o * o; }
        var drag = cd * q * WingArea;

        // Propeller thrust: power / speed, with efficiency falling off at low speed.
        var eta = PropEff * (1f - MathF.Exp(-v / 60f));
        var thrust = MathF.Min(StaticThrustCapLb, eta * Throttle * PowerHp(Altitude) * 550f / v);

        Speed = MathF.Max(0f, Speed + G * ((thrust - drag) / WeightLb - MathF.Sin(Gamma)) * Dt);
        Altitude += Speed * MathF.Sin(Gamma) * Dt * VerticalRateScale;
        if (Altitude <= 0f)
        {
            // Report the touchdown; the game decides whether it was a crash landing or a wreck.
            GroundHit = true;
            ImpactSinkFpm = MathF.Max(0f, -VerticalSpeedFpm);
            ImpactSpeedMph = Speed * Mph;
            ImpactAngleDeg = MathHelper.ToDegrees(-Gamma);
            Altitude = 0f;
            Gamma = MathF.Max(Gamma, 0f);
        }

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
        var turnPart = Bank * TurnCoeff * controlEff * turnSpeedFactor * turnScale;
        Heading = MathHelper.WrapAngle(Heading + noise + turnPart);

        // Instrument feeds: turn rate (rad/s), and the slip ball. In this turn model bank and turn rate
        // always agree (a coordinated turn), so the ball only leaves centre when the wing stops delivering
        // the turn the bank asked for (turnScale < 1) or the airframe is being buffeted.
        YawRate = (noise + turnPart) * 60f;
        var bankSign = MathF.Sign(Bank) * MathF.Min(1f, MathF.Abs(Bank) / MaxBank);
        SlipBall = MathHelper.Clamp(bankSign * (1f - turnScale) * 1.5f + noise * 25f, -1f, 1f);
    }
}
