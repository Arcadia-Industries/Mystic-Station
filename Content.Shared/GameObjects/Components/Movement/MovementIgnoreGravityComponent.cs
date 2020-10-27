#nullable enable
using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.Interfaces.Physics;
using Robust.Shared.IoC;

namespace Content.Shared.GameObjects.Components.Movement
{
    [RegisterComponent]
    public sealed class MovementIgnoreGravityComponent : Component
    {
        public override string Name => "MovementIgnoreGravity";
    }

    public static class GravityExtensions
    {
        public static bool IsWeightless(this IEntity entity, IPhysicsManager? physicsManager = null)
        {
            physicsManager ??= IoCManager.Resolve<IPhysicsManager>();

            bool isWeightless = !entity.HasComponent<MovementIgnoreGravityComponent>() &&
                   physicsManager.IsWeightless(entity.Transform.Coordinates);
            if(entity.TryGetComponent<SharedWeightlessStatusComponent> statusComp)
                statusComp.UpdateStatus(isWeightless);
            return isWeightless;
        }
    }
}
