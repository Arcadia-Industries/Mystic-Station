﻿#nullable enable
using System.Threading.Tasks;
using Content.Shared.Construction;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Server.Construction.Completions
{
    [UsedImplicitly]
    public class SpriteStateChange : IGraphAction
    {

        public void ExposeData(ObjectSerializer serializer)
        {
            serializer.DataField(this, x => x.State, "state", null);
            serializer.DataField(this, x => x.Layer, "layer", 0);
        }

        public int Layer { get; private set; } = 0;
        public string State { get; private set; } = string.Empty;

        public async Task StepCompleted(IEntity entity, IEntity user)
        {
            await PerformAction(entity, user);
        }

        public async Task PerformAction(IEntity entity, IEntity? user)
        {
            if (entity.Deleted) return;

            if (!entity.TryGetComponent(out SpriteComponent? sprite)) return;

            sprite.LayerSetState(Layer, State);
        }
    }
}
