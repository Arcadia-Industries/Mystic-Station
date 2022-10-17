using Content.Server.Radiation.Components;
using Content.Server.Radiation.Events;
using Content.Shared.Hands;
using Content.Shared.Inventory.Events;
using Content.Shared.Radiation.Components;
using Content.Shared.Radiation.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server.Radiation.Systems;

public sealed class GeigerSystem : SharedGeigerSystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private static readonly float ApproxEqual = 0.01f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeigerComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<GeigerComponent, GotEquippedHandEvent>(OnEquippedHand);
        SubscribeLocalEvent<GeigerComponent, GotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<GeigerComponent, GotUnequippedHandEvent>(OnUnequippedHand);

        SubscribeLocalEvent<GeigerComponent, ComponentGetState>(OnGetState);
        SubscribeLocalEvent<OnRadiationSystemUpdated>(OnUpdate);
    }

    private void OnEquipped(EntityUid uid, GeigerComponent component, GotEquippedEvent args)
    {
        SetUser(uid, component, args.Equipee);
    }

    private void OnEquippedHand(EntityUid uid, GeigerComponent component, GotEquippedHandEvent args)
    {
        SetUser(uid, component, args.User);
    }

    private void OnUnequipped(EntityUid uid, GeigerComponent component, GotUnequippedEvent args)
    {
        SetUser(uid, component, null);
    }

    private void OnUnequippedHand(EntityUid uid, GeigerComponent component, GotUnequippedHandEvent args)
    {
        SetUser(uid, component, null);
    }

    private void OnUpdate(OnRadiationSystemUpdated ev)
    {
        var query = EntityQuery<GeigerComponent, RadiationReceiverComponent>();
        foreach (var (geiger, receiver) in query)
        {
            var rads = receiver.CurrentRadiation;
            SetCurrentRadiation(geiger.Owner, geiger, rads);
        }
    }

    private void SetCurrentRadiation(EntityUid uid, SharedGeigerComponent component, float rads)
    {
        // check that it's approx equal
        if (MathF.Abs(component.CurrentRadiation - rads) < ApproxEqual)
            return;

        var curLevel = component.DangerLevel;
        var newLevel = RadsToLevel(rads);

        component.CurrentRadiation = rads;
        component.DangerLevel = newLevel;

        if (curLevel != newLevel)
        {
            UpdateAppearance(uid, component);
            UpdateGeigerSound(uid, component);
        }

        Dirty(component);
    }

    private void SetUser(EntityUid uid, SharedGeigerComponent component, EntityUid? user)
    {
        if (component.User == user)
            return;

        component.User = user;
        UpdateGeigerSound(uid, component);
        Dirty(component);
    }

    private void OnGetState(EntityUid uid, SharedGeigerComponent component, ref ComponentGetState args)
    {
        args.State = new GeigerComponentState
        {
            CurrentRadiation = component.CurrentRadiation,
            DangerLevel = component.DangerLevel,
            Equipee = component.User
        };
    }

    private void UpdateAppearance(EntityUid uid, SharedGeigerComponent? component = null,
        AppearanceComponent? appearance = null)
    {
        if (!Resolve(uid, ref component, ref appearance, false))
            return;

        _appearance.SetData(uid, GeigerVisuals.DangerLevel, component.DangerLevel, appearance);
    }

    private void UpdateGeigerSound(EntityUid uid, SharedGeigerComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        component.Stream?.Stop();

        if (component.User == null)
            return;
        if (!component.Sounds.TryGetValue(component.DangerLevel, out var sounds))
            return;

        var sound = _audio.GetSound(sounds);
        var param = sounds.Params.WithLoop(true).WithVolume(-4f)
            .WithPlayOffset(_random.NextFloat(0.0f, 100.0f));
        component.Stream = _audio.Play(sound, Filter.Entities(component.User.Value), uid, param);
    }

    public static GeigerDangerLevel RadsToLevel(float rads)
    {
        return rads switch
        {
            < 0.2f => GeigerDangerLevel.None,
            < 1f => GeigerDangerLevel.Low,
            < 3f => GeigerDangerLevel.Med,
            < 6f => GeigerDangerLevel.High,
            _ => GeigerDangerLevel.Extreme
        };
    }
}


