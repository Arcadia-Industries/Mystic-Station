﻿#nullable enable
using Content.Server.GameObjects.Components.Botany;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;

namespace Content.Server.Chemistry.PlantMetabolism
{
    [UsedImplicitly]
    public class AffectGrowth : AdjustAttribute
    {
        public override void Metabolize(IEntity plantHolder, float customPlantMetabolism = 1f)
        {
            if (!CanMetabolize(plantHolder, out var plantHolderComp))
                return;

            plantHolderComp.AffectGrowth((int) Amount);
        }
    }
}
