﻿using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Content.Client.Message;
using Content.Shared.GPS;

namespace Content.Client.GPS.UI
{
    [GenerateTypedNameReferences]
    public sealed partial class HandheldGPSMenu : DefaultWindow
    {
        private HandheldGPSBoundUserInterface Owner { get; }

        public HandheldGPSMenu(HandheldGPSBoundUserInterface owner, EntityUid ownerUid)
        {
            IoCManager.InjectDependencies(this);
            RobustXamlLoader.Load(this);
            Owner = owner;
        }

        public void Populate(UpdateGPSLocationState state)
        {
            string posText = "Error";
            if (state.Coordinates != null)
            {
                var pos =  state.Coordinates.Value.Position;;
                var x = (int) pos.X;
                var y = (int) pos.Y;
                posText = $"({x}, {y})";
            }
            GPSInfo.SetMarkup(posText);
        }
    }
}
