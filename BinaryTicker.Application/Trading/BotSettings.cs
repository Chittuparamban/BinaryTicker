
using System;

namespace BinaryTicker.Application.Trading
{
    public sealed class BotSettings
    {
        public string Symbol { get; }
        public string Timeframe { get; }
        public string StrategyVersion { get; }

        public TimeSpan PollInterval { get; }
        public TimeSpan MaxDataLag { get; }

        public int HistoryBarCount { get; }
        public decimal Quantity { get; }

        public BotSettings(
            string symbol,
            string timeframe,
            string strategyVersion,
            TimeSpan pollInterval,
            TimeSpan maxDataLag,
            int historyBarCount,
            decimal quantity)
        {
            Symbol = symbol?.Trim().ToUpperInvariant()
    ?? throw new ArgumentNullException(nameof(symbol));
            Timeframe = timeframe;
            StrategyVersion = strategyVersion;
            PollInterval = pollInterval;
            MaxDataLag = maxDataLag;
            HistoryBarCount = historyBarCount;
            Quantity = quantity;

            Validate();
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Symbol) ||
                string.IsNullOrWhiteSpace(StrategyVersion))
                throw new ArgumentException(
                    "Symbol and strategy version are required.");

            if (Timeframe is not (
                "1Min" or "5Min" or "15Min" or "1Hour"))
                throw new ArgumentException(
                    "Unsupported timeframe.");

            if (PollInterval <= TimeSpan.Zero ||
                MaxDataLag <= TimeSpan.Zero)
                throw new ArgumentException(
                    "Time intervals must be positive.");

            if (HistoryBarCount <= 0 ||
                HistoryBarCount > 5000)
                throw new ArgumentException(
                    "History bar count must be between 1 and 5000.");

            if (Quantity <= 0 ||
                Quantity != decimal.Truncate(Quantity))
                throw new ArgumentException(
                    "Quantity must be a positive whole number.");
        }
    }
}
