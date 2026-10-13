namespace Angels_Proj;

/// <summary>
/// The Supermarine Spitfire Mk II's airframe and limits. They are calibrated against the real Spitfire's Mk IX performance
/// (level speed ~335 / 378 / 401 / 407 mph TAS at sea level / 10,000 / 20,000 / 30,000 ft, climb ~4,100 ft/min at sea level
/// and ~2,300 at 30,000, stall ~86 mph, never-exceed 450 mph IAS, dive to ~Mach 0.87 from altitude; measured by the
/// FlightReport tool), with the Mk IX's 85 gallons of fuel and its Merlin 60-series fuel burn.
/// </summary>
public static class SpitfireMk2Airframe
{
    /// <summary>A fresh Spitfire Mk II airframe (each aircraft gets its own, so one can be changed without the others).</summary>
    public static Airframe Make() => new()
    {
        Name = "SPITFIRE MK II",

        // ---- Airframe ----
        WeightLb = 7500f, WingArea = 242f, Span = 36.83f,
        CD0 = 0.0180f, OswaldE = 0.75f, CLmax = 1.6f,
        PropEff = 0.78f, StaticThrustCapLb = 3500f,
        IdlePropDragCD = 0.006f,
        // Merlin 61: two-stage, two-speed supercharger. Medium gear gives 1,565 hp at 12,250 ft; full gear takes over
        // above about 18,000 ft and gives 1,390 hp at 23,500 ft (the second blower costs some power to drive).
        Stages = new[]
        {
            new SuperchargerStage(1500f, 1630f, 12250f),
            new SuperchargerStage(1320f, 1440f, 23500f),
        },
        AltitudeFallOff = 1.1f,
        // Two main tanks ahead of the cockpit, 48 upper + 37 lower imperial gallons (the same on the Mk I and Mk IX).
        FuelCapacityGal = 85f,
        // Imperial gallons an hour against throttle, from the Spitfire IX Pilot's Notes (AP 1565J) and A&AEE trials of the
        // Merlin 66 (JL165): full throttle is combat power, +18 lb/3,000 rpm, about 150 gal/hr; 0.8 is max climb,
        // +12 lb/2,850 rpm, 105 gal/hr; 0.6 (AI cruise) is weak-mixture cruise, +3.75 lb/2,650 rpm, about 65 gal/hr; idle
        // is an estimate (no published figure). So a full load lasts ~34 min flat out and ~78 min at cruise.
        FuelBurnTable = new (float throttle, float galPerHour)[]
        {
            (0f, 10f), (0.6f, 65f), (0.8f, 105f), (1f, 150f),
        },
        FuelTankParts = new[] { Part.Fuselage },                     // the tanks sit between the firewall and the cockpit
        EngineAheadFt = 11f,

        // ---- Limits ----
        VneMph = 450f,
        // Level flight is 1 g, so 7 up and -5 down are the same 6 g of change either way: pulling and pushing the nose
        // have equal authority.
        MaxNPos = 7.0f, MaxNNeg = 5.0f,
        NTurnMax = 7f, NStruct = 12f,
        QRef = 160f,                                                 // ~250 mph at sea level
        StallDragCD = 0.6f,
        RudderRate = 0.0015f,                                        // about 5 deg/s at full rudder
        RudderSlipDragCD = 0.012f,
        StallNoseDropDegS = 75f,
        MaxClimbDeg = 60f, MaxDiveDeg = 90f,
        CeilingFt = 51550f,
        RollResponse = 0.18f,
        RollRateDegS = 120f, RollPeakMph = 280f, RollFadePer100Mph = 0.4f,   // Mk IX metal ailerons: crisp to 300 mph, heavy beyond
    };
}
