
using System;

namespace BinaryTicker.Domain.Risk
{
    public sealed class RiskLimits
    {
        public decimal MaxOrderVal { get; }
        public decimal MaxQty { get; }
        public decimal MaxPosVal { get; }
        public decimal PriceBufferFrac { get; }

        public RiskLimits( decimal maxOrderValue,decimal maxQuantity,
            decimal maxPositionValue, decimal priceBufferFraction)
        {
            MaxOrderVal = maxOrderValue;
            MaxQty = maxQuantity;
            MaxPosVal = maxPositionValue;
            PriceBufferFrac = priceBufferFraction;

            Validate();
        }

        public void Validate()
        {
            if (MaxOrderVal <= 0)
                throw new ArgumentException("Maximum order value must be positive.");

            if (MaxQty <= 0 || MaxQty != decimal.Truncate(MaxQty))
                throw new ArgumentException("Maximum quantity must be a positive whole number.");

            if (MaxPosVal <= 0)
                throw new ArgumentException("Maximum position value must be positive.");

            if (PriceBufferFrac < 0)
                throw new ArgumentException("Price buffer cannot be negative.");
        }
    }
}
