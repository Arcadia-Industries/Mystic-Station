using System.Linq;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Humanoid;

public sealed class HumanoidVisualizerSystem : VisualizerSystem<HumanoidComponent>
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private MarkingManager _markingManager = default!;

    protected override void OnAppearanceChange(EntityUid uid, HumanoidComponent component, ref AppearanceChangeEvent args)
    {
        base.OnAppearanceChange(uid, component, ref args);

        if (args.Sprite == null)
        {
            return;
        }

        if (!args.AppearanceData.TryGetValue(HumanoidVisualizerKey.Key, out var dataRaw)
            || dataRaw is not HumanoidVisualizerData data)
        {
            return;
        }

        if (!_prototypeManager.TryIndex(data.Species, out SpeciesPrototype? speciesProto)
            || !_prototypeManager.TryIndex(speciesProto.SpriteSet, out HumanoidSpeciesBaseSpritesPrototype? baseSprites))
        {
            return;
        }

        var dirty = data.SkinColor != component.SkinColor || data.Sex != component.Sex;
        component.Sex = data.Sex;

        if (data.CustomBaseLayerInfo.Count != 0)
        {
            dirty |= MergeCustomBaseSprites(uid, baseSprites.Sprites, data.CustomBaseLayerInfo, component);
        }
        else
        {
            dirty |= MergeCustomBaseSprites(uid, baseSprites.Sprites, null, component);
        }

        if (dirty)
        {
            ApplyBaseSprites(uid, component, args.Sprite);
            ApplySkinColor(uid, data.SkinColor, component, args.Sprite);
            //
            component.CachedHairColor = data.HairColor;
            component.CachedFacialHairColor = data.FacialHairColor;
            component.CachedEyeColor = data.EyeColor;
        }

        if (data.CustomBaseLayerInfo.Count != 0)
        {
            foreach (var (layer, info) in data.CustomBaseLayerInfo)
            {
                SetBaseLayerColor(uid, layer, info.Color, args.Sprite);
            }
        }

        var layerVis = data.LayerVisibility.ToHashSet();
        dirty |= ReplaceHiddenLayers(uid, layerVis, component, args.Sprite);

        DiffAndApplyMarkings(uid, data.Markings, dirty, component, args.Sprite);
    }

    private bool ReplaceHiddenLayers(EntityUid uid, HashSet<HumanoidVisualLayers> hiddenLayers,
        HumanoidComponent humanoid, SpriteComponent sprite)
    {
        if (hiddenLayers.SetEquals(humanoid.HiddenLayers))
        {
            return false;
        }

        SetSpriteVisibility(uid, hiddenLayers, false, sprite);

        humanoid.HiddenLayers.ExceptWith(hiddenLayers);

        SetSpriteVisibility(uid, humanoid.HiddenLayers, true, sprite);

        humanoid.HiddenLayers.Clear();
        humanoid.HiddenLayers.UnionWith(hiddenLayers);

        return true;
    }

    private void SetSpriteVisibility(EntityUid uid, HashSet<HumanoidVisualLayers> layers, bool visibility, SpriteComponent sprite)
    {
        foreach (var layer in layers)
        {
            if (!sprite.LayerMapTryGet(layer, out var index))
            {
                continue;
            }

            sprite[index].Visible = visibility;
        }
    }

    private void DiffAndApplyMarkings(EntityUid uid,
        List<Marking> newMarkings,
        bool layersDirty,
        HumanoidComponent humanoid,
        SpriteComponent sprite)
    {
        // skip this entire thing if both sets are empty
        if (humanoid.CurrentClientMarkings.Count == 0 && newMarkings.Count == 0)
        {
            return;
        }

        var dirtyMarkings = new List<int>();
        var dirtyRangeStart = humanoid.CurrentClientMarkings.Count == 0 ? 0 : -1;

        // edge cases:
        // humanoid.CurrentClientMarkings < newMarkings.Count
        // - check if count matches this condition before diffing
        // - if count is unequal, set dirty range to start from humanoid.CurrentClientMarkings.Count
        // humanoid.CurrentClientMarkings > newMarkings.Count, no dirty markings
        // - break count upon meeting this condition
        // - clear markings from newMarkings.Count to humanoid.CurrentClientMarkings.Count - newMarkings.Count

        for (var i = 0; i < humanoid.CurrentClientMarkings.Count; i++)
        {
            // if we've reached the end of the new set of markings,
            // then that means it's time to finish
            if (newMarkings.Count == i)
            {
                break;
            }

            // if the marking is different here, set the range start to i and break, we need
            // to rebuild all markings starting from i
            if (humanoid.CurrentClientMarkings[i].MarkingId != newMarkings[i].MarkingId)
            {
                dirtyRangeStart = i;
                break;
            }

            // otherwise, we add the current marking to dirtyMarkings if it has different
            // settings
            // however: if the hidden layers are set to dirty, then we need to
            // instead just add every single marking, since we don't know ahead of time
            // where these markings go
            if (humanoid.CurrentClientMarkings[i] != newMarkings[i] || layersDirty)
            {
                dirtyMarkings.Add(i);
            }
        }

        foreach (var i in dirtyMarkings)
        {
            if (!_markingManager.TryGetMarking(newMarkings[i], out var dirtyMarking))
            {
                continue;
            }

            ApplyMarking(uid, dirtyMarking, newMarkings[i].MarkingColors, newMarkings[i].Visible, humanoid, sprite);
        }

        if (humanoid.CurrentClientMarkings.Count < newMarkings.Count && dirtyRangeStart < 0)
        {
            dirtyRangeStart = humanoid.CurrentClientMarkings.Count;
        }

        if (dirtyRangeStart >= 0)
        {
            var range = newMarkings.GetRange(dirtyRangeStart, newMarkings.Count - dirtyRangeStart);

            if (humanoid.CurrentClientMarkings.Count > 0)
            {
                var oldRange = humanoid.CurrentClientMarkings.GetRange(dirtyRangeStart, humanoid.CurrentClientMarkings.Count - dirtyRangeStart);
                ClearMarkings(uid, oldRange, humanoid, sprite);
            }

            ApplyMarkings(uid, range, humanoid, sprite);
        }
        else if (humanoid.CurrentClientMarkings.Count != newMarkings.Count)
        {
            if (newMarkings.Count == 0)
            {
                ClearAllMarkings(uid, humanoid, sprite);
            }
            else if (humanoid.CurrentClientMarkings.Count > newMarkings.Count)
            {
                var rangeStart = newMarkings.Count;
                var rangeCount = humanoid.CurrentClientMarkings.Count - newMarkings.Count;
                var range = humanoid.CurrentClientMarkings.GetRange(rangeStart, rangeCount);

                ClearMarkings(uid, range, humanoid, sprite);
            }
        }

        if (dirtyMarkings.Count > 0 || dirtyRangeStart >= 0 || humanoid.CurrentClientMarkings.Count != newMarkings.Count)
        {
            humanoid.CurrentClientMarkings = newMarkings;
        }
    }

    private void ClearAllMarkings(EntityUid uid, HumanoidComponent humanoid,
        SpriteComponent spriteComp)
    {
        ClearMarkings(uid, humanoid.CurrentClientMarkings, humanoid, spriteComp);
    }

    private void ClearMarkings(EntityUid uid, List<Marking> markings, HumanoidComponent humanoid,
        SpriteComponent spriteComp)
    {
        foreach (var marking in markings)
        {
            RemoveMarking(uid, marking, spriteComp);
        }
    }

    private void RemoveMarking(EntityUid uid, Marking marking,
        SpriteComponent spriteComp)
    {
        if (!_markingManager.TryGetMarking(marking, out var prototype))
        {
            return;
        }

        foreach (var sprite in prototype.Sprites)
        {
            if (sprite is not SpriteSpecifier.Rsi rsi)
            {
                continue;
            }

            var layerId = $"{marking.MarkingId}-{rsi.RsiState}";
            if (!spriteComp.LayerMapTryGet(layerId, out var index))
            {
                continue;
            }

            spriteComp.LayerMapRemove(layerId);
            spriteComp.RemoveLayer(index);
        }
    }

    private void ApplyMarkings(EntityUid uid,
        List<Marking> markings,
        HumanoidComponent humanoid,
        SpriteComponent spriteComp)
    {
        foreach (var marking in new ReverseMarkingEnumerator(markings))
        {
            if (!_markingManager.TryGetMarking(marking, out var markingPrototype))
            {
                continue;
            }

            ApplyMarking(uid, markingPrototype, marking.MarkingColors, marking.Visible, humanoid, spriteComp);
        }
    }

    // TODO: Do marking things on server-side, so client only must apply it.
    private void ApplyMarking(EntityUid uid,
        MarkingPrototype markingPrototype,
        IReadOnlyList<Color>? colors,
        bool visible,
        HumanoidComponent humanoid,
        SpriteComponent sprite)
    {
        if (!sprite.LayerMapTryGet(markingPrototype.BodyPart, out int targetLayer))
        {
            return;
        }

        visible &= !humanoid.HiddenLayers.Contains(markingPrototype.BodyPart);
        visible &= humanoid.BaseLayers.TryGetValue(markingPrototype.BodyPart, out var setting)
           && setting.AllowsMarkings;

        // Marking coloring
        var skinColor = humanoid.SkinColor;
        if (visible && setting != null) skinColor.A = setting.LayerAlpha;
        var markingColors = MarkingColoring.GetMarkingLayerColors(
                markingPrototype,
                skinColor,
                humanoid.CachedEyeColor,
                humanoid.CachedHairColor,
                humanoid.CachedFacialHairColor
            );
        foreach (var mrk in markingColors) {
            Logger.DebugS("kek", $"color {mrk.ToHex()} -- {markingPrototype.ID}");
        }

        for (var j = 0; j < markingPrototype.Sprites.Count; j++)
        {
            if (markingPrototype.Sprites[j] is not SpriteSpecifier.Rsi rsi)
            {
                continue;
            }

            var layerId = $"{markingPrototype.ID}-{rsi.RsiState}";

            if (!sprite.LayerMapTryGet(layerId, out _))
            {
                var layer = sprite.AddLayer(markingPrototype.Sprites[j], targetLayer + j + 1);
                sprite.LayerMapSet(layerId, layer);
                sprite.LayerSetSprite(layerId, rsi);
            }

            sprite.LayerSetVisible(layerId, visible);

            if (!visible || setting == null) // this is kinda implied
            {
                continue;
            }
            
            if (setting.MarkingsMatchSkin) // Slimes use this for hair
            {
                sprite.LayerSetColor(layerId, skinColor);
            }
            else if (markingPrototype.ForcedColoring || colors == null )
            {
                sprite.LayerSetColor(layerId, markingColors[j]);
            }
            else
            {
                sprite.LayerSetColor(layerId, colors[j]);
            }
        }
    }

    private void ApplySkinColor(EntityUid uid,
        Color skinColor,
        HumanoidComponent humanoid,
        SpriteComponent spriteComp)
    {
        humanoid.SkinColor = skinColor;

        foreach (var (layer, spriteInfo) in humanoid.BaseLayers)
        {
            if (!spriteInfo.MatchSkin)
            {
                continue;
            }

            var color = skinColor;
            color.A = spriteInfo.LayerAlpha;

            SetBaseLayerColor(uid, layer, color, spriteComp);
        }
    }

    private void SetBaseLayerColor(EntityUid uid, HumanoidVisualLayers layer, Color color,
        SpriteComponent sprite)
    {
        if (!sprite.LayerMapTryGet(layer, out var index))
        {
            return;
        }

        sprite[index].Color = color;
    }

    private bool MergeCustomBaseSprites(EntityUid uid, Dictionary<HumanoidVisualLayers, string> baseSprites,
        Dictionary<HumanoidVisualLayers, CustomBaseLayerInfo>? customBaseSprites,
        HumanoidComponent humanoid)
    {
        var newBaseLayers = new Dictionary<HumanoidVisualLayers, HumanoidSpeciesSpriteLayer>();

        foreach (var (key, id) in baseSprites)
        {
            var sexMorph = humanoid.Sex switch
            {
                Sex.Male when HumanoidVisualLayersExtension.HasSexMorph(key) => $"{id}Male",
                Sex.Female when HumanoidVisualLayersExtension.HasSexMorph(key) => $"{id}Female",
                _ => id
            };

            if (!_prototypeManager.TryIndex(sexMorph, out HumanoidSpeciesSpriteLayer? baseLayer))
            {
                continue;
            }

            if (!newBaseLayers.TryAdd(key, baseLayer))
            {
                newBaseLayers[key] = baseLayer;
            }
        }

        if (customBaseSprites == null)
        {
            return IsDirty(newBaseLayers);
        }

        foreach (var (key, info) in customBaseSprites)
        {
            if (!_prototypeManager.TryIndex(info.ID, out HumanoidSpeciesSpriteLayer? baseLayer))
            {
                continue;
            }

            if (!newBaseLayers.TryAdd(key, baseLayer))
            {
                newBaseLayers[key] = baseLayer;
            }
        }

        bool IsDirty(Dictionary<HumanoidVisualLayers, HumanoidSpeciesSpriteLayer> newBaseLayers)
        {
            var dirty = false;
            if (humanoid.BaseLayers.Count != newBaseLayers.Count)
            {
                dirty = true;
                humanoid.BaseLayers = newBaseLayers;
                return dirty;
            }

            foreach (var (key, info) in humanoid.BaseLayers)
            {
                if (!newBaseLayers.TryGetValue(key, out var newInfo))
                {
                    dirty = true;
                    break;
                }

                if (info.ID != newInfo.ID)
                {
                    dirty = true;
                    break;
                }
            }

            if (dirty)
            {
                humanoid.BaseLayers = newBaseLayers;
            }

            return dirty;
        }

        return IsDirty(newBaseLayers);
    }

    private void ApplyBaseSprites(EntityUid uid,
        HumanoidComponent humanoid,
        SpriteComponent spriteComp)
    {
        foreach (var (layer, spriteInfo) in humanoid.BaseLayers)
        {
            if (spriteInfo.BaseSprite != null && spriteComp.LayerMapTryGet(layer, out var index))
            {
                switch (spriteInfo.BaseSprite)
                {
                    case SpriteSpecifier.Rsi rsi:
                        spriteComp.LayerSetRSI(index, rsi.RsiPath);
                        spriteComp.LayerSetState(index, rsi.RsiState);
                        break;
                    case SpriteSpecifier.Texture texture:
                        spriteComp.LayerSetTexture(index, texture.TexturePath);
                        break;
                }
            }
        }
    }
}
