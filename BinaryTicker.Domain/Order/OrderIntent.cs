
using System;

namespace BinaryTicker.Domain.Order
{
    public sealed class OrderIntent
    {
        public string Symbol { get; }
        public OrderSide Side { get; }
        public decimal Quantity { get; }
        public string ClientOrderId { get; }

        public OrderIntent(
            string symbol,
            OrderSide side,
            decimal quantity,
            string clientOrderId)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Symbol is required.");

            if (quantity <= 0 ||
                quantity != decimal.Truncate(quantity) ||
                quantity > long.MaxValue)
                throw new ArgumentException(
                    "Quantity must be a positive whole number.");

            if (!Enum.IsDefined(side))
                throw new ArgumentException("Invalid order side.");

            if (string.IsNullOrWhiteSpace(clientOrderId) ||
                clientOrderId.Length > 48)
                throw new ArgumentException(
                    "A client order ID of at most 48 characters is required.");

            Symbol = symbol.Trim().ToUpperInvariant();
            Side = side;
            Quantity = quantity;
            ClientOrderId = clientOrderId;
        }
    }
}
