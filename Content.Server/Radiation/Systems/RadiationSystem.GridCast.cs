using System.Linq;
using Content.Server.Radiation.Components;
using Content.Shared.Physics;
using Content.Shared.Radiation.Components;
using Content.Shared.Radiation.Events;
using Content.Shared.Radiation.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Timing;

namespace Content.Server.Radiation.Systems;

// main algorithm that fire radiation rays to target
public partial class RadiationSystem
{
    private void UpdateGridcast()
    {
        var saveVisitedTiles = _debugSessions.Count > 0;
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        var sources = EntityQuery<RadiationSourceComponent, TransformComponent>().ToArray();
        var destinations = EntityQuery<RadiationReceiverComponent, TransformComponent>().ToArray();
        var blockerQuery = GetEntityQuery<RadiationBlockerComponent>();
        var resistanceQuery = GetEntityQuery<RadiationGridResistanceComponent>();

        // trace all rays from rad source to rad receivers
        var rays = new List<RadiationRay>();
        var receivedRads = new List<(RadiationReceiverComponent, float)>();
        foreach (var (dest, destTrs) in destinations)
        {
            var rads = 0f;
            foreach (var (source, sourceTrs) in sources)
            {
                // send ray towards destination entity
                var ray = Irradiate(sourceTrs.Owner, sourceTrs, destTrs.Owner, destTrs,
                    source.Intensity, source.Slope, saveVisitedTiles, blockerQuery, resistanceQuery);
                if (ray == null)
                    continue;

                rays.Add(ray);

                // add rads to total rad exposure
                if (ray.ReachedDestination)
                    rads += ray.Rads;
            }

            receivedRads.Add((dest, rads));
        }

        // update information for debug overlay
        var elapsedTime = stopwatch.Elapsed.TotalMilliseconds;
        var totalSources = sources.Length;
        var totalReceivers = destinations.Length;
        var ev = new OnRadiationOverlayUpdateEvent(elapsedTime, totalSources, totalReceivers, rays);
        UpdateDebugOverlay(ev);

        // send rads to each entity
        foreach (var (receiver, rads) in receivedRads)
        {
            // update radiation value of receiver
            // if no radiation rays reached target, that will set it to 0
            receiver.CurrentRadiation = rads;

            // also send an event with combination of total rad
            if (rads > 0)
                IrradiateEntity(receiver.Owner, rads,GridcastUpdateRate);
        }
    }

    private RadiationRay? Irradiate(EntityUid sourceUid, TransformComponent sourceTrs,
        EntityUid destUid, TransformComponent destTrs,
        float incomingRads, float slope, bool saveVisitedTiles,
        EntityQuery<RadiationBlockerComponent> blockerQuery,
        EntityQuery<RadiationGridResistanceComponent> resistanceQuery)
    {
        // lets first check that source and destination on the same map
        if (sourceTrs.MapID != destTrs.MapID)
            return null;
        var mapId = sourceTrs.MapID;

        // get direction from rad source to destination and its distance
        var sourceWorld = sourceTrs.WorldPosition;
        var destWorld = destTrs.WorldPosition;
        var dir = destWorld - sourceWorld;
        var dist = dir.Length;

        // check if receiver is too far away
        if (dist > GridcastMaxDistance)
            return null;
        // will it even reach destination considering distance penalty
        var rads = incomingRads - slope * dist;
        if (rads <= MinIntensity)
            return null;

        // if source and destination on the same grid it's possible that
        // between them can be another grid (ie. shuttle in center of donut station)
        // however we can do simplification and ignore that case
        if (GridcastSimplifiedSameGrid && sourceTrs.GridUid != null && sourceTrs.GridUid == destTrs.GridUid)
        {
            return Gridcast(mapId, sourceTrs.GridUid.Value, sourceUid, destUid,
                sourceWorld, destWorld, rads, saveVisitedTiles, resistanceQuery);
        }

        // lets check how many grids are between source and destination
        // do a box intersection test between target and destination
        // it's not very precise, but really cheap
        var box = Box2.FromTwoPoints(sourceWorld, destWorld);
        var grids = _mapManager.FindGridsIntersecting(mapId, box, true);

        // we are only interested in grids that has some radiation blockers
        // lets count them (could use linq, but this a bit faster)
        var resGridsCount = 0;
        EntityUid lastGridUid = default;
        foreach (var grid in grids)
        {
            if (!resistanceQuery.HasComponent(grid.GridEntityId))
                continue;
            lastGridUid = grid.GridEntityId;

            resGridsCount++;
            if (resGridsCount > 1)
                break;
        }

        if (resGridsCount == 0)
        {
            // no grids found - so no blockers (just distance penalty)
            return new RadiationRay(mapId, sourceUid,sourceWorld,
                destUid,destWorld, rads);
        }
        else if (resGridsCount == 1)
        {
            // one grid found - use it for gridcast
            return Gridcast(mapId, lastGridUid, sourceUid, destUid,
                sourceWorld, destWorld, rads, saveVisitedTiles, resistanceQuery);
        }
        else
        {
            // more than one grid - fallback to raycast
            return Raycast(mapId, sourceUid, destUid, sourceWorld, destWorld,
                dir.Normalized, dist, rads, blockerQuery);
        }
    }

    private RadiationRay Gridcast(MapId mapId, EntityUid gridUid, EntityUid sourceUid, EntityUid destUid,
        Vector2 sourceWorld, Vector2 destWorld, float incomingRads, bool saveVisitedTiles,
        EntityQuery<RadiationGridResistanceComponent> resistanceQuery)
    {
        var visitedTiles = new List<(Vector2i, float?)>();
        var radRay = new RadiationRay(mapId, sourceUid,sourceWorld,
            destUid,destWorld, incomingRads)
        {
            Grid = gridUid,
            VisitedTiles = visitedTiles
        };

        // if grid doesn't have resistance map just apply distance penalty
        if (!resistanceQuery.TryGetComponent(gridUid, out var resistance))
            return radRay;
        var resistanceMap = resistance.ResistancePerTile;

        // get coordinate of source and destination in grid coordinates
        // todo: entity queries doesn't support interface - use it when IMapGridComponent will be removed
        if (!TryComp(gridUid, out IMapGridComponent? grid))
            return radRay;
        var sourceGrid = grid.Grid.TileIndicesFor(sourceWorld);
        var destGrid = grid.Grid.TileIndicesFor(destWorld);

        // iterate tiles in grid line from source to destination
        var line = Line(sourceGrid.X, sourceGrid.Y, destGrid.X, destGrid.Y);
        foreach (var point in line)
        {
            (Vector2i, float?) visitedTile = (point, null);
            if (resistanceMap.TryGetValue(point, out var resData))
            {
                radRay.Rads -= resData;
                visitedTile.Item2 = radRay.Rads;
            }

            // save data for debug
            if (saveVisitedTiles)
                radRay.VisitedTiles.Add(visitedTile);

            // no intensity left after blocker
            if (radRay.Rads <= MinIntensity)
            {
                radRay.Rads = 0;
                return radRay;
            }
        }

        return radRay;
    }

    private RadiationRay Raycast(MapId mapId, EntityUid sourceUid, EntityUid destUid,
        Vector2 sourceWorld, Vector2 destWorld, Vector2 dir, float distance, float incomingRads,
        EntityQuery<RadiationBlockerComponent> blockerQuery)
    {
        var blockers = new List<(Vector2, float)>();
        var radRay = new RadiationRay(mapId, sourceUid, sourceWorld,
            destUid, destWorld, incomingRads)
        {
            Blockers = blockers
        };

        var colRay = new CollisionRay(sourceWorld, dir, (int) CollisionGroup.Impassable);
        var results = _physicsSystem.IntersectRay(mapId, colRay, distance, returnOnFirstHit: false);

        foreach (var obstacle in results)
        {
            if (!blockerQuery.TryGetComponent(obstacle.HitEntity, out var blocker))
                continue;

            radRay.Rads -= blocker.RadResistance;
            blockers.Add((obstacle.HitPos, radRay.Rads));

            if (radRay.Rads <= MinIntensity)
            {
                radRay.Rads = 0;
                return radRay;
            }
        }

        return radRay;
    }


    // bresenhams line algorithm
    // this is slightly rewritten version of code bellow
    // https://stackoverflow.com/questions/11678693/all-cases-covered-bresenhams-line-algorithm
    private IEnumerable<Vector2i> Line(int x, int y, int x2, int y2)
    {
        var w = x2 - x;
        var h = y2 - y;

        var dx1 = Math.Sign(w);
        var dy1 = Math.Sign(h);
        var dx2 = Math.Sign(w);
        var dy2 = 0;

        var longest = Math.Abs(w);
        var shortest = Math.Abs(h);
        if (longest <= shortest)
        {
            (longest, shortest) = (shortest, longest);
            dx2 = 0;
            dy2 = Math.Sign(h);
        }

        var numerator = longest / 2;
        for (var i = 0; i <= longest; i++)
        {
            yield return new Vector2i(x, y);
            numerator += shortest;
            if (numerator >= longest)
            {
                numerator -= longest;
                x += dx1;
                y += dy1;
            }
            else
            {
                x += dx2;
                y += dy2;
            }
        }
    }
}
