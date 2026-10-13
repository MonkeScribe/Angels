using System;
using System.Collections.Generic;
using Angels_Proj;
using Microsoft.Xna.Framework;

// Flies each airframe through the same set of manoeuvres with no game around it and prints the six numbers that decide a
// fight: climb, level speed, acceleration, engine power with height, roll rate and what a hard turn costs. Run it after
// changing an airframe or the flight model:   dotnet run --project FlightReport
const float Dt = 1f / 60f;
var types = new List<(string name, Func<Airframe> make)>
{
    ("SPITFIRE MK II", SpitfireMk2Airframe.Make),
    ("SPITFIRE LATE WAR", SpitfireLateWarAirframe.Make),
    ("HE 111", He111Airframe.Make),
};
var alts = new[] { 0f, 10000f, 20000f, 30000f };

FlightModel Fly(Airframe a, float alt, float tasMph)
{
    var fm = new FlightModel(a) { Altitude = alt, Speed = tasMph / FlightModel.Mph, SnapOnRelease = false };
    fm.Throttle = 1f; fm.EnginePower = 1f;
    return fm;
}

// Level, full throttle, height held: settle and read the speed.
float LevelMph(Airframe a, float alt, out float ias)
{
    var fm = Fly(a, alt, 300f); var rng = new Random(1);
    for (var i = 0; i < 500 * 60; i++) { fm.Altitude = alt; fm.PitchCmdDeg = 0f; fm.Gamma = 0f; fm.Step(a.RollResponse, rng); }
    ias = fm.IasMph;
    return fm.TasMph;
}

// Climb at full throttle at each angle from 2 to 30 degrees, speed settled, height held; the best rate in ft/min, at a
// speed no nearer the stall than a pilot would fly (1.25 x the stall speed).
float BestClimbFpm(Airframe a, float alt, out float atMph)
{
    var best = 0f; atMph = 0f;
    for (var deg = 2; deg <= 30; deg += 2)
    {
        var fm = Fly(a, alt, 300f); var rng = new Random(1);
        for (var i = 0; i < 240 * 60; i++) { fm.Altitude = alt; fm.PitchCmdDeg = deg; fm.Step(a.RollResponse, rng); }
        if (fm.VerticalSpeedFpm > best && fm.Speed >= 1.25f * fm.StallSpeed(1f) && MathF.Abs(MathHelper.ToDegrees(fm.Gamma) - deg) < 1.5f) { best = fm.VerticalSpeedFpm; atMph = fm.IasMph; }
    }
    return best;
}

// Seconds from one true airspeed to another, level, full throttle.
string AccelSec(Airframe a, float alt, float from, float to)
{
    var fm = Fly(a, alt, from); var rng = new Random(1);
    for (var i = 0; i < 120 * 60; i++)
    {
        fm.Altitude = alt; fm.PitchCmdDeg = 0f; fm.Gamma = 0f; fm.Step(a.RollResponse, rng);
        if (fm.TasMph >= to) return $"{i * Dt,5:F1}s";
    }
    return "  n/a";
}

// How fast the bank swings (deg/s) as the stick goes hard over at a given indicated airspeed, sea level: the bank after
// the first 8 ticks, averaged over 40 runs (a wing near its stall shakes the bank about, which would hide the rate).
float RollDegS(Airframe a, float iasMph)
{
    var sum = 0f;
    for (var seed = 0; seed < 40; seed++)
    {
        var fm = Fly(a, 0f, iasMph); var rng = new Random(seed);
        fm.Controls.Roll = 1f;
        for (var i = 0; i < 8; i++)
        {
            fm.Altitude = 0f; fm.Speed = iasMph / FlightModel.Mph; fm.Gamma = 0f; fm.PitchCmdDeg = 0f; fm.Step(a.RollResponse, rng);
        }
        sum += MathHelper.ToDegrees(fm.Bank) / (8 * Dt);
    }
    return sum / 40f;
}

// A held full-bank turn at full throttle, trying to hold height: speed after 0, 5, 10 and 20 s, turn rate and height lost.
string Turn(Airframe a, float alt, float tasMph)
{
    var fm = Fly(a, alt, tasMph); var rng = new Random(1);
    fm.Controls.Roll = 1f;
    var s = new float[4]; var hdg = 0f; var last = fm.Heading; var hdgRate = 0f; var stalled = 0;
    for (var i = 0; i <= 20 * 60; i++)
    {
        fm.PitchCmdDeg = 0f; fm.Step(a.RollResponse, rng);
        hdg += MathHelper.WrapAngle(fm.Heading - last); last = fm.Heading;
        if (fm.AccelStall) stalled++;
        if (i == 0) s[0] = fm.TasMph; if (i == 5 * 60) s[1] = fm.TasMph; if (i == 10 * 60) s[2] = fm.TasMph; if (i == 20 * 60) s[3] = fm.TasMph;
        if (i == 10 * 60) hdgRate = hdg;
    }
    return $"{s[0],4:F0} -> {s[1],4:F0} {s[2],4:F0} {s[3],4:F0} mph | {MathHelper.ToDegrees(hdgRate) / 10f,5:F1} deg/s over 10s | height {fm.Altitude - alt,+7:F0} ft | stalled {stalled * 100f / (20 * 60):F0}%";
}

foreach (var (name, make) in types)
{
    var a = make();
    Console.WriteLine($"=== {a.Name}  {a.WeightLb:F0} lb, {a.WingArea:F0} sq ft ({a.WingLoading:F1} lb/sq ft), AR {a.AspectRatio:F1}");
    Console.WriteLine("  alt ft   power hp   level TAS (IAS) mph   best climb ft/min (at IAS)");
    foreach (var alt in alts)
    {
        var mph = LevelMph(a, alt, out var ias);
        var climb = BestClimbFpm(a, alt, out var at);
        Console.WriteLine($"  {alt,6:F0}   {a.PowerHp(alt),8:F0}   {mph,8:F0} ({ias,4:F0})        {climb,6:F0} ({at:F0})");
    }
    Console.WriteLine($"  acceleration, level, full throttle:  200->300 mph TAS  sea level {AccelSec(a, 0f, 200f, 300f)}   20,000 ft {AccelSec(a, 20000f, 200f, 300f)}");
    Console.Write("  roll rate deg/s by IAS:");
    foreach (var ias in new[] { 150f, 250f, 350f, 450f }) Console.Write($"  {ias:F0} mph {RollDegS(a, ias),5:F0} ");
    Console.WriteLine();
    Console.WriteLine($"  full-bank turn, 10,000 ft, from 250 mph: {Turn(a, 10000f, 250f)}");
    Console.WriteLine($"  full-bank turn, 10,000 ft, from 350 mph: {Turn(a, 10000f, 350f)}");
    Console.WriteLine();
}
