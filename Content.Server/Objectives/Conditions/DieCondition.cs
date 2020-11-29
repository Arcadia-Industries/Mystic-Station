﻿#nullable enable
using Content.Server.Mobs;
using Content.Server.Objectives.Interfaces;
using Content.Shared.GameObjects.Components.Damage;
using JetBrains.Annotations;
using Robust.Shared.Localization;
using Robust.Shared.Utility;

namespace Content.Server.Objectives.Conditions
{
    [UsedImplicitly]
    public class DieCondition : IObjectiveCondition
    {
        private Mind? _mind;

        public IObjectiveCondition GetAssigned(Mind mind)
        {
            return new DieCondition {_mind = mind};
        }

        public string Title => Loc.GetString("Die a glorius death");

        public string Description => Loc.GetString("Die.");

        public SpriteSpecifier Icon => new SpriteSpecifier.Rsi(new ResourcePath("Mobs/Ghosts/ghost_human.rsi"), "icon");

        public float Progress => _mind?.OwnedEntity != null &&
                                 _mind.OwnedEntity.TryGetComponent<IDamageableComponent>(out var damageableComponent) &&
                                    damageableComponent.CurrentState != DamageState.Dead
                                    ? 0f
                                    : 1f;

        public float Difficulty => 1f;

        public bool Equals(IObjectiveCondition? other)
        {
            return other is DieCondition condition && Equals(_mind, condition._mind);
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((DieCondition) obj);
        }

        public override int GetHashCode()
        {
            return (_mind != null ? _mind.GetHashCode() : 0);
        }
    }
}
