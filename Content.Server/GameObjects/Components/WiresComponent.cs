using System;
using System.Collections.Generic;
using Content.Server.GameObjects.Components.Interactable.Tools;
using Content.Server.GameObjects.Components.VendingMachines;
using Content.Server.GameObjects.EntitySystems;
using Content.Server.Interfaces;
using Content.Server.Interfaces.GameObjects;
using Content.Shared.GameObjects.Components;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Server.GameObjects.Components.UserInterface;
using Robust.Server.Interfaces.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Interfaces.Random;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.GameObjects.Components
{
    [RegisterComponent]
    public class WiresComponent : SharedWiresComponent, IAttackBy, IExamine
    {
#pragma warning disable 649
        [Dependency] private readonly IRobustRandom _random;
        [Dependency] private readonly IServerNotifyManager _notifyManager;
#pragma warning restore 649

        private AppearanceComponent _appearance;
        private BoundUserInterface _userInterface;
        private bool _isOpen;

        public bool IsOpen
        {
            get => _isOpen;
            private set
            {
                _isOpen = value;
                _appearance.SetData(WiresVisuals.MaintenancePanelState, value);
            }
        }

        /// <summary>
        /// Contains all registered wires.
        /// </summary>
        public readonly List<Wire> WiresList = new List<Wire>();

        /// <summary>
        /// As seen on /vg/station.
        /// <see cref="AssignColor"/> and <see cref="WiresBuilder.CreateWire"/>.
        /// </summary>
        private readonly List<Color> _availableColors = new List<Color>()
        {
            Color.Red,
            Color.Blue,
            Color.Green,
            Color.Orange,
            Color.Brown,
            Color.Gold,
            Color.Gray,
            Color.Cyan,
            Color.Navy,
            Color.Purple,
            Color.Pink,
            Color.Fuchsia,
            Color.Aqua,
        };

        public override void Initialize()
        {
            base.Initialize();
            _appearance = Owner.GetComponent<AppearanceComponent>();
            _appearance.SetData(WiresVisuals.MaintenancePanelState, IsOpen);
            _userInterface = Owner.GetComponent<ServerUserInterfaceComponent>()
                .GetBoundUserInterface(WiresUiKey.Key);
            _userInterface.OnReceiveMessage += UserInterfaceOnReceiveMessage;

            foreach (var wiresProvider in Owner.GetAllComponents<IWires>())
            {
                var builder = new WiresBuilder(this, wiresProvider);
                wiresProvider.RegisterWires(builder);
            }
        }

        public class Wire
        {
            /// <summary>
            /// Used in client-server communication to identify a wire without telling the client what the wire does.
            /// </summary>
            public readonly Guid Guid;
            /// <summary>
            /// Registered by components implementing IWires, used to identify which wire the client interacted with.
            /// </summary>
            public readonly object Identifier;
            /// <summary>
            /// The color of the wire. It needs to have a corresponding entry in <see cref="Robust.Shared.Maths.Color.DefaultColors"/>.
            /// </summary>
            public readonly Color Color;
            /// <summary>
            /// The component that registered the wire.
            /// </summary>
            public readonly IWires Owner;
            /// <summary>
            /// Whether the wire is cut.
            /// </summary>
            public bool IsCut;
            public Wire(Guid guid, object identifier, Color color, IWires owner, bool isCut)
            {
                Guid = guid;
                Identifier = identifier;
                Color = color;
                Owner = owner;
                IsCut = isCut;
            }
        }

        /// <summary>
        /// Used by <see cref="IWires.RegisterWires"/>.
        /// </summary>
        public class WiresBuilder
        {
            [NotNull] private readonly WiresComponent _wires;
            [NotNull] private readonly IWires _owner;

            public WiresBuilder(WiresComponent wires, IWires owner)
            {
                _wires = wires;
                _owner = owner;
            }

            public void CreateWire(object identifier, Color? color = null, bool isCut = false)
            {
                if (!color.HasValue)
                {
                    color = _wires.AssignColor();
                }
                else
                {
                    _wires._availableColors.Remove(color.Value);
                }
                _wires.WiresList.Add(new Wire(Guid.NewGuid(), identifier, color.Value, _owner, isCut));
            }
        }

        /// <summary>
        /// Picks a color from <see cref="_availableColors"/> and removes it from the list.
        /// </summary>
        /// <returns>The picked color.</returns>
        private Color AssignColor()
        {
            if(_availableColors.Count == 0)
            {
                return Color.Black;
            }
            return _random.PickAndTake(_availableColors);
        }

        /// <summary>
        /// Call this from other components to open the wires UI.
        /// </summary>
        public void OpenInterface(IPlayerSession session)
        {
            _userInterface.Open(session);
        }

        private void UserInterfaceOnReceiveMessage(ServerBoundUserInterfaceMessage serverMsg)
        {
            var message = serverMsg.Message;
            switch (message)
            {
                case WiresActionMessage msg:
                    var wire = WiresList.Find(x => x.Guid == msg.Guid);
                    var player = serverMsg.Session.AttachedEntity;
                    if (!player.TryGetComponent(out IHandsComponent handsComponent))
                    {
                        _notifyManager.PopupMessage(Owner.Transform.GridPosition, player, "You have no hands.");
                        return;
                    }
                    var activeHandEntity = handsComponent.GetActiveHand?.Owner;
                    switch (msg.Action)
                    {
                        case WiresAction.Cut:
                            if (activeHandEntity?.HasComponent<WirecutterComponent>() != true)
                            {
                                _notifyManager.PopupMessage(Owner.Transform.GridPosition, player, "You need to hold a wirecutter in your hand!");
                                return;
                            }
                            wire.IsCut = true;
                            break;
                        case WiresAction.Mend:
                            if (activeHandEntity?.HasComponent<WirecutterComponent>() != true)
                            {
                                _notifyManager.PopupMessage(Owner.Transform.GridPosition, player, "You need to hold a wirecutter in your hand!");
                                return;
                            }
                            wire.IsCut = false;
                            break;
                        case WiresAction.Pulse:
                            if (activeHandEntity?.HasComponent<MultitoolComponent>() != true)
                            {
                                _notifyManager.PopupMessage(Owner.Transform.GridPosition, player, "You need to hold a multitool in your hand!");
                                return;
                            }
                            if (wire.IsCut)
                            {
                                _notifyManager.PopupMessage(Owner.Transform.GridPosition, player, "You can't pulse a wire that's been cut!");
                                return;
                            }
                            break;
                    }
                    wire.Owner.WiresUpdate(new WiresUpdateEventArgs(wire.Identifier, msg.Action));
                    _userInterface.SendMessage(CreateClientWiresList());
                    break;
                case WiresSyncRequestMessage msg:
                    _userInterface.SendMessage(CreateClientWiresList());
                    break;
            }
        }

        /// <summary>
        /// Creates a <see cref="SharedWiresComponent.WiresListMessage"/> from the server-side <see cref="WiresList"/>.
        /// </summary>
        /// <returns>The message for the client.</returns>
        private WiresListMessage CreateClientWiresList()
        {
            var clientList = new List<ClientWiresListEntry>();
            foreach (var entry in WiresList)
            {
                clientList.Add(new ClientWiresListEntry(entry.Guid, entry.Color, entry.IsCut));
            }

            return new WiresListMessage(clientList);
        }

        bool IAttackBy.AttackBy(AttackByEventArgs eventArgs)
        {
            if (!eventArgs.AttackWith.HasComponent<ScrewdriverComponent>()) return false;
            IsOpen = !IsOpen;
            return true;
        }

        void IExamine.Examine(FormattedMessage message)
        {
            message.AddText($"The maintenance panel is {(IsOpen ? "open" : "closed")}.");
        }
    }
}
