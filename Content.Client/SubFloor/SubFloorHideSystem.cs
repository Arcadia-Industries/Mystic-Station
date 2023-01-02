using Content.Shared.SubFloor;
using Robust.Client.GameObjects;

namespace Content.Client.SubFloor;

public sealed class SubFloorHideSystem : SharedSubFloorHideSystem
{
    private bool _showAll;

    [ViewVariables(VVAccess.ReadWrite)]
    public bool ShowAll
    {
        get => _showAll;
        set
        {
            if (_showAll == value) return;
            _showAll = value;

            UpdateAll();
        }
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SubFloorHideComponent, AppearanceChangeEvent>(OnAppearanceChanged);
    }

    private void OnAppearanceChanged(EntityUid uid, SubFloorHideComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        Appearance.TryGetData(uid, SubFloorVisuals.Covered, out bool covered);
        Appearance.TryGetData(uid, SubFloorVisuals.ScannerRevealed, out bool scannerRevealed);

        scannerRevealed &= !ShowAll; // no transparency for show-subfloor mode.

        var revealed = !covered || ShowAll || scannerRevealed;
        var transparency = scannerRevealed ? component.ScannerTransparency : 1f;

        // set visibility & color of each layer
        foreach (var layer in args.Sprite.AllLayers)
        {
            // pipe connection visuals are updated AFTER this, and may re-hide some layers
            layer.Visible = revealed;

            if (layer.Visible)
                layer.Color = layer.Color.WithAlpha(transparency);
        }

        // Is there some layer that is always visible?
        var hasVisibleLayer = false;
        foreach (var layerKey in component.VisibleLayers)
        {
            if (!args.Sprite.LayerMapTryGet(layerKey, out var layerIndex))
                continue;

            var layer = args.Sprite[layerIndex];
            layer.Visible = true;
            layer.Color = layer.Color.WithAlpha(1f);
            hasVisibleLayer = true;
        }

        args.Sprite.Visible = hasVisibleLayer || revealed;
    }

    private void UpdateAll()
    {
        foreach (var (_, appearance) in EntityManager.EntityQuery<SubFloorHideComponent, AppearanceComponent>(true))
        {
            Appearance.MarkDirty(appearance, true);
        }
    }
}
