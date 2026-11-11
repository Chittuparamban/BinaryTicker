
using System;
using System.Collections.Generic;
using System.IO;
using BinaryTicker.Application.Events;
using BinaryTicker.Application.Logging;

namespace BinaryTicker
{
    public sealed class ConsoleTradeObserver : IDisposable
    {
        private readonly BotEventPublisher _publisher;
        private readonly TextWriter _output;
        private readonly object _lock = new();

        private bool _disposed;

        public ConsoleTradeObserver(
            BotEventPublisher publisher,
            TextWriter output)
        {
            _publisher = publisher ??
                throw new ArgumentNullException(nameof(publisher));

            _output = output ??
                throw new ArgumentNullException(nameof(output));

            // Subscribe to publisher
            _publisher.TradeEventPublished += OnTradeEvent;
        }

        private void OnTradeEvent(TradeLogEntry entry)
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                List<string> details = new List<string>
                {
                    entry.TimestampUtc.ToString("O"),
                    $"Level: {entry.Level}",
                    $"Event: {entry.EventName}",
                    $"Message: {entry.Message}"
                };

                if (entry.Symbol is not null)
                    details.Add($"Symbol: {entry.Symbol}");

                if (entry.Status is not null)
                    details.Add($"Status: {entry.Status}");

                if (entry.ClientOrderId is not null)
                    details.Add(
                        $"ClientOrderId: {entry.ClientOrderId}");

                if (entry.BrokerOrderId is not null)
                    details.Add(
                        $"BrokerOrderId: {entry.BrokerOrderId}");

                _output.WriteLine(string.Join(" | ", details));
                _output.Flush();
            }
        }

        public void Dispose()
        {
            // Unsubscribe to prevent further notifications
            _publisher.TradeEventPublished -= OnTradeEvent;

            lock (_lock)
            {
                _disposed = true;
            }
        }
    }
}
