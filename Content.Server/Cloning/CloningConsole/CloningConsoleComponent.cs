using Content.Server.UserInterface;
using Robust.Server.GameObjects;
using Content.Server.Cloning.GeneticScanner;
using Content.Server.Cloning.Components;
using Content.Shared.Cloning.CloningConsole;

namespace Content.Server.Cloning.CloningConsole
{
    [RegisterComponent]
    public sealed class CloningConsoleComponent : SharedCloningConsoleComponent
    {
        [ViewVariables]
        public BoundUserInterface? UserInterface => Owner.GetUIOrNull(CloningConsoleUiKey.Key);
        [ViewVariables]
        public GeneticScannerComponent? GeneticScanner = null;
        [ViewVariables]
        public CloningPodComponent? CloningPod = null;
        [ViewVariables]
        public List<String> CloningHistory = new List<string>();
    }
}
