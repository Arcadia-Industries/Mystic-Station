using Content.Shared.Weapons.Melee;

namespace Content.Server.Speech.Components;

[RegisterComponent]
[AutoGenerateComponentState]
[Access(typeof(SharedMeleeSpeechSystem), Other = AccessPermissions.ReadWrite)]
public sealed class MeleeSpeechComponent : Component
{

	[ViewVariables(VVAccess.ReadWrite)]
	[DataField("Battlecry")]
	[AutoNetworkedField]
	[Access(typeof(SharedMeleeSpeechSystem), Other = AccessPermissions.ReadWrite)]
	public string? Battlecry;
}

