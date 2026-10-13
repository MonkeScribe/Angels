namespace Angels_Proj;

/// <summary>
/// The Heinkel He 111 H's airframe and limits. Estimates from the published figures (not yet tuned with the FlightReport
/// tool): ~270 mph at 16,400 ft, ~800 ft/min climb at sea level, 25,500 ft ceiling, two Jumo 211 engines of about 1,200 hp.
/// </summary>
public static class He111Airframe
{
    /// <summary>A fresh He 111 airframe (each aircraft gets its own, so one can be changed without the others).</summary>
    public static Airframe Make() => new()
    {
        Name = "HE 111",

        // ---- Airframe ----
        WeightLb = 25000f, WingArea = 943f, Span = 74.2f,
        CD0 = 0.0200f, OswaldE = 0.7f, CLmax = 1.5f,
        PropEff = 0.8f, StaticThrustCapLb = 5500f,
        IdlePropDragCD = 0.008f,
        // Two Jumo 211 F, single-speed supercharger: about 1,150 hp each at sea level, 1,200 at their critical height.
        Stages = new[] { new SuperchargerStage(2300f, 2400f, 14000f) },
        AltitudeFallOff = 1f,
        FuelCapacityGal = 660f,
        FuelBurnTable = new (float throttle, float galPerHour)[]
        {
            (0f, 25f), (0.6f, 110f), (0.8f, 140f), (1f, 170f),
        },
        FuelTankParts = new[] { Part.Fuselage, Part.LeftWing, Part.RightWing },
        EngineAheadFt = 12f,

        // ---- Limits ----
        VneMph = 340f,
        MaxNPos = 3.5f, MaxNNeg = 1.5f,
        NTurnMax = 3f, NStruct = 5f,
        QRef = 120f,
        StallDragCD = 0.6f,
        RudderRate = 0.001f,
        RudderSlipDragCD = 0.012f,
        StallNoseDropDegS = 45f,
        MaxClimbDeg = 35f, MaxDiveDeg = 60f,
        CeilingFt = 25500f,
        RollResponse = 0.08f,
        RollRateDegS = 35f, RollPeakMph = 200f, RollFadePer100Mph = 0.4f,
    };
}
