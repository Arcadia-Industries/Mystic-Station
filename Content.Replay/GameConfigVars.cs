using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Replay;

[CVarDefs]
public sealed class GameConfigVars: CVars
{
    /// <summary>
    ///     Determines the threshold before visual events (muzzle flashes, chat pop-ups, etc) are suppressed when jumping forward in time.
    /// </summary>
    /// <remarks>
    ///     Effects should still show up when jumping forward ~ 1 second, but definitely not when skipping a minute or two of a gunfight.
    /// </remarks>
    public static readonly CVarDef<int> VisualEventThreshold = CVarDef.Create("replay.visual_event_threshold", 20);

    // TODO REPLAYS scale with replay tickrate?
    /// <summary>
    ///     Maximum number of ticks before a new checkpoint tick is generated.
    /// </summary>
    public static readonly CVarDef<int> CheckpointInterval = CVarDef.Create("replay.checkpoint_interval", 200);

    /// <summary>
    ///     Maximum number of entities that can be spawned before a new checkpoint tick is generated.
    /// </summary>
    public static readonly CVarDef<int> CheckpointEntitySpawnThreshold = CVarDef.Create("replay.checkpoint_entity_spawn_threshold", 100);

    /// <summary>
    ///     Maximum number of entity states that can be applied before a new checkpoint tick is generated.
    /// </summary>
    public static readonly CVarDef<int> CheckpointEntityStateThreshold = CVarDef.Create("replay.checkpoint_entity_state_threshold", 50 * 600);
}
