using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Angels_Proj;

/// <summary>
/// Draws any aircraft on the map, the player's and everyone else's alike, from what the aircraft carries: its sprite
/// sphere picture for how it is turned, each of its propellers on its hub, and its engine fire. A twin or a bomber is
/// drawn the same way with more propellers; nothing here is particular to one type.
/// </summary>
public sealed class AircraftArt
{
    private readonly Texture2D _pixel;
    private readonly EffectArt _effects;

    // Propeller drawing.
    private const float BladeWidthPx = 2.3f;                       // in sphere px
    private const float TipFrac = 0.12f;                           // the yellow tip's share of the blade
    private static readonly Color BladeColor = new(12, 11, 10), BladeLit = new(46, 44, 41);
    private static readonly Color TipColor = new(232, 196, 58);   // RAF yellow tips
    private static readonly Color BlurColor = new(24, 23, 22);    // the faint line the blades sweep
    private const float BlurMax = 0.3f;
    private const int SmearSamples = 4;
    private const float Shutter = 0.6f;                            // fraction of each frame's rotation smeared into the image

    public AircraftArt(Texture2D pixel, EffectArt effects) { _pixel = pixel; _effects = effects; }

    /// <summary>The aircraft's sphere view for the map, which looks straight down with north at the top: its heading,
    /// pitch and bank all come out of which picture it is and how it is turned.</summary>
    public static SpriteSphere.View MapView(Aircraft a)
    {
        World.Basis(a.Heading, a.Pitch, a.Bank, out var r, out var u, out var f);
        return a.Sphere.Pick(Vector3.UnitY, -Vector3.UnitZ, f, r, u);
    }

    /// <summary>Just the silhouette, for the shadow on the ground. scale is screen px per sphere px.</summary>
    public void DrawShadow(SpriteBatch sb, Aircraft a, SpriteSphere.View view, Vector2 pos, float scale, Color color)
    {
        var flip = view.Flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        sb.Draw(a.Sphere.Sheet, pos, view.Src, color, view.Roll, new Vector2(SpriteSphere.Frame / 2f), scale, flip, 0f);
    }

    /// <summary>The aircraft at pos (screen), scale screen px per sphere px, faded by alpha: its propellers behind the
    /// airframe when the nose points away from us and in front of it otherwise, then any engine fire.</summary>
    public void Draw(SpriteBatch sb, Aircraft a, SpriteSphere.View view, Vector2 pos, float scale, float alpha, float time)
    {
        World.Basis(a.Heading, a.Pitch, a.Bank, out _, out _, out var f);
        var propsBehind = f.Y < -0.1f;                              // the map looks down: a nose pointing down is away from us
        if (propsBehind) foreach (var p in a.Propellers) DrawPropeller(sb, p, view, pos, scale, alpha);
        var flip = view.Flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        sb.Draw(a.Sphere.Sheet, pos, view.Src, Color.White * alpha, view.Roll, new Vector2(SpriteSphere.Frame / 2f), scale, flip, 0f);
        if (!propsBehind) foreach (var p in a.Propellers) DrawPropeller(sb, p, view, pos, scale, alpha);
        if (a.OnFire)
            foreach (var at in a.FirePoints) DrawFire(sb, at, view, pos, scale, a.FireStrength * alpha, a.Salt, time);
    }

    /// <summary>A propeller: its disc placed in the aircraft's own frame and seen from wherever the picture is taken, so it
    /// is a line edge-on, an ellipse as the nose turns toward or away from us, and tips with the bank. Each blade reaches
    /// its tip by the cosine of its angle round the hub, so seen edge-on the yellow tips slide out along the line and
    /// back; a blade on the upper half of its turn faces the sky and is drawn lit and over the spinner.</summary>
    private void DrawPropeller(SpriteBatch sb, Propeller prop, SpriteSphere.View view, Vector2 pos, float scale, float alpha)
    {
        var spec = prop.Spec;
        var hub = pos + view.Project(spec.Hub) * scale;
        var spinnerPx = spec.SpinnerFt * SpriteSphere.PxPerFt;
        var width = MathF.Max(1f, BladeWidthPx * scale);
        // A point on the disc at angle a (0 = toward the right wing, 90 degrees = straight up), as sphere px from the hub.
        Vector2 Disc(float a, float ft) => view.Project(new Vector3(0f, -MathF.Cos(a), MathF.Sin(a)) * ft);
        void Line(Vector2 p0, Vector2 p1, Color color)
        {
            var d = p1 - p0;
            var len = d.Length();
            if (len < 0.25f) return;
            sb.Draw(_pixel, (p0 + p1) / 2f, null, color * alpha, MathF.Atan2(d.Y, d.X), new Vector2(0.5f, 0.5f), new Vector2(len, width),
                SpriteEffects.None, 0f);
        }

        var blur = MathHelper.Clamp(prop.Rate / Propeller.FullSpin, 0f, 1f) * BlurMax;
        if (blur > 0.01f)
            for (var side = 0; side < 2; side++)
            {
                var reach = Disc(side * MathF.PI, spec.RadiusFt);
                var len = reach.Length();
                if (len <= spinnerPx) continue;
                var dir = reach / len;
                Line(hub + dir * spinnerPx * scale, hub + reach * scale, BlurColor * blur);
            }

        // Each sample is the blades at one instant within the frame's exposure, trailing back along the turn.
        var sampleAlpha = MathHelper.Clamp(1.6f / SmearSamples, 0f, 1f);
        for (var k = 0; k < SmearSamples; k++)
        {
            var a0 = prop.Angle + prop.Rate * spec.Turn * Shutter * k / SmearSamples;
            for (var b = 0; b < spec.Blades; b++)
            {
                var a = a0 + b * MathF.Tau / spec.Blades;
                var up = MathF.Sin(a);
                var reach = Disc(a, spec.RadiusFt);
                var length = reach.Length();
                var start = up > 0f ? 0f : spinnerPx;
                if (length <= start) continue;               // hidden under the spinner
                var dir = reach / length;
                var tipStart = MathF.Max(start, length * (1f - TipFrac));
                var shade = Color.Lerp(BladeColor, BladeLit, (up + 1f) / 2f);
                Line(hub + dir * start * scale, hub + dir * tipStart * scale, shade * sampleAlpha);
                Line(hub + dir * tipStart * scale, hub + dir * length * scale, TipColor * sampleAlpha);
            }
        }
    }

    /// <summary>An engine fire: the animation drawn at a fire point, turned so the flames stream back along the aircraft
    /// (shorter as it points toward or away from us) and longer the fiercer the fire.</summary>
    private void DrawFire(SpriteBatch sb, Vector3 point, SpriteSphere.View view, Vector2 pos, float scale, float strength, int salt, float time)
    {
        if (strength <= 0f) return;
        var at = pos + view.Project(point) * scale;
        var back = view.Project(new Vector3(-1f, 0f, 0f));               // sphere px per foot toward the tail, on screen
        var len = back.Length() / SpriteSphere.PxPerFt;                   // 1 side-on, 0 end-on
        var rot = len > 0.05f ? MathF.Atan2(back.X, -back.Y) : 0f;
        var frame = _effects.FireFrame(time, salt);
        var lengthFt = (5f + 13f * strength) * MathF.Max(0.35f, len);
        var widthFt = 3f + 5f * strength;
        var ftPx = scale * SpriteSphere.PxPerFt;                          // screen px per foot
        var sc = new Vector2(widthFt * ftPx / (frame.Width * EffectArt.FireWidthFrac), lengthFt * ftPx / (frame.Height * EffectArt.FireLengthFrac));
        // A little of the colour is added rather than laid over, so the flames glow against what's behind them.
        sb.Draw(frame, at, null, new Color(255, 255, 255, 215), rot, EffectArt.FireOrigin(frame), sc, SpriteEffects.None, 0f);
    }
}
