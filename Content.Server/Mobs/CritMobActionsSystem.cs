﻿using Content.Server.Administration;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.Mind.Components;
using Content.Shared.Actions;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.Console;
using Robust.Server.GameObjects;
using System;

namespace Content.Server.Mobs;

/// <summary>
///     Handles performing crit-specific actions.
/// </summary>
public sealed class CritMobActionsSystem : EntitySystem
{
    [Dependency] private readonly QuickDialogSystem _quickDialog = default!;
    [Dependency] private readonly IServerConsoleHost _host = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MobStateActionsComponent, CritSuccumbEvent>(OnSuccumb);
        SubscribeLocalEvent<MobStateActionsComponent, CritFakeDeathEvent>(OnFakeDeath);
        SubscribeLocalEvent<MobStateActionsComponent, CritLastWordsEvent>(OnLastWords);
    }

    private void OnSuccumb(EntityUid uid, MobStateActionsComponent component, CritSuccumbEvent args)
    {
        if (!TryComp<ActorComponent>(uid, out var actor))
            return;

        _host.ExecuteCommand(actor.PlayerSession, "ghost");
    }

    private void OnFakeDeath(EntityUid uid, MobStateActionsComponent component, CritFakeDeathEvent args)
    {
    }

    private void OnLastWords(EntityUid uid, MobStateActionsComponent component, CritLastWordsEvent args)
    {
        if (!TryComp<ActorComponent>(uid, out var actor))
            return;

        _quickDialog.OpenDialog(actor.PlayerSession, Loc.GetString("action-name-crit-last-words"), "",
            (string lastWords) =>
            {
                if (actor.PlayerSession.AttachedEntity != uid
                    || !_mobState.IsCritical(uid))
                    return;

                if (lastWords.Length > 20)
                {
                    lastWords = lastWords.Substring(0, 20);
                }
                lastWords += "...";

                _chat.TrySendInGameICMessage(uid, lastWords, InGameICChatType.Whisper, ChatTransmitRange.Normal, ignoreActionBlocker: true);
                _host.ExecuteCommand(actor.PlayerSession, "ghost");
            });
    }
}

public sealed class CritSuccumbEvent : InstantActionEvent
{
}

public sealed class CritFakeDeathEvent : InstantActionEvent
{
}

public sealed class CritLastWordsEvent : InstantActionEvent
{
}
