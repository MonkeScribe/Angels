using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Angels_Proj;

/// <summary>
/// Top-down arcade flight. The plane is fixed at screen centre and the world scrolls beneath it.
/// The mouse picks a compass bearing from screen centre; the plane banks toward it, so a small
/// heading error gives a gentle turn and a large one a tight turn (as in Skyward's mouse mode).
/// </summary>
public class Game1 : Game
{
    // Mouse steering: constant turn rate. The plane banks fully toward the cursor's bearing unless
    // it is already within ~HeadingSeekRef of it, so small corrections turn as hard as big ones.
    private const float HeadingSeekRef = 0.15f;  // rad of heading error that saturates to full bank
    private const float BankResponse = 0.18f;    // fraction of the bank gap closed per tick
    private const float DeadzonePx = 20f;
    private const float PxPerFoot = 1.2f;        // screen px per ft of ground travel, at 720p

    private const int Cell = 420;                 // scenery grid cell size

    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _sb;
    private Texture2D _pixel, _grass, _plane, _tree;
    private Texture2D[] _houses;

    private Vector2 _pos;      // world position of the plane (screen centre)
    private const float WheelDegPerNotch = 10f;  // one mouse-wheel notch (120 units) steps the pitch command one detent
    private const bool InvertWheel = false;     // false: wheel up = nose up

    private readonly FlightModel _fm = new();
    private Horizon _horizon;
    private int _lastWheel;
    private bool _lastMiddle;
    private readonly Random _rng = new();

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
    }

    protected override void Initialize()
    {
        var mode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        _graphics.PreferredBackBufferWidth = mode.Width;
        _graphics.PreferredBackBufferHeight = mode.Height;
        _graphics.HardwareModeSwitch = false; // borderless fullscreen
        _graphics.IsFullScreen = true;
        _graphics.ApplyChanges();
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _sb = new SpriteBatch(GraphicsDevice);
        _pixel = Art.Pixel(GraphicsDevice);
        _horizon = new Horizon(GraphicsDevice);
        _grass = Art.Grass(GraphicsDevice);
        _plane = Art.Plane(GraphicsDevice);
        _tree = Art.Tree(GraphicsDevice);
        _houses = new[]
        {
            Art.House(GraphicsDevice, new Color(176, 70, 56)),
            Art.House(GraphicsDevice, new Color(84, 90, 120)),
            Art.House(GraphicsDevice, new Color(150, 110, 70)),
            Art.House(GraphicsDevice, new Color(90, 120, 90)),
        };
    }

    private float Scale => GraphicsDevice.Viewport.Height / 720f;

    protected override void Update(GameTime gameTime)
    {
        var kb = Keyboard.GetState();
        if (kb.IsKeyDown(Keys.Escape)) Exit();

        var vp = GraphicsDevice.Viewport;
        var m = Mouse.GetState();
        var d = new Vector2(m.X - vp.Width / 2f, m.Y - vp.Height / 2f);

        // Throttle: Shift up, Z down. Pitch: S nose up (climb), W nose down (dive); release settles to the nearest 10 degrees.
        var throttleKey = (kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift)) ? 1f : kb.IsKeyDown(Keys.Z) ? -1f : 0f;
        var pitchKey = (kb.IsKeyDown(Keys.S) ? 1f : 0f) - (kb.IsKeyDown(Keys.W) ? 1f : 0f);

        // Mouse wheel steps the pitch command by whole detents; middle click returns it to level.
        var wheel = m.ScrollWheelValue;
        var notches = (wheel - _lastWheel) / 120f * (InvertWheel ? -1f : 1f);
        _lastWheel = wheel;
        _fm.PitchCmdDeg = MathHelper.Clamp(_fm.PitchCmdDeg + notches * WheelDegPerNotch, -FlightModel.MaxDiveDeg, FlightModel.MaxClimbDeg);
        var middle = m.MiddleButton == ButtonState.Pressed;
        if (middle && !_lastMiddle) _fm.PitchCmdDeg = 0f;
        _lastMiddle = middle;

        // Bank toward the cursor's bearing; level out inside the deadzone.
        var targetBank = 0f;
        if (d.Length() > DeadzonePx)
        {
            var err = MathHelper.WrapAngle(MathF.Atan2(d.X, -d.Y) - _fm.Heading);
            targetBank = MathHelper.Clamp(err / HeadingSeekRef, -1f, 1f) * FlightModel.MaxBank;
        }

        _fm.Step(pitchKey, throttleKey, targetBank, BankResponse, _rng);
        _pos += new Vector2(MathF.Sin(_fm.Heading), -MathF.Cos(_fm.Heading)) * _fm.GroundSpeed * PxPerFoot * Scale / 60f;
        base.Update(gameTime);
    }

    // Deterministic per-cell scenery so the world is endless without storing anything.
    private static uint Hash(int x, int y, uint salt)
    {
        var h = (uint)(x * 374761393 + y * 668265263) ^ salt * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    protected override void Draw(GameTime gameTime)
    {
        var vp = GraphicsDevice.Viewport;
        float w = vp.Width, h = vp.Height, s = Scale;
        var centre = new Vector2(w / 2f, h / 2f);

        // Ground: tile the grass texture, offset by world position so it scrolls.
        _sb.Begin(samplerState: SamplerState.LinearWrap);
        var ox = (int)MathF.Floor(_pos.X / s);
        var oy = (int)MathF.Floor(_pos.Y / s);
        _sb.Draw(_grass, new Rectangle(0, 0, (int)w, (int)h),
            new Rectangle(ox - (int)(w / s / 2), oy - (int)(h / s / 2), (int)(w / s), (int)(h / s)), Color.White);
        _sb.End();

        // Scenery.
        _sb.Begin(samplerState: SamplerState.LinearClamp);
        var halfW = w / s / 2f + Cell;
        var halfH = h / s / 2f + Cell;
        int cx0 = (int)MathF.Floor((_pos.X - halfW) / Cell), cx1 = (int)MathF.Floor((_pos.X + halfW) / Cell);
        int cy0 = (int)MathF.Floor((_pos.Y - halfH) / Cell), cy1 = (int)MathF.Floor((_pos.Y + halfH) / Cell);
        for (var cy = cy0; cy <= cy1; cy++)
            for (var cx = cx0; cx <= cx1; cx++)
            {
                var r = Hash(cx, cy, 1);
                if (r % 100 >= 55) continue; // empty cell
                var world = new Vector2(
                    (cx + 0.15f + (Hash(cx, cy, 2) % 70) / 100f) * Cell,
                    (cy + 0.15f + (Hash(cx, cy, 3) % 70) / 100f) * Cell);
                var screen = centre + (world - _pos) * s;
                if (r % 100 < 20)
                {
                    var scale = s * (1.4f + (Hash(cx, cy, 4) % 40) / 100f);
                    // Soft shadow, then canopy.
                    _sb.Draw(_tree, screen + new Vector2(10, 12) * s, null, new Color(0, 0, 0, 60), 0f,
                        new Vector2(24, 24), scale, SpriteEffects.None, 0f);
                    _sb.Draw(_tree, screen, null, Color.White, 0f, new Vector2(24, 24), scale, SpriteEffects.None, 0f);
                }
                else
                {
                    var tex = _houses[Hash(cx, cy, 5) % _houses.Length];
                    var rot = (Hash(cx, cy, 6) % 4) * MathHelper.PiOver2;
                    var scale = s * 2.2f;
                    _sb.Draw(tex, screen + new Vector2(8, 10) * s, null, new Color(0, 0, 0, 70), rot,
                        new Vector2(32, 32), scale, SpriteEffects.None, 0f);
                    _sb.Draw(tex, screen, null, Color.White, rot, new Vector2(32, 32), scale, SpriteEffects.None, 0f);
                }
            }

        // Plane. Altitude reads as size and as how far the shadow drifts from the plane.
        var shadowT = MathHelper.Clamp(_fm.Altitude / 5000f, 0f, 2.5f);
        var ps = s * 1.1f * (0.85f + 0.3f * MathF.Sqrt(MathHelper.Clamp(_fm.Altitude / FlightModel.CeilingFt, 0f, 1f)));
        var origin = new Vector2(48, 48);
        _sb.Draw(_plane, centre + new Vector2(0.18f, 0.26f) * 110f * shadowT * s, null, new Color(0, 0, 0, 80), _fm.Heading,
            origin, ps * 0.9f, SpriteEffects.None, 0f);
        // Narrow the wingspan slightly when banked for a hint of tilt.
        var squash = new Vector2(MathF.Cos(_fm.Bank * 0.6f), 1f) * ps;
        _sb.Draw(_plane, centre, null, Color.White, _fm.Heading, origin, squash, SpriteEffects.None, 0f);

        DrawHud();
        DrawHorizon();
        _sb.End();

        base.Draw(gameTime);
    }

    private void DrawHorizon()
    {
        var s = Scale;
        _horizon.Update(_fm.Gamma, _fm.Bank);
        var size = (int)(Horizon.Size * 1.1f * s);
        var pos = new Vector2(24 * s, GraphicsDevice.Viewport.Height - 24 * s - size);
        _sb.Draw(_horizon.Texture, new Rectangle((int)pos.X, (int)pos.Y, size, size), Color.White);
        var px = Math.Max(2, (int)MathF.Round(2.2f * s));
        var label = $"PITCH {MathHelper.ToDegrees(_fm.Gamma):+0;-0;0}  SET {_fm.PitchCmdDeg:+0;-0;0}";
        PixelFont.Draw(_sb, _pixel, label, new Vector2(pos.X, pos.Y - 10 * s - 7 * px), px, new Color(234, 242, 255));
    }

    private void DrawHud()
    {
        var s = Scale;
        var px = Math.Max(2, (int)MathF.Round(2.2f * s));
        var white = new Color(234, 242, 255);
        int x = (int)(24 * s), y = (int)(24 * s), rowH = (int)(34 * s), barW = (int)(240 * s), barH = (int)(10 * s);

        void Row(int i, string label, string value, float fill, Color color, float mark = -1f, float mark2 = -1f)
        {
            var ry = y + i * rowH;
            PixelFont.Draw(_sb, _pixel, label, new Vector2(x, ry), px, new Color(255, 206, 84));
            PixelFont.Draw(_sb, _pixel, value, new Vector2(x + barW - PixelFont.Measure(value, px), ry), px, white);
            var by = ry + 8 * px;
            _sb.Draw(_pixel, new Rectangle(x - 2, by - 2, barW + 4, barH + 4), new Color(6, 14, 28, 170));
            _sb.Draw(_pixel, new Rectangle(x, by, (int)(barW * MathHelper.Clamp(fill, 0f, 1f)), barH), color);
            foreach (var mk in new[] { mark, mark2 })
                if (mk >= 0f) _sb.Draw(_pixel, new Rectangle(x + (int)(barW * mk), by - 3, 2, barH + 6), Color.White);
        }

        var ias = _fm.IasMph;
        var stallIas = _fm.StallSpeed(1f) * MathF.Sqrt(_fm.Rho / 0.0023769f) * 0.681818f;
        Row(0, "THROTTLE", $"{_fm.Throttle * 100f:0}%", _fm.Throttle, new Color(94, 224, 160));
        Row(1, "AIRSPEED", $"{ias:0} MPH IAS", ias / 500f,
            _fm.Overspeed ? new Color(255, 94, 94) : new Color(255, 206, 84), stallIas / 500f, FlightModel.VneMph / 500f);
        Row(2, "ALTITUDE", $"{_fm.Altitude:N0} FT", _fm.Altitude / FlightModel.CeilingFt, new Color(74, 163, 255));
        Row(3, "CLIMB", $"{_fm.VerticalSpeedFpm:+0;-0;0} FT/MIN",
            (_fm.VerticalSpeedFpm + 6000f) / 12000f, _fm.VerticalSpeedFpm < -50f ? new Color(255, 94, 94) : new Color(94, 224, 160), 0.5f);
        PixelFont.Draw(_sb, _pixel, $"TAS {_fm.TasMph:0} MPH   MACH {_fm.Mach:0.00}", new Vector2(x, y + 4 * rowH), px, white);

        var wy = y + 5 * rowH + px * 2;
        if (_fm.Stalled) PixelFont.Draw(_sb, _pixel, "STALL", new Vector2(x, wy), px * 2, new Color(255, 94, 94));
        else if (_fm.Overspeed) PixelFont.Draw(_sb, _pixel, "OVERSPEED", new Vector2(x, wy), px * 2, new Color(255, 94, 94));
    }
}
