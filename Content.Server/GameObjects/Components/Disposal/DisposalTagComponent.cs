using Content.Shared.GameObjects.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;
using Robust.Shared.ViewVariables;

namespace Content.Server.GameObjects.Components.Disposal
{
    [RegisterComponent]
    public class DisposalTagComponent : Component, IExamine
    {
        public override string Name => "DisposalTagComponent";

        [ViewVariables(VVAccess.ReadWrite)]
        public string Tag;

        public override void ExposeData(ObjectSerializer serializer)
        {
            base.ExposeData(serializer);
            serializer.DataField(ref Tag, "tag", null);
        }

        public void Examine(FormattedMessage message, bool inDetailsRange)
        {
            message.AddText("It seems to have a disposaltag attached.");
            if(inDetailsRange) message.AddText($"The tag reads: \"{Tag}\".");
        }
    }
}
