#nullable enable
using System.Text;
using System.Linq;
using Content.Client.Administration.Managers;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.Administration.UI.Tabs.AdminTab;
using Content.Shared.Administration;
using Robust.Client.AutoGenerated;
using Robust.Client.Console;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Network;
using Robust.Shared.IoC;

namespace Content.Client.Administration.UI
{
    /// <summary>
    /// This window connects to a BwoinkSystem channel. BwoinkSystem manages the rest.
    /// </summary>
    [GenerateTypedNameReferences]
    public partial class BwoinkWindow : DefaultWindow
    {
        [Dependency] private readonly IClientAdminManager _adminManager = default!;
        [Dependency] private readonly IClientConsoleHost _console = default!;

        private readonly BwoinkSystem _bwoinkSystem;
        private PlayerInfo? _currentPlayer = default;

        public BwoinkWindow(BwoinkSystem bs)
        {
            RobustXamlLoader.Load(this);
            IoCManager.InjectDependencies(this);
            _bwoinkSystem = bs;

            _adminManager.AdminStatusUpdated += () => FixButtons();
            FixButtons();

            ChannelSelector.OnSelectionChanged += sel =>
            {
                _currentPlayer = sel;
                if (sel is not null)
                {
                    SwitchToChannel(sel.SessionId);
                    Title = $"{sel.CharacterName} / {sel.Username}";
                }

                foreach (var li in ChannelSelector.PlayerItemList)
                    li.Text = FormatTabTitle(li);
            };

            ChannelSelector.DecoratePlayer += (PlayerInfo pl, ItemList.Item li) =>
            {
                li.Text = FormatTabTitle(li, pl);
            };

            ChannelSelector.Comparison = (a, b) =>
            {
                var aChannelExists = _bwoinkSystem.TryGetChannel(a.SessionId, out var ach);
                var bChannelExists = _bwoinkSystem.TryGetChannel(b.SessionId, out var bch);
                if (!aChannelExists && !bChannelExists)
                    return 0;

                if (!aChannelExists)
                    return 1;

                if (!bChannelExists)
                    return -1;

                return bch!.LastMessage.CompareTo(ach!.LastMessage);
            };

            // ew
            Ban.OnPressed += _ =>
            {
                var bw = new BanWindow();
                bw.OnPlayerSelectionChanged(_currentPlayer);
                bw.Open();
            };

            Kick.OnPressed += _ =>
            {
                // TODO: Reason field
                if (_currentPlayer is not null)
                    _console.ExecuteCommand($"kick \"{_currentPlayer.Username}\"");
            };

            Teleport.OnPressed += _ =>
            {
                if (_currentPlayer is not null)
                    _console.ExecuteCommand($"tpto \"{_currentPlayer.Username}\"");
            };

            Respawn.OnPressed += _ =>
            {
                if (_currentPlayer is not null)
                    _console.ExecuteCommand($"respawn \"{_currentPlayer.Username}\"");
            };
        }

        public void OnBwoink(NetUserId channel)
        {
            var open = IsOpen;
            Open();

            ChannelSelector.RefreshDecorators();
            ChannelSelector.Sort();

            if (!open)
            {
                SelectChannel(channel);
            }
        }

        public void SelectChannel(NetUserId channel)
        {
            var pi = ChannelSelector
                .PlayerItemList
                .FirstOrDefault(i => ((PlayerInfo) i.Metadata!).SessionId == channel);

            if (pi is not null)
                pi.Selected = true;
        }

        private void FixButtons()
        {
            Ban.Visible = _adminManager.HasFlag(AdminFlags.Ban);
            Ban.Disabled = !Ban.Visible;

            Kick.Visible = _adminManager.CanCommand("kick");
            Kick.Disabled = !Kick.Visible;

            Teleport.Visible = _adminManager.CanCommand("tpto");
            Teleport.Disabled = !Teleport.Visible;

            Respawn.Visible = _adminManager.CanCommand("respawn");
            Respawn.Disabled = !Respawn.Visible;
        }

        private string FormatTabTitle(ItemList.Item li, PlayerInfo? pl = default)
        {
            pl ??= (PlayerInfo) li.Metadata!;
            var sb = new StringBuilder();
            sb.Append(pl.Connected ? '●' : '○');
            sb.Append(' ');
            if (_bwoinkSystem.TryGetChannel(pl.SessionId, out var panel) && panel.Unread > 0)
            {
                if (panel.Unread < 11)
                    sb.Append(new Rune('➀' + (panel.Unread-1)));
                else
                    sb.Append(new Rune(0x2639)); // ☹
                sb.Append(' ');
            }

            if (pl.Antag)
                sb.Append(new Rune(0x1F5E1)); // 🗡

            sb.AppendFormat("\"{0}\"", pl.CharacterName)
                .Append(' ')
                .Append(pl.Username);

            return sb.ToString();
        }

        private void SwitchToChannel(NetUserId ch)
        {
            foreach (var bw in BwoinkArea.Children)
                bw.Visible = false;
            var panel = _bwoinkSystem.EnsurePanel(ch);
            panel.Visible = true;
        }
    }
}
