using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Containers;
using Robust.Shared.Input;
using Robust.Shared.Timing;
using static Robust.Client.UserInterface.Controls.BaseButton;
using Range = Robust.Client.UserInterface.Controls.Range;

namespace Content.Client.Instruments.UI
{
    [GenerateTypedNameReferences]
    public sealed partial class InstrumentMenu : DefaultWindow
    {
        private readonly InstrumentBoundUserInterface _owner;

        public InstrumentMenu(InstrumentBoundUserInterface owner)
        {
            RobustXamlLoader.Load(this);

            _owner = owner;

            if (_owner.Instrument != null)
            {
                _owner.Instrument.OnMidiPlaybackEnded += InstrumentOnMidiPlaybackEnded;
                Title = _owner.Entities.GetComponent<MetaDataComponent>(_owner.Owner).EntityName;
                LoopButton.Disabled = !_owner.Instrument.IsMidiOpen;
                LoopButton.Pressed = _owner.Instrument.LoopMidi;
                ChannelsButton.Disabled = !_owner.Instrument.IsRendererAlive;
                StopButton.Disabled = !_owner.Instrument.IsMidiOpen;
                PlaybackSlider.MouseFilter = _owner.Instrument.IsMidiOpen ? MouseFilterMode.Pass : MouseFilterMode.Ignore;
            }

            if (!_owner.MidiManager.IsAvailable)
            {
                UnavailableOverlay.Visible = true;
                // We return early as to not give the buttons behavior.
                return;
            }

            InputButton.OnToggled += MidiInputButtonOnOnToggled;
            BandButton.OnPressed += BandButtonOnPressed;
            BandButton.OnToggled += BandButtonOnToggled;
            FileButton.OnPressed += MidiFileButtonOnOnPressed;
            LoopButton.OnToggled += MidiLoopButtonOnOnToggled;
            ChannelsButton.OnPressed += ChannelsButtonOnPressed;
            StopButton.OnPressed += MidiStopButtonOnPressed;
            PlaybackSlider.OnValueChanged += PlaybackSliderSeek;
            PlaybackSlider.OnKeyBindUp += PlaybackSliderKeyUp;

            MinSize = SetSize = new Vector2(400, 150);
        }

        private void BandButtonOnPressed(ButtonEventArgs obj)
        {
            if (!PlayCheck())
                return;

            _owner.OpenBandMenu();
        }

        private void BandButtonOnToggled(ButtonToggledEventArgs obj)
        {
            if (obj.Pressed)
                return;

            _owner.Instruments.SetMaster(_owner.Owner, null);
        }

        private void ChannelsButtonOnPressed(ButtonEventArgs obj)
        {
            _owner.OpenChannelsMenu();
        }

        private void InstrumentOnMidiPlaybackEnded()
        {
            MidiPlaybackSetButtonsDisabled(true);
        }

        public void MidiPlaybackSetButtonsDisabled(bool disabled)
        {
            if(disabled)
                _owner.CloseChannelsMenu();

            LoopButton.Disabled = disabled;
            StopButton.Disabled = disabled;

            // Whether to allow the slider to receive events..
            PlaybackSlider.MouseFilter = !disabled ? MouseFilterMode.Pass : MouseFilterMode.Ignore;
        }

        private async void MidiFileButtonOnOnPressed(ButtonEventArgs obj)
        {
            _owner.CloseBandMenu();

            var filters = new FileDialogFilters(new FileDialogFilters.Group("mid", "midi"));
            await using var file = await _owner.FileDialogManager.OpenFile(filters);

            // did the instrument menu get closed while waiting for the user to select a file?
            if (Disposed)
                return;

            // The following checks are only in place to prevent players from playing MIDI songs locally.
            // There are equivalents for these checks on the server.

            if (file == null)
                return;

            /*if (!_midiManager.IsMidiFile(filename))
            {
                Logger.Warning($"Not a midi file! Chosen file: {filename}");
                return;
            }*/

            if (!PlayCheck())
                return;

            MidiStopButtonOnPressed(null);
            await using var memStream = new MemoryStream((int) file.Length);
            // 100ms delay is due to a race condition or something idk.
            // While we're waiting, load it into memory.
            await Task.WhenAll(Timer.Delay(100), file.CopyToAsync(memStream));

            if (_owner.Instrument is not {} instrument
                || !_owner.Instruments.OpenMidi(_owner.Owner, memStream.GetBuffer().AsSpan(0, (int) memStream.Length), instrument))
                return;

            MidiPlaybackSetButtonsDisabled(false);
            if (InputButton.Pressed)
                InputButton.Pressed = false;
        }

        private void MidiInputButtonOnOnToggled(ButtonToggledEventArgs obj)
        {
            _owner.CloseBandMenu();

            if (obj.Pressed)
            {
                if (!PlayCheck())
                    return;

                MidiStopButtonOnPressed(null);
                if(_owner.Instrument is {} instrument)
                    _owner.Instruments.OpenInput(_owner.Owner, instrument);
            }
            else if (_owner.Instrument is { } instrument)
            {
                _owner.Instruments.CloseInput(_owner.Owner, false, instrument);
                _owner.CloseChannelsMenu();
            }
        }

        private bool PlayCheck()
        {
            // TODO all of these checks should also be done server-side.

            var instrumentEnt = _owner.Owner;
            var instrument = _owner.Instrument;

            if (instrument == null)
                return false;

            var localPlayer = _owner.PlayerManager.LocalPlayer;

            // If we don't have a player or controlled entity, we return.
            if (localPlayer?.ControlledEntity == null)
                return false;

            // By default, allow an instrument to play itself and skip all other checks
            if (localPlayer.ControlledEntity == instrumentEnt)
                return true;

            // If we're a handheld instrument, we might be in a container. Get it just in case.
            instrumentEnt.TryGetContainerMan(out var conMan);

            // If the instrument is handheld and we're not holding it, we return.
            if ((instrument.Handheld && (conMan == null || conMan.Owner != localPlayer.ControlledEntity)))
                return false;

            if (!_owner.ActionBlocker.CanInteract(localPlayer.ControlledEntity.Value, instrumentEnt))
                return false;

            // We check that we're in range unobstructed just in case.
            return _owner.Interactions.InRangeUnobstructed(localPlayer.ControlledEntity.Value, instrumentEnt);
        }

        private void MidiStopButtonOnPressed(ButtonEventArgs? obj)
        {
            MidiPlaybackSetButtonsDisabled(true);

            if (_owner.Instrument is not {} instrument)
                return;

            _owner.Instruments.CloseMidi(_owner.Owner, false, instrument);
            _owner.CloseChannelsMenu();
        }

        private void MidiLoopButtonOnOnToggled(ButtonToggledEventArgs obj)
        {
            if (_owner.Instrument == null)
                return;

            _owner.Instrument.LoopMidi = obj.Pressed;
            _owner.Instruments.UpdateRenderer(_owner.Owner, _owner.Instrument);
        }

        private void PlaybackSliderSeek(Range _)
        {
            // Do not seek while still grabbing.
            if (PlaybackSlider.Grabbed || _owner.Instrument is not {} instrument)
                return;

            _owner.Instruments.SetPlayerTick(_owner.Owner, (int)Math.Ceiling(PlaybackSlider.Value), instrument);
        }

        private void PlaybackSliderKeyUp(GUIBoundKeyEventArgs args)
        {
            if (args.Function != EngineKeyFunctions.UIClick || _owner.Instrument is not {} instrument)
                return;

            _owner.Instruments.SetPlayerTick(_owner.Owner, (int)Math.Ceiling(PlaybackSlider.Value), instrument);
        }

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);

            if (_owner.Instrument == null)
                return;

            var hasMaster = _owner.Instrument.Master != null;
            BandButton.ToggleMode = hasMaster;
            BandButton.Pressed = hasMaster;
            BandButton.Disabled = _owner.Instrument.IsMidiOpen || _owner.Instrument.IsInputOpen;
            ChannelsButton.Disabled = !_owner.Instrument.IsRendererAlive;

            if (!_owner.Instrument.IsMidiOpen)
            {
                PlaybackSlider.MaxValue = 1;
                PlaybackSlider.SetValueWithoutEvent(0);
                return;
            }

            if (PlaybackSlider.Grabbed)
                return;

            PlaybackSlider.MaxValue = _owner.Instrument.PlayerTotalTick;
            PlaybackSlider.SetValueWithoutEvent(_owner.Instrument.PlayerTick);
        }
    }
}
