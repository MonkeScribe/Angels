using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// The Supermarine Spitfire Mk II: an Aircraft with everything that makes it one. Its airframe and limits are in
/// SpitfireMk2Airframe; its hit boxes are laid out on its 30 ft length and 37 ft span, and each part has its armour,
/// integrity and damage multiplier. Tweak a Spitfire here.
/// </summary>
public sealed class SpitfireMk2 : Aircraft
{
    public SpitfireMk2(Pilot pilot)
        : base(SpitfireMk2Airframe.Make(), HitBoxes, Armor, Integrity, DamageMultiplier, Look, MakeGuns(), pilot) { }

    // ---- Guns ----

    /// <summary>Where each gun's round leaves the airframe, as a pixel of the level sprite (256 px, nose up): the tips of the
    /// barrel stubs on its wings. There are four stubs, two a wing, and each serves two of the eight guns (guns 0-3 are the
    /// left wing, 4-7 the right).</summary>
    private static readonly Vector2[] MuzzleSpritePx =
    {
        new(85.5f, 89f), new(85.5f, 89f), new(89.5f, 79f), new(89.5f, 79f),     // left wing: outer stub, then inner stub
        new(166.5f, 79f), new(166.5f, 79f), new(171f, 89f), new(171f, 89f),     // right wing: inner stub, then outer stub
    };

    private const float SpritePxPerFt = 204f / 37.2f;     // the sprite's wingspan in px over the real span in feet
    private const float ConvergeFt = 750f;                 // 250 yd harmonisation

    /// <summary>The "A" wing: eight .303 Browning Mk IIs, four in each wing, harmonised to converge ahead of the nose. Muzzles
    /// are in the aircraft frame (feet: x along the right wing, y up, z forward), from the sprite points above (the sprite's
    /// origin, 128,133, is the aircraft's origin), a little below the wing chord line. Each Spitfire gets its own guns (the late-war Spitfire reuses these as a placeholder).</summary>
    public static Gun[] MakeGuns()
    {
        var guns = new Gun[MuzzleSpritePx.Length];
        for (var g = 0; g < guns.Length; g++)
        {
            var muzzle = new Vector3((MuzzleSpritePx[g].X - 128f) / SpritePxPerFt, -1f, (133f - MuzzleSpritePx[g].Y) / SpritePxPerFt);
            guns[g] = new Gun(GunSpec.Browning303, muzzle, ConvergeFt);
        }
        return guns;
    }

    // ---- Look ----

    /// <summary>Drawn from the early-war sheet. One three-bladed propeller (de Havilland), 10 ft 9 in across, its hub on
    /// that sheet's nose; the engine fire shows a little behind it on the cowling.</summary>
    public static readonly AircraftLook Look = new()
    {
        Sheet = new SpriteSheetSpec
        {
            Path = "Content/Sprites/aircraft/spitfire_mk2/sheet.png",
            SpanPx = 234f, SpanFt = 37.2f, AzZeroDeg = 0f,
            MuzzleShift = new Vector3(-1.3f, 0f, -0.55f),
        },
        Propellers = new[]
        {
            new PropellerSpec { Hub = new Vector3(12.7f, 0f, -0.55f), Blades = 3, RadiusFt = 5.45f, SpinnerFt = 1.0f, Turn = 1f },
        },
        FirePoints = new[] { new Vector3(9.2f, 0f, -0.15f) },
    };

    // ---- Parts, by kind in Part order: Engine, Canopy, Wing, Tail, Fuselage (one entry each, shared by every part of the kind) ----

    /// <summary>Subtracted from every roll that hits the part.</summary>
    public static readonly float[] Armor = { 4f, 0f, 1f, 0.5f, 2f };

    /// <summary>How hard each part is to destroy outright, 0-100 (see DamageTuning). Against the .303's Destructive of 30:
    /// engine and canopy have no floor (the .303 can destroy them); wings and tail have a floor of 55 hp and the fuselage
    /// 60, below which the .303 does less and less and can't take them into black (they stop at 26 hp, in red).</summary>
    public static readonly float[] Integrity = { 30f, 10f, 85f, 85f, 90f };

    /// <summary>What's left after armour is multiplied by this before it comes off the part's hit points (1 = as is).</summary>
    public static readonly float[] DamageMultiplier = { 1f, 1f, 1f, 1f, 1f };

    /// <summary>Hit boxes in the aircraft's frame, feet: x toward the right wing, y up, z toward the tail (the nose is at
    /// -z); the origin is about at the wing root.</summary>
    public static readonly DamageTuning.HitBox[] HitBoxes =
    {
        new(new Vector3(-2.6f, -2.4f, -16f), new Vector3(2.6f, 2.4f, -6.5f), Part.Engine),        // nose to firewall
        new(new Vector3(-1.3f, 1.8f, -2f), new Vector3(1.3f, 4.2f, 3f), Part.Canopy),             // cockpit glazing
        new(new Vector3(-2.5f, -2.6f, -6.5f), new Vector3(2.5f, 2.2f, 12.8f), Part.Fuselage),     // firewall to tail
        new(new Vector3(-1.2f, -1.4f, 12.8f), new Vector3(1.2f, 1.4f, 16.8f), Part.Fuselage),     // tail cone
        new(new Vector3(-18.6f, -1f, -4.2f), new Vector3(-2.5f, 1f, 4.7f), Part.Wing, 0),
        new(new Vector3(2.5f, -1f, -4.2f), new Vector3(18.6f, 1f, 4.7f), Part.Wing, 1),
        new(new Vector3(-5.4f, -0.3f, 13f), new Vector3(5.4f, 0.7f, 16.8f), Part.Tail),          // tailplane and elevators
        new(new Vector3(-0.5f, 0.7f, 12.8f), new Vector3(0.5f, 7f, 16.8f), Part.Tail),           // fin and rudder
    };
}
