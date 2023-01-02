﻿using Robust.Shared.Physics;

namespace Content.Shared.Physics.Pull
{
    public sealed class PullStartedMessage : PullMessage
    {
        public PullStartedMessage(PhysicsComponent puller, PhysicsComponent pulled) :
            base(puller, pulled)
        {
        }
    }
}
