using Content.Client.UserInterface.Controls;
using Content.Shared.Mech;
using Content.Shared.Mech.Components;
using Robust.Client.AutoGenerated;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;

namespace Content.Client.Mech.Ui;

[GenerateTypedNameReferences]
public sealed partial class MechMenu : FancyWindow
{
    [Dependency] private readonly IEntityManager _ent = default!;

    private EntityUid _mech;

    public event Action<EntityUid, bool>? OnEnableButtonPressed;
    public event Action<EntityUid>? OnRemoveButtonPressed;

    public MechMenu(EntityUid mech)
    {
        RobustXamlLoader.Load(this);
        IoCManager.InjectDependencies(this);

        _mech = mech;

        if (!_ent.TryGetComponent<SpriteComponent>(mech, out var sprite))
            return;

        MechView.Sprite = sprite;
    }

    public void UpdateMechStats()
    {
        if (!_ent.TryGetComponent<SharedMechComponent>(_mech, out var mechComp))
            return;

        IntegrityDisplay.Text = Loc.GetString("mech-integrity-display", ("amount", mechComp.Integrity));
        EnergyDisplay.Text = Loc.GetString("mech-energy-display", ("amount", mechComp.Energy));
        SlotDisplay.Text = Loc.GetString("mech-slot-display",
            ("amount", mechComp.MaxEquipmentAmount - mechComp.EquipmentContainer.ContainedEntities.Count));
    }

    public void UpdateEquipmentView(MechBoundUserInterfaceState state)
    {
        EquipmentControlContainer.Children.Clear();
        foreach (var info in state.EquipmentInfo)
        {
            var ent = info.Equipment;

            if (!_ent.TryGetComponent<SpriteComponent>(ent, out var sprite) ||
                !_ent.TryGetComponent<MetaDataComponent>(ent, out var metaData))
                continue;

            var control = new MechEquipmentControl(metaData.EntityName, info, sprite);

            control.OnEnableButtonPressed += b => OnEnableButtonPressed?.Invoke(ent, b);
            control.OnRemoveButtonPressed += () => OnRemoveButtonPressed?.Invoke(ent);

            EquipmentControlContainer.AddChild(control);
        }
    }
}

