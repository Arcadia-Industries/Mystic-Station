using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared.Xenoarchaeology.Equipment;
using Robust.Client.AutoGenerated;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Xenoarchaeology.Ui;

[GenerateTypedNameReferences]
public sealed partial class AnalysisConsoleMenu : FancyWindow
{
    [Dependency] private readonly IEntityManager _ent = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public event Action? OnServerSelectionButtonPressed;
    public event Action? OnScanButtonPressed;
    public event Action? OnPrintButtonPressed;
    public event Action? OnExtractButtonPressed;

    // For rendering the progress bar, updated from BUI state
    private TimeSpan? _startTime;
    private TimeSpan? _totalTime;

    public AnalysisConsoleMenu()
    {
        RobustXamlLoader.Load(this);
        IoCManager.InjectDependencies(this);

        ServerSelectionButton.OnPressed += _ => OnServerSelectionButtonPressed?.Invoke();
        ScanButton.OnPressed += _ => OnScanButtonPressed?.Invoke();
        PrintButton.OnPressed += _ => OnPrintButtonPressed?.Invoke();
        ExtractButton.OnPressed += _ => OnExtractButtonPressed?.Invoke();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_startTime is not { } start || _totalTime is not { } total)
            return;

        var remaining = start + total - _timing.CurTime;
        var secsText = Math.Max((int) remaining.TotalSeconds, 0);

        ProgressLabel.Text = Loc.GetString("analysis-console-progress-text",
            ("seconds", secsText));

        // 1.0 - div because we want it to tick up not down
        ProgressBar.Value = Math.Clamp(1.0f - (float) remaining.Divide(total), 0.0f, 1.0f);
    }

    public void SetButtonsDisabled(AnalysisConsoleScanUpdateState state)
    {
        ScanButton.Disabled = !state.CanScan;
        PrintButton.Disabled = !state.CanPrint;

        var disabled = !state.ServerConnected || !state.CanScan || state.PointAmount <= 0;

        ExtractButton.Disabled = disabled;

        if (disabled)
        {
            ExtractButton.RemoveStyleClass("ButtonColorGreen");
        }
        else
        {
            ExtractButton.AddStyleClass("ButtonColorGreen");
        }
    }

    private void UpdateArtifactIcon(EntityUid? uid)
    {
        if (uid == null)
        {
            ArtifactDisplay.Visible = false;
            return;
        }

        ArtifactDisplay.Visible = true;
        ArtifactDisplay.SetEntity(uid);
    }

    public void UpdateInformationDisplay(AnalysisConsoleScanUpdateState state)
    {
        var message = new FormattedMessage();

        if (state.Scanning)
        {
            if (state.Paused)
            {
                message.AddMarkup(Loc.GetString("analysis-console-info-scanner-paused"));
            }
            else
            {
                message.AddMarkup(Loc.GetString("analysis-console-info-scanner"));
            }
            Information.SetMessage(message);
            UpdateArtifactIcon(null); //set it to blank
            return;
        }

        UpdateArtifactIcon(_ent.GetEntity(state.Artifact));

        if (state.ScanReport == null)
        {
            if (!state.AnalyzerConnected) //no analyzer connected
                message.AddMarkup(Loc.GetString("analysis-console-info-no-scanner"));
            else if (!state.CanScan) //no artifact
                message.AddMarkup(Loc.GetString("analysis-console-info-no-artifact"));
            else if (state.Artifact == null) //ready to go
                message.AddMarkup(Loc.GetString("analysis-console-info-ready"));
        }
        else
        {
            message.AddMessage(state.ScanReport);
        }

        Information.SetMessage(message);
    }

    public void UpdateProgressBar(AnalysisConsoleScanUpdateState state)
    {
        ProgressBar.Visible = state.Scanning;
        ProgressLabel.Visible = state.Scanning;

        _startTime = state.StartTime;
        _totalTime = state.TotalTime;
    }
}

