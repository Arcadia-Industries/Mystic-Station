using Content.Shared.Administration;
using Robust.Client.AutoGenerated;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using static Content.Client.Administration.UI.Tabs.PlayerTab.PlayerTabHeader;

namespace Content.Client.Administration.UI.Tabs.PlayerTab
{
    [GenerateTypedNameReferences]
    public sealed partial class PlayerTab : Control
    {
        private readonly Color _altColor = Color.FromHex("#292B38");
        private readonly Color _defaultColor = Color.FromHex("#2F2F3B");
        private readonly AdminSystem _adminSystem;
        private readonly List<PlayerTabEntry> _players = new();

        private Headers _headerClicked = Headers.Username;
        private bool _ascending = true;

        public event Action<BaseButton.ButtonEventArgs>? OnEntryPressed;

        public PlayerTab()
        {
            _adminSystem = EntitySystem.Get<AdminSystem>();
            RobustXamlLoader.Load(this);
            RefreshPlayerList(_adminSystem.PlayerList);
            _adminSystem.PlayerListChanged += RefreshPlayerList;
            OverlayButtonOn.OnPressed += _adminSystem.AdminOverlayOn;
            OverlayButtonOff.OnPressed += _adminSystem.AdminOverlayOff;

            ListHeader.BackgroundColorPanel.PanelOverride = new StyleBoxFlat(_altColor);
            ListHeader.OnHeaderClicked += HeaderClicked;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            _adminSystem.PlayerListChanged -= RefreshPlayerList;
            OverlayButtonOn.OnPressed -= _adminSystem.AdminOverlayOn;
            OverlayButtonOff.OnPressed -= _adminSystem.AdminOverlayOff;
        }

        private void RefreshPlayerList(IReadOnlyList<PlayerInfo> players)
        {
            foreach (var control in _players)
            {
                PlayerList.RemoveChild(control);
            }

            _players.Clear();

            var playerManager = IoCManager.Resolve<IPlayerManager>();
            PlayerCount.Text = $"Players: {playerManager.PlayerCount}";

            var sortedPlayers = new List<PlayerInfo>(players);
            sortedPlayers.Sort((x, y) =>
            {
                if (!_ascending)
                {
                    (x, y) = (y, x);
                }

                return _headerClicked switch
                {
                    Headers.Username => Compare(x.Username, y.Username),
                    Headers.Character => Compare(x.CharacterName, y.CharacterName),
                    Headers.Job => Compare(x.StartingJob, y.StartingJob),
                    Headers.Antagonist => x.Antag.CompareTo(y.Antag),
                    _ => 1
                };
            });

            var useAltColor = false;
            foreach (var player in sortedPlayers)
            {
                var entry = new PlayerTabEntry(player.Username,
                    player.CharacterName,
                    player.StartingJob,
                    player.Antag ? "YES" : "NO",
                    new StyleBoxFlat(useAltColor ? _altColor : _defaultColor),
                    player.Connected);
                entry.PlayerUid = player.EntityUid;
                entry.OnPressed += args => OnEntryPressed?.Invoke(args);
                PlayerList.AddChild(entry);
                _players.Add(entry);

                useAltColor ^= true;
            }
        }

        private int Compare(string x, string y)
        {
            return string.Compare(x, y, StringComparison.Ordinal);
        }

        private void HeaderClicked(Headers header)
        {
            if (_headerClicked == header)
            {
                _ascending = !_ascending;
            }
            else
            {
                _headerClicked = header;
                _ascending = true;
            }

            RefreshPlayerList(_adminSystem.PlayerList);
        }
    }
}
