using Robust.Shared.GameStates;

namespace Content.Shared.Item;

/// <summary>
/// Handles whether the item is hot when toggled on. 
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ItemToggleHotComponent : Component
{
    /// <summary>
    ///     Item becomes hot when active.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), DataField]
    public bool IsHotWhenActivated = true;
}
