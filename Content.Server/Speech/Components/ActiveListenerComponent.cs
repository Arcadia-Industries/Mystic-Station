namespace Content.Server.Speech.Components;

/// <summary>
///     This component is used to relay speech events to other systems.
/// </summary>
[RegisterComponent]
public sealed class ActiveListenerComponent : Component
{
    [DataField("range")]
    public float Range = 10;

    [DataField("requireUnobstructed")]
    public bool RequireUnobstructed = true;
}
