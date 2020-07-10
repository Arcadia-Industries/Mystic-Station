using System;
using System.Collections.Generic;
using Content.Server.GameObjects.Components.Access;
using Content.Server.GameObjects.EntitySystems.AI.Pathfinding.Pathfinders;
using Content.Server.GameObjects.EntitySystems.Pathfinding;
using Content.Shared.AI;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects.Components;
using Robust.Shared.GameObjects.Systems;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.Interfaces.Map;
using Robust.Shared.Interfaces.Timing;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Server.GameObjects.EntitySystems.AI.Pathfinding.Accessible
{
    /// <summary>
    /// Determines whether an AI has access to a specific pathfinding node.
    /// </summary>
    /// Long-term can be used to do hierarchical pathfinding
    [UsedImplicitly]
    public sealed class AiReachableSystem : EntitySystem
    {
        /*
         * The purpose of this is to provide a higher-level / hierarchical abstraction of the actual pathfinding graph
         * The goal is so that we can more quickly discern if a specific node is reachable or not rather than
         * Pathfinding the entire graph.
         *
         * There's a lot of different implementations of hierarchical or some variation of it: HPA*, PRA, HAA*, etc.
         * (HPA* technically caches the edge nodes of each chunk), e.g. Rimworld, Factorio, etc.
         * so we'll just write one with SS14's requirements in mind.
         *
         * There's probably a better data structure to use though you'd need to benchmark multiple ones to compare,
         * at the very least on the memory side it could definitely be better.
         */

#pragma warning disable 649
        [Dependency] private IMapManager _mapmanager;
        [Dependency] private IGameTiming _gameTiming;
#pragma warning restore 649
        private PathfindingSystem _pathfindingSystem;
        
        /// <summary>
        /// Queued region updates
        /// </summary>
        private HashSet<PathfindingChunk> _queuedUpdates = new HashSet<PathfindingChunk>();
        
        // Oh god the nesting. Shouldn't need to go beyond this
        /// <summary>
        /// The corresponding regions for each PathfindingChunk.
        /// Regions are groups of nodes with the same profile (for pathfinding purposes)
        /// i.e. same collision, not-space, same access, etc.
        /// </summary>
        private Dictionary<GridId, Dictionary<PathfindingChunk, HashSet<PathfindingRegion>>> _regions = 
            new Dictionary<GridId, Dictionary<PathfindingChunk, HashSet<PathfindingRegion>>>();
        
        /// <summary>
        /// Minimum time for the cached reachable regions to be stored
        /// </summary>
        private const float MinCacheTime = 1.0f;
        
        // Cache what regions are accessible from this region. Cached per ReachableArgs
        // so multiple entities in the same region with the same args should all be able to share their reachable lookup
        // Also need to store when we cached it to know if it's stale if the chunks have updated
        
        // TODO: There's probably a more memory-efficient way to cache this
        // Then again, there's likely also a more memory-efficient way to implement regions.
        
        // Also, didn't use a dictionary because there didn't seem to be a clean way to do the lookup
        // Plus this way we can check if everything is equal except for vision so an entity with a lower vision radius can use an entity with a higher vision radius' cached result
        private Dictionary<ReachableArgs, Dictionary<PathfindingRegion, (TimeSpan CacheTime, HashSet<PathfindingRegion> Regions)>> _cachedAccessible = 
            new Dictionary<ReachableArgs, Dictionary<PathfindingRegion, (TimeSpan, HashSet<PathfindingRegion>)>>();

#if DEBUG
        private int _runningCacheIdx = 0;
#endif
        
        public override void Initialize()
        {
            _pathfindingSystem = Get<PathfindingSystem>();
            SubscribeLocalEvent<PathfindingChunkUpdateMessage>(RecalculateNodeRegions);
#if DEBUG
            SubscribeLocalEvent<PlayerAttachSystemMessage>(SendDebugMessage);
#endif
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);
            foreach (var chunk in _queuedUpdates)
            {
                GenerateRegions(chunk);
            }

#if DEBUG
            if (_queuedUpdates.Count > 0)
            {
                foreach (var (gridId, regs) in _regions)
                {
                    if (regs.Count > 0)
                    {
                        SendRegionsDebugMessage(gridId);
                    }
                }
            }
#endif
            _queuedUpdates.Clear();
        }

        public override void Shutdown()
        {
            base.Shutdown();
            _queuedUpdates.Clear();
            _regions.Clear();
            _cachedAccessible.Clear();
        }

        private void RecalculateNodeRegions(PathfindingChunkUpdateMessage message)
        {
            // TODO: Only need to do changed nodes ideally
            // For now this is fine but it's a low-hanging fruit optimisation
            _queuedUpdates.Add(message.Chunk);
        }

        /// <summary>
        /// Can the entity reach the target?
        /// </summary>
        /// First it does a quick check to see if there are any traversable nodes in range.
        /// Then it will go through the regions to try and see if there's a region connection between the target and itself
        /// Will used a cached region if available
        /// <param name="entity"></param>
        /// <param name="target"></param>
        /// <param name="range"></param>
        /// <returns></returns>
        public bool CanAccess(IEntity entity, IEntity target, float range = 0.0f)
        {
            var targetTile = _mapmanager.GetGrid(target.Transform.GridID).GetTileRef(target.Transform.GridPosition);
            var targetNode = _pathfindingSystem.GetNode(targetTile);

            var collisionMask = 0;
            if (entity.TryGetComponent(out CollidableComponent collidableComponent))
            {
                collisionMask = collidableComponent.CollisionMask;
            }

            var access = AccessReader.FindAccessTags(entity);

            // We'll do a quick traversable check before going through regions
            // If we can't access it we'll try to get a valid node in range (this is essentially an early-out)
            if (!PathfindingHelpers.Traversable(collisionMask, access, targetNode))
            {
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (range == 0.0f)
                {
                    return false;
                }

                var pathfindingArgs = new PathfindingArgs(entity.Uid, access, collisionMask, default, targetTile, range);
                foreach (var node in BFSPathfinder.GetNodesInRange(pathfindingArgs, false))
                {
                    targetNode = node;
                }
            }
            
            return CanAccess(entity, targetNode);
        }

        public bool CanAccess(IEntity entity, PathfindingNode targetNode)
        {
            if (entity.Transform.GridID != targetNode.TileRef.GridIndex)
            {
                return false;
            }
            
            var entityTile = _mapmanager.GetGrid(entity.Transform.GridID).GetTileRef(entity.Transform.GridPosition);
            var entityNode = _pathfindingSystem.GetNode(entityTile);
            var entityRegion = GetRegion(entityNode);
            var targetRegion = GetRegion(targetNode);
            // TODO: Regional pathfind from target to entity
            // Early out
            if (entityRegion == targetRegion)
            {
                return true;
            }

            // We'll go from target's position to us because most of the time it's probably in a locked room rather than vice versa
            var reachableArgs = ReachableArgs.GetArgs(entity);
            var reachableRegions = GetReachableRegions(reachableArgs, targetRegion);

            return reachableRegions.Contains(entityRegion);
        }

        /// <summary>
        /// Retrieve the reachable regions
        /// </summary>
        /// <param name="reachableArgs"></param>
        /// <param name="region"></param>
        /// <returns></returns>
        public HashSet<PathfindingRegion> GetReachableRegions(ReachableArgs reachableArgs, PathfindingRegion region)
        {
            // if we're on a node that's not tracked at all atm then region will be null
            if (region == null)
            {
                return new HashSet<PathfindingRegion>();
            }
            
            var cachedArgs = GetCachedArgs(reachableArgs);
            (TimeSpan CacheTime, HashSet<PathfindingRegion> Regions) cached;

            if (!IsCacheValid(cachedArgs, region))
            {
                cached = GetVisionReachable(cachedArgs, region);
                _cachedAccessible[cachedArgs][region] = cached;
#if DEBUG
                SendRegionCacheMessage(region.ParentChunk.GridId, cached.Regions, false);
#endif
            }
            else
            {
                cached = _cachedAccessible[cachedArgs][region];
#if DEBUG
                SendRegionCacheMessage(region.ParentChunk.GridId, cached.Regions, true);
#endif
            }

            return cached.Regions;
        }

        /// <summary>
        /// Get any adequate cached args if possible, otherwise just use ours
        /// </summary>
        /// Essentially any args that have the same access AND >= our vision radius can be used
        /// <param name="accessibleArgs"></param>
        /// <returns></returns>
        private ReachableArgs GetCachedArgs(ReachableArgs accessibleArgs)
        {
            ReachableArgs foundArgs = null;
            
            foreach (var (cachedAccessible, _) in _cachedAccessible)
            {
                if (Equals(cachedAccessible.Access, accessibleArgs.Access) &&
                    cachedAccessible.CollisionMask == accessibleArgs.CollisionMask &&
                    cachedAccessible.VisionRadius <= accessibleArgs.VisionRadius)
                {
                    foundArgs = cachedAccessible;
                    break;
                }
            }

            return foundArgs ?? accessibleArgs;
        }

        /// <summary>
        /// Checks whether there's a valid cache for our accessibility args.
        /// Most regular mobs can share their cached accessibility with each other
        /// </summary>
        /// Will also remove it from the cache if it is invalid
        /// <param name="accessibleArgs"></param>
        /// <param name="region"></param>
        /// <returns></returns>
        private bool IsCacheValid(ReachableArgs accessibleArgs, PathfindingRegion region)
        {
            if (!_cachedAccessible.TryGetValue(accessibleArgs, out var cachedArgs))
            {
                _cachedAccessible.Add(accessibleArgs, new Dictionary<PathfindingRegion, (TimeSpan, HashSet<PathfindingRegion>)>());
                return false;
            }
            
            if (!cachedArgs.TryGetValue(region, out var regionCache))
            {
                return false;
            }

            // Just so we don't invalidate the cache every tick we'll store it for a minimum amount of time
            var currentTime = _gameTiming.CurTime;
            if ((currentTime - regionCache.CacheTime).TotalSeconds < MinCacheTime)
            {
                return true;
            }

            var checkedAccess = new HashSet<PathfindingRegion>();
            // Check if cache is stale
            foreach (var accessibleRegion in regionCache.Regions)
            {
                if (checkedAccess.Contains(accessibleRegion)) continue;
                
                // Any applicable chunk has been invalidated OR one of our neighbors has been invalidated (i.e. new connections)
                // TODO: Could look at storing the TimeSpan directly on the region so our neighbor can tell us straight-up
                if (accessibleRegion.ParentChunk.LastUpdate > regionCache.CacheTime)
                {
                    // Remove the stale cache, to be updated later
                    _cachedAccessible[accessibleArgs].Remove(region);
                    return false;
                }

                foreach (var neighbor in accessibleRegion.Neighbors)
                {
                    if (checkedAccess.Contains(neighbor)) continue;
                    if (neighbor.ParentChunk.LastUpdate > regionCache.CacheTime)
                    {
                        _cachedAccessible[accessibleArgs].Remove(region);
                        return false;
                    }
                    checkedAccess.Add(neighbor);
                }
                checkedAccess.Add(accessibleRegion);
            }
            return true;
        }

        /// <summary>
        /// Caches the entity's nearby accessible regions in vision radius
        /// </summary>
        /// Longer-term TODO: Hierarchical pathfinding in which case this function would probably get bulldozed, BRRRTT
        /// <param name="reachableArgs"></param>
        /// <param name="entityRegion"></param>
        private (TimeSpan, HashSet<PathfindingRegion>) GetVisionReachable(ReachableArgs reachableArgs, PathfindingRegion entityRegion)
        {
            var openSet = new Queue<PathfindingRegion>();
            openSet.Enqueue(entityRegion);
            var closedSet = new HashSet<PathfindingRegion>();
            var accessible = new HashSet<PathfindingRegion> {entityRegion};

            while (openSet.Count > 0)
            {
                var region = openSet.Dequeue();
                closedSet.Add(region);

                foreach (var neighbor in region.Neighbors)
                {
                    if (closedSet.Contains(neighbor))
                    {
                        continue;
                    }
                    
                    // Distance is an approximation here so we'll be generous with it
                    // TODO: Could do better; the fewer nodes the better it is.
                    if (!neighbor.RegionTraversable(reachableArgs) ||
                        neighbor.Distance(entityRegion) > reachableArgs.VisionRadius + 1)
                    {
                        closedSet.Add(neighbor);
                        continue;
                    }
                    
                    openSet.Enqueue(neighbor);
                    accessible.Add(neighbor);
                }
            }
            
            return (_gameTiming.CurTime, accessible);
        }

        /// <summary>
        /// Grab the related cardinal nodes and if they're in different regions then add to our edge and their edge
        /// </summary>
        /// Implicitly they would've already been merged if possible
        /// <param name="region"></param>
        /// <param name="node"></param>
        private void UpdateRegionEdge(PathfindingRegion region, PathfindingNode node)
        {
            DebugTools.Assert(region.Nodes.Contains(node));
            // Originally I tried just doing bottom and left but that doesn't work as the chunk update order is not guaranteed

            var checkDirections = new[] {Direction.East, Direction.South, Direction.West, Direction.North};
            foreach (var direction in checkDirections)
            {
                var directionNode = node.GetNeighbor(direction);
                if (directionNode == null) continue;
                
                var directionRegion = GetRegion(directionNode);
                if (directionRegion == null || directionRegion == region) continue;

                region.Neighbors.Add(directionRegion);
                directionRegion.Neighbors.Add(region);
            }
        }

        /// <summary>
        /// Get the current region for this entity
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public PathfindingRegion GetRegion(IEntity entity)
        {
            var entityTile = _mapmanager.GetGrid(entity.Transform.GridID).GetTileRef(entity.Transform.GridPosition);
            var entityNode = _pathfindingSystem.GetNode(entityTile);
            return GetRegion(entityNode);
        }

        /// <summary>
        /// Get the current region for this node
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public PathfindingRegion GetRegion(PathfindingNode node)
        {
            // Not sure on the best way to optimise this
            // On the one hand, just storing each node's region is faster buuutttt muh memory
            // On the other hand, you might need O(n) lookups on regions for each chunk, though it's probably not too bad with smaller chunk sizes?
            // Someone smarter than me will know better
            var parentChunk = node.ParentChunk;
            
            // No guarantee the node even has a region yet (if we're doing neighbor lookups)
            if (!_regions[parentChunk.GridId].TryGetValue(parentChunk, out var regions))
            {
                return null;
            }

            foreach (var region in regions)
            {
                if (region.Nodes.Contains(node))
                {
                    return region;
                }
            }

            // Longer term this will probably be guaranteed a region but for now space etc. are no region
            return null;
        }

        /// <summary>
        /// Add this node to the relevant region.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="existingRegions"></param>
        /// <returns></returns>
        private PathfindingRegion CalculateNode(PathfindingNode node, Dictionary<PathfindingNode, PathfindingRegion> existingRegions)
        {
            DebugTools.Assert(_regions.ContainsKey(node.ParentChunk.GridId));
            DebugTools.Assert(_regions[node.ParentChunk.GridId].ContainsKey(node.ParentChunk));
            // TODO For now we don't have these regions but longer-term yeah sure
            if (node.BlockedCollisionMask != 0x0 || node.TileRef.Tile.IsEmpty)
            {
                return null;
            }

            var parentChunk = node.ParentChunk;
            // Doors will be their own separate region
            // We won't store them in existingRegions so they don't show up and can't be connected to (at least for now)
            if (node.AccessReaders.Count > 0)
            {
                var region = new PathfindingRegion(node, new HashSet<PathfindingNode>(1) {node}, true);
                _regions[parentChunk.GridId][parentChunk].Add(region);
                UpdateRegionEdge(region, node);
                return region;
            }

            // Relative x and y of the chunk
            // If one of our bottom / left neighbors are in a region try to join them
            // Otherwise, make our own region.
            var x = node.TileRef.X - parentChunk.Indices.X;
            var y = node.TileRef.Y - parentChunk.Indices.Y;
            var leftNeighbor = x > 0 ? parentChunk.Nodes[x - 1, y] : null;
            var bottomNeighbor = y > 0 ? parentChunk.Nodes[x, y - 1] : null;
            PathfindingRegion leftRegion;
            PathfindingRegion bottomRegion;

            // We'll check if our left or down neighbors are already in a region and join them, unless we're a door
            if (node.AccessReaders.Count == 0)
            {
                // Is left node valid to connect to
                if (leftNeighbor != null &&
                    existingRegions.TryGetValue(leftNeighbor, out leftRegion) && 
                    !leftRegion.IsDoor)
                {
                    // We'll try and connect the left node's region to the bottom region if they're separate (yay merge)
                    if (bottomNeighbor != null && existingRegions.TryGetValue(bottomNeighbor, out bottomRegion) &&
                        !bottomRegion.IsDoor)
                    {
                        bottomRegion.Add(node);
                        existingRegions.Add(node, bottomRegion);
                        MergeInto(leftRegion, bottomRegion);
                        return bottomRegion;
                    }

                    leftRegion.Add(node);
                    existingRegions.Add(node, leftRegion);
                    return leftRegion;
                }

                //Is bottom node valid to connect to
                if (bottomNeighbor != null && 
                    existingRegions.TryGetValue(bottomNeighbor, out bottomRegion) && 
                    !bottomRegion.IsDoor)
                {
                    bottomRegion.Add(node);
                    existingRegions.Add(node, bottomRegion);
                    return bottomRegion;
                }
            }

            // If we can't join an existing region then we'll make our own
            var newRegion = new PathfindingRegion(node, new HashSet<PathfindingNode> {node}, node.AccessReaders.Count > 0);
            _regions[parentChunk.GridId][parentChunk].Add(newRegion);
            existingRegions.Add(node, newRegion);
            UpdateRegionEdge(newRegion, node);
            
            return newRegion;
        }

        /// <summary>
        /// Combines the two regions into one bigger region
        /// </summary>
        /// <param name="source"></param>
        /// <param name="target"></param>
        private void MergeInto(PathfindingRegion source, PathfindingRegion target)
        {
            DebugTools.AssertNotNull(source);
            DebugTools.AssertNotNull(target);
            foreach (var node in source.Nodes)
            {
                target.Add(node);
            }

            source.Shutdown();
            _regions[source.ParentChunk.GridId][source.ParentChunk].Remove(source);

            foreach (var node in target.Nodes)
            {
                UpdateRegionEdge(target, node);
            }
        }
        
        /// <summary>
        /// Generate all of the regions within a chunk
        /// </summary>
        /// These can't across over into another chunk and doors are their own region
        /// <param name="chunk"></param>
        private void GenerateRegions(PathfindingChunk chunk)
        {
            if (!_regions.ContainsKey(chunk.GridId))
            {
                _regions.Add(chunk.GridId, new Dictionary<PathfindingChunk, HashSet<PathfindingRegion>>());
            }

            if (_regions[chunk.GridId].TryGetValue(chunk, out var regions))
            {
                foreach (var region in regions)
                {
                    region.Shutdown();
                }
                _regions[chunk.GridId].Remove(chunk);
            }

            // Temporarily store the corresponding region for each node
            // Makes merging regions or adding nodes to existing regions neater.
            var nodeRegions = new Dictionary<PathfindingNode, PathfindingRegion>();
            var chunkRegions = new HashSet<PathfindingRegion>();
            _regions[chunk.GridId].Add(chunk, chunkRegions);

            for (var y = 0; y < PathfindingChunk.ChunkSize; y++)
            {
                for (var x = 0; x < PathfindingChunk.ChunkSize; x++)
                {
                    var node = chunk.Nodes[x, y];
                    var region = CalculateNode(node, nodeRegions);
                    // Currently we won't store a separate region for each mask / space / whatever because muh effort
                    // Long-term you'll want to account for it probably
                    if (region == null)
                    {
                        continue;
                    }
                    
                    chunkRegions.Add(region);
                }
            }
#if DEBUG
            SendRegionsDebugMessage(chunk.GridId);
#endif
        }

#if DEBUG
        private void SendDebugMessage(PlayerAttachSystemMessage message)
        {
            var playerGrid = message.Entity.Transform.GridID;
            SendRegionsDebugMessage(playerGrid);
        }

        private void SendRegionsDebugMessage(GridId gridId)
        {
            var grid = _mapmanager.GetGrid(gridId);
            // Chunk / Regions / Nodes
            var debugResult = new Dictionary<int, Dictionary<int, List<Vector2>>>();
            var chunkIdx = 0;
            var regionIdx = 0;
            
            foreach (var (_, regions) in _regions[gridId])
            {
                var debugRegions = new Dictionary<int, List<Vector2>>();
                debugResult.Add(chunkIdx, debugRegions);

                foreach (var region in regions)
                {
                    var debugRegionNodes = new List<Vector2>(region.Nodes.Count);
                    debugResult[chunkIdx].Add(regionIdx, debugRegionNodes);

                    foreach (var node in region.Nodes)
                    {
                        var nodeVector = grid.GridTileToLocal(node.TileRef.GridIndices).ToMapPos(_mapmanager);
                        debugRegionNodes.Add(nodeVector);
                    }

                    regionIdx++;
                }

                chunkIdx++;
            }
            RaiseNetworkEvent(new SharedAiDebug.ReachableChunkRegionsDebugMessage(gridId, debugResult));
        }

        /// <summary>
        /// Sent whenever the reachable cache for a particular mob is built or retrieved
        /// </summary>
        /// <param name="gridId"></param>
        /// <param name="regions"></param>
        /// <param name="cached"></param>
        private void SendRegionCacheMessage(GridId gridId, IEnumerable<PathfindingRegion> regions, bool cached)
        {
            var grid = _mapmanager.GetGrid(gridId);
            var debugResult = new Dictionary<int, List<Vector2>>();
            
            foreach (var region in regions)
            {
                debugResult.Add(_runningCacheIdx, new List<Vector2>());
                
                foreach (var node in region.Nodes)
                {
                    var nodeVector = grid.GridTileToLocal(node.TileRef.GridIndices).ToMapPos(_mapmanager);
                    
                    debugResult[_runningCacheIdx].Add(nodeVector);
                }

                _runningCacheIdx++;
            }

            RaiseNetworkEvent(new SharedAiDebug.ReachableCacheDebugMessage(gridId, debugResult, cached));
        }
#endif
    }
}