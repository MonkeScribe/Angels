using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// The late-war Spitfire: the same airframe geometry as the Mk II, drawn from the late-war sheet with its four-bladed
/// propeller. Its guns, hit boxes, armour and integrity are the Mk II's until this mark gets its own.
/// </summary>
public sealed class SpitfireLateWar : Aircraft
{
    public SpitfireLateWar(Pilot pilot)
        : base(SpitfireLateWarAirframe.Make(), SpitfireMk2.HitBoxes, SpitfireMk2.Armor, SpitfireMk2.Integrity,
            SpitfireMk2.DamageMultiplier, Look, SpitfireMk2.MakeGuns(), pilot) { }

    // ---- Look ----

    /// <summary>Drawn from the late-war sheet. One four-bladed propeller on a hub at (14.6, 0, -1.25); the engine fire
    /// shows a little behind it on the cowling.</summary>
    public static readonly AircraftLook Look = new()
    {
        Sheet = new SpriteSheetSpec
        {
            Path = "Content/Sprites/aircraft/spitfire_latewar/sheet.png",
            SpanPx = 234f, SpanFt = 37.2f, AzZeroDeg = 0f,
            MuzzleShift = new Vector3(0.7f, 0f, -1.25f),
        },
        Propellers = new[]
        {
            new PropellerSpec { Hub = new Vector3(14.6f, 0f, -1.25f), Blades = 4, RadiusFt = 5.45f, SpinnerFt = 1.0f, Turn = 1f },
        },
        FirePoints = new[] { new Vector3(11.1f, 0f, -0.85f) },
    };
}
