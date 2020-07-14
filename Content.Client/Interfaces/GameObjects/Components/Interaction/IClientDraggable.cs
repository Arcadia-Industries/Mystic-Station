using System;
using Robust.Shared.Interfaces.GameObjects;

namespace Content.Client.Interfaces.GameObjects.Components.Interaction
{
    /// <summary>
    /// This interface allows a local client to initiate dragging of the component's entity by mouse, for drag and
    /// drop interactions. The actual logic of what happens on drop
    /// is handled by IDragDrop
    /// </summary>
    public interface IClientDraggable
    {

        /// <summary>
        /// Invoked on entities visible to the user to check if this component's entity
        /// can be dropped on the indicated target entity. No need to check range / reachability in here.
        /// </summary>
        /// <returns>true iff target is a valid target to be dropped on by this
        /// component's entity. Returning true will cause the target entity to be highlighted as a potential
        /// target and allow dropping when in range.</returns>
        bool ClientCanDropOn(CanDropEventArgs eventArgs);

        /// <summary>
        /// Invoked clientside when user is attempting to initiate a drag with this component's entity
        /// in range. Return true if the drag should be initiated. It's fine to
        /// return true even if there wouldn't be any valid targets - just return true
        /// if this entity is in a "draggable" state.
        /// </summary>
        /// <param name="eventArgs"></param>
        /// <returns>true iff drag should be initiated</returns>
        bool ClientCanDrag(CanDragEventArgs eventArgs);
    }

    public class CanDropEventArgs : EventArgs
    {
        public CanDropEventArgs(IEntity user, IEntity dragged, IEntity target)
        {
            User = user;
            Dragged = dragged;
            Target = target;
        }

        public IEntity User { get; }
        public IEntity Dragged { get; }
        public IEntity Target { get; }
    }

    public class CanDragEventArgs : EventArgs
    {
        public CanDragEventArgs(IEntity user, IEntity dragged)
        {
            User = user;
            Dragged = dragged;
        }

        public IEntity User { get; }
        public IEntity Dragged { get; }
    }
}
