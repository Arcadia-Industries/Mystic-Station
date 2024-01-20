using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;
using Content.Shared.Random.Helpers;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Shared.Random;

namespace Content.Shared.Toilet;

/// <summary>
/// Plungers can be used to unblock entities with PlungerUseComponent.
/// </summary>
public sealed class PlungerSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] protected readonly IPrototypeManager _proto = default!;
    [Dependency] protected readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PlungerComponent, AfterInteractEvent>(OnInteract);
        SubscribeLocalEvent<PlungerComponent, PlungerDoAfterEvent>(OnDoAfter);
    }

    private void OnInteract(EntityUid uid, PlungerComponent component, AfterInteractEvent args)
    {
        if (args.Handled)
            return;

        if (!args.CanReach || args.Target is not { Valid: true } target || !HasComp<PlungerUseComponent>(args.Target))
            return;

        TryComp<PlungerUseComponent>(args.Target, out var plunger);

        if (plunger == null || !plunger.NeedsPlunger == false)
            return;

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, component.PlungeDuration, new PlungerDoAfterEvent(), uid, target, uid)
        {
            BreakOnUserMove = true,
            BreakOnDamage = true,
            BreakOnTargetMove = true,
            MovementThreshold = 1.0f,
        });
        args.Handled = true;
    }

    private void OnDoAfter(EntityUid uid, PlungerComponent component, DoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Args.Target == null || !HasComp<PlungerUseComponent>(args.Args.Target))
            return;

        if (args.Target is not { Valid: true } target)
            return;

        if (!TryComp(target, out PlungerUseComponent? plunge))
            return;

        if (_timing.IsFirstTimePredicted)
        {
            _audio.PlayPvs(plunge.Sound, target);
            _popup.PopupClient(Loc.GetString("plunger-unblock", ("target", target)), args.User, args.User, PopupType.Medium);
            plunge.Plunged = true;

            var spawn = _proto.Index<WeightedRandomEntityPrototype>(plunge.PlungerLoot).Pick(_random);

            if (_net.IsServer)
                Spawn(spawn, Transform(target).Coordinates);
            RemComp<PlungerUseComponent>(target);
            Dirty(target, plunge);
        }
        args.Handled = true;
    }
}

