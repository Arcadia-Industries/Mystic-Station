﻿using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;

namespace Content.Client.UserInterface.UIWindows;

[GenerateTypedNameReferences]
public sealed partial class SandboxWindow : DefaultWindow
{
    public SandboxWindow()
    {
        RobustXamlLoader.Load(this);
    }
}
