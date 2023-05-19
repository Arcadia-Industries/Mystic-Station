using Content.Shared.Movement.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared.Movement.Components;

/// <summary>
/// Holds SS14 eye data not relevant for engine, e.g. lerp targets.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(SharedContentEyeSystem))]
public sealed partial class ContentEyeComponent : Component
{
    public bool IsProcessed = false;

    private Vector2 _targetZoom = Vector2.One;

    /// <summary>
    /// Zoom we're lerping to.
    /// </summary>
    [DataField("targetZoom"), AutoNetworkedField]
    public Vector2 TargetZoom
    {
        get => _targetZoom;
        set
        {
            _targetZoom = value;
            IsProcessed = true;
        }
    }

    /// <summary>
    /// How far we're allowed to zoom out.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("maxZoom"), AutoNetworkedField]
    public Vector2 MaxZoom = Vector2.One;
}
