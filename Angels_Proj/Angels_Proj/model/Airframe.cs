using System;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// One gear of an engine's supercharger. Below its critical altitude the engine is held to its boost limit, so power
/// barely changes (it climbs a little from SeaLevelHp to RatedHp as the thinner air lets the exhaust out); above it the
/// supercharger is flat out and power falls away with the air density. A single-speed engine has one stage, a two-speed
/// or two-stage one several, and the engine gives whichever is best at the altitude it is at (the gear change).
/// </summary>
public readonly record struct SuperchargerStage(float SeaLevelHp, float RatedHp, float CriticalAltFt);

/// <summary>
/// What an aircraft type is made of and can stand, for the flight model to fly. Each aircraft carries one
/// (Aircraft.Airframe) and its flight model works everything out from it, so a different airframe flies differently on
/// the same inputs. It has no values of its own: each aircraft type fills one in (see model/planes).
///
/// The six things that decide who wins a fight, and what sets each of them:
///   rate of climb          - engine power at height (Stages) over weight, less the drag of climbing (CD0, wing)
///   energy lost in a turn  - the induced drag of the g being pulled: wing loading (WeightLb, WingArea), AspectRatio,
///                            OswaldE, and how soon the wing stalls (CLmax) and sheds more (StallDragCD)
///   level acceleration     - thrust minus drag over weight: engine, propeller (PropEff, StaticThrustCapLb), CD0
///   engine at altitude     - the supercharger stages and how fast power falls above them (AltitudeFallOff)
///   roll rate              - RollRateDegS at its best speed, and how it fades as the stick stiffens (RollPeakMph,
///                            RollFadePer100Mph)
///   top speed, level       - power against drag: the same as acceleration, run until they balance
/// </summary>
public sealed class Airframe
{
    public string Name;

    // ---- Airframe ----
    public float WeightLb, WingArea, Span;          // lb, sq ft, ft
    public float CD0, OswaldE, CLmax;               // zero-lift drag, span efficiency, max lift coefficient
    public float PropEff, StaticThrustCapLb;        // propeller efficiency; the most thrust it gives at low speed
    public float IdlePropDragCD;                    // a throttled-back propeller's drag

    // ---- Engine ----
    /// <summary>The supercharger gears, best power of each (see SuperchargerStage).</summary>
    public SuperchargerStage[] Stages;
    /// <summary>How fast power falls above a stage's critical altitude: power follows air density to this power (1 =
    /// directly with it; a little under 1 for engines that gain from the thinner air's lower back pressure).</summary>
    public float AltitudeFallOff = 1f;
    public float FuelCapacityGal;                   // imperial gallons
    /// <summary>How fast the engine drinks (imperial gallons an hour) against throttle (0-1), from idle to full; between
    /// two points it is straight. Real engines run lean at cruise and rich at full power, so it isn't a straight line.</summary>
    public (float throttle, float galPerHour)[] FuelBurnTable;
    /// <summary>The parts its fuel tanks are in: a hit that takes one of them into a worse damage band may hole a tank.</summary>
    public Part[] FuelTankParts;

    public float AspectRatio => Span * Span / WingArea;
    public float WingLoading => WeightLb / WingArea;

    /// <summary>Fuel burnt at this throttle, imperial gallons an hour (see FuelBurnTable).</summary>
    public float FuelBurnGalPerHour(float throttle)
    {
        var t = FuelBurnTable;
        if (throttle <= t[0].throttle) return t[0].galPerHour;
        for (var i = 1; i < t.Length; i++)
            if (throttle <= t[i].throttle)
                return MathHelper.Lerp(t[i - 1].galPerHour, t[i].galPerHour, (throttle - t[i - 1].throttle) / (t[i].throttle - t[i - 1].throttle));
        return t[^1].galPerHour;
    }

    public bool IsFuelTank(Part p) => FuelTankParts != null && Array.IndexOf(FuelTankParts, p) >= 0;

    // ---- Limits ----
    public float VneMph;                            // never-exceed, indicated; drag climbs above it (OverspeedDragK)
    public float WaveDragK = 3f;                    // compressibility drag above Mach 0.75
    public float OverspeedDragK = 0.3f;             // structural-limit drag above Vne
    public float MaxNPos, MaxNNeg;                  // pitch-axis g limits, up and down
    public float NTurnMax, NStruct;                 // g at full bank at reference speed; structural limit
    public float QRef;                              // dynamic pressure (psf) the turn g is referenced to
    public float StallDragCD;                       // separated flow when the wing is over-pulled
    public float StallBleedPerSec = 0.5f;           // share of its speed an accelerated stall sheds each second, at full depth
    public float RudderRate;                        // rad per tick of flat yaw at full rudder
    public float RudderSlipDragCD;                  // extra drag at full rudder
    public float StallNoseDropDegS;                 // how fast a fully stalled nose falls toward the ground
    public float MaxClimbDeg, MaxDiveDeg;           // steepest flight path the pilot can command
    public float CeilingFt;

    // ---- Roll ----
    public float RollResponse;                      // how quickly it rolls to the bank asked for: fraction of the gap per tick
    /// <summary>Its best roll rate, deg/s, at indicated speed RollPeakMph. Below that the ailerons bite less, in proportion
    /// to the airflow; above it the stick stiffens and the rate fades by RollFadePer100Mph of the best for every 100 mph.</summary>
    public float RollRateDegS = 120f, RollPeakMph = 280f, RollFadePer100Mph = 0.4f;
    private const float RollFloor = 0.2f;           // the least share of its best rate it rolls at, however slow or fast

    /// <summary>Engine power (hp) at full throttle at this altitude (ft): the best supercharger stage there.</summary>
    public float PowerHp(float altFt)
    {
        FlightModel.Atmosphere(altFt, out var rho, out _);
        var best = 0f;
        foreach (var s in Stages)
        {
            float hp;
            if (altFt <= s.CriticalAltFt) hp = MathHelper.Lerp(s.SeaLevelHp, s.RatedHp, altFt / s.CriticalAltFt);
            else
            {
                FlightModel.Atmosphere(s.CriticalAltFt, out var rhoCrit, out _);
                hp = s.RatedHp * MathF.Pow(rho / rhoCrit, AltitudeFallOff);
            }
            best = MathF.Max(best, hp);
        }
        return best;
    }

    /// <summary>The fastest it rolls (rad/s) at this indicated airspeed, with the ailerons whole.</summary>
    public float RollRateRadS(float iasMph)
    {
        var share = iasMph <= RollPeakMph
            ? iasMph / RollPeakMph
            : 1f - RollFadePer100Mph * (iasMph - RollPeakMph) / 100f;
        return MathHelper.ToRadians(RollRateDegS) * MathF.Max(RollFloor, share);
    }
}
