using Content.Server.EUI;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Server.StationRecords;
using Content.Shared.CrewManifest;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Player;
using Robust.Shared.Players;

namespace Content.Server.CrewManifest;

public sealed class CrewManifestSystem : EntitySystem
{
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly StationRecordsSystem _recordsSystem = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly EuiManager _euiManager = default!;

    /// <summary>
    ///     Cached crew manifest entries. The alternative is to outright
    ///     rebuild the crew manifest every time the state is requested:
    ///     this is inefficient.
    /// </summary>
    private readonly Dictionary<EntityUid, CrewManifestEntries> _cachedEntries = new();

    private readonly Dictionary<EntityUid, Dictionary<IPlayerSession, CrewManifestEui>> _openEuis = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<AfterGeneralRecordCreatedEvent>(AfterGeneralRecordCreated);
        SubscribeLocalEvent<RecordModifiedEvent>(OnRecordModified);
        SubscribeLocalEvent<CrewManifestViewerComponent, BoundUIClosedEvent>(OnBoundUiClose);
        SubscribeLocalEvent<CrewManifestViewerComponent, CrewManifestOpenUiMessage>(OpenEuiFromBui);
        SubscribeNetworkEvent<RequestCrewManifestMessage>(OnRequestCrewManifest);
    }

    // As a result of this implementation, the crew manifest does not refresh when somebody
    // joins the game, or if their title is modified. Equally, this should never be opened
    // without ensuring that whatever parent window comes from it also closes, as otherwise
    // this will never close on its own.
    private void OnRequestCrewManifest(RequestCrewManifestMessage message, EntitySessionEventArgs args)
    {

        if (args.SenderSession is not IPlayerSession sessionCast)
        {
            return;
        }

        OpenEui(message.Id, sessionCast);
    }

    // Not a big fan of this one. Rebuilds the crew manifest every time
    // somebody spawns in, meaning that at round start, it rebuilds the crew manifest
    // wrt the amount of players readied up.
    private void AfterGeneralRecordCreated(AfterGeneralRecordCreatedEvent ev)
    {
        BuildCrewManifest(ev.Key.OriginStation);
        UpdateEuis(ev.Key.OriginStation);
    }

    private void OnRecordModified(RecordModifiedEvent ev)
    {
        BuildCrewManifest(ev.Key.OriginStation);
        UpdateEuis(ev.Key.OriginStation);
    }

    private void OnBoundUiClose(EntityUid uid, CrewManifestViewerComponent component, BoundUIClosedEvent ev)
    {
         var owningStation = _stationSystem.GetOwningStation(uid);
         if (owningStation == null || ev.Session is not IPlayerSession sessionCast)
         {
             return;
         }

         CloseEui(owningStation.Value, sessionCast, uid);
    }

    public (string name, CrewManifestEntries? entries) GetCrewManifest(EntityUid station)
    {
        var valid = _cachedEntries.TryGetValue(station, out var manifest);
        return (valid ? MetaData(station).EntityName : string.Empty, valid ? manifest : null);
    }

    private void UpdateEuis(EntityUid station)
    {
        if (_openEuis.TryGetValue(station, out var euis))
        {
            foreach (var eui in euis.Values)
            {
                eui.StateDirty();
            }
        }
    }

    private void OpenEuiFromBui(EntityUid uid, CrewManifestViewerComponent component, CrewManifestOpenUiMessage msg)
    {
        var owningStation = _stationSystem.GetOwningStation(uid);
        if (owningStation == null || msg.Session is not IPlayerSession sessionCast)
        {
            return;
        }

        OpenEui(owningStation.Value, sessionCast, uid);
    }

    public void OpenEui(EntityUid station, IPlayerSession session, EntityUid? owner = null)
    {
        if (!HasComp<StationRecordsComponent>(station))
        {
            return;
        }

        if (!_openEuis.TryGetValue(station, out var euis))
        {
            euis = new();
        }

        if (euis.ContainsKey(session))
        {
            return;
        }

        var eui = new CrewManifestEui(station, owner, this);
        euis.Add(session, eui);

        _euiManager.OpenEui(eui, session);
        eui.StateDirty();
    }

    public void CloseEui(EntityUid station, IPlayerSession session, EntityUid? owner = null)
    {
        if (!HasComp<StationRecordsComponent>(station))
        {
            return;
        }

        if (!_openEuis.TryGetValue(station, out var euis)
            || !euis.TryGetValue(session, out var eui))
        {
            return;
        }

        if (eui.Owner == owner)
        {
            eui.Close();
            euis.Remove(session);
        }

        if (euis.Count == 0)
        {
            _openEuis.Remove(station);
        }
    }

    /// <summary>
    ///     Builds the crew manifest for a station. Stores it in the cache afterwards.
    /// </summary>
    /// <param name="station"></param>
    private void BuildCrewManifest(EntityUid station)
    {
        var iter = _recordsSystem.GetRecordsOfType<GeneralStationRecord>(station);

        if (iter == null)
        {
            return;
        }

        var entries = new CrewManifestEntries();

        foreach (var recordObject in iter)
        {
            if (recordObject == null)
            {
                continue;
            }

            var record = recordObject.Value.Item2;
            var entry = new CrewManifestEntry(record.Name, record.JobTitle, record.JobIcon, record.DisplayPriority);

            foreach (var department in record.Departments)
            {
                if (!entries.Entries.TryGetValue(department, out var entryList))
                {
                    entryList = new();
                    entries.Entries.Add(department, entryList);
                }

                entryList.Add(entry);
            }
        }

        if (_cachedEntries.ContainsKey(station))
        {
            _cachedEntries[station] = entries;
        }
        else
        {
            _cachedEntries.Add(station, entries);
        }
    }
}
