﻿using Content.Server.GameObjects.Components.Atmos;
using Content.Server.Interfaces.Atmos;
using Content.Shared.Atmos;
using Robust.Server.Interfaces.Timing;
using Robust.Shared.GameObjects.Components.Transform;
using Robust.Shared.Interfaces.GameObjects.Components;
using Robust.Shared.Interfaces.Map;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Content.Server.Atmos
{
    /// <inheritdoc cref="IAtmosphereMap"/>
    internal class AtmosphereMap : IAtmosphereMap, IPostInjectInit
    {
#pragma warning disable 649
        [Dependency] private readonly IMapManager _mapManager;
        [Dependency] private readonly IPauseManager _pauseManager;
#pragma warning restore 649

        private readonly Dictionary<GridId, GridAtmosphereManager> _gridAtmosphereManagers =
            new Dictionary<GridId, GridAtmosphereManager>();

        public void PostInject()
        {
            _mapManager.TileChanged += AtmosphereMapOnTileChanged;
        }

        public IGridAtmosphereManager GetGridAtmosphereManager(GridId grid)
        {
            if (_gridAtmosphereManagers.TryGetValue(grid, out var manager))
                return manager;

            if (!_mapManager.GridExists(grid))
                throw new ArgumentException("Cannot get atmosphere of missing grid", nameof(grid));

            manager = new GridAtmosphereManager(_mapManager.GetGrid(grid));
            _gridAtmosphereManagers[grid] = manager;
            return manager;
        }

        public IAtmosphere GetAtmosphere(ITransformComponent position)
        {
            var indices = _mapManager.GetGrid(position.GridID).SnapGridCellFor(position.GridPosition, SnapGridOffset.Center);
            return GetGridAtmosphereManager(position.GridID).GetAtmosphere(indices);
        }

        public void Update(float frameTime)
        {
            foreach (var (gridId, atmos) in _gridAtmosphereManagers)
            {
                if (_pauseManager.IsGridPaused(gridId))
                    continue;

                atmos.Update(frameTime);
            }
        }

        private void AtmosphereMapOnTileChanged(object sender, TileChangedEventArgs eventArgs)
        {
            // When a tile changes, we want to update it only if it's gone from
            // space -> not space or vice versa. So if the old tile is the
            // same as the new tile in terms of space-ness, ignore the change

            if (eventArgs.NewTile.Tile.IsEmpty == eventArgs.OldTile.IsEmpty)
            {
                return;
            }

            // If the grid itself has no atmosphere simulation, there's no need
            // create one - since the atmospheres will be built correctly as
            // necessary later. That's why we *don't* use GetGridAtmosphereManager()

            if (!_gridAtmosphereManagers.TryGetValue(eventArgs.NewTile.GridIndex, out var gridManager))
            {
                return;
            }

            gridManager.Invalidate(eventArgs.NewTile.GridIndices);
        }
    }
}
