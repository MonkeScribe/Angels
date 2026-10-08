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

    /// <summary>Colour of the level plane (96x96 sprite space, nose up) at an offset from its centre.</summary>
    public static readonly Color PlayerBody = new(210, 60, 50);

    private static Color PlaneColor(float dx, float dy, Color body)
    {
        var wing = new Color(235, 235, 225);
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
    }

    /// <summary>
    /// Top-down plane, 96x96, nose pointing up, centred, for one pitch step. Seen from above, pitching
    /// shortens the fuselage (foreshortening) and the end tipped toward the camera looks larger: the nose
    /// when diving, the tail when climbing. Placeholder art; swap in real sprites per 10 degree step.
    /// </summary>
    public static Texture2D Plane(GraphicsDevice gd, float pitchDeg, Color? bodyColor = null)
    {
        var body = bodyColor ?? PlayerBody;
        var th = pitchDeg * MathF.PI / 180f;
        var lengthScale = MathF.Max(MathF.Cos(th), 0.3f);
        return Make(gd, 96, 96, (x, y) =>
        {
            float dx = x + 0.5f - 48f, dy = y + 0.5f - 48f;
            var f = MathF.Max(0.5f, 1f + 0.30f * MathF.Sin(th) * (dy / 48f)); // perspective: nearer end is bigger
            float sx = dx / f, sy = dy / (f * lengthScale);
            return (Math.Abs(sx) > 48f || Math.Abs(sy) > 48f) ? Color.Transparent : PlaneColor(sx, sy, body);
        });
    }

    /// <summary>Soft puffy cloud, 256x160, premultiplied white with a slightly shaded underside.</summary>
    public static Texture2D Cloud(GraphicsDevice gd, int seed)
    {
        var rng = new Random(seed);
        var blobs = new (float x, float y, float r)[9];
        for (var i = 0; i < blobs.Length; i++)
            blobs[i] = (50f + (float)rng.NextDouble() * 156f, 62f + (float)rng.NextDouble() * 44f, 26f + (float)rng.NextDouble() * 26f);
        return Make(gd, 256, 160, (x, y) =>
        {
            var dens = 0f;
            foreach (var (bx, by, br) in blobs)
            {
                var d2 = ((x - bx) * (x - bx) + (y - by) * (y - by)) / (br * br);
                if (d2 < 1f) dens += (1f - d2) * (1f - d2);
            }
            var a = Math.Clamp(dens * 1.4f, 0f, 1f) * 0.95f;
            if (a <= 0f) return Color.Transparent;
            var shade = 1f - 0.2f * Math.Clamp((y - 55f) / 60f, 0f, 1f);
            return new Color(shade * a, shade * a, (shade + 0.02f) * a, a); // premultiplied
        });
    }
}
