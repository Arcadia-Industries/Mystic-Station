using Content.Shared.Nodes.Components;

namespace Content.Shared.Nodes.Events;

[ByRefEvent]
public readonly record struct EdgeAddedEvent
(EntityUid NodeId, EntityUid EdgeId, EdgeFlags Flags, GraphNodeComponent Node, GraphNodeComponent Edge);

[ByRefEvent]
public readonly record struct EdgeRemovedEvent
(EntityUid NodeId, EntityUid EdgeId, EdgeFlags Flags, GraphNodeComponent Node, GraphNodeComponent Edge);

[ByRefEvent]
public readonly record struct EdgeChangedEvent
(EntityUid NodeId, EntityUid EdgeId, EdgeFlags NewFlags, EdgeFlags OldFlags, GraphNodeComponent Node, GraphNodeComponent Edge);

[ByRefEvent]
public record struct UpdateEdgesEvent(EntityUid NodeId, GraphNodeComponent Node)
{
    public readonly EntityUid NodeId = NodeId;
    public readonly GraphNodeComponent Node = Node;
    public Dictionary<EntityUid, EdgeFlags>? Edges = null;
}

[ByRefEvent]
public record struct CheckEdgeEvent(EntityUid NodeId, EntityUid EdgeId, GraphNodeComponent Node, GraphNodeComponent Edge)
{
    public readonly EntityUid NodeId = NodeId;
    public readonly EntityUid EdgeId = EdgeId;
    public readonly GraphNodeComponent Node = Node;
    public readonly GraphNodeComponent Edge = Edge;
    public EdgeFlags Flags = Components.Edge.DefaultFlags;
    public bool Wanted = false;
}
