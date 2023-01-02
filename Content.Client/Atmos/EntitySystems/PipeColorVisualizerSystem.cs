using Content.Client.Atmos.Components;
using Robust.Client.GameObjects;
using Content.Shared.Atmos.Piping;

namespace Content.Client.Atmos.EntitySystems;

public sealed class PipeColorVisualizerSystem : VisualizerSystem<PipeColorVisualsComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, PipeColorVisualsComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
        {
            return;
        }

        if(AppearanceSystem.TryGetData<Color>(uid, PipeColorVisuals.Color, out var color))
        {
            // T-ray scanner / sub floor runs after this visualizer. Lets not bulldoze transparency.
            var layer = args.Sprite[PipeVisualLayers.Pipe];
            layer.Color = color.WithAlpha(layer.Color.A);
        }
    }
}

public enum PipeVisualLayers : byte
{
    Pipe,
}
