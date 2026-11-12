
using System;
using System.Text.Json.Serialization;
using BinaryTicker.Domain.Fibonacci;
using BinaryTicker.Domain.Order;

namespace BinaryTicker.Application.Trading
{
    public sealed class TradeState
    {
        public string AccountId { get; }
        public string Symbol { get; }
        public string Timeframe { get; }
        public string StrategyVersion { get; }

        public DateTimeOffset? LastProcessedCandleUtc { get; }
        public SwingPoint? ActiveLow { get; }
        public SwingPoint? ActiveHigh { get; }
        public OrderIntent? PendingOrder { get; }

        [JsonConstructor]
        public TradeState(string accountId,string symbol,string timeframe,
            string strategyVersion, DateTimeOffset? lastProcessedCandleUtc,
            SwingPoint? activeLow, SwingPoint? activeHigh, OrderIntent? pendingOrder)
        {
            if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(symbol) ||
                string.IsNullOrWhiteSpace(timeframe) ||string.IsNullOrWhiteSpace(strategyVersion))
                throw new ArgumentException("State identity values are required.");

            if ((activeLow is null) != (activeHigh is null))
                throw new ArgumentException("Both swing points must be stored together.");

            if (pendingOrder is not null &&
                !string.Equals(pendingOrder.Symbol, symbol,  StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Pending order symbol does not match state.");

            AccountId = accountId;
            Symbol = symbol.Trim().ToUpper();
            Timeframe = timeframe;
            StrategyVersion = strategyVersion;

            LastProcessedCandleUtc = lastProcessedCandleUtc?.ToUniversalTime();

            ActiveLow = activeLow;
            ActiveHigh = activeHigh;
            PendingOrder = pendingOrder;
        }

        public static TradeState CreateNew( string accountId,  string symbol,string timeframe,string strategyVersion)
        {
            return new TradeState(accountId, symbol, timeframe, strategyVersion, null, null, null, null);
        }

        public TradeState WithEvaluation( DateTimeOffset candleTime, FibonacciLevels setup)
        {
            return new TradeState(AccountId, Symbol, Timeframe, StrategyVersion,
                candleTime, setup.Low, setup.High, PendingOrder);
        }

        public TradeState WithPendingOrder(OrderIntent order)
        {
            if (order is null)
                throw new ArgumentNullException(nameof(order));

            if (PendingOrder is not null)
                throw new InvalidOperationException("A pending order already exists.");

            return new TradeState(AccountId, Symbol, Timeframe, StrategyVersion,
                LastProcessedCandleUtc, ActiveLow, ActiveHigh, order);
        }

        public TradeState ClearPendingOrder()
        {
            return new TradeState(AccountId, Symbol, Timeframe, StrategyVersion,
                LastProcessedCandleUtc,ActiveLow, ActiveHigh, null);
        }
    }
}
