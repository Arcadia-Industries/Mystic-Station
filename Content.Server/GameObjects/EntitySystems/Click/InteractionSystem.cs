using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.GameObjects.Components.Items.Storage;
using Content.Server.GameObjects.Components.Mobs;
using Content.Server.GameObjects.Components.Pulling;
using Content.Server.GameObjects.Components.Buckle;
using Content.Server.GameObjects.Components.Timing;
using Content.Server.Interfaces.GameObjects.Components.Items;
using Content.Shared.GameObjects.Components.Inventory;
using Content.Shared.GameObjects.Components.Items;
using Content.Shared.GameObjects.Components.Rotatable;
using Content.Shared.GameObjects.EntitySystemMessages;
using Content.Shared.GameObjects.EntitySystems;
using Content.Shared.GameObjects.EntitySystems.ActionBlocker;
using Content.Shared.Input;
using Content.Shared.Interfaces.GameObjects.Components;
using Content.Shared.Utility;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Players;
using Robust.Shared.Random;

namespace Content.Server.GameObjects.EntitySystems.Click
{
    /// <summary>
    /// Governs interactions during clicking on entities
    /// </summary>
    [UsedImplicitly]
    public sealed class InteractionSystem : SharedInteractionSystem
    {
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IRobustRandom _random = default!;

        public override void Initialize()
        {
            SubscribeNetworkEvent<DragDropRequestEvent>(HandleDragDropRequestEvent);

            CommandBinds.Builder
                .Bind(EngineKeyFunctions.Use,
                    new PointerInputCmdHandler(HandleUseInteraction))
                .Bind(ContentKeyFunctions.WideAttack,
                    new PointerInputCmdHandler(HandleWideAttack))
                .Bind(ContentKeyFunctions.ActivateItemInWorld,
                    new PointerInputCmdHandler(HandleActivateItemInWorld))
                .Bind(ContentKeyFunctions.TryPullObject,
                    new PointerInputCmdHandler(HandleTryPullObject))
                .Register<InteractionSystem>();
        }

        public override void Shutdown()
        {
            CommandBinds.Unregister<InteractionSystem>();
            base.Shutdown();
        }

        #region Client Input Validation
        private bool ValidateClientInput(ICommonSession? session, EntityCoordinates coords, EntityUid uid, [NotNullWhen(true)] out IEntity? userEntity)
        {
            userEntity = null;

            if (!coords.IsValid(_entityManager))
            {
                Logger.InfoS("system.interaction", $"Invalid Coordinates: client={session}, coords={coords}");
                return false;
            }

            if (uid.IsClientSide())
            {
                Logger.WarningS("system.interaction",
                    $"Client sent interaction with client-side entity. Session={session}, Uid={uid}");
                return false;
            }

            userEntity = ((IPlayerSession?) session)?.AttachedEntity;

            if (userEntity == null || !userEntity.IsValid())
            {
                Logger.WarningS("system.interaction",
                    $"Client sent interaction with no attached entity. Session={session}");
                return false;
            }

            return true;
        }
        #endregion

        #region Drag drop
        private void HandleDragDropRequestEvent(DragDropRequestEvent msg, EntitySessionEventArgs args)
        {
            if (!ValidateClientInput(args.SenderSession, msg.DropLocation, msg.Target, out var userEntity))
            {
                Logger.InfoS("system.interaction", $"DragDropRequestEvent input validation failed");
                return;
            }

            if (!EntityManager.TryGetEntity(msg.Dropped, out var dropped))
                return;
            if (!EntityManager.TryGetEntity(msg.Target, out var target))
                return;

            var interactionArgs = new DragDropEvent(userEntity, msg.DropLocation, dropped, target);

            // must be in range of both the target and the object they are drag / dropping
            // Client also does this check but ya know we gotta validate it.
            if (!interactionArgs.InRangeUnobstructed(ignoreInsideBlocker: true, popup: true))
                return;

            // trigger dragdrops on the dropped entity
            RaiseLocalEvent(dropped.Uid, interactionArgs);
            foreach (var dragDrop in dropped.GetAllComponents<IDraggable>())
            {
                if (dragDrop.CanDrop(interactionArgs) &&
                    dragDrop.Drop(interactionArgs))
                {
                    return;
                }
            }

            // trigger dragdropons on the targeted entity
            RaiseLocalEvent(target.Uid, interactionArgs, false);
            foreach (var dragDropOn in target.GetAllComponents<IDragDropOn>())
            {
                if (dragDropOn.CanDragDropOn(interactionArgs) &&
                    dragDropOn.DragDropOn(interactionArgs))
                {
                    return;
                }
            }
        }
        #endregion

        #region ActivateItemInWorld
        private bool HandleActivateItemInWorld(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            if (!ValidateClientInput(session, coords, uid, out var playerEnt))
            {
                Logger.InfoS("system.interaction", $"ActivateItemInWorld input validation failed");
                return false;
            }

            if (!EntityManager.TryGetEntity(uid, out var used))
                return false;

            InteractionActivate(playerEnt, used);
            return true;
        }

        /// <summary>
        /// Activates the IActivate behavior of an object
        /// Verifies that the user is capable of doing the use interaction first
        /// </summary>
        public void TryInteractionActivate(IEntity? user, IEntity? used)
        {
            if (user == null || used == null)
                return;

            InteractionActivate(user, used);
        }

        private void InteractionActivate(IEntity user, IEntity used)
        {
            if (!ActionBlockerSystem.CanInteract(user) || ! ActionBlockerSystem.CanUse(user))
                return;

            // all activates should only fire when in range / unbostructed
            if (!InRangeUnobstructed(user, used, ignoreInsideBlocker: true, popup: true))
                return;

            var activateMsg = new ActivateInWorldEvent(user, used);
            RaiseLocalEvent(used.Uid, activateMsg);
            if (activateMsg.Handled)
                return;

            if (!used.TryGetComponent(out IActivate? activateComp))
                return;

            var activateEventArgs = new ActivateEventArgs(user, used);
            activateComp.Activate(activateEventArgs);
        }
        #endregion

        private bool HandleWideAttack(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            // client sanitization
            if (!ValidateClientInput(session, coords, uid, out var userEntity))
            {
                Logger.InfoS("system.interaction", $"WideAttack input validation failed");
                return true;
            }

            if (userEntity.TryGetComponent(out CombatModeComponent? combatMode) && combatMode.IsInCombatMode)
                DoAttack(userEntity, coords, true);

            return true;
        }

        /// <summary>
        /// Entity will try and use their active hand at the target location.
        /// Don't use for players
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="coords"></param>
        /// <param name="uid"></param>
        internal void AiUseInteraction(IEntity entity, EntityCoordinates coords, EntityUid uid)
        {
            if (entity.HasComponent<ActorComponent>())
                throw new InvalidOperationException();

            UserInteraction(entity, coords, uid);
        }

        public bool HandleUseInteraction(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            // client sanitization
            if (!ValidateClientInput(session, coords, uid, out var userEntity))
            {
                Logger.InfoS("system.interaction", $"Use input validation failed");
                return true;
            }

            UserInteraction(userEntity, coords, uid);

            return true;
        }

        private bool HandleTryPullObject(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            if (!ValidateClientInput(session, coords, uid, out var userEntity))
            {
                Logger.InfoS("system.interaction", $"TryPullObject input validation failed");
                return true;
            }

            if (userEntity.Uid == uid)
                return false;

            if (!EntityManager.TryGetEntity(uid, out var pulledObject))
                return false;

            if (!InRangeUnobstructed(userEntity, pulledObject, popup: true))
                return false;

            if (!pulledObject.TryGetComponent(out PullableComponent? pull))
                return false;

            return pull.TogglePull(userEntity);
        }

        public async void UserInteraction(IEntity player, EntityCoordinates coordinates, EntityUid clickedUid)
        {
            if (player.TryGetComponent(out CombatModeComponent? combatMode) && combatMode.IsInCombatMode)
            {
                DoAttack(player, coordinates, false, clickedUid);
                return;
            }

            // Verify player is on the same map as the entity he clicked on
            if (coordinates.GetMapId(_entityManager) != player.Transform.MapID)
            {
                Logger.WarningS("system.interaction",
                    $"Player named {player.Name} clicked on a map he isn't located on");
                return;
            }

            FaceClickCoordinates(player, coordinates);

            if (!ActionBlockerSystem.CanInteract(player))
                return;

            // Get entity clicked upon from UID if valid UID, if not assume no entity clicked upon and null
            EntityManager.TryGetEntity(clickedUid, out var attacked);

            // Check if interacted entity is a in the same container, the direct child, or direct parent of the user.
            if (attacked != null && !player.IsInSameOrParentContainer(attacked))
            {
                // Either the attacked entity is null, not contained or in a different container
                Logger.WarningS("system.interaction",
                    $"Player named {player.Name} clicked on object {attacked.Name} that isn't in the same container");
                return;
            }

            // Verify player has a hand, and find what object he is currently holding in his active hand
            if (!player.TryGetComponent<IHandsComponent>(out var hands))
                return;

            var item = hands.GetActiveHand?.Owner;

            // TODO: Check if client should be able to see that object to click on it in the first place

            // Clicked on empty space behavior, try using ranged attack
            if (attacked == null)
            {
                if (item != null)
                {
                    // After attack: Check if we clicked on an empty location, if so the only interaction we can do is AfterInteract
                    var distSqrt = (player.Transform.WorldPosition - coordinates.ToMapPos(EntityManager)).LengthSquared;
                    InteractAfter(player, item, coordinates, distSqrt <= InteractionRangeSquared);
                }

                return;
            }

            // RangedInteract/AfterInteract: Check distance between user and clicked item, if too large parse it in the ranged function
            // TODO: have range based upon the item being used? or base it upon some variables of the player himself?
            var distance = (player.Transform.WorldPosition - attacked.Transform.WorldPosition).LengthSquared;
            if (distance > InteractionRangeSquared)
            {
                if (item != null)
                {
                    InteractUsingRanged(player, item, attacked, coordinates);
                    return;
                }

                return; // Add some form of ranged InteractHand here if you need it someday, or perhaps just ways to modify the range of InteractHand
            }

            // We are close to the nearby object and the object isn't contained in our active hand
            // InteractUsing/AfterInteract: We will either use the item on the nearby object
            if (item != null)
            {
                await InteractUsing(player, item, attacked, coordinates);
            }
            // InteractHand/Activate: Since our hand is empty we will use InteractHand/Activate
            else
            {
                InteractHand(player, attacked);
            }
        }

        private void FaceClickCoordinates(IEntity player, EntityCoordinates coordinates)
        {
            var diff = coordinates.ToMapPos(EntityManager) - player.Transform.MapPosition.Position;
            if (diff.LengthSquared <= 0.01f)
                return;
            var diffAngle = Angle.FromWorldVec(diff);
            if (ActionBlockerSystem.CanChangeDirection(player))
            {
                player.Transform.LocalRotation = diffAngle;
            }
            else
            {
                if (player.TryGetComponent(out BuckleComponent? buckle) && (buckle.BuckledTo != null))
                {
                    // We're buckled to another object. Is that object rotatable?
                    if (buckle.BuckledTo!.Owner.TryGetComponent(out SharedRotatableComponent? rotatable) && rotatable.RotateWhileAnchored)
                    {
                        // Note the assumption that even if unanchored, player can only do spinnychair with an "independent wheel".
                        // (Since the player being buckled to it holds it down with their weight.)
                        // This is logically equivalent to RotateWhileAnchored.
                        // Barstools and office chairs have independent wheels, while regular chairs don't.
                        rotatable.Owner.Transform.LocalRotation = diffAngle;
                    }
                }
            }
        }

        /// <summary>
        ///     We didn't click on any entity, try doing an AfterInteract on the click location
        /// </summary>
        private async void InteractAfter(IEntity user, IEntity weapon, EntityCoordinates clickLocation, bool canReach)
        {
            var message = new AfterInteractEvent(user, weapon, null, clickLocation, canReach);
            RaiseLocalEvent(weapon.Uid, message);
            if (message.Handled)
            {
                return;
            }

            var afterInteractEventArgs = new AfterInteractEventArgs(user, clickLocation, null, canReach);
            await InteractAfter(weapon, afterInteractEventArgs);
        }

        /// <summary>
        /// Uses a weapon/object on an entity
        /// Finds components with the InteractUsing interface and calls their function
        /// </summary>
        public async Task InteractUsing(IEntity user, IEntity weapon, IEntity attacked, EntityCoordinates clickLocation)
        {
            if (!ActionBlockerSystem.CanInteract(user))
                return;

            // all interactions should only happen when in range / unobstructed, so no range check is needed
            if (InRangeUnobstructed(user, attacked, ignoreInsideBlocker: true, popup: true))
            {
                var attackMsg = new InteractUsingEvent(user, weapon, attacked, clickLocation);
                RaiseLocalEvent(attacked.Uid, attackMsg);
                if (attackMsg.Handled)
                    return;

                var attackByEventArgs = new InteractUsingEventArgs(user, clickLocation, weapon, attacked);

                var attackBys = attacked.GetAllComponents<IInteractUsing>().OrderByDescending(x => x.Priority);
                foreach (var attackBy in attackBys)
                {
                    if (await attackBy.InteractUsing(attackByEventArgs))
                    {
                        // If an InteractUsing returns a status completion we finish our attack
                        return;
                    }
                }
            }

            // If we aren't directly attacking the nearby object, lets see if our item has an after attack we can do
            var afterAtkMsg = new AfterInteractEvent(user, weapon, attacked, clickLocation, true);
            RaiseLocalEvent(weapon.Uid, afterAtkMsg, false);
            if (afterAtkMsg.Handled)
            {
                return;
            }

            var afterInteractEventArgs = new AfterInteractEventArgs(user, clickLocation, attacked, canReach: true);
            await InteractAfter(weapon, afterInteractEventArgs);
        }

        /// <summary>
        /// Uses an empty hand on an entity
        /// Finds components with the InteractHand interface and calls their function
        /// </summary>
        public void InteractHand(IEntity user, IEntity attacked)
        {
            if (!ActionBlockerSystem.CanInteract(user))
                return;

            if (InRangeUnobstructed(user, attacked, ignoreInsideBlocker: true, popup: true))
            {
                var message = new InteractHandEvent(user, attacked);
                RaiseLocalEvent(attacked.Uid, message);
                if (message.Handled)
                    return;

                var attackHandEventArgs = new InteractHandEventArgs(user, attacked);

                // all attackHands should only fire when in range / unobstructed
                var attackHands = attacked.GetAllComponents<IInteractHand>().ToList();
                foreach (var attackHand in attackHands)
                {
                    if (attackHand.InteractHand(attackHandEventArgs))
                    {
                        // If an InteractHand returns a status completion we finish our attack
                        return;
                    }
                }
            }

            // Else we run Activate.
            InteractionActivate(user, attacked);
        }

        #region Hands
        #region Use
        /// <summary>
        /// Activates the IUse behaviors of an entity
        /// Verifies that the user is capable of doing the use interaction first
        /// </summary>
        /// <param name="user"></param>
        /// <param name="used"></param>
        public void TryUseInteraction(IEntity user, IEntity used)
        {
            if (user != null && used != null && ActionBlockerSystem.CanUse(user))
            {
                UseInteraction(user, used);
            }
        }

        /// <summary>
        /// Activates the IUse behaviors of an entity without first checking
        /// if the user is capable of doing the use interaction.
        /// </summary>
        public void UseInteraction(IEntity user, IEntity used)
        {
            if (used.TryGetComponent<UseDelayComponent>(out var delayComponent))
            {
                if (delayComponent.ActiveDelay)
                    return;
                else
                    delayComponent.BeginDelay();
            }

            var useMsg = new UseInHandEvent(user, used);
            RaiseLocalEvent(used.Uid, useMsg);
            if (useMsg.Handled)
            {
                return;
            }

            var uses = used.GetAllComponents<IUse>().ToList();

            // Try to use item on any components which have the interface
            foreach (var use in uses)
            {
                if (use.UseEntity(new UseEntityEventArgs(user)))
                {
                    // If a Use returns a status completion we finish our attack
                    return;
                }
            }
        }
        #endregion

        #region Throw
        /// <summary>
        /// Activates the Throw behavior of an object
        /// Verifies that the user is capable of doing the throw interaction first
        /// </summary>
        public bool TryThrowInteraction(IEntity user, IEntity item)
        {
            if (user == null || item == null || !ActionBlockerSystem.CanThrow(user)) return false;

            ThrownInteraction(user, item);
            return true;
        }

        /// <summary>
        ///     Calls Thrown on all components that implement the IThrown interface
        ///     on an entity that has been thrown.
        /// </summary>
        public void ThrownInteraction(IEntity user, IEntity thrown)
        {
            var throwMsg = new ThrownEvent(user, thrown);
            RaiseLocalEvent(thrown.Uid, throwMsg);
            if (throwMsg.Handled)
            {
                return;
            }

            var comps = thrown.GetAllComponents<IThrown>().ToList();
            var args = new ThrownEventArgs(user);

            // Call Thrown on all components that implement the interface
            foreach (var comp in comps)
            {
                comp.Thrown(args);
            }
        }
        #endregion

        #region Equip
        /// <summary>
        ///     Calls Equipped on all components that implement the IEquipped interface
        ///     on an entity that has been equipped.
        /// </summary>
        public void EquippedInteraction(IEntity user, IEntity equipped, EquipmentSlotDefines.Slots slot)
        {
            var equipMsg = new EquippedEvent(user, equipped, slot);
            RaiseLocalEvent(equipped.Uid, equipMsg);
            if (equipMsg.Handled)
            {
                return;
            }

            var comps = equipped.GetAllComponents<IEquipped>().ToList();

            // Call Thrown on all components that implement the interface
            foreach (var comp in comps)
            {
                comp.Equipped(new EquippedEventArgs(user, slot));
            }
        }

        /// <summary>
        ///     Calls Unequipped on all components that implement the IUnequipped interface
        ///     on an entity that has been equipped.
        /// </summary>
        public void UnequippedInteraction(IEntity user, IEntity equipped, EquipmentSlotDefines.Slots slot)
        {
            var unequipMsg = new UnequippedEvent(user, equipped, slot);
            RaiseLocalEvent(equipped.Uid, unequipMsg);
            if (unequipMsg.Handled)
            {
                return;
            }

            var comps = equipped.GetAllComponents<IUnequipped>().ToList();

            // Call Thrown on all components that implement the interface
            foreach (var comp in comps)
            {
                comp.Unequipped(new UnequippedEventArgs(user, slot));
            }
        }

        #region Equip Hand
        /// <summary>
        ///     Calls EquippedHand on all components that implement the IEquippedHand interface
        ///     on an item.
        /// </summary>
        public void EquippedHandInteraction(IEntity user, IEntity item, SharedHand hand)
        {
            var equippedHandMessage = new EquippedHandEvent(user, item, hand);
            RaiseLocalEvent(item.Uid, equippedHandMessage);
            if (equippedHandMessage.Handled)
            {
                return;
            }

            var comps = item.GetAllComponents<IEquippedHand>().ToList();

            foreach (var comp in comps)
            {
                comp.EquippedHand(new EquippedHandEventArgs(user, hand));
            }
        }

        /// <summary>
        ///     Calls UnequippedHand on all components that implement the IUnequippedHand interface
        ///     on an item.
        /// </summary>
        public void UnequippedHandInteraction(IEntity user, IEntity item, SharedHand hand)
        {
            var unequippedHandMessage = new UnequippedHandEvent(user, item, hand);
            RaiseLocalEvent(item.Uid, unequippedHandMessage);
            if (unequippedHandMessage.Handled)
            {
                return;
            }

            var comps = item.GetAllComponents<IUnequippedHand>().ToList();

            foreach (var comp in comps)
            {
                comp.UnequippedHand(new UnequippedHandEventArgs(user, hand));
            }
        }
        #endregion
        #endregion

        #region Drop
        /// <summary>
        /// Activates the Dropped behavior of an object
        /// Verifies that the user is capable of doing the drop interaction first
        /// </summary>
        public bool TryDroppedInteraction(IEntity user, IEntity item, bool intentional)
        {
            if (user == null || item == null || !ActionBlockerSystem.CanDrop(user)) return false;

            DroppedInteraction(user, item, intentional);
            return true;
        }

        /// <summary>
        ///     Calls Dropped on all components that implement the IDropped interface
        ///     on an entity that has been dropped.
        /// </summary>
        public void DroppedInteraction(IEntity user, IEntity item, bool intentional)
        {
            var dropMsg = new DroppedEvent(user, item, intentional);
            RaiseLocalEvent(item.Uid, dropMsg);
            if (dropMsg.Handled)
            {
                return;
            }

            item.Transform.LocalRotation = intentional ? Angle.Zero : (_random.Next(0, 100) / 100f) * MathHelper.TwoPi;

            var comps = item.GetAllComponents<IDropped>().ToList();

            // Call Land on all components that implement the interface
            foreach (var comp in comps)
            {
                comp.Dropped(new DroppedEventArgs(user, intentional));
            }
        }
        #endregion

        #region Hand Selected
        /// <summary>
        ///     Calls HandSelected on all components that implement the IHandSelected interface
        ///     on an item entity on a hand that has just been selected.
        /// </summary>
        public void HandSelectedInteraction(IEntity user, IEntity item)
        {
            var handSelectedMsg = new HandSelectedEvent(user, item);
            RaiseLocalEvent(item.Uid, handSelectedMsg);
            if (handSelectedMsg.Handled)
            {
                return;
            }

            var comps = item.GetAllComponents<IHandSelected>().ToList();

            // Call Land on all components that implement the interface
            foreach (var comp in comps)
            {
                comp.HandSelected(new HandSelectedEventArgs(user));
            }
        }

        /// <summary>
        ///     Calls HandDeselected on all components that implement the IHandDeselected interface
        ///     on an item entity on a hand that has just been deselected.
        /// </summary>
        public void HandDeselectedInteraction(IEntity user, IEntity item)
        {
            var handDeselectedMsg = new HandDeselectedEvent(user, item);
            RaiseLocalEvent(item.Uid, handDeselectedMsg);
            if (handDeselectedMsg.Handled)
            {
                return;
            }

            var comps = item.GetAllComponents<IHandDeselected>().ToList();

            // Call Land on all components that implement the interface
            foreach (var comp in comps)
            {
                comp.HandDeselected(new HandDeselectedEventArgs(user));
            }
        }
        #endregion
        #endregion

        /// <summary>
        /// Will have two behaviors, either "uses" the weapon at range on the entity if it is capable of accepting that action
        /// Or it will use the weapon itself on the position clicked, regardless of what was there
        /// </summary>
        public async void InteractUsingRanged(IEntity user, IEntity weapon, IEntity attacked, EntityCoordinates clickLocation)
        {
            var rangedMsg = new RangedInteractEvent(user, weapon, attacked, clickLocation);
            RaiseLocalEvent(attacked.Uid, rangedMsg);
            if (rangedMsg.Handled)
                return;

            var rangedInteractions = attacked.GetAllComponents<IRangedInteract>().ToList();
            var rangedInteractionEventArgs = new RangedInteractEventArgs(user, weapon, clickLocation);

            // See if we have a ranged attack interaction
            foreach (var t in rangedInteractions)
            {
                if (t.RangedInteract(rangedInteractionEventArgs))
                {
                    // If an InteractUsing returns a status completion we finish our attack
                    return;
                }
            }

            var afterAtkMsg = new AfterInteractEvent(user, weapon, attacked, clickLocation, false);
            RaiseLocalEvent(weapon.Uid, afterAtkMsg);
            if (afterAtkMsg.Handled)
                return;

            // See if we have a ranged attack interaction
            var afterInteractEventArgs = new AfterInteractEventArgs(user, clickLocation, attacked, canReach: false);
            await InteractAfter(weapon, afterInteractEventArgs);
        }

        private static async Task InteractAfter(IEntity weapon, AfterInteractEventArgs afterInteractEventArgs)
        {
            var afterInteracts = weapon.GetAllComponents<IAfterInteract>().OrderByDescending(x => x.Priority).ToList();

            foreach (var afterInteract in afterInteracts)
            {
                if (await afterInteract.AfterInteract(afterInteractEventArgs))
                {
                    return;
                }
            }
        }

        public void DoAttack(IEntity player, EntityCoordinates coordinates, bool wideAttack, EntityUid targetUid = default)
        {
            // Verify player is on the same map as the entity he clicked on
            if (coordinates.GetMapId(EntityManager) != player.Transform.MapID)
            {
                Logger.WarningS("system.interaction",
                    $"Player named {player.Name} clicked on a map he isn't located on");
                return;
            }

            FaceClickCoordinates(player, coordinates);

            if (!ActionBlockerSystem.CanAttack(player) ||
                (!wideAttack && !player.InRangeUnobstructed(coordinates, ignoreInsideBlocker: true)))
            {
                return;
            }

            // In a container where the target entity is not the container's owner
            if (player.TryGetContainer(out var playerContainer) &&
                (!EntityManager.TryGetEntity(targetUid, out var target) ||
                target != playerContainer.Owner))
            {
                // Either the target entity is null, not contained or in a different container
                if (target == null ||
                    !target.TryGetContainer(out var attackedContainer) ||
                    attackedContainer != playerContainer)
                {
                    return;
                }
            }

            var eventArgs = new AttackEvent(player, coordinates, wideAttack, targetUid);

            // Verify player has a hand, and find what object he is currently holding in his active hand
            if (player.TryGetComponent<IHandsComponent>(out var hands))
            {
                var item = hands.GetActiveHand?.Owner;

                if (item != null)
                {
                    RaiseLocalEvent(item.Uid, eventArgs, false);
                    foreach (var attackComponent in item.GetAllComponents<IAttack>())
                    {
                        if (wideAttack ? attackComponent.WideAttack(eventArgs) : attackComponent.ClickAttack(eventArgs))
                            return;
                    }
                }
                else
                {
                    // We pick up items if our hand is empty, even if we're in combat mode.
                    if (EntityManager.TryGetEntity(targetUid, out var targetEnt))
                    {
                        if (targetEnt.HasComponent<ItemComponent>())
                        {
                            InteractHand(player, targetEnt);
                            return;
                        }
                    }
                }
            }

            RaiseLocalEvent(player.Uid, eventArgs);
            foreach (var attackComponent in player.GetAllComponents<IAttack>())
            {
                if (wideAttack)
                    attackComponent.WideAttack(eventArgs);
                else
                    attackComponent.ClickAttack(eventArgs);
            }
        }
    }
}
