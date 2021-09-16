using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Content.Server.Fluids;
using Content.Server.Fluids.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Coordinates;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using PuddleSystem = Content.Server.Fluids.EntitySystems.PuddleSystem;

namespace Content.IntegrationTests.Tests.Fluids
{
    [TestFixture]
    [TestOf(typeof(PuddleComponent))]
    public class PuddleTest : ContentIntegrationTest
    {
        [Test]
        public async Task TilePuddleTest()
        {
            var server = StartServerDummyTicker();

            await server.WaitIdleAsync();

            var mapManager = server.ResolveDependency<IMapManager>();
            var pauseManager = server.ResolveDependency<IPauseManager>();
            var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
            var puddleSystem = server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<PuddleSystem>();

            EntityCoordinates coordinates = default;

            // Build up test environment
            server.Post(() =>
            {
                // Create a one tile grid to spill onto
                var mapId = mapManager.CreateMap();

                pauseManager.AddUninitializedMap(mapId);

                var gridId = new GridId(1);

                if (!mapManager.TryGetGrid(gridId, out var grid))
                {
                    grid = mapManager.CreateGrid(mapId, gridId);
                }

                var tileDefinition = tileDefinitionManager["underplating"];
                var tile = new Tile(tileDefinition.TileId);
                coordinates = grid.ToCoordinates();

                grid.SetTile(coordinates, tile);

                pauseManager.DoMapInitialize(mapId);
            });

            await server.WaitIdleAsync();

            server.Assert(() =>
            {
                var solution = new Solution("water", ReagentUnit.New(20));
                var puddle = puddleSystem.SpillAt(solution, coordinates, "PuddleSmear");
                Assert.NotNull(puddle);
            });

            await server.WaitIdleAsync();
        }

        [Test]
        public async Task SpaceNoPuddleTest()
        {
            var server = StartServerDummyTicker();

            await server.WaitIdleAsync();
            var mapManager = server.ResolveDependency<IMapManager>();
            var pauseManager = server.ResolveDependency<IPauseManager>();
            var puddleSystem = server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<PuddleSystem>();
            IMapGrid grid = null;

            // Build up test environment
            server.Post(() =>
            {
                var mapId = mapManager.CreateMap();

                pauseManager.AddUninitializedMap(mapId);

                var gridId = new GridId(1);

                if (!mapManager.TryGetGrid(gridId, out grid))
                {
                    grid = mapManager.CreateGrid(mapId, gridId);
                }
            });

            await server.WaitIdleAsync();

            server.Assert(() =>
            {
                var coordinates = grid.ToCoordinates();
                var solution = new Solution("water", ReagentUnit.New(20));
                var puddle = puddleSystem.SpillAt(solution, coordinates, "PuddleSmear");
                Assert.Null(puddle);
            });

            await server.WaitIdleAsync();
        }

        [Test]
        public async Task PuddlePauseTest()
        {
            var server = StartServer();

            await server.WaitIdleAsync();

            var sMapManager = server.ResolveDependency<IMapManager>();
            var sPauseManager = server.ResolveDependency<IPauseManager>();
            var sTileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
            var sGameTiming = server.ResolveDependency<IGameTiming>();
            var sEntityManager = server.ResolveDependency<IEntityManager>();
            var puddleSystem = server.ResolveDependency<IEntitySystemManager>().GetEntitySystem<PuddleSystem>();


            MapId sMapId = default;
            IMapGrid sGrid;
            GridId sGridId = default;
            IEntity sGridEntity = null;
            EntityCoordinates sCoordinates = default;

            // Spawn a paused map with one tile to spawn puddles on
            await server.WaitPost(() =>
            {
                sMapId = sMapManager.CreateMap();
                sPauseManager.SetMapPaused(sMapId, true);
                sGrid = sMapManager.CreateGrid(sMapId);
                sGridId = sGrid.Index;
                sGridEntity = sEntityManager.GetEntity(sGrid.GridEntityId);
                sGridEntity.Paused = true; // See https://github.com/space-wizards/RobustToolbox/issues/1444

                var tileDefinition = sTileDefinitionManager["underplating"];
                var tile = new Tile(tileDefinition.TileId);
                sCoordinates = sGrid.ToCoordinates();

                sGrid.SetTile(sCoordinates, tile);
            });

            // Check that the map and grid are paused
            await server.WaitAssertion(() =>
            {
                Assert.True(sPauseManager.IsGridPaused(sGridId));
                Assert.True(sPauseManager.IsMapPaused(sMapId));
                Assert.True(sGridEntity.Paused);
            });

            float sEvaporateTime = default;
            PuddleComponent sPuddle = null;
            EvaporationComponent sEvaporation = null;
            ReagentUnit sPuddleStartingVolume = default;

            // Spawn a puddle
            await server.WaitAssertion(() =>
            {
                var solution = new Solution("water", ReagentUnit.New(20));
                sPuddle= puddleSystem.SpillAt(solution, sCoordinates, "PuddleSmear");

                // Check that the puddle was created
                Assert.NotNull(sPuddle);

                sEvaporation = sPuddle.Owner.GetComponent<EvaporationComponent>();

                sPuddle.Owner.Paused = true; // See https://github.com/space-wizards/RobustToolbox/issues/1445

                Assert.True(sPuddle.Owner.Paused);

                // Check that the puddle is going to evaporate
                Assert.Positive(sEvaporation.EvaporateTime);

                // Should have a timer component added to it for evaporation
                Assert.True(sPuddle.Owner.TryGetComponent(out TimerComponent _));

                sEvaporateTime = sEvaporation.EvaporateTime;
                sPuddleStartingVolume = sPuddle.CurrentVolume;
            });

            // Wait enough time for it to evaporate if it was unpaused
            var sTimeToWait = (5 + (int) Math.Ceiling(sEvaporateTime * sGameTiming.TickRate)) * 2;
            await server.WaitRunTicks(sTimeToWait);

            // No evaporation due to being paused
            await server.WaitAssertion(() =>
            {
                Assert.True(sPuddle.Owner.Paused);
                Assert.True(sPuddle.Owner.TryGetComponent(out TimerComponent _));

                // Check that the puddle still exists
                Assert.False(sPuddle.Owner.Deleted);
            });

            // Unpause the map
            await server.WaitPost(() =>
            {
                sPauseManager.SetMapPaused(sMapId, false);
            });

            // Check that the map, grid and puddle are unpaused
            await server.WaitAssertion(() =>
            {
                Assert.False(sPauseManager.IsMapPaused(sMapId));
                Assert.False(sPauseManager.IsGridPaused(sGridId));
                Assert.False(sPuddle.Owner.Paused);

                // Check that the puddle still exists
                Assert.False(sPuddle.Owner.Deleted);
            });

            // Wait enough time for it to evaporate
            await server.WaitRunTicks(sTimeToWait);

            // Puddle evaporation should have ticked
            await server.WaitAssertion(() =>
            {
                // Check that the puddle is unpaused
                Assert.False(sPuddle.Owner.Paused);

                // Check that the puddle has evaporated some of its volume
                Assert.That(sPuddle.CurrentVolume, Is.LessThan(sPuddleStartingVolume));

                // If its new volume is zero it should have been deleted
                if (sPuddle.CurrentVolume == ReagentUnit.Zero)
                {
                    Assert.True(sPuddle.Deleted);
                }
            });
        }
    }
}
