using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos.Piping;
using Content.Server.Atmos.Piping.Binary.Components;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Atmos.Piping.EntitySystems;
using Content.Server.Construction;
using Content.Server.Nodes.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Audio;
using Content.Shared.Examine;
using JetBrains.Annotations;
using Robust.Server.GameObjects;

namespace Content.Server.Atmos.Piping.Binary.EntitySystems
{
    [UsedImplicitly]
    public sealed class GasReyclerSystem : EntitySystem
    {
        [Dependency] private readonly AppearanceSystem _appearance = default!;
        [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
        [Dependency] private readonly SharedAmbientSoundSystem _ambientSoundSystem = default!;
        [Dependency] private readonly NodeGraphSystem _nodeSystem = default!;
        [Dependency] private readonly AtmosPipeNetSystem _pipeNodeSystem = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<GasRecyclerComponent, AtmosDeviceEnabledEvent>(OnEnabled);
            SubscribeLocalEvent<GasRecyclerComponent, AtmosDeviceUpdateEvent>(OnUpdate);
            SubscribeLocalEvent<GasRecyclerComponent, AtmosDeviceDisabledEvent>(OnDisabled);
            SubscribeLocalEvent<GasRecyclerComponent, ExaminedEvent>(OnExamined);
            SubscribeLocalEvent<GasRecyclerComponent, RefreshPartsEvent>(OnRefreshParts);
            SubscribeLocalEvent<GasRecyclerComponent, UpgradeExamineEvent>(OnUpgradeExamine);
        }

        private void OnEnabled(EntityUid uid, GasRecyclerComponent comp, AtmosDeviceEnabledEvent args)
        {
            UpdateAppearance(uid, comp);
        }

        private void OnExamined(EntityUid uid, GasRecyclerComponent comp, ExaminedEvent args)
        {
            if (!EntityManager.GetComponent<TransformComponent>(uid).Anchored || !args.IsInDetailsRange) // Not anchored? Out of range? No status.
                return;

            if (!_nodeSystem.TryGetNode<AtmosPipeNodeComponent>(uid, comp.InletName, out var inletId, out var inletNode, out var inlet))
                return;

            if (comp.Reacting)
            {
                args.PushMarkup(Loc.GetString("gas-recycler-reacting"));
            }
            else
            {
                if (!_pipeNodeSystem.TryGetGas(inletId, out var inletGas, inlet, inletNode) || inletGas.Pressure < comp.MinPressure)
                {
                    args.PushMarkup(Loc.GetString("gas-recycler-low-pressure"));
                }

                if (inletGas is null || inletGas.Temperature < comp.MinTemp)
                {
                    args.PushMarkup(Loc.GetString("gas-recycler-low-temperature"));
                }
            }
        }

        private void OnUpdate(EntityUid uid, GasRecyclerComponent comp, AtmosDeviceUpdateEvent args)
        {

            if (!_nodeSystem.TryGetNode<AtmosPipeNodeComponent>(uid, comp.InletName, out var inletId, out var inletNode, out var inlet)
            || !_pipeNodeSystem.TryGetGas(inletId, out var inletGas, inlet, inletNode)
            || !_nodeSystem.TryGetNode<AtmosPipeNodeComponent>(uid, comp.OutletName, out var outletId, out var outletNode, out var outlet)
            || !_pipeNodeSystem.TryGetGas(outletId, out var outletGas, outlet, outletNode))
            {
                _ambientSoundSystem.SetAmbience(uid, false);
                return;
            }

            // The gas recycler is a passive device, so it permits gas flow even if nothing is being reacted.
            comp.Reacting = inletGas.Temperature >= comp.MinTemp && inletGas.Pressure >= comp.MinPressure;
            var removed = inletGas.RemoveVolume(PassiveTransferVol(inletGas, outletGas));
            if (comp.Reacting)
            {
                var nCO2 = removed.GetMoles(Gas.CarbonDioxide);
                removed.AdjustMoles(Gas.CarbonDioxide, -nCO2);
                removed.AdjustMoles(Gas.Oxygen, nCO2);
                var nN2O = removed.GetMoles(Gas.NitrousOxide);
                removed.AdjustMoles(Gas.NitrousOxide, -nN2O);
                removed.AdjustMoles(Gas.Nitrogen, nN2O);
            }

            _atmosphereSystem.Merge(outletGas, removed);
            UpdateAppearance(uid, comp);
            _ambientSoundSystem.SetAmbience(uid, true);
        }

        public float PassiveTransferVol(GasMixture inlet, GasMixture outlet)
        {
            if (inlet.Pressure < outlet.Pressure)
            {
                return 0;
            }
            float overPressConst = 300; // pressure difference (in atm) to get 200 L/sec transfer rate
            float alpha = Atmospherics.MaxTransferRate / (float) Math.Sqrt(overPressConst * Atmospherics.OneAtmosphere);
            return alpha * (float) Math.Sqrt(inlet.Pressure - outlet.Pressure);
        }

        private void OnDisabled(EntityUid uid, GasRecyclerComponent comp, AtmosDeviceDisabledEvent args)
        {
            comp.Reacting = false;
            UpdateAppearance(uid, comp);
        }

        private void UpdateAppearance(EntityUid uid, GasRecyclerComponent? comp = null)
        {
            if (!Resolve(uid, ref comp, false))
                return;

            _appearance.SetData(uid, PumpVisuals.Enabled, comp.Reacting);
        }

        private void OnRefreshParts(EntityUid uid, GasRecyclerComponent component, RefreshPartsEvent args)
        {
            var ratingTemp = args.PartRatings[component.MachinePartMinTemp];
            var ratingPressure = args.PartRatings[component.MachinePartMinPressure];

            component.MinTemp = component.BaseMinTemp * MathF.Pow(component.PartRatingMinTempMultiplier, ratingTemp - 1);
            component.MinPressure = component.BaseMinPressure * MathF.Pow(component.PartRatingMinPressureMultiplier, ratingPressure - 1);
        }

        private void OnUpgradeExamine(EntityUid uid, GasRecyclerComponent component, UpgradeExamineEvent args)
        {
            args.AddPercentageUpgrade("gas-recycler-upgrade-min-temp", component.MinTemp / component.BaseMinTemp);
            args.AddPercentageUpgrade("gas-recycler-upgrade-min-pressure", component.MinPressure / component.BaseMinPressure);
        }
    }
}
