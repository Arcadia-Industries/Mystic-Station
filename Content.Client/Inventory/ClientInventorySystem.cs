using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Content.Client.Clothing;
using Content.Client.HUD;
using Content.Client.HUD.Widgets;
using Content.Shared.Input;
using Content.Client.Items.Managers;
using Content.Client.UserInterface.Controls;
using Content.Shared.CCVar;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Content.Shared.Interaction.Events;

namespace Content.Client.Inventory
{
    [UsedImplicitly] //TODO: unfuck this
    public sealed class ClientInventorySystem : InventorySystem
    {
        //[Dependency] private readonly IHudManager _hudManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IConfigurationManager _config = default!;
        [Dependency] private readonly IItemSlotManager _itemSlotManager = default!;
        [Dependency] private readonly ClothingSystem _clothingSystem = default!;

        public Action<SlotData>? EntitySlotUpdate = null;
        public Action? OnOpenInventory = null;
        public Action<ClientInventoryComponent>? OnLinkInventory = null;
        public Action? OnUnlinkInventory = null;

        /// <summary>
        /// Stores delegates used to create controls for a given <see cref="InventoryTemplatePrototype"/>.
        /// </summary>

        public override void Initialize()
        {
            base.Initialize();

            CommandBinds.Builder
                .Bind(ContentKeyFunctions.OpenInventoryMenu,
                    InputCmdHandler.FromDelegate(_ => HandleOpenInventoryMenu()))
                .Register<ClientInventorySystem>();

            SubscribeLocalEvent<ClientInventoryComponent, PlayerAttachedEvent>(OnPlayerAttached);
            SubscribeLocalEvent<ClientInventoryComponent, PlayerDetachedEvent>(OnPlayerDetached);

            SubscribeLocalEvent<ClientInventoryComponent, ComponentInit>(OnInit);
            SubscribeLocalEvent<ClientInventoryComponent, ComponentShutdown>(OnShutdown);

            SubscribeLocalEvent<ClientInventoryComponent, DidEquipEvent>(OnDidEquip);
            SubscribeLocalEvent<ClientInventoryComponent, DidUnequipEvent>(OnDidUnequip);

            SubscribeLocalEvent<ClothingComponent, UseInHandEvent>(OnUseInHand);

        }

        private void OnUseInHand(EntityUid uid, ClothingComponent component, UseInHandEvent args)
        {
            if (args.Handled || !component.QuickEquip)
                return;

            QuickEquip(uid, component, args);
        }

        private void OnDidUnequip(EntityUid uid, ClientInventoryComponent component, DidUnequipEvent args)
        {
            UpdateSlot(component, args.Slot, null);
        }

        private void OnDidEquip(EntityUid uid, ClientInventoryComponent component, DidEquipEvent args)
        {
            UpdateSlot(component, args.Slot, args.Equipment);
        }

        private void OnPlayerDetached(EntityUid uid, ClientInventoryComponent component, PlayerDetachedEvent? args = null)
        {
            OnUnlinkInventory?.Invoke();
        }

        private void OnShutdown(EntityUid uid, ClientInventoryComponent component, ComponentShutdown args)
        {
            OnPlayerDetached(uid, component);
        }

        private void OnPlayerAttached(EntityUid uid, ClientInventoryComponent component, PlayerAttachedEvent args)
        {
            OnLinkInventory?.Invoke(component);
        }

        public override void Shutdown()
        {
            CommandBinds.Unregister<ClientInventorySystem>();
            base.Shutdown();
        }

        private void OnInit(EntityUid uid, ClientInventoryComponent component, ComponentInit args)
        {
            _clothingSystem.InitClothing(uid, component);
            if (!_prototypeManager.TryIndex(component.TemplateId, out InventoryTemplatePrototype? invTemplate)) return;
            foreach (var slot in invTemplate.Slots)
            {
                TryAddSlotDef(component, slot);
            }
        }
        public void SetSlotHighlight(ClientInventoryComponent component, string slotName, bool state)
        {
            var oldData = component.SlotData[slotName];
            var newData = component.SlotData[slotName] = new SlotData(oldData, oldData.HeldEntity, state);
            EntitySlotUpdate?.Invoke(newData);
        }
        public void SetHeldEntity(ClientInventoryComponent component, string slotName, EntityUid heldEntity)
        {
            var oldData = component.SlotData[slotName];
            var newData = component.SlotData[slotName] = new SlotData(oldData, heldEntity, oldData.Highlighted);
            EntitySlotUpdate?.Invoke(newData);
        }
        public void UpdateSlot(ClientInventoryComponent component,string slotName ,EntityUid? heldEntity = null, bool highlight = false)
        {
            var newData = component.SlotData[slotName] = new SlotData(component.SlotData[slotName], heldEntity, highlight);
            EntitySlotUpdate?.Invoke(newData);
        }

        public static bool TryAddSlotDef(ClientInventoryComponent component,SlotDefinition newSlotDef)
        {
            var success = component.SlotData.TryAdd(newSlotDef.Name, newSlotDef);
            //TODO: Call update Delegate
            return success;
        }
        public static void RemoveSlotDef(ClientInventoryComponent component, SlotData slotData)
        {
            component.SlotData.Remove(slotData.SlotName);
            //TODO: call update delegate
        }
        public static void RemoveSlotDef(ClientInventoryComponent component, string slotName)
        {
            component.SlotData.Remove(slotName);
            //TODO: call update delegate
        }
        private void HoverInSlotButton(EntityUid uid, string slot, ItemSlotButton button, InventoryComponent? inventoryComponent = null, SharedHandsComponent? hands = null)
        {
            if (!Resolve(uid, ref inventoryComponent))
                return;

            if (!Resolve(uid, ref hands, false))
                return;

            if (hands.ActiveHandEntity is not EntityUid heldEntity)
                return;

            if(!TryGetSlotContainer(uid, slot, out var containerSlot, out var slotDef, inventoryComponent))
                return;

            _itemSlotManager.HoverInSlot(button, heldEntity,
                CanEquip(uid, heldEntity, slot, out _, slotDef, inventoryComponent) &&
                containerSlot.CanInsert(heldEntity, EntityManager));
        }

        private void HandleSlotButtonPressed(EntityUid uid, string slot, ItemSlotButton button,
            GUIBoundKeyEventArgs args)
        {
            if (TryGetSlotEntity(uid, slot, out var itemUid))
                return;

            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            // only raise event if either itemUid is not null, or the user is holding something
            if (itemUid != null || TryComp(uid, out SharedHandsComponent? hands) && hands.ActiveHandEntity != null)
                EntityManager.RaisePredictiveEvent(new UseSlotNetworkMessage(slot));
        }

        private void HandleOpenInventoryMenu()
        {
            OnOpenInventory?.Invoke();
        }

        public struct SlotData
        {
            public readonly SlotDefinition SlotDef;
            public EntityUid? HeldEntity = null;
            public bool Highlighted = false;
            public string SlotName => SlotDef.Name;
            public string SlotDisplayName => SlotDef.DisplayName;
            public string TextureName => SlotDef.TextureName;
            public SlotData(SlotDefinition slotDef,EntityUid? heldEntity = null, bool highlighted = false)
            {
                SlotDef = slotDef;
                HeldEntity = heldEntity;
                Highlighted = highlighted;
            }

            public SlotData(SlotData oldData, EntityUid? heldEntity, bool highlighted = false)
            {
                SlotDef = oldData.SlotDef;
                HeldEntity = heldEntity;
                Highlighted = highlighted;
            }

            public static implicit operator SlotData(SlotDefinition s)
            {
                return new SlotData(s);
            }
            public static implicit operator SlotDefinition(SlotData s)
            {
                return s.SlotDef;
            }

        }
    }
}
