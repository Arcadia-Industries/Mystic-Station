using Content.Shared.CriminalRecords;
using Content.Client.UserInterface.Controls;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.XAML;

namespace Content.Client.CriminalRecords;

/// <summary>
/// Window opened when Crime History button is pressed
/// </summary>
[GenerateTypedNameReferences]
public sealed partial class CrimeHistoryWindow : FancyWindow
{
    public Action<string>? OnAddHistory;
    public Action<uint>? OnDeleteHistory;

    private uint? _index;

    public CrimeHistoryWindow()
    {
        RobustXamlLoader.Load(this);
        IoCManager.InjectDependencies(this);

        // deselect so when reopening the window it doesnt try to use invalid index
        OnClose += () => { _index = null; };

        AddButton.OnPressed += _ =>
        {
            // TODO: open window to ask for history line
            /*if (string.IsNullOrEmpty(HistoryLineEdit.Text))
                return;

            OnAddHistory?.Invoke(HistoryLineEdit.Text);
            HistoryLineEdit.Clear();
            // adding deselects so prevent deleting yeah
            _index = null;
            DeleteButton.Disabled = true;*/
        };
        DeleteButton.OnPressed += _ =>
        {
            if (_index is {} index)
            {
                OnDeleteHistory?.Invoke(index);
                // prevent total spam wiping
                History.ClearSelected();
                _index = null;
                DeleteButton.Disabled = true;
            }
        };

        History.OnItemSelected += args =>
        {
            _index = (uint) args.ItemIndex;
            DeleteButton.Disabled = false;
        };
        History.OnItemDeselected += args =>
        {
            _index = null;
            DeleteButton.Disabled = true;
        };
    }

    public void UpdateHistory(CriminalRecord record, bool access)
    {
        History.Clear();
        Editing.Visible = access;

        foreach (var entry in record.History)
        {
            var time = entry.AddTime;
            var line = $"{time.Hours:00}:{time.Minutes:00}:{time.Seconds:00} - {entry.Crime}";
            History.AddItem(line);
        }

        // deselect if something goes wrong
        if (_index is {} index && record.History.Count >= index)
            _index = null;
    }
}
