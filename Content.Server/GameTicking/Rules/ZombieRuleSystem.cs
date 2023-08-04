using System.Globalization;
using System.Linq;
using Content.Server.Actions;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Mind;
using Content.Server.Mind.Components;
using Content.Server.Players;
using Content.Server.Popups;
using Content.Server.Preferences.Managers;
using Content.Server.Roles;
using Content.Server.RoundEnd;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.Zombies;
using Content.Shared.Actions.ActionTypes;
using Content.Shared.CCVar;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Zombies;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.GameTicking.Rules;

public sealed class ZombieRuleSystem : GameRuleSystem<ZombieRuleComponent>
{
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly RoundEndSystem _roundEnd = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly ActionsSystem _action = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly ZombieSystem _zombie = default!;
    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartAttemptEvent>(OnStartAttempt);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEndText);
        SubscribeLocalEvent<ZombifyOnDeathComponent, ZombifySelfActionEvent>(OnZombifySelf);
    }

    private void OnUnpause(EntityUid uid, ZombieRuleComponent component, ref EntityUnpausedEvent args)
    {
        if (component.FirstTurnAllowed != null)
            component.FirstTurnAllowed = (TimeSpan)component.FirstTurnAllowed + args.PausedTime;
        if (component.InfectInitialAt != null)
            component.InfectInitialAt = (TimeSpan)component.InfectInitialAt + args.PausedTime;
    }

    private void OnRoundEndText(RoundEndTextAppendEvent ev)
    {
        var healthy = GetHealthyHumans();

        // This is just the general condition thing used for determining the win/lose text
        var fraction = GetInfectedFraction();

        if (fraction <= 0)
            ev.AddLine(Loc.GetString("zombie-round-end-amount-none"));
        else if (fraction <= 0.25)
            ev.AddLine(Loc.GetString("zombie-round-end-amount-low"));
        else if (fraction <= 0.5)
            ev.AddLine(Loc.GetString("zombie-round-end-amount-medium", ("percent", Math.Round((fraction * 100), 2).ToString(CultureInfo.InvariantCulture))));
        else if (fraction < 1)
            ev.AddLine(Loc.GetString("zombie-round-end-amount-high", ("percent", Math.Round((fraction * 100), 2).ToString(CultureInfo.InvariantCulture))));
        else
            ev.AddLine(Loc.GetString("zombie-round-end-amount-all"));

        int infectedNames = 0;
        foreach (var zombie in EntityQuery<ZombieRuleComponent>())
        {
            ev.AddLine(Loc.GetString("zombie-round-end-initial-count",
                ("initialCount", zombie.InitialInfectedNames.Count)));
            foreach (var player in zombie.InitialInfectedNames)
            {
                ev.AddLine(Loc.GetString("zombie-round-end-user-was-initial",
                    ("name", player.Key),
                    ("username", player.Value)));
            }

            infectedNames += zombie.InitialInfectedNames.Count;
        }

        // Gets a bunch of the living players and displays them if they're under a threshold.
        // InitialInfected is used for the threshold because it scales with the player count well.
        if (healthy.Count > 0 && healthy.Count <= 2 * infectedNames)
        {
            ev.AddLine("");
            ev.AddLine(Loc.GetString("zombie-round-end-survivor-count", ("count", healthy.Count)));
            foreach (var survivor in healthy)
            {
                var meta = MetaData(survivor);
                var username = string.Empty;
                    if (TryComp<MindContainerComponent>(survivor, out var mindcomp))
                    if (mindcomp.Mind != null && mindcomp.Mind.Session != null)
                        username = mindcomp.Mind.Session.Name;

                ev.AddLine(Loc.GetString("zombie-round-end-user-was-survivor",
                    ("name", meta.EntityName),
                    ("username", username)));
            }
        }

    }
    /// <summary>
    ///     The big kahoona function for checking if the round is gonna end
    /// </summary>
    private void CheckRoundEnd(EntityUid target)
    {
        // We only care about players, not monkeys and such.
        if (!HasComp<HumanoidAppearanceComponent>(target))
            return;

        var query = EntityQueryEnumerator<ZombieRuleComponent, GameRuleComponent>();

        var fraction = 0.0f;
        var healthyCount = -1;
        while (query.MoveNext(out var uid, out var zombies, out var gameRule))
        {
            if (!GameTicker.IsGameRuleActive(uid, gameRule))
                continue;

            if (healthyCount == -1)
            {
                // Code run in the first relevant zombie rule, though there might be many of them.

                fraction = GetInfectedFraction();
                healthyCount = CountHealthyHumans();

                if (healthyCount == 1)
                {
                    // Only one human left. spooky
                    var healthy = GetHealthyHumans();
                    _popup.PopupEntity(Loc.GetString("zombie-alone"), healthy[0], healthy[0]);
                }

                if (healthyCount == 0) // Oops, all zombies
                    _roundEndSystem.EndRound();

                if (zombies.ShuttleCalls.Count > 0 && fraction >= zombies.ShuttleCalls[0])
                {
                    // Call shuttle if not called
                    zombies.ShuttleCalls.RemoveAt(0);
                    _roundEndSystem.RequestRoundEnd(uid);
                }
            }

            if (!zombies.ForcedZombies && fraction > zombies.ForceZombiesAt)
            {
                zombies.ForcedZombies = true;
                _initialZombie.ForceZombies(uid, zombies);
            }

            CheckRuleEnd(uid, zombies, gameRule);
        }
    }

    // See if the zombie infection controlled by this rule has completely died out. End rule if it has.
    public void CheckRuleEnd(EntityUid ruleUid, ZombieRuleComponent? zombies = null, GameRuleComponent? gameRule = null)
    {
        if (!Resolve(ruleUid, ref zombies, ref gameRule))
            return;

        // Check that we've picked our zombies
        if (zombies.InfectInitialAt != null)
            return;

        // Look for initial infected, pending or living zombies.
        // Any of those mean: leave rule running
        var livingQuery = GetEntityQuery<LivingZombieComponent>();
        var initialQuery = GetEntityQuery<InitialInfectedComponent>();
        var pendingQuery = GetEntityQuery<PendingZombieComponent>();
        int deadZombies = 0;
        var zombers = EntityQueryEnumerator<ZombieComponent>();
        while (zombers.MoveNext(out var uid, out var zombie))
        {
            if (zombie.Family.Rules != ruleUid)
                continue;

            if (livingQuery.HasComponent(uid))
            {
                // Living zombie. Done
                return;
            }
            else if (initialQuery.HasComponent(uid))
            {
                // Living pre-zombie player, done
                return;
            }
            else if (pendingQuery.HasComponent(uid))
            {
                // Zombie waiting to turn, done
                return;
            }
            // Else this zombie is dead (and belongs to this rule)
            deadZombies += 1;
        }

        // If we reached here then there were no current or future zombies in this rule.
        GameTicker.EndGameRule(ruleUid, gameRule);

        if (zombies.WinEndsRoundAbove < 1.0f)
        {
            if (zombies.WinEndsRoundAbove <= 0.0f)
            {
                // Human victory (skip the check)
                _roundEndSystem.EndRound();
            }
            else
            {
                // This fraction is only counting zombies from the current outbreak. See how much they outnumber
                // the living.
                var healthyCount = CountHealthyHumans();
                var fraction = (float)deadZombies / (float)(deadZombies + healthyCount);
                if (fraction > zombies.WinEndsRoundAbove)
                {
                    // Human victory
                    _roundEndSystem.EndRound();
                }
            }
        }

    }

    private bool HaveEnoughPlayers(EntityUid uid, StationEventComponent? stationEvent = null)
    {
        if (!Resolve(uid, ref stationEvent))
            return false;

        var living = CountHealthyHumans();
        var players = _playerManager.PlayerCount;

        return (Math.Min(living, players) >= stationEvent.MinimumPlayers);
    }

    protected override void Started(EntityUid uid, ZombieRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        var curTime = _timing.CurTime;

        component.StartTime = _timing.CurTime + _random.Next(component.MinStartDelay, component.MaxStartDelay);
        component.InfectInitialAt = curTime + TimeSpan.FromSeconds(component.InitialInfectDelaySecs);

        if (component.EarlySettings.EmoteSoundsId != null)
        {
            _prototypeManager.TryIndex(component.EarlySettings.EmoteSoundsId, out component.EarlySettings.EmoteSounds);
        }
        if (component.VictimSettings.EmoteSoundsId != null)
        {
            _prototypeManager.TryIndex(component.VictimSettings.EmoteSoundsId, out component.VictimSettings.EmoteSounds);
        }
    }

    protected override void ActiveTick(EntityUid uid, ZombieRuleComponent component, GameRuleComponent gameRule,
        float frameTime)
    {
        var curTime = _timing.CurTime;
        if (component.InfectInitialAt != null && component.InfectInitialAt < curTime)
        {
            if (HaveEnoughPlayers(uid))
            {
                // Time to infect the initial players
                InfectInitialPlayers(uid, component);
                component.InfectInitialAt = null;
            }
            else
            {
                // Wait 2 additional minutes for more players.
                component.InfectInitialAt = curTime + TimeSpan.FromMinutes(2);
            }
        }

        if (component.FirstTurnAllowed != null && component.FirstTurnAllowed < curTime)
        {
            // Shouldn't usually be necessary but a good forcing function especially if an admin uses VV on the
            // rules to decrease the FirstTurnAllowed time.
            _initialZombie.ActivateZombifyOnDeath(uid, component);
            component.FirstTurnAllowed = null;
        }
    }

    private float GetInfectedFraction()
    {
        var players = EntityQuery<HumanoidAppearanceComponent>(true);
        var zombers = EntityQuery<HumanoidAppearanceComponent, ZombieComponent>(true);

        return zombers.Count() / (float) players.Count();
    }

    /// <summary>
    /// Gets the list of humans who are alive, not zombies, and are on a station.
    /// Flying off via a shuttle disqualifies you.
    /// </summary>
    /// <returns></returns>
    private List<EntityUid> GetHealthyHumans(bool includeOffStation = true)
    {
        var healthy = new List<EntityUid>();

        var stationGrids = new HashSet<EntityUid>();
        if (!includeOffStation)
        {
            foreach (var station in _station.GetStationsSet())
            {
                if (TryComp<StationDataComponent>(station, out var data) && _station.GetLargestGrid(data) is { } grid)
                    stationGrids.Add(grid);
            }
        }

        var players = AllEntityQuery<HumanoidAppearanceComponent, ActorComponent, MobStateComponent, TransformComponent>();
        var zombers = GetEntityQuery<LivingZombieComponent>();
        while (players.MoveNext(out var uid, out _, out _, out var mob, out var xform))
        {
            if (!_mobState.IsAlive(uid, mob))
                continue;

            if (zombers.HasComponent(uid))
                continue;

            if (!includeOffStation && !stationGrids.Contains(xform.GridUid ?? EntityUid.Invalid))
                continue;

            healthy.Add(uid);
        }
        return healthy;
    }

    private int CountHealthyHumans()
    {
        var healthy = 0;
        var players = AllEntityQuery<HumanoidAppearanceComponent, MobStateComponent>();
        var zombers = GetEntityQuery<LivingZombieComponent>();
        while (players.MoveNext(out var uid, out _, out var mob))
        {
            if (_mobState.IsAlive(uid, mob) && !zombers.HasComponent(uid))
            {
                healthy += 1;
            }
        }
        return healthy;
    }

    public void AddToInfectedList(EntityUid uid, ZombieComponent zombie, ZombieRuleComponent rules, MindContainerComponent? mindComponent = null)
    {
        if (!Resolve(uid, ref mindComponent))
            return;

        var mind = mindComponent.Mind;
        if (mind?.Session != null && mind.OwnedEntity != null)
        {
            var inCharacterName = MetaData(mind.OwnedEntity.Value).EntityName;
            rules.InitialInfectedNames.Add(inCharacterName, mind.Session.Name);
        }
    }

    /// <summary>
    ///     Infects the first players with the passive zombie virus.
    ///     Also records their names for the end of round screen.
    /// </summary>
    /// <remarks>
    ///     The reason this code is written separately is to facilitate
    ///     allowing this gamemode to be started midround. As such, it doesn't need
    ///     any information besides just running.
    /// </remarks>
    private void InfectInitialPlayers(EntityUid uid, ZombieRuleComponent rules)
    {

        var allPlayers = _playerManager.ServerSessions.ToList();
        var playerList = new List<IPlayerSession>();
        var prefList = new List<IPlayerSession>();

        foreach (var player in allPlayers)
        {
            // TODO: A
            if (player.AttachedEntity != null && HasComp<HumanoidAppearanceComponent>(player.AttachedEntity))
            {
                if (TryComp<ZombieComponent>(player.AttachedEntity, out var zombie))
                {
                    // This player is already a zombie. If they don't already have a rule, add them to this one.
                    if (zombie.Family.Rules == EntityUid.Invalid)
                    {
                        zombie.Family.Rules = uid;
                        zombie.Settings = rules.EarlySettings;
                        zombie.VictimSettings = rules.VictimSettings;

                        var mind = player.Data.ContentData()?.Mind;
                        if (mind?.Session != null)
                        {
                            rules.InitialInfectedNames[zombie.BeforeZombifiedEntityName] = mind.Session.Name;
                        }
                    }
                }
                else
                {
                playerList.Add(player);

                var pref = (HumanoidCharacterProfile) _prefs.GetPreferences(player.UserId).SelectedCharacter;
                    if (pref.AntagPreferences.Contains(rules.PatientZeroPrototypeID))
                        prefList.Add(player);
                }
            }
        }

        // Check for mindless zombies (not attached to players) that still don't have a rules entity attached...
        var zombers = EntityQueryEnumerator<ZombieComponent, LivingZombieComponent>();
        while (zombers.MoveNext(out var zombUid, out var zombie, out var living))
        {
            if (zombie.Family.Rules == EntityUid.Invalid)
            {
                // Note that we add this zombie to the new rules, but we don't count them towards the num infected. They
                // are not inhabited by a player. Were we to count them, we should probably also check they are still
                // alive.
                zombie.Family.Rules = uid;
                zombie.Settings = rules.EarlySettings;
                zombie.VictimSettings = rules.VictimSettings;
            }
        }

        if (playerList.Count == 0)
            return;

        var numInfected = (int)Math.Clamp(
            Math.Floor((double) playerList.Count / rules.PlayersPerInfected),
            1, rules.MaxInitialInfected);

        // These are already infected (by admins probably)
        numInfected -= rules.InitialInfectedNames.Count;

        var curTime = _timing.CurTime;
        rules.FirstTurnAllowed ??= curTime + TimeSpan.FromSeconds(rules.TurnTimeMin);

        //   Varies randomly from 20 to 30 minutes. After this the virus begins and they start
        var groupTimelimit = _random.NextFloat(rules.MinZombieForceSecs, rules.MaxZombieForceSecs);
        var totalInfected = 0;
        while (totalInfected < numInfected)
        {
            IPlayerSession zombie;
            if (prefList.Count == 0)
            {
                if (playerList.Count == 0)
                {
                    Log.Info("Insufficient number of players. stopping selection.");
                    break;
                }
                zombie = _random.Pick(playerList);
                Log.Info("Insufficient preferred patient 0, picking at random.");
            }
            else
            {
                zombie = _random.Pick(prefList);
                Log.Info("Selected a patient 0.");
            }

            prefList.Remove(zombie);
            playerList.Remove(zombie);
            if (zombie.Data.ContentData()?.Mind is not { } mind || mind.OwnedEntity is not { } ownedEntity)
                continue;

            totalInfected++;

            _mindSystem.AddRole(mind, new ZombieRole(mind, _prototypeManager.Index<AntagPrototype>(rules.PatientZeroPrototypeID)));

            var inCharacterName = string.Empty;
            // Create some variation between the times of each zombie, relative to the time of the group as a whole.
            var personalDelay = _random.NextFloat(0.0f, rules.PlayerZombieForceVariationSecs);
            _initialZombie.AddInitialInfecton(
                mind.OwnedEntity.Value,
                rules.FirstTurnAllowed ?? TimeSpan.Zero,
                // Only take damage after this many seconds
                curTime + TimeSpan.FromSeconds(groupTimelimit + personalDelay));

            var zombie = EnsureComp<ZombieComponent>(mind.OwnedEntity.Value);
            // Patient zero zombies get one set of zombie settings, later zombies get a different (less powerful) set.
            zombie.Settings = rules.EarlySettings;
            zombie.VictimSettings = rules.VictimSettings;
            zombie.Family = new ZombieFamily() { Rules = uid, Generation = 0 };

            inCharacterName = MetaData(mind.OwnedEntity.Value).EntityName;
            EnsureComp<IncurableZombieComponent>(ownedEntity);

            if (mind.Session != null)
            {
                var message = Loc.GetString("zombie-patientzero-role-greeting");
                var wrappedMessage = Loc.GetString("chat-manager-server-wrap-message", ("message", message));

                //gets the names now in case the players leave.
                rules.InitialInfectedNames[inCharacterName] = mind.Session.Name;

                // I went all the way to ChatManager.cs and all i got was this lousy T-shirt
                // You got a free T-shirt!?!?
                _chatManager.ChatMessageToOne(Shared.Chat.ChatChannel.Server, message,
                   wrappedMessage, default, false, mind.Session.ConnectedClient, Color.Plum);

                // Notify player about new role assignment with a sound effect
                _audioSystem.PlayGlobal(component.InitialInfectedSound, mind.Session);
            }
        }
    }

    public (EntityUid, ZombieRuleComponent?) FindActiveRule()
    {
        var query = EntityQueryEnumerator<ZombieRuleComponent, GameRuleComponent>();

        while (query.MoveNext(out var uid, out var zombies, out var gameRule))
        {
            if (!GameTicker.IsGameRuleActive(uid, gameRule))
                continue;

            return (uid, zombies);
        }

        return (EntityUid.Invalid, null);
    }
}
