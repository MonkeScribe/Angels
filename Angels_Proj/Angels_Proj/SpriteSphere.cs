using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Angels_Proj;

/// <summary>
/// An aircraft type as a sphere of pre-rendered views (a sprite sheet, named by the type's SpriteSheetSpec): a 13 x 13
/// sheet of 256 px frames, the camera going round the plane every 15 degrees of azimuth (columns) and up from -90 to +90 degrees
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

    /// <summary>On the map a plane is drawn this many px per foot of its real size at scale 1 (a Spitfire's 37 ft span is
    /// 92 px), so bigger aircraft are drawn bigger.</summary>
    public const float MapPxPerFt = 92f / 37.2f;

    /// <summary>The device sheets are loaded on; set once by the game before anything is drawn.</summary>
    public static GraphicsDevice Device;

    private static readonly Dictionary<string, SpriteSphere> Loaded = new();

    /// <summary>The sphere for a sheet, loaded the first time it is asked for and shared after that (a sheet is big, and
    /// every aircraft of a type uses the same one).</summary>
    public static SpriteSphere For(SpriteSheetSpec spec)
    {
        if (!Loaded.TryGetValue(spec.Path, out var sphere))
        {
            sphere = new SpriteSphere(Device, spec);
            Loaded[spec.Path] = sphere;
        }
        return sphere;
    }

    public readonly SpriteSheetSpec Spec;
    public readonly Texture2D Sheet;

    /// <summary>Sheet px per foot of the aircraft, from how many pixels its wingspan covers.</summary>
    public float PxPerFt => Spec.SpanPx / Spec.SpanFt;

    /// <summary>Feet a whole frame covers.</summary>
    public float FrameFt => Frame / PxPerFt;

    /// <summary>Scale that draws a frame on the map at MapPxPerFt (times the map's own scale).</summary>
    public float MapScale => MapPxPerFt / PxPerFt;

    /// <summary>What to add to a gun muzzle (x right, y up, z forward from the plane's origin, as in Guns.Muzzles, turned
    /// into the frame's axes) to find it in the frames: the frame's middle is not the plane's origin.</summary>
    public Vector3 MuzzleShift => Spec.MuzzleShift;

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
        public readonly float PxPerFt;       // the sheet's px per foot
        private readonly Vector3 _right, _up; // the picture's right and up, in the plane's own frame
        public View(Rectangle src, bool flip, float roll, Vector3 right, Vector3 up, float pxPerFt)
        {
            Src = src; Flip = flip; Roll = roll; _right = right; _up = up; PxPerFt = pxPerFt;
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

    private SpriteSphere(GraphicsDevice gd, SpriteSheetSpec spec)
    {
        Spec = spec;
        Sheet = LoadSheet(gd, spec.Path);
        for (var col = 0; col < Cols; col++)
            for (var row = 0; row < Rows; row++)
            {
                var az = MathHelper.ToRadians(col * Step + spec.AzZeroDeg);
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
        return new View(best.Src, best.Flip, bestRoll, best.R, best.U, PxPerFt);
    }
}

/// <summary>
/// Which sprite sheet an aircraft type is drawn from, and how to read it. The sheet is 13 x 13 frames of 256 px rendered
/// by tools/render_sprites.py.
/// </summary>
public sealed class SpriteSheetSpec
{
    public string Path;

    /// <summary>The wingspan covers SpanPx pixels of a frame (measured on the top view) for a real span of SpanFt feet.</summary>
    public float SpanPx, SpanFt;

    /// <summary>Where azimuth 0 is in the frames: the camera's angle round the plane from the nose, counted anticlockwise
    /// seen from above, in degrees. 0 when the sheet was rendered nose-on first (NOSE_YAW_OFFSET 90 in the script).</summary>
    public float AzZeroDeg;

    /// <summary>See SpriteSphere.MuzzleShift.</summary>
    public Vector3 MuzzleShift;
}
