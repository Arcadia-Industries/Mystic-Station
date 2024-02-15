using Content.Shared.Atmos;
using Content.Shared.Atmos.Piping.Portable.Components;
using JetBrains.Annotations;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Atmos.UI
{
    /// <summary>
    /// Initializes a <see cref="SpaceHeaterWindow"/> and updates it when new server messages are received.
    /// </summary>
    [UsedImplicitly]
    public sealed class SpaceHeaterBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private SpaceHeaterWindow? _window;

        [ViewVariables]
        private float _minTemp = 0.0f;

        [ViewVariables]
        private float _maxTemp = 0.0f;

        public SpaceHeaterBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();

            _window = new SpaceHeaterWindow();

            if (State != null)
                UpdateState(State);

            _window.OpenCentered();

            _window.OnClose += Close;

            _window.ToggleStatusButton.OnPressed += _ => OnToggleStatusButtonPressed();
            _window.TemperatureSpinbox.OnValueChanged += OnTemperatureChanged;
            _window.Mode.OnItemSelected += OnModeChanged;
        }

        private void OnToggleStatusButtonPressed()
        {
            _window?.SetActive(!_window.Active);
            SendMessage(new SpaceHeaterToggleMessage());
        }

        private void OnTemperatureChanged(FloatSpinBox.FloatSpinBoxEventArgs args)
        {
            var actualTemp = Math.Clamp(args.Value, _minTemp, _maxTemp);
            actualTemp = Math.Max(actualTemp, Atmospherics.TCMB);
            _window?.SetTemperature(actualTemp);
            SendMessage(new SpaceHeaterChangeTemperatureMessage(actualTemp));
        }
        private void OnModeChanged(OptionButton.ItemSelectedEventArgs args)
        {
            _window?.Mode.SelectId(args.Id);
            SendMessage(new SpaceHeaterChangeModeMessage((SpaceHeaterMode) args.Id));
        }

        /// <summary>
        /// Update the UI state based on server-sent info
        /// </summary>
        /// <param name="state"></param>
        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);
            if (_window == null || state is not SpaceHeaterBoundUserInterfaceState cast)
                return;

            _window.SetActive(cast.Enabled);
            _window.Mode.SelectId((int) cast.Mode);

            _minTemp = cast.MinTemperature;
            _maxTemp = cast.MaxTemperature;
            _window.SetTemperature(cast.Temperature);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
                return;
            _window?.Dispose();
        }
    }
}
