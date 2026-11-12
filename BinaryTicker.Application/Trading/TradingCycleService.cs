
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BinaryTicker.Application.Events;
using BinaryTicker.Application.Interfaces;
using BinaryTicker.Application.Logging;

using BinaryTicker.Domain;
using BinaryTicker.Domain.Fibonacci;
using BinaryTicker.Domain.Order;
using BinaryTicker.Domain.Risk;
using BinaryTicker.Domain.Strategies;

namespace BinaryTicker.Application.Trading
{
    public sealed class TradingCycleService
    {
        private readonly IMarketDataService _marketData;
        private readonly ITradingGateway _gateway;
        private readonly ITradeStateStore _store;
        private readonly ITradingStrategy _strategy;
        private readonly FibonacciCalc _calculator;
        private readonly RiskManager _riskManager;
        private readonly RiskLimits _riskLimits;
        private readonly OrderIntentCreator _buyCreator;
        private readonly OrderIntentCreator _sellCreator;
        private readonly BotSettings _settings;
        private readonly TimeProvider _clock;
        private readonly bool _enablePaperOrders;
        private readonly BotEventPublisher? _publisher;

        private readonly SemaphoreSlim _cycleLock = new(1, 1);

        public TradingCycleService(
            IMarketDataService marketData,
            ITradingGateway gateway,
            ITradeStateStore store,
            ITradingStrategy strategy,
            FibonacciCalc calculator,
            RiskManager riskManager,
            RiskLimits riskLimits,
            OrderIntentCreator buyCreator,
            OrderIntentCreator sellCreator,
            BotSettings settings,
            TimeProvider clock,
            bool enablePaperOrders = false,
            BotEventPublisher? publisher = null)
        {
            _marketData = marketData ??
                throw new ArgumentNullException(nameof(marketData));

            _gateway = gateway ??
                throw new ArgumentNullException(nameof(gateway));

            _store = store ??
                throw new ArgumentNullException(nameof(store));

            _strategy = strategy ??
                throw new ArgumentNullException(nameof(strategy));

            _calculator = calculator ??
                throw new ArgumentNullException(nameof(calculator));

            _riskManager = riskManager ??
                throw new ArgumentNullException(nameof(riskManager));

            _riskLimits = riskLimits ??
                throw new ArgumentNullException(nameof(riskLimits));

            _buyCreator = buyCreator ??
                throw new ArgumentNullException(nameof(buyCreator));

            _sellCreator = sellCreator ??
                throw new ArgumentNullException(nameof(sellCreator));

            _settings = settings ??
                throw new ArgumentNullException(nameof(settings));

            _clock = clock ??
                throw new ArgumentNullException(nameof(clock));

            if (enablePaperOrders && publisher is null)
            {
                throw new ArgumentException(
                    "Automatic paper orders require an event publisher.");
            }

            _settings.Validate();
            _riskLimits.Validate();

            _enablePaperOrders = enablePaperOrders;
            _publisher = publisher;
        }

        // Runs one complete trading cycle
        public async Task<TradingCycleResult> RunOnceAsync(
            CancellationToken ct = default)
        {
            await _cycleLock.WaitAsync(ct);

            try
            {
                return await ExecuteCycleAsync(ct);
            }
            finally
            {
                _cycleLock.Release();
            }
        }

        // Called by BotRunner during startup recovery
        public async Task<TradingCycleResult> ReconcilePendingAsync(
            CancellationToken ct = default)
        {
            await _cycleLock.WaitAsync(ct);

            try
            {
                DateTimeOffset now = _clock.GetUtcNow();

                TradingAccountSnapshot account =
                    await _gateway.GetAccountAsync(ct);

                if (!account.IsPaper)
                    throw new InvalidOperationException(
                        "Paper trading account is required.");

                TradeState? state = await _store.LoadAsync(
                    account.AccountId,
                    _settings.Symbol,
                    _settings.Timeframe,
                    _settings.StrategyVersion,
                    ct);

                if (state?.PendingOrder is not null)
                {
                    return await ReconcileOrderAsync(
                        state, now, ct);
                }

                if (await _gateway.HasOpenOrdersAsync(
                    _settings.Symbol, ct))
                {
                    return Result(
                        CycleOutcome.Blocked,
                        "Untracked open broker orders exist.",
                        now);
                }

                PositionSnapshot? position =
                    await _gateway.GetPositionAsync(
                        _settings.Symbol, ct);

                decimal held = position?.Quantity ?? 0m;

                if (held < 0 ||
                    held != decimal.Truncate(held))
                {
                    return Result(
                        CycleOutcome.Blocked,
                        "Unsupported short or fractional position.",
                        now);
                }

                if (held > 0 &&
                    (state?.ActiveLow is null ||
                     state.ActiveHigh is null))
                {
                    return Result(
                        CycleOutcome.Blocked,
                        "Existing position has no saved Fibonacci setup.",
                        now);
                }

                return Result(
                    CycleOutcome.Hold,
                    "Recovery completed. No unresolved order.",
                    now);
            }
            finally
            {
                _cycleLock.Release();
            }
        }

        // Executes strategy and handles automatic paper orders
        private async Task<TradingCycleResult> ExecuteCycleAsync(
            CancellationToken ct)
        {
            DateTimeOffset now = _clock.GetUtcNow();

            // STEP 1: Verify paper account
            TradingAccountSnapshot account =
                await _gateway.GetAccountAsync(ct);

            if (!account.IsPaper)
            {
                throw new InvalidOperationException(
                    "Live trading is not permitted.");
            }

            // STEP 2: Load saved state
            TradeState? saved = await _store.LoadAsync(
                account.AccountId,
                _settings.Symbol,
                _settings.Timeframe,
                _settings.StrategyVersion,
                ct);

            TradeState state = saved ?? TradeState.CreateNew(
                account.AccountId,
                _settings.Symbol,
                _settings.Timeframe,
                _settings.StrategyVersion);

            // STEP 3: Resolve previously submitted orders first
            if (state.PendingOrder is not null)
            {
                return await ReconcileOrderAsync(
                    state, now, ct);
            }

            // STEP 4: Check existing broker orders
            if (await _gateway.HasOpenOrdersAsync(
                _settings.Symbol, ct))
            {
                return Result(
                    CycleOutcome.Blocked,
                    "Another open order exists for this stock.",
                    now);
            }

            // STEP 5: Check market hours
            if (!await _gateway.IsMarketOpenAsync(ct))
            {
                return Result(
                    CycleOutcome.Hold,
                    "US stock market is closed.",
                    now);
            }

            // STEP 6: Check asset eligibility
            if (!await _gateway.IsAssetTradableAsync(
                _settings.Symbol, ct))
            {
                return Result(
                    CycleOutcome.Blocked,
                    "Stock is not tradable.",
                    now);
            }

            // STEP 7: Retrieve completed market candles
            IReadOnlyList<MarketBar> received =
                await _marketData.GetCompletedBarsAsync(
                    _settings.Symbol,
                    _settings.Timeframe,
                    now.AddDays(-30),
                    now,
                    ct);

            List<MarketBar> bars = received
                .TakeLast(_settings.HistoryBarCount)
                .ToList();

            if (bars.Count == 0)
            {
                return Result(
                    CycleOutcome.NoNewBar,
                    "No completed market candles.",
                    now);
            }

            MarketBar latest = bars[^1];

            // STEP 8: Prevent duplicate candle processing
            if (state.LastProcessedCandleUtc is not null &&
                latest.CloseTimeUtc <=
                    state.LastProcessedCandleUtc.Value)
            {
                return Result(
                    CycleOutcome.NoNewBar,
                    "Latest candle already processed.",
                    now);
            }

            // STEP 9: Reject stale or future data
            if (latest.CloseTimeUtc > now ||
                now - latest.CloseTimeUtc > _settings.MaxDataLag)
            {
                return Result(
                    CycleOutcome.Blocked,
                    "Market data is not sufficiently recent.",
                    now);
            }

            // STEP 10: Retrieve broker position
            PositionSnapshot? position =
                await _gateway.GetPositionAsync(
                    _settings.Symbol, ct);

            decimal heldQuantity = position?.Quantity ?? 0m;

            if (heldQuantity < 0 ||
                heldQuantity != decimal.Truncate(heldQuantity))
            {
                return Result(
                    CycleOutcome.Blocked,
                    "Unsupported short or fractional position.",
                    now);
            }

            // STEP 11: Restore Fibonacci setup
            FibonacciLevels? activeSetup = null;

            if (state.ActiveLow is not null &&
                state.ActiveHigh is not null)
            {
                activeSetup = _calculator.Calculate(
                    state.ActiveLow,
                    state.ActiveHigh);
            }

            if (heldQuantity > 0 && activeSetup is null)
            {
                return Result(
                    CycleOutcome.Blocked,
                    "Cannot manage an untracked position.",
                    now);
            }

            // STEP 12: Evaluate the Fibonacci strategy
            TradeSignal signal = _strategy.Evaluate(
                bars,
                heldQuantity,
                activeSetup);

            // STEP 13: Save completed candle and setup
            state = state.WithEvaluation(
                latest.CloseTimeUtc,
                signal.ActiveSetup);

            await _store.SaveAsync(state, ct);

            if (signal.Action == SignalAction.Hold)
            {
                return Result(
                    CycleOutcome.Hold,
                    signal.Reason,
                    now,
                    signal);
            }

            // STEP 14: Choose Buy or Sell creator
            OrderIntentCreator creator;
            decimal quantity;

            if (signal.Action == SignalAction.Buy)
            {
                if (heldQuantity != 0m)
                {
                    return Result(
                        CycleOutcome.Blocked,
                        "Buy rejected: position already exists.",
                        now,
                        signal);
                }

                creator = _buyCreator;
                quantity = _settings.Quantity;
            }
            else if (signal.Action == SignalAction.Sell)
            {
                if (heldQuantity <= 0m)
                {
                    return Result(
                        CycleOutcome.Blocked,
                        "Sell rejected: no shares available.",
                        now,
                        signal);
                }

                creator = _sellCreator;
                quantity = heldQuantity;
            }
            else
            {
                throw new InvalidOperationException(
                    "Unsupported strategy action.");
            }

            // STEP 15: Create the OrderIntent once
            OrderIntent intent = creator.Create(
                _settings.Symbol,
                quantity,
                "bt-" + Guid.NewGuid().ToString("N"));

            // STEP 16: Refresh account information
            TradingAccountSnapshot freshAccount =
                await _gateway.GetAccountAsync(ct);

            if (!freshAccount.IsPaper ||
                freshAccount.AccountId != account.AccountId)
            {
                throw new InvalidOperationException(
                    "Trading account changed or is not paper.");
            }

            // STEP 17: Recheck position
            PositionSnapshot? freshPosition =
                await _gateway.GetPositionAsync(
                    _settings.Symbol, ct);

            decimal freshHeld = freshPosition?.Quantity ?? 0m;

            if (freshHeld != heldQuantity)
            {
                return Result(
                    CycleOutcome.Blocked,
                    "Broker position changed during evaluation.",
                    now,
                    signal);
            }

            // STEP 18: Recheck for conflicting orders
            if (await _gateway.HasOpenOrdersAsync(
                _settings.Symbol, ct))
            {
                return Result(
                    CycleOutcome.Blocked,
                    "Conflicting broker order appeared.",
                    now,
                    signal);
            }

            // STEP 19: Recheck trading eligibility
            bool marketOpen =
                await _gateway.IsMarketOpenAsync(ct);

            bool assetTradable =
                await _gateway.IsAssetTradableAsync(
                    _settings.Symbol, ct);

            if (!assetTradable)
            {
                return Result(
                    CycleOutcome.Blocked,
                    "Stock is no longer tradable.",
                    now,
                    signal);
            }

            // STEP 20: Check data freshness again
            DateTimeOffset submissionTime =
                _clock.GetUtcNow();

            if (latest.CloseTimeUtc > submissionTime ||
                submissionTime - latest.CloseTimeUtc >
                    _settings.MaxDataLag)
            {
                return Result(
                    CycleOutcome.Blocked,
                    "Market data became stale before submission.",
                    submissionTime,
                    signal);
            }

            // STEP 21: Validate risk
            var riskResult = _riskManager.Validate(
                intent: intent,
                limits: _riskLimits,
                marketPrice: latest.Close,
                availableBuyingPower: freshAccount.BuyingPower,
                heldQuantity: freshHeld,
                currentPositionValue:
                    freshPosition?.MarketValue ?? 0m,
                reservedSellQuantity: 0m,
                canTrade:
                    freshAccount.CanTrade &&
                    freshAccount.IsPaper,
                isMarketOpen: marketOpen);

            if (!riskResult.Allowed)
            {
                return Result(
                    CycleOutcome.Blocked,
                    riskResult.Reason,
                    submissionTime,
                    signal);
            }

            // STEP 22: Preview mode
            if (!_enablePaperOrders)
            {
                return Result(
                    CycleOutcome.Preview,
                    $"Would {signal.Action} {quantity} " +
                    $"{intent.Symbol} shares.",
                    submissionTime,
                    signal);
            }

            // STEP 23: Persist pending order BEFORE submission
            state = state.WithPendingOrder(intent);

            await _store.SaveAsync(state, ct);

            // STEP 24: Publish prepared order
            // A logging exception prevents submission.
            _publisher!.Publish(new TradeLogEntry(
                timestampUtc: _clock.GetUtcNow(),
                level: TradeLogLevel.Information,
                eventName: "OrderPrepared",
                message:
                    $"Prepared {intent.Side} order for " +
                    $"{intent.Quantity} shares.",
                symbol: intent.Symbol,
                status: "Prepared",
                clientOrderId: intent.ClientOrderId));

            // STEP 25: Submit ONCE to Alpaca Paper
            // Do not retry on timeout or uncertain result.
            OrderSnapshot submitted =
                await _gateway.SubmitOrderAsync(intent, ct);

            // STEP 26: Verify broker acknowledgement
            if (submitted.ClientOrderId != intent.ClientOrderId ||
                !string.Equals(
                    submitted.Symbol,
                    intent.Symbol,
                    StringComparison.OrdinalIgnoreCase) ||
                submitted.Side != intent.Side ||
                submitted.Quantity != intent.Quantity)
            {
                throw new InvalidOperationException(
                    "Broker acknowledgement does not match the order.");
            }

            // STEP 27: Log the actual broker response
            _publisher.Publish(new TradeLogEntry(
                timestampUtc: _clock.GetUtcNow(),
                level: TradeLogLevel.Information,
                eventName: "OrderAcknowledged",
                message:
                    $"Status: {submitted.Status}; " +
                    $"Filled: {submitted.FilledQuantity}; " +
                    $"Average price: {submitted.AverageFillPrice}",
                symbol: submitted.Symbol,
                status: submitted.Status.ToString(),
                clientOrderId: submitted.ClientOrderId,
                brokerOrderId: submitted.BrokerOrderId));

            // Keep pending state for subsequent reconciliation
            return Result(
                CycleOutcome.Submitted,
                "Paper order acknowledged. Fill not yet confirmed.",
                _clock.GetUtcNow(),
                signal,
                submitted);
        }

        // Reconcile a previously saved Buy/Sell order
        private async Task<TradingCycleResult>
            ReconcileOrderAsync(
                TradeState state,
                DateTimeOffset now,
                CancellationToken ct)
        {
            OrderIntent pending = state.PendingOrder
                ?? throw new InvalidOperationException(
                    "There is no pending order to reconcile.");

            OrderSnapshot? order =
                await _gateway.GetOrderByClientOrderIdAsync(
                    pending.ClientOrderId, ct);

            if (order is null)
            {
                return Result(
                    CycleOutcome.Blocked,
                    "Pending order cannot be confirmed. " +
                    $"Client ID: {pending.ClientOrderId}. " +
                    "Manual reconciliation required.",
                    now);
            }

            // Ensure broker response matches the saved order
            if (order.ClientOrderId != pending.ClientOrderId ||
                !string.Equals(
                    order.Symbol,
                    pending.Symbol,
                    StringComparison.OrdinalIgnoreCase) ||
                order.Side != pending.Side ||
                order.Quantity != pending.Quantity)
            {
                throw new InvalidOperationException(
                    "Broker order does not match saved intent.");
            }

            // Active orders may still receive fills
            bool terminal =
                order.Status == OrderStatus.Filled ||
                order.Status == OrderStatus.Canceled ||
                order.Status == OrderStatus.Rejected ||
                order.Status == OrderStatus.Expired;

            if (!terminal)
            {
                return Result(
                    CycleOutcome.Blocked,
                    $"Order unresolved: {order.Status}; " +
                    $"filled {order.FilledQuantity}/" +
                    $"{order.Quantity}; " +
                    $"average price {order.AverageFillPrice}.",
                    now,
                    order: order);
            }

            // A cancelled order can contain partial fills.
            // Do not silently discard those shares.
            if (order.Status != OrderStatus.Filled &&
                order.FilledQuantity > 0m)
            {
                return Result(
                    CycleOutcome.Blocked,
                    "Terminal order contains partial fills. " +
                    "Manual position reconciliation required.",
                    now,
                    order: order);
            }

            if (order.Status == OrderStatus.Filled)
            {
                if (order.FilledQuantity != pending.Quantity)
                {
                    return Result(
                        CycleOutcome.Blocked,
                        "Filled quantity does not match order quantity.",
                        now,
                        order: order);
                }

                // Confirm broker position after fill
                PositionSnapshot? position =
                    await _gateway.GetPositionAsync(
                        pending.Symbol, ct);

                decimal actualQuantity =
                    position?.Quantity ?? 0m;

                decimal expectedQuantity =
                    pending.Side == OrderSide.Buy
                        ? pending.Quantity
                        : 0m;

                if (actualQuantity != expectedQuantity)
                {
                    return Result(
                        CycleOutcome.Blocked,
                        "Broker position is not reconciled. " +
                        $"Expected {expectedQuantity}; " +
                        $"actual {actualQuantity}.",
                        now,
                        order: order);
                }
            }

            // Never clear state while another order remains open
            if (await _gateway.HasOpenOrdersAsync(
                pending.Symbol, ct))
            {
                return Result(
                    CycleOutcome.Blocked,
                    "Other broker orders remain open.",
                    now,
                    order: order);
            }

            // Order is terminal and broker state is reconciled
            state = state.ClearPendingOrder();

            await _store.SaveAsync(state, ct);

            return Result(
                CycleOutcome.Reconciled,
                $"Order reconciled: {order.Status}; " +
                $"filled {order.FilledQuantity}; " +
                $"average price {order.AverageFillPrice}.",
                now,
                order: order);
        }

        // Helper for creating consistent cycle results
        private static TradingCycleResult Result(
            CycleOutcome outcome,
            string reason,
            DateTimeOffset atUtc,
            TradeSignal? signal = null,
            OrderSnapshot? order = null)
        {
            return new TradingCycleResult(
                outcome,
                reason,
                atUtc,
                signal,
                order);
        }
    }
}
