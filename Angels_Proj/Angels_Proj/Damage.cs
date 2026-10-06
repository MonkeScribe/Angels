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

    /// <summary>The state bands: at MaxHp undamaged, above SlightAbove slight, above ModerateAbove moderate, above
    /// CriticalAbove critical, above zero black, at zero gone.</summary>
    public static float SlightAbove = 75f, ModerateAbove = 50f, CriticalAbove = 25f;

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

    /// <summary>A fresh aircraft: every part at full hit points.</summary>
    public static float[] NewParts()
    {
        var p = new float[PartCount];
        Array.Fill(p, DamageTuning.MaxHp);
        return p;
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
    public static float Floor(Part part, DamageTuning.Weapon w) =>
        DamageTuning.MaxHp * Math.Clamp((DamageTuning.Integrity[(int)part] - w.Destructive) / 100f, 0f, 1f);

    /// <summary>One round hits a part: roll, take off the armour, and take what's left off its hit points, at full
    /// effect down to the weapon's floor for that part and reduced below it. Returns the hit points lost (0 if the
    /// armour stopped it).</summary>
    public static float Hit(float[] parts, Part part, DamageTuning.Weapon w, float impactSpeedFtS, Random rng)
    {
        var i = (int)part;
        var left = Roll(w, impactSpeedFtS, rng) - DamageTuning.Armor[i];
        if (left <= 0f || parts[i] <= 0f) return 0f;
        var raw = left * DamageTuning.DamageMultiplier[i];
        var hp = parts[i];
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
        var loss = parts[i] - hp;
        parts[i] = hp;
        return loss;
    }

    /// <summary>Placeholder until each state has its own effects: the aircraft goes down when the engine, the fuselage,
    /// the canopy (the pilot), the tail or either wing is gone.</summary>
    public static bool Downed(float[] parts)
    {
        foreach (var hp in parts)
            if (hp <= 0f) return true;
        return false;
    }

    /// <summary>Colour for a state, for the debug views: green, yellow-green, yellow, orange, red, dark grey.</summary>
    public static Color StateColor(PartState s) => s switch
    {
        PartState.Undamaged => new Color(90, 230, 110),
        PartState.Slight => new Color(190, 235, 80),
        PartState.Moderate => new Color(250, 220, 60),
        PartState.Critical => new Color(255, 140, 40),
        PartState.Black => new Color(235, 45, 40),
        _ => new Color(70, 70, 70),
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
        PartState.Undamaged => "OK",
        PartState.Slight => "SLIGHT",
        PartState.Moderate => "MODERATE",
        PartState.Critical => "CRITICAL",
        PartState.Black => "BLACK",
        _ => "GONE",
    };
}
