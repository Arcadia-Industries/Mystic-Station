﻿#nullable enable
using System.Threading.Tasks;
using Content.Server.Utility;
using Content.Shared.Construction;
using JetBrains.Annotations;
using Robust.Shared.GameObjects.Components.Transform;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Server.Construction.Completions
{
    [UsedImplicitly]
    public class SnapToGrid : IGraphAction
    {
        public SnapGridOffset Offset { get; private set; } = SnapGridOffset.Center;
        public bool ZeroRotation { get; private set; } = false;

        public void ExposeData(ObjectSerializer serializer)
        {
            serializer.DataField(this, x => x.Offset, "offset", SnapGridOffset.Center);
            serializer.DataField(this, x => x.ZeroRotation, "zeroRotation", false);
        }

        public async Task PerformAction(IEntity entity, IEntity? user)
        {
            if (entity.Deleted) return;

            entity.SnapToGrid(Offset);
            if (ZeroRotation)
            {
                entity.Transform.LocalRotation = 0;
            }
        }
    }
}
