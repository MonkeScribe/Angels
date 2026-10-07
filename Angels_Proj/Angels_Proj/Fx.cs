using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>World-space (feet) effects that are left behind in the air, drawn by both the map and the gunsight: the
/// black smoke trailing from a damaged engine, the mist of fuel from a leak, and hit sparks.</summary>
public sealed class Fx
{
    public enum Kind { Smoke, Vapour, Spark }

    public struct Particle
    {
        public Vector3 Pos, Vel;
        public float Life, MaxLife, Size, Growth;
        public float Alpha;       // peak opacity
        public float Shade;       // smoke: grey level the sprite is tinted to (0 black .. 1 as drawn)
        public float Rot;         // turn of the sprite, so neighbouring puffs don't look alike
        public int Variant;       // which smoke sprite
        public bool Own;          // from the player's own aircraft: the gunsight (in the cockpit) leaves these out
        public Kind Kind;
        public float T => Life / MaxLife;

        /// <summary>Opacity now: in quickly, then thinning away over its life.</summary>
        public float Opacity => Alpha * MathF.Min(1f, (MaxLife - Life) / 0.12f) * T;
    }

    public readonly List<Particle> Particles = new();
    private readonly Random _rng = new(11);

    private float R() => (float)_rng.NextDouble() - 0.5f;
    private float U() => (float)_rng.NextDouble();

    public void Spark(Vector3 pos) => Particles.Add(new Particle
    {
        Kind = Kind.Spark, Pos = pos, Vel = new Vector3(R(), R(), R()) * 60f, Life = 0.18f, MaxLife = 0.18f, Size = 1.6f, Alpha = 1f, Shade = 1f,
    });

    /// <summary>
    /// What a damaged engine leaves behind this tick. at is the exhaust (world feet), back the direction to the tail.
    /// smoke and fire are 0-1 (AircraftDamage.SmokeStrength / FireStrength); both thicken and darken the trail, which
    /// is a puff most ticks, so at flying speed the puffs overlap into a continuous trail. leaks is the number of fuel
    /// leaks, which trail a thin pale mist from under the fuselage.
    /// </summary>
    public void EngineTrail(Vector3 at, Vector3 back, float smoke, float fire, int leaks, bool own)
    {
        var heavy = MathF.Max(smoke, fire > 0f ? 0.45f + 0.55f * fire : 0f);
        if (heavy > 0.001f && U() < 0.3f + 0.7f * heavy)
        {
            var life = 2.5f + 4f * heavy;
            Particles.Add(new Particle
            {
                Kind = Kind.Smoke, Own = own,
                Pos = at + back * (2f + U() * 3f) + new Vector3(R(), R(), R()) * (1.5f + 2f * heavy),
                Vel = new Vector3(R() * 3f, 2f + U() * 3f, R() * 3f),
                Life = life, MaxLife = life,
                Size = 2.5f + 4f * heavy, Growth = 2.5f + 6f * heavy,
                Alpha = 0.25f + 0.65f * heavy,
                Shade = MathHelper.Lerp(0.75f, 0.2f, heavy),
                Rot = U() * MathF.Tau, Variant = _rng.Next(4),
            });
        }
        if (leaks > 0 && U() < MathF.Min(1f, 0.35f * leaks))
        {
            var life = 1.2f + 0.4f * leaks;
            Particles.Add(new Particle
            {
                Kind = Kind.Vapour, Own = own,
                Pos = at + back * (8f + U() * 2f) + new Vector3(R(), R() - 1.5f, R()),
                Vel = new Vector3(R(), R(), R()),
                Life = life, MaxLife = life, Size = 1.2f, Growth = 1.5f + 0.5f * leaks, Alpha = 0.35f, Shade = 1f,
                Rot = U() * MathF.Tau, Variant = _rng.Next(4),
            });
        }
    }

    public void Update()
    {
        const float dt = 1f / 60f;
        for (var i = Particles.Count - 1; i >= 0; i--)
        {
            var p = Particles[i];
            p.Life -= dt;
            if (p.Life <= 0f) { Particles.RemoveAt(i); continue; }
            p.Pos += p.Vel * dt;
            p.Vel *= 0.985f;
            p.Size = Math.Max(0.5f, p.Size + p.Growth * dt);
            Particles[i] = p;
        }
    }
}
