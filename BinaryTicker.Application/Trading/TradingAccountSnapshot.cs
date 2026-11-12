
using System;

namespace BinaryTicker.Application.Trading
{
    public sealed class TradingAccountSnapshot
    {
        public string AccountId { get; }
        public decimal Cash { get; }
        public decimal BuyingPower { get; }
        public bool CanTrade { get; }
        public bool IsPaper { get; }
        public DateTimeOffset RetrievedAtUtc { get; }

        public TradingAccountSnapshot(string accountId,decimal cash, decimal buyingPower,
            bool canTrade, bool isPaper, DateTimeOffset retrievedAtUtc)
        {
            if (string.IsNullOrWhiteSpace(accountId))
                throw new ArgumentException("Account ID is required.");

            AccountId = accountId;
            Cash = cash;
            BuyingPower = buyingPower;
            CanTrade = canTrade;
            IsPaper = isPaper;
            RetrievedAtUtc = retrievedAtUtc;
        }
    }
}
