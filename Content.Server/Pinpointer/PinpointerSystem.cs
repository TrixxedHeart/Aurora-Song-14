using Content.Shared.Interaction;
using Content.Shared.Pinpointer;
using System.Linq;
using System.Numerics;
using Robust.Shared.Utility;
using Content.Server.Shuttles.Events;
using Content.Shared.Verbs; // Aurora's Song: LR Config

namespace Content.Server.Pinpointer;

public sealed partial class PinpointerSystem : SharedPinpointerSystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PinpointerComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<FTLCompletedEvent>(OnLocateTarget);
        SubscribeLocalEvent<PinpointerComponent, GetVerbsEvent<InteractionVerb>>(AddToggleVerb);
    }

    public override bool TogglePinpointer(Entity<PinpointerComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return false;

        var isActive = !ent.Comp.IsActive;
        SetActive(ent, isActive);
        UpdateAppearance(ent);
        return isActive;
    }

    private void UpdateAppearance(Entity<PinpointerComponent?, AppearanceComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp1) || !Resolve(ent, ref ent.Comp2))
            return;

        _appearance.SetData(ent, PinpointerVisuals.IsActive, ent.Comp1.IsActive, ent.Comp2);
        _appearance.SetData(ent, PinpointerVisuals.TargetDistance, ent.Comp1.DistanceToTarget, ent.Comp2);
    }

    private void OnActivate(Entity<PinpointerComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        TogglePinpointer(ent.AsNullable());

        if (!ent.Comp.CanRetarget)
            LocateTarget(ent);

        args.Handled = true;
    }

    private void OnLocateTarget(ref FTLCompletedEvent ev)
    {
        // This feels kind of expensive, but it only happens once per hyperspace jump

        // todo: ideally, you would need to raise this event only on jumped entities
        // this code update ALL pinpointers in game
        var query = EntityQueryEnumerator<PinpointerComponent>();

        while (query.MoveNext(out var uid, out var pinpointer))
        {
            if (pinpointer.CanRetarget)
                continue;

            LocateTarget((uid, pinpointer));
        }
    }

    private void LocateTarget(Entity<PinpointerComponent> ent)
    {
        var component = ent.Comp;

        // try to find target from whitelist
        if (component.IsActive && component.Component != null)
        {
            if (!EntityManager.ComponentFactory.TryGetRegistration(component.Component, out var reg))
            {
                Log.Error($"Unable to find component registration for {component.Component} for pinpointer!");
                DebugTools.Assert(false);
                return;
            }

            var target = FindTargetFromComponent(ent.Owner, reg.Type);
            SetTarget(ent.AsNullable(), target);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // because target or pinpointer can move
        // we need to update pinpointers arrow each frame
        var query = EntityQueryEnumerator<PinpointerComponent>();
        while (query.MoveNext(out var uid, out var pinpointer))
        {
            // aurora's song
            if (!pinpointer.HasTarget || pinpointer.Target == null || !Exists(pinpointer.Target.Value))
                continue;

            UpdateDirectionToTarget((uid, pinpointer));
            UpdateTargetCoordinates((uid, pinpointer)); // Aurora's Song
        }
    }

    /// <summary>
    ///     Try to find the closest entity from whitelist on a current map
    ///     Will return null if can't find anything
    /// </summary>
    private EntityUid? FindTargetFromComponent(Entity<TransformComponent?> ent, Type whitelist)
    {
        if (!Resolve(ent, ref ent.Comp))
            return null;

        // sort all entities in distance increasing order
        var mapId = ent.Comp.MapID;
        var l = new SortedList<float, EntityUid>();
        var worldPos = _transform.GetWorldPosition(ent.Comp);

        foreach (var (otherUid, _) in EntityManager.GetAllComponents(whitelist))
        {
            if (!TryComp(otherUid, out TransformComponent? compXform) || compXform.MapID != mapId)
                continue;

            var dist = (_transform.GetWorldPosition(compXform) - worldPos).LengthSquared();
            l.TryAdd(dist, otherUid);
        }

        // return uid with a smallest distance
        return l.Count > 0 ? l.First().Value : null;
    }

    // Aurora's Song Start
    private void UpdateTargetCoordinates(Entity<PinpointerComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        if (ent.Comp.Target != null)
        {
            // this transform is safe because exists is already checked in the caller
            var xform = Transform(ent.Comp.Target.Value);
            var coords = _transform.GetWorldPosition(xform);
            SetTargetCoordinates(ent, coords);
        }
    }
    // Aurora's Song End

    /// <summary>
    ///     Update direction from pinpointer to selected target (if it was set)
    /// </summary>
    protected override void UpdateDirectionToTarget(Entity<PinpointerComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        var pinpointer = ent.Comp;

        if (!pinpointer.IsActive)
            return;

        var oldDist = pinpointer.DistanceToTarget; // Frontier: moved up

        var target = pinpointer.Target;
        if (target == null || !Exists(target.Value))
        {
            SetDistance(ent, Distance.Unknown);
            TrySetArrowAngle(ent, Angle.Zero); // Frontier
            if (oldDist != pinpointer.DistanceToTarget) // Frontier
                UpdateAppearance(ent); // Frontier
            return;
        }

        var dirVec = CalculateDirection(ent, target.Value);
        // var oldDist = pinpointer.DistanceToTarget; // Frontier: moved up

        // Frontier: if the pinpointer has a max range and the distance to target is greater than the max range, set the distance to unknown
        if (pinpointer.MaxRange > 0 && dirVec != null && dirVec.Value.LengthSquared() > pinpointer.MaxRange * pinpointer.MaxRange)
        {
            SetDistance(ent, Distance.Unknown);
            TrySetArrowAngle(ent, Angle.Zero);
            if (oldDist != pinpointer.DistanceToTarget) // Frontier
                UpdateAppearance(ent); // Frontier
            return;
        }

        if (dirVec != null)
        {
            var angle = dirVec.Value.ToWorldAngle();
            TrySetArrowAngle(ent, angle);
            var dist = CalculateDistance(dirVec.Value, pinpointer);
            SetDistance(ent, dist);
        }
        else
        {
            SetDistance(ent, Distance.Unknown);
            TrySetArrowAngle(ent, Angle.Zero); // Frontier
        }
        if (oldDist != pinpointer.DistanceToTarget)
            UpdateAppearance(ent);
    }

    /// <summary>
    ///     Calculate direction from pinUid to trgUid
    /// </summary>
    /// <returns>Null if failed to calculate distance between two entities</returns>
    private Vector2? CalculateDirection(EntityUid pinUid, EntityUid trgUid)
    {
        // check if entities have transform component
        if (!TryComp(pinUid, out TransformComponent? pin))
            return null;
        if (!TryComp(trgUid, out TransformComponent? trg))
            return null;

        // check if they are on same map
        if (pin.MapID != trg.MapID)
            return null;

        // get world direction vector
        var dir = _transform.GetWorldPosition(trg) - _transform.GetWorldPosition(pin);
        return dir;
    }

    private Distance CalculateDistance(Vector2 vec, PinpointerComponent pinpointer)
    {
        var dist = vec.Length();
        // Begin Aurora's Song
        float ModifiedReachedDistance = pinpointer.LongRange ? pinpointer.ReachedDistance * 256 : pinpointer.ReachedDistance; // 256 metres in LR configuration with default value (1 metre)
        float ModifiedCloseDistance = pinpointer.LongRange ? pinpointer.CloseDistance * 128 : pinpointer.CloseDistance; // 1024 metres in LR configuration with default value (8 metres)
        float ModifiedMediumDistance = pinpointer.LongRange ? pinpointer.MediumDistance * 256 : pinpointer.MediumDistance; // 4096 metres in LR configuration with default value (16 metres)
        // End Aurora's Song

        if (dist <= ModifiedReachedDistance) // Aurora's Song: Set to use Modified for dynamicism
            return Distance.Reached;
        else if (dist <= ModifiedCloseDistance) // Aurora's Song: Set to use Modified for dynamicism
            return Distance.Close;
        else if (dist <= ModifiedMediumDistance) // Aurora's Song: Set to use Modified for dynamicism
            return Distance.Medium;
        else
            return Distance.Far;
    }

    // Frontier: clear function
    public void ClearPinpointer(EntityUid uid, PinpointerComponent? pinpointer = null)
    {
        if (!Resolve(uid, ref pinpointer))
            return;

        pinpointer.Target = null;
        UpdateDirectionToTarget((uid, pinpointer));
        UpdateAppearance((uid, pinpointer));
    }
    // End Frontier: clear function

    // Aurora's Song: Verb and Function for toggling LR configuration
    private void AddToggleVerb(EntityUid uid, PinpointerComponent component, GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || args.Hands == null)
            return;

        //here we build our dynamic verb. Using the object's sprite for now to make it more dynamic for the moment.
        InteractionVerb toggleVerb = new()
        {
            IconEntity = GetNetEntity(uid),
            Act = () => ToggleState(component),
            Text = component.LongRange ? Loc.GetString("verb-pinpointer-deactivate-text") : Loc.GetString("verb-pinpointer-activate-text"),
            Priority = 3
        };

        args.Verbs.Add(toggleVerb);
    }
    private void ToggleState(PinpointerComponent component)
    {
        component.LongRange = !component.LongRange;
    }
}
