using Robust.Client.AutoGenerated;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using Content.Shared.Localizations;
using Content.Shared.Temperature;
using static Content.Shared.Atmos.Components.SharedGasAnalyzerComponent;

namespace Content.Client.Atmos.UI
{
    /// <summary>
    /// Gas analyzer UI.
    /// </summary>
    [GenerateTypedNameReferences]
    public partial class GasAnalyzerWindow : SS14Window
    {
        int _lastGasCount = 0;

        public GasAnalyzerWindow()
        {
            RobustXamlLoader.Load(this);
        }
        public void UpdateState(GasAnalyzerBoundUserInterfaceState state)
        {
            GasGrid.RemoveAllChildren();

            var statusMessage = new FormattedMessage();
            StatusLabel.SetMessage(statusMessage);

            if (state.Error != null)
            {
                GasBar.RemoveAllChildren();
                statusMessage.PushColor(Color.Red);
                statusMessage.AddMarkup(Loc.GetString("gas-analyzer-window-error-text", ("errorText", state.Error)));
                statusMessage.Pop();
                return;
            }

            statusMessage.AddMarkup(
                Loc.GetString(
                    "gas-analyzer-window-pressure-text",
                    ("pressure", Units.Pressure.Format(state.Pressure,"0.#"))
                )
            );

            statusMessage.PushNewline();
            statusMessage.AddMarkup(Loc.GetString(
                "gas-analyzer-window-temperature-text",
                    ("tempK", Units.Temperature.Format(state.Temperature,"0.#")),
                    ("tempC", $"{TemperatureHelpers.KelvinToCelsius(state.Temperature):0.#}")
                )
            );

            if(state.Gases == null)
            {
                GasBar.RemoveAllChildren();
                return;
            }

            var totalGasAmount = 0f;
            foreach (var gas in state.Gases)
            {
                totalGasAmount += gas.Amount;
            }

            var minSize = 24; // This basically allows gases which are too small, to be shown properly

            // Make sure that the number of children panels in the gas bar matches the number of gases.
            // Note: just erasing them all and recreating them everytime would be simpler, but would make
            // tooltips unusable as it would make them disappear everytime we update.
            if( _lastGasCount > state.Gases.Length )
            {
                GasBar.RemoveAllChildren();
                _lastGasCount = 0;
            }

            while( _lastGasCount < state.Gases.Length )
            {
                GasBar.AddChild(new PanelContainer
                {
                    HorizontalExpand = true,
                    MouseFilter = MouseFilterMode.Pass,
                    MinSize = new Vector2(minSize, 0)
                } );

                _lastGasCount++;
            }

            using(var gasBarEnumerator = GasBar.Children.GetEnumerator())
            {
                foreach(var gas in state.Gases)
                {
                    var color = Color.FromHex($"#{gas.Color}", Color.White);

                    GasGrid.AddChild(new Label
                    {
                        Text = gas.Name,
                        FontColorOverride = color
                    } );
                    GasGrid.AddChild(new Label
                    {
                        Text = Loc.GetString("gas-analyzer-window-molality-text", ("mol", $"{gas.Amount:0.##}")),
                        FontColorOverride = color
                    } );

                    gasBarEnumerator.MoveNext();
                    var panel = (PanelContainer)gasBarEnumerator.Current;
                    panel.ToolTip = Loc.GetString("gas-analyzer-window-molality-percentage-text",
                                        ("gasName", gas.Name),
                                        ("amount", $"{gas.Amount:0.##}"),
                                        ("percentage", $"{(gas.Amount / totalGasAmount * 100):0.#}"));
                    panel.SizeFlagsStretchRatio = gas.Amount;
                    panel.PanelOverride = new StyleBoxFlat
                    {
                        BackgroundColor = color
                    };
                }
            }
        }
    }
}
