using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// The Heinkel He 111 H: a twin-engined medium bomber, drawn from its own sheet. Its two engines each take half of its
/// power (lose one and it has half the thrust left), its two wings go the way a fighter's do, and its hit boxes are
/// measured off the sheet's top view (3.2 px a foot: the 74 ft span is 238 px). A stub in other ways: it carries two
/// .303s in the nose as a placeholder (its defensive guns aren't modelled yet).
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
        new Gun(GunSpec.Browning303, new Vector3(-1.2f, 0.5f, 25.5f), ConvergeFt),
        new Gun(GunSpec.Browning303, new Vector3(1.2f, 0.5f, 25.5f), ConvergeFt),
    };

    // ---- Look ----

    /// <summary>Drawn from the He 111 sheet. Two three-bladed propellers on the nacelles, 8.6 ft either side of the
    /// fuselage and level with the nose; each engine's fire shows a little behind its hub. Positions are feet from the
    /// middle of a frame (x toward the nose, y toward the left wing, z up), listed left to right like the engines: the left
    /// engine is at y +8.6. The prop radius is what the sheet's own prop discs measure (4.3 ft).</summary>
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
            new PropellerSpec { Hub = new Vector3(24.5f, 8.6f, -1f), Blades = 3, RadiusFt = 4.3f, SpinnerFt = 0.8f, Turn = 1f },
            new PropellerSpec { Hub = new Vector3(24.5f, -8.6f, -1f), Blades = 3, RadiusFt = 4.3f, SpinnerFt = 0.8f, Turn = 1f },
        },
        FirePoints = new[] { new Vector3(18f, 8.6f, -1f), new Vector3(18f, -8.6f, -1f) },
    };

    // ---- Parts, by kind in Part order: Engine, Canopy, Wing, Tail, Fuselage (one entry each, shared by every part of the kind) ----

    /// <summary>Subtracted from every roll that hits the part.</summary>
    public static readonly float[] Armor = { 4f, 0f, 1f, 0.5f, 2f };

    /// <summary>How hard each part is to destroy outright, 0-100 (see DamageTuning). A bomber is tougher than a fighter.</summary>
    public static readonly float[] Integrity = { 40f, 10f, 100f, 90f, 100f };

    /// <summary>What's left after armour is multiplied by this before it comes off the part's hit points (1 = as is).</summary>
    public static readonly float[] DamageMultiplier = { 1f, 1f, 1f, 1f, 1f };

    /// <summary>Hit boxes in the aircraft's frame, feet: x toward the right wing, y up, z toward the tail (the nose is at
    /// -z); the origin is the middle of its sheet frame: 26 ft from the nose and from the tail. Two engines (index 0 left,
    /// 1 right), two wings, the glazed nose, the fuselage in three boxes as it narrows, and the tailplane and fin.</summary>
    public static readonly DamageTuning.HitBox[] HitBoxes =
    {
        // Engines: the Jumo 211s in their nacelles, 6.4 to 10.8 ft out, from behind the prop back under the wing.
        new(new Vector3(-10.8f, -2.2f, -24.8f), new Vector3(-6.4f, 2.0f, -10f), Part.Engine, 0),
        new(new Vector3(6.4f, -2.2f, -24.8f), new Vector3(10.8f, 2.0f, -10f), Part.Engine, 1),
        // The glazed nose and cockpit, forward of the wing.
        new(new Vector3(-2.4f, -1.8f, -26f), new Vector3(2.4f, 4f, -13f), Part.Canopy),
        // Fuselage: the cabin and bomb bay at the wing root, then the narrowing tail boom, then the cone under the tailplane.
        new(new Vector3(-3.1f, -3.2f, -13f), new Vector3(3.1f, 3.3f, 3f), Part.Fuselage),
        new(new Vector3(-1.9f, -2f, 3f), new Vector3(1.9f, 2.5f, 16.8f), Part.Fuselage),
        new(new Vector3(-1f, -1.2f, 16.8f), new Vector3(1f, 1.6f, 25f), Part.Fuselage),
        // Wings: from the fuselage out to 36.8 ft, trailing edge straight at -3.7 ft, leading edge from -18.3 (root) to
        // -14 (tip); one box each for the tapering plan.
        new(new Vector3(-36.8f, -1.2f, -18f), new Vector3(-3.1f, 1.2f, -3.7f), Part.Wing, 0),
        new(new Vector3(3.1f, -1.2f, -18f), new Vector3(36.8f, 1.2f, -3.7f), Part.Wing, 1),
        // Tail: the elliptical tailplane (12.9 ft each side, 16.8 to 26 ft aft) and the fin and rudder above it.
        new(new Vector3(-12.9f, -0.3f, 16.8f), new Vector3(12.9f, 0.9f, 26f), Part.Tail),
        new(new Vector3(-0.6f, 0.9f, 17f), new Vector3(0.6f, 6.5f, 26f), Part.Tail),
    };
}
