using System.Linq;
using Content.Server.Fax;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.GameTicking;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.Fugitive;
using Content.Server.Mind.Components;
using Content.Server.NPC.Systems;
using Content.Server.Mind;
using Content.Server.Objectives;
using Content.Server.Objectives.Interfaces;
using Content.Server.Roles;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Server.Fugitive;

public sealed class FugitiveSystem: EntitySystem
{
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly FaxSystem _faxSystem = default!;
    [Dependency] private readonly FactionSystem _faction = default!;
    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly IObjectivesManager _objectivesManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] protected readonly GameTicker GameTicker = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FugitiveComponent, MindAddedMessage>(OnMindAdded);
    }

    /// <summary>
    ///   When a mind is added to a fugitive, send wanted notices to all fax machines on all stations.
    /// </summary>
    private void OnMindAdded(EntityUid entity, FugitiveComponent component, MindAddedMessage args)
    {
        if (component.WantedNoticeSent)
            return;

        AddFugitiveRole(entity);

        var stations = _stationSystem.GetStations();
        foreach (var station in stations)
        {
            SendWantedNotices(station, entity);
        }

        component.WantedNoticeSent = true;

    }

    /// <summary>
    ///   Add the fugitive role and escape objective to the entity.
    /// </summary>
    private void AddFugitiveRole(EntityUid entity)
    {
        if (!TryComp<MindContainerComponent>(entity, out var mindContainerComponent) || mindContainerComponent.Mind == null)
            return;

        var traitorRule = EntityQuery<TraitorRuleComponent>().FirstOrDefault();
        if (traitorRule == null)
        {
            GameTicker.StartGameRule("Traitor", out var ruleEntity);
            traitorRule = Comp<TraitorRuleComponent>(ruleEntity);
        }

        // Prepare antagonist role
        var antagPrototype = _prototypeManager.Index<AntagPrototype>(traitorRule.TraitorPrototypeId);
        var mind = mindContainerComponent.Mind;
        var traitorRole = new TraitorRole(mind, antagPrototype);

        // Assign traitor role
        _mindSystem.AddRole(mind, traitorRole);
        traitorRule.Traitors.Add(traitorRole);

        // Change the faction
        _faction.RemoveFaction(entity, "NanoTrasen", false);
        _faction.AddFaction(entity, "Syndicate");

        // Add escape objective
        _prototypeManager.TryIndex<ObjectivePrototype>("EscapeShuttleObjective", out var objective);
        if (objective == null)
            return;
        _mindSystem.TryAddObjective(traitorRole.Mind, objective);
    }

    /// <summary>
    ///    Send wanted notices to all fax machines on the station.
    /// </summary>
    private void SendWantedNotices(EntityUid station, EntityUid uid)
    {
        if (!HasComp<StationDataComponent>(station))
            return;

        var faxes = EntityQueryEnumerator<FaxMachineComponent>();
        var meta = MetaData(uid);

        while (faxes.MoveNext(out var faxEnt, out var fax))
        {
            var printout = new FaxPrintout(
                Loc.GetString("fugitive-wanted-notice-wanted") + " " + meta.EntityName + "\n\n" + Loc.GetString("fugitive-wanted-notice-description-line1") + "\n\n" + Loc.GetString("fugitive-wanted-notice-description-line2") + "\n\n" + Loc.GetString("fugitive-wanted-notice-description-line3"),
                Loc.GetString("fugitive-wanted-notice-wanted") + " " + meta.EntityName,
                null,
                "paper_stamp-cent",
                new() { Loc.GetString("stamp-component-stamped-name-centcom") });
            _faxSystem.Receive(faxEnt, printout, null, fax);
        }
    }
}
