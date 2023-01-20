using Content.Server.Administration.Commands;
using Content.Server.Speech.Components;
using Content.Server.Popups;
using Content.Shared.Popups;
using Content.Server.Interaction.Components;
using Content.Server.Speech;
using Content.Shared.Mobs;
using Robust.Shared.Random;
using Robust.Shared.Audio;
using Content.Shared.IdentityManagement;
using Content.Shared.Stunnable;
using YamlDotNet.Serialization;

namespace Content.Server.Cluwne;

public sealed class CluwneSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly SharedStunSystem _stunSystem = default!;


    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CluwneComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<CluwneComponent, ComponentStartup>(OnComponentStartup);
    }

    private void OnMobState(EntityUid uid, CluwneComponent component, MobStateChangedEvent args)
    {
        // removes cluwne component if cluwne dies.
        if (args.NewMobState == MobState.Dead)
           RemComp<CluwneComponent>(uid);

    }

    private void OnComponentStartup(EntityUid uid, CluwneComponent component, ComponentStartup args)
    {
        var meta = MetaData(uid);
        var name = meta.EntityName;

        //accent, it is backwards but with some thought you can still communicate.
        EnsureComp<BackwardsAccentComponent>(uid);

        var vocal = EnsureComp<VocalComponent>(uid);
        var scream = new SoundCollectionSpecifier("CluwneScreams");
        vocal.FemaleScream = scream;
        vocal.MaleScream = scream;

        //make cluwne clumsy
        EnsureComp<ClumsyComponent>(uid);

        //popup x has turned into a cluwne and play bikehorn when cluwnified.
        _popupSystem.PopupEntity(Loc.GetString("cluwne-transform", ("target", uid)), uid, PopupType.LargeCaution);
        _audio.PlayPvs(component.SpawnSound, uid);

        //gives it the funny "Cluwnified ___" name.
        meta.EntityName = Loc.GetString("cluwne-name-prefix", ("target", name));

        //gives the cluwne costume
        SetOutfitCommand.SetOutfit(uid, "CluwneGear", EntityManager);
    }

    private void DoGiggle(EntityUid uid, CluwneComponent component)
    {
        // makes the cluwne randomly emote, scream, falldown or honk.
        if (component.LastDamageGiggleCooldown > 0)
            return;

            //will on occasion fall over and make a noise.
            if (_robustRandom.Prob(0.2f))
            { 
                _audio.PlayPvs(component.KnockSound, uid);
                _stunSystem.TryParalyze(uid, TimeSpan.FromSeconds(component.ParalyzeTime), true);
            }

        // will scream.
        if (_robustRandom.Prob(0.3f))
            {
                _audio.PlayPvs(component.Giggle, uid);
            }

         // will twitch and honk.
         else
         {
            _popupSystem.PopupEntity(Loc.GetString("cluwne-twitch", ("target", Identity.Entity(uid, EntityManager))), uid);
            _audio.PlayPvs(component.SpawnSound, uid);
         }

        component.LastDamageGiggleCooldown = component.GiggleCooldown;
         
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var cluwnecomp in EntityQuery<CluwneComponent>())
        {
            cluwnecomp.Accumulator += frameTime;
            cluwnecomp.LastDamageGiggleCooldown -= frameTime;

            if (cluwnecomp.Accumulator < cluwnecomp.RandomGiggleAttempt)
                continue;
            cluwnecomp.Accumulator -= cluwnecomp.RandomGiggleAttempt;

            if (!_robustRandom.Prob(cluwnecomp.GiggleChance))
                continue;

            //either do twitch and honk or fidget and honk.
            DoGiggle(cluwnecomp.Owner, cluwnecomp);
        }

    }

}

