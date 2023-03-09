namespace Content.Server.Emp;

/// <summary>
/// While entity has this component it is "disabled" by EMP.
/// Add desired behaviour in other systems 
/// </summary>
[RegisterComponent]
[Access(typeof(EmpSystem))]
public sealed class EmpDisabledComponent : Component
{
    /// <summary>
    /// When this timer hits 0 component will be removed
    /// </summary>
    [DataField("timeLeft"), ViewVariables(VVAccess.ReadWrite)]
    public float TimeLeft;

    [DataField("effectCoolDown"), ViewVariables(VVAccess.ReadWrite)]
    public float EffectCooldown = 3f;

    /// <summary>
    /// When next effect will be spawned
    /// </summary>
    public TimeSpan TargetTime = TimeSpan.Zero;
}
