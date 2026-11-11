
using System;
using BinaryTicker.Application.Logging;

namespace BinaryTicker.Application.Events
{
    public sealed class BotEventPublisher
    {
        // Event to which observers subscribe
        public event Action<TradeLogEntry>? TradeEventPublished;

        // Notify all registered observers
        public void Publish(TradeLogEntry entry)
        {
            if (entry is null)
                throw new ArgumentNullException(nameof(entry));

            TradeEventPublished?.Invoke(entry);
        }
    }
}
