using Content.Shared.Storage;

namespace Content.Shared.Procedural.PostGeneration;

/// <summary>
/// Spawns entities inside corners.
/// </summary>
public sealed class CornerClutterPostGen : IPostDunGen
{
    [DataField("chance")]
    public float Chance = 0.25f;

    /// <summary>
    /// The default starting bulbs
    /// </summary>
    [DataField("contents", required: true)]
    public List<EntitySpawnEntry> Contents = new();
}
