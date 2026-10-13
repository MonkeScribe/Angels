namespace Angels_Proj;

/// <summary>
/// The airframes of aircraft that have no type of their own yet (no sprites, hit boxes or guns): enough to fly one in the
/// flight model and set it against the Spitfire, which is how the models are tuned (see the FlightReport tool). Each is
/// calibrated against the real aircraft's published figures, noted beside it. When one gets its own aircraft type, its
/// airframe moves there, as the Spitfire's has.
/// </summary>
public static class Airframes
{
    /// <summary>Messerschmitt Bf 109 E-4: lighter and smaller than the Spitfire with a fluid-drive single-stage
    /// supercharger. Real: ~348 mph at 14,500 ft, ~3,300 ft/min at sea level, slats keep its wing flying at low speed,
    /// and its heavy ailerons lock up past 300 mph.</summary>
    public static Airframe Bf109E4() => new()
    {
        Name = "BF 109 E-4",
        WeightLb = 5875f, WingArea = 174f, Span = 32.4f,
        CD0 = 0.0220f, OswaldE = 0.6f, CLmax = 1.8f,
        PropEff = 0.8f, StaticThrustCapLb = 3000f,
        IdlePropDragCD = 0.006f,
        Stages = new[] { new SuperchargerStage(1070f, 1070f, 12000f) },
        AltitudeFallOff = 0.95f,
        FuelCapacityGal = 88f,
        FuelBurnIdleGalPerMin = 0.3f, FuelBurnFullGalPerMin = 1.6f,
        EngineAheadFt = 10f,

        VneMph = 466f,
        MaxNPos = 7.0f, MaxNNeg = 4.0f, NTurnMax = 7f, NStruct = 12f, QRef = 160f,
        StallDragCD = 0.6f, RudderRate = 0.0015f, RudderSlipDragCD = 0.012f, StallNoseDropDegS = 75f,
        MaxClimbDeg = 60f, MaxDiveDeg = 90f, CeilingFt = 34500f,
        RollResponse = 0.18f,
        RollRateDegS = 100f, RollPeakMph = 230f, RollFadePer100Mph = 0.45f,
    };

    /// <summary>Focke-Wulf Fw 190 A-4: heavy, high wing loading and a stall that comes sharply, but a big radial that
    /// pulls hard at low and medium height and the best ailerons of the war. Real: ~408 mph at 20,600 ft, ~3,300 ft/min
    /// at sea level, rolls well even at 400 mph, and power falls away steeply above 20,000 ft.</summary>
    public static Airframe Fw190A4() => new()
    {
        Name = "FW 190 A-4",
        WeightLb = 8770f, WingArea = 197f, Span = 34.4f,
        CD0 = 0.0215f, OswaldE = 0.6f, CLmax = 1.4f,
        PropEff = 0.82f, StaticThrustCapLb = 4200f,
        IdlePropDragCD = 0.007f,
        Stages = new[]
        {
            new SuperchargerStage(1560f, 1590f, 4000f),
            new SuperchargerStage(1480f, 1560f, 19000f),
        },
        AltitudeFallOff = 1.05f,
        FuelCapacityGal = 115f,
        FuelBurnIdleGalPerMin = 0.4f, FuelBurnFullGalPerMin = 2.0f,
        EngineAheadFt = 9f,

        VneMph = 485f,
        MaxNPos = 7.0f, MaxNNeg = 5.0f, NTurnMax = 7f, NStruct = 12f, QRef = 160f,
        StallDragCD = 0.8f, RudderRate = 0.0015f, RudderSlipDragCD = 0.012f, StallNoseDropDegS = 90f,
        MaxClimbDeg = 60f, MaxDiveDeg = 90f, CeilingFt = 37400f,
        RollResponse = 0.18f,
        RollRateDegS = 150f, RollPeakMph = 300f, RollFadePer100Mph = 0.15f,
    };
}
