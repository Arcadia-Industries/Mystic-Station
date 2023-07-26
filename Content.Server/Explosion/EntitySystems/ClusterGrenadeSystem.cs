using Content.Server.Explosion.Components;
using Content.Server.Flash.Components;
using Content.Shared.Explosion;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Throwing;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Content.Server.Weapons.Ranged.Systems;
using System.Numerics;

namespace Content.Server.Explosion.EntitySystems;

public sealed class ClusterGrenadeSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly ThrowingSystem _throwingSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly GunSystem _gun = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClusterGrenadeComponent, ComponentInit>(OnClugInit);
        SubscribeLocalEvent<ClusterGrenadeComponent, ComponentStartup>(OnClugStartup);
        SubscribeLocalEvent<ClusterGrenadeComponent, InteractUsingEvent>(OnClugUsing);
        SubscribeLocalEvent<ClusterGrenadeComponent, TriggerEvent>(OnClugTrigger);
    }

    private void OnClugInit(EntityUid uid, ClusterGrenadeComponent component, ComponentInit args)
    {
        component.GrenadesContainer = _container.EnsureContainer<Container>(uid, "cluster-flash");
    }

    private void OnClugStartup(EntityUid uid, ClusterGrenadeComponent component, ComponentStartup args)
    {
        if (component.FillPrototype != null)
        {
            component.UnspawnedCount = Math.Max(0, component.MaxGrenades - component.GrenadesContainer.ContainedEntities.Count);
            UpdateAppearance(uid, component);
        }
    }

    private void OnClugUsing(EntityUid uid, ClusterGrenadeComponent component, InteractUsingEvent args)
    {
        if (args.Handled) return;

        // TODO: Should use whitelist.
        if (component.GrenadesContainer.ContainedEntities.Count >= component.MaxGrenades ||
            !HasComp<FlashOnTriggerComponent>(args.Used))
            return;

        component.GrenadesContainer.Insert(args.Used);
        UpdateAppearance(uid, component);
        args.Handled = true;
    }

    private void OnClugTrigger(EntityUid uid, ClusterGrenadeComponent component, TriggerEvent args)
    {
        component.CountDown = true;
        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<ClusterGrenadeComponent>();

        while (query.MoveNext(out var uid, out var clug))
        {
            if (clug.CountDown && clug.UnspawnedCount > 0)
            {
                var grenadesInserted = clug.GrenadesContainer.ContainedEntities.Count + clug.UnspawnedCount;
                var thrownCount = 0;
                var segmentAngle = 360 / grenadesInserted;
                var bombletDelay = 0f;

                while (TryGetGrenade(uid, clug, out var grenade))
                {
                    // var distance = random.NextFloat() * _throwDistance;
                    var angleMin = segmentAngle * thrownCount;
                    var angleMax = segmentAngle * (thrownCount + 1);
                    var angle = Angle.FromDegrees(_random.Next(angleMin, angleMax));
                    if (clug.RandomAngle)
                        angle = _random.NextAngle();
                    thrownCount++;

                    if (clug.GrenadeType == "shoot")
                        ShootProjectile(grenade, angle, clug, uid);

                    if (clug.GrenadeType == "throw")
                        ThrowGrenade(grenade, angle, clug);

                    // give an active timer trigger to the contained grenades when they get launched
                    if (clug.TriggerBomblets)
                    {
                        bombletDelay += _random.NextFloat(clug.BombletDelayMin, clug.BombletDelayMax);
                        var bomblet = grenade.EnsureComponent<ActiveTimerTriggerComponent>();
                        bomblet.TimeRemaining = (clug.MinimumDelay + bombletDelay);
                        var ev = new ActiveTimerTriggerEvent(grenade, uid);
                        RaiseLocalEvent(uid, ref ev);
                    }
                }
                // delete the empty shell of the clusterbomb
                EntityManager.DeleteEntity(uid);
            }
        }
    }

    private void ShootProjectile(EntityUid grenade, Angle angle, ClusterGrenadeComponent clug, EntityUid clugUid)
    {
        if (clug.RandomSpread)
            _gun.ShootProjectile(grenade, _random.NextVector2().Normalized(), Vector2.One.Normalized(), clugUid);
        else _gun.ShootProjectile(grenade, angle.ToVec().Normalized(), Vector2.One.Normalized(), clugUid);

    }

    private void ThrowGrenade(EntityUid grenade, Angle angle, ClusterGrenadeComponent clug)
    {
        if (clug.RandomSpread)
            _throwingSystem.TryThrow(grenade, angle.ToVec().Normalized() * _random.NextFloat(clug.MinSpreadDistance, clug.MaxSpreadDistance), clug.Velocity);
        else _throwingSystem.TryThrow(grenade, angle.ToVec().Normalized() * clug.Distance, clug.Velocity);
    }

    private bool TryGetGrenade(EntityUid clugUid, ClusterGrenadeComponent component, out EntityUid grenade)
    {
        grenade = default;

        if (component.UnspawnedCount > 0)
        {
            component.UnspawnedCount--;
            grenade = EntityManager.SpawnEntity(component.FillPrototype, Transform(clugUid).MapPosition);
            return true;
        }

        if (component.GrenadesContainer.ContainedEntities.Count > 0)
        {
            grenade = component.GrenadesContainer.ContainedEntities[0];

            // This shouldn't happen but you never know.
            if (!component.GrenadesContainer.Remove(grenade))
                return false;

            return true;
        }

        return false;
    }

    private void UpdateAppearance(EntityUid uid, ClusterGrenadeComponent component)
    {
        if (!TryComp<AppearanceComponent>(uid, out var appearance)) return;

        _appearance.SetData(uid, ClusterGrenadeVisuals.GrenadesCounter, component.GrenadesContainer.ContainedEntities.Count + component.UnspawnedCount, appearance);
    }
}
