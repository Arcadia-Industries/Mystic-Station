namespace Content.Server.Salvage.Expeditions.Structure;

/// <summary>
/// Tracks expedition data for <see cref="SalvageStructure"/>
/// </summary>
[RegisterComponent]
public sealed class SalvageStructureExpeditionComponent : Component
{
    [ViewVariables]
    public List<EntityUid> Structures = new();
}
