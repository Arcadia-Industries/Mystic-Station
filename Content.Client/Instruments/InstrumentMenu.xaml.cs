using System;
using System.IO;
using System.Threading.Tasks;
using Content.Client.GameObjects.Components.Instruments;
using Content.Client.UserInterface.Stylesheets;
using Content.Client.Utility;
using Robust.Client.Audio.Midi;
using Robust.Client.AutoGenerated;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Containers;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Range = Robust.Client.UserInterface.Controls.Range;

namespace Content.Client.Instruments
{
    [GenerateTypedNameReferences]
    public partial class InstrumentMenu : SS14Window
    {
        [Dependency] private readonly IMidiManager _midiManager = default!;
        [Dependency] private readonly IFileDialogManager _fileDialogManager = default!;

        private readonly InstrumentBoundUserInterface _owner;

        protected override Vector2? CustomSize => (400, 150);

        public InstrumentMenu(InstrumentBoundUserInterface owner)
        {
            RobustXamlLoader.Load(this);
            IoCManager.InjectDependencies(this);

            _owner = owner;

            _owner.Instrument.OnMidiPlaybackEnded += InstrumentOnMidiPlaybackEnded;

            Title = _owner.Instrument.Owner.Name;

            LoopButton.Disabled = !_owner.Instrument.IsMidiOpen;
            LoopButton.Pressed = _owner.Instrument.LoopMidi;
            StopButton.Disabled = !_owner.Instrument.IsMidiOpen;
            PlaybackSlider.MouseFilter = _owner.Instrument.IsMidiOpen ? MouseFilterMode.Pass : MouseFilterMode.Ignore;

            if (!_midiManager.IsAvailable)
            {
                Margin.AddChild(new PanelContainer
                {
                    MouseFilter = MouseFilterMode.Stop,
                    PanelOverride = new StyleBoxFlat {BackgroundColor = Color.Black.WithAlpha(0.90f)},
                    Children =
                    {
                        new Label
                        {
                            Align = Label.AlignMode.Center,
                            SizeFlagsVertical = SizeFlags.ShrinkCenter,
                            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                            StyleClasses = {StyleNano.StyleClassLabelBig},
                            Text = Loc.GetString("MIDI support is currently\nnot available on your platform.")
                        }
                    }
                });

                // We return early as to not give the buttons behavior.
                return;
            }

            InputButton.OnToggled += MidiInputButtonOnOnToggled;
            FileButton.OnPressed += MidiFileButtonOnOnPressed;
            LoopButton.OnToggled += MidiLoopButtonOnOnToggled;
            StopButton.OnPressed += MidiStopButtonOnPressed;
            PlaybackSlider.OnValueChanged += PlaybackSliderSeek;
            PlaybackSlider.OnKeyBindUp += PlaybackSliderKeyUp;
        }

        private void InstrumentOnMidiPlaybackEnded()
        {
            MidiPlaybackSetButtonsDisabled(true);
        }

        public void MidiPlaybackSetButtonsDisabled(bool disabled)
        {
            LoopButton.Disabled = disabled;
            StopButton.Disabled = disabled;

            // Whether to allow the slider to receive events..
            PlaybackSlider.MouseFilter = !disabled ? MouseFilterMode.Pass : MouseFilterMode.Ignore;
        }

        private async void MidiFileButtonOnOnPressed(BaseButton.ButtonEventArgs obj)
        {
            var filters = new FileDialogFilters(new FileDialogFilters.Group("mid", "midi"));
            await using var file = await _fileDialogManager.OpenFile(filters);

            // The following checks are only in place to prevent players from playing MIDI songs locally.
            // There are equivalents for these checks on the server.

            if (file == null) return;

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

            if (!_owner.Instrument.OpenMidi(memStream.GetBuffer().AsSpan(0, (int) memStream.Length)))
                return;

            MidiPlaybackSetButtonsDisabled(false);
            if (InputButton.Pressed)
                InputButton.Pressed = false;
        }

        private void MidiInputButtonOnOnToggled(BaseButton.ButtonToggledEventArgs obj)
        {
            if (obj.Pressed)
            {
                if (!PlayCheck())
                    return;

                MidiStopButtonOnPressed(null);
                _owner.Instrument.OpenInput();
            }
            else
                _owner.Instrument.CloseInput();
        }

        private bool PlayCheck()
        {
            var instrumentEnt = _owner.Instrument.Owner;
            var instrument = _owner.Instrument;

            _owner.Instrument.Owner.TryGetContainerMan(out var conMan);

            var localPlayer = IoCManager.Resolve<IPlayerManager>().LocalPlayer;

            // If we don't have a player or controlled entity, we return.
            if (localPlayer?.ControlledEntity == null) return false;

            // If the instrument is handheld and we're not holding it, we return.
            if ((instrument.Handheld && (conMan == null
                                         || conMan.Owner != localPlayer.ControlledEntity))) return false;

            // We check that we're in range unobstructed just in case.
            return localPlayer.InRangeUnobstructed(instrumentEnt,
                predicate: (e) => e == instrumentEnt || e == localPlayer.ControlledEntity);
        }

        private void MidiStopButtonOnPressed(BaseButton.ButtonEventArgs obj)
        {
            MidiPlaybackSetButtonsDisabled(true);
            _owner.Instrument.CloseMidi();
        }

        private void MidiLoopButtonOnOnToggled(BaseButton.ButtonToggledEventArgs obj)
        {
            _owner.Instrument.LoopMidi = obj.Pressed;
        }

        private void PlaybackSliderSeek(Range _)
        {
            // Do not seek while still grabbing.
            if (PlaybackSlider.Grabbed) return;

            _owner.Instrument.PlayerTick = (int)Math.Ceiling(PlaybackSlider.Value);
        }

        private void PlaybackSliderKeyUp(GUIBoundKeyEventArgs args)
        {
            if (args.Function != EngineKeyFunctions.UIClick) return;
            _owner.Instrument.PlayerTick = (int)Math.Ceiling(PlaybackSlider.Value);
        }

        protected override void Update(FrameEventArgs args)
        {
            base.Update(args);

            if (!_owner.Instrument.IsMidiOpen)
            {
                PlaybackSlider.MaxValue = 1;
                PlaybackSlider.SetValueWithoutEvent(0);
                return;
            }

            if (PlaybackSlider.Grabbed) return;

            PlaybackSlider.MaxValue = _owner.Instrument.PlayerTotalTick;
            PlaybackSlider.SetValueWithoutEvent(_owner.Instrument.PlayerTick);
        }
    }
}
