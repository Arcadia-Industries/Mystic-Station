﻿using System;
using Content.Shared.StatusEffect;
using Robust.Shared.GameObjects;

namespace Content.Shared.Speech.EntitySystems
{
    public abstract class SharedSlurredSystem : EntitySystem
    {
        public virtual void DoSlur(EntityUid uid, TimeSpan time, StatusEffectsComponent? status = null) { }
    }
}
