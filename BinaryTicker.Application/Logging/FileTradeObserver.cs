
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BinaryTicker.Application.Events;
using BinaryTicker.Application.Logging;

namespace BinaryTicker.Infrastructure.Logging
{
    public sealed class FileTradeObserver : IDisposable
    {
        private readonly BotEventPublisher _publisher;
        private readonly StreamWriter _writer;
        private readonly object _lock = new();

        private readonly JsonSerializerOptions _options = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private bool _disposed;

        public FileTradeObserver(
            BotEventPublisher publisher,
            string filePath)
        {
            _publisher = publisher ??
                throw new ArgumentNullException(nameof(publisher));

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException(
                    "Log file path is required.");

            string fullPath = Path.GetFullPath(filePath);

            string directory = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(directory);

            // Append preserves previous log history
            var stream = new FileStream(
                fullPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read);

            _writer = new StreamWriter(
                stream,
                new UTF8Encoding(false))
            {
                AutoFlush = true
            };

            // Subscribe after opening the file
            _publisher.TradeEventPublished += OnTradeEvent;
        }

        private void OnTradeEvent(TradeLogEntry entry)
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                string json = JsonSerializer.Serialize(
                    entry, _options);

                _writer.WriteLine(json);
            }
        }

        public void Dispose()
        {
            // Unsubscribe first
            _publisher.TradeEventPublished -= OnTradeEvent;

            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _writer.Dispose();
            }
        }
    }
}
