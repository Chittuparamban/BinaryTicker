
using System;
using System.Collections.Generic;

namespace BinaryTicker.Domain.Fibonacci
{
    public class FibonacciCalc
    {
        public FibonacciLevels Calculate(SwingPoint low,SwingPoint high)
        {
            if (low is null)
                throw new ArgumentNullException(nameof(low));

            if (high is null)
                throw new ArgumentNullException(nameof(high));

            if (low.Type != SwingType.Low)
                throw new ArgumentException("First point must be a swing low.");

            if (high.Type != SwingType.High)
                throw new ArgumentException("Second point must be a swing high.");

            if (low.PivotTimeUtc >= high.PivotTimeUtc)
                throw new ArgumentException("Swing high must occur after swing low.");

            if (high.Price <= low.Price)
                throw new ArgumentException("Swing high must be greater than swing low.");

            decimal range = high.Price - low.Price;

            Dictionary<decimal, decimal> prices = new Dictionary<decimal, decimal>();

            prices.Add(0.236m, high.Price - range * 0.236m);
            prices.Add(0.382m, high.Price - range * 0.382m);
            prices.Add(0.500m, high.Price - range * 0.500m);
            prices.Add(0.618m, high.Price - range * 0.618m);
            prices.Add(0.786m, high.Price - range * 0.786m);

            return new FibonacciLevels(low, high, prices);
        }
    }
}
