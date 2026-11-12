
using System;
using BinaryTicker.Domain.Order;

namespace BinaryTicker.Application.Trading
{
    public sealed class OrderSnapshot
    {
        public string BrokerOrderId { get; }
        public string ClientOrderId { get; }
        public string Symbol { get; }
        public OrderSide Side { get; }
        public decimal Quantity { get; }
        public decimal FilledQuantity { get; }
        public decimal? AverageFillPrice { get; }
        public OrderStatus Status { get; }
        public string RawStatus { get; }
        public DateTimeOffset UpdatedAtUtc { get; }

        public OrderSnapshot( string brokerOrderId,string clientOrderId,
            string symbol, OrderSide side, decimal quantity, decimal filledQuantity,
            decimal? averageFillPrice, OrderStatus status, string rawStatus, DateTimeOffset updatedAtUtc)
        {
            if (string.IsNullOrWhiteSpace(brokerOrderId) ||
                string.IsNullOrWhiteSpace(clientOrderId) ||string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException( "Order identifiers and symbol are required.");

            if (quantity <= 0 || filledQuantity < 0 || filledQuantity > quantity)
                throw new ArgumentException( "Invalid order quantities.");

            BrokerOrderId = brokerOrderId;
            ClientOrderId = clientOrderId;
            Symbol = symbol;
            Side = side;
            Quantity = quantity;
            FilledQuantity = filledQuantity;
            AverageFillPrice = averageFillPrice;
            Status = status;
            RawStatus = rawStatus;
            UpdatedAtUtc = updatedAtUtc;
        }
    }
}
