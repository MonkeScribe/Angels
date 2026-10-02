using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Angels_Proj;

/// <summary>
/// The RAF "Basic Six" blind-flying panel, as fitted to WW2 Spitfires, in two clusters of three:
///   left:  air speed indicator, artificial horizon, rate of climb      (the panel's top row)
///   right: altimeter, directional gyro, turn and slip                  (the panel's bottom row)
/// Faces are baked once into textures (black dials, white markings); needles and cards are drawn live.
/// </summary>
public sealed class Instruments
{
    private const int N = 256;                 // baked face size
    private const float C = (N - 1) / 2f;      // face centre
    private const float FaceR = 112f;          // radius inside the bezel

    private readonly Texture2D _pixel;
    private readonly Texture2D _bezel, _asi, _vsi, _alt, _dgCard, _turn, _ball;
    private readonly Horizon _horizon;

    // Instruments lag a little like real ones; this smooths the raw feeds.
    private float _vsi_fpm, _turnRate, _slip;

    private static readonly Color Cream = new(240, 238, 222);
    private static readonly Color Red = new(230, 60, 50);

    public Instruments(GraphicsDevice gd, Texture2D pixel)
    {
        _pixel = pixel;
        _horizon = new Horizon(gd);
        _bezel = MakeBezel(gd);
        _asi = MakeAsi(gd);
        _vsi = MakeVsi(gd);
        _alt = MakeAltimeter(gd);
        _dgCard = MakeDgCard(gd);
        _turn = MakeTurnAndSlip(gd);
        _ball = MakeBall(gd);
    }

    // ---------------------------------------------------------------- baking helpers

    private sealed class Canvas
    {
        public readonly Color[] Px = new Color[N * N];

        public void Face(Color col, float radius = FaceR)
        {
            for (var y = 0; y < N; y++)
                for (var x = 0; x < N; x++)
                {
                    var d = MathF.Sqrt((x - C) * (x - C) + (y - C) * (y - C));
                    Px[y * N + x] = d <= radius ? col : Color.Transparent;
                }
        }

        private void Blend(int x, int y, Color col, float cov)
        {
            if (x < 0 || y < 0 || x >= N || y >= N || cov <= 0f) return;
            var dst = Px[y * N + x];
            if (dst.A == 0) return; // stay inside the face
            Px[y * N + x] = Color.Lerp(dst, col, Math.Min(1f, cov));
        }

        public void Line(float x0, float y0, float x1, float y1, float thick, Color col)
        {
            float dx = x1 - x0, dy = y1 - y0, len2 = dx * dx + dy * dy;
            int minX = (int)MathF.Floor(MathF.Min(x0, x1) - thick - 1), maxX = (int)MathF.Ceiling(MathF.Max(x0, x1) + thick + 1);
            int minY = (int)MathF.Floor(MathF.Min(y0, y1) - thick - 1), maxY = (int)MathF.Ceiling(MathF.Max(y0, y1) + thick + 1);
            for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                {
                    var t = len2 > 0 ? Math.Clamp(((x - x0) * dx + (y - y0) * dy) / len2, 0f, 1f) : 0f;
                    float px = x0 + t * dx - x, py = y0 + t * dy - y;
                    var d = MathF.Sqrt(px * px + py * py);
                    Blend(x, y, col, thick / 2f + 0.5f - d);
                }
        }

        /// <summary>Radial tick at angle theta (deg clockwise from up), from radius r0 to r1, about (cx, cy).</summary>
        public void Tick(float cx, float cy, float deg, float r0, float r1, float thick, Color col)
        {
            var a = MathHelper.ToRadians(deg);
            float s = MathF.Sin(a), c = MathF.Cos(a);
            Line(cx + r0 * s, cy - r0 * c, cx + r1 * s, cy - r1 * c, thick, col);
        }

        /// <summary>Text centred at (cx, cy), optionally rotated by deg (clockwise). Sampled per destination pixel so it stays crisp.</summary>
        public void Text(string text, float cx, float cy, int px, Color col, float deg = 0f)
        {
            int gw = text.Length * 6 - 1, gh = 7;
            var lit = new bool[gw, gh];
            foreach (var (gx, gy) in PixelFont.LitPixels(text)) lit[gx, gy] = true;
            var a = MathHelper.ToRadians(deg);
            float s = MathF.Sin(a), c = MathF.Cos(a);
            var reach = (int)MathF.Ceiling(MathF.Sqrt(gw * gw + gh * gh) * px / 2f) + 1;
            for (var y = (int)cy - reach; y <= (int)cy + reach; y++)
                for (var x = (int)cx - reach; x <= (int)cx + reach; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float lx = dx * c + dy * s, ly = -dx * s + dy * c; // back into the unrotated text frame
                    int gx = (int)MathF.Floor(lx / px + gw / 2f), gy = (int)MathF.Floor(ly / px + gh / 2f);
                    if (gx >= 0 && gy >= 0 && gx < gw && gy < gh && lit[gx, gy]) Blend(x, y, col, 1f);
                }
        }

        public void Disc(float cx, float cy, float r, Color col)
        {
            for (var y = (int)(cy - r - 1); y <= cy + r + 1; y++)
                for (var x = (int)(cx - r - 1); x <= cx + r + 1; x++)
                {
                    var d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    Blend(x, y, col, r + 0.5f - d);
                }
        }

        public Texture2D ToTexture(GraphicsDevice gd)
        {
            var t = new Texture2D(gd, N, N);
            t.SetData(Px);
            return t;
        }
    }

    private static Vector2 Pol(float deg, float r)
    {
        var a = MathHelper.ToRadians(deg);
        return new Vector2(C + r * MathF.Sin(a), C - r * MathF.Cos(a));
    }

    private static Texture2D MakeBezel(GraphicsDevice gd)
    {
        var px = new Color[N * N];
        for (var y = 0; y < N; y++)
            for (var x = 0; x < N; x++)
            {
                var d = MathF.Sqrt((x - C) * (x - C) + (y - C) * (y - C));
                Color col;
                if (d < FaceR || d > 127.5f) col = Color.Transparent;
                else if (d < FaceR + 3f) col = new Color(8, 8, 9);                  // dark inner lip
                else
                {
                    // Brushed-metal rim, lit from the top-left.
                    var lit = 0.5f + 0.5f * (-(x - C) - (y - C)) / (2f * 127f);
                    var v = (int)(60 + 90 * lit);
                    col = new Color(v, v, v + 4);
                    if (d > 126f) { var cov = 127.5f - d; col = new Color((int)(col.R * cov), (int)(col.G * cov), (int)(col.B * cov), (int)(255 * cov)); }
                }
                px[y * N + x] = col;
            }
        var t = new Texture2D(gd, N, N);
        t.SetData(px);
        return t;
    }

    private static readonly Color FaceBlack = new(16, 16, 18);

    // 0..500 mph over 300 degrees from 7 o'clock; red line at the 450 mph never-exceed speed.
    private static float AsiDeg(float mph) => -150f + Math.Clamp(mph, 0f, 520f) * 0.6f;

    private static Texture2D MakeAsi(GraphicsDevice gd)
    {
        var c = new Canvas();
        c.Face(FaceBlack);
        for (var v = 0; v <= 500; v += 10)
            c.Tick(C, C, AsiDeg(v), FaceR - 3, FaceR - (v % 50 == 0 ? 17 : 9), v % 50 == 0 ? 2.6f : 1.4f, Color.White);
        for (var v = 100; v <= 500; v += 100)
        {
            var p = Pol(AsiDeg(v), 80f);
            c.Text(v.ToString(), p.X, p.Y, 3, Color.White);
        }
        c.Tick(C, C, AsiDeg(450), 92, FaceR - 2, 5f, Red);
        c.Text("AIR SPEED", C, C - 30, 2, Color.White);
        c.Text("MPH", C, C + 50, 2, Color.White);
        return c.ToTexture(gd);
    }

    // Rate of climb: zero at 9 o'clock, up clockwise over the top, down anticlockwise underneath. The scale
    // is non-linear (like the real instrument) so small rates are easy to read: 6,000 ft/min is full scale.
    public static float VsiDeg(float fpm)
    {
        var f = MathF.Sqrt(MathF.Min(MathF.Abs(fpm), 6500f) / 6000f);
        return -90f + MathF.Sign(fpm) * 160f * f;
    }

    private static Texture2D MakeVsi(GraphicsDevice gd)
    {
        var c = new Canvas();
        c.Face(FaceBlack);
        foreach (var sign in new[] { 1, -1 })
            foreach (var v in new[] { 500, 1000, 1500, 2000, 3000, 4000, 5000, 6000 })
            {
                var big = v % 1000 == 0 && v != 3000 && v != 5000;
                c.Tick(C, C, VsiDeg(sign * v), FaceR - 3, FaceR - (big ? 17 : 9), big ? 2.6f : 1.4f, Color.White);
                if (big)
                {
                    var p = Pol(VsiDeg(sign * v), 78f);
                    c.Text((v / 1000).ToString(), p.X, p.Y, 3, Color.White);
                }
            }
        c.Tick(C, C, VsiDeg(0), FaceR - 3, FaceR - 19, 3.4f, Color.White);
        c.Text("0", C - 78, C, 3, Color.White);
        c.Text("UP", C, C - 44, 2, Color.White);
        c.Text("DOWN", C, C + 44, 2, Color.White);
        c.Text("X1000", C + 30, C - 12, 2, Color.White);
        c.Text("FT/MIN", C + 30, C + 10, 2, Color.White);
        return c.ToTexture(gd);
    }

    private static Texture2D MakeAltimeter(GraphicsDevice gd)
    {
        var c = new Canvas();
        c.Face(FaceBlack);
        for (var i = 0; i < 50; i++)
            c.Tick(C, C, i * 7.2f, FaceR - 3, FaceR - (i % 5 == 0 ? 17 : 8), i % 5 == 0 ? 2.6f : 1.3f, Color.White);
        for (var i = 0; i < 10; i++)
        {
            var p = Pol(i * 36f, 80f);
            c.Text(i.ToString(), p.X, p.Y, 4, Color.White);
        }
        c.Text("ALT", C, C - 40, 2, Color.White);
        c.Text("FEET", C, C + 42, 2, Color.White);
        return c.ToTexture(gd);
    }

    // Heading card (no bezel; it spins under the fixed one): ticks every 5 degrees, figures every 30.
    private static Texture2D MakeDgCard(GraphicsDevice gd)
    {
        var c = new Canvas();
        c.Face(FaceBlack);
        for (var d = 0; d < 360; d += 5)
            c.Tick(C, C, d, FaceR - 2, FaceR - (d % 10 == 0 ? 15 : 8), d % 30 == 0 ? 2.8f : 1.4f, Color.White);
        for (var d = 0; d < 360; d += 30)
        {
            var label = d switch { 0 => "N", 90 => "E", 180 => "S", 270 => "W", _ => (d / 10).ToString() };
            var p = Pol(d, 86f);
            c.Text(label, p.X, p.Y, label.Length == 1 && d % 90 == 0 ? 5 : 4, d % 90 == 0 ? new Color(255, 220, 120) : Color.White, d);
        }
        return c.ToTexture(gd);
    }

    // Turn and slip: needle marks for left / standard-rate / right, and the ball tube underneath.
    private const float TurnPivotY = C + 18f, TurnNeedleMaxDeg = 40f;

    private static Texture2D MakeTurnAndSlip(GraphicsDevice gd)
    {
        var c = new Canvas();
        c.Face(FaceBlack);
        foreach (var d in new[] { -40f, -20f, 0f, 20f, 40f })
            c.Tick(C, TurnPivotY, d, 80, d == 0f ? 96 : 100, d == 0f ? 2f : 3.2f, d == 0f ? Color.White : new Color(255, 220, 120));
        c.Text("L", C + 82f * MathF.Sin(MathHelper.ToRadians(-48)), TurnPivotY - 82f * MathF.Cos(MathHelper.ToRadians(-48)), 3, Color.White);
        c.Text("R", C + 82f * MathF.Sin(MathHelper.ToRadians(48)), TurnPivotY - 82f * MathF.Cos(MathHelper.ToRadians(48)), 3, Color.White);
        c.Text("2 MIN", C, C - 58, 2, Color.White);
        c.Text("TURN", C, C + 44, 2, Color.White);
        // Ball tube.
        c.Line(C - 40, C + 78, C + 40, C + 78, 24f, new Color(60, 68, 62));
        c.Line(C - 40, C + 78, C + 40, C + 78, 18f, new Color(150, 168, 152));
        c.Line(C - 13, C + 64, C - 13, C + 92, 2f, Color.Black);
        c.Line(C + 13, C + 64, C + 13, C + 92, 2f, Color.Black);
        return c.ToTexture(gd);
    }

    private static Texture2D MakeBall(GraphicsDevice gd)
    {
        const int B = 32;
        var px = new Color[B * B];
        for (var y = 0; y < B; y++)
            for (var x = 0; x < B; x++)
            {
                var d = MathF.Sqrt((x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f));
                var a = Math.Clamp(15.5f - d, 0f, 1f);
                var shine = d < 6f && x < 14 && y < 14 ? 60 : 0;
                px[y * B + x] = new Color((int)((14 + shine) * a), (int)((14 + shine) * a), (int)((16 + shine) * a), (int)(255 * a));
            }
        var t = new Texture2D(gd, B, B);
        t.SetData(px);
        return t;
    }

    // ---------------------------------------------------------------- live drawing

    private void Needle(SpriteBatch sb, Vector2 pivot, float deg, float length, float thick, float tail, Color col, float k)
    {
        var rot = MathHelper.ToRadians(deg) - MathHelper.PiOver2; // a +x pointer rotated to point 'deg' clockwise from up
        var origin = new Vector2(0f, 0.5f);
        sb.Draw(_pixel, pivot, null, col, rot, origin, new Vector2(length * k, Math.Max(1f, thick * k)), SpriteEffects.None, 0f);
        if (tail > 0f)
            sb.Draw(_pixel, pivot, null, col, rot + MathHelper.Pi, origin, new Vector2(tail * k, Math.Max(1f, thick * k * 1.5f)), SpriteEffects.None, 0f);
    }

    private void Hub(SpriteBatch sb, Vector2 centre, float r, float k)
    {
        var d = (int)MathF.Ceiling(r * k * 2f);
        sb.Draw(_pixel, new Rectangle((int)(centre.X - d / 2f), (int)(centre.Y - d / 2f), d, d), new Color(30, 30, 32));
    }

    /// <summary>The free space between the two instrument plates, where the gunsight window goes.</summary>
    public static Rectangle GunsightRect(Rectangle viewport, float scale)
    {
        float d = 124f * scale, gap = 9f * scale, pad = 10f * scale, margin = 12f * scale;
        var plateW = 3 * d + 2 * gap + 2 * pad;
        var plateH = d + 2 * pad;
        var inset = 12f * scale;
        var x = margin + plateW + inset;
        var w = viewport.Width - 2 * (margin + plateW + inset);
        return new Rectangle((int)x, (int)(viewport.Height - margin - plateH), (int)w, (int)plateH);
    }

    /// <summary>Draws both clusters along the bottom of the screen. Needs the sprite batch already begun.</summary>
    public void Draw(SpriteBatch sb, FlightModel fm, Rectangle viewport, float scale)
    {
        // Smooth the feeds (real instruments lag: the VSI notoriously so).
        _vsi_fpm += (fm.VerticalSpeedFpm - _vsi_fpm) * 0.06f;
        _turnRate += (fm.YawRate - _turnRate) * 0.15f;
        _slip += (fm.SlipBall - _slip) * 0.15f;

        var d = 124f * scale;                // dial diameter on screen
        var gap = 9f * scale;
        var pad = 10f * scale;
        var margin = 12f * scale;
        var k = d / N;
        var plateW = 3 * d + 2 * gap + 2 * pad;
        var plateH = d + 2 * pad;
        var plateY = viewport.Height - margin - plateH;
        var leftX = margin;
        var rightX = viewport.Width - margin - plateW;

        DrawPlate(sb, new Rectangle((int)leftX, (int)plateY, (int)plateW, (int)plateH), scale);
        DrawPlate(sb, new Rectangle((int)rightX, (int)plateY, (int)plateW, (int)plateH), scale);

        Vector2 Centre(float plateX, int i) => new(plateX + pad + d / 2f + i * (d + gap), plateY + pad + d / 2f);
        void Face(Texture2D t, Vector2 c, float rot = 0f) =>
            sb.Draw(t, c, null, Color.White, rot, new Vector2(N / 2f, N / 2f), k, SpriteEffects.None, 0f);

        // 1. Air speed indicator.
        var c0 = Centre(leftX, 0);
        Face(_asi, c0);
        Needle(sb, c0, AsiDeg(fm.IasMph), 100f, 4f, 22f, Cream, k);
        Hub(sb, c0, 7f, k); Face(_bezel, c0);

        // 2. Artificial horizon (the same instrument as before, now in its panel slot).
        var c1 = Centre(leftX, 1);
        _horizon.Update(fm.Gamma, fm.Bank);
        sb.Draw(_horizon.Texture, c1, null, Color.White, 0f, new Vector2(Horizon.Size / 2f), d / Horizon.Size, SpriteEffects.None, 0f);

        // 3. Rate of climb.
        var c2 = Centre(leftX, 2);
        Face(_vsi, c2);
        Needle(sb, c2, VsiDeg(_vsi_fpm), 98f, 4f, 22f, Cream, k);
        Hub(sb, c2, 7f, k); Face(_bezel, c2);

        // 4. Altimeter: 100 ft hand (long), 1,000 ft hand (short, broad), 10,000 ft hand (thin, tipped).
        var c3 = Centre(rightX, 0);
        Face(_alt, c3);
        var alt = MathF.Max(0f, fm.Altitude);
        Needle(sb, c3, alt % 10000f / 10000f * 360f, 62f, 8f, 0f, Cream, k);
        Needle(sb, c3, alt / 100000f * 360f, 40f, 4f, 0f, new Color(255, 220, 120), k);
        Needle(sb, c3, alt % 1000f / 1000f * 360f, 100f, 3.4f, 24f, Cream, k);
        Hub(sb, c3, 7f, k); Face(_bezel, c3);

        // 5. Directional gyro: the card turns under a fixed lubber line, with a little aircraft in the middle.
        var c4 = Centre(rightX, 1);
        Face(_dgCard, c4, -fm.Heading);
        void Box(float cx, float cy, float w, float h, Color col) =>
            sb.Draw(_pixel, new Rectangle((int)MathF.Round(c4.X + (cx - w / 2f) * k), (int)MathF.Round(c4.Y + (cy - h / 2f) * k),
                Math.Max(2, (int)MathF.Round(w * k)), Math.Max(2, (int)MathF.Round(h * k))), col);
        Box(0, -100, 5, 24, Red);          // lubber line
        Box(0, 0, 5, 62, Cream);           // fuselage
        Box(0, -10, 70, 6, Cream);         // wings
        Box(0, 24, 28, 5, Cream);          // tailplane
        Face(_bezel, c4);

        // 6. Turn and slip: needle for turn rate, ball for coordination.
        var c5 = Centre(rightX, 2);
        Face(_turn, c5);
        var needleDeg = Math.Clamp(MathHelper.ToDegrees(_turnRate) / 90f * TurnNeedleMaxDeg, -TurnNeedleMaxDeg - 6f, TurnNeedleMaxDeg + 6f);
        var pivot = c5 + new Vector2(0f, (TurnPivotY - C) * k);
        Needle(sb, pivot, needleDeg, 94f, 4f, 18f, Cream, k);
        Hub(sb, pivot, 6f, k);
        var ballX = _slip * 26f;
        var ballY = 78f - ballX * ballX * 0.012f; // rolls up the curved tube
        sb.Draw(_ball, c5 + new Vector2(ballX, ballY) * k, null, Color.White, 0f, new Vector2(16f), k * 0.75f, SpriteEffects.None, 0f);
        Face(_bezel, c5);
    }

    private void DrawPlate(SpriteBatch sb, Rectangle r, float s)
    {
        sb.Draw(_pixel, new Rectangle(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6), new Color(0, 0, 0, 120));
        sb.Draw(_pixel, r, new Color(34, 36, 40, 238));
        sb.Draw(_pixel, new Rectangle(r.X, r.Y, r.Width, Math.Max(1, (int)(2 * s))), new Color(70, 74, 82)); // top edge highlight
        var rv = Math.Max(3, (int)(4 * s));
        foreach (var (x, y) in new[] { (r.X + rv, r.Y + rv), (r.Right - 2 * rv, r.Y + rv), (r.X + rv, r.Bottom - 2 * rv), (r.Right - 2 * rv, r.Bottom - 2 * rv) })
            sb.Draw(_pixel, new Rectangle(x, y, rv, rv), new Color(96, 98, 104)); // rivets
    }
}
