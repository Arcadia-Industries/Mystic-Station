﻿#nullable enable
using System.Collections.Generic;
using Content.Server.GameObjects.Components.Destructible.Thresholds;
using Content.Server.GameObjects.EntitySystems;
using Content.Shared.GameObjects.Components.Damage;
using Robust.Shared.GameObjects;
using Robust.Shared.GameObjects.Systems;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.ViewVariables;

namespace Content.Server.GameObjects.Components.Destructible
{
    /// <summary>
    ///     When attached to an <see cref="IEntity"/>, allows it to take damage
    ///     and triggers thresholds when reached.
    /// </summary>
    [RegisterComponent]
    [CustomDataClass(typeof(DestructibleComponentData))]
    public class DestructibleComponent : Component
    {
        private DestructibleSystem _destructibleSystem = default!;

        public override string Name => "Destructible";

        [ViewVariables]
        [DataClassTarget("thresholds")]
        private SortedDictionary<int, Threshold> _lowestToHighestThresholds = new();

        [ViewVariables] private int PreviousTotalDamage { get; set; }

        public IReadOnlyDictionary<int, Threshold> LowestToHighestThresholds => _lowestToHighestThresholds;

        public override void Initialize()
        {
            base.Initialize();

            _destructibleSystem = EntitySystem.Get<DestructibleSystem>();
        }

        public override void HandleMessage(ComponentMessage message, IComponent? component)
        {
            base.HandleMessage(message, component);

            switch (message)
            {
                case DamageChangedMessage msg:
                {
                    if (msg.Damageable.Owner != Owner)
                    {
                        break;
                    }

                    foreach (var (damage, threshold) in _lowestToHighestThresholds)
                    {
                        if (threshold.Triggered)
                        {
                            if (threshold.TriggersOnce)
                            {
                                continue;
                            }

                            if (PreviousTotalDamage >= damage)
                            {
                                continue;
                            }
                        }

                        if (msg.Damageable.TotalDamage >= damage)
                        {
                            var thresholdMessage = new DestructibleThresholdReachedMessage(this, threshold, msg.Damageable.TotalDamage, damage);
                            SendMessage(thresholdMessage);

                            threshold.Trigger(Owner, _destructibleSystem);
                        }
                    }

                    PreviousTotalDamage = msg.Damageable.TotalDamage;

                    break;
                }
            }
        }
    }
}
