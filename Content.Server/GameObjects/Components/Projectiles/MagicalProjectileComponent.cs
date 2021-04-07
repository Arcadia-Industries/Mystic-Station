using Content.Server.GameObjects.Components.Explosion;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;
using System;

namespace Content.Server.GameObjects.Components.Projectiles
{
    [RegisterComponent]
    [DataDefinition]
    public class MagicalProjectileComponent : Component, ICollideBehavior
    {
        public override string Name => "MagicalProjectile";

        [ViewVariables] [DataField("NeedComponent")] public string TargetType { get; set; } = "Mind";

        [ViewVariables] [DataField("AddedComponent")] public string InduceComponent { get; set; } = "RadiatonPulse";

        public Type RegisteredTargetType;

        public Type RegisteredInduceType;
      

        public override void Initialize()
        {
            base.Initialize();

        }

        void ICollideBehavior.CollideWith(IEntity target)
        {
            var compFactory = IoCManager.Resolve<IComponentFactory>();
            var registration = compFactory.GetRegistration(TargetType);
            RegisteredTargetType = registration.Type;
            //Inducer registration
            var registrationInducer = compFactory.GetRegistration(InduceComponent);
            RegisteredInduceType = registrationInducer.Type;
            if (!target.TryGetComponent(RegisteredTargetType, out var component))
            {
                return;
            }
            if (target.HasComponent(RegisteredInduceType))
            {
                return;
            }
            var componentInduced = compFactory.GetComponent(RegisteredInduceType);
            Component compInducedFinal = (Component) componentInduced;
            compInducedFinal.Owner = target;
            target.EntityManager.ComponentManager.AddComponent(target, compInducedFinal);
        }

        // Projectile should handle the deleting
        void ICollideBehavior.PostCollide(int collisionCount)
        {
            return;
        }
    }
}
