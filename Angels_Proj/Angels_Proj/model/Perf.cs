using System.Collections.Generic;
using System.Diagnostics;

namespace Angels_Proj;

/// <summary>
/// Simple timing for the PERF overlay: how many milliseconds each named stretch of work takes, added up over a frame and
/// smoothed over frames. These are CPU times; work the graphics card does later (filling the screen) shows up in the
/// frame time, not here, which is why the overlay also counts how much screen the clouds cover.
/// </summary>
public sealed class Perf
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, double> _frame = new();
    private readonly Dictionary<string, float> _shown = new();

    /// <summary>A timestamp to pass to Add.</summary>
    public long Now => _clock.ElapsedTicks;

    /// <summary>Adds the time since since to the named section for this frame.</summary>
    public void Add(string name, long since)
    {
        var ms = (_clock.ElapsedTicks - since) * 1000.0 / Stopwatch.Frequency;
        _frame[name] = (_frame.TryGetValue(name, out var v) ? v : 0.0) + ms;
    }

    /// <summary>Ends a frame: each section's total eases the shown value toward it.</summary>
    public void EndFrame()
    {
        foreach (var kv in _frame)
        {
            var shown = _shown.TryGetValue(kv.Key, out var s) ? s : (float)kv.Value;
            _shown[kv.Key] = shown + ((float)kv.Value - shown) * 0.1f;
        }
        var keys = new List<string>(_frame.Keys);
        foreach (var k in keys) _frame[k] = 0.0;
    }

    /// <summary>The smoothed milliseconds of a section.</summary>
    public float this[string name] => _shown.TryGetValue(name, out var v) ? v : 0f;
}
