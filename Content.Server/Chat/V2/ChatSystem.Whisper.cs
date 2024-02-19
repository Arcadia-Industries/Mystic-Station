﻿using System.Globalization;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Speech.EntitySystems;
using Content.Shared.CCVar;
using Content.Shared.Chat.V2;
using Content.Shared.Chat.V2.Components;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects.Components.Localization;
using Robust.Shared.Player;
using Robust.Shared.Replays;
using Robust.Shared.Utility;

namespace Content.Server.Chat.V2;

public sealed partial class ChatSystem
{
    public void InitializeServerWhisper()
    {
        SubscribeNetworkEvent<AttemptWhisperEvent>((msg, args) => { HandleAttemptWhisperEvent(args.SenderSession, msg.Speaker, msg.Message); });
    }

    private void HandleAttemptWhisperEvent(ICommonSession player, NetEntity entity, string message)
    {
        var entityUid = GetEntity(entity);

        if (player.AttachedEntity != entityUid)
        {
            return;
        }

        if (IsRateLimited(entityUid, out var reason))
        {
            RaiseNetworkEvent(new WhisperFailedEvent(entity, reason), player);

            return;
        }

        if (!TryComp<WhisperableComponent>(entityUid, out var comp))
        {
            RaiseNetworkEvent(new WhisperFailedEvent(entity, Loc.GetString("chat-system-whisper-failed")), player);

            return;
        }

        if (message.Length > MaxChatMessageLength)
        {
            RaiseNetworkEvent(new WhisperFailedEvent( entity, Loc.GetString("chat-system-max-message-length") ), player);

            return;
        }

        SendWhisperMessage(entityUid, message, comp.MinRange, comp.MaxRange);
    }

    public bool TrySendWhisperMessage(EntityUid entityUid, string message, string asName = "")
    {
        if (!TryComp<WhisperableComponent>(entityUid, out var whisper))
            return false;

        SendWhisperMessage(entityUid, message, whisper.MinRange, whisper.MaxRange, asName);

        return true;
    }

    /// <summary>
    /// Send a whisper.
    /// </summary>
    /// <param name="entityUid">The entity who is chatting</param>
    /// <param name="message">The message to send. This will be mutated with accents, to remove tags, etc.</param>
    /// <param name="minRange">The maximum range the audio can be fully heard in</param>
    /// <param name="maxRange">The maximum range the audio can be heard at all in</param>
    /// <param name="asName">Override the name this entity will appear as.</param>
    public void SendWhisperMessage(EntityUid entityUid, string message, float minRange, float maxRange, string asName = "")
    {
        message = SanitizeSpeechMessage(entityUid, message, out var emoteStr);

        if (emoteStr?.Length > 0)
        {
            TrySendEmoteMessageWithoutRecursion(entityUid, emoteStr, asName);
        }

        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        // Mitigation for exceptions such as https://github.com/space-wizards/space-station-14/issues/24671
        try
        {
            message = FormattedMessage.RemoveMarkup(message);
        }
        catch (Exception e)
        {
            _logger.GetSawmill("chat").Error($"UID {entityUid} attempted to send {message} {(asName.Length > 0 ? "as name, " : "")} but threw a parsing error: {e}");

            return;
        }

        message = TransformSpeech(entityUid, FormattedMessage.RemoveMarkup(message));

        if (string.IsNullOrEmpty(message))
            return;

        if (string.IsNullOrEmpty(asName))
            asName = GetSpeakerName(entityUid);

        var obfuscatedMessage = ObfuscateMessageReadability(message, 0.2f);

        var name = SanitizeName(asName, UseEnglishGrammar);

        RaiseLocalEvent(new WhisperSuccessEvent(entityUid, name, minRange, maxRange, message, obfuscatedMessage));

        var msgOut = new WhisperEvent(
            GetNetEntity(entityUid),
            name,
            message
        );

        var obfuscatedMsgOut = new WhisperEvent(
            GetNetEntity(entityUid),
            name,
            obfuscatedMessage
        );

        var totallyObfuscatedlyMsgOut = new WhisperEvent(
            GetNetEntity(entityUid),
            "",
            obfuscatedMessage
        );

        var recipients = GetWhisperRecipients(entityUid, minRange, maxRange);

        // Now fire it off to legal recipients
        foreach (var session in recipients.insideMinRange)
        {
            RaiseNetworkEvent(msgOut, session);
        }

        foreach (var session in recipients.insideMaxRangeExclusiveMin)
        {
            RaiseNetworkEvent(obfuscatedMsgOut, session);
        }

        foreach (var session in recipients.insideMaxRangeExclusiveMinNoSight)
        {
            RaiseNetworkEvent(totallyObfuscatedlyMsgOut, session);
        }

        _replay.RecordServerMessage(msgOut);
        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Whisper from {ToPrettyString(entityUid):user} as {asName}: {message}");
    }

    /// <summary>
    /// Get the recipients who can hear this whisper.
    /// </summary>
    /// <param name="source">The whisperer.</param>
    /// <param name="minRange">The maximum range that the entire whisper can be heard.</param>
    /// <param name="maxRange">The maximum range the whisper can be heard at all.</param>
    /// <returns>Returns a tuple of those in the minimum range (zeroth) and the maximum range but not the minimum range (first).</returns>
    private (List<ICommonSession> insideMinRange, List<ICommonSession> insideMaxRangeExclusiveMin, List<ICommonSession> insideMaxRangeExclusiveMinNoSight) GetWhisperRecipients(EntityUid source, float minRange, float maxRange)
    {
        var minRecipients = new List<ICommonSession>();
        var maxRecipients = new List<ICommonSession>();
        var maxRecipientsNoSight = new List<ICommonSession>();

        var ghostHearing = GetEntityQuery<GhostHearingComponent>();
        var xforms = GetEntityQuery<TransformComponent>();

        var transformSource = xforms.GetComponent(source);
        var sourceMapId = transformSource.MapID;
        var sourceCoords = transformSource.Coordinates;

        foreach (var player in _playerManager.Sessions)
        {
            if (player.AttachedEntity is not { Valid: true } playerEntity)
                continue;

            var transformEntity = xforms.GetComponent(playerEntity);

            if (transformEntity.MapID != sourceMapId)
                continue;

            // Use MaxValue here to stop any shenanigans where it being defaulted to 0.0 causes it to always be in range
            var distance = float.MaxValue;

            // even if they are a ghost hearer, in some situations we still need the range
            if (!ghostHearing.HasComponent(playerEntity) &&
                (!sourceCoords.TryDistance(EntityManager, transformEntity.Coordinates, out distance) ||
                 !(distance < maxRange)))
                continue;

            if (distance < minRange)
                minRecipients.Add(player);
            else if(_interactionSystem.InRangeUnobstructed(source, playerEntity, maxRange, Shared.Physics.CollisionGroup.Opaque))
                maxRecipients.Add(player);
            else
                maxRecipientsNoSight.Add(player);
        }

        return (minRecipients, maxRecipients, maxRecipientsNoSight);
    }
}

/// <summary>
/// Raised when a character whispers.
/// </summary>
[Serializable]
public sealed class WhisperSuccessEvent : EntityEventArgs
{
    public EntityUid Speaker;
    public string AsName;
    public readonly string Message;
    public readonly string ObfuscatedMessage;
    public float MinRange;
    public float MaxRange;

    public WhisperSuccessEvent(EntityUid speaker, string asName, float minRange, float maxRange, string message, string obfuscatedMessage)
    {
        Speaker = speaker;
        AsName = asName;
        Message = message;
        ObfuscatedMessage = obfuscatedMessage;
        MinRange = minRange;
        MaxRange = maxRange;
    }
}
