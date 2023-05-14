﻿using Content.Shared.PDA;
using Robust.Shared.Utility;
using Content.Shared.CartridgeLoader;
using Content.Client.Message;
using Robust.Client.UserInterface;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.XAML;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.PDA
{
    [GenerateTypedNameReferences]
    public sealed partial class PDAMenu : PDAWindow
    {
        public const int HomeView = 0;
        public const int ProgramListView = 1;
        public const int SettingsView = 2;
        public const int ProgramContentView = 3;

        private int _currentView = 0;

        public event Action<EntityUid>? OnProgramItemPressed;
        public event Action<EntityUid>? OnUninstallButtonPressed;
        public event Action<EntityUid>? OnInstallButtonPressed;
        public PDAMenu()
        {
            RobustXamlLoader.Load(this);

            ViewContainer.OnChildAdded += control => control.Visible = false;

            HomeButton.IconTexture = new SpriteSpecifier.Texture(new ("/Textures/Interface/home.png"));
            FlashLightToggleButton.IconTexture = new SpriteSpecifier.Texture(new ("/Textures/Interface/light.png"));
            EjectPenButton.IconTexture = new SpriteSpecifier.Texture(new ("/Textures/Interface/pencil.png"));
            EjectIdButton.IconTexture = new SpriteSpecifier.Texture(new ("/Textures/Interface/eject.png"));
            ProgramCloseButton.IconTexture = new SpriteSpecifier.Texture(new ("/Textures/Interface/Nano/cross.svg.png"));


            HomeButton.OnPressed += _ => ToHomeScreen();

            ProgramListButton.OnPressed += _ =>
            {
                HomeButton.IsCurrent = false;
                ProgramListButton.IsCurrent = true;
                SettingsButton.IsCurrent = false;
                ProgramTitle.IsCurrent = false;

                ChangeView(ProgramListView);
            };


            SettingsButton.OnPressed += _ =>
            {
                HomeButton.IsCurrent = false;
                ProgramListButton.IsCurrent = false;
                SettingsButton.IsCurrent = true;
                ProgramTitle.IsCurrent = false;

                ChangeView(SettingsView);
            };

            ProgramTitle.OnPressed += _ =>
            {
                HomeButton.IsCurrent = false;
                ProgramListButton.IsCurrent = false;
                SettingsButton.IsCurrent = false;
                ProgramTitle.IsCurrent = true;

                ChangeView(ProgramContentView);
            };

            ProgramCloseButton.OnPressed += _ =>
            {
                HideProgramHeader();
                ToHomeScreen();
            };


            HideAllViews();
            ToHomeScreen();
        }

        public void UpdateState(PDAUpdateState state)
        {
            FlashLightToggleButton.IsActive = state.FlashlightEnabled;

            if (state.PdaOwnerInfo.ActualOwnerName != null)
            {
                PdaOwnerLabel.SetMarkup(Loc.GetString("comp-pda-ui-owner",
                    ("ActualOwnerName", state.PdaOwnerInfo.ActualOwnerName)));
            }


            if (state.PdaOwnerInfo.IdOwner != null || state.PdaOwnerInfo.JobTitle != null)
            {
                IdInfoLabel.SetMarkup(Loc.GetString("comp-pda-ui",
                    ("Owner",state.PdaOwnerInfo.IdOwner ?? Loc.GetString("comp-pda-ui-unknown")),
                    ("JobTitle",state.PdaOwnerInfo.JobTitle ?? Loc.GetString("comp-pda-ui-unassigned"))));
            }
            else
            {
                IdInfoLabel.SetMarkup(Loc.GetString("comp-pda-ui-blank"));
            }

            StationNameLabel.SetMarkup(Loc.GetString("comp-pda-ui-station",
                ("Station",state.StationName ?? Loc.GetString("comp-pda-ui-unknown"))));
            if (state.StationTime is { Hours: { }, Minutes: { } })
            {
                StationTimeLabel.SetMarkup(Loc.GetString("comp-pda-ui-station-time",
                    ("hours", state.StationTime.Hours), ("minutes", state.StationTime.Minutes)));
            }
            else
            {
                StationTimeLabel.SetMarkup(Loc.GetString("comp-pda-ui-station-time-unknown"));
            }

            if (state.StationAlert.Level != null)
            {
                StationAlertLevelInstructions.SetMarkup(Loc.GetString("comp-pda-ui-station-alert-level-instructions",
                        ("AlertLevelInstructions", Loc.GetString($"alert-level-{state.StationAlert.Level}-instructions"))));
                StationAlertLevelLabel.SetMarkup(Loc.GetString("comp-pda-ui-station-alert-level",
                    ("ColorLevel", state.StationAlert.Color),
                    ("AlertLevel", Loc.GetString($"alert-level-{state.StationAlert.Level}"))));
            }
            else
            {
                StationAlertLevelLabel.SetMarkup(Loc.GetString("comp-pda-ui-station-alert-level",
                    ("ColorLevel", "white"), ("AlertLevel", Loc.GetString("comp-pda-ui-unknown"))));
                StationAlertLevelInstructions.SetMarkup(
                    Loc.GetString("comp-pda-ui-station-alert-level-instructions",
                    ("AlertLevelInstructions", Loc.GetString("comp-pda-ui-unknown"))));
            }

            var accessLevels = "";
            for (var i = 0; i < state.AccessLevels.Count; i++)
            {
                var access = state.AccessLevels[i];
                accessLevels += Loc.GetString($"{access}-short");
                if (i < state.AccessLevels.Count - 1)
                {
                    accessLevels += ", ";
                }
                else
                {
                    accessLevels += ".";
                }
            }

            IdAccessLevels.SetMarkup(Loc.GetString("comp-pda-ui-station-acceses-levels",
                ("AccessLevels", accessLevels)));

            AddressLabel.Text = state.Address?.ToUpper() ?? " - ";

            EjectIdButton.IsActive = state.PdaOwnerInfo.IdOwner != null || state.PdaOwnerInfo.JobTitle != null;
            EjectPenButton.IsActive = state.HasPen;
            ActivateMusicButton.Visible = state.CanPlayMusic;
            ShowUplinkButton.Visible = state.HasUplink;
            LockUplinkButton.Visible = state.HasUplink;
        }

        public void UpdateAvailablePrograms(List<(EntityUid, CartridgeComponent)> programs)
        {
            ProgramList.RemoveAllChildren();

            if (programs.Count == 0)
            {
                ProgramList.AddChild(new Label()
                {
                    Text = Loc.GetString("comp-pda-io-no-programs-available"),
                    HorizontalAlignment = HAlignment.Center,
                    VerticalAlignment = VAlignment.Center,
                    VerticalExpand = true
                });

                return;
            }

            var row = CreateProgramListRow();
            var itemCount = 1;
            ProgramList.AddChild(row);

            foreach (var (uid, component) in programs)
            {
                //Create a new row every second program item starting from the first
                if (itemCount % 2 != 0)
                {
                    row = CreateProgramListRow();
                    ProgramList.AddChild(row);
                }

                var item = new PDAProgramItem();

                if (component.Icon is not null)
                    item.Icon.SetFromSpriteSpecifier(component.Icon);

                item.OnPressed += _ => OnProgramItemPressed?.Invoke(uid);

                switch (component.InstallationStatus)
                {
                    case InstallationStatus.Cartridge:
                        item.InstallButton.Visible = true;
                        item.InstallButton.Text = Loc.GetString("cartridge-bound-user-interface-install-button");
                        item.InstallButton.OnPressed += _ => OnInstallButtonPressed?.Invoke(uid);
                        break;
                    case InstallationStatus.Installed:
                        item.InstallButton.Visible = true;
                        item.InstallButton.Text = Loc.GetString("cartridge-bound-user-interface-uninstall-button");
                        item.InstallButton.OnPressed += _ => OnUninstallButtonPressed?.Invoke(uid);
                        break;
                }

                item.ProgramName.Text = Loc.GetString(component.ProgramName);
                item.SetHeight = 20;
                row.AddChild(item);

                itemCount++;
            }

            //Add a filler item to the last row when it only contains one item
            if (itemCount % 2 == 0)
                row.AddChild(new Control() { HorizontalExpand = true });
        }

        /// <summary>
        /// Changes the current view to the home screen (view 0) and sets the tabs `IsCurrent` flag accordingly
        /// </summary>
        public void ToHomeScreen()
        {
            HomeButton.IsCurrent = true;
            ProgramListButton.IsCurrent = false;
            SettingsButton.IsCurrent = false;
            ProgramTitle.IsCurrent = false;

            ChangeView(HomeView);
        }

        /// <summary>
        /// Hides the program title and close button.
        /// </summary>
        public void HideProgramHeader()
        {
            ProgramTitle.IsCurrent = false;
            ProgramTitle.Visible = false;
            ProgramCloseButton.Visible = false;
            ProgramListButton.Visible = true;
            SettingsButton.Visible = true;
        }

        /// <summary>
        /// Changes the current view to the program content view (view 3), sets the program title and sets the tabs `IsCurrent` flag accordingly
        /// </summary>
        public void ToProgramView(string title)
        {
            HomeButton.IsCurrent = false;
            ProgramListButton.IsCurrent = false;
            SettingsButton.IsCurrent = false;
            ProgramTitle.IsCurrent = false;
            ProgramTitle.IsCurrent = true;
            ProgramTitle.Visible = true;
            ProgramCloseButton.Visible = true;
            ProgramListButton.Visible = false;
            SettingsButton.Visible = false;

            ProgramTitle.LabelText = title;
            ChangeView(ProgramContentView);
        }


        /// <summary>
        /// Changes the current view to the given view number
        /// </summary>
        public void ChangeView(int view)
        {
            if (ViewContainer.ChildCount <= view)
                return;

            ViewContainer.GetChild(_currentView).Visible = false;
            ViewContainer.GetChild(view).Visible = true;
            _currentView = view;
        }

        private BoxContainer CreateProgramListRow()
        {
            return new BoxContainer()
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true
            };
        }

        private void HideAllViews()
        {
            var views = ViewContainer.Children;
            foreach (var view in views)
            {
                view.Visible = false;
            }
        }
    }
}
