using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Starfall.Particles.Visuals;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Inventory;
using Robust.Shared.Player;

namespace Content.Server._Starfall.Particles.Visuals;

/// <summary>Creates visible breath when an unmasked breathing entity exhales into cold air.</summary>
public sealed partial class ColdBreathParticleSystem : EntitySystem
{
    // Visible breath generally begins around 10 C, depending on humidity.
    private const float ColdBreathTemperature = Atmospherics.T0C + 10f;

    [Dependency] private InventorySystem _inventory = null!;
    [Dependency] private SharedInternalsSystem _internals = null!;
    [Dependency] private SharedTransformSystem _transform = null!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RespiratorComponent, ExhaledGasEvent>(OnExhaled);
    }

    private void OnExhaled(Entity<RespiratorComponent> ent, ref ExhaledGasEvent args)
    {
        // Do not create condensation in warm air or near-vacuum.
        if (args.Gas.Temperature > ColdBreathTemperature ||
            args.Gas.Pressure < Atmospherics.HazardLowPressure)
            return;

        // Working internals keep the entity's breath inside its breathing equipment.
        if (_internals.AreInternalsWorking(ent.Owner))
            return;

        // Ordinary masks hide the mouth. Breath-capable headgear includes sealed hardsuit
        // helmets, which occupy the head slot rather than the mask slot.
        var slots = _inventory.GetSlotEnumerator((ent.Owner, null), SlotFlags.MASK | SlotFlags.HEAD);
        while (slots.NextItem(out var worn, out var slot))
        {
            if ((slot.SlotFlags & SlotFlags.MASK) != 0 || HasComp<BreathToolComponent>(worn))
                return;
        }

        RaiseNetworkEvent(
            new ColdBreathParticleEvent(_transform.GetMapCoordinates(ent.Owner), GetNetEntity(ent.Owner)),
            Filter.Pvs(ent.Owner));
    }
}
