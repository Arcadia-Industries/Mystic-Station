using Content.Server.StationRecords.Systems;
using Content.Shared.CriminalRecords;
using Content.Shared.Security;
using Content.Shared.StationRecords;

namespace Content.Server.CriminalRecords.Systems;

/// <summary>
///     Criminal records
///
///     Criminal Records inherit Station Records' core and add roleplaying tools for Security:
///         - Ability to track a person's status (Detained/Wanted/None)
///         - See security officers' actions in Criminal Records in the radio
///         - See reasons for any action with no need to ask the officer personally
/// </summary>
public sealed class CriminalRecordsSystem : EntitySystem
{
    [Dependency] private readonly StationRecordsSystem _stationRecordsSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AfterGeneralRecordCreatedEvent>(OnGeneralRecordCreated);
    }

    private void OnGeneralRecordCreated(AfterGeneralRecordCreatedEvent ev)
    {

        var record = new GeneralCriminalRecord()
        {
            Status = SecurityStatus.None,
            Reason = string.Empty
        };

        _stationRecordsSystem.AddRecordEntry(ev.Key, record);
        _stationRecordsSystem.Synchronize(ev.Key.OriginStation);
    }

    public bool TryChangeStatus(EntityUid station, StationRecordKey key, SecurityStatus status,
        out SecurityStatus updatedStatus, string? reason)
    {
        updatedStatus = default;

        if (!_stationRecordsSystem.TryGetRecord(station, key, out GeneralCriminalRecord? record)
            || status == record.Status)
            return false;

        record.Reason = (status == SecurityStatus.None ? string.Empty : reason)!;
        record.Status = status;

        updatedStatus = record.Status;

        _stationRecordsSystem.Synchronize(station);

        return true;
    }

    public bool TryArrest(EntityUid station, StationRecordKey key, out SecurityStatus updatedStatus, string? reason)
    {
        updatedStatus = default;

        return TryChangeStatus(station, key, SecurityStatus.Detained, out updatedStatus, reason)
               || TryChangeStatus(station, key, SecurityStatus.None, out updatedStatus, reason);
    }
}
