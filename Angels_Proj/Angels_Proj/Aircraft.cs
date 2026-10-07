using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// One aircraft, the player's or anyone else's: where it is, its flight model (which holds its control inputs), its
/// parts and everything about their damage (hit points, state, armour, hit boxes), its fuel, leaks and engine fire, how
/// it is drawn, and the pilot flying it. Look at an aircraft and its whole condition is here. Each tick the pilot sets
/// the controls and the aircraft does what its flight model and damage make of them.
///
/// This holds no numbers of its own: what an aircraft is (its airframe, limits, hit boxes, armour) comes from its type,
/// a class built on this one (Spitfire), which hands them over when it is made.
/// </summary>
public abstract class Aircraft
{
    public Vector2 Pos;                                   // world px (map)
    public readonly FlightModel Flight;
    private readonly SpriteSheetSpec _sheet;
    private SpriteSphere _sphere;
    public Pilot Pilot;
    public bool IsPlayer;
    public bool Crashed;                                  // hit the ground (the only thing that ends an aircraft)
    public bool Frozen;                                   // held still in the air (a gun-test target): damage, smoke and fire run, it doesn't fly
    public int Salt;                                      // staggers its fire animation from the others'

    // ---------------------------------------------------------------- parts

    public readonly AircraftPart Engine, Canopy, LeftWing, RightWing, Tail, Fuselage;

    /// <summary>All its parts, in the order of the Part enum (so Parts[(int)Part.Tail] is Tail).</summary>
    public readonly AircraftPart[] Parts;

    public AircraftPart this[Part p] => Parts[(int)p];

    // ---------------------------------------------------------------- look

    /// <summary>Its propellers (one each for a single-engined fighter, two or four for a twin or a bomber), each with its
    /// place on the airframe and how fast it is turning.</summary>
    public readonly Propeller[] Propellers;

    /// <summary>Where an engine fire is drawn on it, in its sprite sphere's frame (feet: x nose, y left, z up).</summary>
    public readonly Vector3[] FirePoints;

    /// <summary>Its pictures from every angle: the sprite sheet its type names (AircraftLook.Sheet), loaded the first time
    /// it is drawn and shared by every aircraft of the type.</summary>
    public SpriteSphere Sphere => _sphere ??= SpriteSphere.For(_sheet);

    // ---------------------------------------------------------------- fuel, leaks, fire

    public float FuelGal;
    public int Leaks;
    private PartState _engineSeen = PartState.Undamaged;   // the engine band last dealt with, so each band rolls once
    private float _throttleOffSec;

    /// <summary>The pilot is dead (canopy gone): nobody flies it any more.</summary>
    public bool PilotKilled;
    private readonly bool[] _goneSeen;                      // parts whose loss has been dealt with

    /// <summary>Any part on fire.</summary>
    public bool Burning
    {
        get
        {
            foreach (var p in Parts) if (p.OnFire) return true;
            return false;
        }
    }

    public float EngineHp => Engine.Hp;
    public PartState EngineState => Engine.State;

    /// <summary>Thrust available, 0-1: the engine's hit points as a fraction, and nothing without fuel.</summary>
    public float EnginePower => FuelGal > 0f ? Engine.Hp / DamageTuning.MaxHp : 0f;

    /// <summary>How fierce the engine fire is, 0-1: about 1% just into red, 100% with the engine at 0 hp.</summary>
    public float FireStrength => FireStrengthOf(Engine);

    /// <summary>How fierce a part's fire is, 0-1. The engine's follows its hit points from red (about 1%) to 0 hp (100%);
    /// any other part's from DamageTuning.FireMinStrength when whole to 100% at 0 hp.</summary>
    public float FireStrengthOf(AircraftPart p)
    {
        if (!p.OnFire) return 0f;
        if (p == Engine) return MathHelper.Clamp((DamageTuning.ModerateAbove - p.Hp) / DamageTuning.ModerateAbove, 0.01f, 1f);
        return MathHelper.Clamp(1f - p.Hp / DamageTuning.MaxHp, DamageTuning.FireMinStrength, 1f);
    }

    /// <summary>How thick the engine's black smoke is, 0-1.</summary>
    public float SmokeStrength => MathHelper.Clamp((DamageTuning.SmokeStartsBelowHp - Engine.Hp) / DamageTuning.SmokeStartsBelowHp, 0f, 1f);

    // ---------------------------------------------------------------- flight

    public ControlInputs Controls => Flight.Controls;
    /// <summary>What it's made of and can stand (Airframe, below); its flight model flies by it.</summary>
    public Airframe Airframe => Flight.Airframe;
    public Vector3 PositionFt => World.ToFt(Pos, Flight.Altitude);
    public float Altitude { get => Flight.Altitude; set => Flight.Altitude = value; }
    public float Heading { get => Flight.Heading; set => Flight.Heading = value; }
    public float Pitch => Flight.Gamma;
    public float Bank => Flight.Bank;

    /// <summary>Its velocity, ft/s in the gunsight's world frame.</summary>
    public Vector3 VelocityFt
    {
        get
        {
            World.Basis(Flight.Heading, Flight.Gamma, Flight.Bank, out _, out _, out var f);
            return f * Flight.Speed;
        }
    }

    /// <summary>Made by an aircraft type with what it is.</summary>
    /// <param name="airframe">Its airframe and limits, which its flight model flies by.</param>
    /// <param name="hitBoxes">Its hit boxes, each tagged with its part.</param>
    /// <param name="armor">Each part's armour, integrity and damage multiplier, in Part order.</param>
    /// <param name="look">Its sprite sheet, and where its propellers and engine fires are drawn.</param>
    protected Aircraft(Airframe airframe, DamageTuning.HitBox[] hitBoxes, float[] armor, float[] integrity, float[] damageMultiplier,
        AircraftLook look, Pilot pilot)
    {
        _sheet = look.Sheet;
        Propellers = new Propeller[look.Propellers.Length];
        for (var i = 0; i < Propellers.Length; i++) Propellers[i] = new Propeller(look.Propellers[i]);
        FirePoints = look.FirePoints;
        Flight = new FlightModel(airframe);
        FuelGal = airframe.FuelCapacityGal;
        Pilot = pilot;
        Parts = new AircraftPart[DamageModel.PartCount];
        for (var i = 0; i < Parts.Length; i++)
        {
            var kind = (Part)i;
            var boxes = new List<DamageTuning.HitBox>();
            foreach (var b in hitBoxes) if (b.Part == kind) boxes.Add(b);
            Parts[i] = new AircraftPart(kind, boxes.ToArray(), armor[i], integrity[i], damageMultiplier[i]);
        }
        Engine = this[Part.Engine]; Canopy = this[Part.Canopy]; LeftWing = this[Part.LeftWing];
        RightWing = this[Part.RightWing]; Tail = this[Part.Tail]; Fuselage = this[Part.Fuselage];
        _goneSeen = new bool[Parts.Length];
    }

    /// <summary>A point in the aircraft's own frame (feet: x toward the right wing, y up, z toward the tail) in the world (feet).</summary>
    public Vector3 LocalToWorldFt(Vector3 local)
    {
        World.Basis(Flight.Heading, Flight.Gamma, Flight.Bank, out var r, out var u, out var f);
        return PositionFt + r * local.X + u * local.Y - f * local.Z;
    }

    /// <summary>A point in the aircraft's own frame as a point in its sprite sphere's frame (x nose, y left wing, z up).</summary>
    public static Vector3 LocalToSphere(Vector3 local) => new(-local.Z, -local.X, local.Y);

    /// <summary>Where a part's fire burns, in the world (feet): the engine's at the engine, any other part's at the middle of
    /// its hit box.</summary>
    public Vector3 FireFt(AircraftPart p) => p == Engine ? EngineFt() : LocalToWorldFt(p.Centre);

    /// <summary>Where the engine is (world feet): in the nose, ahead of the origin and a little below the centre line.</summary>
    public Vector3 EngineFt()
    {
        World.Basis(Flight.Heading, Flight.Gamma, Flight.Bank, out _, out var u, out var f);
        return PositionFt + f * Airframe.EngineAheadFt - u * 0.5f;
    }

    // ---------------------------------------------------------------- damage

    /// <summary>A round hits one of its parts (see DamageModel.Hit). Returns the hit points the part lost.</summary>
    public float Hit(Part part, DamageTuning.Weapon weapon, float impactSpeedFtS, Random rng)
    {
        var loss = DamageModel.Hit(this[part], weapon, impactSpeedFtS, rng);
        if (part == Part.Engine && loss > 0f) EngineDamaged(rng);
        return loss;
    }

    /// <summary>Damage that has already got through a part's armour (see DamageModel.Apply), for a round that carries its
    /// damage on from part to part. Returns the hit points the part lost.</summary>
    public float TakeDamage(Part part, DamageTuning.Weapon weapon, float left, Random rng)
    {
        var loss = DamageModel.Apply(this[part], weapon, left);
        if (part == Part.Engine && loss > 0f) EngineDamaged(rng);
        return loss;
    }

    /// <summary>Takes hit points straight off a part (debug, or damage that isn't a round).</summary>
    public void DamagePart(Part part, float hp, Random rng)
    {
        var p = this[part];
        p.Hp = MathF.Max(0f, p.Hp - hp);
        if (part == Part.Engine) EngineDamaged(rng);
    }

    /// <summary>The engine has just lost hit points: each band it has newly entered has its say (a leak roll on entering
    /// yellow and on entering orange; a fire on reaching red), and a hit on an engine already in red lights it again.</summary>
    private void EngineDamaged(Random rng)
    {
        var now = Engine.State;
        while (_engineSeen < now)
        {
            _engineSeen++;
            if (_engineSeen is PartState.Slight or PartState.Moderate)
                if (rng.NextDouble() < DamageTuning.LeakChance) Leaks++;
        }
        if (now >= PartState.Critical && FuelGal > 0f)
        {
            Engine.OnFire = true;
            _throttleOffSec = 0f;
        }
    }

    /// <summary>Fuel burns (by throttle) and leaks; a fire burns on while the throttle is up, eating the engine, and goes
    /// out with the throttle held at idle or the fuel gone. Once the engine is gone the throttle can't starve it: only
    /// running out of fuel puts it out.</summary>
    private void UpdateEngine(float dt)
    {
        var throttle = Flight.Throttle;
        if (FuelGal > 0f)
        {
            var burn = EnginePower > 0f ? MathHelper.Lerp(Airframe.FuelBurnIdleGalPerMin, Airframe.FuelBurnFullGalPerMin, throttle) : 0f;
            FuelGal = MathF.Max(0f, FuelGal - (burn + Leaks * DamageTuning.LeakGalPerMin) * dt / 60f);
        }
        if (!Engine.OnFire) return;
        if (FuelGal <= 0f) { Engine.OnFire = false; return; }
        _throttleOffSec = throttle < DamageTuning.FireThrottleOff && !Engine.Gone ? _throttleOffSec + dt : 0f;
        if (_throttleOffSec >= DamageTuning.FireOutSec) { Engine.OnFire = false; return; }
        var rate = MathHelper.Lerp(DamageTuning.FireBurnMinHpPerSec, DamageTuning.FireBurnMaxHpPerSec, FireStrength);
        Engine.Hp = MathF.Max(0f, Engine.Hp - rate * dt);
    }

    /// <summary>Fires on parts other than the engine eat their part and never go out; a part that is gone and burning
    /// sets its neighbours alight, a chance each second.</summary>
    private void UpdateFires(float dt, Random rng)
    {
        foreach (var p in Parts)
        {
            if (!p.OnFire || p == Engine) continue;
            var rate = MathHelper.Lerp(DamageTuning.PartFireBurnMinHpPerSec, DamageTuning.PartFireBurnMaxHpPerSec, FireStrengthOf(p));
            p.Hp = MathF.Max(0f, p.Hp - rate * dt);
        }
        foreach (var p in Parts)
        {
            if (!p.Gone || !p.OnFire) continue;
            foreach (var n in DamageModel.Neighbours(p.Kind))
                if (!this[n].OnFire && rng.NextDouble() < DamageTuning.FireSpreadChancePerSec * dt) this[n].OnFire = true;
        }
    }

    /// <summary>Deals with each part the moment it is gone (once):
    ///   engine   - blows up: an explosion, and the fire takes every part touching it;
    ///   fuselage - the airframe breaks: an explosion, it burns, and the aircraft noses over into a steep spiralling dive;
    ///   a wing   - the other wing's lift rolls it over: a spin toward the missing wing all the way down;
    ///   canopy   - the pilot is dead: nobody flies it, it holds its heading and the nose slowly sinks;
    ///   tail     - all pitch and roll control gone (see UpdateControls).</summary>
    private void CheckLosses(Fx fx, Random rng)
    {
        for (var i = 0; i < Parts.Length; i++)
        {
            var p = Parts[i];
            if (!p.Gone || _goneSeen[i]) continue;
            _goneSeen[i] = true;
            switch (p.Kind)
            {
                case Part.Engine:
                    fx.Explosion(EngineFt(), 30f, IsPlayer);
                    p.OnFire = true;
                    foreach (var n in DamageModel.Neighbours(Part.Engine)) this[n].OnFire = true;
                    break;
                case Part.Fuselage:
                    fx.Explosion(FireFt(p), 40f, IsPlayer);
                    p.OnFire = true;
                    Fail(FlightFailure.NoseDive, rng.Next(2) == 0 ? -1f : 1f);
                    break;
                case Part.LeftWing:
                    Fail(FlightFailure.Spin, -1f);
                    break;
                case Part.RightWing:
                    Fail(FlightFailure.Spin, 1f);
                    break;
                case Part.Canopy:
                    PilotKilled = true;
                    Fail(FlightFailure.DeadPilot, 1f);
                    break;
            }
        }
    }

    /// <summary>The aircraft goes out of control the given way, unless it is already out of control in a worse one.</summary>
    private void Fail(FlightFailure how, float dir)
    {
        if (how <= Flight.Failure) return;
        Flight.Failure = how;
        Flight.SpinDir = dir;
    }

    /// <summary>What the damage leaves of the controls (see DamageModel.ControlLoss): the tail takes its loss off pitch,
    /// roll and rudder, each wing half of its own off roll and turn; a black wing's lost lift rolls the aircraft toward it.</summary>
    private void UpdateControls()
    {
        var tail = DamageModel.ControlLoss(Tail.Hp);
        var wings = (DamageModel.ControlLoss(LeftWing.Hp) + DamageModel.ControlLoss(RightWing.Hp)) / 2f;
        Flight.PitchAuthority = 1f - tail;
        Flight.RollAuthority = (1f - wings) * (1f - tail);
        Flight.YawAuthority = 1f - tail;
        Flight.TurnAuthority = 1f - wings;
        float liftL = DamageModel.LiftLoss(LeftWing.Hp), liftR = DamageModel.LiftLoss(RightWing.Hp);
        Flight.RollBias = (liftR - liftL) * DamageTuning.WingRollBias;   // + rolls right, toward a weaker right wing
        Flight.LiftLoss = (liftL + liftR) / 2f;
        Flight.NoseDropDegS = PilotKilled ? DamageTuning.DeadPilotNoseDropDegS : Tail.Gone ? DamageTuning.TailGoneNoseDropDegS : 0f;
    }

    // ---------------------------------------------------------------- tick

    /// <summary>One tick of flight: the pilot (if alive) sets the controls; the engine's damage and fuel set the power,
    /// and the wings' and tail's damage what the controls can do; parts just lost have their say; fires burn and spread;
    /// a damaged engine trails smoke (and fire), a leak a thin mist, any other burning part smoke of its own; the flight
    /// model flies; the aircraft moves.</summary>
    public void Step(Fx fx, Random rng)
    {
        const float dt = 1f / 60f;
        if (!Frozen && !PilotKilled) Pilot?.Fly(this);
        CheckLosses(fx, rng);
        UpdateEngine(dt);
        UpdateFires(dt, rng);
        CheckLosses(fx, rng);   // a fire may just have finished a part
        UpdateControls();
        Flight.EnginePower = EnginePower;
        World.Basis(Flight.Heading, Flight.Gamma, Flight.Bank, out _, out _, out var fwd);
        if (SmokeStrength > 0f || Engine.OnFire || Leaks > 0)
            fx.EngineTrail(EngineFt() - fwd * 4f, -fwd, SmokeStrength, FireStrength, Leaks, IsPlayer);
        foreach (var p in Parts)
            if (p.OnFire && p != Engine) fx.EngineTrail(FireFt(p) - fwd * 2f, -fwd, 0f, FireStrengthOf(p), 0, IsPlayer);
        if (Frozen) return;
        Flight.Step(Airframe.RollResponse, rng);
        foreach (var p in Propellers) p.Update(Flight.Throttle, EnginePower > 0f);
        if (Flight.GroundHit && !IsPlayer) Crashed = true;   // (the game handles the player's landings and crashes)
        var dir = new Vector2(MathF.Sin(Flight.Heading), -MathF.Cos(Flight.Heading));
        Pos += dir * Flight.GroundSpeed * World.PxPerFoot / 60f;
    }
}

/// <summary>
/// One part of one aircraft: what it is, its hit points and so its state, its armour, integrity and damage multiplier
/// (given by its aircraft type), and its hit boxes in the aircraft's frame.
/// </summary>
public sealed class AircraftPart
{
    public readonly Part Kind;
    public float Hp = DamageTuning.MaxHp;
    public float Armor, Integrity, DamageMultiplier;
    public readonly DamageTuning.HitBox[] Boxes;
    public bool OnFire;
    /// <summary>The middle of its biggest hit box, in the aircraft's frame: where its fire burns.</summary>
    public readonly Vector3 Centre;

    public AircraftPart(Part kind, DamageTuning.HitBox[] boxes, float armor, float integrity, float damageMultiplier)
    {
        Kind = kind;
        Boxes = boxes;
        Armor = armor;
        Integrity = integrity;
        DamageMultiplier = damageMultiplier;
        var biggest = -1f;
        foreach (var b in boxes)
        {
            var size = b.Max - b.Min;
            var volume = size.X * size.Y * size.Z;
            if (volume > biggest) { biggest = volume; Centre = (b.Min + b.Max) / 2f; }
        }
    }

    public PartState State => DamageModel.StateOf(Hp);
    public bool Gone => Hp <= 0f;
    public string Name => DamageModel.Name(Kind);
}

/// <summary>
/// What an aircraft type is made of and can stand, for the flight model to fly: its airframe (weight, wing, drag, engine,
/// propeller and fuel) and its limits (speed, g, stall behaviour, controls). Each aircraft carries one (Aircraft.Airframe)
/// and its flight model works everything out from it, so a different airframe flies differently on the same inputs.
/// It has no values of its own: each aircraft type fills one in (see Spitfire).
/// </summary>
public sealed class Airframe
{
    public string Name;

    // ---- Airframe ----
    public float WeightLb, WingArea, Span;          // lb, sq ft, ft
    public float CD0, OswaldE, CLmax;               // zero-lift drag, span efficiency, max lift coefficient
    public float PropEff, StaticThrustCapLb;        // propeller efficiency; the most thrust it gives at low speed
    public float IdlePropDragCD;                    // a throttled-back propeller's drag
    /// <summary>Engine power (hp) at full throttle against altitude (ft); beyond the table it falls away.</summary>
    public (float alt, float hp)[] PowerTable;
    public float FuelCapacityGal;                   // imperial gallons
    public float FuelBurnIdleGalPerMin, FuelBurnFullGalPerMin;   // at idle and full throttle
    public float EngineAheadFt;                     // where the engine sits, ahead of the aircraft's origin

    public float AspectRatio => Span * Span / WingArea;

    // ---- Limits ----
    public float VneMph;                            // never-exceed, indicated
    public float WaveDragK;                         // compressibility drag above Mach 0.75
    public float OverspeedDragK;                    // structural-limit drag above Vne
    public float MaxNPos, MaxNNeg;                  // pitch-axis g limits, up and down
    public float NTurnMax, NStruct;                 // g at full bank at reference speed; structural limit
    public float QRef;                              // dynamic pressure (psf) the turn g is referenced to
    public float StallDragCD;                       // separated flow when the wing is over-pulled
    public float RudderRate;                        // rad per tick of flat yaw at full rudder
    public float RudderSlipDragCD;                  // extra drag at full rudder
    public float StallNoseDropDegS;                 // how fast a fully stalled nose falls toward the ground
    public float MaxClimbDeg, MaxDiveDeg;           // steepest flight path the pilot can command
    public float CeilingFt;
    public float RollResponse;                      // how quickly it rolls to the bank asked for: fraction of the gap per tick
}

/// <summary>How an aircraft type looks: the sprite sheet it is drawn from, its propellers and where its engine fires
/// show. Positions are in the sprite sphere's frame, feet from the middle of a frame (x toward the nose, y toward the
/// left wing, z up).</summary>
public sealed class AircraftLook
{
    public SpriteSheetSpec Sheet;
    public PropellerSpec[] Propellers;
    public Vector3[] FirePoints;
}

/// <summary>One propeller of a type: where its hub is, how many blades, how big, and which way it turns.</summary>
public sealed class PropellerSpec
{
    public Vector3 Hub;
    public int Blades;
    public float RadiusFt, SpinnerFt;
    /// <summary>+1 clockwise seen from the cockpit (over the top toward the right wing), -1 the other way.</summary>
    public float Turn = 1f;
}

/// <summary>A propeller on one aircraft, turning with its engine. It turns at a rate the eye can follow rather than its
/// real one, which would only alias into a bar that looks still.</summary>
public sealed class Propeller
{
    public const float IdleSpin = 0.2f, FullSpin = 0.55f;   // rad per tick; under pi/4, so it never seems to run backwards

    public readonly PropellerSpec Spec;
    public float Angle, Rate;                                // rad, rad per tick

    public Propeller(PropellerSpec spec) { Spec = spec; }

    /// <summary>One tick: it follows the throttle, and winds down when the engine is dead.</summary>
    public void Update(float throttle, bool engineRunning)
    {
        var target = engineRunning ? IdleSpin + throttle * (FullSpin - IdleSpin) : 0f;
        Rate += (target - Rate) * (engineRunning ? 0.05f : 0.02f);
        Angle = MathHelper.WrapAngle(Angle - Rate * Spec.Turn);
    }
}
