
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BinaryTicker.Application.Interfaces;
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
            bool enablePaperOrders = false)
        {
            _marketData = marketData ?? throw new ArgumentNullException(nameof(marketData));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
            _riskManager = riskManager ?? throw new ArgumentNullException(nameof(riskManager));
            _riskLimits = riskLimits ?? throw new ArgumentNullException(nameof(riskLimits));
            _buyCreator = buyCreator ?? throw new ArgumentNullException(nameof(buyCreator));
            _sellCreator = sellCreator ?? throw new ArgumentNullException(nameof(sellCreator));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _enablePaperOrders = enablePaperOrders;
        }

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

        private async Task<TradingCycleResult> ExecuteCycleAsync(
            CancellationToken ct)
        {
            DateTimeOffset now = _clock.GetUtcNow();

            // STEP 1: Retrieve broker account
            TradingAccountSnapshot account =
                await _gateway.GetAccountAsync(ct);

            if (!account.IsPaper)
                throw new InvalidOperationException(
                    "A paper-trading account is required.");

            // STEP 2: Restore previous state
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

            // STEP 3: Reconcile pending execution first
            if (state.PendingOrder is not null)
            {
                OrderSnapshot? order =
                    await _gateway.GetOrderByClientOrderIdAsync(
                        state.PendingOrder.ClientOrderId, ct);

                if (order is null)
                {
                    return new TradingCycleResult(
                        CycleOutcome.Blocked,
                        "Pending order cannot be confirmed. Manual reconciliation required.",
                        now);
                }

                if (order.ClientOrderId !=
                    state.PendingOrder.ClientOrderId)
                {
                    throw new InvalidOperationException(
                        "Broker returned a mismatched order ID.");
                }

                bool isTerminal =
                    order.Status == OrderStatus.Filled ||
                    order.Status == OrderStatus.Canceled ||
                    order.Status == OrderStatus.Rejected ||
                    order.Status == OrderStatus.Expired;

                if (!isTerminal)
                {
                    return new TradingCycleResult(
                        CycleOutcome.Blocked,
                        "An order is still pending or its status is unknown.",
                        now,
                        order: order);
                }

                state = state.ClearPendingOrder();
                await _store.SaveAsync(state, ct);

                return new TradingCycleResult(
                    CycleOutcome.Reconciled,
                    "Broker order is terminal. Position will be checked next cycle.",
                    now,
                    order: order);
            }

            // STEP 4: Retrieve completed market candles
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
                return new TradingCycleResult(
                    CycleOutcome.NoNewBar,
                    "No completed bars available.",
                    now);
            }

            MarketBar latest = bars[^1];

            if (state.LastProcessedCandleUtc is not null &&
                latest.CloseTimeUtc <= state.LastProcessedCandleUtc)
            {
                return new TradingCycleResult(
                    CycleOutcome.NoNewBar,
                    "Latest candle was already processed.",
                    now);
            }

            if (latest.CloseTimeUtc > now ||
                now - latest.CloseTimeUtc > _settings.MaxDataLag)
            {
                return new TradingCycleResult(
                    CycleOutcome.Blocked,
                    "Market data is not sufficiently recent.",
                    now);
            }

            // STEP 5: Retrieve existing position
            PositionSnapshot? position =
                await _gateway.GetPositionAsync(
                    _settings.Symbol, ct);

            decimal heldQuantity = position?.Quantity ?? 0m;

            // STEP 6: Restore active Fibonacci setup
            FibonacciLevels? activeSetup = null;

            if (state.ActiveLow is not null &&
                state.ActiveHigh is not null)
            {
                activeSetup = _calculator.Calculate(
                    state.ActiveLow,
                    state.ActiveHigh);
            }

            // STEP 7: Evaluate strategy
            TradeSignal signal = _strategy.Evaluate(
                bars,
                heldQuantity,
                activeSetup);

            // STEP 8: Save evaluated candle and setup
            state = state.WithEvaluation(
                latest.CloseTimeUtc,
                signal.ActiveSetup);

            await _store.SaveAsync(state, ct);

            if (signal.Action == SignalAction.Hold)
            {
                return new TradingCycleResult(
                    CycleOutcome.Hold,
                    signal.Reason,
                    now,
                    signal);
            }

            // STEP 9: Determine order quantity and creator
            OrderIntentCreator creator;
            decimal quantity;

            if (signal.Action == SignalAction.Buy)
            {
                creator = _buyCreator;
                quantity = _settings.Quantity;

                if (heldQuantity != 0)
                {
                    return new TradingCycleResult(
                        CycleOutcome.Blocked,
                        "Cannot buy while a position already exists.",
                        now,
                        signal);
                }
            }
            else if (signal.Action == SignalAction.Sell)
            {
                creator = _sellCreator;
                quantity = heldQuantity;

                if (heldQuantity <= 0)
                {
                    return new TradingCycleResult(
                        CycleOutcome.Blocked,
                        "There are no shares to sell.",
                        now,
                        signal);
                }
            }
            else
            {
                throw new InvalidOperationException(
                    "Unknown strategy action.");
            }

            // STEP 10: Create OrderIntent once
            OrderIntent intent = creator.Create(
                _settings.Symbol,
                quantity,
                "bt-" + Guid.NewGuid().ToString("N"));

            // STEP 11: Check market eligibility and risk
            bool isMarketOpen =
                await _gateway.IsMarketOpenAsync(ct);

            var riskResult = _riskManager.Validate(
                intent: intent,
                limits: _riskLimits,
                marketPrice: latest.Close,
                availableBuyingPower: account.BuyingPower,
                heldQuantity: heldQuantity,
                currentPositionValue: position?.MarketValue ?? 0m,
                reservedSellQuantity: 0m,
                canTrade: account.CanTrade && account.IsPaper,
                isMarketOpen: isMarketOpen);

            if (!riskResult.Allowed)
            {
                return new TradingCycleResult(
                    CycleOutcome.Blocked,
                    riskResult.Reason,
                    now,
                    signal);
            }

            // STEP 12: Preview mode
            if (!_enablePaperOrders)
            {
                return new TradingCycleResult(
                    CycleOutcome.Preview,
                    $"Risk checks passed. Would {signal.Action} {quantity} shares.",
                    now,
                    signal);
            }

            // STEP 13: Save pending order before submission
            state = state.WithPendingOrder(intent);
            await _store.SaveAsync(state, ct);

            // STEP 14: Submit validated order once
            OrderSnapshot submitted =
                await _gateway.SubmitOrderAsync(intent, ct);

            return new TradingCycleResult(
                CycleOutcome.Submitted,
                "Paper order submitted; fill not yet confirmed.",
                now,
                signal,
                submitted);
        }
    }
}
