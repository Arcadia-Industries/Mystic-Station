using Robust.Shared.GameObjects;

namespace Content.Server.GameObjects.Components.Sound
{
    /// <summary>
    /// Simple sound emitter that emits sound on LandEvent
    /// </summary>
    [RegisterComponent]
    public class EmitSoundOnLandComponent : BaseEmitSoundComponent
    {
        /// <inheritdoc />
        ///
        public override string Name => "EmitSoundOnLand";
    }
}
