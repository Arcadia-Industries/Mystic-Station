using Content.Server.UserInterface;
using Content.Server.Popups;
using Content.Shared.Interaction;
using Content.Shared.NukeOps;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Content.Server.GameTicking.Rules;
using Content.Shared.Database;
using Content.Server.Administration.Logs;

namespace Content.Server.NukeOps
{
    /// <summary>
    /// War declarator that used in NukeOps game rule for declaring war
    /// </summary>
    [UsedImplicitly]
    public sealed class WarDeclaratorSystem : EntitySystem
    {
        [Dependency] private readonly UserInterfaceSystem _userInterfaceSystem = default!;
        [Dependency] private readonly PopupSystem _popupSystem = default!;
        [Dependency] private readonly NukeopsRuleSystem _nukeopsRuleSystem = default!;
        [Dependency] private readonly IAdminLogManager _adminLogger = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<WarDeclaratorComponent, ActivateInWorldEvent>(OnActivate);
            // Bound UI subscriptions
            SubscribeLocalEvent<WarDeclaratorComponent, ComponentInit>(OnComponentInit);
            SubscribeLocalEvent<WarDeclaratorComponent, WarDeclaratorChangedMessage>(OnMessageChanged);
            SubscribeLocalEvent<WarDeclaratorComponent, WarDeclaratorPressedWarButton>(OnWarButtonPressed);
        }

        private void OnComponentInit(EntityUid uid, WarDeclaratorComponent comp, ComponentInit args)
        {
            comp.Message = comp.DefaultMessage;
            DirtyUI(uid, comp);
        }

        private void OnActivate(EntityUid uid, WarDeclaratorComponent comp, ActivateInWorldEvent args)
        {
            if (!EntityManager.TryGetComponent(args.User, out ActorComponent? actor))
                return;

            comp.Owner.GetUIOrNull(WarDeclaratorUiKey.Key)?.Open(actor.PlayerSession);
            args.Handled = true;
        }

        private void OnMessageChanged(EntityUid uid, WarDeclaratorComponent comp, WarDeclaratorChangedMessage args)
        {
            if (args.Session.AttachedEntity is not {Valid: true} player)
                return;

            comp.Message = args.Message.Trim().Substring(0, Math.Min(comp.MaxMessageLength, args.Message.Length));
            DirtyUI(uid, comp);
        }

        private void OnWarButtonPressed(EntityUid uid, WarDeclaratorComponent comp, WarDeclaratorPressedWarButton args)
        {
            if (args.Session.AttachedEntity is not {Valid: true} player)
                return;

            if (_nukeopsRuleSystem.GetWarCondition() != WarConditionStatus.YES_WAR)
            {
                DirtyUI(uid, comp);
                return;
            }

            comp.Message = String.Empty;

            var msg = args.Message?.Trim().Substring(0, Math.Min(comp.MaxMessageLength, args.Message.Length)) ?? comp.Message;
            var title = Loc.GetString(comp.DeclarementDisplayName);

            _nukeopsRuleSystem.DeclareWar(msg, title, comp.DeclarementSound, comp.DeclarementColor);

            if (args.Session.AttachedEntity != null)
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"{ToPrettyString(args.Session.AttachedEntity.Value):player} has declared war with this text: {msg}");
        }

        public void RefreshAllDeclaratorsUI()
        {
            foreach (var comp in EntityQuery<WarDeclaratorComponent>())
            {
                DirtyUI(comp.Owner, comp);
            }
        }

        private void DirtyUI(EntityUid uid,
            WarDeclaratorComponent? warDeclarator = null)
        {
            if (!Resolve(uid, ref warDeclarator))
                return;

            _userInterfaceSystem.TrySetUiState(uid, WarDeclaratorUiKey.Key,
                new WarDeclaratorBoundUserInterfaceState
                (
                    warDeclarator.Message,
                    _nukeopsRuleSystem.GetWarCondition(),
                    _nukeopsRuleSystem.NukeopsRuleConfig.WarMinCrewSize,
                    _nukeopsRuleSystem.NukeopsRuleConfig.WarTimeLimit,
                    _nukeopsRuleSystem.GameruleStartTime
                )
            );
        }
    }
}
