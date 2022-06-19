using Content.Client.Computer;
using Content.Client.UserInterface;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

[GenerateTypedNameReferences]
public sealed partial class EmergencyShuttleConsoleWindow : FancyWindow,
    IComputerWindow<RadarConsoleBoundInterfaceState>
{
    public EmergencyShuttleConsoleWindow()
    {
        RobustXamlLoader.Load(this);
    }
}
