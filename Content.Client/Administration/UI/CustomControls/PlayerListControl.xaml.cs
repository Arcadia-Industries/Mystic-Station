﻿using System.Linq;
using Content.Client.UserInterface.Controls;
using Content.Client.Verbs;
using Content.Shared.Administration;
using Content.Shared.Input;
using Robust.Client.AutoGenerated;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Input;

namespace Content.Client.Administration.UI.CustomControls
{
    [GenerateTypedNameReferences]
    public sealed partial class PlayerListControl : BoxContainer
    {
        private readonly AdminSystem _adminSystem;
        private readonly VerbSystem _verbSystem;
        private List<PlayerInfo> _sortedPlayerList = new();

        public event Action<PlayerInfo?>? OnSelectionChanged;

        public Func<PlayerInfo, string, string>? OverrideText;
        public Comparison<PlayerInfo>? Comparison;

        public PlayerListControl()
        {
            _adminSystem = EntitySystem.Get<AdminSystem>();
            _verbSystem = EntitySystem.Get<VerbSystem>();
            IoCManager.InjectDependencies(this);
            RobustXamlLoader.Load(this);
            // Fill the Option data
            PlayerListContainer.ItemPressed += PlayerListItemPressed;
            PlayerListContainer.GenerateItem += GenerateButton;
            PopulateList(_adminSystem.PlayerList);
            FilterLineEdit.OnTextChanged += _ => PopulateList();
            _adminSystem.PlayerListChanged += PopulateList;
            BackgroundPanel.PanelOverride = new StyleBoxFlat {BackgroundColor = new Color(32, 32, 40)};
        }

        private void PlayerListItemPressed(BaseButton.ButtonEventArgs args, IControlData data)
        {
            if (data is not PlayerListData {Info: var selectedPlayer})
                return;
            if (args.Event.Function == EngineKeyFunctions.UIClick)
            {
                OnSelectionChanged?.Invoke(selectedPlayer);
            }
            else if (args.Event.Function == ContentKeyFunctions.OpenContextMenu)
            {
                _verbSystem.VerbMenu.OpenVerbMenu(selectedPlayer.EntityUid);
            }
        }

        public void Sort()
        {
            if (Comparison != null)
                _sortedPlayerList.Sort((a, b) => Comparison(a, b));
        }

        public void PopulateList(IReadOnlyList<PlayerInfo>? players = null)
        {
            players ??= _adminSystem.PlayerList;
            _sortedPlayerList.Clear();
            foreach (var info in players)
            {
                var displayName = $"{info.CharacterName} ({info.Username})";
                if (!string.IsNullOrEmpty(FilterLineEdit.Text) &&
                    !displayName.ToLowerInvariant().Contains(FilterLineEdit.Text.Trim().ToLowerInvariant()))
                {
                    continue;
                }

                _sortedPlayerList.Add(info);
            }
            Sort();
            PlayerListContainer.PopulateList(players.Select(info => new PlayerListData(info)).ToList());
        }

        private void GenerateButton(IControlData data, ListContainerButton button)
        {
            if (data is not PlayerListData {Info: var info})
                return;
            var text = $"{info.CharacterName} ({info.Username})";
            if (OverrideText != null)
                text = OverrideText?.Invoke(info, text);

            button.AddChild(new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                Children =
                {
                    new Label
                    {
                        ClipText = true,
                        Text = text
                    }
                }
            });
            button.EnableAllKeybinds = true;
            button.AddStyleClass(ListContainer.StyleClassListContainerButton);
        }
    }

    public sealed class PlayerListData : IControlData
    {
        public PlayerInfo Info;

        public PlayerListData(PlayerInfo info)
        {
            Info = info;
        }
    }
}
