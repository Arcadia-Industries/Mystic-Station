﻿#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using Content.Server.GameObjects.Components.Interactable;
using Content.Server.GameObjects.EntitySystems;
using Content.Shared.GameObjects.Components.Interactable;
using Robust.Shared.GameObjects;
using Robust.Shared.GameObjects.Components;
using Robust.Shared.Interfaces.GameObjects;

namespace Content.Server.GameObjects.Components
{
    [RegisterComponent]
    public class AnchorableComponent : Component, IInteractUsing
    {
        public override string Name => "Anchorable";

        /// <summary>
        ///
        /// </summary>
        /// <param name="user"></param>
        /// <param name="utilizing"></param>
        /// <param name="physics"></param>
        /// <param name="force"></param>
        /// <returns></returns>
        private bool Valid(IEntity user, IEntity? utilizing, [MaybeNullWhen(false)] out PhysicsComponent physics, bool force = false)
        {
            if (!Owner.TryGetComponent(out physics))
            {
                return false;
            }

            if (!force)
            {
                if (utilizing == null ||
                    !utilizing.TryGetComponent(out ToolComponent tool) ||
                    !tool.UseTool(user, Owner, ToolQuality.Anchoring))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        ///     Tries to anchor the owner of this component.
        /// </summary>
        /// <param name="user">The entity doing the anchoring</param>
        /// <param name="utilizing">The tool being used, if any</param>
        /// <param name="force">Whether or not to ignore valid tool checks</param>
        /// <returns>true if anchored, false otherwise</returns>
        public bool TryAnchor(IEntity user, IEntity? utilizing = null, bool force = false)
        {
            if (!Valid(user, utilizing, out var physics, force))
            {
                return false;
            }

            physics.Anchored = true;

            return true;
        }

        /// <summary>
        ///     Tries to unanchor the owner of this component.
        /// </summary>
        /// <param name="user">The entity doing the unanchoring</param>
        /// <param name="utilizing">The tool being used, if any</param>
        /// <param name="force">Whether or not to ignore valid tool checks</param>
        /// <returns>true if unanchored, false otherwise</returns>
        public bool TryUnAnchor(IEntity user, IEntity? utilizing = null, bool force = false)
        {
            if (!Valid(user, utilizing, out var physics, force))
            {
                return false;
            }

            physics.Anchored = false;

            return true;
        }

        /// <summary>
        ///     Tries to toggle the anchored status of this component's owner.
        /// </summary>
        /// <param name="user">The entity doing the unanchoring</param>
        /// <param name="utilizing">The tool being used, if any</param>
        /// <param name="force">Whether or not to ignore valid tool checks</param>
        /// <returns>true if toggled, false otherwise</returns>
        private bool TryToggleAnchor(IEntity user, IEntity? utilizing = null, bool force = false)
        {
            if (!Owner.TryGetComponent(out PhysicsComponent physics))
            {
                return false;
            }

            return physics.Anchored ?
                TryUnAnchor(user, utilizing, force) :
                TryAnchor(user, utilizing, force);
        }

        public override void Initialize()
        {
            base.Initialize();
            Owner.EnsureComponent<PhysicsComponent>();
        }

        bool IInteractUsing.InteractUsing(InteractUsingEventArgs eventArgs)
        {
            return TryToggleAnchor(eventArgs.User, eventArgs.Using);
        }
    }
}
