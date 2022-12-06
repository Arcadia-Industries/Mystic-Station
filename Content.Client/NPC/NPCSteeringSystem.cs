using Content.Shared.NPC;
using Content.Shared.NPC.Events;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client.NPC;

public sealed class NPCSteeringSystem : SharedNPCSteeringSystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;

    public bool DebugEnabled
    {
        get => _debugEnabled;
        set
        {
            if (_debugEnabled == value)
                return;

            _debugEnabled = value;

            if (_debugEnabled)
            {
                _overlay.AddOverlay(new NPCSteeringOverlay(EntityManager));
                RaiseNetworkEvent(new RequestNPCSteeringDebugEvent()
                {
                    Enabled = true
                });
            }
            else
            {
                _overlay.RemoveOverlay<NPCSteeringOverlay>();
                RaiseNetworkEvent(new RequestNPCSteeringDebugEvent()
                {
                    Enabled = false
                });

                foreach (var comp in EntityQuery<NPCSteeringComponent>(true))
                {
                    RemCompDeferred<NPCSteeringComponent>(comp.Owner);
                }
            }
        }
    }

    private bool _debugEnabled;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<NPCSteeringDebugEvent>(OnDebugEvent);
        DebugEnabled = true;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        DebugEnabled = false;
    }

    private void OnDebugEvent(NPCSteeringDebugEvent ev)
    {
        if (!DebugEnabled || !Exists(ev.EntityUid))
            return;

        var comp = EnsureComp<NPCSteeringComponent>(ev.EntityUid);
        comp.Direction = ev.Direction;
    }
}

public sealed class NPCSteeringOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly IEntityManager _entManager;

    public NPCSteeringOverlay(IEntityManager entManager)
    {
        _entManager = entManager;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        foreach (var (comp, xform) in _entManager.EntityQuery<NPCSteeringComponent, TransformComponent>(true))
        {
            if (xform.MapID != args.MapId)
            {
                continue;
            }

            var (worldPos, worldRot) = xform.GetWorldPositionRotation();

            if (!args.WorldAABB.Contains(worldPos))
                continue;

            var rotationOffset = worldRot - xform.LocalRotation;
            args.WorldHandle.DrawLine(worldPos, worldPos + rotationOffset.RotateVec(comp.Direction), Color.Blue);
        }
    }
}
