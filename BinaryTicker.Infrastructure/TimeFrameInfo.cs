using System;
using Alpaca.Markets;
// This class is so that GetTimeFrame() could return a TimeFrameInfo object instead of a tuple.
namespace BinaryTicker.Infrastructure
{
    public sealed class TimeFrameInfo
    {
        public BarTimeFrame AlpacaTimeFrame { get; }
        public TimeSpan Duration { get; }

        public TimeFrameInfo(
            BarTimeFrame alpacaTimeFrame,
            TimeSpan duration)
        {
            AlpacaTimeFrame = alpacaTimeFrame;
            Duration = duration;
        }
    }
}