using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Angels_Proj;

/// <summary>World-space (feet) smoke, fire and sparks, drawn by both the map and the gunsight.</summary>
public sealed class Fx
{
    public enum Kind { Smoke, Fire, Spark }

    public struct Particle
    {
        public Vector3 Pos, Vel;
        public float Life, MaxLife, Size, Growth;
        public Kind Kind;
        public float T => Life / MaxLife;
    }

    public readonly List<Particle> Particles = new();
    private readonly Random _rng = new(11);

    private float R() => (float)_rng.NextDouble() - 0.5f;

    private void Add(Kind k, Vector3 pos, Vector3 vel, float life, float size, float growth) =>
        Particles.Add(new Particle { Kind = k, Pos = pos, Vel = vel, Life = life, MaxLife = life, Size = size, Growth = growth });

    public void Smoke(Vector3 pos) => Add(Kind.Smoke, pos + new Vector3(R(), R(), R()) * 4f, new Vector3(R(), 4f + R() * 4f, R()), 3.5f, 5f, 6f);
    public void Fire(Vector3 pos) => Add(Kind.Fire, pos + new Vector3(R(), R(), R()) * 5f, new Vector3(R() * 8f, 6f, R() * 8f), 0.35f, 6f, -8f);
    public void Spark(Vector3 pos) => Add(Kind.Spark, pos, new Vector3(R(), R(), R()) * 60f, 0.18f, 1.6f, 0f);

    public void Explosion(Vector3 pos)
    {
        for (var i = 0; i < 40; i++)
        {
            var v = new Vector3(R(), R() + 0.4f, R()) * 160f;
            Add(i % 3 == 0 ? Kind.Smoke : Kind.Fire, pos, v, 0.6f + (float)_rng.NextDouble() * 1.8f, 8f + (float)_rng.NextDouble() * 10f, 4f);
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
