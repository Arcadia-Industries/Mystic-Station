using System.Numerics;
using System.Text;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.MedicalScanner;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Prototypes;

namespace Content.Client.HealthAnalyzer.UI
{
    [GenerateTypedNameReferences]
    public sealed partial class HealthAnalyzerWindow : DefaultWindow
    {
        public HealthAnalyzerWindow()
        {
            RobustXamlLoader.Load(this);
        }

        public void Populate(HealthAnalyzerScannedUserMessage msg)
        {
            var text = new StringBuilder();
            var entities = IoCManager.Resolve<IEntityManager>();
            var target = entities.GetEntity(msg.TargetEntity);

            if (msg.TargetEntity != null && entities.TryGetComponent<DamageableComponent>(target, out var damageable))
            {
                string entityName = "Unknown";
                if (msg.TargetEntity != null &&
                    entities.HasComponent<MetaDataComponent>(target.Value))
                {
                    entityName = Identity.Name(target.Value, entities);
                }

                IReadOnlyDictionary<string, FixedPoint2> damagePerGroup = damageable.DamagePerGroup;
                IReadOnlyDictionary<string, FixedPoint2> damagePerType = damageable.Damage.DamageDict;

                text.Append($"{Loc.GetString("health-analyzer-window-entity-health-text", ("entityName", entityName))}\n\n");


                text.Append($"{Loc.GetString("health-analyzer-window-entity-temperature-text", ("temperature", float.IsNaN(msg.Temperature) ? "N/A" : $"{msg.Temperature - 273f:F1} °C"))}\n");


                text.Append($"{Loc.GetString("health-analyzer-window-entity-blood-level-text", ("bloodLevel", float.IsNaN(msg.BloodLevel) ? "N/A" : $"{msg.BloodLevel * 100:F1} %"))}\n\n");


                // Damage
                text.Append($"{Loc.GetString("health-analyzer-window-entity-damage-total-text", ("amount", damageable.TotalDamage))}\n");

                HashSet<string> shownTypes = new();

                var protos = IoCManager.Resolve<IPrototypeManager>();

                // Show the total damage and type breakdown for each damage group.
                foreach (var (damageGroupId, damageAmount) in damagePerGroup)
                {
                    if (damageAmount == 0)
                    {
                        continue;
                    }
                    text.Append($"\n{Loc.GetString("health-analyzer-window-damage-group-text", ("damageGroup", Loc.GetString("health-analyzer-window-damage-group-" + damageGroupId)), ("amount", damageAmount))}");

                    // Show the damage for each type in that group.
                    var group = protos.Index<DamageGroupPrototype>(damageGroupId);
                    foreach (var type in group.DamageTypes)
                    {
                        if (damagePerType.TryGetValue(type, out var typeAmount) )
                        {
                            // If damage types are allowed to belong to more than one damage group, they may appear twice here. Mark them as duplicate.
                            if (!shownTypes.Contains(type) && typeAmount > 0)
                            {
                                shownTypes.Add(type);
                                text.Append($"\n- {Loc.GetString("health-analyzer-window-damage-type-text", ("damageType", Loc.GetString("health-analyzer-window-damage-type-" + type)), ("amount", typeAmount))}");
                            }
                        }
                    }
                    text.AppendLine();
                }
                Diagnostics.Text = text.ToString();
                SetSize = new Vector2(250, 600);
            }
            else
            {
                Diagnostics.Text = Loc.GetString("health-analyzer-window-no-patient-data-text");
                SetSize = new Vector2(250, 100);
            }
        }
    }
}
