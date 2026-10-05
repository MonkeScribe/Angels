using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Angels_Proj;

/// <summary>
/// The player's Spitfire, drawn from the pixel art in Content/Sprites (256x256, nose up). The art has no
/// propeller: it is drawn here, seen exactly edge-on from above, so the whole propeller is a line along the span.
/// Each blade reaches its radius times the cosine of its angle round the hub, so as it turns its yellow tip
/// slides out along the line and back in. A blade on the upper half of its turn faces the sky and is drawn lit and
/// over the spinner; on the lower half it is in shadow and passes under the spinner. The propeller is turned at a
/// rate the eye can follow rather than its real one, which would only alias into a bar that looks still.
///
/// Firing: spitfire_firing.png is the peak of a shot. At load it is split from the plain sprite into the
/// muzzle flames (pixels outside the airframe) and the light they throw on the wings (airframe pixels that
/// brighten noticeably). Each shot flares one wing's guns to the peak and lets them die away over a few ticks,
/// with every flame a slightly different length so no two shots look alike.
/// </summary>
public sealed class Spitfire
{
    public const int Size = 256;
    public static readonly Vector2 Origin = new(128f, 128f);

    /// <summary>Screen px of sprite per unit of the old 96 px placeholder scale: wingspan 204 px -> 92 px.</summary>
    public const float ArtScale = 92f / 204f;

    // Propeller (sprite px). Spitfire IX: four blades, 10 ft 9 in across on a 36 ft 10 in span.
    private const int Blades = 4;
    private static readonly Vector2 Hub = new(127.5f, 57f);
    private const float SpinnerR = 5.5f, BladeR = 30f, BladeWidth = 2f, TipFrac = 0.12f;
    private static readonly Color BladeColor = new(32, 30, 28), BladeLit = new(150, 144, 132);
    private static readonly Color TipColor = new(232, 196, 58);   // RAF yellow tips
    private static readonly Color BlurColor = new(170, 166, 156); // the faint line the blades sweep
    private const float IdleSpin = 0.2f, FullSpin = 0.55f;        // rad per tick; under pi/4, so it never seems to run backwards
    private const float BlurMax = 0.3f;
    private const int SmearSamples = 4;
    private const float Shutter = 0.6f;               // fraction of each frame's rotation smeared into the image

    // Firing.
    private const int GlowThreshold = 60;             // summed RGB change for an airframe pixel to count as lit
    private const float FlashDecay = 0.5f, GlowDecay = 0.7f;

    private sealed class Flame
    {
        public Rectangle Src;
        public Vector2 Base;                           // the muzzle, in sprite px: flames grow away from it
        public int Side;                               // 0 left wing, 1 right
        public float Length = 1f, Width = 1f;
    }

    private readonly Texture2D _pixel, _body, _flames;
    private readonly Texture2D[] _glow = new Texture2D[2];
    private readonly List<Flame> _flameList = new();
    private readonly float[] _flash = new float[2], _glowLevel = new float[2];
    private float _propAngle, _propRate;              // rad, rad per tick

    public Texture2D Body => _body;

    public Spitfire(GraphicsDevice gd, Texture2D pixel)
    {
        _pixel = pixel;
        var plain = Load(gd, "Content/Sprites/spitfire.png");
        var firing = Load(gd, "Content/Sprites/spitfire_firing.png");

        var flames = new Color[Size * Size];
        var glow = new[] { new Color[Size * Size], new Color[Size * Size] };
        var muzzle = new bool[Size * Size];
        for (var i = 0; i < plain.Length; i++)
        {
            Color a = plain[i], b = firing[i];
            if (b.A == 0 || a == b) continue;
            if (a.A == 0) { flames[i] = b; muzzle[i] = true; continue; }
            if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) >= GlowThreshold)
                glow[i % Size < Size / 2 ? 0 : 1][i] = b;
        }
        _body = MakeTexture(gd, plain);
        _flames = MakeTexture(gd, flames);
        _glow[0] = MakeTexture(gd, glow[0]);
        _glow[1] = MakeTexture(gd, glow[1]);
        FindFlames(muzzle);
    }

    private static Color[] Load(GraphicsDevice gd, string path)
    {
        using var stream = TitleContainer.OpenStream(path);
        using var tex = Texture2D.FromStream(gd, stream);
        if (tex.Width != Size || tex.Height != Size)
            throw new InvalidOperationException($"{path} must be {Size}x{Size}, not {tex.Width}x{tex.Height}");
        var px = new Color[Size * Size];
        tex.GetData(px);
        for (var i = 0; i < px.Length; i++) // SpriteBatch blends premultiplied colour
            px[i] = px[i].A == 0 ? Color.Transparent : Color.FromNonPremultiplied(px[i].R, px[i].G, px[i].B, px[i].A);
        return px;
    }

    /// <summary>A mipmapped texture, so the 256 px art stays clean when drawn at a fraction of its size.</summary>
    private static Texture2D MakeTexture(GraphicsDevice gd, Color[] px)
    {
        var tex = new Texture2D(gd, Size, Size, true, SurfaceFormat.Color);
        int n = Size, level = 0;
        var cur = px;
        while (true)
        {
            tex.SetData(level, null, cur, 0, cur.Length);
            if (n == 1 || ++level >= tex.LevelCount) break;
            var m = n / 2;
            var next = new Color[m * m];
            for (var y = 0; y < m; y++)
                for (var x = 0; x < m; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (var k = 0; k < 4; k++)
                    {
                        var c = cur[(2 * y + k / 2) * n + 2 * x + k % 2];
                        r += c.R; g += c.G; b += c.B; a += c.A;
                    }
                    next[y * m + x] = new Color(r / 4, g / 4, b / 4, a / 4);
                }
            cur = next;
            n = m;
        }
        return tex;
    }

    /// <summary>Groups the flame pixels into one flame per gun; stray sparks join the nearest flame.</summary>
    private void FindFlames(bool[] muzzle)
    {
        var seen = new bool[muzzle.Length];
        var blobs = new List<List<Point>>();
        for (var i = 0; i < muzzle.Length; i++)
        {
            if (!muzzle[i] || seen[i]) continue;
            var blob = new List<Point>();
            var stack = new Stack<int>();
            stack.Push(i);
            seen[i] = true;
            while (stack.Count > 0)
            {
                var j = stack.Pop();
                int x = j % Size, y = j / Size;
                blob.Add(new Point(x, y));
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= Size || ny >= Size) continue;
                        var k = ny * Size + nx;
                        if (muzzle[k] && !seen[k]) { seen[k] = true; stack.Push(k); }
                    }
            }
            blobs.Add(blob);
        }
        blobs.Sort((a, b) => b.Count.CompareTo(a.Count));
        var main = blobs.FindAll(b => b.Count >= 20);
        if (main.Count == 0) return;
        var groups = main.ConvertAll(b => new List<Point>(b));
        foreach (var spark in blobs.FindAll(b => b.Count < 20))
        {
            var best = 0;
            var bestD = float.MaxValue;
            for (var g = 0; g < main.Count; g++)
            {
                var d = Vector2.DistanceSquared(Centre(main[g]), Centre(spark));
                if (d < bestD) { bestD = d; best = g; }
            }
            groups[best].AddRange(spark);
        }
        foreach (var g in groups)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = 0, y1 = 0;
            foreach (var p in g) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
            var c = Centre(g);
            _flameList.Add(new Flame
            {
                Src = new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1),
                Base = new Vector2(c.X, y1 + 1f), // flames point forward, so the muzzle is at the bottom
                Side = c.X < Size / 2f ? 0 : 1,
            });
        }
    }

    private static Vector2 Centre(List<Point> pts)
    {
        var s = Vector2.Zero;
        foreach (var p in pts) s += new Vector2(p.X + 0.5f, p.Y + 0.5f);
        return s / pts.Count;
    }

    /// <summary>A round leaves the guns on one wing (0 left, 1 right).</summary>
    public void Shot(int side, Random rng)
    {
        _flash[side] = 1f;
        _glowLevel[side] = 1f;
        foreach (var f in _flameList)
            if (f.Side == side)
            {
                f.Length = 0.7f + (float)rng.NextDouble() * 0.5f;
                f.Width = 0.85f + (float)rng.NextDouble() * 0.3f;
            }
    }

    /// <summary>One tick. The propeller follows the throttle and winds down when the engine is dead.</summary>
    public void Update(float throttle, bool engineRunning)
    {
        var target = engineRunning ? IdleSpin + throttle * (FullSpin - IdleSpin) : 0f;
        _propRate += (target - _propRate) * (engineRunning ? 0.05f : 0.02f);
        // Clockwise as seen from the cockpit: over the top towards the right wing.
        _propAngle = MathHelper.WrapAngle(_propAngle - _propRate);
        for (var s = 0; s < 2; s++)
        {
            _flash[s] *= FlashDecay;
            _glowLevel[s] *= GlowDecay;
        }
    }

    /// <summary>Draws the plane at a screen position. scale is per sprite axis (x across the wings, y along the
    /// fuselage).</summary>
    public void Draw(SpriteBatch sb, Vector2 pos, float heading, Vector2 scale, Color tint)
    {
        sb.Draw(_body, pos, null, tint, heading, Origin, scale, SpriteEffects.None, 0f);
        for (var s = 0; s < 2; s++)
            if (_glowLevel[s] > 0.01f)
                sb.Draw(_glow[s], pos, null, Color.White * _glowLevel[s], heading, Origin, scale, SpriteEffects.None, 0f);
        DrawPropeller(sb, pos, heading, scale);
        DrawFlames(sb, pos, heading, scale);
    }

    /// <summary>Just the silhouette, for the shadow on the ground.</summary>
    public void DrawShadow(SpriteBatch sb, Vector2 pos, float heading, Vector2 scale, Color color) =>
        sb.Draw(_body, pos, null, color, heading, Origin, scale, SpriteEffects.None, 0f);

    private Vector2 ToScreen(Vector2 sprite, Vector2 pos, float heading, Vector2 scale)
    {
        var v = (sprite - Origin) * scale;
        float c = MathF.Cos(heading), s = MathF.Sin(heading);
        return pos + new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    private void DrawPropeller(SpriteBatch sb, Vector2 pos, float heading, Vector2 scale)
    {
        var width = BladeWidth * scale.Y;
        var blur = MathHelper.Clamp(_propRate / FullSpin, 0f, 1f) * BlurMax;
        if (blur > 0.01f)
            for (var side = -1; side <= 1; side += 2)
                Segment(sb, pos, heading, scale, Hub + new Vector2(side * SpinnerR, 0f), Hub + new Vector2(side * BladeR, 0f), width,
                    BlurColor * blur);

        // Each sample is the four blades at one instant within the frame's exposure, trailing back along the turn.
        var alpha = MathHelper.Clamp(1.6f / SmearSamples, 0f, 1f);
        for (var k = 0; k < SmearSamples; k++)
        {
            var a0 = _propAngle + _propRate * Shutter * k / SmearSamples;
            for (var b = 0; b < Blades; b++)
            {
                var a = a0 + b * MathF.Tau / Blades;
                float reach = MathF.Cos(a) * BladeR, up = MathF.Sin(a);
                var start = up > 0f ? 0f : SpinnerR;
                var length = MathF.Abs(reach);
                if (length <= start) continue; // hidden under the spinner
                var dir = new Vector2(MathF.Sign(reach), 0f);
                var tipStart = MathF.Max(start, length * (1f - TipFrac));
                var shade = Color.Lerp(BladeColor, BladeLit, (up + 1f) / 2f);
                Segment(sb, pos, heading, scale, Hub + dir * start, Hub + dir * tipStart, width, shade * alpha);
                Segment(sb, pos, heading, scale, Hub + dir * tipStart, Hub + dir * length, width, TipColor * alpha);
            }
        }
    }

    private void Segment(SpriteBatch sb, Vector2 pos, float heading, Vector2 scale, Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 p = ToScreen(a, pos, heading, scale), q = ToScreen(b, pos, heading, scale);
        var d = q - p;
        var len = d.Length();
        if (len < 0.25f) return;
        sb.Draw(_pixel, (p + q) / 2f, null, color, MathF.Atan2(d.Y, d.X), new Vector2(0.5f, 0.5f), new Vector2(len, MathF.Max(1f, width)),
            SpriteEffects.None, 0f);
    }

    private void DrawFlames(SpriteBatch sb, Vector2 pos, float heading, Vector2 scale)
    {
        foreach (var f in _flameList)
        {
            var k = _flash[f.Side];
            if (k < 0.04f) continue;
            // Flames shrink back toward the muzzle and fade as the shot dies.
            var grow = 0.45f + 0.55f * k;
            var origin = new Vector2(f.Base.X - f.Src.X, f.Src.Height);
            sb.Draw(_flames, ToScreen(f.Base, pos, heading, scale), f.Src, Color.White * MathF.Min(1f, k * 1.4f), heading, origin,
                scale * new Vector2(f.Width * (0.7f + 0.3f * k), f.Length * grow), SpriteEffects.None, 0f);
        }
    }
}
