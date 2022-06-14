﻿using Content.Client.Gameplay;
using Content.Client.MainMenu;
using Content.Client.Options.UI;
using Content.Client.UserInterface.Controls;
using Robust.Client.Console;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client.UserInterface.Systems.EscapeMenu;

public sealed class EscapeUIController : UIController, IOnStateEntered<GameplayState>, IOnStateEntered<MainScreen>
{
    [Dependency] private readonly IClientConsoleHost _console = default!;
    [Dependency] private readonly IInputManager _input = default!;

    private Options.UI.EscapeMenu? _window;

    private MenuButton? EscapeButton => UIManager.GetActiveUIWidgetOrNull<MenuBar.Widgets.MenuBar>()?.EscapeButton;

    public void OnStateEntered(GameplayState state)
    {
        if (EscapeButton != null)
        {
            EscapeButton.OnPressed += EscapeButtonPressed;
        }
    }

    public void OnStateEntered(MainScreen state)
    {
        _input.SetInputCommand(EngineKeyFunctions.EscapeMenu,
            InputCmdHandler.FromDelegate(_ => ToggleWindow()));
    }

    private void EscapeButtonPressed(ButtonEventArgs args)
    {
        ToggleWindow();
    }

    private void OpenWindow()
    {
        _window = new Options.UI.EscapeMenu();

        _window.AHelpButton.OnPressed += _ => {
            _console.ExecuteCommand("openahelp");
            CloseWindow();
        };

        _window.DisconnectButton.OnPressed += _ =>
        {
            _console.ExecuteCommand("disconnect");
            CloseWindow();
        };

        _window.OptionsButton.OnPressed += _ =>
        {
            new OptionsMenu().OpenCentered();
            CloseWindow();
        };

        _window.QuitButton.OnPressed += _ =>
        {
            _console.ExecuteCommand("quit");
            CloseWindow();
        };

        _window.OpenCentered();

        if (EscapeButton != null)
            EscapeButton.Pressed = true;
    }

    private void CloseWindow()
    {
        _window?.Close();

        if (EscapeButton != null)
            EscapeButton.Pressed = false;
    }

    private void ToggleWindow()
    {
        if (_window?.IsOpen != true)
            OpenWindow();
        else
            CloseWindow();
    }
}
