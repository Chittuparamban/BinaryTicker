
using System;

namespace BinaryTicker.Infrastructure
{
    public sealed class AlpacaSettings
    {
        // These are Read-only configuration properties
        public string KeyId { get; }
        public string SecretKey { get; }
        public string DataFeed { get; }
        public string AdjustmentPolicy { get; }
        public TimeSpan RequestTimeout { get; }
        public bool IsPaperTrading { get; }

        // public Constructor
        public AlpacaSettings(string keyId, string secretKey, string dataFeed,
            string adjustmentPolicy, TimeSpan? requestTimeout, bool isPaperTrading)
        {
            KeyId = keyId;
            SecretKey = secretKey;
            DataFeed = dataFeed;
            AdjustmentPolicy = adjustmentPolicy;
            RequestTimeout = requestTimeout??TimeSpan.FromSeconds(30);
            IsPaperTrading = isPaperTrading;

        }

        // Validate configuration before trading
        // throw new ArgumentException is used to disrupt the execution if invalid values are initialised
        // exceptions will be handled in Program.cs
        public void ValidatePaperEnvironment()
        {
            if (string.IsNullOrWhiteSpace(KeyId))
                throw new ArgumentException("Alpaca API Key ID is required.");

            if (string.IsNullOrWhiteSpace(SecretKey))
                throw new ArgumentException("Alpaca Secret Key is required.");

            if (!IsPaperTrading)
                throw new InvalidOperationException("BinaryTicker only supports paper trading.");

            if (RequestTimeout <= TimeSpan.Zero)
                throw new ArgumentException("Request timeout must be positive number.");

            if (DataFeed is not ("iex" or "sip"))
                throw new ArgumentException("Unsupported market data feed.");

            if (AdjustmentPolicy is not ("raw" or "split" or "dividend" or"spin-off" or "all"))
                throw new ArgumentException("Unsupported adjustment policy.");
        }
    }
}
