
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
        private BotEventPublisher Publisher { get; }
        private StreamWriter Writer { get; }
        private object WriterLock { get; }
        private JsonSerializerOptions Options { get; }

        private bool IsDisposed { get; set; }

        public FileTradeObserver( BotEventPublisher publisher, string filePath)
        {
            // Initialize the synchronization object
            WriterLock = new object();

            // Initialize JSON settings
            Options = new JsonSerializerOptions();

            // Store enum values as strings instead of no.s
            Options.Converters.Add(new JsonStringEnumConverter());

            // Store the publisher
            if (publisher == null)
            {
                throw new ArgumentNullException(nameof(publisher));
            }

            Publisher = publisher;

            // Validate the file path
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException( "Log file path is required.", nameof(filePath));
            }

            //  Get the absolute path
            string fullPath = Path.GetFullPath(filePath);

            //  Create the directory if it does not exist
            string directory = Path.GetDirectoryName(fullPath)!;

            Directory.CreateDirectory(directory);

            // Open the file without deleting previous logs
            FileStream stream = new FileStream(fullPath,FileMode.Append,FileAccess.Write,FileShare.Read);

             // Create a writer that uses UTF-8 encoding
            Writer = new StreamWriter(  stream, new UTF8Encoding(false));

            // Automatically flush each write to the stream
            Writer.AutoFlush = true;

            //  Subscribe to trading events
            Publisher.TradeEventPublished += OnTradeEvent;
        }

        // Called whenever BotEventPublisher publishes an event
        private void OnTradeEvent(TradeLogEntry entry)
        {
            lock (WriterLock)
            {
            // Do not write after disposal
                if (IsDisposed)
                {
                    return;
                }

                // convert the trading event into JSON
                string json = JsonSerializer.Serialize(entry,Options);

                // Write the JSON as a separate line
                Writer.WriteLine(json);
            }
        }

        // release resources and unsubscribe from events
        public void Dispose()
        {
            // stop receiving trading events
            Publisher.TradeEventPublished -= OnTradeEvent;

            lock (WriterLock)
            {
               // Prevent disposing the writer twice
                if (IsDisposed)
                {
                    return;
                }

                IsDisposed = true;

     // closes the file and release resources
                Writer.Dispose();
            }
        }
    }
}
