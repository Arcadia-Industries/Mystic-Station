using System.Linq;
using Content.Server.Access.Systems;
using Content.Server.Cargo.Components;
using Content.Server.MachineLinking.Components;
using Content.Server.MachineLinking.System;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.Cargo;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Events;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Players;

namespace Content.Server.Cargo.Systems
{
    public sealed partial class CargoSystem
    {
        /// <summary>
        /// How much time to wait (in seconds) before increasing bank accounts balance.
        /// </summary>
        private const int Delay = 10;

        /// <summary>
        /// How many points to give to every bank account every second.
        /// </summary>
        private const int PointIncrease = 5;

        /// <summary>
        /// Keeps track of how much time has elapsed since last balance increase.
        /// </summary>
        private float _timer;

        [Dependency] private readonly IdCardSystem _idCardSystem = default!;
        [Dependency] private readonly AccessReaderSystem _accessReaderSystem = default!;
        [Dependency] private readonly SignalLinkerSystem _linker = default!;
        [Dependency] private readonly PopupSystem _popup = default!;
        [Dependency] private readonly StationSystem _station = default!;
        [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;

        private void InitializeConsole()
        {
            SubscribeLocalEvent<CargoOrderConsoleComponent, CargoConsoleAddOrderMessage>(OnAddOrderMessage);
            SubscribeLocalEvent<CargoOrderConsoleComponent, CargoConsoleRemoveOrderMessage>(OnRemoveOrderMessage);
            SubscribeLocalEvent<CargoOrderConsoleComponent, CargoConsoleApproveOrderMessage>(OnApproveOrderMessage);
            SubscribeLocalEvent<CargoOrderConsoleComponent, ComponentInit>(OnInit);
            SubscribeLocalEvent<RoundRestartCleanupEvent>(Reset);
            Reset();
        }


        private void OnInit(EntityUid uid, CargoOrderConsoleComponent orderConsole, ComponentInit args)
        {
            var station = _station.GetOwningStation(uid);
            UpdateOrderState(orderConsole, station);
        }

        private void Reset(RoundRestartCleanupEvent ev)
        {
            Reset();
        }

        private void Reset()
        {
            _timer = 0;
        }

        private void UpdateConsole(float frameTime)
        {
            _timer += frameTime;

            while (_timer > Delay)
            {
                _timer -= Delay;

                foreach (var account in EntityQuery<StationBankAccountComponent>())
                {
                    account.Balance += PointIncrease * Delay;
                }

                foreach (var comp in EntityQuery<CargoOrderConsoleComponent>())
                {
                    // TODO: Also update on opening.
                    if (!_uiSystem.IsUiOpen(comp.Owner, CargoConsoleUiKey.Orders)) continue;

                    var station = _station.GetOwningStation(comp.Owner);
                    UpdateOrderState(comp, station);
                }
            }
        }

        #region Interface

        private void OnApproveOrderMessage(EntityUid uid, CargoOrderConsoleComponent component, CargoConsoleApproveOrderMessage args)
        {
            if (args.Session.AttachedEntity is not {Valid: true} player)
                return;

            var orderDatabase = GetOrderDatabase(component);
            var bankAccount = GetBankAccount(component);

            // No station to deduct from.
            if (orderDatabase == null || bankAccount == null)
            {
                ConsolePopup(args.Session, "No available station");
                PlayDenySound(uid, component);
                return;
            }

            // No order to approve?
            if (!orderDatabase.Orders.TryGetValue(args.OrderNumber, out var order) ||
                order.Approved) return;

            // Invalid order
            if (!_protoMan.TryIndex<CargoProductPrototype>(order.ProductId, out var product))
            {
                ConsolePopup(args.Session, "Invalid product ID");
                PlayDenySound(uid, component);
                return;
            }

            var amount = GetOrderCount(orderDatabase);
            var capacity = orderDatabase.Capacity;

            // Too many orders, avoid them getting spammed in the UI.
            if (amount >= capacity)
            {
                ConsolePopup(args.Session, "Too many approved orders");
                PlayDenySound(uid, component);
                return;
            }

            // Cap orders so someone can't spam thousands.
            var orderAmount = Math.Min(capacity - amount, order.Amount);

            if (orderAmount != order.Amount)
            {
                order.Amount = orderAmount;
                ConsolePopup(args.Session, "Order trimmed to capacity");
                PlayDenySound(uid, component);
            }

            var cost = product.PointCost * order.Amount;

            // Not enough balance
            if (cost > bankAccount.Balance)
            {
                ConsolePopup(args.Session, $"Insufficient funds (require {cost})");
                PlayDenySound(uid, component);
                return;
            }

            order.Approved = true;
            _idCardSystem.TryFindIdCard(player, out var idCard);
            order.Approver = idCard?.FullName ?? string.Empty;

            DeductFunds(bankAccount, cost);
            UpdateOrders(orderDatabase);
        }

        private void OnRemoveOrderMessage(EntityUid uid, CargoOrderConsoleComponent component, CargoConsoleRemoveOrderMessage args)
        {
            var orderDatabase = GetOrderDatabase(component);
            if (orderDatabase == null) return;
            RemoveOrder(orderDatabase, args.OrderNumber);
            UpdateOrders(orderDatabase);
        }

        private void OnAddOrderMessage(EntityUid uid, CargoOrderConsoleComponent component, CargoConsoleAddOrderMessage args)
        {
            if (args.Amount <= 0)
                return;

            var bank = GetBankAccount(component);
            if (bank == null) return;
            var orderDatabase = GetOrderDatabase(component);
            if (orderDatabase == null) return;

            var data = GetOrderData(args, GetNextIndex(orderDatabase));

            if (!TryAddOrder(orderDatabase, data))
            {
                PlayDenySound(uid, component);
                return;
            }
        }

        #endregion

        private void UpdateOrderState(CargoOrderConsoleComponent component, EntityUid? station)
        {
            if (station == null ||
                !TryComp<StationCargoOrderDatabaseComponent>(station, out var orderDatabase) ||
                !TryComp<StationBankAccountComponent>(station, out var bankAccount)) return;

            var state = new CargoConsoleInterfaceState(
                MetaData(station.Value).EntityName,
                GetOrderCount(orderDatabase),
                orderDatabase.Capacity,
                bankAccount.Balance,
                orderDatabase.Orders.Values.ToList());

            _uiSystem.GetUiOrNull(component.Owner, CargoConsoleUiKey.Orders)?.SetState(state);
        }

        private void ConsolePopup(ICommonSession session, string text)
        {
            _popup.PopupCursor(text, Filter.SinglePlayer(session));
        }

        private void PlayDenySound(EntityUid uid, CargoOrderConsoleComponent component)
        {
            SoundSystem.Play(component.ErrorSound.GetSound(), Filter.Pvs(uid, entityManager: EntityManager));
        }

        private CargoOrderData GetOrderData(CargoConsoleAddOrderMessage args, int index)
        {
            return new CargoOrderData(index, args.Requester, args.Reason, args.ProductId, args.Amount);
        }

        private int GetOrderCount(StationCargoOrderDatabaseComponent component)
        {
            var amount = 0;

            foreach (var (_, order) in component.Orders)
            {
                if (!order.Approved) continue;
                amount += order.Amount;
            }

            return amount;
        }

        /// <summary>
        /// Updates all of the cargo-related consoles for a particular station.
        /// This should be called whenever orders change.
        /// </summary>
        private void UpdateOrders(StationCargoOrderDatabaseComponent component)
        {
            // Order added so all consoles need updating.
            foreach (var comp in EntityQuery<CargoOrderConsoleComponent>(true))
            {
                var station = _station.GetOwningStation(component.Owner);
                if (station != component.Owner) continue;

                UpdateOrderState(comp, station);
            }

            foreach (var comp in EntityQuery<CargoShuttleConsoleComponent>(true))
            {
                var station = _station.GetOwningStation(component.Owner);
                if (station != component.Owner) continue;

                UpdateShuttleState(comp, station);
            }
        }

        public bool TryAddOrder(StationCargoOrderDatabaseComponent component, CargoOrderData data)
        {
            component.Orders.Add(data.OrderNumber, data);
            UpdateOrders(component);
            return true;
        }

        private int GetNextIndex(StationCargoOrderDatabaseComponent component)
        {
            var index = component.Index;
            component.Index++;
            return index;
        }

        public void RemoveOrder(StationCargoOrderDatabaseComponent component, int index)
        {
            if (!component.Orders.Remove(index)) return;
        }

        public void ClearOrders(StationCargoOrderDatabaseComponent component)
        {
            if (component.Orders.Count == 0) return;

            component.Orders.Clear();
            Dirty(component);
        }

        private void DeductFunds(StationBankAccountComponent component, int amount)
        {
            component.Balance = Math.Max(0, component.Balance - amount);
            Dirty(component);
        }

        #region Station

        private StationBankAccountComponent? GetBankAccount(CargoOrderConsoleComponent component)
        {
            var station = _station.GetOwningStation(component.Owner);

            TryComp<StationBankAccountComponent>(station, out var bankComponent);
            return bankComponent;
        }

        private StationCargoOrderDatabaseComponent? GetOrderDatabase(CargoOrderConsoleComponent component)
        {
            var station = _station.GetOwningStation(component.Owner);

            TryComp<StationCargoOrderDatabaseComponent>(station, out var orderComponent);
            return orderComponent;
        }

        #endregion
    }
}
