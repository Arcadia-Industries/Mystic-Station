using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Server.Atmos;
using Content.Server.Atmos.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.NodeContainer;
using Content.Shared.Atmos.Piping;
using Content.Shared.Atmos;
using Content.Shared.CCVar;
using Content.Shared.Interaction;
using JetBrains.Annotations;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server.Atmos.EntitySystems;

public sealed class HeatExchangerSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HeatExchangerComponent, AtmosDeviceUpdateEvent>(OnAtmosUpdate);
    }

    private void OnAtmosUpdate(EntityUid uid, HeatExchangerComponent comp, AtmosDeviceUpdateEvent args)
    {
        if (!EntityManager.TryGetComponent(uid, out NodeContainerComponent? nodeContainer)
                || !_nodeContainer.TryGetNode(nodeContainer, comp.InletName, out PipeNode? inlet)
                || !_nodeContainer.TryGetNode(nodeContainer, comp.OutletName, out PipeNode? outlet))
        {
            return;
        }

        // Positive dN flows from inlet to outlet
        var dt = 1/_atmosphereSystem.AtmosTickRate;
        var dP = inlet.Air.Pressure - outlet.Air.Pressure;
        var dN = comp.G*dP*dt;

        GasMixture xfer;
        if (dN > 0)
            xfer = inlet.Air.Remove(dN);
        else
            xfer = outlet.Air.Remove(-dN);

        var radTemp = Atmospherics.TCMB;

        // Convection
        var environment = _atmosphereSystem.GetContainingMixture(uid, true, true);
        if (environment != null)
        {
            radTemp = environment.Temperature;

            // Positive dT is from pipe to surroundings
            var dT = xfer.Temperature - environment.Temperature;
            var dE = comp.K * dT * dt;
            var envLim = Math.Abs(_atmosphereSystem.GetHeatCapacity(environment) * dT * dt);
            var xferLim = Math.Abs(_atmosphereSystem.GetHeatCapacity(xfer) * dT * dt);
            var dEactual = Math.Sign(dE) * Math.Min(Math.Abs(dE), Math.Min(envLim, xferLim));
            _atmosphereSystem.AddHeat(xfer, -dEactual);
            _atmosphereSystem.AddHeat(environment, dEactual);
        }

        // Radiation
        float dTR = xfer.Temperature - radTemp;
        float a0 = _cfg.GetCVar(CCVars.SuperconductionTileLoss) / MathF.Pow(Atmospherics.T20C, 4);
        float dER = comp.alpha * a0 * MathF.Pow(dTR, 4) * dt;
        _atmosphereSystem.AddHeat(xfer, -dER);

        if (dN > 0)
            _atmosphereSystem.Merge(outlet.Air, xfer);
        else
            _atmosphereSystem.Merge(inlet.Air, xfer);

    }
}
