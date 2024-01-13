using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;

namespace Content.Server.Devour.Components;

[RegisterComponent]
[Access(typeof(DevouredSystem))]
public sealed partial class DevouredComponent : Component
{
    /// <summary>
    ///     The entity that devoured this entity.
    /// </summary>
    [DataField]
    public EntityUid Devourer;

    /// <summary>
    ///     Keeps track of if the target was originally pacified.
    ///     This is so it only gets removed again if the entity originally didn't have it.
    /// </summary>
    [DataField]
    public bool OriginallyPacified;

    /// <summary>
    ///     Stores the original MobStates the entity was allowed to be healed in.
    ///     This is so it can be returned to it's original state when the component is removed.
    /// </summary>
    [DataField]
    public List<MobState>? OriginalAllowedMobStates;

    /// <summary>
    ///     Stores the stomach damage so it can be deducted from the devoured entity when the damage is altered.
    /// </summary>
    public DamageSpecifier? StomachDamage;

    /// <summary>
    ///     How much extra damage can be done to the entity after it has died.
    /// </summary>
    [DataField]
    public FixedPoint2 DamageCap = 100;

    /// <summary>
    ///     Stores the current damage modifier.
    ///     This is to make sure the correct amount of damage is changed.
    /// </summary>
    [DataField]
    public FixedPoint2 CurrentModifier;

    /// <summary>
    ///     Damage multiplier if the devoured entity is alive.
    /// </summary>
    [DataField]
    public FixedPoint2 AliveMultiplier = 5;

    /// <summary>
    ///     Damage multiplier if the devoured entity is critical.
    /// </summary>
    [DataField]
    public FixedPoint2 CritMultiplier = 1;

    /// <summary>
    ///     Damage multiplier if the devoured entity is dead.
    /// </summary>
    [DataField]
    public FixedPoint2 DeadMultiplier = 0.25;

}
