using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.XAML;
using Robust.Client.GameObjects;
using Robust.Shared.IoC;
using System;
using Content.Client.Stylesheets;
using Content.Shared.Power;
using Content.Shared.SMES;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using FancyWindow = Content.Client.UserInterface.Controls.FancyWindow;

namespace Content.Client.Power.SMES.UI;

[GenerateTypedNameReferences]
public sealed partial class SmesMenu : FancyWindow
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    public SmesMenu(SmesBoundUserInterface owner, ClientUserInterfaceComponent component)
    {
        IoCManager.InjectDependencies(this);
        RobustXamlLoader.Load(this);

        EntityView.Sprite = _entityManager.GetComponent<SpriteComponent>(component.Owner);
    }

    public void UpdateState(SmesBoundInterfaceState state)
    {
        if (PowerLabel != null)
        {
            PowerLabel.Text = state.Power + "W";
        }

        if (ExternalPowerStateLabel != null)
        {
            PowerUIHelpers.FillExternalPowerLabel(ExternalPowerStateLabel, state.ExternalPower);
        }

        if (ChargeBar != null)
        {
            PowerUIHelpers.FillBatteryChargeProgressBar(ChargeBar, state.Charge);
            var chargePercentage = (state.Charge / ChargeBar.MaxValue);
            ChargePercentage.Text = Loc.GetString("apc-menu-charge-label", ("percent", chargePercentage.ToString("P0")));
        }
    }
}
