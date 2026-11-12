
using System;

namespace BinaryTicker.Domain.Fibonacci
{
    public sealed class SwingPoint
    {
        public SwingType Type { get; }
        public decimal Price { get; }
        public DateTimeOffset PivotTimeUtc { get; }
        public DateTimeOffset ConfirmedAtUtc { get; }

        public SwingPoint(SwingType type,decimal price,DateTimeOffset pivotTimeUtc,
            DateTimeOffset confirmedAtUtc)
        {
            if (type != SwingType.Low &&
                type != SwingType.High)
                throw new ArgumentException("Invalid swing type.");

            if (price <= 0)
                throw new ArgumentException("Swing price must be positive.");

            if (confirmedAtUtc < pivotTimeUtc)
                throw new ArgumentException("Confirmation cannot occur before pivot time.");

            Type = type;
            Price = price;
            PivotTimeUtc = pivotTimeUtc.ToUniversalTime();
            ConfirmedAtUtc = confirmedAtUtc.ToUniversalTime();
        }
    }
}
