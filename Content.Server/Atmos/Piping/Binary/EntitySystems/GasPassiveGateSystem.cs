using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Binary.Components;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Atmos.Piping.EntitySystems;
using Content.Server.Nodes.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Examine;
using JetBrains.Annotations;

namespace Content.Server.Atmos.Piping.Binary.EntitySystems
{
    [UsedImplicitly]
    public sealed class GasPassiveGateSystem : EntitySystem
    {
        [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
        [Dependency] private readonly NodeGraphSystem _nodeSystem = default!;
        [Dependency] private readonly AtmosPipeNetSystem _pipeNodeSystem = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<GasPassiveGateComponent, AtmosDeviceUpdateEvent>(OnPassiveGateUpdated);
            SubscribeLocalEvent<GasPassiveGateComponent, ExaminedEvent>(OnExamined);
        }

        private void OnPassiveGateUpdated(EntityUid uid, GasPassiveGateComponent gate, AtmosDeviceUpdateEvent args)
        {
            if (!_nodeSystem.TryGetNode<AtmosPipeNodeComponent>(uid, gate.InletName, out var inletId, out var inletNode, out var inlet)
            || !_pipeNodeSystem.TryGetGas(inletId, out var inletGas, inlet, inletNode))
                return;
            if (!_nodeSystem.TryGetNode<AtmosPipeNodeComponent>(uid, gate.OutletName, out var outletId, out var outletNode, out var outlet)
            || !_pipeNodeSystem.TryGetGas(outletId, out var outletGas, outlet, outletNode))
                return;

            var n1 = inletGas.TotalMoles;
            var n2 = outletGas.TotalMoles;
            var P1 = inletGas.Pressure;
            var P2 = outletGas.Pressure;
            var V1 = inletGas.Volume;
            var V2 = outletGas.Volume;
            var T1 = inletGas.Temperature;
            var T2 = outletGas.Temperature;
            var pressureDelta = P1 - P2;

            float dt = 1/_atmosphereSystem.AtmosTickRate;
            float dV = 0;
            var denom = (T1*V2 + T2*V1);

            if (pressureDelta > 0 && P1 > 0 && denom > 0)
            {
                // Calculate the number of moles to transfer to equalize the final pressure of
                // both sides of the valve. You can derive this equation yourself by solving
                // the equations:
                //
                //    P_inlet,final = P_outlet,final (pressure equilibrium)
                //    n_inlet,initial + n_outlet,initial = n_inlet,final + n_outlet,final (mass conservation)
                //
                // These simplifying assumptions allow an easy closed-form solution:
                //
                //    T_inlet,initial = T_inlet,final
                //    T_outlet,initial = T_outlet,final
                //
                // If you don't want to push through the math, just know that this behaves like a
                // pump that can equalize pressure instantly, i.e. much faster than pressure or
                // volume pumps.
                var transferMoles = n1 - (n1+n2)*T2*V1 / denom;

                // Get the volume transfered to update our flow meter.
                dV = n1*Atmospherics.R*T1/P1;

                // Actually transfer the gas.
                _atmosphereSystem.Merge(outletGas, inletGas.Remove(transferMoles));
            }

            // Update transfer rate with an exponential moving average.
            var tau = 1;    // Time constant (averaging time) in seconds
            var a = dt/tau;
            gate.FlowRate = a*dV/tau + (1-a)*gate.FlowRate; // in L/sec
        }

        private void OnExamined(EntityUid uid, GasPassiveGateComponent gate, ExaminedEvent args)
        {
            if (!EntityManager.GetComponent<TransformComponent>(uid).Anchored || !args.IsInDetailsRange) // Not anchored? Out of range? No status.
                return;

            var str = Loc.GetString("gas-passive-gate-examined", ("flowRate", $"{gate.FlowRate:0.#}"));
            args.PushMarkup(str);
        }
    }
}
