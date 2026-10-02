using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Angels_Proj;

/// <summary>Placeholder textures generated in code, so no content-pipeline assets are needed yet.</summary>
public static class Art
{
    private static Texture2D Make(GraphicsDevice gd, int w, int h, Func<int, int, Color> f)
    {
        var px = new Color[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                px[y * w + x] = f(x, y);
        var t = new Texture2D(gd, w, h);
        t.SetData(px);
        return t;
    }

    public static Texture2D Pixel(GraphicsDevice gd) => Make(gd, 1, 1, (_, _) => Color.White);

    /// <summary>Seamlessly tiling 256x256 grass with soft patches.</summary>
    public static Texture2D Grass(GraphicsDevice gd)
    {
        const int N = 256;
        var rng = new Random(3);
        // A few periodic sine waves keep the tile seamless.
        var waves = new (int fx, int fy, float ph)[8];
        for (var i = 0; i < waves.Length; i++)
            waves[i] = (rng.Next(1, 5), rng.Next(1, 5), (float)(rng.NextDouble() * MathF.Tau));
        return Make(gd, N, N, (x, y) =>
        {
            var v = 0f;
            foreach (var (fx, fy, ph) in waves)
                v += MathF.Sin(MathF.Tau * (fx * x + fy * y) / N + ph);
            v /= waves.Length;
            var speck = ((x * 73856093) ^ (y * 19349663)) & 15;
            var g = 0.5f + v * 0.25f + (speck == 0 ? -0.08f : 0f);
            return new Color((int)(70 + 40 * g), (int)(125 + 50 * g), (int)(60 + 30 * g));
        });
    }

    /// <summary>Top-down house roof, 64x64: two gable halves around a ridge, with a chimney.</summary>
    public static Texture2D House(GraphicsDevice gd, Color roof)
    {
        var dark = new Color((int)(roof.R * 0.75f), (int)(roof.G * 0.75f), (int)(roof.B * 0.75f));
        return Make(gd, 64, 64, (x, y) =>
        {
            if (x < 6 || x > 57 || y < 10 || y > 53) // eaves shadow margin
                return (x >= 4 && x <= 59 && y >= 8 && y <= 55) ? new Color(0, 0, 0, 70) : Color.Transparent;
            if (x >= 40 && x <= 47 && y >= 16 && y <= 23) return new Color(110, 90, 80); // chimney
            if (Math.Abs(x - 31.5f) < 1) return new Color(60, 40, 30);                  // ridge
            return x < 32 ? roof : dark;
        });
    }

    /// <summary>Top-down tree canopy, 48x48.</summary>
    public static Texture2D Tree(GraphicsDevice gd) => Make(gd, 48, 48, (x, y) =>
    {
        var d = MathF.Sqrt((x - 23.5f) * (x - 23.5f) + (y - 23.5f) * (y - 23.5f));
        if (d > 22) return Color.Transparent;
        var lit = 1f - (x + y) / 140f;
        return new Color((int)(25 + 30 * lit), (int)(80 + 60 * lit), (int)(35 + 30 * lit));
    });

    /// <summary>Top-down plane, 96x96, nose pointing up, centred.</summary>
    public static Texture2D Plane(GraphicsDevice gd)
    {
        var body = new Color(210, 60, 50);
        var wing = new Color(235, 235, 225);
        return Make(gd, 96, 96, (x, y) =>
        {
            float dx = x + 0.5f - 48f, dy = y + 0.5f - 48f;
            // Propeller blur at the nose.
            if (dy > -42 && dy < -38 && Math.Abs(dx) < 16) return new Color(30, 30, 30, 110);
            // Cockpit.
            if ((dx * dx) / 25f + ((dy + 4) * (dy + 4)) / 49f <= 1) return new Color(120, 190, 230);
            // Fuselage: tapered capsule.
            var half = dy < -20 ? 7f : 7f - (dy + 20) * 0.1f;
            if (dy > -38 && dy < 40 && Math.Abs(dx) < Math.Max(2.5f, half)) return body;
            // Main wings: slight taper toward tips.
            if (dy > -14 && dy < 2 && Math.Abs(dx) < 46 && dy > -14 + Math.Abs(dx) * 0.08f) return wing;
            // Tailplane.
            if (dy > 28 && dy < 38 && Math.Abs(dx) < 18 && dy < 38 - Math.Abs(dx) * 0.2f) return wing;
            return Color.Transparent;
        });
    }
}
