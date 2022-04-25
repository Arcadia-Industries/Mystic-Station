﻿using Content.Server.DoAfter;
using Content.Server.Popups;
using Content.Server.Stack.Events;
using Content.Shared.Sticky.Components;
using Content.Server.Sticky.Events;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Lathe.Events;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Serilog;

namespace Content.Server.Sticky.Systems;

public sealed class StickySystem : EntitySystem
{
    [Dependency] private readonly DoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly SharedHandsSystem _handsSystem = default!;

    private const string StickerSlotId = "stickers_container";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StickSuccessfulEvent>(OnStickSuccessful);
        SubscribeLocalEvent<UnstickSuccessfulEvent>(OnUnstickSuccessful);

        SubscribeLocalEvent<StickyComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<StickyComponent, GetVerbsEvent<Verb>>(AddUnstickVerb);

        SubscribeLocalEvent<InsertMaterialAttemptEvent>(OnInsertMaterialAttemptEvent);
        SubscribeLocalEvent<StackSplitAttemptEvent>(OnStackSplitAttemptEvent);
    }

    private void OnStackSplitAttemptEvent(StackSplitAttemptEvent ev)
    {
        if (EntityManager.HasComponent<HasEntityStuckOnComponent>(ev.Used))
        {
            _popupSystem.PopupEntity("cannot-split-due-to-sticky", ev.Used ,Filter.Entities(ev.User));
            ev.Cancel();
        }

        if (EntityManager.TryGetComponent(ev.Used , out StickyComponent targetComp)
            && targetComp.StuckTo != null)
        {
            _popupSystem.PopupEntity("cannot-split-due-to-sticky", ev.Used ,Filter.Entities(ev.User));
            ev.Cancel();
        }
    }

    private void OnInsertMaterialAttemptEvent(InsertMaterialAttemptEvent ev)
    {
        //Check if the inserted material has anything stuck on it
        if (
            EntityManager.TryGetComponent(ev.Inserted , out StickyComponent inserted)
            && inserted.StuckTo != null
            || EntityManager.HasComponent<HasEntityStuckOnComponent>(ev.Inserted)
            )
            ev.Cancel();
            //_popupSystem.PopupCursor( "cannot-split-due-to-sticky" , Filter.Entities(ev.User));


    }

    private void OnAfterInteract(EntityUid uid, StickyComponent component, AfterInteractEvent args)
    {

        if (args.Handled || !args.CanReach || args.Target == null)
            return;

        // try stick object to a clicked target entity
        args.Handled = StartSticking(uid, args.User, args.Target.Value, component);
    }

    private void AddUnstickVerb(EntityUid uid, StickyComponent component, GetVerbsEvent<Verb> args)
    {
        if (component.StuckTo == null || !component.CanUnstick)
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("comp-sticky-unstick-verb-text"),
            IconTexture = "/Textures/Interface/VerbIcons/eject.svg.192dpi.png",
            Act = () => StartUnsticking(uid, args.User,args.Target ,component)
        });
    }

    private bool StartSticking(EntityUid uid, EntityUid user, EntityUid target, StickyComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return false;

        // check whitelist and blacklist
        if (component.Whitelist != null && !component.Whitelist.IsValid(target))
            return false;
        if (component.Blacklist != null && component.Blacklist.IsValid(target))
            return false;

        //makes sure that neither the target nor the host have entities stuck to them or is stuck to entities
        if (
            EntityManager.HasComponent<HasEntityStuckOnComponent>(uid)
            || EntityManager.HasComponent<HasEntityStuckOnComponent>(target)
            || EntityManager.TryGetComponent(target , out StickyComponent stuckComp)
            && stuckComp.StuckTo != null
            || EntityManager.GetComponent<StickyComponent>(uid).StuckTo != null
        )
            return false;

        // check if delay is not zero to start do after
        var delay = (float) component.StickDelay.TotalSeconds;
        if (delay > 0)
        {
            // show message to user
            if (component.StickPopupStart != null)
            {
                var msg = Loc.GetString(component.StickPopupStart);
                _popupSystem.PopupEntity(msg, user, Filter.Entities(user));
            }

            // start sticking object to target
            _doAfterSystem.DoAfter(new DoAfterEventArgs(user, delay, target: target)
            {
                BroadcastFinishedEvent = new StickSuccessfulEvent(uid, user, target),
                BreakOnStun = true,
                BreakOnTargetMove = true,
                BreakOnUserMove = true,
                NeedHand = true
            });
        }
        else
        {
            // if delay is zero - stick entity immediately
            StickToEntity(uid, target, user, component);
        }

        return true;
    }

    private void OnStickSuccessful(StickSuccessfulEvent ev)
    {
        // check if entity still has sticky component
        if (!TryComp(ev.Uid, out StickyComponent? component))
            return;

        StickToEntity(ev.Uid, ev.Target, ev.User, component);
    }

    private void StartUnsticking(EntityUid uid, EntityUid user, EntityUid target ,StickyComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        var delay = (float) component.UnstickDelay.TotalSeconds;
        if (delay > 0)
        {
            // show message to user
            if (component.UnstickPopupStart != null)
            {
                var msg = Loc.GetString(component.UnstickPopupStart);
                _popupSystem.PopupEntity(msg, user, Filter.Entities(user));
            }

            // start unsticking object
            _doAfterSystem.DoAfter(new DoAfterEventArgs(user, delay, target: uid)
            {
                BroadcastFinishedEvent = new UnstickSuccessfulEvent(uid, user , target),
                BreakOnStun = true,
                BreakOnTargetMove = true,
                BreakOnUserMove = true,
                NeedHand = true
            });
        }
        else
        {
            // if delay is zero - unstick entity immediately
            UnstickFromEntity(uid, user, component);
        }

        return;
    }

    private void OnUnstickSuccessful(UnstickSuccessfulEvent ev)
    {
        // check if entity still has sticky component
        if (!TryComp(ev.Uid, out StickyComponent? component))
            return;

        UnstickFromEntity(ev.Uid, ev.User, component);

        if (EntityManager.HasComponent<HasEntityStuckOnComponent>(ev.Target))
            EntityManager.RemoveComponent<HasEntityStuckOnComponent>(ev.Target);
        else
            Log.Warning("has-entity-stuck-on-without-stuck-comp");
    }

    public void StickToEntity(EntityUid uid, EntityUid target, EntityUid user, StickyComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        // add container to entity and insert sticker into it
        var container = _containerSystem.EnsureContainer<Container>(target, StickerSlotId);
        container.ShowContents = true;
        if (!container.Insert(uid))
            return;

        // show message to user
        if (component.StickPopupSuccess != null)
        {
            var msg = Loc.GetString(component.StickPopupSuccess);
            _popupSystem.PopupEntity(msg, user, Filter.Entities(user));
        }

        // send information to appearance that entity is stuck
        if (TryComp(uid, out AppearanceComponent? appearance))
        {
            appearance.SetData(StickyVisuals.IsStuck, true);
        }

        //adds HasEntityStuckOnComponent to the entity that is stuck to use for identification
        EntityManager.AddComponent<HasEntityStuckOnComponent>(target);

        component.StuckTo = target;
        RaiseLocalEvent(uid, new EntityStuckEvent(target, user));
    }

    public void UnstickFromEntity(EntityUid uid, EntityUid user, StickyComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;
        if (component.StuckTo == null)
            return;

        // try to remove sticky item from target container
        var target = component.StuckTo.Value;
        if (!_containerSystem.TryGetContainer(target, StickerSlotId, out var container) || !container.Remove(uid))
            return;
        // delete container if it's now empty
        if (container.ContainedEntities.Count == 0)
            container.Shutdown();

        // try place dropped entity into user hands
        _handsSystem.PickupOrDrop(user, uid);

        // send information to appearance that entity isn't stuck
        if (TryComp(uid, out AppearanceComponent? appearance))
        {
            appearance.SetData(StickyVisuals.IsStuck, false);
        }

        // show message to user
        if (component.UnstickPopupSuccess != null)
        {
            var msg = Loc.GetString(component.UnstickPopupSuccess);
            _popupSystem.PopupEntity(msg, user, Filter.Entities(user));
        }

        component.StuckTo = null;
        RaiseLocalEvent(uid, new EntityUnstuckEvent(target, user));
    }

    private sealed class StickSuccessfulEvent : EntityEventArgs
    {
        public readonly EntityUid Uid;
        public readonly EntityUid User;
        public readonly EntityUid Target;

        public StickSuccessfulEvent(EntityUid uid, EntityUid user, EntityUid target)
        {
            Uid = uid;
            User = user;
            Target = target;
        }
    }

    private sealed class UnstickSuccessfulEvent : EntityEventArgs
    {
        public readonly EntityUid Uid;
        public readonly EntityUid User;
        public readonly EntityUid Target;

        public UnstickSuccessfulEvent(EntityUid uid, EntityUid user , EntityUid target)
        {
            Uid = uid;
            User = user;
            Target = target;
        }
    }
}
