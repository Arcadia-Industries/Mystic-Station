using System.Linq;
using Content.Server.Antag.Components;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Inventory;
using Content.Server.Mind;
using Content.Server.Preferences.Managers;
using Content.Server.Roles;
using Content.Server.Roles.Jobs;
using Content.Server.Shuttles.Components;
using Content.Shared.Antag;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Serialization.Manager;

namespace Content.Server.Antag;

public sealed class NuAntagSelectionSystem : GameRuleSystem<AntagSelectionComponent>
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ISerializationManager _serialization = default!;
    [Dependency] private readonly IServerPreferencesManager _pref = default!;
    [Dependency] private readonly AntagSelectionSystem _antagSelection = default!;
    [Dependency] private readonly ServerInventorySystem _inventory = default!;
    [Dependency] private readonly JobSystem _jobs = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly RoleSystem _role = default!;

    public const float LatejoinRandomChance = 0.5f;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RulePlayerSpawningEvent>(OnPlayerSpawning);
        SubscribeLocalEvent<RulePlayerJobsAssignedEvent>(OnJobsAssigned);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);
    }

    private void OnPlayerSpawning(RulePlayerSpawningEvent args)
    {
        var pool = args.PlayerPool;

        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var comp, out _))
        {
            if (comp.SelectionTime != AntagSelectionTime.PrePlayerSpawn)
                continue;

            if (comp.SelectionsComplete)
                return;

            ChooseAntags((uid, comp), ref pool);
            comp.SelectionsComplete = true;
        }
    }

    private void OnJobsAssigned(RulePlayerJobsAssignedEvent args)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var comp, out _))
        {
            if (comp.SelectionTime != AntagSelectionTime.PostPlayerSpawn)
                continue;

            if (comp.SelectionsComplete)
                return;

            ChooseAntags((uid, comp));
            comp.SelectionsComplete = true;
        }
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (!args.LateJoin)
            return;

        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var antag, out _))
        {
            if (!antag.Definitions.Any(p => p.LateJoinAdditional))
                continue;

            var totalTargetCount = GetTargetAntagCount((uid, antag));
            if (antag.SelectedMinds.Count >= totalTargetCount)
                continue;

            foreach (var def in antag.Definitions)
            {
                if (!def.LateJoinAdditional)
                    continue;

                // don't add latejoin antags before actual selection is done.
                if (!antag.SelectionsComplete)
                    continue;

                // TODO: this really doesn't handle multiple latejoin definitions well
                // eventually this should probably store the players per definition with some kind of unique identifier.
                // something to figure out later.
                if (antag.SelectedMinds.Count >= def.Max)
                    continue;

                if (!RobustRandom.Prob(LatejoinRandomChance))
                    continue;

                MakeAntag((uid, antag), args.Player, def);
            }
        }
    }

    protected override void Added(EntityUid uid, AntagSelectionComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);

        for (var i = 0; i < component.Definitions.Count; i++)
        {
            var def = component.Definitions[i];

            if (def.MinRange != null)
            {
                def.Min = def.MinRange.Value.Next(_random);
            }

            if (def.MaxRange != null)
            {
                def.Max = def.MaxRange.Value.Next(_random);
            }
        }
    }

    protected override void Started(EntityUid uid, AntagSelectionComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (component.SelectionsComplete)
            return;

        if (GameTicker.RunLevel != GameRunLevel.InRound)
            return;

        ChooseAntags((uid, component));
        component.SelectionsComplete = true;
    }

    public void ChooseAntags(Entity<AntagSelectionComponent> ent)
    {
        var sessions = _playerManager.Sessions.ToList();
        ChooseAntags(ent, ref sessions);
    }

    public void ChooseAntags(Entity<AntagSelectionComponent> ent, ref List<ICommonSession> pool)
    {
        foreach (var def in ent.Comp.Definitions)
        {
            ChooseAntags(ent, ref pool, def);
        }
    }

    public void ChooseAntags(Entity<AntagSelectionComponent> ent, ref List<ICommonSession> pool, AntagSelectionDefinition def)
    {
        //TODO: add in an option for having player-less antags.
        // even better, make it a config so you can specify half player, half ghost role.
        var playerPool = GetPlayerPool(ent, pool, def);
        var count = GetTargetAntagCount(ent, playerPool, def);
        for (var i = 0; i < count; i++)
        {
            if (!playerPool.TryPickAndTake(_random, out var session))
                break;

            MakeAntag(ent, session, def);
        }
    }

    public void MakeAntag(Entity<AntagSelectionComponent> ent, ICommonSession? session, AntagSelectionDefinition def)
    {
        var antagEnt = (EntityUid?) null;

        if (session != null)
        {
            ent.Comp.SelectedSessions.Add(session);
            antagEnt = session.AttachedEntity;
        }

        if (!antagEnt.HasValue)
        {
            var getEntEv = new AntagSelectEntityEvent(session, ent);
            RaiseLocalEvent(ent, ref getEntEv, true);

            if (!getEntEv.Handled)
            {
                throw new InvalidOperationException($"Attempted to make {session} antagonist in gamerule {ToPrettyString(ent)} but there was no valid entity for player.");
            }

            antagEnt = getEntEv.Entity;
        }

        if (antagEnt is not { } player)
            return;

        //todo replace this shit down here with sloth's balling methods
        foreach (var (_, entry) in def.Components)
        {
            var comp = (Component) _serialization.CreateCopy(entry.Component, notNullableOverride: true);
            comp.Owner = player; // look im sorry ok this .owner has to live until engine api exists
            EntityManager.RemoveComponent(player, comp.GetType());
            EntityManager.AddComponent(player, comp);
        }

        if (session?.GetMind() is { } mind)
        {
            foreach (var (_, entry) in def.MindComponents)
            {
                var comp = (Component) _serialization.CreateCopy(entry.Component, notNullableOverride: true);
                comp.Owner = mind; // look im sorry ok this .owner has to live until engine api exists
                EntityManager.RemoveComponent(mind, comp.GetType());
                EntityManager.AddComponent(mind, comp);
            }

            ent.Comp.SelectedMinds.Add((mind, Name(player)));
        }

        _inventory.SpawnItemsOnEntity(player, def.Equipment);

        var afterEv = new AfterAntagEntitySelectedEvent(session, player, ent);
        RaiseLocalEvent(ent, ref afterEv, true);

        if (def.Briefing is { } briefing)
        {
            _antagSelection.SendBriefing(session, Loc.GetString(briefing.Text), briefing.Color, briefing.Sound);
        }
    }

    public AntagSelectionPlayerPool GetPlayerPool(Entity<AntagSelectionComponent> ent, List<ICommonSession> sessions, AntagSelectionDefinition def)
    {
        var primaryList = new List<ICommonSession>();
        var secondaryList = new List<ICommonSession>();
        var fallbackList = new List<ICommonSession>();
        var rawList = new List<ICommonSession>(sessions);
        foreach (var session in sessions)
        {
            if (!IsSessionValid(ent, session, def))
                continue;

            if (!IsEntityValid(session.AttachedEntity, def))
                continue;

            var pref = (HumanoidCharacterProfile) _pref.GetPreferences(session.UserId).SelectedCharacter;
            if (def.PrefRoles.Count == 0 || pref.AntagPreferences.Any(p => def.PrefRoles.Contains(p)))
            {
                primaryList.Add(session);
            }
            else if (def.PrefRoles.Count == 0 || pref.AntagPreferences.Any(p => def.FallbackRoles.Contains(p)))
            {
                secondaryList.Add(session);
            }
            else
            {
                fallbackList.Add(session);
            }

            rawList.Remove(session);
        }

        return new AntagSelectionPlayerPool(primaryList, secondaryList, fallbackList, rawList);
    }

    public int GetTargetAntagCount(Entity<AntagSelectionComponent> ent, AntagSelectionPlayerPool? pool = null)
    {
        var count = 0;
        foreach (var def in ent.Comp.Definitions)
        {
            count += GetTargetAntagCount(ent, pool, def);
        }

        return count;
    }

    public int GetTargetAntagCount(Entity<AntagSelectionComponent> ent, AntagSelectionPlayerPool? pool, AntagSelectionDefinition def)
    {
        var poolSize = pool?.Count ?? _playerManager.Sessions.Length;
        // factor in other definitions' affect on the count.
        var countOffset = 0;
        foreach (var otherDef in ent.Comp.Definitions)
        {
            countOffset += Math.Clamp(poolSize / otherDef.PlayerRatio, otherDef.Min, otherDef.Max) * otherDef.PlayerRatio;
        }
        // make sure we don't double-count the current selection
        countOffset -= Math.Clamp((poolSize + countOffset) / def.PlayerRatio, def.Min, def.Max) * def.PlayerRatio;

        return Math.Clamp((poolSize - countOffset) / def.PlayerRatio, def.Min, def.Max);
    }

    public bool IsSessionValid(Entity<AntagSelectionComponent> ent, ICommonSession session, AntagSelectionDefinition def, EntityUid? mind = null)
    {
        mind ??= session.GetMind();

        if (session.Status is SessionStatus.Disconnected or SessionStatus.Zombie)
            return false;

        if (ent.Comp.SelectedSessions.Contains(session))
            return false;

        //todo: we need some way to check that we're not getting the same role twice. (double picking thieves of zombies through midrounds)

        switch (def.MultiAntagSetting)
        {
            case AntagAcceptability.None:
            {
                if (_role.MindIsAntagonist(mind))
                    return false;
                break;
            }
            case AntagAcceptability.NotExclusive:
            {
                if (_role.MindIsExclusiveAntagonist(mind))
                    return false;
                break;
            }
        }

        // todo: expand this to allow for more fine antag-selection logic for game rules.
        if (!_jobs.CanBeAntag(session))
            return false;

        return true;
    }

    public bool IsEntityValid(EntityUid? entity, AntagSelectionDefinition def)
    {
        if (entity == null)
            return false;

        if (HasComp<PendingClockInComponent>(entity))
            return false;

        if (!def.AllowNonHumans && !HasComp<HumanoidAppearanceComponent>(entity))
            return false;

        if (def.Whitelist != null)
        {
            if (!def.Whitelist.IsValid(entity.Value, EntityManager))
                return false;
        }

        if (def.Blacklist != null)
        {
            if (def.Blacklist.IsValid(entity.Value, EntityManager))
                return false;
        }

        return true;
    }

    public List<(EntityUid, SessionData, string)> GetAntagSessionData(Entity<AntagSelectionComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return new List<(EntityUid, SessionData, string)>();

        var output = new List<(EntityUid, SessionData, string)>();
        foreach (var (mind, name) in ent.Comp.SelectedMinds)
        {
            if (!TryComp<MindComponent>(mind, out var mindComp) || mindComp.OriginalOwnerUserId == null)
                continue;

            if (!_playerManager.TryGetPlayerData(mindComp.OriginalOwnerUserId.Value, out var data))
                continue;

            output.Add((mind, data, name));
        }
        return output;
    }

    public List<Entity<MindComponent>> GetAntagMinds(Entity<AntagSelectionComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return new();

        var output = new List<Entity<MindComponent>>();
        foreach (var (mind, _) in ent.Comp.SelectedMinds)
        {
            if (!TryComp<MindComponent>(mind, out var mindComp) || mindComp.OriginalOwnerUserId == null)
                continue;

            output.Add((mind, mindComp));
        }
        return output;
    }

    public List<EntityUid> GetAntagMindUids(Entity<AntagSelectionComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return new();

        return ent.Comp.SelectedMinds.Select(p => p.Item1).ToList();
    }
}

/// <summary>
/// Event raised on an entity
/// </summary>
[ByRefEvent]
public record struct AntagSelectEntityEvent(ICommonSession? Session, Entity<AntagSelectionComponent> GameRule)
{
    public readonly ICommonSession? Session = Session;

    public bool Handled => Entity != null;

    public EntityUid? Entity;
}

/// <summary>
/// Event raised on an entity
/// </summary>
[ByRefEvent]
public readonly record struct AfterAntagEntitySelectedEvent(ICommonSession? Session, EntityUid EntityUid, Entity<AntagSelectionComponent> GameRule);
