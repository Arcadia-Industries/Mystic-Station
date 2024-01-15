using Content.Shared.RCD.Systems;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.RCD.Components;

/// <summary>
/// Main component for the RCD
/// Optionally uses LimitedChargesComponent.
/// Charges can be refilled with RCD ammo
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(RCDSystem))]
public sealed partial class RCDComponent : Component
{
    /// <summary>
    /// Sound that plays when a RCD operation successfully completes
    /// </summary>
    [DataField("successSound")]
    public SoundSpecifier SuccessSound = new SoundPathSpecifier("/Audio/Items/deconstruct.ogg");

    /// <summary>
    /// The ProtoId of the currently selected RCD prototype
    /// </summary>
    [AutoNetworkedField]
    public ProtoId<RCDPrototype> ProtoId = default!;

    /// <summary>
    /// A cached copy of currently selected RCD prototype
    /// </summary>
    /// <remarks>
    /// If the ProtoId is changed, make sure to update the CachedPrototype as well
    /// </remarks>
    public RCDPrototype CachedPrototype = default!;

    /// <summary>
    /// List of RCD prototypes that the device comes loaded with
    /// </summary>
    [DataField("availablePrototypes"), AutoNetworkedField]
    public HashSet<ProtoId<RCDPrototype>> AvailablePrototypes = new();

    public Direction PrototypeDirection = Direction.South;
}

public enum RcdMode : byte
{
    Invalid,
    Deconstruct,
    DeconstructTile,
    DeconstructObject,
    ConstructTile,
    ConstructObject,
}

public enum RcdConstructionRule : byte
{
    Invalid,
    MustBuildOnEmptyTile,
    CanBuildOnEmptyTile,
    MustBuildOnSubfloor,
    DirectionalCollider,
    IsWindow,
}

public enum RcdRotationRule : byte
{
    Fixed,
    Camera,
    User,
}

[Serializable, NetSerializable]
public sealed class RCDSystemMessage : BoundUserInterfaceMessage
{
    public ProtoId<RCDPrototype> ProtoId;

    public RCDSystemMessage(ProtoId<RCDPrototype> protoId)
    {
        ProtoId = protoId;
    }
}

/// <summary>
/// A message that calls the click interaction on a alert
/// </summary>
[Serializable, NetSerializable]
public sealed class RCDRotationEvent : EntityEventArgs
{
    public readonly NetEntity NetEntity;
    public readonly Direction Direction;

    public RCDRotationEvent(NetEntity netEntity, Direction direction)
    {
        NetEntity = netEntity;
        Direction = direction;
    }
}

[Serializable, NetSerializable]
public enum RcdUiKey : byte
{
    Key
}
