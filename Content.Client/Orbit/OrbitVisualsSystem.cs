﻿using Content.Shared.Follower;
using Content.Shared.Follower.Components;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.Random;

namespace Content.Client.Orbit;

public sealed class OrbitVisualsSystem : VisualizerSystem<OrbitVisualsComponent>
{
    [Dependency] private readonly IRobustRandom _robustRandom = default!;

    private readonly string _orbitAnimationKey = "orbiting";
    private readonly string _orbitStopKey = "orbiting_stop";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OrbitVisualsComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<OrbitVisualsComponent, AnimationCompletedEvent>(OnAnimationCompleted);
    }

    private void OnComponentInit(EntityUid uid, OrbitVisualsComponent component, ComponentInit args)
    {
        component.OrbitDistance =
            _robustRandom.NextFloat(0.75f * component.OrbitDistance, 1.25f * component.OrbitDistance);

        component.OrbitLength = _robustRandom.NextFloat(0.5f * component.OrbitLength, 1.5f * component.OrbitLength);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        foreach (var (orbit, sprite) in EntityManager.EntityQuery<OrbitVisualsComponent, ISpriteComponent>())
        {
            var angle = new Angle(Math.PI * 2 * orbit.Orbit);
            var vec = angle.RotateVec(new Vector2(orbit.OrbitDistance, 0));

            sprite.Rotation = angle;
            sprite.Offset = vec;
        }
    }

    protected override void OnAppearanceChange(EntityUid uid, OrbitVisualsComponent component, ref AppearanceChangeEvent args)
    {
        if (!args.Component.TryGetData<bool>(OrbitingVisuals.IsOrbiting, out var orbiting))
            return;

        if (!TryComp<ISpriteComponent>(uid, out var sprite))
            return;

        var animationPlayer = EntityManager.EnsureComponent<AnimationPlayerComponent>(uid);

        if (orbiting)
        {
            if (animationPlayer.HasRunningAnimation(_orbitAnimationKey))
                return;

            animationPlayer.Play(GetOrbitAnimation(component), _orbitAnimationKey);
        }
        else
        {
            RemComp<OrbitVisualsComponent>(uid);
            if (animationPlayer.HasRunningAnimation(_orbitAnimationKey))
            {
                animationPlayer.Stop(_orbitAnimationKey);
            }

            if (!animationPlayer.HasRunningAnimation(_orbitStopKey))
            {
                animationPlayer.Play(GetStopAnimation(component, sprite), _orbitStopKey);
            }
        }
    }

    private void OnAnimationCompleted(EntityUid uid, OrbitVisualsComponent component, AnimationCompletedEvent args)
    {
        if (args.Key == _orbitAnimationKey)
        {
            if(EntityManager.TryGetComponent(uid, out AnimationPlayerComponent? animationPlayer))
                animationPlayer.Play(GetOrbitAnimation(component), _orbitAnimationKey);
        }
    }

    private Animation GetOrbitAnimation(OrbitVisualsComponent component)
    {
        var length = component.OrbitLength;

        return new Animation()
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(OrbitVisualsComponent),
                    Property = nameof(OrbitVisualsComponent.Orbit),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(0.0f, 0f),
                        new AnimationTrackProperty.KeyFrame(1.0f, length),
                    },
                    InterpolationMode = AnimationInterpolationMode.Linear
                }
            }
        };
    }

    private Animation GetStopAnimation(OrbitVisualsComponent component, ISpriteComponent sprite)
    {
        var length = component.OrbitStopLength;

        return new Animation()
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(ISpriteComponent),
                    Property = nameof(ISpriteComponent.Offset),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(sprite.Offset, 0f),
                        new AnimationTrackProperty.KeyFrame(Vector2.Zero, length),
                    },
                    InterpolationMode = AnimationInterpolationMode.Linear
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(ISpriteComponent),
                    Property = nameof(ISpriteComponent.Rotation),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(sprite.Rotation.Reduced(), 0f),
                        new AnimationTrackProperty.KeyFrame(Angle.Zero, length),
                    },
                    InterpolationMode = AnimationInterpolationMode.Linear
                }
            }
        };
    }
}
