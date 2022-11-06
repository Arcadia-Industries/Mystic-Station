using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.ViewVariables;

using Content.Shared.Radiation.Components;
using Content.Shared.Singularity.Components;
using Content.Shared.Singularity.Events;

namespace Content.Shared.Singularity.EntitySystems;

/// <summary>
/// The entity system primarily responsible for managing <see cref="SharedSingularityComponent"/>s.
/// </summary>
public abstract class SharedSingularitySystem : EntitySystem
{
#region Dependencies
    [Dependency] private readonly SharedAppearanceSystem _visualizer = default!;
    [Dependency] private readonly SharedEventHorizonSystem _horizons = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] protected readonly IViewVariablesManager _vvm = default!;
#endregion Dependencies

    /// <summary>
    /// The minimum level a singularity can be set to.
    /// </summary>
    public const byte MinSingularityLevel = 0;

    /// <summary>
    /// The maximum level a singularity can be set to.
    /// </summary>
    public const byte MaxSingularityLevel = 6;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SharedSingularityComponent, ComponentStartup>(OnSingularityStartup);
        SubscribeLocalEvent<AppearanceComponent, SingularityLevelChangedEvent>(UpdateAppearance);
        SubscribeLocalEvent<RadiationSourceComponent, SingularityLevelChangedEvent>(UpdateRadiation);
        SubscribeLocalEvent<PhysicsComponent, SingularityLevelChangedEvent>(UpdateBody);
        SubscribeLocalEvent<SharedEventHorizonComponent, SingularityLevelChangedEvent>(UpdateEventHorizon);
        SubscribeLocalEvent<SingularityDistortionComponent, SingularityLevelChangedEvent>(UpdateDistortion);

        var vvHandle = _vvm.GetTypeHandler<SharedSingularityComponent>();
        vvHandle.AddPath(nameof(SharedSingularityComponent.Level), (_, comp) => comp.Level, SetLevel);
        vvHandle.AddPath(nameof(SharedSingularityComponent.RadsPerLevel), (_, comp) => comp.RadsPerLevel, SetRadsPerLevel);
    }

    public override void Shutdown()
    {
        var vvHandle = _vvm.GetTypeHandler<SharedSingularityComponent>();
        vvHandle.RemovePath(nameof(SharedSingularityComponent.Level));
        vvHandle.RemovePath(nameof(SharedSingularityComponent.RadsPerLevel));

        base.Shutdown();
    }

#region Getters/Setters
    /// <summary>
    /// Setter for <see cref="SharedSingularityComponent.Level"/>
    /// Also sends out an event alerting that the singularities level has changed.
    /// </summary>
    /// <param name="singularity">The singularity to change the level of.</param>
    /// <param name="value">The value of the new level the singularity should have.</param>
    public void SetLevel(SharedSingularityComponent singularity, byte value)
    {
        value = MathHelper.Clamp(value, MinSingularityLevel, MaxSingularityLevel);
        var oldValue = singularity.Level;
        if (oldValue == value)
            return;

        singularity.Level = value;
        UpdateSingularityLevel(singularity, oldValue);
    }

    /// <summary>
    /// Setter for <see cref="SharedSingularityComponent.RadsPerLevel"/>
    /// Also updates the radiation output of the singularity according to the new values.
    /// </summary>
    /// <param name="singularity">The singularity to change the radioactivity of.</param>
    /// <param name="value">The new amount of radiation the singularity should emit per its level.</param>
    public void SetRadsPerLevel(SharedSingularityComponent singularity, float value)
    {
        var oldValue = singularity.RadsPerLevel;
        if (oldValue == value)
            return;

        singularity.RadsPerLevel = value;
        UpdateRadiation(singularity);
    }

    /// <summary>
    /// Alerts the entity hosting the singularity that the level of the singularity has changed.
    /// Usually follows a SharedSingularitySystem.SetLevel call, but is also used on component startup to sync everything.
    /// </summary>
    /// <param name="singularity">The singularity to update the level of.</param>
    /// <param name="oldValue">The previous level of the singularity.</param>
    public void UpdateSingularityLevel(SharedSingularityComponent singularity, byte oldValue)
    {
        RaiseLocalEvent(singularity.Owner, new SingularityLevelChangedEvent(singularity.Level, oldValue, singularity));
        if (singularity.Level <= 0)
            EntityManager.DeleteEntity(singularity.Owner);
    }

    /// <summary>
    /// Alerts the entity hosting the singularity that the level of the singularity has changed without the level actually changing.
    /// Used to sync components when the singularity component is added to an entity.
    /// </summary>
    /// <param name="singularity">The singularity to update the level of.</param>
    public void UpdateSingularityLevel(SharedSingularityComponent singularity)
        => UpdateSingularityLevel(singularity, singularity.Level);

    /// <summary>
    /// Updates the amount of radiation the singularity emits.
    /// </summary>
    /// <param name="singularity">The singularity to update the associated radiation of.</param>
    /// <param name="rads">The radiation source associated with the same entity as the singularity.</param>
    private void UpdateRadiation(SharedSingularityComponent singularity, RadiationSourceComponent? rads = null)
    {
        if(!Resolve(singularity.Owner, ref rads, logMissing: false))
            return;
        rads.Intensity = singularity.Level * singularity.RadsPerLevel;
    }
#region VV
    /// <summary>
    /// VV Setter for <see cref="SharedSingularityComponent.Level"/>
    /// Also sends out an event alerting that the singularities level has changed.
    /// </summary>
    /// <param name="uid">The entity hosting the singularity that is being modified.</param>
    /// <param name="value">The value of the new level the singularity should have.</param>
    /// <param name="comp">The singularity to change the level of.</param>
    private void SetLevel(EntityUid uid, byte value, SharedSingularityComponent? comp)
    {
        if (Resolve(uid, ref comp))
            SetLevel(comp, value);
    }

    /// <summary>
    /// VV Setter for <see cref="SharedSingularityComponent.RadsPerLevel"/>
    /// </summary>
    /// <param name="uid">The entity hosting the singularity that is being modified.</param>
    /// <param name="value">The new amount of radiation the singularity should emit per its level.</param>
    /// <param name="comp">The singularity to change the radioactivity of.</param>
    private void SetRadsPerLevel(EntityUid uid, float value, SharedSingularityComponent? comp)
    {
        if (Resolve(uid, ref comp))
            SetRadsPerLevel(comp, value);
    }
#endregion VV
#endregion Getters/Setters
#region Derivations
    /// <summary>
    /// The scaling factor for the size of a singularities gravity well.
    /// </summary>
    public const float BaseGravityWellRadius = 2f;

    /// <summary>
    /// The scaling factor for the base acceleration of a singularities gravity well.
    /// </summary>
    public const float BaseGravityWellAcceleration = 10f;

    /// <summary>
    /// The level at and above which a singularity should be capable of breaching containment.
    /// </summary>
    public const byte SingularityBreachThreshold = 5;

    /// <summary>
    /// Derives the proper gravity well radius for a singularity from its state.
    /// </summary>
    /// <param name="singulo">A singularity.</param>
    /// <returns>The gravity well radius the singularity should have given its state.</returns>
    public float GravPulseRange(SharedSingularityComponent singulo)
        => BaseGravityWellRadius * (singulo.Level + 1);

    /// <summary>
    /// Derives the proper base gravitational acceleration for a singularity from its state.
    /// </summary>
    /// <param name="singulo">A singularity.</param>
    /// <returns>The base gravitational acceleration the singularity should have given its state.</returns>
    public (float, float) GravPulseAcceleration(SharedSingularityComponent singulo)
        => (BaseGravityWellAcceleration * singulo.Level, 0f);

    /// <summary>
    /// Derives the proper event horizon radius for a singularity from its state.
    /// </summary>
    /// <param name="singulo">A singularity.</param>
    /// <returns>The event horizon radius the singularity should have given its state.</returns>
    public float EventHorizonRadius(SharedSingularityComponent singulo)
        => (float) singulo.Level - 0.5f;

    /// <summary>
    /// Derives whether a singularity should be able to breach containment from its state.
    /// </summary>
    /// <param name="singulo">A singularity.</param>
    /// <returns>Whether the singularity should be able to breach containment.</returns>
    public bool CanBreachContainment(SharedSingularityComponent singulo)
        => singulo.Level >= SingularityBreachThreshold;

    /// <summary>
    /// Derives the proper distortion shader falloff for a singularity from its state.
    /// </summary>
    /// <param name="singulo">A singularity.</param>
    /// <returns>The distortion shader falloff the singularity should have given its state.</returns>
    public float GetFalloff(float level)
    {
        return level switch {
            0 => 9999f,
            1 => MathF.Sqrt(6.4f),
            2 => MathF.Sqrt(7.0f),
            3 => MathF.Sqrt(8.0f),
            4 => MathF.Sqrt(10.0f),
            5 => MathF.Sqrt(12.0f),
            6 => MathF.Sqrt(12.0f),
            _ => -1.0f
        };
    }

    /// <summary>
    /// Derives the proper distortion shader intensity for a singularity from its state.
    /// </summary>
    /// <param name="singulo">A singularity.</param>
    /// <returns>The distortion shader intensity the singularity should have given its state.</returns>
    public float GetIntensity(float level)
    {
        return level switch {
            0 => 0.0f,
            1 => 3645f,
            2 => 103680f,
            3 => 1113920f,
            4 => 16200000f,
            5 => 180000000f,
            6 => 180000000f,
            _ => -1.0f
        };
    }
#endregion Derivations

#region EventHandlers
    /// <summary>
    /// Syncs other components with the state of the singularity via event on startup.
    /// </summary>
    /// <param name="uid">The entity that is becoming a singularity.</param>
    /// <param name="comp">The singularity component that is being added to the entity.</param>
    /// <param name="args">The event arguments.</param>
    private void OnSingularityStartup(EntityUid uid, SharedSingularityComponent comp, ComponentStartup args)
    {
        UpdateSingularityLevel(comp);
    }

    // TODO: Figure out which systems should have control of which coupling.
    /// <summary>
    /// Syncs the radius of an event horizon associated with a singularity that just changed levels.
    /// </summary>
    /// <param name="uid">The entity that the event horizon and singularity are attached to.</param>
    /// <param name="comp">The event horizon associated with the singularity.</param>
    /// <param name="args">The event arguments.</param>
    private void UpdateEventHorizon(EntityUid uid, SharedEventHorizonComponent comp, SingularityLevelChangedEvent args)
    {
        var singulo = args.Singularity;
        _horizons.SetRadius(comp, EventHorizonRadius(singulo), false);
        _horizons.SetCanBreachContainment(comp, CanBreachContainment(singulo), false);
        _horizons.UpdateEventHorizonFixture(comp);
    }

    /// <summary>
    /// Updates the distortion shader associated with a singularity when the singuarity changes levels.
    /// </summary>
    /// <param name="uid">The entity that the distortion shader and singularity are attached to.</param>
    /// <param name="comp">The distortion shader associated with the singularity.</param>
    /// <param name="args">The event arguments.</param>
    private void UpdateDistortion(EntityUid uid, SingularityDistortionComponent comp, SingularityLevelChangedEvent args)
    {
        comp.FalloffPower = GetFalloff(args.NewValue);
        comp.Intensity = GetIntensity(args.NewValue);
    }

    /// <summary>
    /// Updates the state of the physics body associated with a singularity when the singualrity changes levels.
    /// </summary>
    /// <param name="uid">The entity that the physics body and singularity are attached to.</param>
    /// <param name="comp">The physics body associated with the singularity.</param>
    /// <param name="args">The event arguments.</param>
    private void UpdateBody(EntityUid uid, PhysicsComponent comp, SingularityLevelChangedEvent args)
    {
        comp.BodyStatus = (args.NewValue > 1) ? BodyStatus.InAir : BodyStatus.OnGround;
        if (args.NewValue <= 1 && args.OldValue > 1) // Apparently keeps singularities from getting stuck in the corners of containment fields.
            _physics.SetLinearVelocity(comp, Vector2.Zero); // No idea how stopping the singularities movement keeps it from getting stuck though.
    }

    /// <summary>
    /// Updates the appearance of a singularity when the singularities level changes.
    /// </summary>
    /// <param name="uid">The entity that the singularity is attached to.</param>
    /// <param name="comp">The appearance associated with the singularity.</param>
    /// <param name="args">The event arguments.</param>
    private void UpdateAppearance(EntityUid uid, AppearanceComponent comp, SingularityLevelChangedEvent args)
    {
        _visualizer.SetData(uid, SingularityVisuals.Level, args.NewValue, comp);
    }

    /// <summary>
    /// Updates the amount of radiation a singularity emits when the singularities level changes.
    /// </summary>
    /// <param name="uid">The entity that the singularity is attached to.</param>
    /// <param name="comp">The radiation source associated with the singularity.</param>
    /// <param name="args">The event arguments.</param>
    private void UpdateRadiation(EntityUid uid, RadiationSourceComponent comp, SingularityLevelChangedEvent args)
    {
        UpdateRadiation(args.Singularity, comp);
    }

#endregion EventHandlers

}
