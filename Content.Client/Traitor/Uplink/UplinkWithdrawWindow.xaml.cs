using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Localization;

namespace Content.Client.Traitor.Uplink
{
    /// <summary>
    ///     Window to select amount TC to withdraw from Uplink account
    ///     Used as sub-window in Uplink UI
    /// </summary>
    [GenerateTypedNameReferences]
    public partial class UplinkWithdrawWindow : SS14Window
    {
        public event System.Action<int>? OnWithdrawAttempt;

        public UplinkWithdrawWindow(int tcCount)
        {
            RobustXamlLoader.Load(this);

            UpdateSliderValue(WithdrawSlider.Value);

            WithdrawSlider.MinValue = 0;
            WithdrawSlider.MaxValue = tcCount;
            WithdrawSlider.OnValueChanged += args => UpdateSliderValue(args.Value);

            ApplyButton.OnButtonDown += _ =>
            {
                var valueInt = (int) WithdrawSlider.Value;
                OnWithdrawAttempt?.Invoke(valueInt);

                Close();
            };

            CancelButton.OnButtonDown += _ => Close();
        }

        private void UpdateSliderValue(float value)
        {
            var valueInt = (int) value;
            var msg = Loc.GetString("uplink-user-interface-withdraw-label", ("balance", valueInt));
            WithdrawAmountLabel.Text = msg;

            ApplyButton.Disabled = valueInt <= 0;
        }
    }
}
