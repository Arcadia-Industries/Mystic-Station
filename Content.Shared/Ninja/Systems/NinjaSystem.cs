using Content.Shared.Ninja.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Network;

namespace Content.Shared.Ninja.Systems;

public abstract class SharedNinjaSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NinjaComponent, ComponentGetState>(OnNinjaGetState);
        SubscribeLocalEvent<NinjaComponent, ComponentHandleState>(OnNinjaHandleState);
    }

    /// <summary>
    /// Set the ninja's worn suit entity
    /// </summary>
    public void AssignSuit(NinjaComponent comp, EntityUid? suit)
    {
        comp.Suit = suit;
    }

    /// <summary>
    /// Set the ninja's worn gloves entity
    /// </summary>
    public void AssignGloves(NinjaComponent comp, EntityUid? gloves)
    {
        comp.Gloves = gloves;
    }

    /// <summary>
    /// Bind a katana entity to a ninja, letting it be recalled and dash.
    /// </summary>
    public void BindKatana(NinjaComponent comp, EntityUid? katana)
    {
        comp.Katana = katana;
    }

    // TODO: remove when objective stuff moved into objectives somehow
    public void DetonateSpiderCharge(NinjaComponent comp)
    {
        comp.SpiderChargeDetonated = true;
    }

    /// <summary>
    /// Marks the objective as complete.
    /// On server, makes announcement and adds rule of random threat.
    /// </summary>
    public virtual void CallInThreat(NinjaComponent comp)
    {
        comp.CalledInThreat = true;
    }

    /// <summary>
    /// Drain power from a target battery into the ninja's suit battery.
    /// Serverside only.
    /// </summary>
    public virtual void TryDrainPower(EntityUid user, NinjaDrainComponent drain, EntityUid target)
    {
    }

    /// <summary>
    /// Download the given set of nodes, returning how many new nodes were downloaded.'
    /// </summary>
    public int Download(NinjaComponent ninja, List<string> ids)
    {
        var oldCount = ninja.DownloadedNodes.Count;
        ninja.DownloadedNodes.UnionWith(ids);
        var newCount = ninja.DownloadedNodes.Count;
        return newCount - oldCount;
    }

    /// <summary>
    /// Gets the user's battery and tries to use some charge from it, returning true if successful.
    /// Serverside only.
    /// </summary>
    public virtual bool TryUseCharge(EntityUid user, float charge)
    {
        return false;
    }

    private void OnNinjaGetState(EntityUid uid, NinjaComponent comp, ref ComponentGetState args)
    {
        args.State = new NinjaComponentState(comp.DoorsJacked, comp.DownloadedNodes, comp.SpiderChargeTarget, comp.SpiderChargeDetonated, comp.CalledInThreat);
    }

    private void OnNinjaHandleState(EntityUid uid, NinjaComponent comp, ref ComponentHandleState args)
    {
        if (args.Current is not NinjaComponentState state)
            return;

        comp.DoorsJacked = state.DoorsJacked;
        comp.DownloadedNodes = state.DownloadedNodes;
        comp.SpiderChargeTarget = state.SpiderChargeTarget;
        comp.SpiderChargeDetonated = state.SpiderChargeDetonated;
        comp.CalledInThreat = state.CalledInThreat;
    }
}
