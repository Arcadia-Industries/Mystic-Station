using System.Linq;
using Content.Shared.Markings;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Humanoid;

[GenerateTypedNameReferences]
public sealed partial class SingleMarkingPicker : BoxContainer
{
    [Dependency] private MarkingManager _markingManager = default!;

    /// <summary>
    ///     What happens if a marking is selected.
    ///     It will send the 'slot' (marking index)
    ///     and the selected marking's ID.
    /// </summary>
    public Action<(uint, string)>? OnMarkingSelect;
    /// <summary>
    ///     What happens if a slot is removed.
    ///     This will send the 'slot' (marking index).
    /// </summary>
    public Action<uint>? OnSlotRemove;

    /// <summary>
    ///     What happens when a slot is added.
    /// </summary>
    public Action? OnSlotAdd;

    public Action<uint>? OnSlotSelected;

    /// <summary>
    ///     What happens if a marking's color is changed.
    ///     Sends a 'slot' number, and the marking in question.
    /// </summary>
    public Action<(uint, Marking)>? OnColorChanged;

    // current selected marking
    private Marking? _marking;
    // current selected slot
    private uint _slot;

    // amount of slots to show
    private uint _pointsUsed;
    private uint _totalPoints;

    private bool _ignoreItemSelected;

    private MarkingCategories _category;
    public MarkingCategories Category
    {
        get => _category;
        set
        {
            if (!string.IsNullOrEmpty(_species))
            {
                PopulateList();
            }
        }
    }

    private string? _species;

    public SingleMarkingPicker()
    {
        IoCManager.InjectDependencies(this);

        MarkingList.OnItemSelected += SelectMarking;
        AddButton.OnPressed += _ =>
        {
            OnSlotAdd!();
        };

        SlotSelector.OnItemSelected += args =>
        {
            OnSlotSelected!((uint) args.Button.SelectedId);
        };

        RemoveButton.OnPressed += _ =>
        {
            OnSlotRemove!(_slot);
        };
    }

    public void UpdateData(Marking marking, string species, uint selectedSlot, uint pointsUsed, uint totalPoints)
    {
        _species = species;
        _marking = marking;
        _slot = selectedSlot;
        _pointsUsed = pointsUsed;
        _totalPoints = totalPoints;

        PopulateList();
        PopulateColors();
        PopulateSlotSelector();
    }

    public void PopulateList()
    {
        if (string.IsNullOrEmpty(_species))
        {
            throw new ArgumentException("Tried to populate marking list without a set species!");
        }

        var dict = _markingManager.MarkingsByCategoryAndSpecies(Category, _species);

        if (_marking == null)
        {
            _marking = dict.First().Value.AsMarking();
        }

        foreach (var (id, marking) in dict)
        {
            var item = MarkingList.AddItem(id);
            item.Metadata = marking.ID;

            if (_marking.MarkingId == marking.ID)
            {
                _ignoreItemSelected = true;
                item.Selected = true;
                _ignoreItemSelected = false;
            }
        }
    }

    private void PopulateColors()
    {
        if (_marking == null
            || !_markingManager.TryGetMarking(_marking, out var proto))
        {
            return;
        }

        ColorSelectorContainer.DisposeAllChildren();
        ColorSelectorContainer.RemoveAllChildren();

        if (_marking.MarkingColors.Count != proto.Sprites.Count)
        {
            _marking = new Marking(_marking.MarkingId, proto.Sprites.Count);
        }

        for (var i = 0; i < _marking.MarkingColors.Count; i++)
        {
            var selector = new ColorSelectorSliders();
            selector.Color = _marking.MarkingColors[i];

            var colorIndex = i;
            selector.OnColorChanged += color =>
            {
                _marking.SetColor(colorIndex, color);
                OnColorChanged!((_slot, _marking));
            };
        }
    }

    private void SelectMarking(ItemList.ItemListSelectedEventArgs args)
    {
        if (_ignoreItemSelected)
        {
            return;
        }

        var id = (string) MarkingList[args.ItemIndex].Metadata!;
        if (!_markingManager.Markings.TryGetValue(id, out var proto))
        {
            throw new ArgumentException("Attempted to select non-existent marking.");
        }

        _marking = proto.AsMarking();

        OnMarkingSelect!((_slot, id));
    }

    // Slot logic

    private void PopulateSlotSelector()
    {
        // if the total amount of points available is one,
        // we don't really need to have visible slots, right?
        if (_totalPoints == 1)
        {
            SlotSelectorContainer.Visible = false;
            return;
        }

        SlotSelectorContainer.Visible = true;
        SlotSelector.Clear();

        for (var i = 0; i < _pointsUsed; i++)
        {
            SlotSelector.AddItem($"Slot {i + 1}", i);

            if (i == _slot)
            {
                SlotSelector.SelectId(i);
            }
        }
    }
}
