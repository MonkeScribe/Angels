using System;
using System.Linq;
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

    private const int Cell = 420;                 // scenery grid cell size (world px)

    // Camera: the further above the ground, the smaller everything on it looks.
    private const float GroundZoom = 0.55f, ZoomAltScaleFt = 4000f;

    // Crash rules.
    private const float SurvivableSinkFpm = 1000f, SurvivableSpeedMph = 200f; // gentle enough to skid in
    private const float SkidDecelFtS2 = 18f;                                  // belly friction
    private const float HouseHeightFt = 40f, TreeHeightFt = 55f, HouseRadius = 60f, TreeRadius = 34f;

    // The mouse wheel points the nose: each notch swings it this much, the same either way, and when the wheel stops
    // the nose locks where it is. Scrolling fast is just more notches. W/S take over; middle click levels out.
    private const float WheelDegPerNotch = 5f;   // flight-path angle per notch (120 units)
    private const float AimDegPerNotch = 1f;     // and per notch while the aimer is up, for fine aim
    private const int AssistTicks = 90;          // the assist flies onto the target for this long (a second and a half), then lets go
    private const int PointDwellTicks = 30;      // the mouse must stay on a plane this long (half a second) before pointing starts
    private const float AimLeadTicks = 15f;      // the assist aims where the target's bearing and elevation will be this soon
    // Mouse aim (War Thunder style) while the aimer is up: the pointer is hidden and held at the screen centre,
    // and moving the mouse swings the aim point the assist flies to, up/down and left/right of the target.
    private const float PanGain = 1.6f;          // screen px of pan per px of mouse movement
    private const float PanMarginPx = 45f;       // how close the plane may get to a screen edge when panning (720p px)
    private const float PanFollow = 0.12f;       // how fast the camera swings to keep an aimed-at target on screen (fraction of the gap a tick)
    private const float PanReturn = 0.88f;       // pan left each tick once the button is released: it eases back to centre
    private const float ExitPointerPx = 180f;    // where the pointer is put when the aimer drops: this far from the centre along the heading (720p px)
    private const int ExitHoldTicks = 8;
    private const float AimMaxStepPx = 80f;      // a tick's mouse movement beyond this is a glitch, not aiming
    private const int AimWarpSkipTicks = 3;
    private const float AimMouseDegPerPx = 0.05f; // aim swing per px of mouse movement at 720p
    private static readonly float AimYawTrimMax = MathF.PI; // free: once the assist lets go the player can follow the target anywhere
    private const bool InvertWheel = true;       // true: scroll back (towards you) pulls the nose up

    // Guns: eight .303 Brownings firing real rounds; see Guns.cs.

    private const string SecretCode = "ANGEL";

    private enum Phase { Flying, Skidding, Wrecked }

    private struct Particle
    {
        public Vector2 Pos, Vel;
        public float Life, MaxLife, Size;
        public Color Color;
    }

    private struct Prop
    {
        public bool Tree;
        public Vector2 Pos;
        public float Scale, Rot;
        public int House;
    }

    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _sb;
    private Texture2D _pixel, _grass, _tree;
    private Texture2D[] _houses, _clouds;
    private SpriteSphere _sphere;      // views of the (early-war) Spitfire from every angle: every aircraft is drawn from it
    private SpriteSphere _playerSphere; // the player's, the same sphere (the late-war one is still there: SpriteSphere.LoadLateWar)
    private readonly Fx _fx = new();
    private EffectArt _effects;                 // fire animation and smoke sprites
    private float _time;                        // seconds of play, for animations
    private Traffic _traffic;                    // brings AI formations into the world and takes them away
    private readonly System.Collections.Generic.List<Aircraft> _craft = new(); // draw-sorted copy
    private Gunsight _gunsight;
    private RenderTarget2D _world, _w2, _w4, _w8; // the ground layer, and blurred copies for out-of-focus ground
    private readonly System.Collections.Generic.List<Gunsight.Tracer> _tracers = new();
    private readonly Guns _guns;
    private int _assistTicks;                    // ticks left of the assist flying onto the target
    private float _aimBaseBearing;               // rad: where the assist pointed the heading, held once it lets go
    private Vector2 _pan;                       // camera pan, screen px: the plane sits this far from the screen centre the other way
    private bool _panning;                       // right button held: the mouse pans the camera
    private Point _panStart, _panPrev;           // the pointer when the pan began (steering is held on it), and its last reading
    private Vector2 _panLockedD;                // pointer minus plane at the start of the pan: the steering held while panning
    private float _panReleaseLen;                // how far the camera was panned when the button was let go
    private bool _panFollowing;                  // the camera is panned to keep the aimed-at target on screen
    private int _panSkip;                        // readings to ignore after the game has moved the pointer
    private int _exitHold;                      // ticks left of ignoring the pointer after mouse aim ends
    private Point _aimPrev;                      // the pointer's last reading during mouse aim
    private int _aimSkip;                        // readings to ignore after the game has moved the pointer
    private Aircraft _hovered;             // the plane under the mouse on the map, whose hit boxes are shown
    private readonly System.Collections.Generic.List<(Vector2[] poly, float damage)> _hoverZones = new();
    private bool _sphereSprite = true;           // debug menu: the player's plane from the sprite sphere (off: the old pitch views)
    private bool _showHitboxes;                 // debug menu: draw the planes' hit boxes on the map and in the sight
    private readonly System.Collections.Generic.List<(Vector3 a, Vector3 b, Color color)> _boxEdges = new();
    private bool _mouseAim;                      // the pointer is captured for mouse aim
    private float _aimYaw;                       // rad: the mouse's heading offset from the target
    private Aircraft _lastPointed;          // the plane the pointing assist followed last tick, for its rates
    private int _pointDwell;                     // ticks the mouse has stayed on that plane
    private float _lastPointBearing, _lastPointElev;
    private Aircraft _lastAimed;            // the target the assist tracked last tick, for its bearing rate
    private float _lastAimBearing, _lastAimElev;
    private Aircraft _tracked; // the plane the aimer is on: picked with the mouse, kept while it stays in the sight's view
    private float _sightAlpha; // gunsight window: fades in when a target is in view, out when none is
    private bool _firing;
    private Spitfire _spitfire;

    private readonly World _worldModel = new();  // the aircraft in the world (World.cs); the player's is Player
    private readonly PlayerPilot _pilot = new(); // the player as a pilot: keys and mouse -> the aircraft's control inputs
    private Aircraft Player => _worldModel.Player;
    // Shorthands for the player's aircraft: its flight model, its damage, and its position (world px at 720p; screen centre).
    private FlightModel _fm => Player.Flight;
    private Aircraft _dmg => Player;              // (its damage, fuel and fire are on the aircraft itself)
    private Vector2 _pos { get => Player.Pos; set => Player.Pos = value; }
    private Instruments _instruments;
    private int _lastWheel;
    private bool _lastMiddle;
    private KeyboardState _kb, _prevKb;
    private bool _prevLeft;
    private string _codeBuffer = "";
    private bool _menuOpen, _arcade, _cloudsOn = true, _hudBars;
    private int _menuSel;
    private readonly Random _rng = new();
    private Phase _phase = Phase.Flying;
    private string _reason = "";
    private float _phaseTime;
    private readonly System.Collections.Generic.List<Particle> _particles = new();

    public Game1()
    {
        _worldModel.SpawnPlayer(_pilot, Vector2.Zero, false, null);
        _guns = new Guns(_rng);
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
        _instruments = new Instruments(GraphicsDevice, _pixel);
        _sphere = SpriteSphere.LoadEarlyWar(GraphicsDevice);
        _playerSphere = _sphere;
        Player.Sphere = _playerSphere;
        _traffic = new Traffic(_sphere);
        _effects = new EffectArt(GraphicsDevice);
        _gunsight = new Gunsight(GraphicsDevice, _sb, _sphere, _effects);
        var vp0 = GraphicsDevice.Viewport;
        _world = new RenderTarget2D(GraphicsDevice, vp0.Width, vp0.Height);
        _w2 = new RenderTarget2D(GraphicsDevice, vp0.Width / 2, vp0.Height / 2);
        _w4 = new RenderTarget2D(GraphicsDevice, vp0.Width / 4, vp0.Height / 4);
        _w8 = new RenderTarget2D(GraphicsDevice, vp0.Width / 8, vp0.Height / 8);
        _grass = Art.Grass(GraphicsDevice);
        _spitfire = new Spitfire(GraphicsDevice, _pixel);
        _tree = Art.Tree(GraphicsDevice);
        _clouds = new[] { Art.Cloud(GraphicsDevice, 11), Art.Cloud(GraphicsDevice, 23), Art.Cloud(GraphicsDevice, 37) };
        _houses = new[]
        {
            Art.House(GraphicsDevice, new Color(176, 70, 56)),
            Art.House(GraphicsDevice, new Color(84, 90, 120)),
            Art.House(GraphicsDevice, new Color(150, 110, 70)),
            Art.House(GraphicsDevice, new Color(90, 120, 90)),
        };
    }

    private float Scale => GraphicsDevice.Viewport.Height / 720f;

    /// <summary>Camera distance factor to something at the given height above the ground (1 = at ground level
    /// under a plane at 0 ft). The camera hangs ZoomAltScaleFt above the plane.</summary>
    private float DistFactor(float heightFt) => 1f + (_fm.Altitude - heightFt) / ZoomAltScaleFt;

    /// <summary>Screen px per world px for things at the given height (0 = the ground).</summary>
    private float ZoomAt(float heightFt) => Scale * GroundZoom / DistFactor(heightFt);

    private float Zoom => ZoomAt(0f);

    /// <summary>Where the plane is on screen: the centre, shifted the other way from the camera pan.</summary>
    private Vector2 PlaneScreen()
    {
        var vp = GraphicsDevice.Viewport;
        return new Vector2(vp.Width / 2f, vp.Height / 2f) - _pan;
    }

    private void Reset()
    {
        _worldModel.SpawnPlayer(_pilot, _pos, _arcade, _playerSphere);   // a fresh aircraft where the old one was
        _phase = Phase.Flying;
        _particles.Clear();
        _pan = Vector2.Zero;
        _guns.Rearm();
        _reason = "";
        _phaseTime = 0f;
    }

    private void Wreck(string reason)
    {
        _phase = Phase.Wrecked;
        _reason = reason;
        _phaseTime = 0f;
        for (var i = 0; i < 70; i++)
        {
            var ang = (float)(_rng.NextDouble() * MathF.Tau);
            var spd = 60f + (float)_rng.NextDouble() * 360f;
            var fire = _rng.NextDouble();
            var col = fire < 0.35 ? new Color(255, 138, 61) : fire < 0.65 ? new Color(255, 206, 84) : new Color(70, 70, 70);
            AddParticle(_pos, new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * spd, 0.5f + (float)_rng.NextDouble() * 1.2f,
                8f + (float)_rng.NextDouble() * 16f, col);
        }
    }

    private void AddParticle(Vector2 pos, Vector2 vel, float life, float size, Color color) =>
        _particles.Add(new Particle { Pos = pos, Vel = vel, Life = life, MaxLife = life, Size = size, Color = color });

    // Deterministic per-cell scenery so the world is endless without storing anything.
    private static uint Hash(int x, int y, uint salt) => World.Hash(x, y, salt);

    private bool TryGetProp(int cx, int cy, out Prop p)
    {
        p = default;
        var r = Hash(cx, cy, 1);
        if (r % 100 >= 55) return false; // empty cell
        p.Pos = new Vector2(
            (cx + 0.15f + (Hash(cx, cy, 2) % 70) / 100f) * Cell,
            (cy + 0.15f + (Hash(cx, cy, 3) % 70) / 100f) * Cell);
        if (r % 100 < 20)
        {
            p.Tree = true;
            p.Scale = 1.4f + (Hash(cx, cy, 4) % 40) / 100f;
        }
        else
        {
            p.House = (int)(Hash(cx, cy, 5) % _houses.Length);
            p.Rot = (Hash(cx, cy, 6) % 4) * MathHelper.PiOver2;
            p.Scale = 2.2f;
        }
        return true;
    }

    /// <summary>Returns a crash reason if the plane is low enough to hit a house or tree, else null.</summary>
    private string CheckScenery()
    {
        if (_fm.Altitude > MathF.Max(HouseHeightFt, TreeHeightFt)) return null;
        int cx0 = (int)MathF.Floor(_pos.X / Cell), cy0 = (int)MathF.Floor(_pos.Y / Cell);
        for (var cy = cy0 - 1; cy <= cy0 + 1; cy++)
            for (var cx = cx0 - 1; cx <= cx0 + 1; cx++)
            {
                if (!TryGetProp(cx, cy, out var p)) continue;
                var radius = p.Tree ? TreeRadius * p.Scale / 1.6f : HouseRadius;
                var height = p.Tree ? TreeHeightFt : HouseHeightFt;
                if (_fm.Altitude < height && Vector2.DistanceSquared(_pos, p.Pos) < radius * radius)
                    return p.Tree ? "HIT A TREE" : "HIT A HOUSE";
            }
        return null;
    }

    /// <summary>The plane the mouse is over on the map that is also inside the sight's view (nearest to the pointer), if any.</summary>
    private Aircraft PlaneAimedAt(Vector2 mouse, Vector3 camFt, Vector3 sr, Vector3 su, Vector3 sf, float aspect, bool needSight = true)
    {
        var centre = PlaneScreen();
        Aircraft best = null;
        var bestD = float.MaxValue;
        var zones = new System.Collections.Generic.List<(Vector2[] poly, float damage)>();
        foreach (var p in _worldModel.Others)
        {
            var f = DistFactor(p.Altitude);
            if (f < 0.2f || _fm.Altitude - p.Altitude > World.ViewBoxFt) continue;
            if (needSight && !Gunsight.Sees(World.ToFt(p.Pos, p.Altitude) - camFt, sr, su, sf, aspect, World.ViewBoxFt)) continue;
            // The plane is under the mouse when the mouse is inside its targeting box.
            zones.Clear();
            SpriteZones(p, centre, zones, HoverPadPx);
            var hit = false;
            foreach (var (poly, _) in zones) if (InsideConvex(poly, mouse)) { hit = true; break; }
            if (!hit) continue;
            var d = Vector2.Distance(centre + (p.Pos - _pos) * (Scale * GroundZoom / f), mouse);
            if (d < bestD) { best = p; bestD = d; }
        }
        return best;
    }

    // The targeting box of a traffic plane: one box over the whole sprite (96 px, nose up, centred on 48,48), from
    // wingtip to wingtip and nose to tail, in sprite px. Hovering it tells the plane to turn and pitch at the target.
    // This is separate from the hit boxes that rounds are tested against (see each aircraft's parts, DamageTuning.Fighter in Damage.cs, and the HITBOXES view).
    private const float BoxX0 = -46f, BoxY0 = -42f, BoxX1 = 46f, BoxY1 = 40f;
    private const float HoverPadPx = 4f;             // sprite px of slack round the box, so the plane is easy to hover

    /// <summary>The on-screen targeting box of a traffic plane, as the sprite is drawn: scaled by its distance and
    /// squashed by its bank and pitch, then turned to its heading. pad grows it outward, in sprite px.</summary>
    private void SpriteZones(Aircraft p, Vector2 centre, System.Collections.Generic.List<(Vector2[] poly, float damage)> into, float pad = 0f)
    {
        var s = Scale;
        var ps = s * 0.8f * (0.85f + 0.3f * MathF.Sqrt(MathHelper.Clamp(_fm.Altitude / FlightModel.CeilingFt, 0f, 1f)));
        var f = DistFactor(p.Altitude);
        var screen = centre + (p.Pos - _pos) * (s * GroundZoom / f);
        var scale = ps / f;
        var squash = new Vector2(MathF.Cos(p.Bank * 0.6f), MathF.Max(0.5f, MathF.Cos(p.Pitch)));
        float c = MathF.Cos(p.Heading), sn = MathF.Sin(p.Heading);
        Vector2 T(float x, float y)
        {
            var v = new Vector2(x * squash.X, y * squash.Y) * scale;
            return screen + new Vector2(v.X * c - v.Y * sn, v.X * sn + v.Y * c);
        }
        into.Add((new[] { T(BoxX0 - pad, BoxY0 - pad), T(BoxX1 + pad, BoxY0 - pad), T(BoxX1 + pad, BoxY1 + pad), T(BoxX0 - pad, BoxY1 + pad) }, 0f));
    }

    private static bool InsideConvex(Vector2[] poly, Vector2 q)
    {
        float sign = 0f;
        for (var i = 0; i < poly.Length; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
            var cr = (b.X - a.X) * (q.Y - a.Y) - (b.Y - a.Y) * (q.X - a.X);
            if (cr == 0f) continue;
            if (sign == 0f) sign = MathF.Sign(cr);
            else if (MathF.Sign(cr) != sign) return false;
        }
        return true;
    }

    /// <summary>The player's view in the sprite sphere for the map, which looks straight down with north at the top, and whether
    /// the nose points away from us so that the airframe hides the propeller.</summary>
    private SpriteSphere.View PlayerView(out bool propBehind)
    {
        World.Basis(_fm.Heading, _fm.Gamma, _fm.Bank, out var r, out var u, out var f);
        propBehind = f.Y < -0.1f;
        return _playerSphere.Pick(Vector3.UnitY, -Vector3.UnitZ, f, r, u);
    }

    /// <summary>One tick with the trigger held: every gun fires its share of rounds, each leaving at muzzle velocity
    /// plus the plane's own velocity, and each wing that fired flashes.</summary>
    private void Fire()
    {
        _firing = true;
        World.Basis(_fm.Heading, _fm.Gamma, _fm.Bank, out var r, out var u, out var f);
        var gs = _fm.GroundSpeed;
        var vel = new Vector3(MathF.Sin(_fm.Heading) * gs, _fm.Speed * MathF.Sin(_fm.Gamma) * _fm.VerticalRateScale, -MathF.Cos(_fm.Heading) * gs);
        // Where each muzzle is on the map, from the sprite as it is drawn (it is drawn bigger than life), so the round
        // starts at the barrel tip on screen.
        var kw = 0.8f * (0.85f + 0.3f * MathF.Sqrt(MathHelper.Clamp(_fm.Altitude / FlightModel.CeilingFt, 0f, 1f))) * Spitfire.ArtScale / GroundZoom;
        float ch = MathF.Cos(_fm.Heading), sh = MathF.Sin(_fm.Heading), bankSq = MathF.Cos(_fm.Bank * 0.6f);
        var mapMuzzles = new Vector2[Guns.GunCount];
        var view = PlayerView(out _);
        var kws = kw / Spitfire.ArtScale * SpriteSphere.MapScale;   // world px per px of the sphere's sprite
        for (var g = 0; g < Guns.GunCount; g++)
        {
            if (_sphereSprite)
            {
                // The muzzle in the sphere's frame (x nose, y left, z up; its centre is a little ahead of the plane's origin
                // and above the nose's axis), put where the picture shows it.
                var m = Guns.Muzzles[g];
                mapMuzzles[g] = _pos + view.Project(new Vector3(m.Z, -m.X, m.Y) + _playerSphere.MuzzleShift) * kws;
                continue;
            }
            var sp = Guns.MuzzleSpritePx[g] - Spitfire.Origin;
            var off = new Vector2(sp.X * bankSq, sp.Y) * kw;
            mapMuzzles[g] = _pos + new Vector2(off.X * ch - off.Y * sh, off.X * sh + off.Y * ch);
        }
        var wings = _guns.Fire(World.ToFt(_pos, _fm.Altitude), r, u, f, vel, mapMuzzles);
        if (!Guns.ShowEffects) return;
        if ((wings & 1) != 0) _spitfire.Shot(0, _rng);
        if ((wings & 2) != 0) _spitfire.Shot(1, _rng);
    }

    private bool Pressed(Keys k) => _kb.IsKeyDown(k) && !_prevKb.IsKeyDown(k);

    /// <summary>Typing the secret code at any time (none of its letters are flight controls) toggles the debug menu.</summary>
    private void TrackSecretCode()
    {
        for (var k = Keys.A; k <= Keys.Z; k++)
        {
            if (!Pressed(k)) continue;
            _codeBuffer += (char)('A' + (k - Keys.A));
            if (_codeBuffer.Length > SecretCode.Length) _codeBuffer = _codeBuffer[^SecretCode.Length..];
            if (_codeBuffer == SecretCode)
            {
                _menuOpen = !_menuOpen;
                _menuSel = 0;
                _codeBuffer = "";
            }
        }
    }

    private static readonly string[] MenuRows = { "MODE", "CLOUDS", "HUD BARS", "HITBOXES", "PLAYER SPRITE", "HIT OWN ENGINE", "SPAWN TARGETS", "CLOSE" };

    private string MenuValue(int i) => i switch
    {
        0 => _arcade ? "ARCADE" : "REALISM",
        1 => _cloudsOn ? "ON" : "OFF",
        2 => _hudBars ? "ON" : "OFF",
        3 => _showHitboxes ? "ON" : "OFF",
        4 => _sphereSprite ? "3D VIEWS" : "OLD",
        5 => $"{MathF.Ceiling(_dmg.EngineHp):0} HP",
        _ => "",
    };

    private void MenuActivate(int i)
    {
        switch (i)
        {
            case 0:
                _arcade = !_arcade;
                _fm.VerticalRateScale = _arcade ? 2f : 1f; // arcade: altitude gain and loss twice as fast
                break;
            case 1: _cloudsOn = !_cloudsOn; break;
            case 2: _hudBars = !_hudBars; break;
            case 3: _showHitboxes = !_showHitboxes; break;
            case 4: _sphereSprite = !_sphereSprite; break;
            case 5: _dmg.DamagePart(Part.Engine, 10f, _rng); break;   // test the engine's damage bands, leaks and fire
            case 6:
                _traffic.SpawnAhead(_worldModel, _pos, _fm.Altitude, _fm.Heading, 900f, MathF.Max(120f, _fm.TasMph - 40f));
                _menuOpen = false;
                break;
            default: _menuOpen = false; break;
        }
    }

    private Rectangle MenuPanel()
    {
        var s = Scale;
        var vp = GraphicsDevice.Viewport;
        int w = (int)(460 * s), h = (int)((90 + MenuRows.Length * 44 + 54) * s);
        return new Rectangle((vp.Width - w) / 2, (int)(vp.Height * 0.25f), w, h);
    }

    private Rectangle MenuRowRect(int i)
    {
        var s = Scale;
        var panel = MenuPanel();
        return new Rectangle(panel.X + (int)(16 * s), panel.Y + (int)((70 + i * 44) * s), panel.Width - (int)(32 * s), (int)(36 * s));
    }

    private void UpdateMenu(MouseState m)
    {
        if (Pressed(Keys.Escape)) { _menuOpen = false; return; }
        if (Pressed(Keys.Down)) _menuSel = (_menuSel + 1) % MenuRows.Length;
        if (Pressed(Keys.Up)) _menuSel = (_menuSel + MenuRows.Length - 1) % MenuRows.Length;
        if (Pressed(Keys.Enter) || Pressed(Keys.Space)) MenuActivate(_menuSel);

        var pt = new Point(m.X, m.Y);
        for (var i = 0; i < MenuRows.Length; i++)
            if (MenuRowRect(i).Contains(pt))
            {
                _menuSel = i;
                if (m.LeftButton == ButtonState.Pressed && !_prevLeft) MenuActivate(i);
            }
    }

    protected override void Update(GameTime gameTime)
    {
        _prevKb = _kb;
        _kb = Keyboard.GetState();
        var kb = _kb;
        var m = Mouse.GetState();
        TrackSecretCode();
        if (_menuOpen)
        {
            IsMouseVisible = true; // the menu needs the pointer, even mid-aim
            _mouseAim = false;
            UpdateMenu(m); // the game is paused while the menu is open
            _lastWheel = m.ScrollWheelValue;
            _prevLeft = m.LeftButton == ButtonState.Pressed;
            base.Update(gameTime);
            return;
        }
        _prevLeft = m.LeftButton == ButtonState.Pressed;
        // A fresh press only: the press that closed the debug menu is still held on the next tick and mustn't quit the game.
        if (Pressed(Keys.Escape)) Exit();
        if (Pressed(Keys.R)) Reset();

        var vp = GraphicsDevice.Viewport;

        // Camera pan: holding the right button, mouse movement pans the camera (forward pans north, and so on), up to the
        // point where the plane reaches the edge of the screen. The pointer is hidden and steering is held on where the
        // pointer was when the pan began; letting go puts the pointer back there and the camera eases back to the plane.
        // Like mouse aim, only the movement since the last reading counts and the pointer is recentred near an edge.
        var rightDown = m.RightButton == ButtonState.Pressed;
        if (rightDown && _phase == Phase.Flying && !_mouseAim)
        {
            var now = new Point(m.X, m.Y);
            if (!_panning)
            {
                _panning = true;
                _panStart = _panPrev = now;
                _panSkip = 0;
                // Steering is locked as a direction: the pointer's bearing from the plane as it is now, held as it is
                // whatever the camera does, so the plane keeps flying the same way.
                _panLockedD = new Vector2(now.X, now.Y) - PlaneScreen();
                IsMouseVisible = false;
            }
            else if (IsActive)
            {
                if (_panSkip > 0) _panSkip--;
                else
                {
                    float dx = MathHelper.Clamp(now.X - _panPrev.X, -AimMaxStepPx, AimMaxStepPx);
                    float dy = MathHelper.Clamp(now.Y - _panPrev.Y, -AimMaxStepPx, AimMaxStepPx);
                    _pan += new Vector2(dx, dy) * PanGain;
                }
                _panPrev = now;
                if (now.X < vp.Width / 5 || now.X > vp.Width * 4 / 5 || now.Y < vp.Height / 5 || now.Y > vp.Height * 4 / 5)
                {
                    Mouse.SetPosition(vp.Width / 2, vp.Height / 2);
                    _panSkip = AimWarpSkipTicks;
                }
            }
        }
        else
        {
            if (_panning)
            {
                _panning = false;
                _panReleaseLen = MathF.Max(_pan.Length(), 1f);
                IsMouseVisible = true;
                if (IsActive) Mouse.SetPosition(_panStart.X, _panStart.Y);
            }
            // With the aimer up, the camera follows the target: it pans just far enough to keep it on screen, within the same
            // limit as the manual pan (the plane stays on screen). A target beyond that is let go: the aimer fades out
            // and the camera recentres.
            var panMargin0 = PanMarginPx * Scale;
            var following = false;
            if (_tracked != null && _phase == Phase.Flying && _worldModel.Others.Contains(_tracked))
            {
                var tk = Scale * GroundZoom / MathF.Max(0.2f, DistFactor(_tracked.Altitude));
                var t0 = new Vector2(vp.Width / 2f, vp.Height / 2f) + (_tracked.Pos - _pos) * tk;   // where it is with no pan
                var want = new Vector2(
                    t0.X < panMargin0 ? t0.X - panMargin0 : t0.X > vp.Width - panMargin0 ? t0.X - (vp.Width - panMargin0) : 0f,
                    t0.Y < panMargin0 ? t0.Y - panMargin0 : t0.Y > vp.Height - panMargin0 ? t0.Y - (vp.Height - panMargin0) : 0f);
                if (MathF.Abs(want.X) > vp.Width / 2f - panMargin0 || MathF.Abs(want.Y) > vp.Height / 2f - panMargin0)
                    _tracked = null;   // out of panning range
                else
                {
                    following = true;
                    _pan += (want - _pan) * PanFollow;
                    _panReleaseLen = 0f;   // no steering blend while following: the mouse is aiming, not steering
                }
            }
            if (!following)
            {
                if (_panFollowing && _pan != Vector2.Zero)
                {
                    // The camera is coming home: hold the steering on the heading the plane is on and ease it to the pointer.
                    _panLockedD = new Vector2(MathF.Sin(_fm.Heading), -MathF.Cos(_fm.Heading)) * ExitPointerPx * Scale;
                    _panReleaseLen = MathF.Max(_pan.Length(), 1f);
                }
                _pan *= PanReturn;
                if (_pan.LengthSquared() < 0.25f) _pan = Vector2.Zero;
            }
            _panFollowing = following;
        }
        // The plane must stay on screen.
        var panMargin = PanMarginPx * Scale;
        _pan.X = MathHelper.Clamp(_pan.X, -(vp.Width / 2f - panMargin), vp.Width / 2f - panMargin);
        _pan.Y = MathHelper.Clamp(_pan.Y, -(vp.Height / 2f - panMargin), vp.Height / 2f - panMargin);

        // The pointer for steering and hovering: while panning, where it was when the pan began.
        var mouseP = _panning ? new Vector2(_panStart.X, _panStart.Y) : new Vector2(m.X, m.Y);
        // Steering: while panning, the direction locked when the pan began, so the plane flies on whichever way it was
        // going (look behind and it doesn't turn). Once released, as the camera eases back the direction follows the
        // pointer, which never moved, blending from the locked direction as the plane's screen position returns.
        var d = mouseP - PlaneScreen();
        if (_panning) d = _panLockedD;
        else if (_pan != Vector2.Zero && _panReleaseLen > 0f)
        {
            var t = MathHelper.Clamp(_pan.Length() / _panReleaseLen, 0f, 1f);   // 1 at the moment of release, 0 when the camera is home
            float locked = MathF.Atan2(_panLockedD.X, -_panLockedD.Y), live = MathF.Atan2(d.X, -d.Y);
            var ang = locked + MathHelper.WrapAngle(live - locked) * (1f - t);
            var len = MathHelper.Lerp(_panLockedD.Length(), d.Length(), 1f - t);
            d = new Vector2(MathF.Sin(ang), -MathF.Cos(ang)) * len;
        }
        // While the camera is panned (or coming back) the pointer isn't picking targets: planes slide under it.
        var pickP = _panning || _pan != Vector2.Zero ? new Vector2(-9999f, -9999f) : mouseP;
        _phaseTime += 1f / 60f;

        // Particles run in every phase.
        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life -= 1f / 60f;
            if (p.Life <= 0f) { _particles.RemoveAt(i); continue; }
            p.Pos += p.Vel / 60f;
            p.Vel *= 0.96f;
            _particles[i] = p;
        }

        var hd = 0.5f * MathF.Sqrt(vp.Width * vp.Width + vp.Height * vp.Height);
        _traffic.Update(_worldModel, _pos, _fm.Altitude, f => hd * f / (Scale * GroundZoom), DistFactor);
        _worldModel.UpdateOthers(_fx, _rng);   // the AI aircraft fly (the player's is flown below, once its inputs are known)
        _fx.Update();
        _time += 1f / 60f;
        _boxEdges.Clear();
        if (_showHitboxes) foreach (var p in _worldModel.Others) World.HitBoxEdges(p, _boxEdges);
        _gunsight.DebugLines.Clear();
        foreach (var e in _boxEdges) _gunsight.DebugLines.Add((e.a, e.b, e.color));
        _guns.Update(_worldModel, Player, _fx);
        _guns.Tracers(_tracers);
        _firing = false;
        _spitfire.Update(_fm.Throttle, _phase == Phase.Flying && _dmg.EnginePower > 0f);   // a dead engine's propeller winds down

        // The aimer comes up when the mouse is over a plane on the map that is also inside the gunsight's view.
        // After that the mouse is free: the aimer stays on that plane until the sight loses it.
        var rect = Instruments.GunsightRect(vp.Bounds, Scale);
        var aspect = (float)rect.Width / Math.Max(1, rect.Height);
        World.Basis(_fm.Heading, _fm.Gamma, _fm.Bank, out var sr, out var su, out var sf);
        var camFt = World.ToFt(_pos, _fm.Altitude);
        _hovered = _mouseAim || _phase != Phase.Flying ? null : PlaneAimedAt(pickP, camFt, sr, su, sf, aspect, needSight: false);
        _hoverZones.Clear();
        if (_hovered != null && _showHitboxes) SpriteZones(_hovered, PlaneScreen(), _hoverZones); // drawn in the HITBOXES debug view only
        if (_tracked == null || _phase != Phase.Flying || !_worldModel.Others.Contains(_tracked) ||
            !Gunsight.Sees(World.ToFt(_tracked.Pos, _tracked.Altitude) - camFt, sr, su, sf, aspect, World.ViewBoxFt))
            _tracked = PlaneAimedAt(pickP, camFt, sr, su, sf, aspect);
        var target = _phase == Phase.Flying && _tracked != null;
        // Aim assist: getting the aimer up is the objective. For the first second and a half the game flies the plane,
        // rolling and pitching onto the target (aimed a little ahead along its motion) to steady you on it. Then it
        // lets go: the aim point stays where the assist left it and only the player moves it, with the mouse
        // (below), the wheel and W/S.
        var aimBearing = 0f;
        if (target)
        {
            var rel = World.ToFt(_tracked.Pos, _tracked.Altitude) - camFt;
            var bearing = MathF.Atan2(rel.X, -rel.Z);
            var elev = MathF.Atan2(rel.Y, MathF.Sqrt(rel.X * rel.X + rel.Z * rel.Z));
            float bearingRate = 0f, elevRate = 0f;
            if (_lastAimed == _tracked)
            {
                bearingRate = MathHelper.WrapAngle(bearing - _lastAimBearing);
                elevRate = elev - _lastAimElev;
            }
            _lastAimed = _tracked; _lastAimBearing = bearing; _lastAimElev = elev;
            if (!_fm.Aiming)
            {
                _fm.AimAcquired();
                _assistTicks = AssistTicks;
            }
            if (_assistTicks > 0)
            {
                _assistTicks--;
                _aimBaseBearing = bearing + bearingRate * AimLeadTicks;
                _fm.AimElevationDeg = MathHelper.ToDegrees(elev + elevRate * AimLeadTicks);
            }
            aimBearing = _aimBaseBearing;
        }
        else _lastAimed = null;
        _fm.Aiming = target;

        // Pointing assist: resting the mouse on a plane the sight can't see yet (say 45 degrees off the nose) for half
        // a second points the plane at it, banking toward its bearing and pitching gently to its elevation, both a
        // little ahead along its motion, until it swings into the sight and the aimer above takes over. The wait
        // means brushing over a plane in passing does nothing.
        Aircraft pointAt = null;
        float pointBearing = 0f, pointElevDeg = 0f;
        if (!target && _phase == Phase.Flying)
            pointAt = PlaneAimedAt(pickP, camFt, sr, su, sf, aspect, needSight: false);
        _pointDwell = pointAt != null && pointAt == _lastPointed ? _pointDwell + 1 : 0;
        if (pointAt != null && _pointDwell < PointDwellTicks)
        {
            // Still waiting: remember the plane and where it is, so its motion is known when steering starts.
            var wrel = World.ToFt(pointAt.Pos, pointAt.Altitude) - camFt;
            _lastPointed = pointAt;
            _lastPointBearing = MathF.Atan2(wrel.X, -wrel.Z);
            _lastPointElev = MathF.Atan2(wrel.Y, MathF.Sqrt(wrel.X * wrel.X + wrel.Z * wrel.Z));
            pointAt = null;
        }
        else if (pointAt != null)
        {
            var rel = World.ToFt(pointAt.Pos, pointAt.Altitude) - camFt;
            var bearing = MathF.Atan2(rel.X, -rel.Z);
            var elev = MathF.Atan2(rel.Y, MathF.Sqrt(rel.X * rel.X + rel.Z * rel.Z));
            float bearingRate = 0f, elevRate = 0f;
            if (_lastPointed == pointAt)
            {
                bearingRate = MathHelper.WrapAngle(bearing - _lastPointBearing);
                elevRate = elev - _lastPointElev;
            }
            _lastPointed = pointAt; _lastPointBearing = bearing; _lastPointElev = elev;
            pointBearing = bearing + bearingRate * AimLeadTicks;
            pointElevDeg = MathHelper.ToDegrees(elev + elevRate * AimLeadTicks);
        }
        else _lastPointed = null;

        // Mouse aim: while the aimer is up the pointer disappears and the mouse moves the aim point instead,
        // left/right swinging the heading the assist rolls toward, up/down the pitch. Only the mouse's movement
        // since the last tick counts. The hidden pointer is left where it is and only put back in the middle when
        // it nears an edge, and the readings straight after that are skipped: on macOS a pointer moved by the game
        // lands late and the mouse is briefly ignored, so the first readings after a move are not real movement.
        // Losing the target from the sight drops the aimer and brings the pointer back.
        if (target)
        {
            var now = new Point(m.X, m.Y);
            if (!_mouseAim)
            {
                _mouseAim = true;
                _aimYaw = 0f;
                _aimPrev = now;
                _aimSkip = 0;
                IsMouseVisible = false;
            }
            else if (IsActive)
            {
                if (_aimSkip > 0) _aimSkip--;
                else
                {
                    float dx = MathHelper.Clamp(now.X - _aimPrev.X, -AimMaxStepPx, AimMaxStepPx);
                    float dy = MathHelper.Clamp(now.Y - _aimPrev.Y, -AimMaxStepPx, AimMaxStepPx);
                    var k = AimMouseDegPerPx / Scale;
                    _aimYaw = MathHelper.Clamp(_aimYaw + MathHelper.ToRadians(dx * k), -AimYawTrimMax, AimYawTrimMax);
                    _fm.AimTrimBy(-dy * k);
                }
                _aimPrev = now;
                // Near an edge the hidden pointer would stop moving, so bring it back to the middle.
                if (now.X < vp.Width / 5 || now.X > vp.Width * 4 / 5 || now.Y < vp.Height / 5 || now.Y > vp.Height * 4 / 5)
                {
                    Mouse.SetPosition(vp.Width / 2, vp.Height / 2);
                    _aimSkip = AimWarpSkipTicks;
                }
            }
        }
        else if (_mouseAim)
        {
            // Put the pointer where the plane is already heading: out from the screen centre along its heading. The
            // plane banks toward the pointer's bearing, so it carries straight on instead of yanking round to wherever
            // the pointer was parked or was before the aimer came up. Mouse steering is held off for a few ticks
            // while the pointer lands (on macOS it lands late).
            _mouseAim = false;
            IsMouseVisible = true;
            var hdg = new Vector2(MathF.Sin(_fm.Heading), -MathF.Cos(_fm.Heading));
            var exit = PlaneScreen() + hdg * ExitPointerPx * Scale;
            exit = Vector2.Clamp(exit, new Vector2(8f, 8f), new Vector2(vp.Width - 8f, vp.Height - 8f));
            if (IsActive) Mouse.SetPosition((int)exit.X, (int)exit.Y);
            _exitHold = ExitHoldTicks;
        }
        _sightAlpha = MathHelper.Clamp(_sightAlpha + (target ? 0.06f : -0.025f), 0f, 1f);

        var dir = new Vector2(MathF.Sin(_fm.Heading), -MathF.Cos(_fm.Heading));
        if (_phase == Phase.Wrecked)
        {
            // Smoke drifts up from the wreck for a while.
            if (_phaseTime < 8f)
                AddParticle(_pos + new Vector2((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f) * 30f,
                    new Vector2((float)_rng.NextDouble() - 0.3f, -20f - (float)_rng.NextDouble() * 20f), 2.5f,
                    14f + (float)_rng.NextDouble() * 12f, new Color(60, 60, 60));
            base.Update(gameTime);
            return;
        }
        if (_phase == Phase.Skidding)
        {
            _fm.Speed = MathF.Max(0f, _fm.Speed - SkidDecelFtS2 / 60f);
            _pos += dir * _fm.Speed * PxPerFoot / 60f;
            if (_fm.Speed > 8f)
                AddParticle(_pos - dir * 24f, new Vector2((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f) * 90f,
                    0.8f, 8f + (float)_rng.NextDouble() * 8f, new Color(190, 175, 140));
            base.Update(gameTime);
            return;
        }

        // Throttle: Shift up, Z down. Pitch: S nose up (climb), W nose down (dive); release settles to the nearest 10 degrees.
        _pilot.ReadKeys(kb);
        var pitchKey = _pilot.PitchKey;

        // Mouse wheel: each notch swings the nose, and it stops dead where the wheel stops. Notches are fine
        // steps while the aimer is up.
        var wheel = m.ScrollWheelValue;
        var notches = (wheel - _lastWheel) / 120f * (InvertWheel ? -1f : 1f);
        _lastWheel = wheel;
        _fm.WheelPitch(notches * (target ? AimDegPerNotch : WheelDegPerNotch));
        var middle = m.MiddleButton == ButtonState.Pressed;
        if (middle && !_lastMiddle) _fm.WheelLevel();
        _lastMiddle = middle;

        // Bank toward the cursor's bearing; level out inside the deadzone. While aiming, bank toward the target
        // instead, offset by the mouse aim; while pointing, bank toward the hovered plane. Pointing also pitches
        // the nose to it, unless W/S is held.
        var targetBank = 0f;
        if (target)
        {
            var err = MathHelper.WrapAngle(aimBearing + _aimYaw - _fm.Heading);
            targetBank = MathHelper.Clamp(err / HeadingSeekRef, -1f, 1f) * FlightModel.MaxBank;
        }
        else if (pointAt != null)
        {
            var err = MathHelper.WrapAngle(pointBearing - _fm.Heading);
            targetBank = MathHelper.Clamp(err / HeadingSeekRef, -1f, 1f) * FlightModel.MaxBank;
            if (pitchKey == 0f) _fm.PointPitch(pointElevDeg);
        }
        else if (_exitHold > 0)
        {
            _exitHold--; // the pointer is landing on the heading; hold the bank until it has
        }
        else if (d.Length() > DeadzonePx)
        {
            var err = MathHelper.WrapAngle(MathF.Atan2(d.X, -d.Y) - _fm.Heading);
            targetBank = MathHelper.Clamp(err / HeadingSeekRef, -1f, 1f) * FlightModel.MaxBank;
        }

        // The player's aircraft flies a tick: the pilot (keys, and the bank wanted from the mouse) sets its control inputs,
        // then its engine, damage and flight model do the rest and it moves (Aircraft.Step).
        _pilot.Bank = targetBank / FlightModel.MaxBank;
        Player.BankResponse = BankResponse;
        Player.Step(_fx, _rng);
        if (kb.IsKeyDown(Keys.Space) || m.LeftButton == ButtonState.Pressed) Fire();

        var hit = CheckScenery();
        if (hit != null) Wreck(hit);
        else if (_fm.GroundHit)
        {
            // Gentle, slow touchdowns skid to a stop; anything harder is fatal.
            if (_fm.ImpactSinkFpm <= SurvivableSinkFpm && _fm.ImpactSpeedMph <= SurvivableSpeedMph)
            {
                _phase = Phase.Skidding;
                _reason = "CRASH LANDED";
                _phaseTime = 0f;
                _fm.Bank = 0f;
            }
            else Wreck(_fm.ImpactSinkFpm <= SurvivableSinkFpm ? "TOO FAST TO LAND" : "HIT THE GROUND");
        }
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        var vp = GraphicsDevice.Viewport;
        float w = vp.Width, h = vp.Height, s = Scale, z = Zoom;
        var centre = PlaneScreen();

        // The gunsight's 3D view goes into its own render target before anything is drawn to the screen.
        var sightRect = Instruments.GunsightRect(vp.Bounds, s);
        if (_sightAlpha > 0.01f)
            _gunsight.Render(sightRect.Width, sightRect.Height, World.ToFt(_pos, _fm.Altitude), _fm.Heading, _fm.Gamma, _fm.Bank,
                _worldModel.Others, _fx, _tracers, _fm.Throttle);

        _craft.Clear();
        _craft.AddRange(_worldModel.Others);
        _craft.Sort((a, b) => a.Altitude.CompareTo(b.Altitude));

        // The ground is only inside the view box while we are within ViewBoxFt of it. As we climb it first goes
        // out of focus, then fades into sky; diving back down it fades in and sharpens.
        var groundT = _fm.Altitude / World.ViewBoxFt;
        var vis = 1f - World.Smooth(0.7f, 1f, groundT);
        var blur = World.Smooth(0.08f, 0.85f, groundT);
        if (vis > 0.002f)
        {
            DrawGroundLayer(centre, blur);
            if (blur > 0.02f) BlurGroundLayer();
        }

        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(SkyColor());
        _sb.Begin(samplerState: SamplerState.LinearClamp);
        if (vis > 0.002f)
        {
            // Sharp, mid-blur and heavy-blur copies of the ground, weighted so they add up to vis.
            var u = Math.Clamp(blur * 2f, 0f, 2f);
            float wSharp = u < 1f ? 1f - u : 0f, w4 = u < 1f ? u : 2f - u, w8 = u < 1f ? 0f : u - 1f;
            wSharp *= vis; w4 *= vis; w8 *= vis;
            var full = new Rectangle(0, 0, (int)w, (int)h);
            // Sequential "over" compositing: alpha_k = W_k / (1 - sum of weights drawn after it).
            if (wSharp > 0.001f) _sb.Draw(_world, full, Color.White * (wSharp / Math.Max(0.001f, 1f - w4 - w8)));
            if (w4 > 0.001f) _sb.Draw(_w4, full, Color.White * (w4 / Math.Max(0.001f, 1f - w8)));
            if (w8 > 0.001f) _sb.Draw(_w8, full, Color.White * w8);
        }

        if (_cloudsOn) DrawClouds(centre, CloudPass.Below);
        DrawTraffic(centre, TrafficPass.Below);

        // Plane: pitching foreshortens the fuselage as seen from above. Altitude reads as size and as how far
        // the shadow drifts from the plane.
        if (_phase != Phase.Wrecked)
        {
            var shadowT = MathHelper.Clamp(_fm.Altitude / 5000f, 0f, 2.5f);
            var ps = s * 0.8f * (0.85f + 0.3f * MathF.Sqrt(MathHelper.Clamp(_fm.Altitude / FlightModel.CeilingFt, 0f, 1f))) * Spitfire.ArtScale;
            var shadowAt = centre + new Vector2(0.18f, 0.26f) * 110f * shadowT * s;
            if (_sphereSprite)
            {
                // The sprite sphere: the picture for how the plane is turned (heading, pitch and bank all show in it).
                var view = PlayerView(out var propBehind);
                var sc = ps / Spitfire.ArtScale * SpriteSphere.MapScale;
                Spitfire.DrawSphereShadow(_sb, _playerSphere, view, shadowAt, sc * 0.9f, new Color(0, 0, 0, 80) * vis);
                _spitfire.DrawSphere(_sb, _playerSphere, view, propBehind, centre, sc, Color.White);
                if (_dmg.OnFire) DrawFire(_playerSphere, view, centre, sc, _dmg.FireStrength, 0);
            }
            else
            {
                _spitfire.DrawShadow(_sb, shadowAt, _fm.Heading, new Vector2(ps * 0.9f), _fm.Gamma, new Color(0, 0, 0, 80) * vis);
                // Narrow the wingspan slightly when banked for a hint of tilt.
                var squash = new Vector2(MathF.Cos(_fm.Bank * 0.6f), 1f) * ps; // the pitch views are already foreshortened
                _spitfire.Draw(_sb, centre, _fm.Heading, squash, _fm.Gamma, Color.White);
            }
        }
        else
        {
            // Scorch mark where the plane went in.
            _sb.Draw(_pixel, new Rectangle((int)(centre.X - 40 * z), (int)(centre.Y - 28 * z), (int)(80 * z), (int)(56 * z)), new Color(20, 20, 20, 150));
        }

        // Hit boxes (debug menu): each box's edges, every end placed and scaled by its own height like everything
        // else on the map. Colour shows the state of the part the box belongs to (green undamaged ... red black, grey gone).
        foreach (var e in _boxEdges)
        {
            var col = e.color;
            Vector2 P(Vector3 q) => centre + (new Vector2(q.X, q.Z) * World.PxPerFoot - _pos) * (s * GroundZoom / MathF.Max(DistFactor(q.Y), 0.2f));
            var a = P(e.a); var b = P(e.b);
            var dl = b - a; var len = dl.Length();
            if (len < 0.5f) continue;
            _sb.Draw(_pixel, (a + b) / 2f, null, col * 0.9f, MathF.Atan2(dl.Y, dl.X), new Vector2(0.5f, 0.5f),
                new Vector2(len, MathF.Max(1f, 1.2f * s)), SpriteEffects.None, 0f);
        }

        // HITBOXES debug view: the plane under the mouse shows its targeting box (one box over the whole sprite), green,
        // filled translucent with an outline. The mouse is over the plane when it is inside this (plus a little slack).
        foreach (var (poly, damage) in _hoverZones)
        {
            var col = new Color(80, 235, 110);
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var hp in poly) { minY = MathF.Min(minY, hp.Y); maxY = MathF.Max(maxY, hp.Y); }
            for (var y = MathF.Floor(minY); y <= MathF.Ceiling(maxY); y += 1f)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (var i = 0; i < poly.Length; i++)
                {
                    Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
                    if ((a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y))
                    {
                        var x = a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                        lo = MathF.Min(lo, x); hi = MathF.Max(hi, x);
                    }
                }
                if (hi > lo) _sb.Draw(_pixel, new Rectangle((int)lo, (int)y, Math.Max(1, (int)(hi - lo)), 1), col * 0.28f);
            }
            for (var i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
                var dl = b - a; var len = dl.Length();
                if (len < 0.5f) continue;
                _sb.Draw(_pixel, (a + b) / 2f, null, col, MathF.Atan2(dl.Y, dl.X), new Vector2(0.5f, 0.5f),
                    new Vector2(len, MathF.Max(1f, 1.5f * s)), SpriteEffects.None, 0f);
            }
        }

        // Particles (world space).
        foreach (var p in _particles)
        {
            var t = p.Life / p.MaxLife;
            var size = Math.Max(1, (int)(p.Size * z * (0.5f + 0.5f * t)));
            var pos = centre + (p.Pos - _pos) * z;
            _sb.Draw(_pixel, new Rectangle((int)pos.X - size / 2, (int)pos.Y - size / 2, size, size), p.Color * Math.Min(1f, t * 1.5f));
        }

        // Tracers on the map: tiny burning dashes flying along the round's path, white-hot at the head and red behind,
        // with a faint red glow. Like the real thing they light a little way down the range and die away at the end.
        // They start at the drawn muzzle and are placed and scaled by their height like everything else.
        if (Guns.ShowMapTracers)
        {
            var tw = Math.Max(1, (int)MathF.Round(s));
            foreach (var rd in _guns.Rounds)
            {
                var glow = Guns.TracerGlow(rd.Age);
                if (!rd.Tracer || glow < 0.02f) continue;
                var zt = s * GroundZoom / MathF.Max(DistFactor(rd.Pos.Y), 0.2f);
                var wp = new Vector2(rd.Pos.X, rd.Pos.Z) * World.PxPerFoot + rd.MapOffset;
                var sp = centre + (wp - _pos) * zt;
                var vd = new Vector2(rd.Vel.X, rd.Vel.Z);
                if (vd.LengthSquared() < 1e-3f) continue;
                vd.Normalize();
                var ang = MathF.Atan2(vd.Y, vd.X);
                // Drawn back from the head: faint glow, red tail, brighter red, white-hot head.
                void Dash(float from, float len, float width, Color c) =>
                    _sb.Draw(_pixel, sp - vd * from * tw, null, c, ang, new Vector2(1f, 0.5f), new Vector2(len * tw, width * tw), SpriteEffects.None, 0f);
                Dash(-1f, 9f, 3f, new Color(255, 120, 90) * (0.3f * glow));   // daylight glare: washed-out pinkish orange
                Dash(3f, 5f, 1f, new Color(255, 70, 50) * (0.75f * glow));     // red tail
                Dash(0f, 4f, 1f, new Color(255, 200, 175) * glow);             // hot pink-white
                Dash(-1f, 3f, 1f, new Color(255, 255, 250) * glow);            // white-hot head
                Dash(-1f, 2f, 2f, new Color(255, 250, 240) * (0.5f * glow));   // and its flare
            }
        }

        // The plane under the mouse: its targeting box (one box over the whole sprite), green, filled translucent
        // with an outline. The mouse is over the plane when it is inside this (plus a little slack).
        foreach (var (poly, damage) in _hoverZones)
        {
            var col = new Color(80, 235, 110);
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var hp in poly) { minY = MathF.Min(minY, hp.Y); maxY = MathF.Max(maxY, hp.Y); }
            for (var y = MathF.Floor(minY); y <= MathF.Ceiling(maxY); y += 1f)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (var i = 0; i < poly.Length; i++)
                {
                    Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
                    if ((a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y))
                    {
                        var x = a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                        lo = MathF.Min(lo, x); hi = MathF.Max(hi, x);
                    }
                }
                if (hi > lo) _sb.Draw(_pixel, new Rectangle((int)lo, (int)y, Math.Max(1, (int)(hi - lo)), 1), col * 0.28f);
            }
            for (var i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
                var dl = b - a; var len = dl.Length();
                if (len < 0.5f) continue;
                _sb.Draw(_pixel, (a + b) / 2f, null, col, MathF.Atan2(dl.Y, dl.X), new Vector2(0.5f, 0.5f),
                    new Vector2(len, MathF.Max(1f, 1.5f * s)), SpriteEffects.None, 0f);
            }
        }

        // Particles (world space).
        foreach (var p in _particles)
        {
            var t = p.Life / p.MaxLife;
            var size = Math.Max(1, (int)(p.Size * z * (0.5f + 0.5f * t)));
            var pos = centre + (p.Pos - _pos) * z;
            _sb.Draw(_pixel, new Rectangle((int)pos.X - size / 2, (int)pos.Y - size / 2, size, size), p.Color * Math.Min(1f, t * 1.5f));
        }

        // Tracers on the map: tiny pure red dashes, 1 px wide and 4 long, flying along the round's path, starting at
        // the drawn muzzle and placed and scaled by their height like everything else.
        if (Guns.ShowMapTracers)
        {
            var tw = Math.Max(1, (int)MathF.Round(s));
            foreach (var rd in _guns.Rounds)
            {
                if (!rd.Tracer) continue;
                var zt = s * GroundZoom / MathF.Max(DistFactor(rd.Pos.Y), 0.2f);
                var wp = new Vector2(rd.Pos.X, rd.Pos.Z) * World.PxPerFoot + rd.MapOffset;
                var sp = centre + (wp - _pos) * zt;
                var vd = new Vector2(rd.Vel.X, rd.Vel.Z);
                if (vd.LengthSquared() < 1e-3f) continue;
                vd.Normalize();
                _sb.Draw(_pixel, sp, null, new Color(255, 0, 0), MathF.Atan2(vd.Y, vd.X), new Vector2(0.5f, 0.5f),
                    new Vector2(4f * tw, tw), SpriteEffects.None, 0f);
            }
        }

        // The plane under the mouse: its targeting box (one box over the whole sprite), green, filled translucent
        // with an outline. The mouse is over the plane when it is inside this (plus a little slack).
        foreach (var (poly, damage) in _hoverZones)
        {
            var col = new Color(80, 235, 110);
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var hp in poly) { minY = MathF.Min(minY, hp.Y); maxY = MathF.Max(maxY, hp.Y); }
            for (var y = MathF.Floor(minY); y <= MathF.Ceiling(maxY); y += 1f)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (var i = 0; i < poly.Length; i++)
                {
                    Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
                    if ((a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y))
                    {
                        var x = a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                        lo = MathF.Min(lo, x); hi = MathF.Max(hi, x);
                    }
                }
                if (hi > lo) _sb.Draw(_pixel, new Rectangle((int)lo, (int)y, Math.Max(1, (int)(hi - lo)), 1), col * 0.28f);
            }
            for (var i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
                var dl = b - a; var len = dl.Length();
                if (len < 0.5f) continue;
                _sb.Draw(_pixel, (a + b) / 2f, null, col, MathF.Atan2(dl.Y, dl.X), new Vector2(0.5f, 0.5f),
                    new Vector2(len, MathF.Max(1f, 1.5f * s)), SpriteEffects.None, 0f);
            }
        }

        // Particles (world space).
        foreach (var p in _particles)
        {
            var t = p.Life / p.MaxLife;
            var size = Math.Max(1, (int)(p.Size * z * (0.5f + 0.5f * t)));
            var pos = centre + (p.Pos - _pos) * z;
            _sb.Draw(_pixel, new Rectangle((int)pos.X - size / 2, (int)pos.Y - size / 2, size, size), p.Color * Math.Min(1f, t * 1.5f));
        }

        DrawTraffic(centre, TrafficPass.Above);
        if (_cloudsOn) DrawClouds(centre, CloudPass.Above);

        DrawHud();
        if (_showHitboxes) DrawDamageReadout();
        _instruments.Draw(_sb, _fm, GraphicsDevice.Viewport.Bounds, Scale);
        _gunsight.Draw(_sb, _pixel, sightRect, s, _firing, _sightAlpha);
        DrawBanner();
        DrawMenu();
        _sb.End();

        base.Draw(gameTime);
    }

    /// <summary>Sky colour behind everything: pale and bright low down, deepening as we climb.</summary>
    private Color SkyColor() =>
        Color.Lerp(new Color(122, 182, 236), new Color(58, 108, 204), MathHelper.Clamp(_fm.Altitude / 40000f, 0f, 1f));

    /// <summary>Everything on the ground (grass, houses, trees, and the shadows planes and clouds cast) into one layer.</summary>
    private void DrawGroundLayer(Vector2 centre, float blur)
    {
        var vp = GraphicsDevice.Viewport;
        float w = vp.Width, h = vp.Height, z = Zoom;
        GraphicsDevice.SetRenderTarget(_world);

        // Flat colour plus the tiled grass, offset by world position so it scrolls. The texture fades out as we
        // climb so it doesn't shimmer when it is heavily minified.
        _sb.Begin(samplerState: SamplerState.LinearWrap);
        _sb.Draw(_pixel, new Rectangle(0, 0, (int)w, (int)h), new Color(90, 150, 75));
        var grassA = MathHelper.Clamp((z - 0.12f) / 0.3f, 0f, 1f);
        if (grassA > 0f)
        {
            var vw = w / z; var vh = h / z;
            _sb.Draw(_grass, new Rectangle(0, 0, (int)w, (int)h),
                new Rectangle((int)MathF.Floor(_pos.X + _pan.X / z - vw / 2f), (int)MathF.Floor(_pos.Y + _pan.Y / z - vh / 2f), (int)vw, (int)vh),
                Color.White * grassA);
        }
        _sb.End();

        // Scenery, scaled by altitude along with the ground.
        _sb.Begin(samplerState: SamplerState.LinearClamp);
        var halfW = w / z / 2f + Cell;
        var halfH = h / z / 2f + Cell;
        // The view's centre in the world is the plane's position moved by the pan.
        var viewC = _pos + _pan / z;
        int cx0 = (int)MathF.Floor((viewC.X - halfW) / Cell), cx1 = (int)MathF.Floor((viewC.X + halfW) / Cell);
        int cy0 = (int)MathF.Floor((viewC.Y - halfH) / Cell), cy1 = (int)MathF.Floor((viewC.Y + halfH) / Cell);
        for (var cy = cy0; cy <= cy1; cy++)
            for (var cx = cx0; cx <= cx1; cx++)
            {
                if (!TryGetProp(cx, cy, out var p)) continue;
                var screen = centre + (p.Pos - _pos) * z;
                var scale = z * p.Scale;
                if (scale * 64f < 1.5f) continue; // too small to see
                if (p.Tree)
                {
                    // Soft shadow, then canopy.
                    _sb.Draw(_tree, screen + new Vector2(10, 12) * z, null, new Color(0, 0, 0, 60), 0f,
                        new Vector2(24, 24), scale, SpriteEffects.None, 0f);
                    _sb.Draw(_tree, screen, null, Color.White, 0f, new Vector2(24, 24), scale, SpriteEffects.None, 0f);
                }
                else
                {
                    var tex = _houses[p.House];
                    _sb.Draw(tex, screen + new Vector2(8, 10) * z, null, new Color(0, 0, 0, 70), p.Rot,
                        new Vector2(32, 32), scale, SpriteEffects.None, 0f);
                    _sb.Draw(tex, screen, null, Color.White, p.Rot, new Vector2(32, 32), scale, SpriteEffects.None, 0f);
                }
            }

        if (_cloudsOn) DrawClouds(centre, CloudPass.Shadows);
        DrawTraffic(centre, TrafficPass.Shadows);
        _sb.End();
    }

    /// <summary>Halves the ground layer repeatedly (bilinear) to get the soft copies used when it is out of focus.</summary>
    private void BlurGroundLayer()
    {
        RenderTarget2D[] chain = { _w2, _w4, _w8 };
        Texture2D from = _world;
        foreach (var rt in chain)
        {
            GraphicsDevice.SetRenderTarget(rt);
            _sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp);
            _sb.Draw(from, new Rectangle(0, 0, rt.Width, rt.Height), Color.White);
            _sb.End();
            from = rt;
        }
    }

    private enum TrafficPass { Shadows, Below, Above }

    /// <summary>Other aircraft: placed and scaled by their own distance from the camera, like clouds.</summary>
    private void DrawTraffic(Vector2 centre, TrafficPass pass)
    {
        var s = Scale;
        var zGround = Zoom;
        // Same on-screen size as the player at the same altitude; nearer or further planes scale by perspective.
        var ps = s * 0.8f * (0.85f + 0.3f * MathF.Sqrt(MathHelper.Clamp(_fm.Altitude / FlightModel.CeilingFt, 0f, 1f)));
        var origin = new Vector2(SpriteSphere.Frame / 2f);
        foreach (var c in _craft)
        {
            var f = DistFactor(c.Altitude);
            // The map looks straight down with north at the top, so the plane's view is picked for that: its heading,
            // pitch and bank all come out of which picture of the sphere it is and how it is turned.
            World.Basis(c.Heading, c.Pitch, c.Bank, out var pr, out var pu, out var pf);
            var view = _sphere.Pick(Vector3.UnitY, -Vector3.UnitZ, pf, pr, pu);
            var flip = view.Flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            var tint = Color.White;
            if (pass == TrafficPass.Shadows)
            {
                // Ground shadow: world-consistent size, nudged away from the plane with height.
                var worldScale = ps / (s * GroundZoom);
                var sp = centre + (c.Pos + new Vector2(4f, 6f) * (c.Altitude / 1000f) - _pos) * zGround;
                var ss = worldScale * zGround;
                if (ss * 96f < 3f) continue;
                _sb.Draw(_sphere.Sheet, sp, view.Src, new Color(0, 0, 0, 70), view.Roll, origin, ss * 0.9f * SpriteSphere.MapScale, flip, 0f);
                continue;
            }
            var above = c.Altitude > _fm.Altitude;
            if (above != (pass == TrafficPass.Above) || f < 0.2f || _fm.Altitude - c.Altitude > World.ViewBoxFt) continue;
            var z = s * GroundZoom / f;
            var scale = ps / f;
            if (scale * 96f < 3f) continue;
            var screen = centre + (c.Pos - _pos) * z;
            var alpha = above ? MathHelper.Clamp((f - 0.2f) / 0.4f, 0f, 1f) : MathHelper.Clamp((World.ViewBoxFt - (_fm.Altitude - c.Altitude)) / 1000f, 0f, 1f);
            _sb.Draw(_sphere.Sheet, screen, view.Src, tint * alpha, view.Roll, origin, scale * SpriteSphere.MapScale, flip, 0f);
            if (c.OnFire) DrawFire(_sphere, view, screen, scale * SpriteSphere.MapScale, c.FireStrength * alpha, c.Salt);
        }
        if (pass != TrafficPass.Shadows) DrawFx(centre, pass == TrafficPass.Above);
    }

    /// <summary>Smoke, fire and sparks on the map, placed and scaled by height like everything else.</summary>
    private void DrawFx(Vector2 centre, bool above)
    {
        foreach (var p in _fx.Particles)
        {
            if ((p.Pos.Y > _fm.Altitude) != above) continue;
            var f = DistFactor(p.Pos.Y);
            if (f < 0.2f || _fm.Altitude - p.Pos.Y > World.ViewBoxFt) continue;
            var z = Scale * GroundZoom / f;
            var pos = centre + (new Vector2(p.Pos.X, p.Pos.Z) * World.PxPerFoot - _pos) * z;
            var size = p.Size * World.PxPerFoot * z;   // across, screen px
            var a = p.Opacity;
            if (a < 0.01f || size < 0.5f) continue;
            if (p.Kind == Fx.Kind.Spark)
            {
                var sz = Math.Max(1, (int)size);
                _sb.Draw(_pixel, new Rectangle((int)pos.X - sz / 2, (int)pos.Y - sz / 2, sz, sz), Color.White * a);
                continue;
            }
            // Smoke puffs and leaking-fuel mist, each sprite turned its own way so the trail doesn't repeat.
            var tex = p.Kind == Fx.Kind.Smoke ? _effects.Smoke[p.Variant % _effects.Smoke.Length] : _effects.Mist;
            var col = new Color(p.Shade * a, p.Shade * a, p.Shade * a, a);
            _sb.Draw(tex, pos, null, col, p.Rot, new Vector2(tex.Width / 2f, tex.Height / 2f), size / (tex.Width * 0.85f), SpriteEffects.None, 0f);
        }
    }

    /// <summary>An engine fire on the map: the animation drawn on the engine of a plane's picture, turned so the flames
    /// stream back along it (shorter as the plane points toward or away from us) and longer the fiercer the fire.
    /// view is the plane's sphere view and spriteScale screen px per sphere px, as it was drawn.</summary>
    private void DrawFire(SpriteSphere sphere, SpriteSphere.View view, Vector2 planeScreen, float spriteScale, float strength, int salt)
    {
        if (strength <= 0f) return;
        var at = planeScreen + view.Project(sphere.Hub - new Vector3(3.5f, 0f, -0.4f)) * spriteScale;
        var back = view.Project(new Vector3(-1f, 0f, 0f));               // sphere px per foot toward the tail, on screen
        var len = back.Length() / SpriteSphere.PxPerFt;                   // 1 side-on, 0 end-on
        var rot = len > 0.05f ? MathF.Atan2(back.X, -back.Y) : 0f;
        var frame = _effects.FireFrame(_time, salt);
        var lengthFt = (5f + 13f * strength) * MathF.Max(0.35f, len);
        var widthFt = 3f + 5f * strength;
        var ftPx = spriteScale * SpriteSphere.PxPerFt;                    // screen px per foot
        var sc = new Vector2(widthFt * ftPx / (frame.Width * EffectArt.FireWidthFrac), lengthFt * ftPx / (frame.Height * EffectArt.FireLengthFrac));
        // A little of the colour is added rather than laid over, so the flames glow against what's behind them.
        _sb.Draw(frame, at, null, new Color(255, 255, 255, 215), rot, EffectArt.FireOrigin(frame), sc, SpriteEffects.None, 0f);
    }

    private enum CloudPass { Shadows, Below, Above }

    private void DrawClouds(Vector2 centre, CloudPass pass)
    {
        float w = GraphicsDevice.Viewport.Width, h = GraphicsDevice.Viewport.Height;
        var zGround = Zoom;
        var alt = _fm.Altitude;
        var s0 = (int)MathF.Floor(MathF.Max(0f, alt - World.ViewBoxFt) / CloudField.SliceFt);
        var s1 = (int)MathF.Floor((alt + World.ViewBoxFt) / CloudField.SliceFt);
        for (var slice = s0; slice <= s1; slice++)
        {
            // The deepest part of the slice is the furthest away, so it has the smallest zoom and the widest view.
            var fFar = MathF.Max(DistFactor(slice * CloudField.SliceFt), 0.2f);
            var zMin = MathF.Min(Scale * GroundZoom / fFar, zGround);
            var halfW = w / zMin / 2f + CloudField.Cell + 1000f + MathF.Abs(_pan.X) / zMin;   // the pan moves the view's centre
            var halfH = h / zMin / 2f + CloudField.Cell + 1000f + MathF.Abs(_pan.Y) / zMin;
            int cx0 = (int)MathF.Floor((_pos.X - halfW) / CloudField.Cell), cx1 = (int)MathF.Floor((_pos.X + halfW) / CloudField.Cell);
            int cy0 = (int)MathF.Floor((_pos.Y - halfH) / CloudField.Cell), cy1 = (int)MathF.Floor((_pos.Y + halfH) / CloudField.Cell);
            for (var cy = cy0; cy <= cy1; cy++)
                for (var cx = cx0; cx <= cx1; cx++)
                {
                    if (!CloudField.TryGet(cx, cy, slice, out var c)) continue;
                    var depth = alt - c.Height;                       // + below us, - above us
                    if (MathF.Abs(depth) > World.ViewBoxFt) continue; // outside the view box
                    var tex = _clouds[c.Variant];
                    var origin = new Vector2(tex.Width / 2f, tex.Height / 2f);
                    var f = DistFactor(c.Height);

                    if (pass == CloudPass.Shadows)
                    {
                        // Soft shadow on the ground, where the sun would put it.
                        var sp = centre + (c.Pos + new Vector2(40f, 60f) - _pos) * zGround;
                        var ss = zGround * c.Size;
                        if (ss * tex.Width < 4f) continue;
                        _sb.Draw(tex, sp, null, Color.Black * 0.14f, 0f, origin, ss, SpriteEffects.None, 0f);
                        continue;
                    }

                    var above = depth < 0f;
                    if (above != (pass == CloudPass.Above) || f < 0.2f) continue;
                    var z = Scale * GroundZoom / f;
                    var scale = z * c.Size;
                    if (scale * tex.Width < 4f) continue;
                    var screen = centre + (c.Pos - _pos) * z;
                    // Fade out toward the edges of the box: passing through above us, thinning away below us.
                    var alpha = (above ? MathHelper.Clamp((f - 0.2f) / 0.5f, 0f, 1f) : MathHelper.Clamp((World.ViewBoxFt - depth) / 1000f, 0f, 1f)) * 0.9f;
                    _sb.Draw(tex, screen, null, Color.White * alpha, 0f, origin, scale, SpriteEffects.None, 0f);
                }
        }
    }

    private void DrawMenu()
    {
        if (!_menuOpen) return;
        var s = Scale;
        var px = Math.Max(2, (int)MathF.Round(2.6f * s));
        var white = new Color(234, 242, 255);
        var panel = MenuPanel();
        _sb.Draw(_pixel, new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height), new Color(0, 0, 0, 90));
        _sb.Draw(_pixel, panel, new Color(6, 14, 28, 235));
        PixelFont.Draw(_sb, _pixel, "DEBUG", new Vector2(panel.X + 16 * s, panel.Y + 16 * s), px * 2, new Color(255, 206, 84));
        for (var i = 0; i < MenuRows.Length; i++)
        {
            var r = MenuRowRect(i);
            if (i == _menuSel) _sb.Draw(_pixel, r, new Color(74, 163, 255, 70));
            var ty = r.Y + (r.Height - 7 * px) / 2;
            PixelFont.Draw(_sb, _pixel, MenuRows[i], new Vector2(r.X + 10 * s, ty), px, white);
            var v = MenuValue(i);
            if (v.Length > 0)
                PixelFont.Draw(_sb, _pixel, v, new Vector2(r.Right - 10 * s - PixelFont.Measure(v, px), ty), px,
                    new Color(94, 224, 160));
        }
        var hint = "UP/DOWN  ENTER OR CLICK  ESC CLOSE";
        var hpx = Math.Max(2, px - 1);
        PixelFont.Draw(_sb, _pixel, hint, new Vector2(panel.X + 16 * s, panel.Bottom - 16 * s - 7 * hpx), hpx, new Color(150, 165, 190));
    }

    private void DrawBanner()
    {
        if (_phase == Phase.Flying) return;
        var s = Scale;
        var px = Math.Max(3, (int)MathF.Round(6f * s));
        var title = _phase == Phase.Wrecked ? "DESTROYED" : "CRASH LANDED";
        var vp = GraphicsDevice.Viewport;
        PixelFont.Draw(_sb, _pixel, title, new Vector2((vp.Width - PixelFont.Measure(title, px)) / 2f, vp.Height * 0.2f), px,
            _phase == Phase.Wrecked ? new Color(255, 94, 94) : new Color(255, 206, 84));
        var px2 = Math.Max(2, px / 3);
        var sub = _phase == Phase.Wrecked ? _reason : "YOU WALKED AWAY";
        PixelFont.Draw(_sb, _pixel, sub, new Vector2((vp.Width - PixelFont.Measure(sub, px2)) / 2f, vp.Height * 0.2f + 9 * px), px2, Color.White);
        var hint = "PRESS R TO FLY AGAIN";
        PixelFont.Draw(_sb, _pixel, hint, new Vector2((vp.Width - PixelFont.Measure(hint, px2)) / 2f, vp.Height * 0.2f + 9 * px + 11 * px2), px2, new Color(234, 242, 255));
    }

    /// <summary>HITBOXES debug view: each part of the plane being aimed at (or hovered) with its hit points and state.</summary>
    private void DrawDamageReadout()
    {
        var p = _tracked ?? _hovered;
        if (p == null) return;
        var s = Scale;
        var px = Math.Max(2, (int)MathF.Round(2.2f * s));
        int w = (int)(300 * s), rowH = 10 * px;
        var x = GraphicsDevice.Viewport.Width - w - (int)(24 * s);
        var y = (int)(24 * s);
        _sb.Draw(_pixel, new Rectangle(x - 10, y - 10, w + 20, rowH * (DamageModel.PartCount + 1) + 14), new Color(6, 14, 28, 190));
        PixelFont.Draw(_sb, _pixel, "TARGET DAMAGE", new Vector2(x, y), px, new Color(255, 206, 84));
        for (var i = 0; i < DamageModel.PartCount; i++)
        {
            var hp = p.Parts[i].Hp;
            var state = DamageModel.StateOf(hp);
            var ry = y + (i + 1) * rowH;
            var col = state == PartState.Black ? new Color(90, 90, 90) : DamageModel.StateColor(state);   // black on the dark panel: grey
            PixelFont.Draw(_sb, _pixel, DamageModel.Name((Part)i), new Vector2(x, ry), px, col);
            var v = $"{MathF.Ceiling(hp):0} {DamageModel.Name(state)}";
            PixelFont.Draw(_sb, _pixel, v, new Vector2(x + w - PixelFont.Measure(v, px), ry), px, col);
        }
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

        // Flight instruments live on the panel; this is just the throttle quadrant and status text.
        // "HUD BARS" in the debug menu adds the old digital bars back.
        var line = 0;
        Row(line++, "THROTTLE", $"{_fm.Throttle * 100f:0}%", _fm.Throttle, new Color(94, 224, 160));
        var ammo = _guns.AmmoLeft;
        Row(line++, "AMMO", $"{ammo}", ammo / (float)(Guns.GunCount * Guns.RoundsPerGun), new Color(255, 190, 70));
        // Engine: hit points are the power available, in the colour of its damage band (black drawn as dark grey here).
        var eng = _dmg.EngineHp / DamageTuning.MaxHp;
        var engCol = _dmg.EngineState == PartState.Black ? new Color(90, 90, 90) : DamageModel.StateColor(_dmg.EngineState);
        Row(line++, "ENGINE", $"{eng * 100f:0}% POWER", eng, engCol);
        Row(line++, "FUEL", _dmg.Leaks > 0 ? $"LEAK X{_dmg.Leaks}  {_dmg.FuelGal:0} GAL" : $"{_dmg.FuelGal:0} GAL",
            _dmg.FuelGal / DamageTuning.FuelCapacityGal, _dmg.Leaks > 0 ? new Color(255, 140, 40) : new Color(120, 200, 255));
        if (_hudBars)
        {
            var ias = _fm.IasMph;
            var stallIas = _fm.StallSpeed(1f) * MathF.Sqrt(_fm.Rho / 0.0023769f) * 0.681818f;
            Row(line++, "AIRSPEED", $"{ias:0} MPH IAS", ias / 500f,
                _fm.Overspeed ? new Color(255, 94, 94) : new Color(255, 206, 84), stallIas / 500f, FlightModel.VneMph / 500f);
            Row(line++, "ALTITUDE", $"{_fm.Altitude:N0} FT", _fm.Altitude / FlightModel.CeilingFt, new Color(74, 163, 255));
            Row(line++, "CLIMB", $"{_fm.VerticalSpeedFpm:+0;-0;0} FT/MIN",
                (_fm.VerticalSpeedFpm + 6000f) / 12000f, _fm.VerticalSpeedFpm < -50f ? new Color(255, 94, 94) : new Color(94, 224, 160), 0.5f);
        }
        var ty = y + line * rowH;
        PixelFont.Draw(_sb, _pixel, $"TAS {_fm.TasMph:0} MPH   MACH {_fm.Mach:0.00}   G {_fm.LoadFactor:0.0}", new Vector2(x, ty), px, white);
        PixelFont.Draw(_sb, _pixel, $"PITCH {MathHelper.ToDegrees(_fm.Gamma):+0;-0;0}  SET {_fm.PitchCmdDeg:+0;-0;0}", new Vector2(x, ty + 10 * px), px, white);
        PixelFont.Draw(_sb, _pixel, $"HITS {_worldModel.Hits}", new Vector2(x, ty + 20 * px), px, white);
        if (_arcade) PixelFont.Draw(_sb, _pixel, "ARCADE", new Vector2(x, ty + 30 * px), px, new Color(255, 206, 84));

        var wy = ty + 42 * px;
        if (_fm.Stalled) PixelFont.Draw(_sb, _pixel, "STALL", new Vector2(x, wy), px * 2, new Color(255, 94, 94));
        else if (_fm.Overspeed) PixelFont.Draw(_sb, _pixel, "OVERSPEED", new Vector2(x, wy), px * 2, new Color(255, 94, 94));
        wy += 20 * px;
        if (_dmg.OnFire && (_time * 3f) % 1f < 0.6f)
            PixelFont.Draw(_sb, _pixel, $"ENGINE FIRE {_dmg.FireStrength * 100f:0}%", new Vector2(x, wy), px * 2, new Color(255, 120, 40));
        else if (_dmg.FuelGal <= 0f) PixelFont.Draw(_sb, _pixel, "OUT OF FUEL", new Vector2(x, wy), px * 2, new Color(255, 94, 94));
        else if (_dmg.EngineHp <= 0f) PixelFont.Draw(_sb, _pixel, "ENGINE DEAD", new Vector2(x, wy), px * 2, new Color(255, 94, 94));
    }
}
