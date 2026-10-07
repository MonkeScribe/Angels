using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Angels_Proj;

/// <summary>
/// The Spitfire as a sphere of pre-rendered views (Content/Sprites/spitfire3d/spitfire_sheet.png): a 13 x 13 sheet of
/// 256 px frames, the camera going round the plane every 15 degrees of azimuth (columns) and up from -90 to +90 degrees
/// of elevation (rows, +90 first). Each frame is an orthographic picture with the aircraft's centre in the middle of the
/// frame, and the frames only go half way round: the plane is the same either side, so the other half is the same
/// frame flipped.
///
/// Given where the viewer is in relation to a plane (as a direction in the plane's own frame) and which way is up on
/// the viewer's screen, Pick finds the frame that looks at the plane from the nearest angle, and the roll to turn it by
/// on screen so the plane's wings and fin lie right. That one lookup serves both the map (looking straight down, north
/// at the top) and the gunsight (looking along the line of sight, up as the sight's up).
/// </summary>
public sealed class SpriteSphere
{
    public const int Frame = 256, Step = 15, Cols = 13, Rows = 13;
    public const string LateWar = "Content/Sprites/spitfire3d/spitfire_sheet.png";
    public const string EarlyWar = "Content/Sprites/spitfire_oldwar/spitfire_sheet.png";

    /// <summary>How big the plane is in a frame: its wingspan covers this many pixels (the top view, measured), for a span of
    /// SpanFt feet. So a frame covers FrameFt feet across.</summary>
    public const float SpanPx = 234f, SpanFt = 37.2f;
    public const float FrameFt = Frame * SpanFt / SpanPx;
    public const float PxPerFt = SpanPx / SpanFt;

    /// <summary>Scale that draws a frame on the map at the size the old 96 px plane had (92 px wingspan at scale 1).</summary>
    public const float MapScale = 92f / SpanPx;

    /// <summary>Where azimuth 0 is: the angle of the camera round the plane in the frames, in degrees, taken from the nose and
    /// counted anticlockwise seen from above. In the current sheet azimuth 0 is nose-on, 90 the plane's left side and 180
    /// tail-on, so the frames cover its whole length and the missing right side is the left flipped. (The first render
    /// began at the right side, -90, and had no tail views.)</summary>
    private const float AzZeroDeg = 0f;

    public readonly Texture2D Sheet;

    /// <summary>What to add to a gun muzzle (given as x right, y up, z forward from the plane's origin, as in Guns.Muzzles, then
    /// turned into the frame's axes) to find it in the frames: the frame's middle is not the plane's origin.</summary>
    public readonly Vector3 MuzzleShift;

    /// <summary>The late-war Spitfire (the other aircraft): four-bladed.</summary>
    public static SpriteSphere LoadLateWar(GraphicsDevice gd) =>
        new(gd, LateWar, new Vector3(0.7f, 0f, -1.25f));   // (its propeller: 4 blades, hub at 14.6, 0, -1.25)

    /// <summary>The early-war Spitfire (the player): three-bladed, shorter in the nose.</summary>
    public static SpriteSphere LoadEarlyWar(GraphicsDevice gd) =>
        new(gd, EarlyWar, new Vector3(-1.3f, 0f, -0.55f));

    /// <summary>One of the views, in the plane's own frame (x nose, y left, z up): which way the camera is, which way is up
    /// and right in its picture.</summary>
    private readonly struct Cand
    {
        public readonly Vector3 C, U, R;     // toward the camera, up and right in the picture
        public readonly Rectangle Src;
        public readonly bool Flip;
        public Cand(Vector3 c, Vector3 u, Rectangle src, bool flip)
        {
            C = c; U = u; R = Vector3.Cross(-c, u); Src = src; Flip = flip;
        }
    }

    public readonly struct View
    {
        public readonly Rectangle Src;       // the frame on the sheet
        public readonly bool Flip;           // draw it flipped left to right
        public readonly float Roll;          // turn it this far, clockwise on screen, radians
        private readonly Vector3 _right, _up; // the picture's right and up, in the plane's own frame
        public View(Rectangle src, bool flip, float roll, Vector3 right, Vector3 up)
        {
            Src = src; Flip = flip; Roll = roll; _right = right; _up = up;
        }

        /// <summary>Where a point of the plane lands on screen, as sprite px (x right, y down) from the plane's centre, for a
        /// point given in feet in the plane's own frame (x nose, y left, z up). It follows the frame as it is drawn, flip
        /// and roll included, so things drawn on the plane (the propeller) sit on the picture.</summary>
        public Vector2 Project(Vector3 body)
        {
            float x = Vector3.Dot(body, _right) * PxPerFt, y = Vector3.Dot(body, _up) * PxPerFt;   // y up in the picture
            float c = MathF.Cos(Roll), s = MathF.Sin(Roll);
            return new Vector2(x * c + y * s, x * s - y * c);
        }
    }

    private readonly List<Cand> _views = new();

    private SpriteSphere(GraphicsDevice gd, string path, Vector3 muzzleShift)
    {
        MuzzleShift = muzzleShift;
        Sheet = LoadSheet(gd, path);
        for (var col = 0; col < Cols; col++)
            for (var row = 0; row < Rows; row++)
            {
                var az = MathHelper.ToRadians(col * Step + AzZeroDeg);
                var el = MathHelper.ToRadians(90 - row * Step);
                float ce = MathF.Cos(el), se = MathF.Sin(el), ca = MathF.Cos(az), sa = MathF.Sin(az);
                var c = new Vector3(ce * ca, ce * sa, se);
                // The picture's up is the world's up as the camera sees it; looking straight down or up it is the
                // direction away from the camera's azimuth (what the limit gives).
                var u = new Vector3(-se * ca, -se * sa, ce);
                var src = new Rectangle(col * Frame, row * Frame, Frame, Frame);
                _views.Add(new Cand(c, u, src, false));
                // The plane is the same seen from its other side: that view is this one flipped.
                _views.Add(new Cand(new Vector3(c.X, -c.Y, c.Z), new Vector3(u.X, -u.Y, u.Z), src, true));
            }
    }

    private static Texture2D LoadSheet(GraphicsDevice gd, string path)
    {
        using var stream = TitleContainer.OpenStream(path);
        using var tex = Texture2D.FromStream(gd, stream);
        var px = new Color[tex.Width * tex.Height];
        tex.GetData(px);
        for (var i = 0; i < px.Length; i++) // blended premultiplied, like every other sprite
            px[i] = px[i].A == 0 ? Color.Transparent : Color.FromNonPremultiplied(px[i].R, px[i].G, px[i].B, px[i].A);
        var sheet = new Texture2D(gd, tex.Width, tex.Height);
        sheet.SetData(px);
        return sheet;
    }

    /// <summary>The view of a plane for a viewer. toViewer is the direction from the plane to the viewer and screenUp is
    /// which way is up on the viewer's screen, both in world space; right, up and fwd are the plane's own axes.</summary>
    public View Pick(Vector3 toViewer, Vector3 screenUp, Vector3 fwd, Vector3 right, Vector3 up)
    {
        var c = new Vector3(Vector3.Dot(toViewer, fwd), -Vector3.Dot(toViewer, right), Vector3.Dot(toViewer, up));
        var s = new Vector3(Vector3.Dot(screenUp, fwd), -Vector3.Dot(screenUp, right), Vector3.Dot(screenUp, up));
        c.Normalize();
        var best = default(Cand);
        var bestRoll = 0f;
        var bestScore = float.MaxValue;
        foreach (var v in _views)
        {
            var err = MathF.Acos(MathHelper.Clamp(Vector3.Dot(v.C, c), -1f, 1f));
            if (err > bestScore) continue;
            // Roll: screen-up, as it lies in this view's picture, is turned this far from the picture's own up.
            var p = s - v.C * Vector3.Dot(s, v.C);
            var roll = p.LengthSquared() < 1e-6f ? 0f : MathF.Atan2(Vector3.Dot(p, v.U), Vector3.Dot(p, v.R)) - MathHelper.PiOver2;
            roll = MathHelper.WrapAngle(roll);
            // Near a tie (all the frames at a pole look the same way) the one needing the least turn wins.
            var score = err + 0.01f * MathF.Abs(roll);
            if (score >= bestScore) continue;
            best = v; bestRoll = roll; bestScore = score;
        }
        return new View(best.Src, best.Flip, bestRoll, best.R, best.U);
    }
}
