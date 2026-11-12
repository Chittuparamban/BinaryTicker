
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

    var handlers = TradeEventPublished;

    if (handlers is null)
        throw new InvalidOperationException("No event observers are registered.");

    var failures = new List<Exception>();

    foreach (Delegate handler in handlers.GetInvocationList())
    {
        try
        {
            ((Action<TradeLogEntry>)handler)(entry);
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }
    }

    if (failures.Count > 0)
    {
        throw new AggregateException("One or more trade event observers failed.",failures);
    }
}

    }
}
