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
    // Tuning, ported from Skyward's Spitfire at 60 ticks/s. Angles in radians.
    private const float MaxBank = 0.58f;
    private const float TurnCoeff = 0.046f * 60f; // heading rad/s per unit of bank
    private const float HeadingSeekRef = 0.5f;    // heading error that saturates to full bank
    private const float BankResponse = 0.18f * 60f;
    private const float DeadzonePx = 20f;
    private const float Speed = 360f;             // world px/s at 720p; scaled with screen height

    private const int Cell = 420;                 // scenery grid cell size

    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _sb;
    private Texture2D _pixel, _grass, _plane, _tree;
    private Texture2D[] _houses;

    private Vector2 _pos;      // world position of the plane (screen centre)
    private float _heading;    // 0 = north (up), clockwise
    private float _bank;

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
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _sb = new SpriteBatch(GraphicsDevice);
        _pixel = Art.Pixel(GraphicsDevice);
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
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (Keyboard.GetState().IsKeyDown(Keys.Escape)) Exit();

        var vp = GraphicsDevice.Viewport;
        var m = Mouse.GetState();
        var d = new Vector2(m.X - vp.Width / 2f, m.Y - vp.Height / 2f);

        // Inside the deadzone the plane levels out and holds its heading.
        var targetBank = 0f;
        if (d.Length() > DeadzonePx)
        {
            var targetHeading = MathF.Atan2(d.X, -d.Y);
            var err = MathHelper.WrapAngle(targetHeading - _heading);
            targetBank = MathHelper.Clamp(err / HeadingSeekRef, -1f, 1f) * MaxBank;
        }
        _bank += (targetBank - _bank) * (1f - MathF.Exp(-BankResponse * dt));
        _heading = MathHelper.WrapAngle(_heading + _bank * TurnCoeff * dt);

        _pos += new Vector2(MathF.Sin(_heading), -MathF.Cos(_heading)) * Speed * Scale * dt;
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

        // Plane: ground shadow offset away from the light, then the plane rotated to its heading.
        var ps = s * 1.1f;
        var origin = new Vector2(48, 48);
        _sb.Draw(_plane, centre + new Vector2(22, 30) * s, null, new Color(0, 0, 0, 80), _heading, origin, ps * 0.95f,
            SpriteEffects.None, 0f);
        // Narrow the wingspan slightly when banked for a hint of tilt.
        var squash = new Vector2(MathF.Cos(_bank * 0.6f), 1f) * ps;
        _sb.Draw(_plane, centre, null, Color.White, _heading, origin, squash, SpriteEffects.None, 0f);
        _sb.End();

        base.Draw(gameTime);
    }
}
