using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// One aircraft, the player's or anyone else's: where it is, its flight model (which holds its control inputs), its
/// parts and everything about their damage (hit points, state, armour, hit boxes), its fuel, leaks and engine fire, how
/// it is drawn, and the pilot flying it. Look at an aircraft and its whole condition is here. Each tick the pilot sets
/// the controls and the aircraft does what its flight model and damage make of them.
/// </summary>
public sealed class Aircraft
{
    public Vector2 Pos;                                   // world px (map)
    public readonly FlightModel Flight;
    public SpriteSphere Sphere;                           // its pictures (the type's look)
    public Pilot Pilot;
    public bool IsPlayer;
    public bool Crashed;                                  // hit the ground (the only thing that ends an aircraft)
    public int Salt;                                      // staggers its fire animation from the others'
    /// <summary>How quickly it rolls to the bank its pilot asks for: the fraction of the gap closed each tick.</summary>
    public float BankResponse = 0.18f;

    // ---------------------------------------------------------------- parts

    public readonly AircraftPart Engine, Canopy, LeftWing, RightWing, Tail, Fuselage;

    /// <summary>All its parts, in the order of the Part enum (so Parts[(int)Part.Tail] is Tail).</summary>
    public readonly AircraftPart[] Parts;

    public AircraftPart this[Part p] => Parts[(int)p];

    // ---------------------------------------------------------------- fuel, leaks, fire

    public float FuelGal = DamageTuning.FuelCapacityGal;
    public int Leaks;
    public bool OnFire;
    private PartState _engineSeen = PartState.Undamaged;   // the engine band last dealt with, so each band rolls once
    private float _throttleOffSec;

    public float EngineHp => Engine.Hp;
    public PartState EngineState => Engine.State;

    /// <summary>Thrust available, 0-1: the engine's hit points as a fraction, and nothing without fuel.</summary>
    public float EnginePower => FuelGal > 0f ? Engine.Hp / DamageTuning.MaxHp : 0f;

    /// <summary>How fierce the engine fire is, 0-1: about 1% just into red, 100% with the engine at 0 hp.</summary>
    public float FireStrength => OnFire
        ? MathHelper.Clamp((DamageTuning.ModerateAbove - Engine.Hp) / DamageTuning.ModerateAbove, 0.01f, 1f) : 0f;

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

    /// <param name="hitBoxes">The type's hit boxes (each tagged with its part); a single-engined fighter's by default.</param>
    public Aircraft(FlightModel flight, Pilot pilot, SpriteSphere sphere = null, DamageTuning.HitBox[] hitBoxes = null)
    {
        Flight = flight;
        Pilot = pilot;
        Sphere = sphere;
        hitBoxes ??= DamageTuning.Fighter;
        Parts = new AircraftPart[DamageModel.PartCount];
        for (var i = 0; i < Parts.Length; i++)
        {
            var kind = (Part)i;
            var boxes = new List<DamageTuning.HitBox>();
            foreach (var b in hitBoxes) if (b.Part == kind) boxes.Add(b);
            Parts[i] = new AircraftPart(kind, boxes.ToArray());
        }
        Engine = this[Part.Engine]; Canopy = this[Part.Canopy]; LeftWing = this[Part.LeftWing];
        RightWing = this[Part.RightWing]; Tail = this[Part.Tail]; Fuselage = this[Part.Fuselage];
    }

    /// <summary>Where the engine is (world feet): in the nose, ahead of the origin and a little below the centre line.</summary>
    public Vector3 EngineFt()
    {
        World.Basis(Flight.Heading, Flight.Gamma, Flight.Bank, out _, out var u, out var f);
        return PositionFt + f * 11f - u * 0.5f;
    }

    // ---------------------------------------------------------------- damage

    /// <summary>A round hits one of its parts (see DamageModel.Hit). Returns the hit points the part lost.</summary>
    public float Hit(Part part, DamageTuning.Weapon weapon, float impactSpeedFtS, Random rng)
    {
        var loss = DamageModel.Hit(this[part], weapon, impactSpeedFtS, rng);
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
            OnFire = true;
            _throttleOffSec = 0f;
        }
    }

    /// <summary>Fuel burns (by throttle) and leaks; a fire burns on while the throttle is up, eating the engine, and goes
    /// out with the throttle held at idle or the fuel gone.</summary>
    private void UpdateEngine(float dt)
    {
        var throttle = Flight.Throttle;
        if (FuelGal > 0f)
        {
            var burn = EnginePower > 0f ? MathHelper.Lerp(DamageTuning.FuelBurnIdleGalPerMin, DamageTuning.FuelBurnFullGalPerMin, throttle) : 0f;
            FuelGal = MathF.Max(0f, FuelGal - (burn + Leaks * DamageTuning.LeakGalPerMin) * dt / 60f);
        }
        if (!OnFire) return;
        if (FuelGal <= 0f) { OnFire = false; return; }
        _throttleOffSec = throttle < DamageTuning.FireThrottleOff ? _throttleOffSec + dt : 0f;
        if (_throttleOffSec >= DamageTuning.FireOutSec) { OnFire = false; return; }
        var rate = MathHelper.Lerp(DamageTuning.FireBurnMinHpPerSec, DamageTuning.FireBurnMaxHpPerSec, FireStrength);
        Engine.Hp = MathF.Max(0f, Engine.Hp - rate * dt);
    }

    // ---------------------------------------------------------------- tick

    /// <summary>One tick of flight: the pilot sets the controls; the engine's damage and fuel set the power; a damaged
    /// engine trails smoke (and fire), a leak a thin mist; the flight model flies; the aircraft moves.</summary>
    public void Step(Fx fx, Random rng)
    {
        Pilot?.Fly(this);
        UpdateEngine(1f / 60f);
        Flight.EnginePower = EnginePower;
        if (SmokeStrength > 0f || OnFire || Leaks > 0)
        {
            World.Basis(Flight.Heading, Flight.Gamma, Flight.Bank, out _, out _, out var f);
            fx.EngineTrail(EngineFt() - f * 4f, -f, SmokeStrength, FireStrength, Leaks, IsPlayer);
        }
        Flight.Step(BankResponse, rng);
        if (Flight.GroundHit && !IsPlayer) Crashed = true;   // (the game handles the player's landings and crashes)
        var dir = new Vector2(MathF.Sin(Flight.Heading), -MathF.Cos(Flight.Heading));
        Pos += dir * Flight.GroundSpeed * World.PxPerFoot / 60f;
    }
}

/// <summary>
/// One part of one aircraft: what it is, its hit points and so its state, its armour, integrity and damage multiplier
/// (from DamageTuning, held here so a part, or an aircraft type, can differ), and its hit boxes in the aircraft's frame.
/// </summary>
public sealed class AircraftPart
{
    public readonly Part Kind;
    public float Hp = DamageTuning.MaxHp;
    public float Armor, Integrity, DamageMultiplier;
    public readonly DamageTuning.HitBox[] Boxes;

    public AircraftPart(Part kind, DamageTuning.HitBox[] boxes)
    {
        Kind = kind;
        Boxes = boxes;
        Armor = DamageTuning.Armor[(int)kind];
        Integrity = DamageTuning.Integrity[(int)kind];
        DamageMultiplier = DamageTuning.DamageMultiplier[(int)kind];
    }

    public PartState State => DamageModel.StateOf(Hp);
    public bool Gone => Hp <= 0f;
    public string Name => DamageModel.Name(Kind);
}

/// <summary>
/// What an aircraft type is made of and can stand, for the flight model to fly: its airframe (weight, wing, drag, engine
/// and propeller) and its limits (speed, g, stall behaviour, controls). Each aircraft carries one (Aircraft.Airframe),
/// and its flight model works everything out from it, so a different airframe flies differently on the same inputs.
/// These are a Spitfire Mk IX's: level speed ~403 mph TAS at ~30,000 ft, ~4,100 ft/min climb at sea level falling to 0
/// at 51,550 ft, stall ~86 mph, never-exceed 450 mph IAS, dive to ~Mach 0.87 from altitude.
/// </summary>
public sealed class Airframe
{
    public string Name = "SPITFIRE";

    // ---- Airframe ----
    public float WeightLb = 7400f, WingArea = 242f, Span = 36.83f;   // lb, sq ft, ft
    public float CD0 = 0.0195f, OswaldE = 0.55f, CLmax = 1.6f;       // zero-lift drag, span efficiency, max lift coefficient
    public float PropEff = 0.82f, StaticThrustCapLb = 3500f;         // propeller efficiency; the most thrust it gives at low speed
    public float IdlePropDragCD = 0.006f;                            // a throttled-back propeller's drag

    /// <summary>Engine power (hp) at full throttle against altitude (ft). Chosen so the climb rates match the real
    /// aircraft; beyond the table power falls away quickly.</summary>
    public (float alt, float hp)[] PowerTable =
    {
        (0, 1491), (5000, 1477), (10000, 1440), (15000, 1408), (20000, 1353), (25000, 1290),
        (28000, 1231), (30000, 1180), (35000, 1036), (40000, 843), (43000, 767), (47000, 780),
        (51550, 840), (56000, 600), (65000, 150),
    };

    public float AspectRatio => Span * Span / WingArea;

    // ---- Limits ----
    public float VneMph = 450f;              // never-exceed, indicated
    public float WaveDragK = 3f;             // compressibility drag above Mach 0.75
    public float OverspeedDragK = 0.3f;      // structural-limit drag above Vne
    // Pitch-axis g limits. Level flight is 1 g, so 7 up and -5 down are the same 6 g of change either way: pulling and
    // pushing the nose have equal authority.
    public float MaxNPos = 7.0f, MaxNNeg = 5.0f;
    public float NTurnMax = 7f, NStruct = 12f;     // g at full bank at reference speed; structural limit
    public float QRef = 160f;                      // dynamic pressure (psf) of ~250 mph at sea level
    public float StallDragCD = 0.12f;              // separated flow when the wing is over-pulled
    public float RudderRate = 0.0015f;             // rad per tick of flat yaw at full rudder (about 5 deg/s)
    public float RudderSlipDragCD = 0.012f;        // extra drag at full rudder
    public float StallNoseDropDegS = 75f;          // how fast a fully stalled nose falls toward the ground
    public float MaxClimbDeg = 60f, MaxDiveDeg = 90f;   // steepest flight path the pilot can command
    public float CeilingFt = 51550f;
}
