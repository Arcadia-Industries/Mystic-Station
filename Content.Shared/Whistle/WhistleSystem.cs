using Content.Shared.Coordinates;
using Content.Shared.Humanoid;
using Content.Shared.Interaction.Events;
using Content.Shared.Stealth.Components;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.Whistle;

public abstract class SharedWhistleSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _entityLookup = default!;
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
    }

    private void ExclamateTarget(EntityUid target, WhistleComponent component)
    {
        SpawnAttachedTo(component.Effect, target.ToCoordinates());
    }

    public void OnUseInHand(EntityUid uid, WhistleComponent component, UseInHandEvent args)
    {
        if (_netManager.IsClient && !_timing.IsFirstTimePredicted)
            return;

        TryMakeLoudWhistle(uid, args.User, component);
        args.Handled = true;
    }

    public bool TryMakeLoudWhistle(EntityUid uid, EntityUid owner, WhistleComponent? component = null)
    {
        if (!Resolve(uid, ref component, false) || component.Distance <= 0)
            return false;

        MakeLoudWhistle(uid, owner, component);
        return true;
    }

    private void MakeLoudWhistle(EntityUid uid, EntityUid owner, WhistleComponent component)
    {
        foreach (var iterator in
            _entityLookup.GetComponentsInRange<HumanoidAppearanceComponent>(Transform(uid).Coordinates, component.Distance))
        {
            if (HasComp<StealthComponent>(iterator.Owner))
                continue;

            if (iterator.Owner == owner)
                continue;

            ExclamateTarget(iterator.Owner, component);
        }
    }
}
