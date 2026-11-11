
using System;

namespace BinaryTicker.Application.Logging
{
    public sealed class TradeLogEntry
    {
        public Guid EventId { get; }
        public DateTimeOffset TimestampUtc { get; }
        public TradeLogLevel Level { get; }
        public string EventName { get; }
        public string Message { get; }
        public string? Symbol { get; }
        public string? Status { get; }
        public string? ClientOrderId { get; }
        public string? BrokerOrderId { get; }

        public TradeLogEntry(
            DateTimeOffset timestampUtc,
            TradeLogLevel level,
            string eventName,
            string message,
            string? symbol = null,
            string? status = null,
            string? clientOrderId = null,
            string? brokerOrderId = null)
        {
            if (!Enum.IsDefined(level))
                throw new ArgumentException(
                    "Invalid log level.", nameof(level));

            if (string.IsNullOrWhiteSpace(eventName))
                throw new ArgumentException(
                    "Event name is required.");

            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException(
                    "Event message is required.");

            EventId = Guid.NewGuid();
            TimestampUtc = timestampUtc.ToUniversalTime();
            Level = level;
            EventName = eventName;
            Message = message;
            Symbol = symbol?.Trim().ToUpperInvariant();
            Status = status;
            ClientOrderId = clientOrderId;
            BrokerOrderId = brokerOrderId;
        }
    }
}
