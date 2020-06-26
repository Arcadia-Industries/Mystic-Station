﻿using System.Collections.Generic;
using System.Linq;
using Content.Server.GameObjects.EntitySystems;
using Robust.Shared.GameObjects.Systems;
using Robust.Shared.ViewVariables;

namespace Content.Server.GameObjects.Components.Disposal
{
    public class DisposalNet
    {
        public DisposalNet()
        {
            var disposalSystem = EntitySystem.Get<DisposalSystem>();
            disposalSystem.Add(this);
            Uid = disposalSystem.NewUid();
        }

        /// <summary>
        /// Unique identifier per DisposalNet
        /// </summary>
        [ViewVariables] public uint Uid { get; }

        /// <summary>
        /// Set of tubes that make up the DisposalNet
        /// </summary>
        private readonly HashSet<DisposalTubeComponent> _tubeList = new HashSet<DisposalTubeComponent>();

        /// <summary>
        /// Set of disposables currently inside this DisposalNet
        /// </summary>
        private readonly HashSet<DisposableComponent> _contents = new HashSet<DisposableComponent>();

        /// <summary>
        /// If true, this DisposalNet will be regenerated from its tubes
        /// during the next update cycle.
        /// </summary>
        [ViewVariables]
        public bool Dirty { get; private set; }

        public void Add(DisposalTubeComponent tube)
        {
            _tubeList.Add(tube);

            foreach (var entity in tube.ContainedEntities)
            {
                if (!entity.TryGetComponent(out DisposableComponent disposable))
                {
                    continue;
                }

                Insert(disposable);
            }
        }

        public void Remove(DisposalTubeComponent tube)
        {
            _tubeList.Remove(tube);

            foreach (var entity in tube.ContainedEntities)
            {
                if (!entity.TryGetComponent(out DisposableComponent disposable))
                {
                    continue;
                }

                Remove(disposable);
            }

            Dirty = true;
        }

        public void Insert(DisposableComponent disposable)
        {
            _contents.Add(disposable);
        }

        public void Remove(DisposableComponent disposable)
        {
            _contents.Remove(disposable);
        }

        private void Dispose()
        {
            foreach (var tube in _tubeList.ToHashSet())
            {
                tube.DisconnectFromNet();
                Remove(tube);
            }

            foreach (var disposable in _contents.ToHashSet())
            {
                disposable.ExitDisposals();
                Remove(disposable);
            }

            _tubeList.Clear();
            _contents.Clear();
            EntitySystem.Get<DisposalSystem>().Remove(this);
        }

        public void MergeNets(DisposalNet net)
        {
            foreach (var tube in net._tubeList)
            {
                tube.ConnectToNet(this);
            }

            net.Dispose();
        }

        public void Reconnect()
        {
            foreach (var tube in _tubeList)
            {
                tube.Reconnecting = true;
            }

            foreach (var tube in _tubeList)
            {
                if (tube.Reconnecting)
                {
                    tube.SpreadDisposalNet();
                }
            }

            Dispose();
        }

        public void Update(float frameTime)
        {
        }
    }
}
