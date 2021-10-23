using System;
using Content.Shared.Trigger;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Client.Trigger
{
    public sealed class ProximityTriggerVisualizer : AppearanceVisualizer
    {
        [DataField("animationState")]
        private string? _animationState;

        [DataField("duration")]
        private float _animationDuration = 0.3f;

        private const string AnimKey = "proximity";

        private static Animation _animation = default!;

        public override void InitializeEntity(IEntity entity)
        {
            base.InitializeEntity(entity);

            if (_animationState == null) return;

            entity.EnsureComponent<AnimationPlayerComponent>();

            // TODO: Need light and shit for portable flasher but I just want this merged and it's not my PR
            _animation = new Animation
            {
                Length = TimeSpan.FromSeconds(_animationDuration),
                AnimationTracks = {new AnimationTrackSpriteFlick
                {
                    LayerKey = ProximityTriggerVisualLayers.Base,
                    KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(_animationState, 0f)}

                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(PointLightComponent),
                    InterpolationMode = AnimationInterpolationMode.Nearest,
                    Property = nameof(PointLightComponent.Radius),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(0.1f, 0),
                        new AnimationTrackProperty.KeyFrame(3f, 0.1f),
                        new AnimationTrackProperty.KeyFrame(0.1f, 0.5f)
                    }
                }
                }
            };
        }

        public override void OnChangeData(AppearanceComponent component)
        {
            base.OnChangeData(component);

            if (!component.Owner.TryGetComponent(out SpriteComponent? spriteComponent)) return;

            var animSystem = EntitySystem.Get<AnimationPlayerSystem>();
            component.Owner.TryGetComponent(out AnimationPlayerComponent? player);
            component.TryGetData(ProximityTriggerVisualState.State, out ProximityTriggerVisuals state);

            switch (state)
            {
                case ProximityTriggerVisuals.Inactive:
                    if (player != null)
                        animSystem.Stop(player, AnimKey);

                    spriteComponent.LayerSetState(ProximityTriggerVisualLayers.Base, "on");
                    break;
                case ProximityTriggerVisuals.Active:
                    if (_animationState == null || player == null ||
                        animSystem.HasRunningAnimation(player, AnimKey)) return;
                    
                    animSystem.Play(player, _animation, AnimKey);
                    break;
                default:
                    spriteComponent.LayerSetState(ProximityTriggerVisualLayers.Base, "off");
                    break;
            }
        }
    }

    public enum ProximityTriggerVisualLayers : byte
    {
        Base,
    }
}
