using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// The Supermarine Spitfire: an Aircraft with everything that makes it one. Its airframe and limits are calibrated
/// against the real aircraft (Mk IX performance: level speed ~403 mph TAS at ~30,000 ft, ~4,100 ft/min climb at sea
/// level falling to 0 at 51,550 ft, stall ~86 mph, never-exceed 450 mph IAS, dive to ~Mach 0.87 from altitude), with
/// the Mk I's 85 gallons of fuel; its hit boxes are laid out on its 30 ft length and 37 ft span; and each part has its
/// armour, integrity and damage multiplier. Tweak a Spitfire here.
/// </summary>
public sealed class Spitfire : Aircraft
{
    public Spitfire(Pilot pilot)
        : base(MakeAirframe(), HitBoxes, Armor, Integrity, DamageMultiplier, Look, MakeGuns(), pilot) { }

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
    /// origin, 128,133, is the aircraft's origin), a little below the wing chord line. Each Spitfire gets its own guns.</summary>
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

    /// <summary>A fresh Spitfire airframe (each aircraft gets its own, so one can be changed without the others).</summary>
    public static Airframe MakeAirframe() => new()
    {
        Name = "SPITFIRE",

        // ---- Airframe ----
        WeightLb = 7400f, WingArea = 242f, Span = 36.83f,
        CD0 = 0.0195f, OswaldE = 0.55f, CLmax = 1.6f,
        PropEff = 0.82f, StaticThrustCapLb = 3500f,
        IdlePropDragCD = 0.006f,
        // Full-throttle power (hp) by altitude (ft), chosen so the climb rates match the real aircraft.
        PowerTable = new (float alt, float hp)[]
        {
            (0, 1491), (5000, 1477), (10000, 1440), (15000, 1408), (20000, 1353), (25000, 1290),
            (28000, 1231), (30000, 1180), (35000, 1036), (40000, 843), (43000, 767), (47000, 780),
            (51550, 840), (56000, 600), (65000, 150),
        },
        FuelCapacityGal = 85f,                                       // Mk I: 48 + 37 imperial gallons
        FuelBurnIdleGalPerMin = 0.3f, FuelBurnFullGalPerMin = 1.5f,
        EngineAheadFt = 11f,

        // ---- Limits ----
        VneMph = 450f,
        WaveDragK = 3f,
        OverspeedDragK = 0.3f,
        // Level flight is 1 g, so 7 up and -5 down are the same 6 g of change either way: pulling and pushing the nose
        // have equal authority.
        MaxNPos = 7.0f, MaxNNeg = 5.0f,
        NTurnMax = 7f, NStruct = 12f,
        QRef = 160f,                                                 // ~250 mph at sea level
        StallDragCD = 0.12f,
        RudderRate = 0.0015f,                                        // about 5 deg/s at full rudder
        RudderSlipDragCD = 0.012f,
        StallNoseDropDegS = 75f,
        MaxClimbDeg = 60f, MaxDiveDeg = 90f,
        CeilingFt = 51550f,
        RollResponse = 0.18f,
    };

    // ---- Look ----

    /// <summary>Drawn from the early-war sheet. One three-bladed propeller (de Havilland), 10 ft 9 in across, its hub on
    /// that sheet's nose; the engine fire shows a little behind it on the cowling.</summary>
    public static readonly AircraftLook Look = new()
    {
        Sheet = new SpriteSheetSpec
        {
            Path = "Content/Sprites/spitfire_oldwar/spitfire_sheet.png",
            SpanPx = 234f, SpanFt = 37.2f, AzZeroDeg = 0f,
            MuzzleShift = new Vector3(-1.3f, 0f, -0.55f),
        },
        Propellers = new[]
        {
            new PropellerSpec { Hub = new Vector3(12.7f, 0f, -0.55f), Blades = 3, RadiusFt = 5.45f, SpinnerFt = 1.0f, Turn = 1f },
        },
        FirePoints = new[] { new Vector3(9.2f, 0f, -0.15f) },
    };

    /// <summary>The late-war sheet, for a later mark when it gets its own type: its propeller has 4 blades on a hub at
    /// (14.6, 0, -1.25).</summary>
    public static readonly SpriteSheetSpec LateWarSheet = new()
    {
        Path = "Content/Sprites/spitfire3d/spitfire_sheet.png",
        SpanPx = 234f, SpanFt = 37.2f, AzZeroDeg = 0f,
        MuzzleShift = new Vector3(0.7f, 0f, -1.25f),
    };

    // ---- Parts, in Part order: Engine, Canopy, LeftWing, RightWing, Tail, Fuselage ----

    /// <summary>Subtracted from every roll that hits the part.</summary>
    public static readonly float[] Armor = { 4f, 0f, 1f, 1f, 0.5f, 2f };

    /// <summary>How hard each part is to destroy outright, 0-100 (see DamageTuning). Against the .303's Destructive of 30:
    /// engine and canopy have no floor (the .303 can destroy them); wings and tail have a floor of 55 hp and the fuselage
    /// 60, below which the .303 does less and less and can't take them into black (they stop at 26 hp, in red).</summary>
    public static readonly float[] Integrity = { 30f, 10f, 85f, 85f, 85f, 90f };

    /// <summary>What's left after armour is multiplied by this before it comes off the part's hit points (1 = as is).</summary>
    public static readonly float[] DamageMultiplier = { 1f, 1f, 1f, 1f, 1f, 1f };

    /// <summary>Hit boxes in the aircraft's frame, feet: x toward the right wing, y up, z toward the tail (the nose is at
    /// -z); the origin is about at the wing root.</summary>
    public static readonly DamageTuning.HitBox[] HitBoxes =
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
