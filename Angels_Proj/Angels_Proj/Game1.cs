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
    private const float DeadzonePx = 20f;
    private const float PxPerFoot = 1.2f;        // screen px per ft of ground travel, at 720p

    // Camera: the further above the ground, the smaller everything on it looks.
    private const float GroundZoom = 0.55f, ZoomAltScaleFt = 4000f;

    // Crash rules.
    private const float SurvivableSinkFpm = 1000f, SurvivableSpeedMph = 200f; // gentle enough to skid in
    private const float SkidDecelFtS2 = 18f;                                  // belly friction

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
    private const float DotRadiusSpans = 0.5f;   // the sight's dot is "on" a plane within half its wingspan of its centre
    private const int ExitKeepTicks = 60;        // once the sight starts to fade the plane keeps its trajectory this long, pointer hidden
    private const int ExitLandTicks = 8;         // then the pointer pops up on the trajectory and steering waits this long for it to land
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

    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _sb;
    private Texture2D _pixel, _grass;
    private Texture2D[] _clouds;
    private readonly Fx _fx = new();
    private EffectArt _effects;                 // fire animation and smoke sprites
    private float _time;                        // seconds of play, for animations
    private Traffic _traffic;                    // brings AI formations into the world and takes them away
    private readonly System.Collections.Generic.List<Aircraft> _craft = new(); // draw-sorted copy
    private Gunsight _gunsight;
    private RenderTarget2D _world;              // the ground layer, faded into the sky as we climb out of the view box
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
    private int _exitHold;                      // ticks left of ignoring the pointer after mouse aim ends (keep, then land)
    private float _exitHeading;                 // the heading kept while the sight fades
    private Point _aimPrev;                      // the pointer's last reading during mouse aim
    private int _aimSkip;                        // readings to ignore after the game has moved the pointer
    private Aircraft _hovered;             // the plane under the mouse on the map, whose hit boxes are shown
    private readonly System.Collections.Generic.List<(Vector2[] poly, float damage)> _hoverZones = new();
    private bool _showHitboxes;                 // debug menu: draw the planes' hit boxes on the map and in the sight
    private readonly System.Collections.Generic.List<(Vector3 a, Vector3 b, Color color)> _boxEdges = new();
    private bool _mouseAim;                      // the pointer is captured for mouse aim
    private float _aimYaw;                       // rad: the mouse's heading offset from the target
    private Aircraft _lastPointed;          // the plane the pointing assist followed last tick, for its rates
    private int _pointDwell;                     // ticks the mouse has stayed on that plane
    private float _lastPointBearing, _lastPointElev;
    private Aircraft _lastAimed;            // the target the assist tracked last tick, for its bearing rate
    private float _lastAimBearing, _lastAimElev;
    // Tests (debug menu). Each puts a target in the air and holds the player on a sphere round it, facing its centre, the
    // sight up: the gun test with the target held still, the cloud tests with it flying straight through a cloud.
    private enum TestMode { None, Gun, FollowThroughCloud, CloudIntoView }
    private TestMode _test;
    private bool InTest => _test != TestMode.None;
    private Vector3 _testCloudFt;                        // the cloud a cloud test flies through (world feet)
    // Debug menu, and the PERF overlay's timings.
    private readonly MenuPanel _menu = new("DEBUG", false);
    private readonly MenuPanel _options = new("OPTIONS", true);   // Esc: pauses the game
    private bool _graphicsHigh = true;                            // the graphics setting: HIGH or LOW
    private const int LowSmokeBudget = 160;                       // LOW: most smoke puffs drawn at once in the gunsight
    private bool _menuOpen { get => _menu.Open; set => _menu.Open = value; }
    private readonly Perf _perf = new();
    private bool _showPerf;
    private bool _cloudShadows = true;          // clouds' shadows on the ground (debug menu, for finding frame drops)
    private int _mapClouds;                              // clouds the map drew last frame
    private float _mapCloudFill;                         // and how many screens over they cover
    private float _frameMs, _frameMsMax, _frameMsMaxShown; // time between frames drawn; the worst in the last half second
    private long _lastFrameTick;
    // FPS counter: frames drawn, counted over half-second spans of real time.
    private bool _showFps = true;
    private readonly System.Diagnostics.Stopwatch _fpsClock = System.Diagnostics.Stopwatch.StartNew();
    private int _fpsFrames;
    private float _fps;
    private Aircraft _testTarget;
    private float _testAz, _testEl, _testRange;          // rad round the target from its nose-on side, rad up, feet
    private const float TestOrbitDegPerPx = 0.3f;        // mouse movement to orbit
    private const float TestRangeStepFt = 50f;           // per wheel notch
    private Aircraft _tracked; // the plane the aimer is on: picked with the mouse, kept while it stays in the sight's view
    private float _sightAlpha; // gunsight window: fades in when a target is in view, out when none is
    private bool _firing;
    private AircraftArt _art;                   // draws every aircraft on the map: picture, propellers, fire

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
    private bool _arcade, _cloudsOn = true, _hudBars;
    private readonly Random _rng = new();
    private Phase _phase = Phase.Flying;
    private string _reason = "";
    private float _phaseTime;
    private readonly System.Collections.Generic.List<Particle> _particles = new();

    public Game1()
    {
        _worldModel.SpawnPlayer(_pilot, Vector2.Zero, false);
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
        SpriteSphere.Device = GraphicsDevice;   // each aircraft type loads its own sprite sheet when first drawn
        _traffic = new Traffic();
        _effects = new EffectArt(GraphicsDevice);
        _art = new AircraftArt(_pixel, _effects);
        _gunsight = new Gunsight(GraphicsDevice, _sb, _effects);
        BuildMenu();
        BuildOptions();
        ApplyGraphics();
        var vp0 = GraphicsDevice.Viewport;
        _world = new RenderTarget2D(GraphicsDevice, vp0.Width, vp0.Height);
        _grass = Art.Grass(GraphicsDevice);
        _clouds = new[] { Art.Cloud(GraphicsDevice, 11), Art.Cloud(GraphicsDevice, 23), Art.Cloud(GraphicsDevice, 37) };
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
        if (_testTarget != null) { _worldModel.Remove(_testTarget); _testTarget = null; }   // (ends a gun test)
        _test = TestMode.None;
        IsMouseVisible = true;
        _mouseAim = false;
        _exitHold = 0;
        _worldModel.SpawnPlayer(_pilot, _pos, _arcade);   // a fresh aircraft where the old one was
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

    /// <summary>Whether any of a plane is on the screen: inside the view box, and its sprite (about a wingspan across) at
    /// least partly inside the window, with the camera where it is now.</summary>
    private bool OnScreen(Aircraft p)
    {
        var f = DistFactor(p.Altitude);
        if (f < 0.2f || _fm.Altitude - p.Altitude > World.ViewBoxFt) return false;
        var vp = GraphicsDevice.Viewport;
        var k = Scale * GroundZoom / f;
        var at = PlaneScreen() + (p.Pos - _pos) * k;
        var r = p.Airframe.Span * 0.5f * World.PxPerFoot * k;
        return at.X > -r && at.X < vp.Width + r && at.Y > -r && at.Y < vp.Height + r;
    }

    /// <summary>The nearest plane under the gunsight's centre dot (straight down the nose), within the view box: the dot
    /// counts as on it within DotRadiusSpans of its wingspan of its centre.</summary>
    private Aircraft PlaneOnDot(Vector3 camFt, Vector3 forward)
    {
        Aircraft best = null;
        var bestAlong = float.MaxValue;
        foreach (var p in _worldModel.Others)
        {
            var rel = World.ToFt(p.Pos, p.Altitude) - camFt;
            var along = Vector3.Dot(rel, forward);
            if (along < 20f || rel.LengthSquared() > World.ViewBoxFt * World.ViewBoxFt) continue;
            if ((rel - forward * along).Length() > p.Airframe.Span * DotRadiusSpans) continue;
            if (along < bestAlong) { bestAlong = along; best = p; }
        }
        return best;
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
    // This is separate from the hit boxes that rounds are tested against (see each aircraft's parts, Spitfire.HitBoxes, and the HITBOXES view).
    private const float BoxX0 = -46f, BoxY0 = -42f, BoxX1 = 46f, BoxY1 = 40f;
    private const float HoverPadPx = 4f;             // sprite px of slack round the box, so the plane is easy to hover

    /// <summary>The on-screen targeting box of a traffic plane, as the sprite is drawn: scaled by its distance and
    /// squashed by its bank and pitch, then turned to its heading. pad grows it outward, in sprite px.</summary>
    private void SpriteZones(Aircraft p, Vector2 centre, System.Collections.Generic.List<(Vector2[] poly, float damage)> into, float pad = 0f)
    {
        var s = Scale;
        var ps = s * 0.8f * (0.85f + 0.3f * MathF.Sqrt(MathHelper.Clamp(_fm.Altitude / _fm.Airframe.CeilingFt, 0f, 1f)));
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

    /// <summary>One tick with the trigger held: every gun fires its share of rounds, each leaving at muzzle velocity
    /// plus the plane's own velocity.</summary>
    private void Fire()
    {
        _firing = true;
        World.Basis(_fm.Heading, _fm.Gamma, _fm.Bank, out var r, out var u, out var f);
        var gs = _fm.GroundSpeed;
        var vel = new Vector3(MathF.Sin(_fm.Heading) * gs, _fm.Speed * MathF.Sin(_fm.Gamma) * _fm.VerticalRateScale, -MathF.Cos(_fm.Heading) * gs);
        // Where each muzzle is on the map, from the picture as it is drawn (bigger than life), so the round starts at the
        // barrel tip on screen: the muzzle in the sphere's frame (x nose, y left, z up; its centre is a little ahead of
        // the plane's origin and above the nose's axis), put where the picture shows it.
        var kws = 0.8f * (0.85f + 0.3f * MathF.Sqrt(MathHelper.Clamp(_fm.Altitude / _fm.Airframe.CeilingFt, 0f, 1f)))
                  * Player.Sphere.MapScale / GroundZoom;                                 // world px per px of the sphere's picture
        var view = AircraftArt.MapView(Player);
        var mapMuzzles = new Vector2[Guns.GunCount];
        for (var g = 0; g < Guns.GunCount; g++)
        {
            var m = Guns.Muzzles[g];
            mapMuzzles[g] = _pos + view.Project(new Vector3(m.Z, -m.X, m.Y) + Player.Sphere.MuzzleShift) * kws;
        }
        _guns.Fire(World.ToFt(_pos, _fm.Altitude), r, u, f, vel, mapMuzzles);
    }

    /// <summary>Gun test: clears the sky and puts a Spitfire still in the air ahead, hit boxes on, and holds the player
    /// on a sphere round it at the guns' convergence range, facing its centre with the sight up. Moving the mouse orbits
    /// round it, the wheel changes the range, firing works as usual. R (or the menu) ends it.</summary>
    private void StartGunTest()
    {
        var target = BeginTest(TestMode.Gun, _pos, MathF.Max(_fm.Altitude, 3000f), 0f);
        target.Frozen = true;
        target.Flight.Speed = 0f;
        _testEl = 0.17f;               // a little above
        _testRange = Guns.ConvergeFt;
        _showHitboxes = true;
    }

    /// <summary>The cloud tests, for chasing down frame-rate drops: a Spitfire flies straight and level through a cloud
    /// with the player held close behind it, sight up. FollowThroughCloud starts it 2,500 ft short of the cloud (already in
    /// view); CloudIntoView starts it 6,000 ft short, so the cloud first comes into the view box (3,200 ft) during the run.
    /// The mouse still orbits and the wheel still changes the range; R ends it.</summary>
    private void StartCloudTest(TestMode mode)
    {
        // The nearest cloud to the player, at a height where there are plenty.
        var puffs = new System.Collections.Generic.List<CloudField.Puff>();
        CloudField.Query(_pos, 40000f, 12000f, 6000f, puffs);
        if (puffs.Count == 0) return;
        var cloud = puffs[0];
        foreach (var p in puffs)
            if (Vector2.DistanceSquared(p.Pos, _pos) < Vector2.DistanceSquared(cloud.Pos, _pos)) cloud = p;
        _testCloudFt = World.ToFt(cloud.Pos, cloud.Height);
        // Line up on it from the player's side.
        var toCloud = cloud.Pos - _pos;
        var heading = MathF.Atan2(toCloud.X, -toCloud.Y);
        var dir = new Vector2(MathF.Sin(heading), -MathF.Cos(heading));
        var startFt = mode == TestMode.CloudIntoView ? 6000f : 2500f;
        var target = BeginTest(mode, cloud.Pos - dir * startFt * World.PxPerFoot, cloud.Height, heading);
        target.Pilot = new FormationPilot { CourseHeading = heading, TurnRate = 0f, CruiseAltFt = cloud.Height, CruiseThrottle = 0.6f };
        target.Flight.Speed = 280f / FlightModel.Mph;
        target.Flight.SnapOnRelease = false;
        target.Flight.Throttle = 0.6f;
        _testEl = 0.06f;
        _testRange = 350f;
    }

    /// <summary>What every test does first: clears the sky, puts a Spitfire at pos (world px), altFt, heading, and starts
    /// the player behind it with the sight up.</summary>
    private Aircraft BeginTest(TestMode mode, Vector2 pos, float altFt, float heading)
    {
        if (_phase != Phase.Flying || InTest) Reset();
        _traffic.Clear(_worldModel);
        _testTarget = new Spitfire(null) { Pos = pos };
        _testTarget.Flight.Altitude = altFt;
        _testTarget.Flight.Heading = heading;
        _worldModel.Add(_testTarget);
        _testAz = MathF.PI;            // behind it
        _test = mode;
        _menuOpen = false;
        _pan = Vector2.Zero;
        var m = Mouse.GetState();
        _aimPrev = new Point(m.X, m.Y);
        _aimSkip = AimWarpSkipTicks;
        _lastWheel = m.ScrollWheelValue;
        return _testTarget;
    }

    private void UpdateTest(MouseState m)
    {
        var vp = GraphicsDevice.Viewport;
        IsMouseVisible = false;
        // Orbit by the mouse's movement (the pointer is hidden; only movement counts, recentred near an edge).
        var now = new Point(m.X, m.Y);
        if (IsActive)
        {
            if (_aimSkip > 0) _aimSkip--;
            else
            {
                float dx = MathHelper.Clamp(now.X - _aimPrev.X, -AimMaxStepPx, AimMaxStepPx);
                float dy = MathHelper.Clamp(now.Y - _aimPrev.Y, -AimMaxStepPx, AimMaxStepPx);
                _testAz = MathHelper.WrapAngle(_testAz + MathHelper.ToRadians(dx * TestOrbitDegPerPx));
                _testEl = MathHelper.Clamp(_testEl - MathHelper.ToRadians(dy * TestOrbitDegPerPx), -1.48f, 1.48f);
            }
            _aimPrev = now;
            if (now.X < vp.Width / 5 || now.X > vp.Width * 4 / 5 || now.Y < vp.Height / 5 || now.Y > vp.Height * 4 / 5)
            {
                Mouse.SetPosition(vp.Width / 2, vp.Height / 2);
                _aimSkip = AimWarpSkipTicks;
            }
        }
        var wheel = m.ScrollWheelValue;
        _testRange = MathHelper.Clamp(_testRange - (wheel - _lastWheel) / 120f * TestRangeStepFt, 150f, 2000f);
        _lastWheel = wheel;

        // The player on the sphere round the target's centre (azimuth taken from the target's nose, so "behind" stays behind
        // as it flies), facing it, level wings, at the target's speed.
        var local = new Vector3(MathF.Sin(_testAz) * MathF.Cos(_testEl), MathF.Sin(_testEl), -MathF.Cos(_testAz) * MathF.Cos(_testEl));
        float ch = MathF.Cos(_testTarget.Heading), sh = MathF.Sin(_testTarget.Heading);
        var toMe = new Vector3(local.X * ch - local.Z * sh, local.Y, local.X * sh + local.Z * ch);
        var me = _testTarget.PositionFt + toMe * _testRange;
        var look = -toMe;
        _fm.Heading = MathF.Atan2(look.X, -look.Z);
        _fm.Gamma = MathF.Asin(MathHelper.Clamp(look.Y, -1f, 1f));
        _fm.Bank = 0f;
        _fm.Speed = _testTarget.Flight.Speed;
        _fm.Altitude = me.Y;
        _pos = new Vector2(me.X, me.Z) * World.PxPerFoot;

        // The world runs on round them: the target's damage, smoke and fire, the rounds in flight, the hit boxes.
        _fx.Update();
        _time += 1f / 60f;
        _worldModel.UpdateOthers(_fx, _rng);
        _boxEdges.Clear();
        if (_showHitboxes) foreach (var p in _worldModel.Others) World.HitBoxEdges(p, _boxEdges);
        _gunsight.DebugLines.Clear();
        foreach (var e in _boxEdges) _gunsight.DebugLines.Add((e.a, e.b, e.color));
        _guns.Update(_worldModel, Player, _fx);
        _guns.Tracers(_tracers);
        _firing = false;
        _tracked = _testTarget;
        _hovered = null;
        _mouseAim = false;
        _sightAlpha = 1f;
        if (_kb.IsKeyDown(Keys.Space) || m.LeftButton == ButtonState.Pressed) Fire();
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
                _codeBuffer = "";
            }
        }
    }

    /// <summary>The options menu (Esc).</summary>
    private void BuildOptions()
    {
        var o = _options;
        o.Section("SETTINGS");
        o.Add("GRAPHICS", () => _graphicsHigh ? "HIGH" : "LOW", () => { _graphicsHigh = !_graphicsHigh; ApplyGraphics(); });
        o.Section("");
        o.Add("RESUME", null, () => _options.Open = false);
        o.Add("QUIT", null, Exit);
    }

    /// <summary>Puts the graphics setting into effect. HIGH: every smoke puff drawn; LOW: the gunsight draws at most
    /// LowSmokeBudget of them.</summary>
    private void ApplyGraphics()
    {
        _gunsight.SmokeBudget = _graphicsHigh ? 0 : LowSmokeBudget;
    }

    private Part _debugPart = Part.Engine;    // the part the debug menu's damage buttons hit

    /// <summary>Who the debug menu's HIT TARGET hits: the test target, else the aircraft being aimed at, else the nearest.</summary>
    private Aircraft DebugTarget()
    {
        if (_testTarget != null && InTest) return _testTarget;
        if (_tracked != null) return _tracked;
        Aircraft best = null;
        var bestD = float.MaxValue;
        foreach (var a in _worldModel.Others)
        {
            var d = Vector2.DistanceSquared(a.Pos, _pos);
            if (d < bestD) { bestD = d; best = a; }
        }
        return best;
    }

    /// <summary>The debug menu's options, by section.</summary>
    private void BuildMenu()
    {
        var m = _menu;
        m.Section("FLIGHT");
        m.Add("MODE", () => _arcade ? "ARCADE" : "REALISM", () =>
        {
            _arcade = !_arcade;
            _fm.VerticalRateScale = _arcade ? 2f : 1f; // arcade: altitude gain and loss twice as fast
        });
        m.Section("VIEW");
        m.Add("HUD BARS", () => MenuPanel.OnOff(_hudBars), () => _hudBars = !_hudBars);
        m.Add("HITBOXES", () => MenuPanel.OnOff(_showHitboxes), () => _showHitboxes = !_showHitboxes);
        m.Add("FPS", () => MenuPanel.OnOff(_showFps), () => _showFps = !_showFps);
        m.Add("PERF", () => MenuPanel.OnOff(_showPerf), () => _showPerf = !_showPerf);
        m.Section("RENDER");
        m.Add("MAP CLOUDS", () => MenuPanel.OnOff(_cloudsOn), () => _cloudsOn = !_cloudsOn);
        m.Add("CLOUD SHADOWS", () => MenuPanel.OnOff(_cloudShadows), () => _cloudShadows = !_cloudShadows);
        m.Add("SIGHT CLOUDS", () => MenuPanel.OnOff(_gunsight.ShowClouds), () => _gunsight.ShowClouds = !_gunsight.ShowClouds);
        m.Add("SIGHT BLUR", () => MenuPanel.OnOff(_gunsight.Blur), () => _gunsight.Blur = !_gunsight.Blur);
        m.Add("SIGHT SMOKE", () => MenuPanel.OnOff(_gunsight.ShowSmoke), () => _gunsight.ShowSmoke = !_gunsight.ShowSmoke);
        m.Add("SMOKE BUDGET", () => _gunsight.SmokeBudget > 0 ? _gunsight.SmokeBudget.ToString() : "NONE", () =>
            _gunsight.SmokeBudget = _gunsight.SmokeBudget switch { 0 => 80, 80 => 160, 160 => 320, _ => 0 });
        m.Section("DAMAGE");
        m.Add("PART", () => DamageModel.Name(_debugPart), () => _debugPart = (Part)(((int)_debugPart + 1) % DamageModel.PartCount));
        m.Add("HIT OWN -25", () => $"{MathF.Ceiling(_dmg[_debugPart].Hp):0} HP", () => _dmg.DamagePart(_debugPart, 25f, _rng));
        m.Add("HIT TARGET -25", () => DebugTarget() is { } t ? $"{MathF.Ceiling(t[_debugPart].Hp):0} HP" : "NONE",
            () => DebugTarget()?.DamagePart(_debugPart, 25f, _rng));
        m.Section("TESTS");
        m.Add("GUN TEST", () => MenuPanel.OnOff(_test == TestMode.Gun), () => { if (_test == TestMode.Gun) Reset(); else StartGunTest(); });
        m.Add("FPS: FOLLOW THROUGH CLOUD", () => MenuPanel.OnOff(_test == TestMode.FollowThroughCloud),
            () => { if (_test == TestMode.FollowThroughCloud) Reset(); else { StartCloudTest(TestMode.FollowThroughCloud); _showPerf = true; } });
        m.Add("FPS: CLOUD INTO VIEW", () => MenuPanel.OnOff(_test == TestMode.CloudIntoView),
            () => { if (_test == TestMode.CloudIntoView) Reset(); else { StartCloudTest(TestMode.CloudIntoView); _showPerf = true; } });
        m.Add("SPAWN TARGETS", null, () =>
        {
            _traffic.SpawnAhead(_worldModel, _pos, _fm.Altitude, _fm.Heading, 900f, MathF.Max(120f, _fm.TasMph - 40f));
            _menuOpen = false;
        });
        m.Section("");
        m.Add("CLOSE", null, () => _menuOpen = false);
    }

    protected override void Update(GameTime gameTime)
    {
        var t0 = _perf.Now;
        UpdateGame(gameTime);
        _perf.Add("UPDATE", t0);
    }

    private void UpdateGame(GameTime gameTime)
    {
        _prevKb = _kb;
        _kb = Keyboard.GetState();
        var kb = _kb;
        var m = Mouse.GetState();
        if (_options.Open)
        {
            // The options menu pauses the game; Esc (or RESUME) carries on.
            IsMouseVisible = true;
            _mouseAim = false;
            _options.Update(Pressed, m, m.LeftButton == ButtonState.Pressed && !_prevLeft, GraphicsDevice.Viewport.Bounds, Scale);
            _lastWheel = m.ScrollWheelValue;
            _prevLeft = m.LeftButton == ButtonState.Pressed;
            base.Update(gameTime);
            return;
        }
        TrackSecretCode();
        if (_menuOpen)
        {
            IsMouseVisible = true; // the menu needs the pointer, even mid-aim
            _mouseAim = false;
            // The game is paused while the menu is open.
            _menu.Update(Pressed, m, m.LeftButton == ButtonState.Pressed && !_prevLeft, GraphicsDevice.Viewport.Bounds, Scale);
            _lastWheel = m.ScrollWheelValue;
            _prevLeft = m.LeftButton == ButtonState.Pressed;
            base.Update(gameTime);
            return;
        }
        _prevLeft = m.LeftButton == ButtonState.Pressed;
        // Esc opens the options menu. A fresh press only: the press that closed a menu is still held on the next tick and
        // mustn't open it again.
        if (Pressed(Keys.Escape))
        {
            _options.Open = true;
            base.Update(gameTime);
            return;
        }
        if (Pressed(Keys.R)) Reset();
        if (InTest)
        {
            UpdateTest(m);
            base.Update(gameTime);
            return;
        }

        var vp = GraphicsDevice.Viewport;

        // Camera pan: holding the right button, mouse movement pans the camera (forward pans north, and so on), up to the
        // point where the plane reaches the edge of the screen. The pointer is hidden and steering is held on where the
        // pointer was when the pan began; letting go puts the pointer back there and the camera eases back to the plane.
        // Like mouse aim, only the movement since the last reading counts and the pointer is recentred near an edge.
        var rightDown = m.RightButton == ButtonState.Pressed;
        if (rightDown && _phase == Phase.Flying && !_mouseAim && _exitHold == 0)   // (no panning in the window after aim)
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
            // limit as the manual pan (the plane stays on screen; the pan is held to that below). The target is only let
            // go once it is right off the screen (see OnScreen): then the aimer fades out and the camera recentres.
            var panMargin0 = PanMarginPx * Scale;
            var following = false;
            if (_tracked != null && _phase == Phase.Flying && _worldModel.Others.Contains(_tracked))
            {
                var tk = Scale * GroundZoom / MathF.Max(0.2f, DistFactor(_tracked.Altitude));
                var t0 = new Vector2(vp.Width / 2f, vp.Height / 2f) + (_tracked.Pos - _pos) * tk;   // where it is with no pan
                var want = new Vector2(
                    t0.X < panMargin0 ? t0.X - panMargin0 : t0.X > vp.Width - panMargin0 ? t0.X - (vp.Width - panMargin0) : 0f,
                    t0.Y < panMargin0 ? t0.Y - panMargin0 : t0.Y > vp.Height - panMargin0 ? t0.Y - (vp.Height - panMargin0) : 0f);
                following = true;
                _pan += (want - _pan) * PanFollow;
                _panReleaseLen = 0f;   // no steering blend while following: the mouse is aiming, not steering
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

        // The aimer comes up when the mouse is over a plane on the map that is also inside the gunsight's view.
        // After that the mouse is free: the aimer stays on that plane until the sight loses it.
        var rect = Instruments.GunsightRect(vp.Bounds, Scale);
        var aspect = (float)rect.Width / Math.Max(1, rect.Height);
        World.Basis(_fm.Heading, _fm.Gamma, _fm.Bank, out var sr, out var su, out var sf);
        var camFt = World.ToFt(_pos, _fm.Altitude);
        // While the sight is up (aiming, or still fading out after losing its target, or the pointer still landing after
        // aim) the pointer picks nothing: the hidden pointer, or the one just put back, can't lock onto another plane
        // and yank the aim across to it. Only once the sight is down can the mouse pick a target again.
        var sightUp = _mouseAim || _sightAlpha > 0.01f || _exitHold > 0;
        if (sightUp) pickP = new Vector2(-9999f, -9999f);
        _hovered = _mouseAim || _phase != Phase.Flying ? null : PlaneAimedAt(pickP, camFt, sr, su, sf, aspect, needSight: false);
        _hoverZones.Clear();
        if (_hovered != null && _showHitboxes) SpriteZones(_hovered, PlaneScreen(), _hoverZones); // drawn in the HITBOXES debug view only
        // The aimer holds its target until the target is right off the screen, even if it leaves the sight's view; a
        // target is first picked with the mouse (on a plane the sight can see). While the sight is showing, putting its
        // centre dot on another plane switches to that one.
        if (_tracked == null || _phase != Phase.Flying || !_worldModel.Others.Contains(_tracked) || !OnScreen(_tracked))
            _tracked = PlaneAimedAt(pickP, camFt, sr, su, sf, aspect);
        if (_phase == Phase.Flying && (_tracked != null || _sightAlpha > 0.01f) && PlaneOnDot(camFt, sf) is { } onDot)
            _tracked = onDot;
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
            // The sight has lost its target and starts to fade: a hard window. For a whole second the plane keeps the
            // trajectory it was on (wings level on this heading, the nose where it is) with the pointer still hidden and
            // nothing to be picked; then the pointer pops up on that trajectory (see the steering below).
            _mouseAim = false;
            _exitHeading = _fm.Heading;
            _exitHold = ExitKeepTicks + ExitLandTicks;
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
            _exitHold--;
            if (_exitHold > ExitLandTicks)
            {
                // Keeping the trajectory: hold the heading the plane was on when the sight began to fade.
                var err = MathHelper.WrapAngle(_exitHeading - _fm.Heading);
                targetBank = MathHelper.Clamp(err / HeadingSeekRef, -1f, 1f) * FlightModel.MaxBank;
            }
            else if (_exitHold == ExitLandTicks)
            {
                // The window is over: the pointer pops up in line with the trajectory, out from the plane along its
                // heading, so steering toward it carries straight on. Steering waits a few more ticks while it lands
                // (on macOS a pointer moved by the game lands late).
                IsMouseVisible = true;
                var hdg = new Vector2(MathF.Sin(_fm.Heading), -MathF.Cos(_fm.Heading));
                var exit = PlaneScreen() + hdg * ExitPointerPx * Scale;
                exit = Vector2.Clamp(exit, new Vector2(8f, 8f), new Vector2(vp.Width - 8f, vp.Height - 8f));
                if (IsActive) Mouse.SetPosition((int)exit.X, (int)exit.Y);
            }
        }
        else if (d.Length() > DeadzonePx)
        {
            var err = MathHelper.WrapAngle(MathF.Atan2(d.X, -d.Y) - _fm.Heading);
            targetBank = MathHelper.Clamp(err / HeadingSeekRef, -1f, 1f) * FlightModel.MaxBank;
        }

        // The player's aircraft flies a tick: the pilot (keys, and the bank wanted from the mouse) sets its control inputs,
        // then its engine, damage and flight model do the rest and it moves (Aircraft.Step).
        _pilot.Bank = targetBank / FlightModel.MaxBank;
        Player.Step(_fx, _rng);
        if (kb.IsKeyDown(Keys.Space) || m.LeftButton == ButtonState.Pressed) Fire();

        if (_fm.GroundHit)
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
        // Time between frames drawn (what the FPS is), and the worst of the last half second.
        var nowTick = _perf.Now;
        if (_lastFrameTick != 0)
        {
            _frameMs = (nowTick - _lastFrameTick) * 1000f / System.Diagnostics.Stopwatch.Frequency;
            _frameMsMax = MathF.Max(_frameMsMax, _frameMs);
        }
        _lastFrameTick = nowTick;
        DrawGame(gameTime);
        _perf.Add("DRAW", nowTick);
        _perf.EndFrame();
    }

    private void DrawGame(GameTime gameTime)
    {
        var vp = GraphicsDevice.Viewport;
        float w = vp.Width, h = vp.Height, s = Scale, z = Zoom;
        var centre = PlaneScreen();

        // The gunsight's 3D view goes into its own render target before anything is drawn to the screen.
        var sightRect = Instruments.GunsightRect(vp.Bounds, s);
        var tSight = _perf.Now;
        if (_sightAlpha > 0.01f)
            _gunsight.Render(sightRect.Width, sightRect.Height, World.ToFt(_pos, _fm.Altitude), _fm.Heading, _fm.Gamma, _fm.Bank,
                _worldModel.Others, _fx, _tracers, _fm.Throttle);
        else { _gunsight.CloudsDrawn = 0; _gunsight.CloudFill = 0f; }
        _perf.Add("SIGHT", tSight);
        _mapClouds = 0;
        _mapCloudFill = 0f;

        _craft.Clear();
        _craft.AddRange(_worldModel.Others);
        _craft.Sort((a, b) => a.Altitude.CompareTo(b.Altitude));

        // The ground is only inside the view box while we are within ViewBoxFt of it: clear and sharp all the way up,
        // fading into the sky over the last stretch below the top of the box.
        var vis = 1f - World.Smooth(0.85f, 1f, _fm.Altitude / World.ViewBoxFt);
        var tGround = _perf.Now;
        if (vis > 0.002f) DrawGroundLayer(centre);
        _perf.Add("GROUND", tGround);

        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(SkyColor());
        _sb.Begin(samplerState: SamplerState.LinearClamp);
        if (vis > 0.002f) _sb.Draw(_world, new Rectangle(0, 0, (int)w, (int)h), Color.White * vis);

        var tc = _perf.Now;
        if (_cloudsOn) DrawClouds(centre, CloudPass.Below);
        _perf.Add("MAP CLOUDS", tc);
        tc = _perf.Now;
        DrawTraffic(centre, TrafficPass.Below);
        _perf.Add("TRAFFIC", tc);

        // Plane: pitching foreshortens the fuselage as seen from above. Altitude reads as size and as how far
        // the shadow drifts from the plane.
        if (_phase != Phase.Wrecked)
        {
            var shadowT = MathHelper.Clamp(_fm.Altitude / 5000f, 0f, 2.5f);
            var sc = s * 0.8f * (0.85f + 0.3f * MathF.Sqrt(MathHelper.Clamp(_fm.Altitude / _fm.Airframe.CeilingFt, 0f, 1f))) * Player.Sphere.MapScale;
            var shadowAt = centre + new Vector2(0.18f, 0.26f) * 110f * shadowT * s;
            // Drawn like every other aircraft (AircraftArt): the picture for how it is turned, propeller, fire.
            var view = AircraftArt.MapView(Player);
            _art.DrawShadow(_sb, Player, view, shadowAt, sc * 0.9f, new Color(0, 0, 0, 80) * vis);
            _art.Draw(_sb, Player, view, centre, sc, 1f, _time);
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

        tc = _perf.Now;
        DrawTraffic(centre, TrafficPass.Above);
        _perf.Add("TRAFFIC", tc);
        tc = _perf.Now;
        if (_cloudsOn) DrawClouds(centre, CloudPass.Above);
        _perf.Add("MAP CLOUDS", tc);

        DrawHud();
        if (_showHitboxes) DrawDamageReadout();
        _instruments.Draw(_sb, _fm, GraphicsDevice.Viewport.Bounds, Scale);
        _gunsight.Draw(_sb, _pixel, sightRect, s, _firing, _sightAlpha);
        DrawBanner();
        DrawFps();
        DrawPerf();
        _menu.Draw(_sb, _pixel, GraphicsDevice.Viewport.Bounds, Scale);
        _options.Draw(_sb, _pixel, GraphicsDevice.Viewport.Bounds, Scale);
        _sb.End();

        base.Draw(gameTime);
    }

    /// <summary>Sky colour behind everything: pale and bright low down, deepening as we climb.</summary>
    private Color SkyColor() =>
        Color.Lerp(new Color(122, 182, 236), new Color(58, 108, 204), MathHelper.Clamp(_fm.Altitude / 40000f, 0f, 1f));

    /// <summary>Everything on the ground (grass, and the shadows planes and clouds cast) into one layer.</summary>
    private void DrawGroundLayer(Vector2 centre)
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

        _sb.Begin(samplerState: SamplerState.LinearClamp);
        if (_cloudsOn && _cloudShadows) DrawClouds(centre, CloudPass.Shadows);
        DrawTraffic(centre, TrafficPass.Shadows);
        _sb.End();
    }

    private enum TrafficPass { Shadows, Below, Above }

    /// <summary>Other aircraft: placed and scaled by their own distance from the camera, like clouds.</summary>
    private void DrawTraffic(Vector2 centre, TrafficPass pass)
    {
        var s = Scale;
        var zGround = Zoom;
        // Same on-screen size as the player at the same altitude; nearer or further planes scale by perspective.
        var ps = s * 0.8f * (0.85f + 0.3f * MathF.Sqrt(MathHelper.Clamp(_fm.Altitude / _fm.Airframe.CeilingFt, 0f, 1f)));
        foreach (var c in _craft)
        {
            var f = DistFactor(c.Altitude);
            var view = AircraftArt.MapView(c);
            if (pass == TrafficPass.Shadows)
            {
                // Ground shadow: world-consistent size, nudged away from the plane with height.
                var worldScale = ps / (s * GroundZoom);
                var sp = centre + (c.Pos + new Vector2(4f, 6f) * (c.Altitude / 1000f) - _pos) * zGround;
                var ss = worldScale * zGround;
                if (ss * 96f < 3f) continue;
                _art.DrawShadow(_sb, c, view, sp, ss * 0.9f * c.Sphere.MapScale, new Color(0, 0, 0, 70));
                continue;
            }
            var above = c.Altitude > _fm.Altitude;
            if (above != (pass == TrafficPass.Above) || f < 0.2f || _fm.Altitude - c.Altitude > World.ViewBoxFt) continue;
            var z = s * GroundZoom / f;
            var scale = ps / f;
            if (scale * 96f < 3f) continue;
            var screen = centre + (c.Pos - _pos) * z;
            var alpha = above ? MathHelper.Clamp((f - 0.2f) / 0.4f, 0f, 1f) : MathHelper.Clamp((World.ViewBoxFt - (_fm.Altitude - c.Altitude)) / 1000f, 0f, 1f);
            _art.Draw(_sb, c, view, screen, scale * c.Sphere.MapScale, alpha, _time);
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
            if (p.Kind == Fx.Kind.Explosion)
            {
                var frame = _effects.ExplosionFrame(p.Age);
                _sb.Draw(frame, pos, null, Color.White * a, p.Rot, new Vector2(frame.Width / 2f, frame.Height / 2f), size / frame.Width,
                    SpriteEffects.None, 0f);
                continue;
            }
            // Smoke puffs and leaking-fuel mist, each sprite turned its own way so the trail doesn't repeat.
            var tex = p.Kind == Fx.Kind.Smoke ? _effects.Smoke[p.Variant % _effects.Smoke.Length] : _effects.Mist;
            var col = new Color(p.Shade * a, p.Shade * a, p.Shade * a, a);
            _sb.Draw(tex, pos, null, col, p.Rot, new Vector2(tex.Width / 2f, tex.Height / 2f), size / (tex.Width * 0.85f), SpriteEffects.None, 0f);
        }
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
                    // An invisible cloud still costs its whole area to draw, and one passing just above us is drawn
                    // many times the screen's size: skip it.
                    if (alpha < 0.01f) continue;
                    _mapClouds++;
                    _mapCloudFill += tex.Width * scale * tex.Height * scale / (w * h);
                    _sb.Draw(tex, screen, null, Color.White * alpha, 0f, origin, scale, SpriteEffects.None, 0f);
                }
        }
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

    /// <summary>Frames actually drawn per second, in the bottom-left corner. (The game itself ticks at a fixed 60 a second;
    /// this shows whether drawing keeps up.)</summary>
    private void DrawFps()
    {
        _fpsFrames++;
        var secs = (float)_fpsClock.Elapsed.TotalSeconds;
        if (secs >= 0.5f)
        {
            _fps = _fpsFrames / secs;
            _fpsFrames = 0;
            _fpsClock.Restart();
        }
        if (!_showFps) return;
        var s = Scale;
        var px = Math.Max(2, (int)MathF.Round(2.2f * s));
        var col = _fps >= 55f ? new Color(94, 224, 160) : _fps >= 30f ? new Color(255, 206, 84) : new Color(255, 94, 94);
        PixelFont.Draw(_sb, _pixel, $"FPS {_fps:0}", new Vector2(12 * s, GraphicsDevice.Viewport.Height - 12 * s - 7 * px), px, col);
    }

    /// <summary>The PERF overlay (debug menu): frame time, where the CPU's time goes, and how much the clouds cover, which
    /// is what costs the graphics card. Fill is in whole screens (or whole sight views): 3.0 means every pixel drawn three
    /// times over.</summary>
    private void DrawPerf()
    {
        if (_fpsFrames == 0) { _frameMsMaxShown = _frameMsMax; _frameMsMax = 0f; }   // (DrawFps just started a new half second)
        if (!_showPerf) return;
        var s = Scale;
        var px = Math.Max(2, (int)MathF.Round(2f * s));
        var lines = new System.Collections.Generic.List<string>
        {
            $"FRAME {_frameMs:0.0} MS  WORST {_frameMsMaxShown:0.0}",
            $"UPDATE {_perf["UPDATE"]:0.00}  DRAW {_perf["DRAW"]:0.00} MS CPU",
            $" SIGHT {_perf["SIGHT"]:0.00}  GROUND {_perf["GROUND"]:0.00}",
            $" CLOUDS {_perf["MAP CLOUDS"]:0.00}  TRAFFIC {_perf["TRAFFIC"]:0.00}",
            $"MAP CLOUDS {_mapClouds}  FILL {_mapCloudFill:0.0}",
            $"SIGHT CLOUDS {_gunsight.CloudsDrawn}  FILL {_gunsight.CloudFill:0.0}" + (_gunsight.Blur ? "  BLUR" : ""),
            $"SIGHT SMOKE {_gunsight.SmokeDrawn}/{_gunsight.SmokeSeen}  FILL {_gunsight.SmokeFill:0.0}",
            $"SIGHT DRAW CALLS {_gunsight.SpriteRuns} PER SLAB",
            $"PARTICLES {_fx.Particles.Count}  ROUNDS {_guns.Rounds.Count}  AIRCRAFT {_worldModel.Planes.Count}",
        };
        if (_test is TestMode.FollowThroughCloud or TestMode.CloudIntoView)
            lines.Add($"CLOUD {Vector3.Distance(World.ToFt(_pos, _fm.Altitude), _testCloudFt):0} FT AWAY");
        var w = 0;
        foreach (var l in lines) w = Math.Max(w, PixelFont.Measure(l, px));
        var rowH = 10 * px;
        var x = GraphicsDevice.Viewport.Width - w - (int)(24 * s);
        var y = (int)(24 * s) + (_showHitboxes ? rowH * 9 : 0);
        _sb.Draw(_pixel, new Rectangle(x - 8, y - 8, w + 16, lines.Count * rowH + 10), new Color(18, 20, 24, 210));
        for (var i = 0; i < lines.Count; i++)
            PixelFont.Draw(_sb, _pixel, lines[i], new Vector2(x, y + i * rowH), px, new Color(200, 205, 214));
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
            _dmg.FuelGal / Player.Airframe.FuelCapacityGal, _dmg.Leaks > 0 ? new Color(255, 140, 40) : new Color(120, 200, 255));
        if (_hudBars)
        {
            var ias = _fm.IasMph;
            var stallIas = _fm.StallSpeed(1f) * MathF.Sqrt(_fm.Rho / 0.0023769f) * 0.681818f;
            Row(line++, "AIRSPEED", $"{ias:0} MPH IAS", ias / 500f,
                _fm.Overspeed ? new Color(255, 94, 94) : new Color(255, 206, 84), stallIas / 500f, _fm.Airframe.VneMph / 500f);
            Row(line++, "ALTITUDE", $"{_fm.Altitude:N0} FT", _fm.Altitude / _fm.Airframe.CeilingFt, new Color(74, 163, 255));
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
        var red = new Color(255, 94, 94);
        // Out of control, worst first.
        var lost = _fm.Failure switch
        {
            FlightFailure.NoseDive => "AIRFRAME BROKEN",
            FlightFailure.Spin => "WING LOST - SPIN",
            FlightFailure.DeadPilot => "PILOT KILLED",
            _ => _dmg.Tail.Gone ? "TAIL LOST - NO PITCH OR ROLL" : null,
        };
        if (lost != null) { PixelFont.Draw(_sb, _pixel, lost, new Vector2(x, wy), px * 2, red); wy += 20 * px; }
        // Fires, flashing, one line each.
        var flash = (_time * 3f) % 1f < 0.6f;
        foreach (var part in _dmg.Parts)
        {
            if (!part.OnFire) continue;
            if (flash) PixelFont.Draw(_sb, _pixel, $"{part.Name} FIRE {_dmg.FireStrengthOf(part) * 100f:0}%", new Vector2(x, wy), px * 2, new Color(255, 120, 40));
            wy += 20 * px;
        }
        if (_dmg.FuelGal <= 0f) PixelFont.Draw(_sb, _pixel, "OUT OF FUEL", new Vector2(x, wy), px * 2, red);
        else if (_dmg.EngineHp <= 0f) PixelFont.Draw(_sb, _pixel, "ENGINE DEAD", new Vector2(x, wy), px * 2, red);
    }
}
