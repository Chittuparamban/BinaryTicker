
using System;

namespace BinaryTicker.Domain.Fibonacci
{
    public sealed class FibonacciSettings
    {
        public int LeftBars { get; }
        public int RightBars { get; }
        public decimal ZoneStartRatio { get; }
        public decimal ZoneEndRatio { get; }

        public FibonacciSettings(
            int leftBars,
            int rightBars,
            decimal zoneStartRatio,
            decimal zoneEndRatio)
        {
            LeftBars = leftBars;
            RightBars = rightBars;
            ZoneStartRatio = zoneStartRatio;
            ZoneEndRatio = zoneEndRatio;

            Validate();
        }

        public void Validate()
        {
            if (LeftBars <= 0 || RightBars <= 0)
                throw new ArgumentException(
                    "Pivot widths must be positive.");

            if (ZoneStartRatio <= 0 ||
                ZoneEndRatio >= 1 ||
                ZoneStartRatio >= ZoneEndRatio)
                throw new ArgumentException(
                    "Invalid Fibonacci retracement zone.");
        }
    }
}
