
using System;
using System.Collections.Generic;

namespace BinaryTicker.Domain.Fibonacci
{
    public sealed class FibonacciLevels
    {
        public SwingPoint Low { get; }
        public SwingPoint High { get; }
        public Dictionary<decimal, decimal> Prices { get; }

        public FibonacciLevels(
            SwingPoint low,
            SwingPoint high,
            Dictionary<decimal, decimal> prices)
        {
            if (low is null)
                throw new ArgumentNullException(nameof(low));

            if (high is null)
                throw new ArgumentNullException(nameof(high));

            if (prices is null)
                throw new ArgumentNullException(nameof(prices));

            if (low.Type != SwingType.Low ||high.Type != SwingType.High)
                throw new ArgumentException("Expected a swing low followed by a swing high.");

            if (low.PivotTimeUtc >= high.PivotTimeUtc || low.Price >= high.Price)
                throw new ArgumentException("Invalid upward swing.");

            decimal[] requiredRatios = {0.236m, 0.382m, 0.5m, 0.618m, 0.786m};

            foreach (decimal ratio in requiredRatios)
            {
                if (!prices.ContainsKey(ratio) ||prices[ratio] <= 0)
                    throw new ArgumentException("Missing or invalid Fibonacci level.");
            }

            Low = low;
            High = high;

            // Copy the input dictionary
            Prices = new Dictionary<decimal, decimal>(prices);
        }
    }
}
