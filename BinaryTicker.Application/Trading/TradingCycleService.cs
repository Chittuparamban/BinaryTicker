
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
private IMarketDataService MarketData { get; }
private ITradingGateway Gateway { get; }
private ITradeStateStore Store { get; }
private ITradingStrategy Strategy { get; }
private FibonacciCalc Calculator { get; }
private RiskManager RiskManager { get; }
private RiskLimits RiskLimits { get; }
private OrderIntentCreator BuyCreator { get; }
private OrderIntentCreator SellCreator { get; }
private BotSettings Settings { get; }
private TimeProvider Clock { get; }
private bool EnablePaperOrders { get; }
private BotEventPublisher? Publisher { get; }

//First 1	Initially, one operation can enter
//Second 1	Maximum of one operation can enter simultaneously
private SemaphoreSlim CycleLock { get; } = new(1, 1);

        public TradingCycleService(IMarketDataService marketData,ITradingGateway gateway,
            ITradeStateStore store,ITradingStrategy strategy,FibonacciCalc calculator,
            RiskManager riskManager, RiskLimits riskLimits, OrderIntentCreator buyCreator,
            OrderIntentCreator sellCreator, BotSettings settings, TimeProvider clock,
            bool enablePaperOrders = false, BotEventPublisher? publisher = null)
        {
            
            if (marketData is null)
                throw new ArgumentNullException(nameof(marketData));
            else
                MarketData = marketData;

            if (gateway is null)
                throw new ArgumentNullException(nameof(gateway));
            else
                Gateway = gateway;

            if (store is null)
                throw new ArgumentNullException(nameof(store));
            else
                Store = store;

            if (strategy is null)
                throw new ArgumentNullException(nameof(strategy));
            else
                Strategy = strategy;

            if (calculator is null)
                throw new ArgumentNullException(nameof(calculator));
            else
                Calculator = calculator;

            if (riskManager is null)
                throw new ArgumentNullException(nameof(riskManager));
            else
                RiskManager = riskManager;

            if (riskLimits is null)
                throw new ArgumentNullException(nameof(riskLimits));
            else
                RiskLimits = riskLimits;

            if (buyCreator is null)
                throw new ArgumentNullException(nameof(buyCreator));
            else
                BuyCreator = buyCreator;

            if (sellCreator is null)
                throw new ArgumentNullException(nameof(sellCreator));
            else
                SellCreator = sellCreator;

            if (settings is null)
                throw new ArgumentNullException(nameof(settings));
            else
                Settings = settings;

            if (clock is null)
                throw new ArgumentNullException(nameof(clock));
            else
                Clock = clock;

            if (enablePaperOrders && publisher is null)
            {
                throw new ArgumentException(
                    "Automatic paper orders require an event publisher.");
            }

            Settings.Validate();
            RiskLimits.Validate();

            EnablePaperOrders = enablePaperOrders;
            Publisher = publisher;
        }

        // Runs one complete trading cycle
        public async Task<TradingCycleResult> RunOnceAsync( CancellationToken ct = default)
        {
            await CycleLock.WaitAsync(ct);

            try
            {
                return await ExecuteCycleAsync(ct);
            }
            finally
            {
                CycleLock.Release();
            }
        }

        // Called by BotRunner during startup recovery
        public async Task<TradingCycleResult> ReconcilePendingAsync(CancellationToken ct = default)
        {
            await CycleLock.WaitAsync(ct);

            try
            {
                DateTimeOffset now = Clock.GetUtcNow();

                TradingAccountSnapshot account = await Gateway.GetAccountAsync(ct);

                if (!account.IsPaper)
                    throw new InvalidOperationException("Paper trading account is required.");

                TradeState? state = await Store.LoadAsync(account.AccountId,Settings.Symbol,
                    Settings.Timeframe,Settings.StrategyVersion, ct);

                if (state?.PendingOrder is not null)
                {
                    return await ReconcileOrderAsync(state, now, ct);
                }

                if (await Gateway.HasOpenOrdersAsync( Settings.Symbol, ct))
                {
                    return Result(CycleOutcome.Blocked,"Untracked open broker orders exist.", now);
                }

                PositionSnapshot? position = await Gateway.GetPositionAsync( Settings.Symbol, ct);

                decimal held;
                if (position != null)
                    held = position.Quantity;
                else
                    held = 0m;

                if (held < 0 ||  held != decimal.Truncate(held))
                {
                    return Result(CycleOutcome.Blocked, "Unsupported short or fractional position.", now);
                }

                if (held > 0 && (state?.ActiveLow is null || state.ActiveHigh is null))
                {
                    return Result(CycleOutcome.Blocked, "Existing position has no saved Fibonacci setup.", now);
                }

                return Result(CycleOutcome.Hold, "Recovery completed. No unresolved order.", now);
            }
            finally
            {
                CycleLock.Release();
            }
        }

        // Executes strategy and handles automatic paper orders
        private async Task<TradingCycleResult> ExecuteCycleAsync(CancellationToken ct)
        {
            DateTimeOffset now = Clock.GetUtcNow();

            //  Verify paper account
            TradingAccountSnapshot account = await Gateway.GetAccountAsync(ct);

            if (!account.IsPaper)
            {
                throw new InvalidOperationException("Live trading is not permitted.");
            }

            // load saved state
            TradeState? saved = await Store.LoadAsync(account.AccountId, Settings.Symbol,
                Settings.Timeframe, Settings.StrategyVersion, ct);

            TradeState state;
            if (saved != null)
               state = saved;
            else
            {
                state = TradeState.CreateNew(account.AccountId, Settings.Symbol,Settings.Timeframe, Settings.StrategyVersion);
            }


            // Resolve previously submitted orders first
            if (state.PendingOrder is not null)
            {
                return await ReconcileOrderAsync(state, now, ct);
            }

            // Check existing broker orders
            if (await Gateway.HasOpenOrdersAsync(Settings.Symbol, ct))
            {
                return Result(CycleOutcome.Blocked, "Another open order exists for this stock.", now);
            }

            // Check market hours
            if (!await Gateway.IsMarketOpenAsync(ct))
            {
                return Result(CycleOutcome.Hold, "US stock market is closed.", now);
            }

            // Check asset eligibility
            if (!await Gateway.IsAssetTradableAsync( Settings.Symbol, ct))
            {
                return Result(CycleOutcome.Blocked,"Stock is not tradable.", now);
            }

            // Retrieve completed market candles
            IReadOnlyList<MarketBar> received = await MarketData.GetCompletedBarsAsync(Settings.Symbol,
                    Settings.Timeframe, now.AddDays(-30),now, ct);

            List<MarketBar> bars = received.TakeLast(Settings.HistoryBarCount).ToList();

            if (bars.Count == 0)
            {
                return Result(CycleOutcome.NoNewBar,"No completed market candles.",now);
            }

           MarketBar latest = bars[bars.Count - 1];

            // Prevent duplicate candle processing
            if (state.LastProcessedCandleUtc is not null && latest.CloseTimeUtc <=state.LastProcessedCandleUtc.Value)
            {
                return Result( CycleOutcome.NoNewBar,"Latest candle already processed.", now);
            }

            // Reject stale or future data
            if (latest.CloseTimeUtc > now ||now - latest.CloseTimeUtc > Settings.MaxDataLag)
            {
                return Result(CycleOutcome.Blocked,"Market data is not sufficiently recent.", now);
            }

            // Retrieve broker position
            PositionSnapshot? position = await Gateway.GetPositionAsync( Settings.Symbol, ct);

            decimal heldQuantity;

          if (position != null)
          {
              heldQuantity = position.Quantity;
          }
          else
          {
              heldQuantity = 0m;
          }

            if (heldQuantity < 0 ||heldQuantity != decimal.Truncate(heldQuantity))
            {
                return Result(CycleOutcome.Blocked,"Unsupported short or fractional position.",now);
            }

            // Restore Fibonacci setup
            FibonacciLevels? activeSetup = null;

            if (state.ActiveLow is not null &&
                state.ActiveHigh is not null)
            {
                activeSetup = Calculator.Calculate(state.ActiveLow,state.ActiveHigh);
            }

            if (heldQuantity > 0 && activeSetup is null)
            {
                return Result(CycleOutcome.Blocked, "Cannot manage an untracked position.",now);
            }

            // evaluate the Fibonacci strategy
            TradeSignal signal = Strategy.Evaluate(bars,heldQuantity,activeSetup);

            // save completed candle and setup
            state = state.WithEvaluation(latest.CloseTimeUtc, signal.ActiveSetup);

            await Store.SaveAsync(state, ct);

            if (signal.Action == SignalAction.Hold)
            {
                return Result(CycleOutcome.Hold,signal.Reason, now, signal);
            }

            // Choose Buy or Sell creator
            OrderIntentCreator creator;
            decimal quantity;

            if (signal.Action == SignalAction.Buy)
            {
                if (heldQuantity != 0m)
                {
                    return Result(CycleOutcome.Blocked, "Buy rejected: position already exists.",now,signal);
                }

                creator = BuyCreator;
                quantity = Settings.Quantity;
            }
            else if (signal.Action == SignalAction.Sell)
            {
                if (heldQuantity <= 0m)
                {
                    return Result(CycleOutcome.Blocked,"Sell rejected: no shares available.", now,signal);
                }

                creator = SellCreator;
                quantity = heldQuantity;
            }
            else
            {
                throw new InvalidOperationException("Unsupported strategy action.");
            }

            // Create the OrderIntent once
            string symbol = Settings.Symbol;
            decimal orderQuantity = quantity;
            Guid uniqueId = Guid.NewGuid();
            string clientOrderId = "bt-" + uniqueId.ToString("N");;
            OrderIntent intent = creator.Create(symbol,orderQuantity,clientOrderId);

            //  Refresh account information
            TradingAccountSnapshot freshAccount = await Gateway.GetAccountAsync(ct);

            if (!freshAccount.IsPaper ||freshAccount.AccountId != account.AccountId)
            {
                throw new InvalidOperationException("Trading account changed or is not paper.");
            }

            // Recheck position
            PositionSnapshot? freshPosition = await Gateway.GetPositionAsync(Settings.Symbol, ct);

            decimal freshHeld;
            if (freshPosition != null)
            {
                freshHeld = freshPosition.Quantity;
            }
            else
                freshHeld = 0m;


            if (freshHeld != heldQuantity)
            {
                return Result(CycleOutcome.Blocked,"Broker position changed during evaluation.", now,signal);
            }

            // Recheck for conflicting orders
            if (await Gateway.HasOpenOrdersAsync(Settings.Symbol, ct))
            {
                return Result(CycleOutcome.Blocked,"Conflicting broker order appeared.", now,signal);
            }

            // Recheck trading eligibility
            bool marketOpen = await Gateway.IsMarketOpenAsync(ct);

            bool assetTradable = await Gateway.IsAssetTradableAsync( Settings.Symbol, ct);

            if (!assetTradable)
            {
                return Result(CycleOutcome.Blocked,"Stock is no longer tradable.", now,signal);
            }

            // Check data freshness again
            DateTimeOffset submissionTime =Clock.GetUtcNow();

            if (latest.CloseTimeUtc > submissionTime || submissionTime - latest.CloseTimeUtc > Settings.MaxDataLag)
            {
                return Result(CycleOutcome.Blocked,"Market data became stale before submission.", submissionTime, signal);
            }

            //   Validate risk
            var riskResult = RiskManager.Validate(intent: intent, limits: RiskLimits,
                marketPrice: latest.Close,availableBuyingPower: freshAccount.BuyingPower, heldQuantity: freshHeld,
                 currentPositionValue: freshPosition?.MarketValue ?? 0m, reservedSellQuantity: 0m,
                  canTrade: freshAccount.CanTrade && freshAccount.IsPaper, isMarketOpen: marketOpen);

            if (!riskResult.Allowed)
            {
                return Result(CycleOutcome.Blocked,riskResult.Reason, submissionTime,signal);
            }

            //   Preview mode
            if (!EnablePaperOrders)
            {
                string outcome = $"Would {signal.Action} {quantity} " + $"{intent.Symbol} shares.";
                return Result(CycleOutcome.Preview, outcome, submissionTime, signal);
            }

     // Persist pending order BEFORE submission
            state = state.WithPendingOrder(intent);

            await Store.SaveAsync(state, ct);

            // Publish prepared order
            // A logging exception prevents submission.
            string messagepreview = $"Prepared {intent.Side} order for {intent.Quantity} shares.";
            Publisher!.Publish(new TradeLogEntry(timestampUtc: Clock.GetUtcNow(), level: TradeLogLevel.Information,
                eventName: "OrderPrepared", message: messagepreview, symbol: intent.Symbol,
                status: "Prepared", clientOrderId: intent.ClientOrderId));

            // Submit ONCE to Alpaca Paper
            // Do not retry on timeout or uncertain result.
            OrderSnapshot submitted =
                await Gateway.SubmitOrderAsync(intent, ct);

            // Verify broker acknowledgement
            if (submitted.ClientOrderId != intent.ClientOrderId ||
                !string.Equals(submitted.Symbol,intent.Symbol,  StringComparison.OrdinalIgnoreCase) ||
                submitted.Side != intent.Side ||submitted.Quantity != intent.Quantity)
            {
                throw new InvalidOperationException(
                    "Broker acknowledgement does not match the order.");
            }

            // Log the actual broker response
           string msg= $"Submitted {submitted.Side} order for {submitted.Quantity} shares. " + $"Status: {submitted.Status}; " +
                $"Filled: {submitted.FilledQuantity}; " +  $"Average price: {submitted.AverageFillPrice}";
            Publisher.Publish(new TradeLogEntry( timestampUtc: Clock.GetUtcNow(),  level: TradeLogLevel.Information,
                eventName: "OrderAcknowledged",  message: msg, symbol: submitted.Symbol,
                status: submitted.Status.ToString(),    clientOrderId: submitted.ClientOrderId,
                brokerOrderId: submitted.BrokerOrderId));

            // Keep pending state for subsequent reconciliation
            return Result( CycleOutcome.Submitted, "Paper order acknowledged. Fill not yet confirmed.",
                Clock.GetUtcNow(), signal, submitted);
        }

        // Reconcile a previously saved Buy/Sell order
        private async Task<TradingCycleResult>
            ReconcileOrderAsync(TradeState state,  DateTimeOffset now, CancellationToken ct)
        {
            OrderIntent pending;
            if (state.PendingOrder != null) 
            {
                pending = state.PendingOrder;
            }
            else
                throw new InvalidOperationException("There is no pending order to reconcile.");


            OrderSnapshot? order = await Gateway.GetOrderByClientOrderIdAsync( pending.ClientOrderId, ct);

            if (order is null)
            {
                string result = $"Pending order {pending.ClientOrderId} not found at broker. " +
                    "Manual reconciliation required.";
                return Result(CycleOutcome.Blocked, result, now);
            }

            // Ensure broker response matches the saved order
            if (order.ClientOrderId != pending.ClientOrderId ||
                !string.Equals(order.Symbol, pending.Symbol,  StringComparison.OrdinalIgnoreCase) ||
                order.Side != pending.Side ||order.Quantity != pending.Quantity)
            {
                throw new InvalidOperationException("Broker order does not match saved intent.");
            }

            // Active orders may still receive fills
            bool terminal =  order.Status == OrderStatus.Filled || order.Status == OrderStatus.Canceled ||
                order.Status == OrderStatus.Rejected ||order.Status == OrderStatus.Expired;

            if (!terminal)
            {
                string resultmsg = $"Order unresolved: {order.Status}; " +$"filled {order.FilledQuantity}/{order.Quantity}; " +
                    $"average price {order.AverageFillPrice}.";
                return Result(CycleOutcome.Blocked, resultmsg,now, order: order);
            }

            // A cancelled order can contain partial fills.
            // Do not silently discard those shares.
            if (order.Status != OrderStatus.Filled &&order.FilledQuantity > 0m)
            {
                return Result(
                    CycleOutcome.Blocked,"Terminal order contains partial fills. " +
                    "Manual position reconciliation required.", now, order: order);
            }

            if (order.Status == OrderStatus.Filled)
            {
                if (order.FilledQuantity != pending.Quantity)
                {
                    return Result(  CycleOutcome.Blocked,"Filled quantity does not match order quantity.",now, order: order);
                }

                // Confirm broker position after fill
                PositionSnapshot? position =await Gateway.GetPositionAsync( pending.Symbol, ct);
                
                decimal actualQuantity;
                if (position != null)
                    actualQuantity = position.Quantity;
                else
                    actualQuantity = 0m;
                
                decimal expectedQuantity;
                if (pending.Side == OrderSide.Buy)
                    expectedQuantity = pending.Quantity;
                else
                    expectedQuantity = 0m;

                if (actualQuantity != expectedQuantity)
                {
                    return Result(CycleOutcome.Blocked,"Broker position is not reconciled. " +
                        $"Expected {expectedQuantity}; " +$"actual {actualQuantity}.",
                        now, order: order);
                }
            }

            // Never clear state while another order remains open
            if (await Gateway.HasOpenOrdersAsync(pending.Symbol, ct))
            {
                return Result(CycleOutcome.Blocked,"Other broker orders remain open.",now, order: order);
            }

            // Order is terminal and broker state is reconciled
            state = state.ClearPendingOrder();

            await Store.SaveAsync(state, ct);

            return Result(CycleOutcome.Reconciled,
                $"Order reconciled: {order.Status}; " + $"filled {order.FilledQuantity}; " +
                $"average price {order.AverageFillPrice}.",now, order: order);
        }

        // Helper for creating consistent cycle results
        private static TradingCycleResult Result(CycleOutcome outcome, string reason, DateTimeOffset atUtc,
            TradeSignal? signal = null, OrderSnapshot? order = null)
        {
            return new TradingCycleResult(outcome, reason, atUtc, signal, order);
        }
    }
}
