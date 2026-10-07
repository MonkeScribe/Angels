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

    /// <summary>Subtracted from every roll that hits the part. Order: Engine, Canopy, LeftWing, RightWing, Tail, Fuselage.</summary>
    public static readonly float[] Armor = { 4f, 0f, 1f, 1f, 0.5f, 2f };

    /// <summary>What's left after armour is multiplied by this before it comes off the part's hit points (1 = as is).
    /// Same order as Armor.</summary>
    public static readonly float[] DamageMultiplier = { 1f, 1f, 1f, 1f, 1f, 1f };

    /// <summary>How hard each part is to destroy outright, 0-100 (see the class notes). Same order as Armor. With the .303's
    /// Destructive of 30: engine and canopy have no floor, the tail's is 10 hp, wings and fuselage 25 hp.</summary>
    public static readonly float[] Integrity = { 30f, 10f, 55f, 55f, 40f, 55f };

    /// <summary>Below the floor, damage is multiplied by a factor that slides from BelowFloorScale (at the floor) down to
    /// MinScale (at zero hit points), so a weak weapon keeps chipping away but ever more slowly.</summary>
    public static float BelowFloorScale = 0.35f, MinScale = 0.05f;

    /// <summary>The state bands, by colour: at MaxHp green (undamaged), above SlightAbove yellow (slight), above
    /// ModerateAbove orange (moderate), above CriticalAbove red (critical), above zero black, at zero gone.</summary>
    public static float SlightAbove = 75f, ModerateAbove = 50f, CriticalAbove = 25f;

    // ---------------------------------------------------------------- engine, fuel and fire

    /// <summary>Engine power (thrust) is the engine's hit points as a fraction: 50 hp, half the thrust. No fuel, none.</summary>
    public static float FuelCapacityGal = 85f;                 // Spitfire Mk I: 48 + 37 imperial gallons
    public static float FuelBurnIdleGalPerMin = 0.3f, FuelBurnFullGalPerMin = 1.5f;   // at idle and full throttle

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

    // ---------------------------------------------------------------- hit boxes

    /// <summary>One box of a part, in the aircraft's own frame in feet: x toward the right wing, y up, z toward the tail
    /// (the nose is at -z). A part can have several boxes.</summary>
    public readonly struct HitBox
    {
        public readonly Vector3 Min, Max;
        public readonly Part Part;
        public HitBox(Vector3 min, Vector3 max, Part part) { Min = min; Max = max; Part = part; }
    }

    /// <summary>A single-engined fighter laid out like a Spitfire: 30 ft long, 37 ft span, origin about at the wing root.</summary>
    public static readonly HitBox[] Fighter =
    {
        new(new Vector3(-2.6f, -2.4f, -16f), new Vector3(2.6f, 2.4f, -6.5f), Part.Engine),        // nose to firewall
        new(new Vector3(-1.3f, 1.8f, -2f), new Vector3(1.3f, 4.2f, 3f), Part.Canopy),             // cockpit glazing
        new(new Vector3(-2.5f, -2.6f, -6.5f), new Vector3(2.5f, 2.2f, 12.8f), Part.Fuselage),     // firewall to tail
        new(new Vector3(-1.2f, -1.4f, 12.8f), new Vector3(1.2f, 1.4f, 16.8f), Part.Fuselage),     // tail cone
        new(new Vector3(-18.6f, -1f, -4.2f), new Vector3(-2.5f, 1f, 4.7f), Part.LeftWing),
        new(new Vector3(2.5f, -1f, -4.2f), new Vector3(18.6f, 1f, 4.7f), Part.RightWing),
        new(new Vector3(-5.4f, -0.3f, 13f), new Vector3(5.4f, 0.7f, 16.8f), Part.Tail),          // tailplane and elevators
        new(new Vector3(-0.5f, 0.7f, 12.8f), new Vector3(0.5f, 7f, 16.8f), Part.Tail),           // fin and rudder
    };
}

/// <summary>The damage model's rules (the numbers are in DamageTuning).</summary>
public static class DamageModel
{
    public static readonly int PartCount = Enum.GetValues<Part>().Length;

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

    /// <summary>One round hits a part: roll, take off the armour, and take what's left off its hit points, at full
    /// effect down to the weapon's floor for that part and reduced below it. Returns the hit points lost (0 if the
    /// armour stopped it).</summary>
    public static float Hit(AircraftPart part, DamageTuning.Weapon w, float impactSpeedFtS, Random rng)
    {
        var left = Roll(w, impactSpeedFtS, rng) - part.Armor;
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
