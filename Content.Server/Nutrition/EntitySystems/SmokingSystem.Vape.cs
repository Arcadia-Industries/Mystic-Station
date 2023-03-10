using Content.Server.Nutrition.Components;
using Content.Shared.Chemistry.Components;
using Content.Server.Body.Components;
using Content.Shared.Interaction;
using Content.Server.DoAfter;
using System.Threading;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Damage;
using Content.Server.Chemistry.ReactionEffects;
using Content.Server.Popups;
using Content.Shared.IdentityManagement;
using Content.Shared.DoAfter;
using Content.Shared.Emag.Systems;
using Content.Shared.Emag.Components;

namespace Content.Server.Nutrition.EntitySystems
{
    public sealed partial class SmokingSystem
    {
        [Dependency] private readonly DoAfterSystem _doAfterSystem = default!;
        [Dependency] private readonly DamageableSystem _damageableSystem = default!;
        [Dependency] private readonly FoodSystem _foodSystem = default!;
        [Dependency] private readonly ExplosionSystem _explosionSystem = default!;
        [Dependency] private readonly PopupSystem _popupSystem = default!;
        [Dependency] private readonly FlavorProfileSystem _flavorProfileSystem = default!;

        private void InitializeVapes()
        {
            SubscribeLocalEvent<VapeComponent, AfterInteractEvent>(OnVapeInteraction);
            SubscribeLocalEvent<VapeComponent, DoAfterEvent<VapingData>>(OnVapeDoAfter);
            SubscribeLocalEvent<VapeComponent, GotEmaggedEvent>(OnEmagged);
        }

        private void OnVapeInteraction(EntityUid uid, VapeComponent comp, AfterInteractEvent args) 
        {
            _solutionContainerSystem.TryGetRefillableSolution(uid, out var solution);

            var delay = comp.Delay;
            var forced = true;
            var exploded = false;

            if (!args.CanReach
            || solution == null
            || comp.CancelToken != null
            || !TryComp<BloodstreamComponent>(args.Target, out var bloodstream)
            || _foodSystem.IsMouthBlocked(args.Target.Value, args.User))
                return;

            if (args.Target == args.User)
            {
                delay = comp.UserDelay;
                forced = false;
            }

            if (comp.ExplodeOnUse || HasComp<EmaggedComponent>(uid))
            {
                _explosionSystem.QueueExplosion(uid, "Default", comp.ExplosionIntensity, 0.5f, 3, canCreateVacuum: false);
                EntityManager.DeleteEntity(uid);
                exploded = true;
            }
            else
            {
                foreach (var name in comp.ExplodableSolutions)
                {
                    if (solution.ContainsReagent(name))
                    {
                        exploded = true;
                        _explosionSystem.QueueExplosion(uid, "Default", comp.ExplosionIntensity, 0.5f, 3, canCreateVacuum: false);
                        EntityManager.DeleteEntity(uid);
                        break;
                    }
                }
            }

            if (forced)
            {
                var targetName = Identity.Entity(args.Target.Value, EntityManager);
                var userName = Identity.Entity(args.User, EntityManager);

                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-try-use-vape-forced", ("user", userName)), args.Target.Value,
                    args.Target.Value);
                
                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-try-use-vape-forced-user", ("target", targetName)), args.User,
                    args.User);
            }
            else
            {
                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-try-use-vape"), args.User,
                    args.User);
            }
            
            var vapingData = new VapingData(solution, bloodstream, forced);

            if (!exploded)
            {
                comp.CancelToken = new CancellationTokenSource();

                var doAfterArgs = new DoAfterEventArgs(args.User, delay, comp.CancelToken.Token, args.Target, uid)
                {
                    BreakOnTargetMove = true,
                    BreakOnUserMove = false,
                    BreakOnDamage = true,
                    BreakOnStun = true
                };

                _doAfterSystem.DoAfter(doAfterArgs, vapingData);
            }
            args.Handled = true;
		}

        private void OnVapeDoAfter(EntityUid uid, VapeComponent comp, DoAfterEvent<VapingData> args)
        {
            if (args.Cancelled)
            {
                comp.CancelToken = null;
                return;
            }

            comp.CancelToken = null;

            if (args.Handled || args.Args.Target == null)
                return;
            
            var flavors = _flavorProfileSystem.GetLocalizedFlavorsMessage(args.Args.User, args.AdditionalData.Solution);

            if (args.AdditionalData.Solution.Volume != 0)
                SmokeAreaReactionEffect.SpawnSmoke(comp.SmokePrototype, Transform(args.Args.Target.Value).Coordinates,
                    args.AdditionalData.Solution, comp.SmokeAmount, 5, 1, 1, entityManager: EntityManager);

            args.AdditionalData.Solution.ScaleSolution(0.6f);

            //Smoking kills(your lungs, but there is no organ damage yet)
            _damageableSystem.TryChangeDamage(args.Args.Target.Value, comp.Damage, true);

            _bloodstreamSystem.TryAddToChemicals(args.Args.Target.Value, args.AdditionalData.Solution, args.AdditionalData.Bloodstream);

            args.AdditionalData.Solution.RemoveAllSolution();
            
            if (args.AdditionalData.Forced)
            {
                var targetName = Identity.Entity(args.Args.Target.Value, EntityManager);
                var userName = Identity.Entity(args.Args.User, EntityManager);

                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-vape-success-taste-forced", ("flavors", flavors), ("user", userName)), args.Args.Target.Value,
                    args.Args.Target.Value);
                
                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-vape-success-user-forced", ("target", targetName)), args.Args.User,
                    args.Args.Target.Value);
            }
            else
            {
                _popupSystem.PopupEntity(
                    Loc.GetString("vape-component-vape-success-taste", ("flavors", flavors)), args.Args.Target.Value,
                    args.Args.Target.Value);
            }
        }
        private void OnEmagged(EntityUid uid, VapeComponent component, ref GotEmaggedEvent args)
        {
            args.Handled = true;
        }

        private record struct VapingData(Solution Solution, BloodstreamComponent Bloodstream, bool Forced)
        {
            public readonly Solution Solution = Solution;
            public readonly bool Forced = Forced;
            public readonly BloodstreamComponent Bloodstream = Bloodstream;
        }
	}
}