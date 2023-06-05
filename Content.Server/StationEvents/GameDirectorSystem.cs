using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Ghost.Components;
using Content.Server.StationEvents.Components;
using Content.Server.StationEvents.Metric;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Content.Shared.Prototypes;
using JetBrains.Annotations;
using Robust.Server.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.StationEvents;

public sealed class RankedEvent
{
    public PossibleEvent PossibleEvent;
    public ChaosMetrics Result;
    public float Score;

    public RankedEvent(PossibleEvent possibleEvent, ChaosMetrics result, float score)
    {
        PossibleEvent = possibleEvent;
        Result = result;
        Score = score;
    }
}

public sealed class PlayerCount
{
    public int Players;
    public int Ghosts;
}

/// <summary>
///     A scheduler which tries to keep station chaos within a set bound over time with the most suitable
///        good or bad events to nudge it in the correct direction.
/// </summary>
[UsedImplicitly]
public sealed class GameDirectorSystem : GameRuleSystem<GameDirectorComponent>
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly EventManagerSystem _event = default!;
    [Dependency] private readonly StationMetricSystem _metrics = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IComponentFactory _factory = default!;
    [Dependency] public readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly IChatManager _chat = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _log.GetSawmill("game_rule");
    }

    protected override void Added(EntityUid uid, GameDirectorComponent scheduler, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        // This deletes all existing metrics and sets them up again.
        _metrics.SetupMetrics();
        SetupEvents(scheduler, CountActivePlayers());
        CopyStories(uid, scheduler);
        ValidateStories(scheduler);
        LogMessage($"Started, first event in {scheduler.TimeUntilNextEvent} seconds");
    }

    private void SetupEvents(GameDirectorComponent scheduler, PlayerCount count)
    {
        scheduler.PossibleEvents.Clear();
        foreach (var proto in _prototypeManager.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.Abstract)
                continue;

            if (!proto.HasComponent<GameRuleComponent>(_factory))
                continue;

            if (!proto.TryGetComponent<StationEventComponent>(out var stationEvent, _factory))
                continue;

            // Gate here on players, but not on round runtime. The story will probably last long enough for the
            // event to be ready to run again, we'll check CanRun again before we actually launch the event.
            if (!_event.CanRun(proto, stationEvent, count.Players, TimeSpan.MaxValue))
                continue;

            scheduler.PossibleEvents.Add(new PossibleEvent(proto.ID, stationEvent.Chaos));
        }
    }

    protected override void Ended(EntityUid uid, GameDirectorComponent scheduler, GameRuleComponent gameRule,
        GameRuleEndedEvent args)
    {
        scheduler.TimeUntilNextEvent = BasicStationEventSchedulerComponent.MinimumTimeUntilFirstEvent;
    }

    protected override void ActiveTick(EntityUid uid, GameDirectorComponent scheduler, GameRuleComponent gameRule, float frameTime)
    {
        scheduler.BeatTime += frameTime;
        if (scheduler.TimeUntilNextEvent > 0)
        {
            scheduler.TimeUntilNextEvent -= frameTime;
            return;
        }

        // Somewhat expensive process of invoking the metric systems which iterate through entities to determine
        //   how bad things currently are on the station.
        ChaosMetrics chaos;
        var count = CountActivePlayers();

        chaos = _metrics.CalculateChaos();
        scheduler.CurrChaos = chaos;

        // Decide what story beat to work with (which sets chaos goals)
        var beat = DetermineNextBeat(scheduler, chaos, count);

        // Pick the best events (which move the station towards the chaos desired by the beat)
        var bestEvents = ChooseEvents(scheduler, beat, chaos, count);

        // Run the best event here, if we have any to pick from.
        if (bestEvents.Count > 0)
        {
            LogMessage($"Chaos is: {chaos}");
            // Sorts the possible events and then picks semi-randomly.
            // when beat.RandomEventLimit is 1 it's always the "best" event picked. Higher values
            // allow more events to be randomly selected.
            var chosenEvent = SelectBest(bestEvents, beat.RandomEventLimit);

            _event.RunNamedEvent(chosenEvent.PossibleEvent.PrototypeId);

            // Don't select this event again for the current story (when SetupEvents is called again)
            //   Commented out this code as we don't have enough events for this strategy yet.
            // scheduler.PossibleEvents.Remove(chosenEvent.PossibleEvent);

            // 2 - 6 minutes until the next event is considered.
            scheduler.TimeUntilNextEvent = _random.NextFloat(120f, 360f);
        }
        else
        {
            // No events were run. Consider again in 30 seconds.
            LogMessage($"Chaos is: {chaos} (No events ran)", false);
            scheduler.TimeUntilNextEvent = 30f;
        }
    }

    // Count the active players and ghosts on the server.
    //  Players gates which stories and events are available
    //  Ghosts can be used to gate certain events (which require ghosts to occur)
    private PlayerCount CountActivePlayers()
    {
        var allPlayers = _playerManager.ServerSessions.ToList();
        var count = new PlayerCount();
        foreach (var player in allPlayers)
        {
            // TODO: A
            if (player.AttachedEntity != null)
            {
                if (HasComp<HumanoidAppearanceComponent>(player.AttachedEntity))
                {
                    count.Players += 1;
                }
                else if (HasComp<GhostComponent>(player.AttachedEntity))
                {
                    count.Ghosts += 1;
                }
            }
        }

        return count;
    }

    // Sorts the possible events and then picks semi-randomly.
    // when maxRandom is 1 it's always the "best" event picked. Higher values allow more events to be randomly selected.
    protected RankedEvent SelectBest(List<RankedEvent> bestEvents, int maxRandom)
    {
        var ranked =bestEvents.OrderBy(ev => ev.Score).Take(maxRandom).ToList();

        var events = String.Join(", ", ranked.Select(r => r.PossibleEvent.PrototypeId));

        foreach (var rankedEvent in ranked)
        {
            // I'd like a nice weighted random here but I'm lazy
            if (_random.Prob(0.7f))
            {
                // Pick this event
                LogMessage( $"Picked {rankedEvent.PossibleEvent.PrototypeId} from best events (in sequence) {events}");
                return rankedEvent;
            }
        }

        // Random dropped through all, just take best.
        LogMessage( $"Picked {ranked[0].PossibleEvent.PrototypeId} from best events (in sequence) {events}");
        return ranked[0];
    }

    private void LogMessage(string message, bool showChat=true)
    {
        _adminLogger.Add(LogType.GameDirector, showChat?LogImpact.Medium:LogImpact.High, $"{message}");
        if (showChat)
        {
            _chat.SendAdminAnnouncement("GameDirector "+ message);
        }

    }
    // Returns the StoryBeat that should be currently used to select events.
    // Advances the current story and picks new stories when the current beat is complete.
    private StoryBeat DetermineNextBeat(GameDirectorComponent scheduler, ChaosMetrics chaos, PlayerCount count)
    {
        // Potentially Complete CurrBeat, which is always scheduler.CurrStory[0]
        if (scheduler.CurrStory.Count > 0)
        {
            var beatName = scheduler.CurrStory[0];
            var beat = scheduler.StoryBeats[beatName];

            if (scheduler.BeatTime > beat.MaxSecs)
            {
                // Done with this beat (it's lasted too long)
                _sawmill.Info($"StoryBeat {beatName} complete. It's lasted {scheduler.BeatTime} out of a maximum of {beat.MaxSecs} seconds.");
            }
            else if (scheduler.BeatTime > beat.MinSecs)
            {
                // Determine if we meet the chaos thresholds to exit this beat
                if (!beat.EndIfAnyWorse.Empty && chaos.AnyWorseThan(beat.EndIfAnyWorse))
                {
                    // Done with this beat (chaos exceeded set bad level)
                    _sawmill.Info($"StoryBeat {beatName} complete. Chaos exceeds {beat.EndIfAnyWorse} (EndIfAnyWorse).");
                }
                else if(!beat.EndIfAllBetter.Empty && chaos.AllBetterThan(beat.EndIfAllBetter))
                {
                    // Done with this beat (chaos reached set good level)
                    _sawmill.Info($"StoryBeat {beatName} complete. Chaos better than {beat.EndIfAllBetter} (EndIfAllBetter).");
                }
                else
                {
                    return beat;
                }
            }
            else
            {
                return beat;
            }

            // If we didn't return by here, we are done with this beat.
            scheduler.CurrStory.RemoveAt(0);
        }
        scheduler.BeatTime = 0.0f;

        // Advance in the current story
        if (scheduler.CurrStory.Count > 0)
        {
            // Return the next beat in the current story.
            var beatName = scheduler.CurrStory[0];
            var beat = scheduler.StoryBeats[beatName];

            LogMessage($"New StoryBeat {beatName}: {beat.Description}. Goal is {beat.Goal}");
            return beat;
        }

        // Need to find a new story. Pick a random one which meets our needs.
        var stories = scheduler.Stories.Keys.ToList();
        _random.Shuffle(stories);

        foreach (var storyName in stories)
        {
            var story = scheduler.Stories[storyName];
            if (story.MinPlayers > count.Players || story.MaxPlayers < count.Players)
            {
                continue;
            }

            // A new story was picked. Copy the full list of beats (for us to pop beats from the front as we proceed)
            scheduler.CurrStory = story.Beats.ShallowClone();
            scheduler.CurrStoryName = storyName;
            SetupEvents(scheduler, count);
            _sawmill.Info($"New Story {storyName}: {story.Description}. {scheduler.PossibleEvents.Count} events to use.");

            var beatName = scheduler.CurrStory[0];
            var beat = scheduler.StoryBeats[beatName];

            LogMessage($"First StoryBeat {beatName}: {beat.Description}. Goal is {beat.Goal}");
            return beat;
        }

        // Just use the fallback beat when no stories were found. That beat does exist, right!?
        scheduler.CurrStory.Add(scheduler.FallbackBeatName);
        return scheduler.StoryBeats[scheduler.FallbackBeatName];
    }

    private float RankChaosDelta(ChaosMetrics chaos)
    {
        // Just a sum of squares (trying to get close to 0 on every score)
        //   Lower is better
        return chaos.ChaosDict.Values.Sum(v => (float)(v) * (float)(v));
    }

    private List<RankedEvent> ChooseEvents(GameDirectorComponent scheduler, StoryBeat beat, ChaosMetrics chaos, PlayerCount count)
    {
        // TODO : Potentially filter Chaos here using CriticalLevels & DangerLevels which force us to focus on
        //        big problems (lots of hostiles, spacing) prior to smaller ones (food & drink)
        var desiredChange = beat.Goal.ExclusiveSubtract(chaos);
        var result = FilterAndScore(scheduler, chaos, desiredChange, count);

        if (result.Count > 0)
        {
            return result;
        }

        // Fall back to improving all scores (not just the ones the beat is focused on)
        //   Generally this means reducing chaos (unspecified scores are desired to be 0).
        var allDesiredChange = beat.Goal - chaos;
        result = FilterAndScore(scheduler, chaos, allDesiredChange, count, inclNoChaos:true);

        return result;
    }

    // Filter only to events which improve the chaos score in alignment with desiredChange.
    //   Score them (lower is better) in how well they do this.
    private List<RankedEvent> FilterAndScore(GameDirectorComponent scheduler, ChaosMetrics chaos,
        ChaosMetrics desiredChange, PlayerCount count, bool inclNoChaos = false)
    {
        var noEvent = RankChaosDelta(desiredChange);
        var result = new List<RankedEvent>();

        // Choose an event that specifically achieves chaos goals, focusing only on them.
        foreach (var possibleEvent in scheduler.PossibleEvents)
        {
            // How much of the relevant chaos will be left after this event has occurred
            var relevantChaosDelta = desiredChange.ExclusiveSubtract(possibleEvent.Chaos);
            var rank = RankChaosDelta(relevantChaosDelta);

            var allChaosAfter = chaos + possibleEvent.Chaos;

            // Some events have no chaos score assigned. Treat them as if they change nothing and mix them in for flavor.
            var noChaosEvent = inclNoChaos && possibleEvent.Chaos.Empty;

            if (rank < noEvent || noChaosEvent)
            {
                // Look up this event's prototype and check it is ready to run.
                var proto = _prototypeManager.Index<EntityPrototype>(possibleEvent.PrototypeId);

                if (!proto.TryGetComponent<StationEventComponent>(out var stationEvent, _factory))
                    continue;

                if (!_event.CanRun(proto, stationEvent, count.Players, _gameTicker.RoundDuration()))
                    continue;

                result.Add(new RankedEvent(possibleEvent, allChaosAfter, rank));
            }
        }

        return result;
    }

    private void CopyStories(EntityUid uid, GameDirectorComponent scheduler)
    {
        if (TryComp<GameStoriesComponent>(uid, out var stories))
        {
            // There are some stories (probably from a prototype subclass) to copy across
            foreach (var storyBeat in stories.StoryBeats)
            {
                // Overwrite our values with those from the stories component
                // TODO: Which of the two to favor when there are conflicts might need some thought.
                scheduler.StoryBeats[storyBeat.Key] = storyBeat.Value;
            }

            if (stories.OverwriteStories)
            {
                // Overwrite the default stories with the provided ones.
                scheduler.Stories = stories.Stories;
            }
            else
            {
                foreach (var story in stories.Stories)
                {
                    // Overwrite our values with those from the stories component
                    // TODO: Which of the two to favor when there are conflicts might need some thought.
                    scheduler.Stories[story.Key] = story.Value;
                }
            }
        }
    }

    // Check that the stories have a valid configuration.
    private void ValidateStories(GameDirectorComponent scheduler)
    {
        if (scheduler.Stories.Count == 0)
        {
            _sawmill.Warning($"No stories found in GameDirector");
        }
        if (scheduler.StoryBeats.Count == 0)
        {
            _sawmill.Warning($"No storyBeats found in GameDirector");
        }
        if (!scheduler.StoryBeats.ContainsKey(scheduler.FallbackBeatName))
        {
            _sawmill.Warning($"Fallback storyBeat {scheduler.FallbackBeatName} not found in GameDirector");
        }
        foreach (var story in scheduler.Stories)
        {
            foreach (var beat in story.Value.Beats)
            {
                if (!scheduler.StoryBeats.ContainsKey(beat))
                {
                    _sawmill.Warning($"Missing StoryBeat {beat} referenced in Story '{story.Key}'");
                }
            }
        }

        foreach (var beat in scheduler.StoryBeats)
        {
            if (beat.Value.Goal.Empty)
            {
                _sawmill.Warning($"StoryBeat {beat.Key} has no goal Chaos configured.");
            }
        }
    }

}
