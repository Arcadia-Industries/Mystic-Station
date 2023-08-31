using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared.Storage
{
    [RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
    public sealed partial class StorageComponent : Component
    {
        // TODO: This fucking sucks
        [ViewVariables(VVAccess.ReadWrite), DataField("isOpen"), AutoNetworkedField]
        public bool IsUiOpen;

        [ViewVariables]
        public Container Storage = default!;

        public readonly Dictionary<EntityUid, int> SizeCache = new();

        private bool _occludesLight = true;

        [DataField("quickInsert")]
        public bool QuickInsert; // Can insert storables by "attacking" them with the storage entity

        [DataField("clickInsert")]
        public bool ClickInsert = true; // Can insert stuff by clicking the storage entity with it

        [DataField("areaInsert")]
        public bool AreaInsert;  // "Attacking" with the storage entity causes it to insert all nearby storables after a delay

        [DataField("areaInsertRadius")]
        public int AreaInsertRadius = 1;

        [DataField("whitelist")]
        public EntityWhitelist? Whitelist;

        [DataField("blacklist")]
        public EntityWhitelist? Blacklist;

        public int StorageUsed;

        [DataField("capacity")]
        public int StorageCapacityMax = 10000;

        [DataField("storageOpenSound")]
        public SoundSpecifier? StorageOpenSound { get; set; } = new SoundCollectionSpecifier("storageRustle");

        [DataField("storageInsertSound")]
        public SoundSpecifier? StorageInsertSound { get; set; } = new SoundCollectionSpecifier("storageRustle");

        [DataField("storageRemoveSound")] public SoundSpecifier? StorageRemoveSound;

        [DataField("storageCloseSound")] public SoundSpecifier? StorageCloseSound;

        [Serializable, NetSerializable]
        public sealed class StorageBoundUserInterfaceState : BoundUserInterfaceState
        {
            public readonly List<EntityUid> StoredEntities;
            public readonly int StorageSizeUsed;
            public readonly int StorageCapacityMax;

            public StorageBoundUserInterfaceState(List<EntityUid> storedEntities, int storageSizeUsed, int storageCapacityMax)
            {
                StoredEntities = storedEntities;
                StorageSizeUsed = storageSizeUsed;
                StorageCapacityMax = storageCapacityMax;
            }
        }

        [Serializable, NetSerializable]
        public sealed class StorageInsertItemMessage : BoundUserInterfaceMessage
        {
        }

        [Serializable, NetSerializable]
        public sealed class StorageInteractWithItemEvent : BoundUserInterfaceMessage
        {
            public readonly EntityUid InteractedItemUID;
            public StorageInteractWithItemEvent(EntityUid interactedItemUID)
            {
                InteractedItemUID = interactedItemUID;
            }
        }

        [Serializable, NetSerializable]
        public enum StorageUiKey
        {
            Key,
        }
    }

    /// <summary>
    /// Network event for displaying an animation of entities flying into a storage entity
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class AnimateInsertingEntitiesEvent : EntityEventArgs
    {
        public readonly EntityUid Storage;
        public readonly List<EntityUid> StoredEntities;
        public readonly List<EntityCoordinates> EntityPositions;
        public readonly List<Angle> EntityAngles;

        public AnimateInsertingEntitiesEvent(EntityUid storage, List<EntityUid> storedEntities, List<EntityCoordinates> entityPositions, List<Angle> entityAngles)
        {
            Storage = storage;
            StoredEntities = storedEntities;
            EntityPositions = entityPositions;
            EntityAngles = entityAngles;
        }
    }

    [NetSerializable]
    [Serializable]
    public enum StorageVisuals : byte
    {
        Open,
        HasContents,
        CanLock,
        Locked
    }
}
