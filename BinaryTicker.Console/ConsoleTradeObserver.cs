
using System;
using System.Collections.Generic;
using System.IO;
using BinaryTicker.Application.Events;
using BinaryTicker.Application.Logging;

namespace BinaryTicker
{
    public sealed class ConsoleTradeObserver : IDisposable
    {
        // private properties
        private BotEventPublisher Publisher { get; }
        private TextWriter Output { get; }
        private object WriterLock { get; }
        private bool IsDisposed { get; set; }

        public ConsoleTradeObserver(BotEventPublisher publisher,TextWriter output)
        {
            
            WriterLock = new object();

            // Validate and assign publisher
            if (publisher == null)
            {
                throw new ArgumentNullException(nameof(publisher));
            }
            Publisher = publisher;

            // Validate and assign output
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }
            Output = output;

            // Subscribe to publisher events
            Publisher.TradeEventPublished += OnTradeEvent;
        }

        // Called whenever a trading event is published
        private void OnTradeEvent(TradeLogEntry entry)
        {
            lock (WriterLock)
            {
                if (IsDisposed)
                {
                    return;
                }

                List<string> details = new List<string>();

                details.Add(entry.TimestampUtc.ToString("O"));
                details.Add($"Level: {entry.Level}");
                details.Add($"Event: {entry.EventName}");
                details.Add($"Message: {entry.Message}");

                if (entry.Symbol != null)
                {
                    details.Add($"Symbol: {entry.Symbol}");
                }

                if (entry.Status != null)
                {
                    details.Add($"Status: {entry.Status}");
                }

                if (entry.ClientOrderId != null)
                {
                    details.Add(
                        $"ClientOrderId: {entry.ClientOrderId}");
                }

                if (entry.BrokerOrderId != null)
                {
                    details.Add(
                        $"BrokerOrderId: {entry.BrokerOrderId}");
                }

                // Write formatted event to the console
                Output.WriteLine(string.Join(" || ", details));

                // Flush buffered output
                Output.Flush();
            }
        }

        public void Dispose()
        {
            // Unsubscribe from events
            Publisher.TradeEventPublished -= OnTradeEvent;

            lock (WriterLock)
            {
                if (IsDisposed)
                {
                    return;
                }

                IsDisposed = true;
            }

            // Output is owned by the caller.
            // Don'tt dispose Console.Out here.
        }
    }
}
