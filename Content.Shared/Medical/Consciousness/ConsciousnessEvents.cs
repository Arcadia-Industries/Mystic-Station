﻿using Content.Shared.FixedPoint;
using Content.Shared.Medical.Consciousness.Components;

namespace Content.Shared.Medical.Consciousness;

[ByRefEvent]
public record struct UpdateConsciousnessEvent(ConsciousnessComponent Component,
    FixedPoint2 Base, FixedPoint2 Modifier, FixedPoint2 Offset, FixedPoint2 Cap, FixedPoint2 PassoutThreshold);

[ByRefEvent]
public record struct ConsciousnessUpdatedEvent(bool IsConscious, FixedPoint2 Consciousness);
