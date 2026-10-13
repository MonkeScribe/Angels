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
/// a class built on this one (SpitfireMk2, He111), which hands them over when it is made.
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

    /// <summary>Its engines (one for a fighter, two for a twin, four for a heavy), left to right.</summary>
    public readonly AircraftPart[] Engines;

    /// <summary>Its wings, left to right. Which way the aircraft spins when one goes is the wing's own side (AircraftPart.Side).</summary>
    public readonly AircraftPart[] Wings;

    public readonly AircraftPart Canopy, Tail, Fuselage;

    /// <summary>All its parts: by kind in the order of the Part enum, and left to right within a kind.</summary>
    public readonly AircraftPart[] Parts;

    // ---------------------------------------------------------------- look

    /// <summary>Its propellers (one each for a single-engined fighter, two or four for a twin or a bomber), each with its
    /// place on the airframe and how fast it is turning.</summary>
    public readonly Propeller[] Propellers;

    /// <summary>Where an engine fire is drawn on it, in its sprite sphere's frame (feet: x nose, y left, z up).</summary>
    public readonly Vector3[] FirePoints;

    /// <summary>Its pictures from every angle: the sprite sheet its type names (AircraftLook.Sheet), loaded the first time
    /// it is drawn and shared by every aircraft of the type.</summary>
    public SpriteSphere Sphere => _sphere ??= SpriteSphere.For(_sheet);

    // ---------------------------------------------------------------- guns

    /// <summary>Its guns: each is what kind of gun it is, where its rounds leave the airframe and how much ammunition it has
    /// left. The pilot fires them (Pilot.Fire).</summary>
    public readonly Gun[] Guns;

    public int AmmoLeft { get { var n = 0; foreach (var g in Guns) n += g.Ammo; return n; } }
    public int AmmoCapacity { get { var n = 0; foreach (var g in Guns) n += g.Spec.MagazineRounds; return n; } }

    // ---------------------------------------------------------------- fuel, leaks, fire

    /// <summary>Fuel left, imperial gallons (it starts full: Airframe.FuelCapacityGal).</summary>
    public float FuelGal;
    /// <summary>Holes leaking fuel, from engine damage (its fuel lines) or hits on a tank; each drains LeakGalPerMin.</summary>
    public int Leaks;
    private readonly PartState[] _engineSeen;               // the band last dealt with for each engine, so each band rolls once
    private readonly PartState[] _tankSeen;                 // the same for each part, for its fuel tanks
    private readonly float[] _throttleOffSec;               // how long the throttle has been off with each engine on fire

    /// <summary>The pilot is dead (canopy gone): nobody flies it any more.</summary>
    public bool PilotKilled;

    /// <summary>A confirmed kill: one of its parts is gone. It can be killed only once, and it stays killed whatever
    /// happens to it after.</summary>
    public bool Killed;
    /// <summary>Who gets the kill: the pilot who dealt the last blow to the part that went (null if nobody did, e.g. a
    /// debug hit).</summary>
    public Pilot KilledBy;
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

    /// <summary>The engines' hit points averaged: the share of its power left (a twin that has lost one engine is at 50).</summary>
    public float EngineHp
    {
        get
        {
            var sum = 0f;
            foreach (var e in Engines) sum += e.Hp;
            return sum / Engines.Length;
        }
    }

    public PartState EngineState => DamageModel.StateOf(EngineHp);

    /// <summary>Thrust available, 0-1: its power and thrust are shared out equally between its engines, so each engine's hit
    /// points as a fraction count for one over the number of engines (one lost of four takes a quarter off), and nothing
    /// without fuel.</summary>
    public float EnginePower => FuelGal > 0f ? EngineHp / DamageTuning.MaxHp : 0f;

    /// <summary>How fierce a part's fire is, 0-1. An engine's follows its hit points from red (about 1%) to 0 hp (100%);
    /// any other part's from DamageTuning.FireMinStrength when whole to 100% at 0 hp.</summary>
    public float FireStrengthOf(AircraftPart p)
    {
        if (!p.OnFire) return 0f;
        if (p.Kind == Part.Engine) return MathHelper.Clamp((DamageTuning.ModerateAbove - p.Hp) / DamageTuning.ModerateAbove, 0.01f, 1f);
        return MathHelper.Clamp(1f - p.Hp / DamageTuning.MaxHp, DamageTuning.FireMinStrength, 1f);
    }

    /// <summary>How thick an engine's black smoke is, 0-1.</summary>
    public static float SmokeStrengthOf(AircraftPart engine) =>
        MathHelper.Clamp((DamageTuning.SmokeStartsBelowHp - engine.Hp) / DamageTuning.SmokeStartsBelowHp, 0f, 1f);

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
    /// <param name="guns">Its guns, each with its own muzzle position (fresh ones for this aircraft: they hold its ammunition).</param>
    protected Aircraft(Airframe airframe, DamageTuning.HitBox[] hitBoxes, float[] armor, float[] integrity, float[] damageMultiplier,
        AircraftLook look, Gun[] guns, Pilot pilot)
    {
        Guns = guns;
        _sheet = look.Sheet;
        Propellers = new Propeller[look.Propellers.Length];
        for (var i = 0; i < Propellers.Length; i++) Propellers[i] = new Propeller(look.Propellers[i]);
        FirePoints = look.FirePoints;
        Flight = new FlightModel(airframe);
        FuelGal = airframe.FuelCapacityGal;
        Pilot = pilot;
        // One part for each (kind, index) the hit boxes name; armour, integrity and damage multiplier are per kind.
        var all = new List<AircraftPart>();
        for (var k = 0; k < DamageModel.PartCount; k++)
        {
            var kind = (Part)k;
            var ofKind = new List<AircraftPart>();
            foreach (var index in IndicesOf(hitBoxes, kind))
            {
                var boxes = new List<DamageTuning.HitBox>();
                foreach (var b in hitBoxes) if (b.Part == kind && b.Index == index) boxes.Add(b);
                ofKind.Add(new AircraftPart(kind, boxes.ToArray(), armor[k], integrity[k], damageMultiplier[k]));
            }
            if (ofKind.Count == 0) throw new ArgumentException($"{airframe.Name} has no hit box for its {kind}");
            ofKind.Sort((a, b) => a.Centre.X.CompareTo(b.Centre.X));   // left to right
            for (var i = 0; i < ofKind.Count; i++) ofKind[i].Name = AircraftPart.NameOf(kind, i, ofKind.Count);
            all.AddRange(ofKind);
        }
        Parts = all.ToArray();
        Engines = Of(Part.Engine); Wings = Of(Part.Wing);
        Canopy = Of(Part.Canopy)[0]; Tail = Of(Part.Tail)[0]; Fuselage = Of(Part.Fuselage)[0];
        _goneSeen = new bool[Parts.Length];
        _tankSeen = new PartState[Parts.Length];
        _engineSeen = new PartState[Engines.Length];
        _throttleOffSec = new float[Engines.Length];
    }

    private AircraftPart[] Of(Part kind) => Array.FindAll(Parts, p => p.Kind == kind);

    private static List<int> IndicesOf(DamageTuning.HitBox[] boxes, Part kind)
    {
        var indices = new List<int>();
        foreach (var b in boxes) if (b.Part == kind && !indices.Contains(b.Index)) indices.Add(b.Index);
        return indices;
    }

    /// <summary>A point in the aircraft's own frame (feet: x toward the right wing, y up, z toward the tail) in the world (feet).</summary>
    public Vector3 LocalToWorldFt(Vector3 local)
    {
        World.Basis(Flight.Heading, Flight.Gamma, Flight.Bank, out var r, out var u, out var f);
        return PositionFt + r * local.X + u * local.Y - f * local.Z;
    }

    /// <summary>A point in the aircraft's own frame as a point in its sprite sphere's frame (x nose, y left wing, z up).</summary>
    public static Vector3 LocalToSphere(Vector3 local) => new(-local.Z, -local.X, local.Y);

    /// <summary>Where a part's fire burns, in the world (feet): the middle of its biggest hit box.</summary>
    public Vector3 FireFt(AircraftPart p) => LocalToWorldFt(p.Centre);

    /// <summary>Where an engine's fire is drawn on the sprite, in the sprite sphere's frame: the Look's fire point for it
    /// (they are listed left to right like the engines), or the middle of the engine if the Look has no more.</summary>
    public Vector3 EngineFirePoint(AircraftPart engine)
    {
        var i = Array.IndexOf(Engines, engine);
        return i >= 0 && i < FirePoints.Length ? FirePoints[i] : LocalToSphere(engine.Centre);
    }

    // ---------------------------------------------------------------- damage

    /// <summary>A round hits one of its parts (see DamageModel.Hit). Returns the hit points the part lost.</summary>
    public float Hit(AircraftPart part, DamageTuning.Weapon weapon, float impactSpeedFtS, Random rng, Pilot by = null)
    {
        var loss = DamageModel.Hit(part, weapon, impactSpeedFtS, rng);
        Damaged(part, loss, by, rng);
        return loss;
    }

    /// <summary>Damage that has already got through a part's armour (see DamageModel.Apply), for a round that carries its
    /// damage on from part to part. Returns the hit points the part lost.</summary>
    public float TakeDamage(AircraftPart part, DamageTuning.Weapon weapon, float left, Random rng, Pilot by = null)
    {
        var loss = DamageModel.Apply(part, weapon, left);
        Damaged(part, loss, by, rng);
        return loss;
    }

    /// <summary>A round has taken hit points off a part: whoever fired it dealt the part's latest blow (and so lit any
    /// engine fire that follows).</summary>
    private void Damaged(AircraftPart part, float loss, Pilot by, Random rng)
    {
        if (loss <= 0f) return;
        if (by != null) part.LastHitBy = by;
        if (part.Kind == Part.Engine) EngineDamaged(part, rng);
    }

    /// <summary>Takes hit points straight off a part (debug, or damage that isn't a round).</summary>
    public void DamagePart(AircraftPart part, float hp, Random rng)
    {
        part.Hp = MathF.Max(0f, part.Hp - hp);
        PartDamaged(part, rng);
    }

    /// <summary>A part has just lost hit points: an engine has its say (EngineDamaged), and a part holding fuel tanks
    /// rolls TankLeakChance of a new leak for each damage band it has newly entered.</summary>
    private void PartDamaged(AircraftPart part, Random rng)
    {
        if (part.Kind == Part.Engine) EngineDamaged(part, rng);
        if (!Airframe.IsFuelTank(part.Kind)) return;
        var i = Array.IndexOf(Parts, part);
        var now = part.State;
        while (_tankSeen[i] < now)
        {
            _tankSeen[i]++;
            if (rng.NextDouble() < DamageTuning.TankLeakChance) Leaks++;
        }
    }

    /// <summary>An engine has just lost hit points: each band it has newly entered has its say (a leak roll on entering
    /// yellow and on entering orange; a fire on reaching red), and a hit on an engine already in red lights it again.</summary>
    private void EngineDamaged(AircraftPart engine, Random rng)
    {
        var e = Array.IndexOf(Engines, engine);
        var now = engine.State;
        while (_engineSeen[e] < now)
        {
            _engineSeen[e]++;
            if (_engineSeen[e] is PartState.Slight or PartState.Moderate)
                if (rng.NextDouble() < DamageTuning.LeakChance) Leaks++;
        }
        if (now >= PartState.Critical && FuelGal > 0f)
        {
            engine.OnFire = true;
            _throttleOffSec[e] = 0f;
        }
    }

    /// <summary>Fuel burns (by throttle, see Airframe.FuelBurnTable, in proportion to how many engines are still running)
    /// and every leak drains it faster on top; an engine fire burns on while the throttle is up, eating the engine, and goes
    /// out with the throttle held at idle or the fuel gone. Once an engine is gone the throttle can't starve it: only
    /// running out of fuel puts it out.</summary>
    private void UpdateEngine(float dt)
    {
        var throttle = Flight.Throttle;
        if (FuelGal > 0f)
        {
            var running = 0;
            foreach (var e in Engines) if (!e.Gone) running++;
            var burn = Airframe.FuelBurnGalPerHour(throttle) / 60f * running / Engines.Length;   // gal/min
            FuelGal = MathF.Max(0f, FuelGal - (burn + Leaks * DamageTuning.LeakGalPerMin) * dt / 60f);
        }
        for (var i = 0; i < Engines.Length; i++)
        {
            var e = Engines[i];
            if (!e.OnFire) continue;
            if (FuelGal <= 0f) { e.OnFire = false; continue; }
            _throttleOffSec[i] = throttle < DamageTuning.FireThrottleOff && !e.Gone ? _throttleOffSec[i] + dt : 0f;
            if (_throttleOffSec[i] >= DamageTuning.FireOutSec) { e.OnFire = false; continue; }
            var rate = MathHelper.Lerp(DamageTuning.FireBurnMinHpPerSec, DamageTuning.FireBurnMaxHpPerSec, FireStrengthOf(e));
            e.Hp = MathF.Max(0f, e.Hp - rate * dt);
        }
    }

    /// <summary>Fires on parts other than engines eat their part and never go out; a part (other than an engine, whose
    /// fire never spreads) that is gone and burning sets its neighbours alight, a chance each second.</summary>
    private void UpdateFires(float dt, Random rng)
    {
        foreach (var p in Parts)
        {
            if (!p.OnFire || p.Kind == Part.Engine) continue;
            var rate = MathHelper.Lerp(DamageTuning.PartFireBurnMinHpPerSec, DamageTuning.PartFireBurnMaxHpPerSec, FireStrengthOf(p));
            p.Hp = MathF.Max(0f, p.Hp - rate * dt);
        }
        foreach (var p in Parts)
        {
            if (!p.Gone || !p.OnFire || p.Kind == Part.Engine) continue;
            foreach (var n in NeighboursOf(p))
                if (!n.OnFire && rng.NextDouble() < DamageTuning.FireSpreadChancePerSec * dt)
                {
                    n.OnFire = true;
                    n.LastHitBy = p.LastHitBy;   // the fire is the blow of whoever set it
                }
        }
    }

    /// <summary>The parts touching a part, for fire spreading and an engine's blast (see DamageModel.Neighbours for the
    /// kinds that touch). An engine and a wing touch only on the same side of the aircraft.</summary>
    private List<AircraftPart> NeighboursOf(AircraftPart p)
    {
        var touching = DamageModel.Neighbours(p.Kind);
        var found = new List<AircraftPart>();
        foreach (var n in Parts)
        {
            if (n == p || Array.IndexOf(touching, n.Kind) < 0) continue;
            var engineAndWing = (p.Kind, n.Kind) is (Part.Engine, Part.Wing) or (Part.Wing, Part.Engine);
            if (engineAndWing && p.Side != 0 && n.Side != 0 && p.Side != n.Side) continue;
            found.Add(n);
        }
        return found;
    }

    /// <summary>Deals with each part the moment it is gone (once). The first part gone is a confirmed kill, credited to the
    /// pilot who dealt that part its last blow (see Killed). Then:
    ///   engine   - blows up: an explosion that destroys one random part touching it and sets it alight (so that
    ///              part's own loss follows); the engine burns on, but its own fire never spreads after that;
    ///   fuselage - the airframe breaks: an explosion, it burns, and the aircraft noses over into a steep spiralling dive;
    ///   a wing   - the other wing's lift rolls it over: a spin toward the side the wing was on (AircraftPart.Side) all
    ///              the way down;
    ///   canopy   - the pilot is dead: nobody flies it, it holds its heading and the nose slowly sinks;
    ///   tail     - all pitch and roll control gone (see UpdateControls).</summary>
    private void CheckLosses(Fx fx, Random rng)
    {
        for (var i = 0; i < Parts.Length; i++)
        {
            var p = Parts[i];
            if (!p.Gone || _goneSeen[i]) continue;
            _goneSeen[i] = true;
            if (!Killed)
            {
                Killed = true;
                KilledBy = p.LastHitBy;
                if (KilledBy != null && KilledBy != Pilot) KilledBy.Kills++;
            }
            switch (p.Kind)
            {
                case Part.Engine:
                    fx.Explosion(FireFt(p), 30f, IsPlayer);
                    p.OnFire = true;
                    // The blast takes one part touching the engine with it, picked at random: that part is gone and on fire
                    // (its loss is dealt with further on in this same pass: the parts after the engine). After that the
                    // engine's own fire stays where it is.
                    var near = NeighboursOf(p);
                    var blown = near[rng.Next(near.Count)];
                    blown.Hp = 0f;
                    blown.OnFire = true;
                    blown.LastHitBy = p.LastHitBy;   // whoever blew the engine took this part too
                    break;
                case Part.Fuselage:
                    fx.Explosion(FireFt(p), 40f, IsPlayer);
                    p.OnFire = true;
                    Fail(FlightFailure.NoseDive, rng.Next(2) == 0 ? -1f : 1f);
                    break;
                case Part.Wing:
                    // It spins toward the wing that is gone; a wing on the centre line has no side, so either way.
                    Fail(FlightFailure.Spin, p.Side != 0 ? p.Side : rng.Next(2) == 0 ? -1f : 1f);
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
    /// roll and rudder, the wings share theirs (the average) off roll and turn; a black wing's lost lift rolls the aircraft
    /// toward the side with less (a wing's side is where it sits: left is -1, right +1).</summary>
    private void UpdateControls()
    {
        var tail = DamageModel.ControlLoss(Tail.Hp);
        float wings = 0f, liftL = 0f, liftR = 0f;
        int nL = 0, nR = 0;
        foreach (var w in Wings)
        {
            wings += DamageModel.ControlLoss(w.Hp) / Wings.Length;
            var lift = DamageModel.LiftLoss(w.Hp);
            if (w.Side <= 0) { liftL += lift; nL++; }
            if (w.Side >= 0) { liftR += lift; nR++; }
        }
        if (nL > 0) liftL /= nL;
        if (nR > 0) liftR /= nR;
        Flight.PitchAuthority = 1f - tail;
        Flight.RollAuthority = (1f - wings) * (1f - tail);
        Flight.YawAuthority = 1f - tail;
        Flight.TurnAuthority = 1f - wings;
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
        foreach (var e in Engines)
        {
            var smoke = SmokeStrengthOf(e);
            if (smoke > 0f || e.OnFire || Leaks > 0)
                fx.EngineTrail(FireFt(e) - fwd * 4f, -fwd, smoke, FireStrengthOf(e), Leaks, IsPlayer);
        }
        foreach (var p in Parts)
            if (p.OnFire && p.Kind != Part.Engine) fx.EngineTrail(FireFt(p) - fwd * 2f, -fwd, 0f, FireStrengthOf(p), 0, IsPlayer);
        if (Frozen) return;
        Flight.Step(Airframe.RollResponse, rng);
        for (var i = 0; i < Propellers.Length; i++)
        {
            // A propeller turns with the engine it sits on (they are listed left to right like the engines).
            var engine = Engines[Math.Min(i, Engines.Length - 1)];
            Propellers[i].Update(Flight.Throttle, FuelGal > 0f && !engine.Gone);
        }
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
    /// <summary>The pilot who dealt it its latest blow: the last round to take hit points off it, or the fire that whoever
    /// lit it set here. Whoever this is when the part goes gets the kill.</summary>
    public Pilot LastHitBy;
    /// <summary>The middle of its biggest hit box, in the aircraft's frame: where its fire burns.</summary>
    public readonly Vector3 Centre;

    /// <summary>Which side of the aircraft it is on, from where its biggest hit box is: -1 left, +1 right, 0 on the centre
    /// line (within a foot of it). A wing's side is the way the aircraft spins when it goes.</summary>
    public int Side => Centre.X < -1f ? -1 : Centre.X > 1f ? 1 : 0;

    /// <summary>What it is called on the screen: its kind's name when there is only one of it, L or R in front of it when
    /// there are two (L WING, R ENGINE), otherwise numbered left to right (ENGINE 3).</summary>
    public string Name { get; set; }

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
        Name = DamageModel.Name(kind);
    }

    public static string NameOf(Part kind, int leftToRight, int count) => count switch
    {
        1 => DamageModel.Name(kind),
        2 => (leftToRight == 0 ? "L " : "R ") + DamageModel.Name(kind),
        _ => $"{DamageModel.Name(kind)} {leftToRight + 1}",
    };

    public PartState State => DamageModel.StateOf(Hp);
    public bool Gone => Hp <= 0f;
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
