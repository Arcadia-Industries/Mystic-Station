using System.Globalization;
using Content.Shared.Atmos.Piping.Portable.Components;
using Content.Shared.Atmos.Visuals;
using Content.Shared.Examine;
using Content.Shared.UserInterface;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.Components;
using Robust.Server.GameObjects;

namespace Content.Server.Atmos.Portable
{
    public sealed class SpaceHeaterSystem : EntitySystem
    {
        [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
        [Dependency] private readonly PowerReceiverSystem _power = default!;
        [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
        [Dependency] private readonly UserInterfaceSystem _userInterfaceSystem = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<SpaceHeaterComponent, BeforeActivatableUIOpenEvent>(OnBeforeOpened);
            SubscribeLocalEvent<SpaceHeaterComponent, AtmosDeviceUpdateEvent>(OnDeviceUpdated);
            SubscribeLocalEvent<SpaceHeaterComponent, PowerChangedEvent>(OnPowerChanged);

            SubscribeLocalEvent<SpaceHeaterComponent, SpaceHeaterToggleMessage>(OnToggle);
            SubscribeLocalEvent<SpaceHeaterComponent, SpaceHeaterChangeTemperatureMessage>(OnTemperatureChanged);
            SubscribeLocalEvent<SpaceHeaterComponent, SpaceHeaterChangeModeMessage>(OnModeChanged);
        }

        private void OnBeforeOpened(EntityUid uid, SpaceHeaterComponent spaceHeater, BeforeActivatableUIOpenEvent args)
        {
            DirtyUI(uid, spaceHeater);
        }

        private void OnDeviceUpdated(EntityUid uid, SpaceHeaterComponent spaceHeater, ref AtmosDeviceUpdateEvent args)
        {
            if (!_power.IsPowered(uid)
                || !TryComp<GasThermoMachineComponent>(uid, out var thermoMachine))
            {
                return;
            }

            //First get the heat direction of the thermomachine (if any) and update appeareance accordingly
            if (thermoMachine.HysteresisState == false)
            {
                spaceHeater.State = SpaceHeaterState.StandBy;
            }
            else
            {
                spaceHeater.State = thermoMachine.Cp > 0 ? SpaceHeaterState.Heating : SpaceHeaterState.Cooling;
            }
            UpdateAppearance(uid, spaceHeater.State);

            //Then, if in automatic temperature mode, check if we need to adjust the heat exchange direction
            if (spaceHeater.Mode == SpaceHeaterMode.Auto)
            {
                var environment = _atmosphereSystem.GetContainingMixture(uid);
                if (environment == null)
                    return;

                if (environment.Temperature < thermoMachine.TargetTemperature - thermoMachine.TemperatureTolerance)
                {
                    thermoMachine.Cp = spaceHeater.HeatingCp;
                }
                else if (environment.Temperature > thermoMachine.TargetTemperature + thermoMachine.TemperatureTolerance)
                {
                    thermoMachine.Cp = spaceHeater.CoolingCp;
                }
            }
        }

        private void OnPowerChanged(EntityUid uid, SpaceHeaterComponent spaceHeater, ref PowerChangedEvent args)
        {
            UpdateAppearance(uid, spaceHeater.State);
            DirtyUI(uid, spaceHeater);
        }

        private void OnToggle(EntityUid uid, SpaceHeaterComponent spaceHeater, SpaceHeaterToggleMessage args)
        {
            ApcPowerReceiverComponent? powerReceiver = null;
            if (!Resolve(uid, ref powerReceiver))
                return;

            _power.TogglePower(uid);
            if (powerReceiver.PowerDisabled)
                spaceHeater.State = SpaceHeaterState.Off;
            else
                spaceHeater.State = SpaceHeaterState.StandBy;

            UpdateAppearance(uid, spaceHeater.State);
            DirtyUI(uid, spaceHeater);
        }

        private void OnTemperatureChanged(EntityUid uid, SpaceHeaterComponent spaceHeater, SpaceHeaterChangeTemperatureMessage args)
        {
            if (!TryComp<GasThermoMachineComponent>(uid, out var thermoMachine))
                return;

            thermoMachine.TargetTemperature = args.Temperature;
            DirtyUI(uid, spaceHeater);
        }

        private void OnModeChanged(EntityUid uid, SpaceHeaterComponent spaceHeater, SpaceHeaterChangeModeMessage args)
        {
            if (!TryComp<GasThermoMachineComponent>(uid, out var thermoMachine))
                return;

            spaceHeater.Mode = args.Mode;

            if (spaceHeater.Mode == SpaceHeaterMode.Heat)
                thermoMachine.Cp = spaceHeater.HeatingCp;
            else if (spaceHeater.Mode == SpaceHeaterMode.Cool)
                thermoMachine.Cp = spaceHeater.CoolingCp;

            DirtyUI(uid, spaceHeater);
        }

        private void DirtyUI(EntityUid uid, SpaceHeaterComponent? spaceHeater)
        {
            if (!Resolve(uid, ref spaceHeater)
                || !TryComp<GasThermoMachineComponent>(uid, out var thermoMachine)
                || !TryComp<ApcPowerReceiverComponent>(uid, out var powerReceiver))
            {
                return;
            }

            _userInterfaceSystem.TrySetUiState(uid, SpaceHeaterUiKey.Key,
                new SpaceHeaterBoundUserInterfaceState(spaceHeater.MinTemperature, spaceHeater.MaxTemperature, thermoMachine.TargetTemperature, !powerReceiver.PowerDisabled, spaceHeater.Mode));
        }

        private void UpdateAppearance(EntityUid uid, SpaceHeaterState state)
        {
            if (!_power.IsPowered(uid))
            {
                _appearance.SetData(uid, SpaceHeaterVisuals.State, SpaceHeaterState.Off);
                return;
            }

            _appearance.SetData(uid, SpaceHeaterVisuals.State, state);
        }
    }
}
