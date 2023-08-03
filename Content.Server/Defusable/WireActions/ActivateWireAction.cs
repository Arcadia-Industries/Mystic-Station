using Content.Server.Defusable.Components;
using Content.Server.Defusable.Systems;
using Content.Server.Doors.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Popups;
using Content.Server.Wires;
using Content.Shared.Defusable;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Wires;

namespace Content.Server.Defusable.WireActions;

public sealed class ActivateWireAction : ComponentWireAction<DefusableComponent>
{
    public override Color Color { get; set; } = Color.Lime;
    public override string Name { get; set; } = "wire-name-bomb-live";

    public override StatusLightState? GetLightState(Wire wire, DefusableComponent comp)
    {
        return comp.Activated ? StatusLightState.Off : StatusLightState.BlinkingFast;
    }

    public override object StatusKey { get; } = DefusableWireStatus.LiveIndicator;

    public override bool Cut(EntityUid user, Wire wire, DefusableComponent comp)
    {
        if (comp.Activated)
        {
            EntityManager.System<DefusableSystem>().TryDefuseBomb(wire.Owner, comp);
        }
        return true;
    }

    public override bool Mend(EntityUid user, Wire wire, DefusableComponent comp)
    {
        // bomb is defused why do you want it back on again
        return true;
    }

    public override void Pulse(EntityUid user, Wire wire, DefusableComponent comp)
    {
        if (comp.Activated)
        {
            if (!comp.ActivatedWireUsed)
            {
                EntityManager.System<TriggerSystem>().TryDelay(wire.Owner, 30f);
                comp.ActivatedWireUsed = true;
            }
            EntityManager.System<PopupSystem>().PopupEntity(Loc.GetString("defusable-popup-wire-chirp", ("name", wire.Owner)), wire.Owner);
        }
        else
        {
            EntityManager.System<DefusableSystem>().TryStartCountdown(wire.Owner, comp);
        }
    }
}
