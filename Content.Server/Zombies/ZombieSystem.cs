using System.Linq;
using Content.Server.Body.Systems;
using Content.Server.Chat;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.Cloning;
using Content.Server.Drone.Components;
using Content.Server.Humanoid;
using Content.Server.Inventory;
using Content.Shared.Bed.Sleep;
using Content.Shared.Chemistry.Components;
using Content.Server.Emoting.Systems;
using Content.Server.GameTicking.Rules;
using Content.Server.IdentityManagement;
using Content.Server.Popups;
using Content.Server.Speech.EntitySystems;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Zombies;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Zombies
{
    public sealed partial class ZombieSystem : SharedZombieSystem
    {
        [Dependency] private readonly AutoEmoteSystem _autoEmote = default!;
        [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
        [Dependency] private readonly ChatSystem _chat = default!;
        [Dependency] private readonly DamageableSystem _damageable = default!;
        [Dependency] private readonly EmoteOnDamageSystem _emoteOnDamage = default!;
        [Dependency] private readonly HumanoidAppearanceSystem _humanoidSystem = default!;
        [Dependency] private readonly HumanoidAppearanceSystem _sharedHuApp = default!;
        [Dependency] private readonly IChatManager _chatMan = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly IPrototypeManager _proto = default!;
        [Dependency] private readonly IPrototypeManager _protoManager = default!;
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly IdentitySystem _identity = default!;
        [Dependency] private readonly MobStateSystem _mobState = default!;
        [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
        [Dependency] private readonly MovementSpeedModifierSystem _movementSpeedModifier = default!;
        [Dependency] private readonly PassiveHealSystem _passiveHeal = default!;
        [Dependency] private readonly PopupSystem _popup = default!;
        [Dependency] private readonly BurstHealSystem _burstHealSystem = default!;
        [Dependency] private readonly ServerInventorySystem _inv = default!;
        [Dependency] private readonly ServerInventorySystem _serverInventory = default!;
        [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
        [Dependency] private readonly SharedCombatModeSystem _combat = default!;
        [Dependency] private readonly SharedHandsSystem _sharedHands = default!;
        [Dependency] private readonly ZombieRuleSystem _zombieRule = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<ZombieComponent, EmoteEvent>(OnEmote, before:
                new []{typeof(VocalSystem), typeof(BodyEmotesSystem)});

            SubscribeLocalEvent<LivingZombieComponent, MeleeHitEvent>(OnMeleeHit);
            SubscribeLocalEvent<ZombieComponent, MobStateChangedEvent>(OnMobState);
            SubscribeLocalEvent<ZombieComponent, CloningEvent>(OnZombieCloning);
            SubscribeLocalEvent<ZombieComponent, TryingToSleepEvent>(OnSleepAttempt);

            SubscribeLocalEvent<PendingZombieComponent, MapInitEvent>(OnPendingMapInit);
        }

        private void OnPendingMapInit(EntityUid uid, PendingZombieComponent component, MapInitEvent args)
        {
            component.NextTick = _timing.CurTime;
        }

        private void OnSleepAttempt(EntityUid uid, ZombieComponent component, ref TryingToSleepEvent args)
        {
            args.Cancelled = true;
        }

        private void OnEmote(EntityUid uid, ZombieComponent component, ref EmoteEvent args)
        {
            // always play zombie emote sounds and ignore others
            if (args.Handled)
                return;
            args.Handled = _chat.TryPlayEmoteSound(uid, component.Settings.EmoteSounds, args.Emote);
        }

        private void OnMobState(EntityUid uid, ZombieComponent zombie, MobStateChangedEvent args)
        {
            if (!HasComp<LivingZombieComponent>(uid))
            {
                // We have a very specific edge case. Zombies who we marked as "dead" (no burst heal) can come back
                // if they still have Bicardine in their system from biting someone. Or someone foolishly
                // healed them.
                if (HasComp<PendingZombieComponent>(uid) || HasComp<InitialInfectedComponent>(uid))
                {
                    // Handle those elsewhere
                    return;
                }

                // Handle zombies who came back here.
                EnsureComp<LivingZombieComponent>(uid);
            }

            if (args.NewMobState == MobState.Alive)
            {
                // Don't run this routine for zombies who are still transforming...
                // Groaning when damaged
                EnsureComp<EmoteOnDamageComponent>(uid);
                _emoteOnDamage.AddEmote(uid, "Scream");

                // Random groaning
                EnsureComp<AutoEmoteComponent>(uid);
                _autoEmote.AddEmote(uid, "ZombieGroan");

                // Make an emote on returning to life
                _chat.TryEmoteWithoutChat(uid, "ZombieGroan");

                // LivingZombieComponent might get removed by zombie death roll below but if the zombie comes back to
                //   life somehow we must put it back.
                EnsureComp<LivingZombieComponent>(uid);
                _passiveHeal.BeginHealing(uid, zombie.Settings.HealingPerSec);
            }
            else
            {
                // Stop groaning when damaged
                _emoteOnDamage.RemoveEmote(uid, "Scream");

                // Stop random groaning
                _autoEmote.RemoveEmote(uid, "ZombieGroan");

                RemComp<PassiveHealComponent>(uid);

                // Roll to see if this zombie is not coming back.
                if ((args.NewMobState == MobState.Dead) || _random.Prob(zombie.Settings.PermadeathChance))
                {
                    // You're dead! No reviving for you.
                    RemComp<LivingZombieComponent>(uid);
                    _popup.PopupEntity(Loc.GetString("zombie-permadeath"), uid, uid);

                    // Check if this was the last zombie that just got wiped out.
                    if (zombie.Family.Rules != EntityUid.Invalid)
                        _zombieRule.CheckRuleEnd(zombie.Family.Rules);
                }
                else
                {
                    _burstHealSystem.QueueBurstHeal(uid, zombie.Settings.ReviveTime, zombie.Settings.ReviveTimeMax);
                }
            }
        }

        private float GetZombieInfectionChance(EntityUid uid, ZombieComponent component)
        {
            var baseChance = component.Settings.MaxInfectionChance;

            if (!TryComp<InventoryComponent>(uid, out var inventoryComponent))
                return baseChance;

            var enumerator =
                new InventorySystem.ContainerSlotEnumerator(uid, inventoryComponent.TemplateId, _protoManager, _inv,
                    SlotFlags.FEET |
                    SlotFlags.HEAD |
                    SlotFlags.EYES |
                    SlotFlags.GLOVES |
                    SlotFlags.MASK |
                    SlotFlags.NECK |
                    SlotFlags.INNERCLOTHING |
                    SlotFlags.OUTERCLOTHING);

            var items = 0f;
            var total = 0f;
            while (enumerator.MoveNext(out var con))
            {
                total++;

                if (con.ContainedEntity != null)
                    items++;
            }

            var max = component.Settings.MaxInfectionChance;
            var min = component.Settings.MinInfectionChance;
            //gets a value between the max and min based on how many items the entity is wearing
            var chance = (max-min) * ((total - items)/total) + min;
            return chance;
        }

        // When a zombie hits a victim, process what happens next.
        private void OnMeleeHit(EntityUid uid, LivingZombieComponent component, MeleeHitEvent args)
        {
            if (!EntityManager.TryGetComponent<ZombieComponent>(args.User, out var zombieAttacker))
                return;

            if (!args.HitEntities.Any())
                return;

            foreach (var entity in args.HitEntities)
            {
                if (args.User == entity)
                    continue;

                if (!TryComp<MobStateComponent>(entity, out var mobState) || HasComp<DroneComponent>(entity))
                    continue;

                if (HasComp<ZombieComponent>(entity))
                {
                    if (HasComp<LivingZombieComponent>(entity))
                    {
                        // Reduce damage to living zombies.
                        args.BonusDamage = -args.BaseDamage * zombieAttacker.Settings.OtherZombieDamageCoefficient;
                    }

                    if (_random.Prob(0.3f))
                    {
                        // Tell the zombo that they are eating the dead
                        _popup.PopupEntity(Loc.GetString("zombie-bite-already-infected"), uid, uid);
                    }
                }
                else
                {
                    // On a diceroll or if critical we infect this victim
                    if (_random.Prob(GetZombieInfectionChance(entity, zombieAttacker)) ||
                        mobState.CurrentState != MobState.Alive)
                    {
                        if (!HasComp<ZombieComponent>(entity))
                        {
                            var pending = EnsureComp<PendingZombieComponent>(entity);
                            pending.MaxInfectionLength =
                                _random.NextFloat(0.25f, 1.0f) * zombieAttacker.Settings.InfectionTurnTime;
                            pending.InfectionStarted = _timing.CurTime;
                            pending.VirusDamage = zombieAttacker.Settings.VirusDamage;
                            pending.DeadMinTurnTime = zombieAttacker.Settings.DeadMinTurnTime;

                            var zombie = EnsureComp<ZombieComponent>(entity);
                            // Our victims inherit our settings, which defines damage and more.
                            zombie.Settings = zombieAttacker.VictimSettings ?? zombieAttacker.Settings;

                            // Track who infected this new zombo
                            zombie.Family = new ZombieFamily()
                            {
                                Rules = zombieAttacker.Family.Rules,
                                Generation = zombieAttacker.Family.Generation + 1,
                                Infector = uid
                            };
                        }

                        _popup.PopupEntity(Loc.GetString("zombie-bite-infected-victim"), uid, uid);
                    }

                    if (mobState.CurrentState == MobState.Alive) //heals when zombies bite live entities
                    {
                        var healingSolution = new Solution();
                        healingSolution.AddReagent("Bicaridine", 1.00); //if OP, reduce/change chem
                        _bloodstream.TryAddToChemicals(args.User, healingSolution);
                    }
                }
            }
        }

        /// <summary>
        ///     This is the function to call if you want to unzombify an entity.
        /// </summary>
        /// <param name="source">the entity having the ZombieComponent</param>
        /// <param name="target">the entity you want to unzombify (different from source in case of cloning, for example)</param>
        /// <remarks>
        ///     this currently only restore the name and skin/eye color from before zombified
        ///     TODO: reverse everything else done in ZombifyEntity
        /// </remarks>
        public bool UnZombify(EntityUid source, EntityUid target, ZombieComponent? zombiecomp)
        {
            if (!Resolve(source, ref zombiecomp))
                return false;

            foreach (var (layer, info) in zombiecomp.BeforeZombifiedCustomBaseLayers)
            {
                _humanoidSystem.SetBaseLayerColor(target, layer, info.Color);
                _humanoidSystem.SetBaseLayerId(target, layer, info.ID);
            }
            _humanoidSystem.SetSkinColor(target, zombiecomp.BeforeZombifiedSkinColor);

            MetaData(target).EntityName = zombiecomp.BeforeZombifiedEntityName;

            // You're cured!
            RemComp<InitialInfectedComponent>(target);
            RemComp<PendingZombieComponent>(target);
            RemCompDeferred<ZombieComponent>(target);
            RemComp<LivingZombieComponent>(target);

            return true;
        }

        private void OnZombieCloning(EntityUid uid, ZombieComponent zombiecomp, ref CloningEvent args)
        {
            if (UnZombify(args.Source, args.Target, zombiecomp))
                args.NameHandled = true;
        }

    }
}
