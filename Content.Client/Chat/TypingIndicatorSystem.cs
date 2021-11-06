using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.Client.Chat.UI;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.MobState;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Content.Client.Chat
{
    public class TypingIndicatorSystem : SharedTypingIndicatorSystem
    {
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;

        private readonly Dictionary<EntityUid, TypingIndicatorGui> _guis = new();

        private IEntity? _attachedEntity;
        public bool Enabled => _cfg.GetCVar(CCVars.ChatTypingIndicatorSystemEnabled);

        /// <summary>
        /// Time since the chatbox input field had active input.
        /// </summary>
        public TimeSpan TimeSinceType { get; private set; }


        private bool _localTypingNow = false;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeNetworkEvent<ClientTypingMessage>(HandleRemoteTyping);
            SubscribeLocalEvent<PlayerAttachSysMessage>(HandlePlayerAttached);
        }

        public void HandleClientTyping()
        {
            if (!Enabled) return;
            _localTypingNow = true;

        }

        public void ResetTypingTime()
        {
            TimeSinceType = TimeSpan.Zero;
        }


        private void HandleRemoteTyping(ClientTypingMessage ev)
        {
            var entity = EntityManager.GetEntity(ev.EnityId.GetValueOrDefault());
            var comp = entity.EnsureComponent<TypingIndicatorComponent>();
            comp.Enabled = true;
        }

        private void HandlePlayerAttached(PlayerAttachSysMessage message)
        {
            _attachedEntity = message.AttachedEntity;
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);
            var pollRate = TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.ChatTypingIndicatorPollRate));
            if(_localTypingNow && _timing.RealTime.Subtract(TimeSinceType) >= pollRate)
            {
                var player = _playerManager.LocalPlayer;
                if (player == null) return;
                RaiseNetworkEvent(new ClientTypingMessage(player.UserId, player.ControlledEntity?.Uid));
                TimeSinceType = _timing.RealTime;
            }
            else
            {
                var player = _playerManager.LocalPlayer;
                if (player == null) return;
                RaiseNetworkEvent(new ClientStoppedTypingMessage(player.UserId, player.ControlledEntity?.Uid));
            }

        }

        public override void FrameUpdate(float frameTime)
        {
            base.FrameUpdate(frameTime);
            if (!Enabled) return;

            if (_attachedEntity == null || _attachedEntity.Deleted)
            {
                return;
            }

            var viewBox = _eyeManager.GetWorldViewport().Enlarged(2.0f);

            foreach (var (mobState, typingIndicatorComp) in EntityManager.EntityQuery<IMobStateComponent, TypingIndicatorComponent>())
            {
                if (!typingIndicatorComp.Enabled) return;

                var entity = mobState.Owner;
                
                if (_attachedEntity.Transform.MapID != entity.Transform.MapID ||
                    !viewBox.Contains(entity.Transform.WorldPosition))
                {
                    if (_guis.TryGetValue(entity.Uid, out var oldGui))
                    {
                        _guis.Remove(entity.Uid);
                        oldGui.Dispose();
                    }

                    continue;
                }

                if (_guis.ContainsKey(entity.Uid))
                {
                    continue;
                }

                var gui = new TypingIndicatorGui(entity);
                _guis.Add(entity.Uid, gui);
            }
        }

        [UsedImplicitly]
        public sealed class EnabledTypingIndicatorSystem : IConsoleCommand
        {
            public string Command => "enabledtypingindicator";
            public string Description => "Enables the typing indicator";
            public string Help => ""; //helpless

            public void Execute(IConsoleShell shell, string argStr, string[] args)
            {
                var cfg = IoCManager.Resolve<IConfigurationManager>();
                cfg.SetCVar(CCVars.ChatTypingIndicatorSystemEnabled, !cfg.GetCVar(CCVars.ChatTypingIndicatorSystemEnabled));
            }
        }
    }
}
