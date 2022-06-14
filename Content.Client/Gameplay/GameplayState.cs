using Content.Client.Alerts.UI;
using Content.Client.Chat;
using Content.Client.Chat.Managers;
using Content.Client.Chat.UI;
using Content.Client.Construction.UI;
using Content.Client.Hands;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Screens;
using Content.Client.Viewport;
using Content.Client.Voting;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client.Gameplay
{
    public sealed class GameplayState : GamePlayStateBase, IMainViewportState
    {
        [Dependency] private readonly IInputManager _inputManager = default!;
        [Dependency] private readonly IChatManager _chatManager = default!;
        [Dependency] private readonly IVoteManager _voteManager = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] private readonly IOverlayManager _overlayManager = default!;
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly IConfigurationManager _configurationManager = default!;

        protected override Type? LinkedScreenType => typeof(DefaultGameScreen);
        public static readonly Vector2i ViewportSize = (EyeManager.PixelsPerMeter * 21, EyeManager.PixelsPerMeter * 15);
        private ConstructionMenuPresenter? _constructionMenu;
        private AlertsFramePresenter? _alertsFramePresenter;
        private FpsCounter _fpsCounter = default!;

        public MainViewport Viewport { get; private set; } = default!;

        public GameplayState()
        {
            IoCManager.InjectDependencies(this);
        }

        protected override void Startup()
        {
            base.Startup();
            _gameChat = new HudChatBox {PreferredChannel = ChatSelectChannel.Local};
            UserInterfaceManager.StateRoot.AddChild(_gameChat);
            LayoutContainer.SetAnchorAndMarginPreset(_gameChat, LayoutContainer.LayoutPreset.TopRight, margin: 10);
            LayoutContainer.SetAnchorAndMarginPreset(_gameChat, LayoutContainer.LayoutPreset.TopRight, margin: 10);
            LayoutContainer.SetMarginLeft(_gameChat, -475);
            LayoutContainer.SetMarginBottom(_gameChat, HudChatBox.InitialChatBottom);
            _chatManager.ChatBoxOnResized(new ChatResizedEventArgs(HudChatBox.InitialChatBottom));
            Viewport = new MainViewport
            {
                Viewport =
                {
                    ViewportSize = ViewportSize
                }
            };

            UserInterfaceManager.StateRoot.AddChild(Viewport);
            LayoutContainer.SetAnchorPreset(Viewport, LayoutContainer.LayoutPreset.Wide);
            Viewport.SetPositionFirst();
            _chatManager.SetChatBox(_gameChat);

            ChatInput.SetupChatInputHandlers(_inputManager, _gameChat);

            SetupPresenters();

            _eyeManager.MainViewport = Viewport.Viewport;

            _overlayManager.AddOverlay(new ShowHandItemOverlay());

            _fpsCounter = new FpsCounter(_gameTiming);
            UserInterfaceManager.StateRoot.AddChild(_fpsCounter);
            _fpsCounter.Visible = _configurationManager.GetCVar(CCVars.HudFpsCounterVisible);
            _configurationManager.OnValueChanged(CCVars.HudFpsCounterVisible, (show) => { _fpsCounter.Visible = show; });
        }

        protected override void Shutdown()
        {
            _overlayManager.RemoveOverlay<ShowHandItemOverlay>();
            DisposePresenters();

            base.Shutdown();

            _gameChat?.Dispose();
            Viewport.Dispose();
            // Clear viewport to some fallback, whatever.
            _eyeManager.MainViewport = UserInterfaceManager.MainViewport;
            _fpsCounter.Dispose();
        }

        /// <summary>
        /// All UI Presenters should be constructed in here.
        /// </summary>
        private void SetupPresenters()
        {
            // HUD
            //_alertsFramePresenter = new AlertsFramePresenter();
        }

        /// <summary>
        /// All UI Presenters should be disposed in here.
        /// </summary>
        private void DisposePresenters()
        {
            // Windows
            _constructionMenu?.Dispose();

            // HUD
            _alertsFramePresenter?.Dispose();
        }

        internal static void FocusChat(ChatBox chat)
        {
            if (chat.UserInterfaceManager.KeyboardFocused != null)
                return;

            chat.Focus();
        }

        internal static void FocusChannel(ChatBox chat, ChatSelectChannel channel)
        {
            if (chat.UserInterfaceManager.KeyboardFocused != null)
                return;

            chat.Focus(channel);
        }

        public override void FrameUpdate(FrameEventArgs e)
        {
            base.FrameUpdate(e);

            Viewport.Viewport.Eye = _eyeManager.CurrentEye;
        }

        protected override void OnKeyBindStateChanged(ViewportBoundKeyEventArgs args)
        {
            if (args.Viewport == null)
                base.OnKeyBindStateChanged(new ViewportBoundKeyEventArgs(args.KeyEventArgs, Viewport.Viewport));
            else
                base.OnKeyBindStateChanged(args);
        }
    }
}
