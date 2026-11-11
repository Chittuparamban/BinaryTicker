
using System;

namespace BinaryTicker.Domain.Risk
{
    public sealed class RiskLimits
    {
        public decimal MaxOrderValue { get; }
        public decimal MaxQuantity { get; }
        public decimal MaxPositionValue { get; }
        public decimal PriceBufferFraction { get; }

        public RiskLimits(
            decimal maxOrderValue,
            decimal maxQuantity,
            decimal maxPositionValue,
            decimal priceBufferFraction)
        {
            MaxOrderValue = maxOrderValue;
            MaxQuantity = maxQuantity;
            MaxPositionValue = maxPositionValue;
            PriceBufferFraction = priceBufferFraction;

            Validate();
        }

        public void Validate()
        {
            if (MaxOrderValue <= 0)
                throw new ArgumentException(
                    "Maximum order value must be positive.");

            if (MaxQuantity <= 0 ||
                MaxQuantity != decimal.Truncate(MaxQuantity))
                throw new ArgumentException(
                    "Maximum quantity must be a positive whole number.");

            if (MaxPositionValue <= 0)
                throw new ArgumentException(
                    "Maximum position value must be positive.");

            if (PriceBufferFraction < 0)
                throw new ArgumentException(
                    "Price buffer cannot be negative.");
        }
    }
}
