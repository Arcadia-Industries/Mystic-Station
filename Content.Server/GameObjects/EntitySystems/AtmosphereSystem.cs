#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml.Schema;
using Content.Server.Atmos;
using Content.Server.Atmos.Reactions;
using Content.Server.GameObjects.Components.Atmos;
using Content.Shared;
using Content.Shared.Atmos;
using Content.Shared.GameObjects.EntitySystems.Atmos;
using Content.Shared.Maps;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.GameObjects.EntitySystems
{
    [UsedImplicitly]
    public class AtmosphereSystem : SharedAtmosphereSystem
    {
        [Dependency] private readonly IPrototypeManager _protoMan = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IPauseManager _pauseManager = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;

        private GasReactionPrototype[] _gasReactions = Array.Empty<GasReactionPrototype>();

        private SpaceGridAtmosphereComponent _spaceAtmos = default!;
        private GridTileLookupSystem? _gridTileLookup = null;

        /// <summary>
        ///     List of gas reactions ordered by priority.
        /// </summary>
        public IEnumerable<GasReactionPrototype> GasReactions => _gasReactions!;

        private float[] _gasSpecificHeats = new float[Atmospherics.TotalNumberOfGases];
        public float[] GasSpecificHeats => _gasSpecificHeats;

        public GridTileLookupSystem GridTileLookupSystem => _gridTileLookup ??= Get<GridTileLookupSystem>();

        public override void Initialize()
        {
            base.Initialize();

            _gasReactions = _protoMan.EnumeratePrototypes<GasReactionPrototype>().ToArray();
            Array.Sort(_gasReactions, (a, b) => b.Priority.CompareTo(a.Priority));

            _spaceAtmos = new SpaceGridAtmosphereComponent();
            _spaceAtmos.Initialize();
            IoCManager.InjectDependencies(_spaceAtmos);

            _mapManager.TileChanged += OnTileChanged;

            Array.Resize(ref _gasSpecificHeats, MathHelper.NextMultipleOf(Atmospherics.TotalNumberOfGases, 4));

            for (var i = 0; i < GasPrototypes.Length; i++)
            {
                _gasSpecificHeats[i] = GasPrototypes[i].SpecificHeat;
            }

            // Required for airtight components.
            EntityManager.EventBus.SubscribeEvent<RotateEvent>(EventSource.Local, this, RotateEvent);

            _cfg.OnValueChanged(CCVars.SpaceWind, OnSpaceWindChanged, true);
            _cfg.OnValueChanged(CCVars.MonstermosEqualization, OnMonstermosEqualizationChanged, true);
            _cfg.OnValueChanged(CCVars.Superconduction, OnSuperconductionChanged, true);
            _cfg.OnValueChanged(CCVars.AtmosMaxProcessTime, OnAtmosMaxProcessTimeChanged, true);
            _cfg.OnValueChanged(CCVars.AtmosTickRate, OnAtmosTickRateChanged, true);
            _cfg.OnValueChanged(CCVars.ExcitedGroupsSpaceIsAllConsuming, OnExcitedGroupsSpaceIsAllConsumingChanged, true);
        }

        public bool SpaceWind { get; private set; }
        public bool MonstermosEqualization { get; private set; }
        public bool Superconduction { get; private set; }
        public bool ExcitedGroupsSpaceIsAllConsuming { get; private set; }
        public float AtmosMaxProcessTime { get; private set; }
        public float AtmosTickRate { get; private set; }

        private void OnExcitedGroupsSpaceIsAllConsumingChanged(bool obj)
        {
            ExcitedGroupsSpaceIsAllConsuming = obj;
        }

        private void OnAtmosTickRateChanged(float obj)
        {
            AtmosTickRate = obj;
        }

        private void OnAtmosMaxProcessTimeChanged(float obj)
        {
            AtmosMaxProcessTime = obj;
        }

        private void OnMonstermosEqualizationChanged(bool obj)
        {
            MonstermosEqualization = obj;
        }

        private void OnSuperconductionChanged(bool obj)
        {
            Superconduction = obj;
        }

        private void OnSpaceWindChanged(bool obj)
        {
            SpaceWind = obj;
        }

        public override void Shutdown()
        {
            base.Shutdown();

            EntityManager.EventBus.UnsubscribeEvent<RotateEvent>(EventSource.Local, this);
        }

        private void RotateEvent(RotateEvent ev)
        {
            if (ev.Sender.TryGetComponent(out AirtightComponent? airtight))
            {
                airtight.RotateEvent(ev);
            }
        }

        public IGridAtmosphereComponent GetGridAtmosphere(GridId gridId)
        {
            if (!gridId.IsValid())
            {
                return _spaceAtmos;
            }

            var grid = _mapManager.GetGrid(gridId);

            if (!EntityManager.TryGetEntity(grid.GridEntityId, out var gridEnt)) return _spaceAtmos;

            return gridEnt.TryGetComponent(out IGridAtmosphereComponent? atmos) ? atmos : _spaceAtmos;
        }

        /// <summary>
        ///     Unlike <see cref="GetGridAtmosphere"/>, this doesn't return space grid when not found.
        /// </summary>
        public bool TryGetSimulatedGridAtmosphere(GridId gridId, [NotNullWhen(true)] out IGridAtmosphereComponent? atmosphere)
        {
            if (gridId.IsValid()
                && _mapManager.TryGetGrid(gridId, out var mapGrid)
                && ComponentManager.TryGetComponent(mapGrid.GridEntityId, out IGridAtmosphereComponent? atmosGrid)
                && atmosGrid.Simulated)
            {
                atmosphere = atmosGrid;
                return true;
            }

            atmosphere = null;
            return false;
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            foreach (var (mapGridComponent, gridAtmosphereComponent) in EntityManager.ComponentManager.EntityQuery<IMapGridComponent, IGridAtmosphereComponent>(true))
            {
                if (_pauseManager.IsGridPaused(mapGridComponent.GridIndex)) continue;

                gridAtmosphereComponent.Update(frameTime);
            }
        }

        private void OnTileChanged(object? sender, TileChangedEventArgs eventArgs)
        {
            // When a tile changes, we want to update it only if it's gone from
            // space -> not space or vice versa. So if the old tile is the
            // same as the new tile in terms of space-ness, ignore the change

            if (eventArgs.NewTile.IsSpace() == eventArgs.OldTile.IsSpace())
            {
                return;
            }

            GetGridAtmosphere(eventArgs.NewTile.GridIndex)?.Invalidate(eventArgs.NewTile.GridIndices);
        }
    }
}
