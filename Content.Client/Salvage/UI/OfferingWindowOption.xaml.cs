using System.Linq;
using Content.Client.Computer;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared.CCVar;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Procedural;
using Content.Shared.Salvage;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Salvage.Expeditions.Modifiers;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.AutoGenerated;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Salvage.UI;

/// <summary>
/// Generic window for offering multiple selections with a timer.
/// </summary>
[GenerateTypedNameReferences]
public sealed partial class OfferingWindowOption : PanelContainer
{
    public string? Title
    {
        get => TitleStripe.Text;
        set => TitleStripe.Text = value;
    }

    public event Action<BaseButton.ButtonEventArgs>? ClaimPressed;

    public OfferingWindowOption()
    {
        RobustXamlLoader.Load(this);
        IoCManager.InjectDependencies(this);

        LayoutContainer.SetAnchorPreset(this, LayoutContainer.LayoutPreset.Wide);
        BigPanel.PanelOverride = new StyleBoxFlat(new Color(30, 30, 34));

        ClaimButton.OnPressed += ClaimPressed;
    }

    public void AddContent(Control control)
    {
        ContentBox.AddChild(control);
    }
}
