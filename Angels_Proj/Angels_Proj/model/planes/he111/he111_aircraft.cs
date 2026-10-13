using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// The Heinkel He 111 H: a twin-engined bomber, drawn from its own sheet. A stub: its hit boxes are laid out roughly on
/// its 54 ft length and 74 ft span, and it carries two .303s in the nose as a placeholder (its defensive guns aren't
/// modelled yet).
/// </summary>
public sealed class He111 : Aircraft
{
    public He111(Pilot pilot)
        : base(He111Airframe.Make(), HitBoxes, Armor, Integrity, DamageMultiplier, Look, MakeGuns(), pilot) { }

    // ---- Guns ----

    private const float ConvergeFt = 750f;

    /// <summary>Placeholder: two .303 Brownings either side of the nose glazing, in the aircraft frame (feet: x along the
    /// right wing, y up, z forward).</summary>
    public static Gun[] MakeGuns() => new[]
    {
        new Gun(GunSpec.Browning303, new Vector3(-1.5f, -0.5f, 25f), ConvergeFt),
        new Gun(GunSpec.Browning303, new Vector3(1.5f, -0.5f, 25f), ConvergeFt),
    };

    // ---- Look ----

    /// <summary>Drawn from the He 111 sheet. Two three-bladed propellers, 11 ft 6 in across, on the wing nacelles; each
    /// engine's fire shows a little behind its hub. Positions are feet from the middle of a frame (x toward the nose, y
    /// toward the left wing, z up). Estimated, not yet checked against the sheet.</summary>
    public static readonly AircraftLook Look = new()
    {
        Sheet = new SpriteSheetSpec
        {
            Path = "Content/Sprites/aircraft/he111/sheet.png",
            SpanPx = 238f, SpanFt = 74.2f, AzZeroDeg = 0f,
            MuzzleShift = new Vector3(0f, 0f, 0f),
        },
        Propellers = new[]
        {
            new PropellerSpec { Hub = new Vector3(12f, 14.4f, -0.5f), Blades = 3, RadiusFt = 5.75f, SpinnerFt = 1.0f, Turn = 1f },
            new PropellerSpec { Hub = new Vector3(12f, -14.4f, -0.5f), Blades = 3, RadiusFt = 5.75f, SpinnerFt = 1.0f, Turn = 1f },
        },
        FirePoints = new[] { new Vector3(8.5f, 14.4f, -0.5f), new Vector3(8.5f, -14.4f, -0.5f) },
    };

    // ---- Parts, in Part order: Engine, Canopy, LeftWing, RightWing, Tail, Fuselage ----

    /// <summary>Subtracted from every roll that hits the part.</summary>
    public static readonly float[] Armor = { 4f, 0f, 1f, 1f, 0.5f, 2f };

    /// <summary>How hard each part is to destroy outright, 0-100 (see DamageTuning). A bomber is tougher than a fighter.</summary>
    public static readonly float[] Integrity = { 40f, 10f, 100f, 100f, 90f, 100f };

    /// <summary>What's left after armour is multiplied by this before it comes off the part's hit points (1 = as is).</summary>
    public static readonly float[] DamageMultiplier = { 1f, 1f, 1f, 1f, 1f, 1f };

    /// <summary>Hit boxes in the aircraft's frame, feet: x toward the right wing, y up, z toward the tail (the nose is at
    /// -z); the origin is about at the middle of the fuselage.</summary>
    public static readonly DamageTuning.HitBox[] HitBoxes =
    {
        new(new Vector3(-16.6f, -2f, -16f), new Vector3(-12.2f, 2f, -2f), Part.Engine),            // left nacelle
        new(new Vector3(12.2f, -2f, -16f), new Vector3(16.6f, 2f, -2f), Part.Engine),              // right nacelle
        new(new Vector3(-2.2f, 0.5f, -26f), new Vector3(2.2f, 4.5f, -14f), Part.Canopy),           // glazed nose
        new(new Vector3(-2.5f, -3f, -14f), new Vector3(2.5f, 3.5f, 22f), Part.Fuselage),           // nose to tail root
        new(new Vector3(-1.2f, -1.2f, 22f), new Vector3(1.2f, 1.8f, 28f), Part.Fuselage),          // tail cone
        new(new Vector3(-37f, -1.2f, -8f), new Vector3(-2.5f, 1.2f, 6f), Part.LeftWing),
        new(new Vector3(2.5f, -1.2f, -8f), new Vector3(37f, 1.2f, 6f), Part.RightWing),
        new(new Vector3(-11f, -0.3f, 22f), new Vector3(11f, 0.7f, 28f), Part.Tail),                // tailplane and elevators
        new(new Vector3(-0.5f, 0.7f, 22f), new Vector3(0.5f, 9f, 28f), Part.Tail),                 // fin and rudder
    };
}
