using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared.Movement.Components;

[NetworkedComponent, RegisterComponent]
[AutoGenerateComponentState]
public sealed partial class SlowContactsComponent : Component
{
    [DataField("walkSpeedModifier"), ViewVariables(VVAccess.ReadWrite)]
    [AutoNetworkedField]
    public float WalkSpeedModifier = 1.0f;

    [AutoNetworkedField]
    [DataField("sprintSpeedModifier"), ViewVariables(VVAccess.ReadWrite)]
    public float SprintSpeedModifier = 1.0f;

    [DataField("ignoreWhitelist")]
    public EntityWhitelist? IgnoreWhitelist;
}
