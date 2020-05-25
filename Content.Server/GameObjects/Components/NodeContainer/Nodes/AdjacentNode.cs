﻿using Content.Server.GameObjects.Components.NodeContainer.NodeGroups;
using Robust.Shared.GameObjects.Components.Transform;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server.GameObjects.Components.NodeContainer.Nodes
{
    /// <summary>
    ///     A <see cref="Node"/> that can reach other <see cref="AdjacentNode"/>s that are directly adjacent to it.
    /// </summary>
    [Node("AdjacentNode")]
    public class AdjacentNode : Node
    {
        protected override IEnumerable<INode> GetReachableNodes()
        {
            return Owner.GetComponent<SnapGridComponent>()
                .GetCardinalNeighborCells()
                .SelectMany(sgc => sgc.GetLocal())
                .Select(entity => entity.TryGetComponent<NodeContainerComponent>(out var container) ? container : null)
                .Where(container => container != null)
                .SelectMany(container => container.Nodes)
                .Where(node => node != null && node != this);
        }
    }
}
