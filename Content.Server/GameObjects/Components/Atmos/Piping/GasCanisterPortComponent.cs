#nullable enable
using System.Linq;
using Content.Server.Atmos;
using Content.Server.GameObjects.Components.NodeContainer;
using Content.Server.GameObjects.Components.NodeContainer.Nodes;
using Content.Server.Interfaces.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.ViewVariables;

namespace Content.Server.GameObjects.Components.Atmos.Piping
{
    [RegisterComponent]
    public class GasCanisterPortComponent : Component, IAtmosProcess
    {
        public override string Name => "GasCanisterPort";

        [ViewVariables]
        public GasCanisterComponent? ConnectedCanister { get; private set; }

        [ViewVariables]
        public bool ConnectedToCanister => ConnectedCanister != null;

        [ViewVariables]
        private PipeNode? _gasPort;

        public override void Initialize()
        {
            base.Initialize();
            Owner.EnsureComponentWarn<AtmosDeviceComponent>();
            SetGasPort();
            if (Owner.TryGetComponent<SnapGridComponent>(out var snapGrid))
            {
                var entities = snapGrid.GetLocal();
                foreach (var entity in entities)
                {
                    if (entity.TryGetComponent<GasCanisterComponent>(out var canister) && canister.Anchored && !canister.ConnectedToPort)
                    {
                        canister.TryConnectToPort();
                        break;
                    }
                }
            }
        }

        public override void OnRemove()
        {
            base.OnRemove();
            ConnectedCanister?.DisconnectFromPort();
        }

        public void ConnectGasCanister(GasCanisterComponent gasCanister)
        {
            ConnectedCanister = gasCanister;
        }

        public void DisconnectGasCanister()
        {
            ConnectedCanister = null;
        }

        private void SetGasPort()
        {
            if (!Owner.TryGetComponent<NodeContainerComponent>(out var container))
            {
                Logger.Warning($"{nameof(GasCanisterPortComponent)} on {Owner?.Prototype?.ID}, Uid {Owner?.Uid} did not have a {nameof(NodeContainerComponent)}.");
                return;
            }
            _gasPort = container.Nodes.OfType<PipeNode>().FirstOrDefault();
            if (_gasPort == null)
            {
                Logger.Warning($"{nameof(GasCanisterPortComponent)} on {Owner?.Prototype?.ID}, Uid {Owner?.Uid} could not find compatible {nameof(PipeNode)}s on its {nameof(NodeContainerComponent)}.");
                return;
            }
        }

        public void ProcessAtmos(IGridAtmosphereComponent atmosphere)
        {
            if (_gasPort == null || ConnectedCanister == null)
                return;

            ConnectedCanister.Air.Share(_gasPort.Air, 1);
            ConnectedCanister.AirWasUpdated();
        }
    }
}
