#nullable enable
using Content.Client.GameObjects.Components.Disposal;
using Content.Client.GameObjects.Components.MedicalScanner;
using Content.Shared.GameObjects.Components.Body;
using Content.Shared.GameObjects.Components.Damage;
using Content.Shared.Interfaces.GameObjects.Components;
using Robust.Client.Interfaces.GameObjects.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.Interfaces.Network;
using Robust.Shared.IoC;
using Robust.Shared.Players;

namespace Content.Client.GameObjects.Components.Body
{
    [RegisterComponent]
    [ComponentReference(typeof(IDamageableComponent))]
    [ComponentReference(typeof(ISharedBodyManagerComponent))]
    public class BodyManagerComponent : SharedBodyManagerComponent
    {
        [Dependency] private readonly IEntityManager _entityManager = default!;

        public override void HandleNetworkMessage(ComponentMessage message, INetChannel netChannel, ICommonSession? session = null)
        {
            if (!Owner.TryGetComponent(out ISpriteComponent? sprite))
            {
                return;
            }

            switch (message)
            {
                case BodyPartAddedMessage partAdded:
                    sprite.LayerSetVisible(partAdded.RSIMap, true);
                    sprite.LayerSetRSI(partAdded.RSIMap, partAdded.RSIPath);
                    sprite.LayerSetState(partAdded.RSIMap, partAdded.RSIState);
                    break;
                case BodyPartRemovedMessage partRemoved:
                    sprite.LayerSetVisible(partRemoved.RSIMap, false);

                    if (!partRemoved.Dropped.HasValue ||
                        !_entityManager.TryGetEntity(partRemoved.Dropped.Value, out var entity) ||
                        !entity.TryGetComponent(out ISpriteComponent? droppedSprite))
                    {
                        break;
                    }

                    var color = sprite[partRemoved.RSIMap].Color;

                    droppedSprite.LayerSetColor(0, color);
                    break;
                case MechanismSpriteAddedMessage mechanismAdded:
                    sprite.LayerSetVisible(mechanismAdded.RSIMap, true);
                    break;
                case MechanismSpriteRemovedMessage mechanismRemoved:
                    sprite.LayerSetVisible(mechanismRemoved.RSIMap, false);
                    break;
            }
        }
    }
}
