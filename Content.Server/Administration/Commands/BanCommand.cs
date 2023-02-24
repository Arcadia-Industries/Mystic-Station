using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Content.Server.Administration.Notes;
using Content.Server.Database;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Console;


namespace Content.Server.Administration.Commands
{
    [AdminCommand(AdminFlags.Ban)]
    public sealed class BanCommand : IConsoleCommand
    {
        public string Command => "ban";
        public string Description => Loc.GetString("cmd-ban-desc");
        public string Help => Loc.GetString("cmd-ban-help", ("Command", Command));

        public async void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            var player = shell.Player as IPlayerSession;
            var plyMgr = IoCManager.Resolve<IPlayerManager>();
            var locator = IoCManager.Resolve<IPlayerLocator>();
            var dbMan = IoCManager.Resolve<IServerDbManager>();
            var adminNotesManager = IoCManager.Resolve<IAdminNotesManager>();

            string target;
            string reason;
            uint minutes;
            var severity = NoteSeverity.High;

            switch (args.Length)
            {
                case 2:
                    target = args[0];
                    reason = args[1];
                    minutes = 0;
                    break;
                case 3:
                    target = args[0];
                    reason = args[1];

                    if (!uint.TryParse(args[2], out minutes))
                    {
                        shell.WriteLine(Loc.GetString("cmd-ban-invalid-minutes", ("minutes", args[2])));
                        shell.WriteLine(Help);
                        return;
                    }

                    break;
                case 4:
                    target = args[0];
                    reason = args[1];

                    if (!uint.TryParse(args[2], out minutes))
                    {
                        shell.WriteLine(Loc.GetString("cmd-ban-invalid-minutes", ("minutes", args[2])));
                        shell.WriteLine(Help);
                        return;
                    }

                    if (!Enum.TryParse(args[3], ignoreCase: true, out severity))
                    {
                        shell.WriteLine(Loc.GetString("cmd-ban-invalid-severity", ("severity", args[3])));
                        shell.WriteLine(Help);
                        return;
                    }

                    break;
                default:
                    shell.WriteLine(Loc.GetString("cmd-ban-invalid-arguments"));
                    shell.WriteLine(Help);
                    return;
            }

            var located = await locator.LookupIdByNameOrIdAsync(target);
            if (located == null)
            {
                shell.WriteError(Loc.GetString("cmd-ban-player"));
                return;
            }

            var targetUid = located.UserId;
            var targetHWid = located.LastHWId;
            var targetAddr = located.LastAddress;

            if (player != null && player.UserId == targetUid)
            {
                shell.WriteLine(Loc.GetString("cmd-ban-self"));
                return;
            }

            DateTimeOffset? expires = null;
            if (minutes > 0)
            {
                expires = DateTimeOffset.Now + TimeSpan.FromMinutes(minutes);
            }

            (IPAddress, int)? addrRange = null;
            if (targetAddr != null)
            {
                if (targetAddr.IsIPv4MappedToIPv6)
                    targetAddr = targetAddr.MapToIPv4();

                // Ban /64 for IPv4, /32 for IPv4.
                var cidr = targetAddr.AddressFamily == AddressFamily.InterNetworkV6 ? 64 : 32;
                addrRange = (targetAddr, cidr);
            }

            var banDef = new ServerBanDef(
                null,
                targetUid,
                addrRange,
                targetHWid,
                DateTimeOffset.Now,
                expires,
                reason,
                player?.UserId,
                null);

            await dbMan.AddServerBanAsync(banDef);

            var response = new StringBuilder($"Banned {target} with reason \"{reason}\"");

            response.Append(expires == null
                ? " permanently."
                : $" until {expires}");

            shell.WriteLine(response.ToString());

            if (plyMgr.TryGetSessionById(targetUid, out var targetPlayer))
            {
                targetPlayer.ConnectedClient.Disconnect(banDef.DisconnectMessage);
            }

            // Without this the note foreign key constraint fails. You can ban player who have never before connected,
            // but you can't note them. Neat!
            if (located.LastAddress == null)
                return;

            var banMessage = new StringBuilder("Banned from the server ");
            if (minutes == 0)
            {
                banMessage.Append("permanently");
            }
            else
            {
                banMessage.Append("for ");
                var banLength = TimeSpan.FromMinutes(minutes);
                if (banLength.Days > 0)
                    banMessage.Append($"{banLength.TotalDays} days");
                else if (banLength.Hours > 0)
                    banMessage.Append($"{banLength.TotalHours} hours");
                else
                    banMessage.Append($"{minutes} minutes");
            }

            banMessage.Append(" - ");
            banMessage.Append(reason);

            if (player is null)
            {
                Logger.ErrorS("admin.notes", "While creating a server ban, player was null. A note could not be added.");
                return;
            }
            await adminNotesManager.AddNote(player, targetUid, NoteType.Note, banMessage.ToString(), severity, false, null);
        }

        public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        {
            if (args.Length == 1)
            {
                var playerMgr = IoCManager.Resolve<IPlayerManager>();
                var options = playerMgr.ServerSessions.Select(c => c.Name).OrderBy(c => c).ToArray();
                return CompletionResult.FromHintOptions(options, Loc.GetString("cmd-ban-hint"));
            }

            if (args.Length == 2)
                return CompletionResult.FromHint(Loc.GetString("cmd-ban-hint-reason"));

            if (args.Length == 3)
            {
                var durations = new CompletionOption[]
                {
                    new("0", Loc.GetString("cmd-ban-hint-duration-1")),
                    new("1440", Loc.GetString("cmd-ban-hint-duration-2")),
                    new("4320", Loc.GetString("cmd-ban-hint-duration-3")),
                    new("10080", Loc.GetString("cmd-ban-hint-duration-4")),
                    new("20160", Loc.GetString("cmd-ban-hint-duration-5")),
                    new("43800", Loc.GetString("cmd-ban-hint-duration-6")),
                };

                return CompletionResult.FromHintOptions(durations, Loc.GetString("cmd-ban-hint-duration"));
            }

            if (args.Length == 4)
            {
                var severities = new CompletionOption[]
                {
                    new("none", Loc.GetString("admin-note-editor-severity-none")),
                    new("minor", Loc.GetString("admin-note-editor-severity-low")),
                    new("medium", Loc.GetString("admin-note-editor-severity-medium")),
                    new("high", Loc.GetString("admin-note-editor-severity-high")),
                };

                return CompletionResult.FromHintOptions(severities, Loc.GetString("cmd-ban-hint-severity"));
            }

            return CompletionResult.Empty;
        }
    }
}
