using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.Client.Doors;

public sealed class DoorSystem : SharedDoorSystem
{
    [Dependency] private readonly AnimationPlayerSystem _animationSystem = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DoorComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    protected override void OnComponentInit(EntityUid uid, DoorComponent comp, ComponentInit args)
    {
        comp.OpenAnimation = new Animation()
        {
            Length = TimeSpan.FromSeconds(comp.OpeningAnimationTime),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick()
                {
                    LayerKey = DoorVisualLayers.Base,
                    KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(comp.OpeningSpriteState, 0f) }
                }
            },
        };

        comp.CloseAnimation = new Animation()
        {
            Length = TimeSpan.FromSeconds(comp.ClosingAnimationTime),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick()
                {
                    LayerKey = DoorVisualLayers.Base,
                    KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(comp.ClosingSpriteState, 0f) }
                }
            },
        };

        comp.EmaggingAnimation = new Animation ()
        {
            Length = TimeSpan.FromSeconds(comp.EmaggingAnimationTime),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick()
                {
                    LayerKey = DoorVisualLayers.BaseUnlit,
                    KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(comp.EmaggingSpriteState, 0f) }
                }
            },
        };
    }

    private void OnAppearanceChange(EntityUid uid, DoorComponent comp, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if(!AppearanceSystem.TryGetData<DoorState>(uid, DoorVisuals.State, out var state, args.Component))
            state = DoorState.Closed;
        
        var animPlayer = Comp<AnimationPlayerComponent>(uid);
        if (_animationSystem.HasRunningAnimation(uid, animPlayer, DoorComponent.AnimationKey))
            _animationSystem.Stop(uid, animPlayer, DoorComponent.AnimationKey); // Halt all running anomations.

        if (AppearanceSystem.TryGetData<string>(uid, DoorVisuals.BaseRSI, out var baseRsi, args.Component))
        {
            if (!_resourceCache.TryGetResource<RSIResource>(SharedSpriteComponent.TextureRoot / baseRsi, out var res))
            {
                Logger.Error("Unable to load RSI '{0}'. Trace:\n{1}", baseRsi, Environment.StackTrace);
            }
            foreach (ISpriteLayer layer in args.Sprite.AllLayers)
            {
                layer.Rsi = res?.RSI;
            }
        }

        switch(state)
        {
            case DoorState.Open:
                args.Sprite.LayerSetState(DoorVisualLayers.Base, comp.OpenSpriteState);
                break;
            case DoorState.Closed:
                args.Sprite.LayerSetState(DoorVisualLayers.Base, comp.ClosedSpriteState);
                break;
            case DoorState.Opening:
                if (animPlayer != null)
                    _animationSystem.Play(uid, animPlayer, (Animation)comp.OpenAnimation, DoorComponent.AnimationKey);
                break;
            case DoorState.Closing:
                if (comp.CurrentlyCrushing.Count == 0 && animPlayer != null)
                    _animationSystem.Play(uid, animPlayer, (Animation)comp.CloseAnimation, DoorComponent.AnimationKey);
                else
                    goto case DoorState.Closed;
                break;
            case DoorState.Denying:
            case DoorState.Welded:
                break;
            case DoorState.Emagging:
                if (animPlayer != null)
                    _animationSystem.Play(uid, animPlayer, (Animation)comp.EmaggingAnimation, AirlockVisualizerComponent.AnimationKey);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    // Gotta love it when both the client-side and server-side sprite components both have a draw depth, but for
    // whatever bloody reason the shared component doesn't.
    protected override void UpdateAppearance(EntityUid uid, DoorComponent? door = null)
    {
        if (!Resolve(uid, ref door))
            return;

        base.UpdateAppearance(uid, door);

        if (TryComp(uid, out SpriteComponent? sprite))
        {
            sprite.DrawDepth = (door.State == DoorState.Open)
                ? door.OpenDrawDepth
                : door.ClosedDrawDepth;
        }
    }

    // TODO AUDIO PREDICT see comments in server-side PlaySound()
    protected override void PlaySound(EntityUid uid, SoundSpecifier soundSpecifier, AudioParams audioParams, EntityUid? predictingPlayer, bool predicted)
    {
        if (GameTiming.InPrediction && GameTiming.IsFirstTimePredicted)
            Audio.Play(soundSpecifier, Filter.Local(), uid, false, audioParams);
    }
}
