using Content.Client.UserInterface.Systems.Chat;
using Content.Replay.Observer;
using Robust.Client.Audio.Midi;
using Robust.Client.GameObjects;
using Robust.Client.GameStates;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Replay.Manager;

// This partial class contains codes for modifying the current game tick/time.
public sealed partial class ReplayManager
{
    [Dependency] private readonly IMidiManager _midi = default!;

    /// <summary>
    ///     Set the current replay index (aka, jump to a specific point in time).
    /// </summary>
    private void SetIndex(int value, bool stopPlaying)
    {
        if (CurrentReplay == null)
            return;

        Playing &= !stopPlaying;
        value = Math.Clamp(value, 0, CurrentReplay.States.Count - 1);
        if (value == CurrentReplay.CurrentIndex)
            return;

        // When skipping forward or backward in time, we want to avoid changing the player's current position.
        var sys = _entMan.EntitySysManager.GetEntitySystem<ReplayObserverSystem>();
        var observer = sys.GetObserverPosition();

        bool skipEffectEvents = value > CurrentReplay.CurrentIndex + _visualEventThreshold;
        if (value < CurrentReplay.CurrentIndex)
        {
            if (CurrentReplay.RewindDisabled)
                return;

            skipEffectEvents = true;
            ResetToNearestCheckpoint(value, false);
        }
        else if (value > CurrentReplay.CurrentIndex + _visualEventThreshold)
        {
            // If we are skipping many ticks into the future, we try to skip directly to a checkpoint instead of
            // applying every tick.
            var nextCheckpoint = GetNextCheckpoint(CurrentReplay, CurrentReplay.CurrentIndex);
            if (nextCheckpoint.Index >= value)
                ResetToNearestCheckpoint(value, false);
        }

        _entMan.EntitySysManager.GetEntitySystem<ClientDirtySystem>().Reset();

        while (CurrentReplay.CurrentIndex < value)
        {
            CurrentReplay.CurrentIndex++;
            var state = CurrentReplay.CurState;

            _timing.LastRealTick = _timing.LastProcessedTick = _timing.CurTick = CurrentReplay.CurTick;
            _gameState.UpdateFullRep(state, cloneDelta: true);
            _gameState.ApplyGameState(state, CurrentReplay.NextState);
            ProcessMessages(CurrentReplay.CurMessages, skipEffectEvents);

            // TODO REPLAYS find a way to just block audio/midi from starting, instead of stopping it after every state application.
            StopAudio();

            DebugTools.Assert(CurrentReplay.LastApplied + 1 == state.ToSequence);
            CurrentReplay.LastApplied = state.ToSequence;
        }

        sys.SetObserverPosition(observer);
    }

    /// <summary>
    ///     This function resets the game state to some checkpoint state. This is effectively what enables rewinding time.
    /// </summary>
    /// <param name="index">The target tick/index. The actual checkpoint will have an index less than or equal to this.</param>
    /// <param name="flushEntities">Whether to delete all entities</param>
    private void ResetToNearestCheckpoint(int index, bool flushEntities)
    {
        // TODO REPLAYS unload prototypes & resources

        if (CurrentReplay == null)
            return;

        if (flushEntities)
            _entMan.FlushEntities();

        var checkpoint = GetLastCheckpoint(CurrentReplay, index);
        var state = checkpoint.State;
        CurrentReplay.CurrentIndex = checkpoint.Index;
        DebugTools.Assert(state.ToSequence == new GameTick(CurrentReplay.TickOffset.Value + (uint) CurrentReplay.CurrentIndex));

        foreach (var (name, value) in checkpoint.Cvars)
        {
            _netConf.SetCVar(name, value, force: true);
        }

        _timing.TimeBase = checkpoint.TimeBase;
        _timing.CurTick = _timing.LastRealTick = _timing.LastProcessedTick = new GameTick(CurrentReplay.TickOffset.Value + (uint) CurrentReplay.CurrentIndex);
        CurrentReplay.LastApplied = state.ToSequence;

        _gameState.PartialStateReset(state, false, false);
        _entMan.EntitySysManager.GetEntitySystem<ClientDirtySystem>().Reset();
        _entMan.EntitySysManager.GetEntitySystem<TransformSystem>().Reset();

        // TODO REPLAYS add a proper custom chat control?
        // Maybe one that allows players to skip directly to players via their names?
        // I don't like having to just manipulate ChatUiController like this.
        _uiMan.GetUIController<ChatUIController>().History.RemoveAll(x => x.Item1 > _timing.CurTick);
        _uiMan.GetUIController<ChatUIController>().Repopulate();
        _gameState.UpdateFullRep(state, cloneDelta: true);
        _gameState.ApplyGameState(state, CurrentReplay.NextState);

#if DEBUG
        // TODO REPLAYS add asserts
        // foreach entity
        //  if networked
        //    check last applied/modified tick
        //    foreach component
        //      check creation, modified ticks
#endif
        _timing.CurTick += 1;

        StopAudio();
    }

    public void StopAudio()
    {
        _clydeAudio.StopAllAudio();

        foreach (var renderer in _midi.Renderers)
        {
            renderer.ClearEvents();
            renderer.StopAllNotes();
        }
    }
}
