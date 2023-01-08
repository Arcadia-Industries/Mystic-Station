using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;

namespace Content.Client.UserInterface.Controls;

/// <summary>
///     Element of a <see cref="FancyTree"/>
/// </summary>
[GenerateTypedNameReferences]
public sealed partial class TreeItem : PanelContainer
{
    public const string StyleClassExpanded = "expanded";
    public const string StyleClassCollapsed = "collapsed";
    public const string StyleClassSelected = "selected";
    public const string StyleClassTreeButton = "fancy-tree-button";
    public const string StyleClassEvenRow = "even-row";
    public const string StyleClassOddRow = "odd-row";

    public bool Collapsible { get; private set; } = true;

    public object? Metadata;
    public int Index;
    public FancyTree Tree = default!;
    public event Action<TreeItem>? OnSelected;
    public event Action<TreeItem>? OnDeselected;

    public bool Expanded { get; private set; } = false;

    public TreeItem()
    {
        RobustXamlLoader.Load(this);
        Button.AddStyleClass(StyleClassTreeButton);
        Button.AddStyleClass(StyleClassCollapsed);
        Icon.AddStyleClass(StyleClassTreeButton);
        Icon.AddStyleClass(StyleClassCollapsed);
        Body.OnChildAdded += OnItemAdded;
        Body.OnChildRemoved += OnItemRemoved;
    }

    private void OnItemRemoved(Control obj)
    {
        if (Body.ChildCount == 0)
        {
            SetExpanded(false);
            Icon.Visible = false;
        }
    }

    private void OnItemAdded(Control obj)
    {
        if (Expanded && Body.ChildCount == 1)
            SetExpanded(true);

        Icon.Visible = Collapsible;
    }

    public void SetExpanded(bool value)
    {
        Expanded = value;
        Body.Visible = Body.ChildCount > 0 && value;

        if (value && Body.Visible)
        {
            Button.AddStyleClass(StyleClassExpanded);
            Button.RemoveStyleClass(StyleClassCollapsed);
            Icon.AddStyleClass(StyleClassExpanded);
            Icon.RemoveStyleClass(StyleClassCollapsed);
        }
        else
        {
            Button.AddStyleClass(StyleClassCollapsed);
            Button.RemoveStyleClass(StyleClassExpanded);
            Icon.AddStyleClass(StyleClassCollapsed);
            Icon.RemoveStyleClass(StyleClassExpanded);
        }

        Tree.QueueRowStyleUpdate();
    }

    public void SetSelected(bool value)
    {
        if (value)
        {
            OnSelected?.Invoke(this);
            Button.AddStyleClass(StyleClassSelected);
            Icon.AddStyleClass(StyleClassSelected);
        }
        else
        {
            OnDeselected?.Invoke(this);
            Button.RemoveStyleClass(StyleClassSelected);
            Icon.RemoveStyleClass(StyleClassSelected);
        }
    }

    public void SetCollapisble(bool value)
    {
        if (value == Collapsible)
            return;

        Icon.Visible = value && Body.ChildCount > 1;
        Collapsible = value;
        if (!value && !Expanded)
            SetExpanded(true);
    }
}
