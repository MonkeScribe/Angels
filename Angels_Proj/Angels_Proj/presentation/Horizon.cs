using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Angels_Proj;

/// <summary>Artificial horizon: a circular instrument rendered on the CPU into a small texture each frame.</summary>
public sealed class Horizon
{
    public const int Size = 176;
    private const float PxPerDeg = 2.1f; // ladder spacing; about +/-40 deg visible

    private readonly Texture2D _tex;
    private readonly Color[] _px = new Color[Size * Size];

    public Texture2D Texture => _tex;

    public Horizon(GraphicsDevice gd) => _tex = new Texture2D(gd, Size, Size);

    /// <param name="pitch">Nose attitude, rad (+ = up). <param name="bank">Bank, rad (+ = right wing down).</param></param>
    public void Update(float pitch, float bank)
    {
        var sky = new Color(70, 140, 215);
        var ground = new Color(120, 82, 48);
        var line = new Color(245, 245, 245);
        const float c = (Size - 1) / 2f, rIn = c - 8f, rOut = c - 1f;
        float cb = MathF.Cos(bank), sb = MathF.Sin(bank);
        var off = pitch * 180f / MathF.PI * PxPerDeg; // nose up pushes the horizon down

        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                float dx = x - c, dy = y - c;
                var r = MathF.Sqrt(dx * dx + dy * dy);
                if (r > rOut) { _px[y * Size + x] = Color.Transparent; continue; }
                if (r > rIn) { _px[y * Size + x] = new Color(28, 30, 36); continue; } // bezel

                // Rotate into the world frame: n runs "down" the world, t along the horizon.
                var n = dy * cb + dx * sb - off;
                var t = dx * cb - dy * sb;
                var col = n > 0 ? ground : sky;
                if (MathF.Abs(n) < 1.2f) col = line;
                else
                {
                    // Pitch ladder: a rung every 10 degrees, longer at 20 and 40.
                    var deg = -n / PxPerDeg;
                    var rung = MathF.Round(deg / 10f) * 10f;
                    if (rung != 0f && MathF.Abs(rung) <= 90f && MathF.Abs(-(rung * PxPerDeg) - n) < 0.8f)
                    {
                        var half = (MathF.Abs(rung) % 20f == 0f) ? 22f : 11f;
                        if (MathF.Abs(t) < half) col = line;
                    }
                }
                _px[y * Size + x] = col;
            }

        // Bank scale ticks on the bezel (0, +/-30, +/-60 deg from the top), drawn fixed to the case.
        foreach (var a in new[] { -60f, -30f, 0f, 30f, 60f })
        {
            var rad = a * MathF.PI / 180f;
            for (var k = -4; k <= 4; k++)
            {
                var rr = c - 4f + k * 0.6f;
                int px = (int)MathF.Round(c + MathF.Sin(rad) * rr), py = (int)MathF.Round(c - MathF.Cos(rad) * rr);
                _px[py * Size + px] = a == 0f ? new Color(255, 206, 84) : line;
            }
        }

        // Fixed aircraft symbol: wings and a centre dot.
        var sym = new Color(255, 206, 84);
        void Rect(int x0, int y0, int w, int h) { for (var j = y0; j < y0 + h; j++) for (var i = x0; i < x0 + w; i++) _px[j * Size + i] = sym; }
        int ic = (int)c;
        Rect(ic - 46, ic - 1, 30, 3); Rect(ic - 46, ic - 1, 3, 9);
        Rect(ic + 17, ic - 1, 30, 3); Rect(ic + 44, ic - 1, 3, 9);
        Rect(ic - 2, ic - 2, 5, 5);

        _tex.SetData(_px);
    }
}
