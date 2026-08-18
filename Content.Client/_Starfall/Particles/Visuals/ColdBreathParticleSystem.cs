using System.Numerics;
using Content.Shared._Starfall.Particles;
using Content.Shared._Starfall.Particles.Visuals;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._Starfall.Particles.Visuals;

/// <summary>Displays server-authorized cold breath puffs.</summary>
public sealed partial class ColdBreathParticleSystem : EntitySystem
{
    private static readonly ProtoId<ParticleEffectPrototype> ColdBreathEffect = "SfColdBreath";

    [Dependency] private ParticleSystem _particles = null!;
    [Dependency] private SpriteSystem _sprites = null!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<ColdBreathParticleEvent>(OnColdBreath);
    }

    private void OnColdBreath(ColdBreathParticleEvent ev)
    {
        var entity = GetEntity(ev.Entity);
        if (!EntityManager.EntityExists(entity))
        {
            _particles.SpawnEffect(ColdBreathEffect, ev.Coords);
            return;
        }

        ParticleRuntimeOverrides? overrides = null;
        if (TryComp<SpriteComponent>(entity, out var sprite))
        {
            var bounds = _sprites.GetLocalBounds((entity, sprite));
            var facing = Transform(entity).LocalRotation.RotateVec(Vector2.UnitY);

            // Project the sprite bounds onto its facing axis. Width is used for east/west
            // facings and height for north/south, with diagonal facings blending the two.
            var facingExtent = MathF.Abs(facing.X) * bounds.Width * 0.5f +
                               MathF.Abs(facing.Y) * bounds.Height * 0.5f;
            var mouthDistance = MathF.Max(0.1f, facingExtent * 0.7f);
            overrides = new ParticleRuntimeOverrides
            {
                SpawnOffset = new Vector2(0f, mouthDistance),
            };
        }

        _particles.CreateParticle(ColdBreathEffect, entity, overrides: overrides);
    }
}
