using System.Linq;
using Content.Client.Stylesheets;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Reagent;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Prototypes;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client.Chemistry.UI
{
    /// <summary>
    /// Client-side UI used to control a <see cref="ReagentDispenserComponent"/>.
    /// </summary>
    [GenerateTypedNameReferences]
    public sealed partial class ReagentDispenserWindow : DefaultWindow
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        public event Action<BaseButton.ButtonEventArgs, DispenseReagentButton>? OnDispenseReagentButtonPressed;
        public event Action<GUIMouseHoverEventArgs, DispenseReagentButton>? OnDispenseReagentButtonMouseEntered;
        public event Action<GUIMouseHoverEventArgs, DispenseReagentButton>? OnDispenseReagentButtonMouseExited;

        /// <summary>
        /// Create and initialize the dispenser UI client-side. Creates the basic layout,
        /// actual data isn't filled in until the server sends data about the dispenser.
        /// </summary>
        public ReagentDispenserWindow()
        {
            RobustXamlLoader.Load(this);
            IoCManager.InjectDependencies(this);

            var dispenseAmountGroup = new ButtonGroup();
            DispenseButton1.Group = dispenseAmountGroup;
            DispenseButton5.Group = dispenseAmountGroup;
            DispenseButton10.Group = dispenseAmountGroup;
            DispenseButton15.Group = dispenseAmountGroup;
            DispenseButton20.Group = dispenseAmountGroup;
            DispenseButton25.Group = dispenseAmountGroup;
            DispenseButton30.Group = dispenseAmountGroup;
            DispenseButton50.Group = dispenseAmountGroup;
            DispenseButton100.Group = dispenseAmountGroup;
        }

        /// <summary>
        /// Update the button grid of reagents which can be dispensed.
        /// </summary>
        /// <param name="inventory">Reagents which can be dispensed by this dispenser</param>
        public void UpdateReagentsList(List<ReagentId> inventory)
        {
            if (ChemicalList == null)
                return;

            ChemicalList.Children.Clear();

            foreach (var entry in inventory
                .OrderBy(r => {_prototypeManager.TryIndex(r.Prototype, out ReagentPrototype? p); return p?.LocalizedName;}))
            {
                var localizedName = _prototypeManager.TryIndex(entry.Prototype, out ReagentPrototype? p)
                    ? p.LocalizedName
                    : Loc.GetString("reagent-dispenser-window-reagent-name-not-found-text");

                var button = new DispenseReagentButton(entry, localizedName);
                button.OnPressed += args => OnDispenseReagentButtonPressed?.Invoke(args, button);
                button.OnMouseEntered += args => OnDispenseReagentButtonMouseEntered?.Invoke(args, button);
                button.OnMouseExited += args => OnDispenseReagentButtonMouseExited?.Invoke(args, button);
                ChemicalList.AddChild(button);
            }
        }

        /// <summary>
        /// Update the UI state when new state data is received from the server.
        /// </summary>
        /// <param name="state">State data sent by the server.</param>
        public void UpdateState(BoundUserInterfaceState state)
        {
            var castState = (ReagentDispenserBoundUserInterfaceState) state;
            UpdateContainerInfo(castState);
            UpdateReagentsList(castState.Inventory);

            // Disable the Clear & Eject button if no beaker
            ClearButton.Disabled = castState.OutputContainer is null;
            EjectButton.Disabled = castState.OutputContainer is null;

            switch (castState.SelectedDispenseAmount)
            {
                case ReagentDispenserDispenseAmount.U1:
                    DispenseButton1.Pressed = true;
                    break;
                case ReagentDispenserDispenseAmount.U5:
                    DispenseButton5.Pressed = true;
                    break;
                case ReagentDispenserDispenseAmount.U10:
                    DispenseButton10.Pressed = true;
                    break;
                case ReagentDispenserDispenseAmount.U15:
                    DispenseButton15.Pressed = true;
                    break;
                case ReagentDispenserDispenseAmount.U20:
                    DispenseButton20.Pressed = true;
                    break;
                case ReagentDispenserDispenseAmount.U25:
                    DispenseButton25.Pressed = true;
                    break;
                case ReagentDispenserDispenseAmount.U30:
                    DispenseButton30.Pressed = true;
                    break;
                case ReagentDispenserDispenseAmount.U50:
                    DispenseButton50.Pressed = true;
                    break;
                case ReagentDispenserDispenseAmount.U100:
                    DispenseButton100.Pressed = true;
                    break;
            }
        }

        /// <summary>
        /// Update the fill state and list of reagents held by the current reagent container, if applicable.
        /// <para>Also highlights a reagent if it's dispense button is being mouse hovered.</para>
        /// </summary>
        /// <param name="state">State data for the dispenser.</param>
        /// <param name="highlightedReagentId">Prototype ID of the reagent whose dispense button is currently being mouse hovered,
        /// or null if no button is being hovered.</param>
        public void UpdateContainerInfo(ReagentDispenserBoundUserInterfaceState state, ReagentId? highlightedReagentId = null)
        {
            ContainerInfo.Children.Clear();

            if (state.OutputContainer is null)
            {
                ContainerInfo.Children.Add(new Label {Text = Loc.GetString("reagent-dispenser-window-no-container-loaded-text") });
                return;
            }

            ContainerInfo.Children.Add(new BoxContainer // Name of the container and its fill status (Ex: 44/100u)
            {
                Orientation = LayoutOrientation.Horizontal,
                Children =
                {
                    new Label {Text = $"{state.OutputContainer.DisplayName}: "},
                    new Label
                    {
                        Text = $"{state.OutputContainer.CurrentVolume}/{state.OutputContainer.MaxVolume}",
                        StyleClasses = {StyleNano.StyleClassLabelSecondaryColor}
                    }
                }
            });

            foreach (var reagent in state.OutputContainer.Reagents!)
            {
                // Try get to the prototype for the given reagent. This gives us its name.
                var localizedName = _prototypeManager.TryIndex(reagent.Prototype, out ReagentPrototype? p)
                    ? p.LocalizedName
                    : Loc.GetString("reagent-dispenser-window-reagent-name-not-found-text");

                var nameLabel = new Label {Text = $"{localizedName}: "};
                var quantityLabel = new Label
                {
                    Text = Loc.GetString("reagent-dispenser-window-quantity-label-text", ("quantity", reagent.Quantity)),
                    StyleClasses = {StyleNano.StyleClassLabelSecondaryColor},
                };

                // Check if the reagent is being moused over. If so, color it green.
                if (reagent.Id == highlightedReagentId) {
                    nameLabel.SetOnlyStyleClass(StyleNano.StyleClassPowerStateGood);
                    quantityLabel.SetOnlyStyleClass(StyleNano.StyleClassPowerStateGood);
                }

                ContainerInfo.Children.Add(new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                    Children =
                    {
                        nameLabel,
                        quantityLabel,
                    }
                });
            }
        }
    }

    public sealed class DispenseReagentButton : Button {
        public ReagentId ReagentId { get; }

        public DispenseReagentButton(ReagentId reagentId, string text)
        {
            ReagentId = reagentId;
            Text = text;
        }
    }
}
