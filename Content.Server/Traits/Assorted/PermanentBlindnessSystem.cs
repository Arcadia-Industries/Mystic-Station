﻿using Content.Shared.Examine;
using Content.Shared.Eye.Blinding;
using Content.Shared.IdentityManagement;

namespace Content.Server.Traits.Assorted;

/// <summary>
/// This handles...
/// </summary>
public sealed class PermanentBlindnessSystem : EntitySystem
{
    [Dependency] private readonly SharedBlindingSystem _blinding = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<PermanentBlindnessComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<PermanentBlindnessComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<PermanentBlindnessComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, PermanentBlindnessComponent component, ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
        {
            args.PushMarkup(Loc.GetString("permanent-blindness-trait-examined", ("target", Identity.Entity(uid, EntityManager))));
        }
    }

    private void OnShutdown(EntityUid uid, PermanentBlindnessComponent component, ComponentShutdown args)
    {
        _blinding.AdjustBlindSources(uid, false);
    }

    private void OnStartup(EntityUid uid, PermanentBlindnessComponent component, ComponentStartup args)
    {
        _blinding.AdjustBlindSources(uid, true);
    }
}
