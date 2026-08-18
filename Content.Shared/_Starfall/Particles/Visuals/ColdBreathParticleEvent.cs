using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Starfall.Particles.Visuals;

/// <summary>Sent to nearby clients when an unmasked entity exhales into cold air.</summary>
[Serializable, NetSerializable]
public sealed class ColdBreathParticleEvent : EntityEventArgs
{
    public MapCoordinates Coords;
    public NetEntity Entity;

    public ColdBreathParticleEvent(MapCoordinates coords, NetEntity entity)
    {
        Coords = coords;
        Entity = entity;
    }
}
