﻿using Content.Server.GameObjects.Components.Disposal;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.GameObjects.Systems;

namespace Content.Server.GameObjects.EntitySystems
{
    [UsedImplicitly]
    public class DisposalSystem : EntitySystem
    {
        public DisposalSystem()
        {
            EntityQuery = new TypeEntityQuery(typeof(DisposableComponent));
        }

        public override void Update(float frameTime)
        {
            foreach (var disposable in RelevantEntities)
            {
                disposable.GetComponent<DisposableComponent>().Update(frameTime);
            }
        }
    }
}
