using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Angels_Proj;

/// <summary>
/// Sprites for engine fire and smoke, shared by the map and the gunsight. They are separate from the aircraft's own
/// sprite sheet and laid over it: the fire is an animation drawn on the engine, turned so the flames stream back along
/// the aircraft, and the smoke is puffs left in the air behind it (see Fx).
///
/// Loaded from Content/Sprites/fx: fire_0.png, fire_1.png ... (the animation's frames, flame rising toward the top of
/// the image with its base low in the middle), smoke_0.png, smoke_1.png ... (puffs) and explosion_0.png ... (a fireball
/// from first flash to last smoke, centred). Any that are missing are made in code instead, so the game runs without them.
/// </summary>
public sealed class EffectArt
{
    public readonly Texture2D[] Fire;
    public readonly Texture2D[] Smoke;
    public readonly Texture2D[] Explosion;   // played once, first frame to last, over an explosion's life
    public readonly Texture2D Mist;     // pale round puff for leaking fuel (made in code)

    public const float FireFps = 14f;

    /// <summary>Where the flame's base is in a fire frame (fraction of its height from the top), and how much of the
    /// frame's height and width the flame itself fills.</summary>
    public const float FireBaseY = 0.8f, FireLengthFrac = 0.7f, FireWidthFrac = 0.4f;

    public EffectArt(GraphicsDevice gd)
    {
        var fire = LoadSeq(gd, "Content/Sprites/fx/fire_");
        Fire = fire.Length > 0 ? fire : MakeFire(gd);
        var smoke = LoadSeq(gd, "Content/Sprites/fx/smoke_");
        Smoke = smoke.Length > 0 ? smoke : MakeSmoke(gd);
        var boom = LoadSeq(gd, "Content/Sprites/fx/explosion_");
        Explosion = boom.Length > 0 ? boom : MakeExplosion(gd);
        Mist = Bake(gd, 16, 16, (x, y) =>
        {
            var d = MathF.Sqrt((x - 7.5f) * (x - 7.5f) + (y - 7.5f) * (y - 7.5f)) / 7.5f;
            var a = d < 0.6f ? 0.8f : d < 1f ? 0.45f : 0f;      // two hard steps, pixel-art style
            return new Color(0.93f * a, 0.95f * a, a, a);
        });
    }

    /// <summary>The fire frame to show at this time; salt staggers different aircraft so their fires don't flicker in step.</summary>
    public Texture2D FireFrame(float seconds, int salt) => Fire[(int)(seconds * FireFps + salt * 3) % Fire.Length];

    public static Vector2 FireOrigin(Texture2D frame) => new(frame.Width / 2f, frame.Height * FireBaseY);

    private static Texture2D[] LoadSeq(GraphicsDevice gd, string prefix)
    {
        var list = new List<Texture2D>();
        for (var i = 0; i < 64; i++)
        {
            try
            {
                using var stream = TitleContainer.OpenStream($"{prefix}{i}.png");
                using var tex = Texture2D.FromStream(gd, stream);
                var px = new Color[tex.Width * tex.Height];
                tex.GetData(px);
                for (var k = 0; k < px.Length; k++) // blended premultiplied, like every other sprite
                    px[k] = px[k].A == 0 ? Color.Transparent : Color.FromNonPremultiplied(px[k].R, px[k].G, px[k].B, px[k].A);
                var t = new Texture2D(gd, tex.Width, tex.Height);
                t.SetData(px);
                list.Add(t);
            }
            catch (Exception) { break; }   // no more frames
        }
        return list.ToArray();
    }

    private static Texture2D Bake(GraphicsDevice gd, int w, int h, Func<int, int, Color> f)
    {
        var px = new Color[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                px[y * w + x] = f(x, y);
        var t = new Texture2D(gd, w, h);
        t.SetData(px);
        return t;
    }

    /// <summary>Stand-in flame: a flickering teardrop, white-yellow at the base, orange then red toward the edges and tip.</summary>
    private static Texture2D[] MakeFire(GraphicsDevice gd)
    {
        const int W = 32, H = 48, N = 8;
        var frames = new Texture2D[N];
        for (var n = 0; n < N; n++)
        {
            var ph = n * MathF.Tau / N;
            frames[n] = Bake(gd, W, H, (x, y) =>
            {
                var v = (H * FireBaseY - y) / (H * FireBaseY * 0.95f);      // 0 at the base, 1 at the tip
                if (v < -0.12f || v > 1f) return Color.Transparent;
                var sway = 2.2f * MathF.Sin(ph + v * 5f) * v;
                var u = (x + 0.5f - W / 2f - sway) / (W / 2f);
                var width = (v < 0f ? 1f + v * 6f : MathF.Pow(1f - v, 0.75f)) * (0.85f + 0.15f * MathF.Sin(ph * 2f + v * 9f));
                var r = MathF.Abs(u) / MathF.Max(0.05f, width * 0.8f);
                if (r > 1f) return Color.Transparent;
                var heat = (1f - r) * (1f - 0.8f * MathF.Max(0f, v));
                var c = heat > 0.55f ? new Color(255, 248, 210) : heat > 0.35f ? new Color(255, 214, 80)
                      : heat > 0.18f ? new Color(255, 140, 30) : new Color(220, 50, 20);
                return c;
            });
        }
        return frames;
    }

    /// <summary>The explosion frame for how far through it is (0-1).</summary>
    public Texture2D ExplosionFrame(float age) => Explosion[Math.Clamp((int)(age * Explosion.Length), 0, Explosion.Length - 1)];

    /// <summary>Stand-in explosion: a lumpy fireball that swells from a white-hot flash through yellow and orange to a
    /// thinning ring of dark smoke.</summary>
    private static Texture2D[] MakeExplosion(GraphicsDevice gd)
    {
        const int S = 64, N = 10;
        var rng = new Random(77);
        var blobs = new (float x, float y, float r)[9];
        for (var i = 0; i < blobs.Length; i++)
        {
            var a = i * MathF.Tau / blobs.Length + (float)rng.NextDouble() * 0.5f;
            var d = i == 0 ? 0f : 0.3f + (float)rng.NextDouble() * 0.2f;
            blobs[i] = (MathF.Cos(a) * d, MathF.Sin(a) * d, i == 0 ? 0.55f : 0.35f + (float)rng.NextDouble() * 0.15f);
        }
        var frames = new Texture2D[N];
        for (var n = 0; n < N; n++)
        {
            var t = n / (N - 1f);                                   // 0 flash .. 1 last of the smoke
            var grow = 0.35f + 0.65f * MathF.Sqrt(t);               // the ball swells fast, then slows
            var hot = 1f - t;                                       // how much is still burning
            frames[n] = Bake(gd, S, S, (x, y) =>
            {
                float u = (x + 0.5f - S / 2f) / (S / 2f), v = (y + 0.5f - S / 2f) / (S / 2f);
                var inside = 0f;
                foreach (var (bx, by, br) in blobs)
                {
                    float dx = u - bx * grow, dy = v - by * grow, r = br * grow;
                    inside = MathF.Max(inside, 1f - MathF.Sqrt(dx * dx + dy * dy) / r);
                }
                if (inside <= 0f) return Color.Transparent;
                // Hot in the middle of the ball, cooling to smoke at its edge; the hot part shrinks as it burns out.
                var heat = inside * 1.6f * hot - 0.15f * t;
                var a = MathHelper.Clamp(1.2f - t * 0.9f, 0f, 1f);
                Color c = heat > 0.85f ? new Color(255, 250, 225) : heat > 0.6f ? new Color(255, 220, 90)
                        : heat > 0.38f ? new Color(255, 150, 40) : heat > 0.18f ? new Color(210, 70, 25)
                        : new Color(60, 52, 50);
                return new Color((int)(c.R * a), (int)(c.G * a), (int)(c.B * a), (int)(255 * a));
            });
        }
        return frames;
    }

    /// <summary>Stand-in smoke: lumpy round puffs, dark grey with a lighter top.</summary>
    private static Texture2D[] MakeSmoke(GraphicsDevice gd)
    {
        const int S = 32;
        var frames = new Texture2D[4];
        for (var n = 0; n < frames.Length; n++)
        {
            var rng = new Random(31 + n);
            var blobs = new (float x, float y, float r)[6];
            for (var i = 0; i < blobs.Length; i++)
                blobs[i] = (S / 2f + ((float)rng.NextDouble() - 0.5f) * S * 0.4f, S / 2f + ((float)rng.NextDouble() - 0.5f) * S * 0.4f,
                    S * (0.16f + (float)rng.NextDouble() * 0.12f));
            frames[n] = Bake(gd, S, S, (x, y) =>
            {
                var inside = 0f;
                var top = 0f;
                foreach (var (bx, by, br) in blobs)
                {
                    var d = MathF.Sqrt((x + 0.5f - bx) * (x + 0.5f - bx) + (y + 0.5f - by) * (y + 0.5f - by)) / br;
                    if (d < 1f) { inside = 1f; top = MathF.Max(top, (by - y) / br); }
                }
                if (inside == 0f) return Color.Transparent;
                var g = top > 0.45f ? 0.42f : top > 0f ? 0.3f : 0.2f;
                return new Color(g, g, g * 1.04f, 1f);
            });
        }
        return frames;
    }
}
