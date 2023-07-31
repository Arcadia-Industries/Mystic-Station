﻿using Content.Server.Chat.Managers;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Station.Systems;
using Content.Shared.Chat;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.GameTicking.Rules;

/// <summary>
/// This handles logic and interactions related to <see cref="RespawnDeadRuleComponent"/>
/// </summary>
public sealed class RespawnRuleSystem : GameRuleSystem<RespawnDeadRuleComponent>
{
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly StationSystem _station = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
        SubscribeLocalEvent<SuicideEvent>(OnSuicide);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent ev)
    {
        var query = EntityQueryEnumerator<RespawnTrackerComponent>();
        while (query.MoveNext(out _, out var respawn))
        {
            respawn.Players.Add(ev.Player.UserId);
        }
    }

    private void OnSuicide(SuicideEvent ev)
    {
        if (!TryComp<ActorComponent>(ev.Victim, out var actor))
           return;

        var query = EntityQueryEnumerator<RespawnTrackerComponent>();
        while (query.MoveNext(out _, out var respawn))
        {
            respawn.Players.Remove(actor.PlayerSession.UserId);
        }
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        if (!TryComp<ActorComponent>(args.Target, out var actor))
            return;

        var query = EntityQueryEnumerator<RespawnDeadRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out _, out var rule))
        {
            if (!GameTicker.IsGameRuleActive(uid, rule))
                continue;

            RespawnPlayer(args.Target, uid, actor: actor);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_station.GetStations().FirstOrNull() is not { } station)
            return;

        foreach (var tracker in EntityQuery<RespawnTrackerComponent>())
        {
            var queue = new Dictionary<NetUserId, TimeSpan>(tracker.RespawnQueue);
            foreach (var (player, time) in queue)
            {
                if (_timing.CurTime < time)
                    continue;

                if (!_playerManager.TryGetSessionById(player, out var session))
                    continue;

                GameTicker.MakeJoinGame(session, station, silent: true);
                tracker.RespawnQueue.Remove(session.UserId);
            }
        }
    }

    public void RespawnPlayer(EntityUid player, EntityUid respawnTracker, RespawnTrackerComponent? component = null, ActorComponent? actor = null)
    {
        if (!Resolve(respawnTracker, ref component) || !Resolve(player, ref actor, false))
            return;

        if (!component.Players.Contains(actor.PlayerSession.UserId))
            return;

        if (component.RespawnDelay == TimeSpan.Zero)
        {
            if (_station.GetStations().FirstOrNull() is not { } station)
                return;

            GameTicker.MakeJoinGame(actor.PlayerSession, station, silent: true);
            return;
        }

        var msg = Loc.GetString("rule-respawn-in-seconds", ("second", component.RespawnDelay.TotalSeconds));
        var wrappedMsg = Loc.GetString("chat-manager-server-wrap-message", ("message", msg));
        _chatManager.ChatMessageToOne(ChatChannel.Server, msg, wrappedMsg, respawnTracker, false, actor.PlayerSession.ConnectedClient, Color.LimeGreen);
        component.RespawnQueue.Add(actor.PlayerSession.UserId, _timing.CurTime + component.RespawnDelay);
    }
}
