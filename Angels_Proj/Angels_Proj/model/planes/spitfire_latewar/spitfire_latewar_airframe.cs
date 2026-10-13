namespace Angels_Proj;

/// <summary>
/// The late-war Spitfire's airframe and limits. Placeholder: the same figures as the Mk II (themselves tuned to Mk IX
/// performance) until this mark is tuned on its own; it differs only in its name for now.
/// </summary>
public static class SpitfireLateWarAirframe
{
    /// <summary>A fresh late-war Spitfire airframe (each aircraft gets its own, so one can be changed without the others).</summary>
    public static Airframe Make()
    {
        var a = SpitfireMk2Airframe.Make();
        a.Name = "SPITFIRE LATE WAR";
        return a;
    }
}
