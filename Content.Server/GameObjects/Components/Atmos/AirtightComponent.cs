﻿using System;
using Content.Server.Interfaces.Atmos;
using Robust.Server.Interfaces.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.GameObjects.Components.Transform;
using Robust.Shared.Interfaces.Map;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Server.GameObjects.Components.Atmos
{
    [RegisterComponent]
    public class AirtightComponent : Component, IMapInit
    {
#pragma warning disable 649
        [Dependency] private readonly IAtmosphereMap _atmosphereMap;
#pragma warning restore 649

        private SnapGridComponent _snapGrid;
        private (GridId, MapIndices) _lastPosition;

        public override string Name => "Airtight";

        public bool UseAdjacentAtmosphere;

        public override void ExposeData(ObjectSerializer serializer)
        {
            base.ExposeData(serializer);

            serializer.DataField(ref UseAdjacentAtmosphere, "adjacentAtmosphere", false);
        }

        public override void Initialize()
        {
            base.Initialize();

            // Using the SnapGrid is critical for the performance of the room builder, and thus if
            // it is absent the component will not be airtight. An exception is much easier to track
            // down than the object magically not being airtight, so throw one if the SnapGrid component
            // is missing.
            if (!Owner.TryGetComponent<SnapGridComponent>(out _snapGrid))
                throw new Exception("Airtight entities must have a SnapGrid component");
        }

        public void MapInit()
        {
            _snapGrid.OnPositionChanged += OnTransformMove;
            _lastPosition = (Owner.Transform.GridID, _snapGrid.Position);
            UpdatePosition();
        }

        protected override void Shutdown()
        {
            base.Shutdown();
            _snapGrid.OnPositionChanged -= OnTransformMove;
            UpdatePosition();
        }

        private void OnTransformMove()
        {
            UpdatePosition(_lastPosition.Item1, _lastPosition.Item2);
            UpdatePosition();
            _lastPosition = (Owner.Transform.GridID, _snapGrid.Position);
        }

        private void UpdatePosition() => UpdatePosition(Owner.Transform.GridID, _snapGrid.Position);

        private void UpdatePosition(GridId gridId, MapIndices pos)
        {
            _atmosphereMap.GetGridAtmosphereManager(gridId).Invalidate(pos);
        }

    }
}
