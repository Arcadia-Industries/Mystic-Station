#nullable enable
using Content.Server.GameObjects.Components.Interactable;
using Content.Server.GameObjects.Components.Items.Storage;
using Content.Server.Interfaces.Chat;
using Content.Server.Interfaces.GameObjects;
using Content.Server.Interfaces.GameObjects.Components.Items;
using Content.Server.Utility;
using Content.Shared.GameObjects.Components.Body;
using Content.Shared.GameObjects.Components.Body.Part;
using Content.Shared.GameObjects.Components.Interactable;
using Content.Shared.GameObjects.EntitySystems;
using Content.Shared.GameObjects.EntitySystems.ActionBlocker;
using Content.Shared.GameObjects.Verbs;
using Content.Shared.Interfaces;
using Content.Shared.Interfaces.GameObjects.Components;
using Robust.Server.GameObjects;
using Robust.Server.GameObjects.Components.Container;
using Robust.Server.Interfaces.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.Interfaces.Random;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Random;
using Robust.Shared.Utility;
using Robust.Shared.ViewVariables;
using System.Threading.Tasks;

namespace Content.Server.GameObjects.Components.Watercloset
{
    [RegisterComponent]
    public class ToiletComponent : Component, IInteractUsing,
        IInteractHand, IMapInit, IExamine, ISuicideAct
    {
        public sealed override string Name => "Toilet";

        private const float PryLidTime = 1f;

        private bool _isPrying = false;

        [ViewVariables] public bool LidOpen { get; private set; }
        [ViewVariables] public bool IsSeatUp { get; private set; }

        [ViewVariables] private SecretStashComponent _secretStash = default!;

        public override void Initialize()
        {
            base.Initialize();
            _secretStash = Owner.EnsureComponent<SecretStashComponent>();
        }

        public void MapInit()
        {
            // roll is toilet seat will be up or down
            var random = IoCManager.Resolve<IRobustRandom>();
            IsSeatUp = random.NextDouble() > 0.5f;
            UpdateSprite();
        }

        public async Task<bool> InteractUsing(InteractUsingEventArgs eventArgs)
        {
            // are player trying place or lift of cistern lid?
            if (eventArgs.Using.TryGetComponent(out ToolComponent? tool)
                && tool!.HasQuality(ToolQuality.Prying))
            {
                // check if someone is already prying this toilet
                if (_isPrying)
                    return false;
                _isPrying = true;

                if (!await tool.UseTool(eventArgs.User, Owner, PryLidTime, ToolQuality.Prying))
                {
                    _isPrying = false;
                    return false;
                }

                _isPrying = false;

                // all cool - toggle lid
                LidOpen = !LidOpen;
                UpdateSprite();

                return true;
            }
            // maybe player trying to hide something inside cistern?
            else if (LidOpen)
            {
                return _secretStash.TryHideItem(eventArgs.User, eventArgs.Using);
            }

            return false;
        }

        public bool InteractHand(InteractHandEventArgs eventArgs)
        {
            // trying get something from stash?
            if (LidOpen)
            {
                var gotItem = _secretStash.TryGetItem(eventArgs.User);

                if (gotItem)
                    return true;
            }

            ToggleToiletSeat();
            return true;
        }

        public void Examine(FormattedMessage message, bool inDetailsRange)
        {
            if (inDetailsRange && LidOpen)
            {
                if (_secretStash.HasItemInside())
                {
                    message.AddMarkup(Loc.GetString("There is [color=darkgreen]someting[/color] inside cistern!"));
                }
            }
        }

        public void ToggleToiletSeat()
        {
            IsSeatUp = !IsSeatUp;
            UpdateSprite();
        }

        private void UpdateSprite()
        {
            if (Owner.TryGetComponent(out SpriteComponent? sprite))
            {
                var state = string.Format("{0}_toilet_{1}",
                    LidOpen ? "open" : "closed",
                    IsSeatUp ? "seat_up" : "seat_down");

                sprite.LayerSetState(0, state);
            }
        }

        public SuicideKind Suicide(IEntity victim, IChatManager chat)
        {
            // check that victim even have head
            if (victim.TryGetComponent<IBody>(out var body) &&
                body.GetPartsOfType(BodyPartType.Head).Count != 0)
            {
                var othersMessage = Loc.GetString("{0:theName} sticks their head into {1:theName} and flushes it!", victim, Owner);
                victim.PopupMessageOtherClients(othersMessage);

                var selfMessage = Loc.GetString("You stick your head into {0:theName} and flushes it!", Owner);
                victim.PopupMessage(selfMessage);

                return SuicideKind.Asphyxiation;
            }
            else
            {
                var othersMessage = Loc.GetString("{0:theName} bashes themselves with {1:theName}!", victim, Owner);
                victim.PopupMessageOtherClients(othersMessage);

                var selfMessage = Loc.GetString("You bashes themselves with {0:theName}!", Owner);
                victim.PopupMessage(selfMessage);

                return SuicideKind.Blunt;
            }
        }

    }
}
