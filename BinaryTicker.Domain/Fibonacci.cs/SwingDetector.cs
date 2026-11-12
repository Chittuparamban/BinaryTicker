
using System;
using System.Collections.Generic;
using BinaryTicker.Domain;

namespace BinaryTicker.Domain.Fibonacci
{
    public class SwingDetector
    {
        public List<SwingPoint> Detect(
            List<MarketBar> bars,
            FibonacciSettings settings)
        {
            if (bars is null)
                throw new ArgumentNullException(nameof(bars));

            if (settings is null)
                throw new ArgumentNullException(nameof(settings));

            settings.Validate();

            List<SwingPoint> swingPoints = new List<SwingPoint>();

            // Ensure bars are ordered and belong to one stock
            for (int i = 0; i < bars.Count; i++)
            {
                if (bars[i] is null)
                    throw new ArgumentException("Market bars cannot contain null.");

                if (i > 0)
                {
                    if (bars[i].OpenTimeUtc <= bars[i - 1].OpenTimeUtc)
                        throw new ArgumentException("Market bars must be sorted and unique.");

                    if (bars[i].Symbol != bars[0].Symbol)
                        throw new ArgumentException("Market bars must belong to one symbol.");
                }
            }

            // Examine candidates with enough left and right bars
            for (int i = settings.LeftBars; i < bars.Count - settings.RightBars;i++)
            {
                MarketBar candidate = bars[i];

                bool isHigh = true;
                bool isLow = true;

                // Compare against neighbouring candles
                for (int j = i - settings.LeftBars; j <= i + settings.RightBars;  j++)
                {
                    if (j == i)
                        continue;

                    // Equal highs disqualify the swing high
                    if (bars[j].High >= candidate.High)
                        isHigh = false;

                    // Equal lows disqualify the swing low
                    if (bars[j].Low <= candidate.Low)
                        isLow = false;
                }

                // Skip if neither or both conditions qualify
                if (isHigh == isLow)
                    continue;

                SwingType type;
                decimal price;

                if (isHigh)
                {
                    type = SwingType.High;
                    price = candidate.High;
                }
                else
                {
                    type = SwingType.Low;
                    price = candidate.Low;
                }

                // The last right-side candle confirms the pivot
                DateTimeOffset confirmedAt = bars[i + settings.RightBars].CloseTimeUtc;

                SwingPoint point = new SwingPoint( type,price,candidate.OpenTimeUtc,confirmedAt);

                swingPoints.Add(point);
            }

            return swingPoints;
        }
    }
}
