using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Chemistry.EntitySystems;
using Content.Server.Chemistry.ReactionEffects;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Forensics;
using Content.Server.HealthExaminable;
using Content.Server.Medical.Bloodstream.Components;
using Content.Server.Popups;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Drunk;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Medical.Wounds.Components;
using Content.Shared.Medical.Wounds.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Shared.Speech.EntitySystems;

namespace Content.Server.Medical.Bloodstream.Systems;

public sealed class BloodstreamSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly PuddleSystem _puddleSystem = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
    [Dependency] private readonly SharedDrunkSystem _drunkSystem = default!;
    [Dependency] private readonly SolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private readonly SharedStutteringSystem _stutteringSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BloodstreamComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<BloodstreamComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<BloodstreamComponent, HealthBeingExaminedEvent>(OnHealthBeingExamined);
        SubscribeLocalEvent<BloodstreamComponent, BeingGibbedEvent>(OnBeingGibbed);
        SubscribeLocalEvent<BloodstreamComponent, ApplyMetabolicMultiplierEvent>(OnApplyMetabolicMultiplier);
        SubscribeLocalEvent<BloodstreamComponent, ReactionAttemptEvent>(OnReactionAttempt);
        SubscribeLocalEvent<BloodstreamComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<BloodstreamComponent, WoundAddedEvent>(OnWoundAdded);
        SubscribeLocalEvent<BloodstreamComponent, WoundSeverityChangedEvent>(OnWoundSeverityUpdate);
        SubscribeLocalEvent<BloodstreamComponent, WoundCauterizedEvent>(OnWoundCauterizeUpdate);
    }

    private void OnWoundCauterizeUpdate(EntityUid uid, BloodstreamComponent component, ref WoundCauterizedEvent args)
    {
        if (!TryComp(args.WoundEntity, out BleedInflicterComponent? bleed))
            return;

        if (args.OldState)
        {
            //Add bleeding back if we are reopening a wound
            TryModifyBleedAmount(uid, bleed.Bloodloss*args.WoundComponent.Severity, component);
        }
        else
        {
            //Remove bleeding if we are cauterizing
            TryModifyBleedAmount(uid, - bleed.Bloodloss*args.WoundComponent.Severity, component);
        }
    }

    private void OnWoundSeverityUpdate(EntityUid uid, BloodstreamComponent component, ref WoundSeverityChangedEvent args)
    {
        if (!TryComp(args.WoundEntity, out BleedInflicterComponent? bleed) || args.WoundComponent.CanBleed)
            return;
        var severityDelta = args.WoundComponent.Severity - args.OldSeverity;
        var bleedDelta = severityDelta * bleed.Bloodloss;
        TryModifyBleedAmount(uid, bleedDelta, component);
    }

    private void OnWoundAdded(EntityUid uid, BloodstreamComponent component, ref WoundAddedEvent args)
    {
        if (!TryComp(args.WoundEntity, out BleedInflicterComponent? bleed) ||  args.WoundComponent.CanBleed)
            return;
        TryModifyBleedAmount(uid, bleed.Bloodloss*args.WoundComponent.Severity, component);
    }

    private void OnReactionAttempt(EntityUid uid, BloodstreamComponent component, ReactionAttemptEvent args)
    {
        if (args.Solution.Name != BloodstreamComponent.DefaultBloodSolutionName
            && args.Solution.Name != BloodstreamComponent.DefaultChemicalsSolutionName
            && args.Solution.Name != BloodstreamComponent.DefaultBloodTemporarySolutionName)
            return;

        foreach (var effect in args.Reaction.Effects)
        {
            switch (effect)
            {
                case CreateEntityReactionEffect: // Prevent entities from spawning in the bloodstream
                case AreaReactionEffect: // No spontaneous smoke or foam leaking out of blood vessels.
                    args.Cancel();
                    return;
            }
        }

        // The area-reaction effect canceling is part of avoiding smoke-fork-bombs (create two smoke bombs, that when
        // ingested by mobs create more smoke). This also used to act as a rapid chemical-purge, because all the
        // reagents would get carried away by the smoke/foam. This does still work for the stomach (I guess people vomit
        // up the smoke or spawned entities?).

        // TODO apply organ damage instead of just blocking the reaction?
        // Having cheese-clots form in your veins can't be good for you.
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<BloodstreamComponent>();
        while (query.MoveNext(out var uid, out var bloodstream))
        {
            bloodstream.AccumulatedFrametime += frameTime;

            if (bloodstream.AccumulatedFrametime < bloodstream.UpdateInterval)
                continue;

            bloodstream.AccumulatedFrametime -= bloodstream.UpdateInterval;

            // Adds blood to their blood level if it is below the maximum; Blood regeneration. Must be alive.
            if (bloodstream.BloodSolution.Volume < bloodstream.BloodSolution.MaxVolume && _mobStateSystem.IsAlive(uid))
                TryModifyBloodLevel(uid, bloodstream.BloodRefreshAmount, bloodstream);

            // Removes blood from the bloodstream based on bleed amount (bleed rate)
            // as well as stop their bleeding to a certain extent.
            if (bloodstream.BleedAmount > 0)
            {
                TryModifyBloodLevel(uid, (-bloodstream.BleedAmount) / 10, bloodstream);
                TryModifyBleedAmount(uid, -bloodstream.BleedReductionAmount, bloodstream);
            }

            // deal bloodloss damage if their blood level is below a threshold.
            var bloodPercentage = GetBloodLevelPercentage(uid, bloodstream);
            if (bloodPercentage < bloodstream.BloodlossThreshold && _mobStateSystem.IsAlive(uid))
            {
                // bloodloss damage is based on the base value, and modified by how low your blood level is.
                var amt = bloodstream.BloodlossDamage / (0.1f + bloodPercentage);

                _damageableSystem.TryChangeDamage(uid, amt, true, false);

                // Apply dizziness as a symptom of bloodloss.
                // The effect is applied in a way that it will never be cleared without being healthy.
                // Multiplying by 2 is arbitrary but works for this case, it just prevents the time from running out
                _drunkSystem.TryApplyDrunkenness(uid, bloodstream.UpdateInterval*2, false);
                _stutteringSystem.DoStutter(uid, TimeSpan.FromSeconds(bloodstream.UpdateInterval*2), false);

                // storing the drunk and stutter time so we can remove it independently from other effects additions
                bloodstream.StatusTime += bloodstream.UpdateInterval * 2;
            }
            else if (_mobStateSystem.IsAlive(uid))
            {
                // If they're healthy, we'll try and heal some bloodloss instead.
                _damageableSystem.TryChangeDamage(uid, bloodstream.BloodlossHealDamage * bloodPercentage, true, false);

                // Remove the drunk effect when healthy. Should only remove the amount of drunk and stutter added by low blood level
                _drunkSystem.TryRemoveDrunkenessTime(uid, bloodstream.StatusTime);
                _stutteringSystem.DoRemoveStutterTime(uid, bloodstream.StatusTime);
                // Reset the drunk and stutter time to zero
                bloodstream.StatusTime = 0;
            }
        }
    }

    private void OnComponentInit(EntityUid uid, BloodstreamComponent component, ComponentInit args)
    {
        component.ChemicalSolution = _solutionContainerSystem.EnsureSolution(uid, BloodstreamComponent.DefaultChemicalsSolutionName);
        component.BloodSolution = _solutionContainerSystem.EnsureSolution(uid, BloodstreamComponent.DefaultBloodSolutionName);
        component.BloodTemporarySolution = _solutionContainerSystem.EnsureSolution(uid, BloodstreamComponent.DefaultBloodTemporarySolutionName);

        component.ChemicalSolution.MaxVolume = component.ChemicalMaxVolume;
        component.BloodSolution.MaxVolume = component.BloodMaxVolume;
        component.BloodTemporarySolution.MaxVolume = component.BleedPuddleThreshold * 4; // give some leeway, for chemstream as well

        // Fill blood solution with BLOOD
        _solutionContainerSystem.TryAddReagent(uid, component.BloodSolution, component.BloodReagent,
            component.BloodMaxVolume, out _);
    }

    private void OnDamageChanged(EntityUid uid, BloodstreamComponent component, DamageChangedEvent args)
    {
        if (args.DamageDelta is null)
            return;

        // definitely don't make them bleed if they got healed
        if (!args.DamageIncreased)
            return;

        // TODO probably cache this or something. humans get hurt a lot
        if (!_prototypeManager.TryIndex<DamageModifierSetPrototype>(component.DamageBleedModifiers, out var modifiers))
            return;

        var bloodloss = DamageSpecifier.ApplyModifierSet(args.DamageDelta, modifiers);

        if (bloodloss.Empty)
            return;

        var oldBleedAmount = component.BleedAmount;
        var total = bloodloss.Total;
        var totalFloat = total.Float();
        TryModifyBleedAmount(uid, totalFloat, component);

        var prob = Math.Clamp(totalFloat / 50, 0, 1);
        var healPopupProb = Math.Clamp(Math.Abs(totalFloat) / 25, 0, 1);
        if (totalFloat > 0 && _robustRandom.Prob(prob))
        {
            TryModifyBloodLevel(uid, (-total) / 5, component);
            _audio.PlayPvs(component.InstantBloodSound, uid);
        }
        else if (totalFloat < 0 && oldBleedAmount > 0 && _robustRandom.Prob(healPopupProb))
        {
            // Magically, this damage has healed some bleeding, likely
            // because it's burn damage that cauterized their wounds.

            // We'll play a special sound and popup for feedback.
            _audio.PlayPvs(component.BloodHealedSound, uid);
            _popupSystem.PopupEntity(Loc.GetString("bloodstream-component-wounds-cauterized"), uid,
                uid, PopupType.Medium);
        }
    }

    private void OnHealthBeingExamined(EntityUid uid, BloodstreamComponent component, HealthBeingExaminedEvent args)
    {
        if (component.BleedAmount > 10)
        {
            args.Message.PushNewline();
            args.Message.AddMarkup(Loc.GetString("bloodstream-component-profusely-bleeding", ("target", Identity.Entity(uid, EntityManager))));
        }
        else if (component.BleedAmount > 0)
        {
            args.Message.PushNewline();
            args.Message.AddMarkup(Loc.GetString("bloodstream-component-bleeding", ("target", Identity.Entity(uid, EntityManager))));
        }

        if (GetBloodLevelPercentage(uid, component) < component.BloodlossThreshold)
        {
            args.Message.PushNewline();
            args.Message.AddMarkup(Loc.GetString("bloodstream-component-looks-pale", ("target", Identity.Entity(uid, EntityManager))));
        }
    }

    private void OnBeingGibbed(EntityUid uid, BloodstreamComponent component, BeingGibbedEvent args)
    {
        SpillAllSolutions(uid, component);
    }

    private void OnApplyMetabolicMultiplier(EntityUid uid, BloodstreamComponent component, ApplyMetabolicMultiplierEvent args)
    {
        if (args.Apply)
        {
            component.UpdateInterval *= args.Multiplier;
            return;
        }
        component.UpdateInterval /= args.Multiplier;
        // Reset the accumulator properly
        if (component.AccumulatedFrametime >= component.UpdateInterval)
            component.AccumulatedFrametime = component.UpdateInterval;
    }

    private void OnRejuvenate(EntityUid uid, BloodstreamComponent component, RejuvenateEvent args)
    {
        TryModifyBleedAmount(uid, -component.BleedAmount, component);
        TryModifyBloodLevel(uid, component.BloodSolution.AvailableVolume, component);
    }

    /// <summary>
    ///     Attempt to transfer provided solution to internal solution.
    /// </summary>
    public bool TryAddToChemicals(EntityUid uid, Solution solution, BloodstreamComponent? component=null)
    {
        if (!Resolve(uid, ref component, false))
            return false;

        return _solutionContainerSystem.TryAddSolution(uid, component.ChemicalSolution, solution);
    }

    public bool FlushChemicals(EntityUid uid, string excludedReagentID, FixedPoint2 quantity, BloodstreamComponent? component = null) {
        if (!Resolve(uid, ref component, false))
            return false;

        for (var i = component.ChemicalSolution.Contents.Count - 1; i >= 0; i--)
        {
            var (reagentId, _) = component.ChemicalSolution.Contents[i];
            if (reagentId != excludedReagentID)
            {
                _solutionContainerSystem.TryRemoveReagent(uid, component.ChemicalSolution, reagentId, quantity);
            }
        }

        return true;
    }

    public float GetBloodLevelPercentage(EntityUid uid, BloodstreamComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return 0.0f;

        return component.BloodSolution.FillFraction;
    }

    public void SetBloodLossThreshold(EntityUid uid, float threshold, BloodstreamComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return;

        comp.BloodlossThreshold = threshold;
    }

    /// <summary>
    ///     Attempts to modify the blood level of this entity directly.
    /// </summary>
    public bool TryModifyBloodLevel(EntityUid uid, FixedPoint2 amount, BloodstreamComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return false;

        if (amount >= 0)
            return _solutionContainerSystem.TryAddReagent(uid, component.BloodSolution, component.BloodReagent, amount, out _);

        // Removal is more involved,
        // since we also wanna handle moving it to the temporary solution
        // and then spilling it if necessary.
        var newSol = component.BloodSolution.SplitSolution(-amount);
        component.BloodTemporarySolution.AddSolution(newSol, _prototypeManager);

        if (component.BloodTemporarySolution.Volume > component.BleedPuddleThreshold)
        {
            // Pass some of the chemstream into the spilled blood.
            var temp = component.ChemicalSolution.SplitSolution(component.BloodTemporarySolution.Volume / 10);
            component.BloodTemporarySolution.AddSolution(temp, _prototypeManager);
            if (_puddleSystem.TrySpillAt(uid, component.BloodTemporarySolution, out var puddleUid, false))
            {
                if (TryComp<DnaComponent>(uid, out var dna))
                {
                    var comp = EnsureComp<ForensicsComponent>(puddleUid);
                    comp.DNAs.Add(dna.DNA);
                }
            }

            component.BloodTemporarySolution.RemoveAllSolution();
        }

        return true;
    }

    /// <summary>
    ///     Tries to make an entity bleed more or less
    /// </summary>
    public bool TryModifyBleedAmount(EntityUid uid, FixedPoint2 amount, BloodstreamComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return false;

        component.BleedAmount += amount;
        component.BleedAmount = FixedPoint2.Clamp(component.BleedAmount, 0, component.MaxBleedAmount);

        return true;
    }

    /// <summary>
    ///     BLOOD FOR THE BLOOD GOD
    /// </summary>
    public void SpillAllSolutions(EntityUid uid, BloodstreamComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        var max = component.BloodSolution.MaxVolume + component.BloodTemporarySolution.MaxVolume +
                  component.ChemicalSolution.MaxVolume;
        var tempSol = new Solution() { MaxVolume = max };

        tempSol.AddSolution(component.BloodSolution, _prototypeManager);
        component.BloodSolution.RemoveAllSolution();
        tempSol.AddSolution(component.BloodTemporarySolution, _prototypeManager);
        component.BloodTemporarySolution.RemoveAllSolution();
        tempSol.AddSolution(component.ChemicalSolution, _prototypeManager);
        component.ChemicalSolution.RemoveAllSolution();

        if (_puddleSystem.TrySpillAt(uid, tempSol, out var puddleUid))
        {
            if (TryComp<DnaComponent>(uid, out var dna))
            {
                var comp = EnsureComp<ForensicsComponent>(puddleUid);
                comp.DNAs.Add(dna.DNA);
            }
        }
    }

    public void ModifyBleedInflicter(EntityUid inflicterId, EntityUid bloodstreamId, FixedPoint2 delta, BleedInflicterComponent? inflicter = null, WoundComponent? wound = null, BloodstreamComponent? bloodstream = null)
    {
        if (!Resolve(inflicterId, ref inflicter, ref wound))
            return;

        var oldBleed = inflicter.Bloodloss * wound.Severity;

        inflicter.Bloodloss += delta;
        var newBleed = inflicter.Bloodloss * wound.Severity;
        TryModifyBleedAmount(bloodstreamId, newBleed - oldBleed, bloodstream);
    }
}
