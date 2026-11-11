
using System;
using BinaryTicker.Domain.Order;

namespace BinaryTicker.Domain.Risk
{
    public class RiskManager
    {
        public (bool Allowed, string Reason) Validate(
            OrderIntent intent,
            RiskLimits limits,
            decimal marketPrice,
            decimal availableBuyingPower,
            decimal heldQuantity,
            decimal currentPositionValue,
            decimal reservedSellQuantity,
            bool canTrade,
            bool isMarketOpen)
        {
            if (intent is null)
                throw new ArgumentNullException(nameof(intent));

            if (limits is null)
                throw new ArgumentNullException(nameof(limits));

            limits.Validate();

            // Validate supplied market and account data
            if (marketPrice <= 0)
                return (false, "Invalid market price.");

            if (availableBuyingPower < 0 ||
                heldQuantity < 0 ||
                currentPositionValue < 0 ||
                reservedSellQuantity < 0)
                return (false, "Invalid account or position values.");

            if (!canTrade)
                return (false, "Account is not eligible to trade.");

            if (!isMarketOpen)
                return (false, "Market is closed.");

            // BUY ORDER
            if (intent.Side == OrderSide.Buy)
            {
                decimal estimatedOrderValue =
                    intent.Quantity * marketPrice *
                    (1 + limits.PriceBufferFraction);

                if (intent.Quantity > limits.MaxQuantity)
                    return (false, "Order quantity exceeds limit.");

                if (estimatedOrderValue > limits.MaxOrderValue)
                    return (false, "Order value exceeds limit.");

                if (estimatedOrderValue > availableBuyingPower)
                    return (false, "Insufficient buying power.");

                if (currentPositionValue + estimatedOrderValue >
                    limits.MaxPositionValue)
                    return (false, "Position value would exceed limit.");

                return (true, "Buy order passed risk validation.");
            }

            // SELL ORDER
            if (intent.Side == OrderSide.Sell)
            {
                if (reservedSellQuantity > heldQuantity)
                    return (false, "Invalid reserved sell quantity.");

                decimal availableShares =
                    heldQuantity - reservedSellQuantity;

                if (intent.Quantity > availableShares)
                    return (false, "Insufficient available shares.");

                return (true, "Sell order passed risk validation.");
            }

            return (false, "Unsupported order side.");
        }
    }
}
