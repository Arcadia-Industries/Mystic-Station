using Content.Server.Afk;
using Content.Server.Afk.Events;
using Content.Server.GameTicking;
using Content.Server.Players;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Players;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Roles;

/// <summary>
/// This handles...
/// </summary>
public sealed class RoleTimerSystem : SharedRoleTimerSystem
{
    [Dependency] private readonly IAfkManager _afk = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly RoleTimerManager _roleTimers = default!;

    /// <summary>
    /// Autosave regularly in case of server crash.
    /// </summary>
    private float _autosaveDelay = 900;

    private float _autoSaveAccumulator;

    /// <summary>
    /// If someone just joined track the last time we set their times so the autosave doesn't round up.
    /// </summary>
    private readonly Dictionary<IPlayerSession, TimeSpan> _lastSetTime = new();

    public override void Initialize()
    {
        base.Initialize();

        ConfigManager.OnValueChanged(CCVars.GameRoleTimersSaveFrequency, SetAutosaveDelay, true);
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundEnd);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<AFKEvent>(OnAFK);
        SubscribeLocalEvent<UnAFKEvent>(OnUnAFK);
    }

    private void SetAutosaveDelay(float value) => _autosaveDelay = value;

    public override void Shutdown()
    {
        base.Shutdown();
        ConfigManager.UnsubValueChanged(CCVars.GameRoleTimersSaveFrequency, SetAutosaveDelay);
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
        FullSave();
    }

    private async void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        switch (args.NewStatus)
        {
            case SessionStatus.Connected:
                _roleTimers.CreatePlayer(args.Session.UserId);
                _lastSetTime[args.Session] = _timing.CurTime;
                break;
            case SessionStatus.Disconnected:
                Save(args.Session, _timing.CurTime);
                _lastSetTime.Remove(args.Session);
                _roleTimers.RemovePlayer(args.Session.UserId);
                break;
        }
    }

    private void OnRoundEnd(RoundRestartCleanupEvent ev)
    {
        _autoSaveAccumulator = 0f;
        FullSave();
    }

    public override void Update(float frameTime)
    {
        if (_ticker.RunLevel != GameRunLevel.InRound || _autosaveDelay < 1f)
        {
            _autoSaveAccumulator = 0f;
            return;
        }

        _autoSaveAccumulator += frameTime;

        if (_autoSaveAccumulator < _autosaveDelay) return;

        _autoSaveAccumulator -= _autosaveDelay;
        FullSave();
    }

    // TODO: Mind role events?

    private void OnUnAFK(ref UnAFKEvent ev)
    {
        _lastSetTime[ev.Session] = _timing.CurTime;
    }

    private void OnAFK(ref AFKEvent ev)
    {
        // Don't just write to the DB every time someone goes AFK.
        Save(ev.Session, _timing.CurTime, false);
    }

    private void FullSave()
    {
        Sawmill.Info("Running full save of role timers");
        var currentTime = _timing.CurTime;

        // This is gonna have rounding if someone changes their jobs but it's only 5 minutes anyway, not like we need to track
        // per second values every time they get a new job.
        foreach (var player in Filter.GetAllPlayers())
        {
            var pSession = (IPlayerSession) player;
            if (_afk.IsAfk(pSession)) continue;
            Save(pSession, currentTime);
            _roleTimers.SendRoleTimers(pSession);
        }
    }

    private void Save(IPlayerSession pSession, TimeSpan currentTime, bool dbSave = true)
    {
        if (!_lastSetTime.TryGetValue(pSession, out var lastSave))
        {
            lastSave = currentTime;
        }

        if (currentTime <= lastSave) return;

        var addedTime = currentTime - lastSave;
        var roles = GetRoles(pSession);
        Sawmill.Info($"Adding {addedTime.TotalSeconds:0} seconds to {pSession} playtime");

        foreach (var role in roles)
        {
            _roleTimers.AddTimeToRole(pSession.UserId, role, addedTime, dbSave);
        }

        _roleTimers.AddTimeToOverallPlaytime(pSession.UserId, addedTime, dbSave);
        _lastSetTime[pSession] = currentTime;
    }

    private void OnPlayerAttached(PlayerAttachedEvent ev)
    {
        PlayerRolesChanged(ev.Player);
    }

    private void OnPlayerDetached(PlayerDetachedEvent ev)
    {
        // This doesn't fire if the player doesn't leave their body. I guess it's fine?
        PlayerRolesChanged(ev.Player);
    }

    public bool IsAllowed(NetUserId id, string role)
    {
        if (!ProtoManager.TryIndex<JobPrototype>(role, out var job) ||
            job.Requirements == null ||
            !ConfigManager.GetCVar(CCVars.GameRoleTimers)) return true;

        TimeSpan? overall = null;
        Dictionary<string, TimeSpan>? roles = null;

        foreach (var requirement in job.Requirements)
        {
            var reason = RequirementMet(id, job, requirement, overall, roles);
            if (reason == null) continue;
            return false;
        }

        return true;
    }

    public HashSet<string> GetDisallowedJobs(NetUserId id)
    {
        var roles = new HashSet<string>();
        if (!ConfigManager.GetCVar(CCVars.GameRoleTimers)) return roles;

        TimeSpan? overallPlaytime = null;
        Dictionary<string, TimeSpan>? rolePlaytimes = null;

        foreach (var job in ProtoManager.EnumeratePrototypes<JobPrototype>())
        {
            if (job.Requirements != null)
            {
                foreach (var requirement in job.Requirements)
                {
                    var reason = RequirementMet(id, job, requirement, overallPlaytime, rolePlaytimes);
                    if (reason == null) continue;
                    goto NoRole;
                }
            }

            roles.Add(job.ID);
            NoRole:;
        }

        return roles;
    }

    public void SetAllowedJobs(NetUserId id, ref List<string> jobs)
    {
        if (!ConfigManager.GetCVar(CCVars.GameRoleTimers)) return;

        TimeSpan? overallPlaytime = null;
        Dictionary<string, TimeSpan>? rolePlaytimes = null;

        for (var i = 0; i < jobs.Count; i++)
        {
            var job = jobs[i];

            if (!ProtoManager.TryIndex<JobPrototype>(job, out var jobber) ||
                jobber.Requirements == null ||
                jobber.Requirements.Count == 0) continue;

            foreach (var requirement in jobber.Requirements)
            {
                var reason = RequirementMet(id, jobber, requirement, overallPlaytime, rolePlaytimes);
                if (reason == null) continue;

                jobs.RemoveSwap(i);
                i--;
            }
        }
    }

    private List<string> GetRoles(IPlayerSession session)
    {
        var roles = new List<string>();
        var contentData = _playerManager.GetPlayerData(session.UserId).ContentData();

        if (contentData?.Mind == null) return roles;

        foreach (var mindRole in contentData.Mind.AllRoles)
        {
            roles.Add(mindRole.Name);
        }

        return roles;
    }

    public void PlayerRolesChanged(IPlayerSession player)
    {
        Save(player, _timing.CurTime);
    }

    protected override TimeSpan GetOverallPlaytime(NetUserId id)
    {
        return _roleTimers.GetOverallPlaytime(id).Result;
    }

    protected override Dictionary<string, TimeSpan> GetRolePlaytime(NetUserId id, string role)
    {
        return _roleTimers.GetRolePlaytimes(id).Result;
    }
}
