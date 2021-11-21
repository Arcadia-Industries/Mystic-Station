using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;

namespace Content.Server.Maps;

/// <summary>
/// Prototype data for a game map.
/// </summary>
[Prototype("gameMap")]
public class GameMapPrototype : IPrototype
{
    /// <inheritdoc/>
    [ViewVariables, DataField("id", required: true)]
    public string ID { get; } = default!;

    /// <summary>
    /// Minimum players for the given map.
    /// </summary>
    [ViewVariables, DataField("minPlayers", required: true)]
    public uint MinPlayers { get; }

    /// <summary>
    /// Maximum players for the given map.
    /// </summary>
    [ViewVariables, DataField("maxPlayers")]
    public uint MaxPlayers { get; } = uint.MaxValue;

    /// <summary>
    /// Name of the given map.
    /// </summary>
    [ViewVariables, DataField("mapName", required: true)]
    public string MapName { get; } = default!;

    /// <summary>
    /// Relative directory path to the given map, i.e. `Maps/saltern.yml`
    /// </summary>
    [ViewVariables, DataField("mapPath", required: true)]
    public string MapPath { get; } = default!;

    /// <summary>
    /// Controls if the map can be used as a fallback if no maps are eligible.
    /// </summary>
    [ViewVariables, DataField("fallback")]
    public bool Fallback { get; }

    /// <summary>
    /// Controls if the map can be voted for.
    /// </summary>
    [ViewVariables, DataField("votable")]
    public bool Votable { get; } = true;

    /// <summary>
    /// Jobs used at round start should the station run out of job slots.
    /// Doesn't necessarily mean the station has infinite slots for the given jobs midround!
    /// </summary>
    [ViewVariables, DataField("overflowJob", required: true)]
    public List<string> OverflowJob { get; } = default!;

    /// <summary>
    /// Index of all jobs available on the station, of form
    ///  jobname: [roundstart, midround]
    /// </summary>
    [ViewVariables, DataField("availableJobs", required: true)]
    public Dictionary<string, List<int>> AvailableJobs { get; } = default!;
}
