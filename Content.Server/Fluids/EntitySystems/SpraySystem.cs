using Content.Server.Chemistry.Components;
using Content.Server.Chemistry.EntitySystems;
using Content.Server.Cooldown;
using Content.Server.Extinguisher;
using Content.Server.Fluids.Components;
using Content.Server.Popups;
using Content.Shared.Audio;
using Content.Shared.Cooldown;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Vapor;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.Fluids.EntitySystems;

public sealed class SpraySystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly SolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private readonly VaporSystem _vaporSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SprayComponent, AfterInteractEvent>(OnAfterInteract, after: new []{ typeof(FireExtinguisherSystem) });
    }

    private void OnAfterInteract(EntityUid uid, SprayComponent component, AfterInteractEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (!_solutionContainerSystem.TryGetSolution(uid, SprayComponent.SolutionName, out var solution))
            return;

        var ev = new SprayAttemptEvent(args.User);
        RaiseLocalEvent(uid, ev, false);
        if (ev.Cancelled)
            return;

        var curTime = _gameTiming.CurTime;
        if (TryComp<ItemCooldownComponent>(uid, out var cooldown)
            && curTime < cooldown.CooldownEnd)
            return;

        if (solution.CurrentVolume <= 0)
        {
            _popupSystem.PopupEntity(Loc.GetString("spray-component-is-empty-message"), uid,
                Filter.Entities(args.User));
            return;
        }

        var playerXform = Transform(args.User);
        var playerEntPos = playerXform.Coordinates;
        var playerMapPos = playerXform.MapPosition;
        var playerEntInvMatrix = playerXform.InvWorldMatrix;

        var clickMapPos = args.ClickLocation.ToMap(EntityManager);

        var diffPos = clickMapPos.Position - playerMapPos.Position;
        if (diffPos == Vector2.Zero || diffPos == Vector2.NaN)
            return;

        var diffLength = diffPos.Length;
        var diffNorm = diffPos.Normalized;
        var diffAngle = diffNorm.ToAngle();

        // Vectors to determine the spawn position of the vapor clouds.
        var threeQuarters = diffNorm * 0.75f;
        var quarter = diffNorm * 0.25f;

        var amount = Math.Max(Math.Min((solution.CurrentVolume / component.TransferAmount).Int(), component.VaporAmount), 1);
        var spread = component.VaporSpread / amount;

        for (var i = 0; i < amount; i++)
        {
            var rotation = new Angle(diffAngle + Angle.FromDegrees(spread * i) -
                                     Angle.FromDegrees(spread * (amount - 1) / 2));

            var target = playerMapPos
                .Offset((diffNorm + rotation.ToVec()).Normalized * diffLength + quarter);

            var distance = target.Position.Length;
            if (distance > component.SprayDistance)
                target = playerMapPos.Offset(diffNorm * component.SprayDistance);

            var newSolution = _solutionContainerSystem.SplitSolution(uid, solution, component.TransferAmount);

            if (newSolution.TotalVolume <= FixedPoint2.Zero)
                break;

            // Spawn the vapor cloud local to the spray user, then reattach to the grid underneath.
            // TODO: Cache the underlying grid/map on first run. Future runs can just attach directly.
            var vaporPos = playerMapPos.Offset(distance < 1 ? quarter : threeQuarters).Position;
            var vapor = Spawn(component.SprayedPrototype, new EntityCoordinates(args.User, playerEntInvMatrix.Transform(vaporPos)));
            var vaporXform = Transform(vapor);
            vaporXform.WorldRotation = rotation;
            vaporXform.AttachToGridOrMap();

            if (TryComp(vapor, out AppearanceComponent? appearance))
            {
                appearance.SetData(VaporVisuals.Color, solution.Color.WithAlpha(1f));
                appearance.SetData(VaporVisuals.State, true);
            }

            // Add the solution to the vapor and actually send the thing
            var vaporComponent = Comp<VaporComponent>(vapor);
            _vaporSystem.TryAddSolution(vaporComponent, newSolution);

            // impulse direction is defined in world-coordinates, not local coordinates
            var impulseDirection = rotation.ToVec();
            _vaporSystem.Start(vaporComponent, impulseDirection, component.SprayVelocity, target, component.SprayAliveTime);

            if (component.Impulse > 0f && TryComp(args.User, out PhysicsComponent? body))
                body.ApplyLinearImpulse(-impulseDirection * component.Impulse);
        }

        SoundSystem.Play(component.SpraySound.GetSound(), Filter.Pvs(uid), uid, AudioHelpers.WithVariation(0.125f));

        RaiseLocalEvent(uid,
            new RefreshItemCooldownEvent(curTime, curTime + TimeSpan.FromSeconds(component.CooldownTime)), true);
    }
}

public sealed class SprayAttemptEvent : CancellableEntityEventArgs
{
    public EntityUid User;

    public SprayAttemptEvent(EntityUid user)
    {
        User = user;
    }
}
