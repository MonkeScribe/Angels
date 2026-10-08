using System;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>
/// What kind of gun it is: its title, the caliber of the round it fires (mm, which is what makes the projectile's weight and
/// drag), how fast the round leaves, how fast it cycles, how much it carries, how its tracers look and what a round does on
/// hitting. A type like the Browning is defined once here and mounted on any aircraft as many times as it has guns.
/// </summary>
public sealed class GunSpec
{
    public string Title;
    public float CaliberMm;
    public float MuzzleVelocityFtS;
    public float RoundsPerMinute;
    public int MagazineRounds;          // what one gun carries
    public int TracerEvery;             // one round in this many is a tracer
    public Color TracerColor;
    public float SpreadRad;             // dispersion either way
    public DamageTuning.Weapon Weapon;  // what a round does on hitting (Damage.cs)

    /// <summary>The Browning Mk II .303 of the Spitfire Mk I: 7.7 mm Mk VII ball, ~744 m/s, 1,150 rpm, 300 rounds (about 15.7 s
    /// of fire), one round in five a red tracer, about 3.5 mils of dispersion.</summary>
    public static readonly GunSpec Browning303 = new()
    {
        Title = ".303 BROWNING",
        CaliberMm = 7.7f,
        MuzzleVelocityFtS = 2440f,
        RoundsPerMinute = 1150f,
        MagazineRounds = 300,
        TracerEvery = 5,
        TracerColor = new Color(255, 77, 51),
        SpreadRad = 0.0035f,
        Weapon = DamageTuning.Browning303,
    };
}

/// <summary>
/// One gun on one aircraft: what it is (its GunSpec), where a round leaves the airframe, how it is harmonised and how much
/// ammunition it has left. It doesn't fire itself: the aircraft's pilot does (Pilot.Fire), asking each gun for its rounds
/// and having them made here.
/// </summary>
public sealed class Gun
{
    public readonly GunSpec Spec;

    /// <summary>Where its round leaves the airframe, in the aircraft's frame (feet): x along the right wing, y up, z forward.</summary>
    public readonly Vector3 Muzzle;

    /// <summary>The range (feet) at which it is harmonised to hit the sight line.</summary>
    public readonly float ConvergeFt;

    public string Title => Spec.Title;
    public int Ammo;
    public float Phase;                 // fraction of a round the gun has built up
    private int _fired;                 // rounds fired from this belt, for the tracer pattern

    /// <summary>Rounds the gun cycles per tick.</summary>
    public float RoundsPerTick => Spec.RoundsPerMinute / 60f / 60f;

    public Gun(GunSpec spec, Vector3 muzzle, float convergeFt)
    {
        Spec = spec;
        Muzzle = muzzle;
        ConvergeFt = convergeFt;
        Rearm();
    }

    public void Rearm()
    {
        Ammo = Spec.MagazineRounds;
        _fired = Random.Shared.Next(Spec.TracerEvery);       // belts don't all start on a tracer
        Phase = (float)Random.Shared.NextDouble();           // nor do the guns fire in lockstep
    }

    /// <summary>Whether the next round off the belt is a tracer.</summary>
    public bool NextIsTracer() => _fired++ % Spec.TracerEvery == 0;

    /// <summary>The point it is aimed at: harmonised like the real guns, a touch high, so the rounds have dropped onto the
    /// sight line by the time they reach the convergence range.</summary>
    public Vector3 AimPoint(Vector3 pos, Vector3 up, Vector3 fwd)
    {
        var tc = ConvergeFt / Spec.MuzzleVelocityFtS;
        return pos + fwd * ConvergeFt + up * (0.5f * Projectile.G * tc * tc);
    }

    /// <summary>A random sideways or upward deflection (radians) for one round.</summary>
    public float Spread(Random rng) => ((float)rng.NextDouble() * 2f - 1f) * Spec.SpreadRad;
}
