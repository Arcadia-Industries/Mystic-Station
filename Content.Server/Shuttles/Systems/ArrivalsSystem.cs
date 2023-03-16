using System.Linq;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Shuttles.Components;
using Content.Server.Spawners.Components;
using Content.Server.Spawners.EntitySystems;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.CCVar;
using Content.Shared.Spawners.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Shuttles.Systems;

/// <summary>
/// If enabled spawns players on a separate arrivals station before they can transfer to the main station.
/// </summary>
public sealed class ArrivalsSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfgManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly ShuttleSystem _shuttles = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;
    [Dependency] private readonly StationSystem _station = default!;

    /// <summary>
    /// If enabled then spawns players on an alternate map so they can take a shuttle to the station.
    /// </summary>
    private bool _enabled;

    // TODO: CVar
    /// <summary>
    /// Also need to factor in FTL time.
    /// </summary>
    private TimeSpan TransferCooldown = TimeSpan.FromSeconds(90);

    // TODO: CVar
    private ResourcePath _arrivalsStation = new("/Maps/centcomm.yml");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawningEvent>(OnPlayerSpawn, before: new []{typeof(SpawnPointSystem)});
        SubscribeLocalEvent<StationArrivalsComponent, ComponentStartup>(OnArrivalsStartup);
        SubscribeLocalEvent<ArrivalsShuttleComponent, EntityUnpausedEvent>(OnShuttleUnpaused);
        SubscribeLocalEvent<StationInitializedEvent>(OnStationInit);
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);

        // Don't invoke immediately as it will get set in the natural course of things.
        _enabled = _cfgManager.GetCVar(CCVars.ArrivalsShuttles);
        _cfgManager.OnValueChanged(CCVars.ArrivalsShuttles, SetArrivals);
    }

    private void OnStationInit(StationInitializedEvent ev)
    {
        EnsureComp<StationArrivalsComponent>(ev.Station);
    }

    private void OnPlayerSpawn(PlayerSpawningEvent ev)
    {
        // Only works on latejoin even if enabled.
        if (!_enabled || _ticker.RunLevel != GameRunLevel.InRound)
            return;

        var points = EntityQuery<SpawnPointComponent, TransformComponent>().ToList();
        _random.Shuffle(points);
        TryGetArrivals(out var arrivals);

        if (TryComp<TransformComponent>(arrivals, out var arrivalsXform))
        {
            var mapId = arrivalsXform.MapID;

            foreach (var (spawnPoint, xform) in points)
            {
                if (spawnPoint.SpawnType != SpawnPointType.LateJoin || xform.MapID != mapId)
                    continue;

                ev.SpawnResult = _stationSpawning.SpawnPlayerMob(
                    xform.Coordinates,
                    ev.Job,
                    ev.HumanoidCharacterProfile,
                    ev.Station);

                return;

            }
        }
    }

    private void OnShuttleUnpaused(EntityUid uid, ArrivalsShuttleComponent component, ref EntityUnpausedEvent args)
    {
        component.NextTransfer += args.PausedTime;
    }

    private bool TryGetArrivals(out EntityUid uid)
    {
        var arrivalsQuery = EntityQueryEnumerator<ArrivalsSourceComponent>();

        while (arrivalsQuery.MoveNext(out uid, out _))
        {
            return true;
        }

        return false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ArrivalsShuttleComponent, ShuttleComponent, TransformComponent>();
        var curTime = _timing.CurTime;
        TryGetArrivals(out var arrivals);

        // TODO: Stop dispatch if emergency shuttle has arrived.
        // TODO: Need server join message specifying shuttle wait time or smth.
        // TODO: Need maps
        // TODO: Need some kind of comp to shunt people off if they try to get on?
        if (TryComp<TransformComponent>(arrivals, out var arrivalsXform))
        {
            while (query.MoveNext(out var comp, out var shuttle, out var xform))
            {
                if (comp.NextTransfer > curTime || !TryComp<StationDataComponent>(comp.Station, out var data))
                    continue;

                // Go back to arrivals source
                if (xform.MapUid != arrivalsXform.MapUid)
                {
                    if (arrivals.IsValid())
                        _shuttles.FTLTravel(shuttle, arrivals, dock: true);
                }
                // Go to station
                else
                {
                    var targetGrid = _station.GetLargestGrid(data);

                    if (targetGrid != null)
                        _shuttles.FTLTravel(shuttle, targetGrid.Value, dock: true);
                }

                comp.NextTransfer += TransferCooldown;
            }
        }
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        // Setup arrivals station
        if (!_enabled)
            return;

        SetupArrivalsStation();
    }

    private void SetupArrivalsStation()
    {
        var mapId = _mapManager.CreateMap();

        if (!_loader.TryLoad(mapId, _arrivalsStation.ToString(), out var uids))
        {
            return;
        }

        foreach (var id in uids)
        {
            EnsureComp<ArrivalsSourceComponent>(id);
        }

        // Handle roundstart stations.
        var query = AllEntityQuery<StationArrivalsComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            SetupShuttle(uid, comp);
        }
    }

    private void SetArrivals(bool obj)
    {
        _enabled = obj;

        if (_enabled)
        {
            SetupArrivalsStation();
            var query = AllEntityQuery<StationArrivalsComponent>();

            while (query.MoveNext(out var sUid, out var comp))
            {
                SetupShuttle(sUid, comp);
            }
        }
        else
        {
            var sourceQuery = AllEntityQuery<ArrivalsSourceComponent>();

            while (sourceQuery.MoveNext(out var uid, out _))
            {
                QueueDel(uid);
            }

            var shuttleQuery = AllEntityQuery<ArrivalsShuttleComponent>();

            while (shuttleQuery.MoveNext(out var uid, out _))
            {
                QueueDel(uid);
            }
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _cfgManager.UnsubValueChanged(CCVars.ArrivalsShuttles, SetArrivals);
    }

    private void OnArrivalsStartup(EntityUid uid, StationArrivalsComponent component, ComponentStartup args)
    {
        if (!_enabled)
            return;

        // If it's a latespawn station then this will fail but that's okey
        SetupShuttle(uid, component);
    }

    private void SetupShuttle(EntityUid uid, StationArrivalsComponent component)
    {
        if (!Deleted(component.Shuttle))
            return;

        // Spawn arrivals on a dummy map then dock it to the source.
        var dummyMap = _mapManager.CreateMap();

        if (TryGetArrivals(out var arrivals) &&
            _loader.TryLoad(dummyMap, component.ShuttlePath.ToString(), out var shuttleUids))
        {
            component.Shuttle = shuttleUids[0];
            var shuttleComp = Comp<ShuttleComponent>(component.Shuttle);
            var arrivalsComp = EnsureComp<ArrivalsShuttleComponent>(component.Shuttle);
            arrivalsComp.Station = uid;
            _shuttles.FTLTravel(shuttleComp, arrivals, hyperspaceTime: 10f, dock: true);
            arrivalsComp.NextTransfer = _timing.CurTime + TransferCooldown;
        }

        // Don't start the arrivals shuttle immediately docked so power has a time to stabilise?
        var timer = AddComp<TimedDespawnComponent>(_mapManager.GetMapEntityId(dummyMap));
        timer.Lifetime = 15f;
    }
}
