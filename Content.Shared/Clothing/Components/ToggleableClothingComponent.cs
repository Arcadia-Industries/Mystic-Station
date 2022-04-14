using Content.Shared.Actions.ActionTypes;
using Content.Shared.Clothing.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared.Clothing.Components;

/// <summary>
///     This component gives an item an action that will equip or un-equip some clothing. Intended for use with
///     hardsuits and hardsuit helmets.
/// </summary>
[Friend(typeof(ToggleableClothingSystem))]
[RegisterComponent]
public sealed class ToggleableClothingComponent : Component
{
    public const string DefaultClothingContainerId = "toggleable-clothing";

    /// <summary>
    ///     Action used to toggle the clothing on or off.
    /// </summary>
    [DataField("toggleAction", required: true)]
    public InstantAction ToggleAction = new();

    /// <summary>
    ///     Default clothing entity prototype to spawn into the clothing container.
    /// </summary>
    [DataField("clothingPrototype", required: true, customTypeSerializer:typeof(PrototypeIdSerializer<EntityPrototype>))]
    public readonly string ClothingPrototype = default!;

    /// <summary>
    ///     The inventory slot that the clothing is equipped to.
    /// </summary>
    [DataField("slot")]
    public string Slot = "head";

    /// <summary>
    ///     The container that the clothing is stored in when not equipped.
    /// </summary>
    [DataField("containerId")]
    public string ContainerId = DefaultClothingContainerId;

    public ContainerSlot? Container;

    /// <summary>
    ///     The Id of the piece of clothing that belongs to this component. Required for map-saving if the clothing is
    ///     currently not inside of the container.
    /// </summary>
    [DataField("clothingUid")]
    public EntityUid? ClothingUid;
}
