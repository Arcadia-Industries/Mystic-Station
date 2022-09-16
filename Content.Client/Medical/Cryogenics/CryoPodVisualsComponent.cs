﻿using Content.Shared.DragDrop;
using Content.Shared.Medical.Cryogenics;

namespace Content.Client.Medical.Cryogenics;

[RegisterComponent]
public sealed class CryoPodVisualsComponent: SharedCryoPodComponent
{
    public override bool DragDropOn(DragDropEvent eventArgs)
    {
        return false;
    }
}
