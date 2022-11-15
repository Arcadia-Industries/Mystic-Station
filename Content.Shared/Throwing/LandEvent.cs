using JetBrains.Annotations;

namespace Content.Shared.Throwing
{
    /// <summary>
    ///     Raised when an entity that was thrown lands. This occurs before they stop moving and is when their tile-friction is reapplied.
    /// </summary>
    [PublicAPI]
    public sealed class LandEvent : EntityEventArgs
    {
        public EntityUid? User;
    }

    /// <summary>
    /// Raised when a thrown entity is no longer moving.
    /// </summary>
    [ByRefEvent]
    public struct StopThrowEvent
    {
        public EntityUid? User;

        public StopThrowEvent(EntityUid? user)
        {
            User = user;
        }
    }
}
