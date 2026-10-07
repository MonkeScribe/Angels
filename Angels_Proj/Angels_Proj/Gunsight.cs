using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Angels_Proj;

/// <summary>
/// The gunsight: a window in the instrument panel showing a real 3D view straight down the nose. Planes are
/// low-poly models in their own colours over a gridded green ground and a blue sky. Nothing is hazed or fogged:
/// only flying into a cloud thins it out. (An optional depth of field, SIGHT BLUR in the debug menu, renders the scene
/// in distance slabs and blurs the further ones.) Your own spinning propeller sits in front
/// as a translucent smear, and the edges are darkened, so the view is deliberately not a clean second screen.
/// The reticle is drawn on top by the overlay.
/// </summary>
public sealed class Gunsight
{
    public struct Tracer { public Vector3 A, B; public int Life; public float Glow; }

    private const float HFovDeg = 18f, SkyRadius = 900_000f; // square view, so 18 degrees each way: the reticle's worth
    private const float GroundRadius = 600_000f;             // the ground disc: well inside the sky dome, out to the horizon

    // Distance slabs, far to near, in feet, and how soft each is (0 sharp, 1..3 progressively blurrier).
    private static readonly (float near, float far, int blur)[] Bands =
    {
        (3500f, 2_000_000f, 3),
        (1800f, 3500f, 2),
        (900f, 1800f, 1),
        (8f, 900f, 0),
    };

    // With the blur off: the same scene in two sharp slabs (two keep the depth buffer precise near and far).
    private static readonly (float near, float far, int blur)[] SharpBands =
    {
        (900f, 2_000_000f, 0),
        (8f, 900f, 0),
    };

    private static readonly Color GroundCol = new(88, 150, 70), GridCol = new(66, 122, 56), HorizonCol = new(205, 226, 240);

    private readonly GraphicsDevice _gd;
    private readonly SpriteBatch _sb;
    private readonly BasicEffect _fx, _fxTex;
    private readonly Texture2D[] _cloudTex;
    private readonly EffectArt _effects;
    private float _time;
    // Camera-facing sprites (planes and clouds), as six vertices each, drawn far to near.
    private readonly List<(float dist, Texture2D tex, int start)> _bills = new();
    private readonly List<VertexPositionColorTexture> _billVerts = new();
    private readonly List<CloudField.Puff> _puffs = new();
    private RenderTarget2D _work, _final, _r2, _r4, _r8;
    private readonly Texture2D _ring, _prop, _disc, _vignette;
    private readonly List<VertexPositionColor> _tris = new(), _lines = new(), _blend = new(), _blendLines = new();
    private float _propAngle;

    public Texture2D Texture => _final;

    // Debug: switch the clouds or the depth-of-field blur off, and what the last frame drew (for the PERF overlay).
    public bool ShowClouds = true, Blur = false;
    public int CloudsDrawn;
    public float CloudFill;      // the clouds' quads added up, in whole views: how many times over they cover the sight

    /// <summary>Extra world-space lines (feet) to draw in the view, with the damage value that picks their colour. For the
    /// debug hit box view; filled by the game each tick.</summary>
    public readonly List<(Vector3 A, Vector3 B, Color Color)> DebugLines = new();

    /// <summary>Would something at this offset from the camera be inside the sight's field of view and range?</summary>
    public static bool Sees(Vector3 rel, Vector3 right, Vector3 up, Vector3 forward, float aspect, float range)
    {
        var d = Vector3.Dot(rel, forward);
        if (d < 20f || rel.LengthSquared() > range * range) return false;
        var tanH = MathF.Tan(MathHelper.ToRadians(HFovDeg) / 2f);
        return MathF.Abs(Vector3.Dot(rel, right) / d) < tanH && MathF.Abs(Vector3.Dot(rel, up) / d) < tanH / aspect;
    }

    public Gunsight(GraphicsDevice gd, SpriteBatch sb, EffectArt effects)
    {
        _gd = gd;
        _sb = sb;
        _effects = effects;
        _fx = new BasicEffect(gd) { VertexColorEnabled = true, LightingEnabled = false, FogEnabled = false };
        _fxTex = new BasicEffect(gd) { VertexColorEnabled = true, TextureEnabled = true, LightingEnabled = false, FogEnabled = false };
        _cloudTex = new[] { Art.Cloud(gd, 11), Art.Cloud(gd, 23), Art.Cloud(gd, 37) };

        _ring = Bake(gd, 256, 256, (x, y) =>
        {
            var d = MathF.Sqrt((x - 127.5f) * (x - 127.5f) + (y - 127.5f) * (y - 127.5f));
            var a = Math.Clamp(127f - d, 0f, 1f) * Math.Clamp(d - 123f, 0f, 1f);
            return new Color(a, a, a, a);
        });
        _prop = BakeProp(gd);
        _disc = Bake(gd, 256, 256, (x, y) =>
        {
            var d = MathF.Sqrt((x - 127.5f) * (x - 127.5f) + (y - 127.5f) * (y - 127.5f)) / 127.5f;
            var a = Math.Clamp((1f - d) * 6f, 0f, 1f) * Math.Clamp((d - 0.22f) * 5f, 0f, 1f) * 0.9f;
            return new Color(0.1f * a, 0.1f * a, 0.11f * a, a); // premultiplied dark disc
        });
        _vignette = Bake(gd, 256, 128, (x, y) =>
        {
            float dx = (x - 127.5f) / 127.5f, dy = (y - 63.5f) / 63.5f;
            var d = MathF.Sqrt(dx * dx * 0.55f + dy * dy * 0.55f);
            var a = Math.Clamp((d - 0.55f) / 0.55f, 0f, 1f);
            a = a * a * 0.6f;
            return new Color(0f, 0f, 0f, a);
        });
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

    /// <summary>Three-bladed propeller, 512x512, blade tips painted yellow as on the real thing; soft-edged so it reads as a blur.</summary>
    private static Texture2D BakeProp(GraphicsDevice gd) => Bake(gd, 512, 512, (x, y) =>
    {
        float dx = x - 255.5f, dy = y - 255.5f;
        var r = MathF.Sqrt(dx * dx + dy * dy);
        var theta = MathF.Atan2(dx, -dy);
        if (r < 40f)
        {
            var a = Math.Clamp(40f - r, 0f, 1f);
            var v = 0.18f + 0.18f * (1f - r / 40f);
            return new Color(v * a, v * a, (v + 0.02f) * a, a); // spinner
        }
        var cover = 0f;
        for (var k = 0; k < 3; k++)
        {
            var d = MathHelper.WrapAngle(theta - k * MathHelper.TwoPi / 3f);
            var lateral = MathF.Abs(d) * r;
            var half = MathHelper.Lerp(34f, 15f, Math.Clamp((r - 40f) / 215f, 0f, 1f));
            var edge = Math.Clamp((half - lateral) / 9f, 0f, 1f);
            cover = MathF.Max(cover, edge);
        }
        cover *= Math.Clamp((r - 40f) / 10f, 0f, 1f) * Math.Clamp((254f - r) / 8f, 0f, 1f);
        if (cover <= 0f) return Color.Transparent;
        var tip = r > 222f;
        var col = tip ? new Vector3(0.94f, 0.78f, 0.15f) : new Vector3(0.12f, 0.12f, 0.13f);
        return new Color(col.X * cover, col.Y * cover, col.Z * cover, cover);
    });

    // ------------------------------------------------------------------ rendering

    private static RenderTarget2D Target(GraphicsDevice gd, int w, int h, bool msaa) =>
        new(gd, Math.Max(2, w), Math.Max(2, h), false, SurfaceFormat.Color, msaa ? DepthFormat.Depth24 : DepthFormat.None,
            msaa ? 4 : 0, RenderTargetUsage.PreserveContents);

    private void EnsureTargets(int w, int h)
    {
        w = Math.Max(16, w); h = Math.Max(16, h);
        if (_final != null && _final.Width == w && _final.Height == h) return;
        _work?.Dispose(); _final?.Dispose(); _r2?.Dispose(); _r4?.Dispose(); _r8?.Dispose();
        _work = Target(_gd, w, h, true);
        _final = Target(_gd, w, h, false);
        _r2 = Target(_gd, w / 2, h / 2, false);
        _r4 = Target(_gd, w / 4, h / 4, false);
        _r8 = Target(_gd, w / 8, h / 8, false);
    }

    private static VertexPositionColor Vtx(Vector3 p, Color c) => new(p, c);

    private static Color Premul(Vector3 rgb, float a) => new(rgb.X * a, rgb.Y * a, rgb.Z * a, a);

    public void Render(int width, int height, Vector3 camPos, float heading, float pitch, float bank,
        IReadOnlyList<Aircraft> planes, Fx fx, List<Tracer> tracers, float throttle)
    {
        EnsureTargets(width, height);
        World.Basis(heading, pitch, bank, out var right, out var up, out var fwd);

        var alt = MathF.Max(camPos.Y, 1f);
        _fx.World = Matrix.Identity;
        _fx.View = Matrix.CreateLookAt(Vector3.Zero, fwd, up);
        var aspect = (float)_final.Width / _final.Height;
        var vfov = 2f * MathF.Atan(MathF.Tan(MathHelper.ToRadians(HFovDeg) / 2f) / aspect);

        _fxTex.World = Matrix.Identity;
        _fxTex.View = _fx.View;
        BuildGeometry(camPos, alt, fwd, planes, fx, tracers);
        var batches = Batches();
        var tris = _tris.ToArray(); var lines = _lines.ToArray();
        var blend = _blend.ToArray(); var blendLines = _blendLines.ToArray();

        // Render the scene one distance slab at a time, far to near, softening each slab by its blur level
        // and laying it over the last. A slab's near/far planes clip the geometry, so the ground (and anything
        // else long) is automatically split into sharp-near and soft-far parts.
        _gd.SetRenderTarget(_final);
        _gd.Clear(Color.Transparent);
        foreach (var (near, far, blur) in Blur ? Bands : SharpBands)
        {
            _gd.SetRenderTarget(_work);
            _gd.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);
            _gd.DepthStencilState = DepthStencilState.Default;
            _gd.RasterizerState = RasterizerState.CullNone;
            _gd.BlendState = BlendState.Opaque;
            _fx.Projection = Matrix.CreatePerspectiveFieldOfView(vfov, aspect, near, far);
            _fx.CurrentTechnique.Passes[0].Apply();
            Draw(tris, PrimitiveType.TriangleList, 3);
            Draw(lines, PrimitiveType.LineList, 2);
            _gd.BlendState = BlendState.AlphaBlend;
            _gd.DepthStencilState = DepthStencilState.DepthRead;
            _fx.CurrentTechnique.Passes[0].Apply();
            Draw(blend, PrimitiveType.TriangleList, 3);
            Draw(blendLines, PrimitiveType.LineList, 2);
            _fxTex.Projection = _fx.Projection;
            foreach (var (tex, verts) in batches)
            {
                _fxTex.Texture = tex;
                _fxTex.CurrentTechnique.Passes[0].Apply();
                _gd.SamplerStates[0] = SamplerState.LinearClamp;
                _gd.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, verts.Length / 3);
            }

            var src = Soften(_work, Blur ? blur : 0);
            _gd.SetRenderTarget(_final);
            _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
            _sb.Draw(src, new Rectangle(0, 0, _final.Width, _final.Height), Color.White);
            _sb.End();
        }

        // Your own propeller, right in front of the nose: close, so far out of focus, and spinning fast.
        // Drawn into the view so it sits under the vignette. Throttle sets how fast it turns and how solid it looks.
        _time += 1f / 60f;
        _propAngle += (14f + 120f * throttle) / 60f * (0.8f + 0.4f * ((_propAngle * 7f) % 1f));
        var hub = new Vector2(_final.Width / 2f, _final.Height * 1.02f);
        var rad = _final.Height * 0.82f;
        var spinAlpha = MathHelper.Lerp(0.45f, 0.22f, throttle);
        _gd.SetRenderTarget(_final);
        _sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
        _sb.Draw(_disc, hub, null, Color.White * (0.35f * throttle + 0.08f), 0f, new Vector2(128f), rad * 2f / 256f, SpriteEffects.None, 0f);
        var smear = 0.55f * throttle; // blade ghosts spread out along the rotation at high revs
        for (var g = 0; g < 3; g++)
            _sb.Draw(_prop, hub, null, Color.White * (spinAlpha * (1f - 0.25f * g)), _propAngle - g * smear, new Vector2(256f),
                rad / 250f, SpriteEffects.None, 0f);
        _sb.Draw(_vignette, new Rectangle(0, 0, _final.Width, _final.Height), Color.White);
        _sb.End();

        _gd.SetRenderTarget(null);
        _gd.DepthStencilState = DepthStencilState.Default;
        _gd.RasterizerState = RasterizerState.CullCounterClockwise;
        _gd.BlendState = BlendState.Opaque;
    }

    /// <summary>The camera-facing sprites sorted far to near and grouped into runs that share a texture, so they draw
    /// in the right order over each other with as few switches as possible.</summary>
    private List<(Texture2D tex, VertexPositionColorTexture[] verts)> Batches()
    {
        _bills.Sort((a, b) => b.dist.CompareTo(a.dist));
        var batches = new List<(Texture2D, VertexPositionColorTexture[])>();
        var run = new List<VertexPositionColorTexture>();
        Texture2D runTex = null;
        foreach (var (_, tex, start) in _bills)
        {
            if (tex != runTex && run.Count > 0) { batches.Add((runTex, run.ToArray())); run.Clear(); }
            runTex = tex;
            for (var i = 0; i < 6; i++) run.Add(_billVerts[start + i]);
        }
        if (run.Count > 0) batches.Add((runTex, run.ToArray()));
        return batches;
    }

    /// <summary>Adds a camera-facing sprite: the part of the texture in uv (left, top, right, bottom) on a quad of the given
    /// half size about centre, turned clockwise by roll (as seen) and flipped if asked.</summary>
    private void AddBillboard(Texture2D tex, float dist, Vector3 centre, Vector3 rightV, Vector3 upV, float halfW, float halfH,
        float roll, bool flip, Vector4 uv, Color col)
    {
        float cr = MathF.Cos(roll), sr = MathF.Sin(roll);
        float u0 = flip ? uv.Z : uv.X, u1 = flip ? uv.X : uv.Z;
        // Corners as (x, y) with y up, turned clockwise: x' = x cos + y sin, y' = -x sin + y cos.
        VertexPositionColorTexture V(float x, float y, float u, float v)
        {
            float px = (x * cr + y * sr) * halfW, py = (-x * sr + y * cr) * halfH;
            return new VertexPositionColorTexture(centre + rightV * px + upV * py, col, new Vector2(u, v));
        }
        var a = V(-1, -1, u0, uv.W); var b = V(1, -1, u1, uv.W); var d = V(1, 1, u1, uv.Y); var e = V(-1, 1, u0, uv.Y);
        _bills.Add((dist, tex, _billVerts.Count));
        _billVerts.Add(a); _billVerts.Add(b); _billVerts.Add(d);
        _billVerts.Add(a); _billVerts.Add(d); _billVerts.Add(e);
    }

    /// <summary>Blurs a slab by repeatedly halving it with bilinear filtering; level 0 is left sharp.</summary>
    private Texture2D Soften(RenderTarget2D src, int level)
    {
        if (level <= 0) return src;
        var chain = new[] { _r2, _r4, _r8 };
        Texture2D from = src;
        for (var i = 0; i < Math.Min(level, 3); i++)
        {
            _gd.SetRenderTarget(chain[i]);
            _gd.Clear(Color.Transparent);
            _sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp);
            _sb.Draw(from, new Rectangle(0, 0, chain[i].Width, chain[i].Height), Color.White);
            _sb.End();
            from = chain[i];
        }
        return from;
    }

    private void BuildGeometry(Vector3 camPos, float alt, Vector3 fwd, IReadOnlyList<Aircraft> planes, Fx fx, List<Tracer> tracers)
    {
        _tris.Clear(); _lines.Clear(); _blend.Clear(); _blendLines.Clear(); _bills.Clear(); _billVerts.Clear();

        // Sky: a big dome around the camera, graded from pale at the horizon to deep blue overhead, and carried
        // on below the horizon too: past the view box (or above the ground) there is only sky, in every direction.
        var rings = new (float el, Color col)[]
        {
            (-90f, new Color(176, 206, 236)), (-45f, new Color(190, 216, 240)), (-15f, new Color(202, 224, 240)),
            (0f, HorizonCol), (6f, new Color(172, 208, 238)), (16f, new Color(130, 180, 230)),
            (32f, new Color(96, 150, 224)), (58f, new Color(72, 126, 214)), (90f, new Color(56, 106, 200)),
        };
        const int seg = 24;
        Vector3 Dome(float elDeg, int k)
        {
            var el = MathHelper.ToRadians(elDeg); var az = k * MathHelper.TwoPi / seg;
            return new Vector3(MathF.Sin(az) * MathF.Cos(el), MathF.Sin(el), -MathF.Cos(az) * MathF.Cos(el)) * SkyRadius;
        }
        for (var j = 0; j < rings.Length - 1; j++)
            for (var k = 0; k < seg; k++)
            {
                int k2 = (k + 1) % seg;
                Vector3 a = Dome(rings[j].el, k), b = Dome(rings[j].el, k2), c = Dome(rings[j + 1].el, k2), d = Dome(rings[j + 1].el, k);
                _tris.Add(Vtx(a, rings[j].col)); _tris.Add(Vtx(b, rings[j].col)); _tris.Add(Vtx(c, rings[j + 1].col));
                _tris.Add(Vtx(a, rings[j].col)); _tris.Add(Vtx(c, rings[j + 1].col)); _tris.Add(Vtx(d, rings[j + 1].col));
            }

        // Ground: one flat colour out to the horizon at any height, with no haze. (The view box still limits what is
        // drawn on it and in the air: planes, smoke and clouds.)
        const float Box = World.ViewBoxFt;
        {
            const float rg = GroundRadius;
            var fr = new[] { 0f, 0.002f, 0.01f, 0.05f, 0.25f, 1f };   // rings, so no triangle is absurdly long
            const int gseg = 40;
            Vector3 G(float r, int k) { var a = k * MathHelper.TwoPi / gseg; return new Vector3(r * MathF.Cos(a), -alt, r * MathF.Sin(a)); }
            for (var j = 0; j < fr.Length - 1; j++)
                for (var k = 0; k < gseg; k++)
                {
                    float r0 = fr[j] * rg, r1 = fr[j + 1] * rg;
                    Vector3 a = G(r0, k), b = G(r0, (k + 1) % gseg), c = G(r1, (k + 1) % gseg), d = G(r1, k);
                    _tris.Add(Vtx(a, GroundCol)); _tris.Add(Vtx(b, GroundCol)); _tris.Add(Vtx(c, GroundCol));
                    _tris.Add(Vtx(a, GroundCol)); _tris.Add(Vtx(c, GroundCol)); _tris.Add(Vtx(d, GroundCol));
                }

            // Field grid round the point below us, coarser the higher we are. It thins into the plain ground colour toward
            // its own edge (a fixed number of fields out), so it doesn't end in a line.
            var spacing = Math.Clamp(250f * MathF.Pow(2f, MathF.Round(MathF.Log2(Math.Max(alt, 500f) / 1000f))), 250f, 4000f);
            var gr = spacing * 40f;
            void GridLine(float offset, bool alongZ)
            {
                if (MathF.Abs(offset) >= gr) return;
                var half = MathF.Sqrt(gr * gr - offset * offset);
                const int pieces = 8;
                for (var i = 0; i < pieces; i++)
                {
                    float t0 = -half + 2f * half * i / pieces, t1 = -half + 2f * half * (i + 1) / pieces;
                    Vector3 P(float t) => alongZ ? new Vector3(offset, -alt, t) : new Vector3(t, -alt, offset);
                    Color Col(float t) => Color.Lerp(GridCol, GroundCol, World.Smooth(0.5f, 1f, MathF.Sqrt(offset * offset + t * t) / gr));
                    _lines.Add(Vtx(P(t0), Col(t0))); _lines.Add(Vtx(P(t1), Col(t1)));
                }
            }
            for (var gx = MathF.Floor((camPos.X - gr) / spacing) * spacing; gx <= camPos.X + gr; gx += spacing) GridLine(gx - camPos.X, true);
            // Z-lines run along X: their offset is relative to the camera's Z; the along-line coordinate is relative to X.
            for (var gz = MathF.Floor((camPos.Z - gr) / spacing) * spacing; gz <= camPos.Z + gr; gz += spacing) GridLine(gz - camPos.Z, false);
        }

        // The view's own axes in the world, which the camera-facing sprites (planes, smoke, clouds) are laid out along.
        var inv = Matrix.Invert(_fx.View) with { Translation = Vector3.Zero };
        var rightV = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, inv));
        var upV = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, inv));

        // Planes: each is the view of the sprite sphere that looks at it from where we are, laid flat to the view and
        // turned so its wings and fin lie right. No haze: fully solid out to the edge of the box.
        foreach (var p in planes)
        {
            var pos = World.ToFt(p.Pos, p.Altitude) - camPos;
            var dist2 = pos.LengthSquared();
            var depth = Vector3.Dot(pos, fwd);
            if (dist2 > Box * Box || depth < 10f) continue;
            World.Basis(p.Heading, p.Pitch, p.Bank, out var pr, out var pu, out var pf);
            var sphere = p.Sphere;   // each aircraft is drawn from its own type's pictures
            float sheetW = sphere.Sheet.Width, sheetH = sphere.Sheet.Height;
            var view = sphere.Pick(-pos / MathF.Sqrt(dist2), upV, pf, pr, pu);
            const float a = 1f;
            var half = sphere.FrameFt / 2f;
            // Half a texel in from the frame's edge, so neighbouring frames don't bleed in.
            var uv = new Vector4((view.Src.X + 0.5f) / sheetW, (view.Src.Y + 0.5f) / sheetH,
                (view.Src.Right - 0.5f) / sheetW, (view.Src.Bottom - 0.5f) / sheetH);
            AddBillboard(sphere.Sheet, MathF.Sqrt(dist2), pos, rightV, upV, half, half, view.Roll, view.Flip, uv, new Color(a, a, a, a));

            // Engine fire: the animation laid flat to the view with its base on the engine, turned so the flames stream
            // back along the plane as we see it (shorter when it points toward or away from us), bigger the fiercer it is.
            // Sorted by the engine's own distance, so it shows in front of the plane or behind it as it should.
            if (p.OnFire)
            {
                var strength = p.FireStrength;
                var engine = p.EngineFt() - camPos;
                float bx = Vector3.Dot(-pf, rightV), by = Vector3.Dot(-pf, upV), bl = MathF.Sqrt(bx * bx + by * by);
                var flameDir = bl > 0.05f ? (rightV * bx + upV * by) / bl : upV;
                var roll = bl > 0.05f ? MathF.Atan2(bx, by) : 0f;
                var hh = (5f + 13f * strength) * MathF.Max(0.35f, bl) / (2f * EffectArt.FireLengthFrac);
                var hw = (3f + 5f * strength) / (2f * EffectArt.FireWidthFrac);
                var centre = engine + flameDir * hh * (2f * EffectArt.FireBaseY - 1f);
                AddBillboard(_effects.FireFrame(_time, p.Salt), engine.Length(), centre, rightV, upV, hw, hh, roll, false,
                    new Vector4(0f, 0f, 1f, 1f), new Color(a, a, a, 0.8f * a));   // partly added light, so it glows
            }
        }

        // Smoke trails and fuel mist as camera-facing sprites (sorted with the planes and clouds), sparks as plain quads.
        // Our own trail is left out: it starts at our cowling, right in front of the sight, and is behind us at once.
        foreach (var p in fx.Particles)
        {
            if (p.Own) continue;
            var c = p.Pos - camPos;
            var dist = c.Length();
            if (Vector3.Dot(c, fwd) < 8f || dist > Box) continue;
            var op = p.Opacity;
            if (op < 0.01f) continue;
            if (p.Kind == Fx.Kind.Spark)
            {
                var col = Premul(new Vector3(1f, 0.92f, 0.6f), op);
                Vector3 dx = rightV * p.Size, dy = upV * p.Size;
                Vector3 q0 = c - dx - dy, q1 = c + dx - dy, q2 = c + dx + dy, q3 = c - dx + dy;
                _blend.Add(Vtx(q0, col)); _blend.Add(Vtx(q1, col)); _blend.Add(Vtx(q2, col));
                _blend.Add(Vtx(q0, col)); _blend.Add(Vtx(q2, col)); _blend.Add(Vtx(q3, col));
                continue;
            }
            var tex = p.Kind == Fx.Kind.Smoke ? _effects.Smoke[p.Variant % _effects.Smoke.Length] : _effects.Mist;
            var hs = p.Size / (2f * 0.85f);
            AddBillboard(tex, dist, c, rightV, upV, hs, hs, p.Rot, false, new Vector4(0f, 0f, 1f, 1f),
                new Color(p.Shade * op, p.Shade * op, p.Shade * op, op));
        }
        // Clouds: camera-facing billboards from the same field the map uses, only those inside the view box.
        var camPx = new Vector2(camPos.X, camPos.Z) * World.PxPerFoot;
        CloudsDrawn = 0;
        CloudFill = 0f;
        if (ShowClouds) CloudField.Query(camPx, Box * World.PxPerFoot, camPos.Y, Box, _puffs);
        else _puffs.Clear();
        var viewTan = MathF.Tan(MathHelper.ToRadians(HFovDeg) / 2f);
        var viewAspect = (float)_final.Width / MathF.Max(1f, _final.Height);
        _puffs.Sort((a, b) => (World.ToFt(b.Pos, b.Height) - camPos).LengthSquared().CompareTo((World.ToFt(a.Pos, a.Height) - camPos).LengthSquared()));
        foreach (var pf in _puffs)
        {
            var c = World.ToFt(pf.Pos, pf.Height) - camPos;
            var dist = c.Length();
            if (dist > Box || Vector3.Dot(c, fwd) < 60f) continue;
            // Solid out to the edge of the box; only thinning when we fly into one (so close one puff would fill the view).
            var alpha = World.Smooth(120f, 450f, dist) * 0.9f;
            if (alpha <= 0.01f) continue;
            var halfW = 256f * pf.Size / World.PxPerFoot / 2f; // sprite world px -> feet
            var halfH = halfW * 160f / 256f;
            // How much of the view this quad covers (the view at its distance is 2 d tan wide by that over the aspect high).
            var along = Vector3.Dot(c, fwd);
            CloudsDrawn++;
            CloudFill += MathF.Min(50f, 4f * halfW * halfH / (4f * along * along * viewTan * viewTan / viewAspect));
            AddBillboard(_cloudTex[pf.Variant], dist, c, rightV, upV, halfW, halfH, 0f, false, new Vector4(0f, 0f, 1f, 1f),
                new Color(alpha, alpha, alpha, alpha)); // premultiplied white
        }

        foreach (var dl in DebugLines)
        {
            _blendLines.Add(Vtx(dl.A - camPos, dl.Color)); _blendLines.Add(Vtx(dl.B - camPos, dl.Color));
        }

        // Tracers: a burning streak, white-hot at the head fading through pink-red to nothing at the tail, ribbons turned
        // to face the camera and drawn as light added to the scene. Sized in screen pixels (a core about two pixels wide in
        // a pinkish-orange glare) so it stays a fine bright line at any range, but never fatter than a round's glare up close.
        var tanH = MathF.Tan(MathHelper.ToRadians(HFovDeg) / 2f);
        var ftPerPx = 2f * tanH / MathF.Max(1f, _final.Width);   // feet across one pixel, per foot of range
        foreach (var tr in tracers)
        {
            Vector3 a = tr.A - camPos, b = tr.B - camPos, dir = b - a, mid = (a + b) * 0.5f;
            var side = Vector3.Cross(dir, mid);
            if (side.LengthSquared() < 1e-6f) continue; // coming straight at the camera: nothing to show
            side.Normalize();
            var range = MathF.Max(mid.Length(), 20f);
            var px = ftPerPx * range;                     // feet per pixel at this range
            var g = tr.Glow;
            // A ribbon from a fraction 'from' of the way along the streak to its head.
            void Ribbon(float from, float halfHead, float halfTail, Color head, Color tail)
            {
                var a0 = a + dir * from;
                Vector3 p0 = a0 - side * halfTail, p1 = a0 + side * halfTail, p2 = b + side * halfHead, p3 = b - side * halfHead;
                _blend.Add(Vtx(p0, tail)); _blend.Add(Vtx(p1, tail)); _blend.Add(Vtx(p2, head));
                _blend.Add(Vtx(p0, tail)); _blend.Add(Vtx(p2, head)); _blend.Add(Vtx(p3, head));
            }
            // Light rather than paint: premultiplied colour over a smaller alpha adds to what's behind, so the streak
            // brightens the sky the way a tracer's glare does instead of laying a dull red line over it.
            static Color Glow(Vector3 rgb, float bright, float cover) => new(rgb.X * bright, rgb.Y * bright, rgb.Z * bright, cover);
            // Glare: in daylight a red tracer reads as a washed-out pinkish orange haze round a near-white core.
            // About five pixels across at the head (no more than a couple of feet), tapering to the tail.
            var haloHalf = MathF.Min(2.5f * px, 0.9f);
            Ribbon(0f, haloHalf, haloHalf * 0.35f, Glow(new Vector3(1f, 0.55f, 0.42f), 0.6f * g, 0.12f * g), Glow(new Vector3(1f, 0.35f, 0.25f), 0f, 0f));
            // Core: about two pixels wide, white-hot (faintly warm) at the head, pink-red where it trails.
            var coreHalf = MathF.Min(2.0f * px, 0.3f);
            Ribbon(0f, coreHalf, coreHalf * 0.4f, Glow(new Vector3(1f, 0.97f, 0.9f), g, 0.75f * g), Glow(new Vector3(1f, 0.45f, 0.35f), 0.65f * g, 0.35f * g));
            // Head flare: the burning base of the round, the brightest point, over the last third of the streak.
            var flareHalf = MathF.Min(3.0f * px, 0.5f);
            Ribbon(0.65f, flareHalf, flareHalf * 0.5f, Glow(Vector3.One, g, 0.5f * g), Glow(new Vector3(1f, 0.85f, 0.7f), 0.4f * g, 0.15f * g));
        }
    }

    private void Draw(VertexPositionColor[] verts, PrimitiveType type, int perPrim)
    {
        if (verts.Length < perPrim) return;
        _gd.DrawUserPrimitives(type, verts, 0, verts.Length / perPrim);
    }

    // ------------------------------------------------------------------ overlay

    /// <summary>Draws the view into rect with a frame and the reflector-sight reticle. SpriteBatch must be running.</summary>
    public void Draw(SpriteBatch sb, Texture2D pixel, Rectangle rect, float scale, bool firing, float alpha)
    {
        // The window is big enough to cover the middle of the screen, so all of it (plate, view, frame, reticle)
        // fades in with the aimer and is gone when there is nothing to aim at.
        if (alpha <= 0.01f) return;
        sb.Draw(pixel, rect, new Color(26, 28, 32) * alpha);
        sb.Draw(_final, rect, Color.White * alpha);

        // Frame.
        var b = Math.Max(2, (int)(4 * scale));
        var frame = new Color(12, 12, 14) * alpha;
        sb.Draw(pixel, new Rectangle(rect.X - b, rect.Y - b, rect.Width + 2 * b, b), frame);
        sb.Draw(pixel, new Rectangle(rect.X - b, rect.Bottom, rect.Width + 2 * b, b), frame);
        sb.Draw(pixel, new Rectangle(rect.X - b, rect.Y, b, rect.Height), frame);
        sb.Draw(pixel, new Rectangle(rect.Right, rect.Y, b, rect.Height), frame);

        // Reticle: amber ring, centre dot, four stadia ticks, and a pair of range bars.
        var amber = new Color(255, 190, 70) * alpha;
        var c = new Vector2(rect.Center.X, rect.Center.Y);
        var radius = rect.Height * 0.40f;
        sb.Draw(_ring, c, null, amber * 0.95f, 0f, new Vector2(128f), radius * 2f / 256f, SpriteEffects.None, 0f);
        sb.Draw(_ring, c, null, amber * 0.6f, 0f, new Vector2(128f), radius * 0.5f * 2f / 256f, SpriteEffects.None, 0f);
        var t = Math.Max(2, (int)(2 * scale));
        void Bar(float cx, float cy, float w, float h) =>
            sb.Draw(pixel, new Rectangle((int)(cx - w / 2f), (int)(cy - h / 2f), (int)Math.Max(w, 1), (int)Math.Max(h, 1)), amber);
        Bar(c.X, c.Y, t * 2.5f, t * 2.5f);
        var tick = radius * 0.14f;
        Bar(c.X, c.Y - radius - tick / 2f + 1, t, tick); Bar(c.X, c.Y + radius + tick / 2f - 1, t, tick);
        Bar(c.X - radius - tick / 2f + 1, c.Y, tick, t); Bar(c.X + radius + tick / 2f - 1, c.Y, tick, t);
        // Range bars: wingspan marks left and right of centre at the ring's half radius.
        Bar(c.X - radius * 0.5f, c.Y, t, tick * 0.9f); Bar(c.X + radius * 0.5f, c.Y, t, tick * 0.9f);
        if (firing) Bar(c.X, c.Y - radius - tick * 1.6f, t * 3f, t * 3f); // little muzzle-flash lamp
    }
}
