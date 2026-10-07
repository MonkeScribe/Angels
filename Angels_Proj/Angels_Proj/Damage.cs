using System;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>The parts of an aircraft that take damage separately, each with its own hit box(es).</summary>
public enum Part { Engine, Canopy, LeftWing, RightWing, Tail, Fuselage }

/// <summary>How badly a part is damaged, from its hit points (0-100). Every part uses the same bands.</summary>
public enum PartState { Undamaged, Slight, Moderate, Critical, Black, Gone }

/// <summary>
/// Every number of the damage model in one place, to tweak.
///
/// How a hit works: a round passes through a part's hit box -> roll a random value in a range set by the round's
/// calibre and its speed at impact -> subtract the part's armour -> whatever is left over (if anything) comes off
/// the part's hit points. So a fast, heavy round gets through armour and does a lot; a slow or light one may do
/// nothing to an armoured part and still hurt a bare one (the canopy has no armour at all).
///
/// Integrity: how much of a part a weapon can actually destroy. Each part has an Integrity and each weapon a
/// Destructive rating (both 0-100). A weapon does full damage down to a floor of MaxHp x (Integrity - Destructive)%;
/// below that floor its damage drops off sharply and keeps shrinking as the part's hit points fall. So rifle-calibre
/// machine guns shred an engine or a canopy (low integrity, no floor) but only knock a fuselage or wing down about 75%
/// before they start to tell less and less; cannon (high Destructive) have no floor and finish the job.
/// </summary>
public static class DamageTuning
{
    // ---------------------------------------------------------------- weapons

    /// <summary>A gun's round. Power is the middle of its damage roll when it hits at RefSpeedFtS; it grows with
    /// impact speed to the power of VelocityExponent (2 = like kinetic energy).</summary>
    public sealed class Weapon
    {
        public string Name;
        public float CaliberIn;
        public float Power;
        public float RefSpeedFtS;
        public float Destructive;   // 0-100: set against each part's Integrity (see the class notes)
    }

    public static readonly Weapon Browning303 = new() { Name = ".303 BROWNING", CaliberIn = 0.303f, Power = 10f, RefSpeedFtS = 2440f, Destructive = 30f };
    // For later aircraft.
    public static readonly Weapon Hispano20 = new() { Name = "20MM HISPANO", CaliberIn = 0.787f, Power = 45f, RefSpeedFtS = 2880f, Destructive = 85f };
    public static readonly Weapon Mg17 = new() { Name = "7.92MM MG 17", CaliberIn = 0.312f, Power = 10.5f, RefSpeedFtS = 2600f, Destructive = 30f };
    public static readonly Weapon MgFf20 = new() { Name = "20MM MG FF", CaliberIn = 0.787f, Power = 38f, RefSpeedFtS = 1920f, Destructive = 75f };

    public static float VelocityExponent = 2f;

    /// <summary>The roll is uniform between Power x RollMin and Power x RollMax (after the speed scaling).</summary>
    public static float RollMin = 0.5f, RollMax = 1.5f;

    // ---------------------------------------------------------------- parts

    public const float MaxHp = 100f;

    // Each part's armour (subtracted from every roll that hits it), integrity (see the class notes) and damage
    // multiplier belong to the aircraft type: see Spitfire.

    /// <summary>Below the floor, damage is multiplied by a factor that slides from BelowFloorScale (at the floor) down to
    /// MinScale (at zero hit points), so a weak weapon keeps chipping away but ever more slowly.</summary>
    public static float BelowFloorScale = 0.35f, MinScale = 0.05f;

    /// <summary>The state bands, by colour: at MaxHp green (undamaged), above SlightAbove yellow (slight), above
    /// ModerateAbove orange (moderate), above CriticalAbove red (critical), above zero black, at zero gone.</summary>
    public static float SlightAbove = 75f, ModerateAbove = 50f, CriticalAbove = 25f;

    // ---------------------------------------------------------------- engine, fuel and fire

    // Engine power (thrust) is the engine's hit points as a fraction: 50 hp, half the thrust; no fuel, none. How much fuel
    // an aircraft carries and burns is its type's (Airframe).

    /// <summary>Each time the engine enters yellow and then orange it rolls this chance of springing a fuel leak.</summary>
    public static float LeakChance = 0.33f;
    public static float LeakGalPerMin = 3f;                    // per leak

    /// <summary>Entering red starts a fire for certain (and no more leak rolls). Its strength follows the engine: just into
    /// red is about 1%, 0 hp is 100%. While it burns it eats the engine at between these rates (hp per second, weakest to
    /// strongest fire), so a fire left alone grows. It only burns with the throttle up: hold the throttle below
    /// FireThrottleOff for FireOutSec and it goes out (a fresh hit on the engine lights it again). It also dies when the
    /// fuel runs out.</summary>
    public static float FireBurnMinHpPerSec = 0.25f, FireBurnMaxHpPerSec = 1.0f;
    public static float FireThrottleOff = 0.05f, FireOutSec = 3f;

    /// <summary>Black smoke from the engine starts below this many hit points and thickens to its heaviest at zero.</summary>
    public static float SmokeStartsBelowHp = 75f;

    /// <summary>A fire on any other part (it can only catch from a neighbouring part that is gone and burning, or from the
    /// engine blowing up) eats that part at between these rates, weakest to strongest fire; it doesn't go out. Its strength
    /// follows the part: FireMinStrength on a whole part, 100% on one at 0 hp.</summary>
    public static float PartFireBurnMinHpPerSec = 2f, PartFireBurnMaxHpPerSec = 6f, FireMinStrength = 0.2f;

    /// <summary>A part that is gone and burning sets each neighbour alight at this chance per second (see DamageModel.Neighbours).</summary>
    public static float FireSpreadChancePerSec = 0.35f;

    // ---------------------------------------------------------------- controls

    /// <summary>How much control a damaged wing or tail costs, as a fraction of its maximum rates: ControlLossAt75 with the
    /// part at 75 hp, ControlLossAt50 at 50, then straight up to all of it at 0 (see DamageModel.ControlLoss). The tail
    /// takes it off pitch, roll and rudder; each wing takes half of its own off roll and turn, there being two.</summary>
    public static float ControlLossAt75 = 0.05f, ControlLossAt50 = 0.10f;

    /// <summary>A black wing loses lift: from BlackWingLiftLoss just into black to all of it at 0 hp. The aircraft rolls
    /// toward the wing with less lift, up to WingRollBias of its steepest bank with nothing held against it, and goes on
    /// rolling slowly (WingRollDriftRadS) if the controls can't hold it.</summary>
    public static float BlackWingLiftLoss = 0.3f, WingRollBias = 0.6f, WingRollDriftRadS = 0.25f;

    /// <summary>With the tail gone nothing holds the nose up and it drops this fast; with the pilot dead it sinks this fast.</summary>
    public static float TailGoneNoseDropDegS = 4f, DeadPilotNoseDropDegS = 2.5f;

    // ---------------------------------------------------------------- hit boxes

    /// <summary>One box of a part, in the aircraft's own frame in feet: x toward the right wing, y up, z toward the tail
    /// (the nose is at -z). A part can have several boxes.</summary>
    public readonly struct HitBox
    {
        public readonly Vector3 Min, Max;
        public readonly Part Part;
        public HitBox(Vector3 min, Vector3 max, Part part) { Min = min; Max = max; Part = part; }
    }

    // Each aircraft type has its own hit boxes: see Spitfire.
}

/// <summary>The damage model's rules (the numbers are in DamageTuning).</summary>
public static class DamageModel
{
    public static readonly int PartCount = Enum.GetValues<Part>().Length;

    /// <summary>Which parts touch which, for fire spreading: the engine sits ahead of the cockpit and the wing roots, the
    /// fuselage joins everything, the tail hangs off the fuselage.</summary>
    private static readonly Part[][] Touching =
    {
        new[] { Part.Canopy, Part.Fuselage, Part.LeftWing, Part.RightWing },              // Engine
        new[] { Part.Engine, Part.Fuselage },                                             // Canopy
        new[] { Part.Engine, Part.Fuselage },                                             // LeftWing
        new[] { Part.Engine, Part.Fuselage },                                             // RightWing
        new[] { Part.Fuselage },                                                          // Tail
        new[] { Part.Engine, Part.Canopy, Part.LeftWing, Part.RightWing, Part.Tail },     // Fuselage
    };

    public static Part[] Neighbours(Part p) => Touching[(int)p];

    /// <summary>The share of its maximum control rates a part's damage costs, 0-1 (see DamageTuning.ControlLossAt75):
    /// little at first, then climbing straight to all of it below 50 hp.</summary>
    public static float ControlLoss(float hp)
    {
        hp = MathHelper.Clamp(hp, 0f, DamageTuning.MaxHp);
        float hi = DamageTuning.SlightAbove, mid = DamageTuning.ModerateAbove;
        if (hp >= hi) return DamageTuning.ControlLossAt75 * (DamageTuning.MaxHp - hp) / (DamageTuning.MaxHp - hi);
        if (hp >= mid) return MathHelper.Lerp(DamageTuning.ControlLossAt75, DamageTuning.ControlLossAt50, (hi - hp) / (hi - mid));
        return MathHelper.Lerp(DamageTuning.ControlLossAt50, 1f, (mid - hp) / mid);
    }

    /// <summary>The share of a wing's lift lost, 0-1: none until black, then from BlackWingLiftLoss to all of it at 0 hp.</summary>
    public static float LiftLoss(float hp)
    {
        if (hp <= 0f) return 1f;
        if (hp > DamageTuning.CriticalAbove) return 0f;
        return MathHelper.Lerp(DamageTuning.BlackWingLiftLoss, 1f, (DamageTuning.CriticalAbove - hp) / DamageTuning.CriticalAbove);
    }

    public static PartState StateOf(float hp) =>
        hp >= DamageTuning.MaxHp ? PartState.Undamaged
        : hp > DamageTuning.SlightAbove ? PartState.Slight
        : hp > DamageTuning.ModerateAbove ? PartState.Moderate
        : hp > DamageTuning.CriticalAbove ? PartState.Critical
        : hp > 0f ? PartState.Black
        : PartState.Gone;

    /// <summary>The damage roll for a round hitting at this speed (ft/s, relative to the target).</summary>
    public static float Roll(DamageTuning.Weapon w, float impactSpeedFtS, Random rng)
    {
        var power = w.Power * MathF.Pow(MathF.Max(impactSpeedFtS, 0f) / w.RefSpeedFtS, DamageTuning.VelocityExponent);
        var t = (float)rng.NextDouble();
        return power * (DamageTuning.RollMin + (DamageTuning.RollMax - DamageTuning.RollMin) * t);
    }

    /// <summary>The hit points a weapon can take a part down to at full effect (0 = it can destroy it outright).</summary>
    public static float Floor(AircraftPart part, DamageTuning.Weapon w) =>
        DamageTuning.MaxHp * Math.Clamp((part.Integrity - w.Destructive) / 100f, 0f, 1f);

    /// <summary>One round hits a part: roll, take off the armour, and take what's left off its hit points (see Apply).
    /// Returns the hit points lost (0 if the armour stopped it).</summary>
    public static float Hit(AircraftPart part, DamageTuning.Weapon w, float impactSpeedFtS, Random rng) =>
        Apply(part, w, Roll(w, impactSpeedFtS, rng) - part.Armor);

    /// <summary>Damage that got through a part's armour (left, already less the armour) comes off its hit points: at full
    /// effect down to the weapon's floor for that part and reduced below it. Returns the hit points lost.</summary>
    public static float Apply(AircraftPart part, DamageTuning.Weapon w, float left)
    {
        if (left <= 0f || part.Hp <= 0f) return 0f;
        var raw = left * part.DamageMultiplier;
        var hp = part.Hp;
        var floor = Floor(part, w);
        if (hp > floor)
        {
            // Full effect down to the floor; whatever would go past it carries on at the reduced rate.
            var full = MathF.Min(raw, hp - floor);
            hp -= full;
            raw -= full;
        }
        if (raw > 0f && floor > 0f)
        {
            var k = DamageTuning.MinScale + (DamageTuning.BelowFloorScale - DamageTuning.MinScale) * MathHelper.Clamp(hp / floor, 0f, 1f);
            hp -= raw * k;
        }
        else hp -= raw;
        hp = MathF.Max(0f, hp);
        var loss = part.Hp - hp;
        part.Hp = hp;
        return loss;
    }

    /// <summary>Colour for a state: green, yellow, orange, red, black, and pale grey for gone.</summary>
    public static Color StateColor(PartState s) => s switch
    {
        PartState.Undamaged => new Color(90, 230, 110),
        PartState.Slight => new Color(250, 225, 60),
        PartState.Moderate => new Color(255, 140, 40),
        PartState.Critical => new Color(235, 45, 40),
        PartState.Black => new Color(16, 16, 16),
        _ => new Color(150, 150, 150),
    };

    public static string Name(Part p) => p switch
    {
        Part.Engine => "ENGINE",
        Part.Canopy => "CANOPY",
        Part.LeftWing => "L WING",
        Part.RightWing => "R WING",
        Part.Tail => "TAIL",
        _ => "FUSELAGE",
    };

    public static string Name(PartState s) => s switch
    {
        PartState.Undamaged => "GREEN",
        PartState.Slight => "YELLOW",
        PartState.Moderate => "ORANGE",
        PartState.Critical => "RED",
        PartState.Black => "BLACK",
        _ => "GONE",
    };
}
