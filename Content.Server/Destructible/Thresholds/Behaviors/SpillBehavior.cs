using Content.Server.Fluids;
using Content.Server.Fluids.Components;
using Content.Shared.Chemistry.EntitySystems;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using PuddleSystem = Content.Server.Fluids.EntitySystems.PuddleSystem;

namespace Content.Server.Destructible.Thresholds.Behaviors
{
    [UsedImplicitly]
    [DataDefinition]
    public class SpillBehavior : IThresholdBehavior
    {
        [DataField("solution")]
        public string? Solution;

        /// <summary>
        /// If there is a SpillableComponent on IEntity owner use it to create a puddle/smear.
        /// Or whatever solution is specified in the behavior itself.
        /// If none are available do nothing.
        /// </summary>
        /// <param name="owner">Entity on which behavior is executed</param>
        /// <param name="system">system calling the behavior</param>
        public void Execute(IEntity owner, DestructibleSystem system)
        {
            var solutionContainerSystem = EntitySystem.Get<SolutionContainerSystem>();
            var puddleSystem = EntitySystem.Get<PuddleSystem>();

            if (owner.TryGetComponent(out SpillableComponent? spillableComponent) &&
                solutionContainerSystem.TryGetSolution(owner.Uid, spillableComponent.SolutionName,
                    out var compSolution))
            {
                puddleSystem.SpillAt(compSolution, owner.Transform.Coordinates, "PuddleSmear", false);
            }
            else if (Solution != null &&
                     solutionContainerSystem.TryGetSolution(owner.Uid, Solution, out var behaviorSolution))
            {
                puddleSystem.SpillAt(behaviorSolution, owner.Transform.Coordinates, "PuddleSmear", false);
            }
        }
    }
}
