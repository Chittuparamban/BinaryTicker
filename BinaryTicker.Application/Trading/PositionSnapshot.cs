
using System;

namespace BinaryTicker.Application.Trading
{
    public sealed class PositionSnapshot
    {
        public string Symbol { get; }
        public decimal Quantity { get; }
        public decimal AverageEntryPrice { get; }
        public decimal MarketValue { get; }
        public DateTimeOffset RetrievedAtUtc { get; }

        public PositionSnapshot(
            string symbol,
            decimal quantity,
            decimal averageEntryPrice,
            decimal marketValue,
            DateTimeOffset retrievedAtUtc)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Symbol is required.");

            if (quantity <= 0 || averageEntryPrice < 0)
                throw new ArgumentException(
                    "Invalid position quantity or entry price.");

            Symbol = symbol;
            Quantity = quantity;
            AverageEntryPrice = averageEntryPrice;
            MarketValue = marketValue;
            RetrievedAtUtc = retrievedAtUtc;
        }
    }
}
