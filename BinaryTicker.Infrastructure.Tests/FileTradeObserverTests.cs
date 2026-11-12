
using System;
using System.IO;
using NUnit.Framework;
using BinaryTicker.Application.Events;
using BinaryTicker.Application.Logging;
using BinaryTicker.Infrastructure.Logging;

namespace BinaryTicker.Infrastructure.Tests
{
    [TestFixture]
    public class FileTradeObserverTests
    {
        [Test]
        public void PublishedEvents_AreAppendedtoFile()
        {
            string directory = Path.Combine( Path.GetTempPath(),
                "BinaryTickerTests",Guid.NewGuid().ToString("N"));

            string path = Path.Combine(directory, "events.jsonl");

            try
            {
                BotEventPublisher publisher = new BotEventPublisher();

                using (var observer = new FileTradeObserver(publisher, path))
                {
                    publisher.Publish(new TradeLogEntry(  DateTimeOffset.UtcNow,
                        TradeLogLevel.Information,"BotStarted","Bot started successfully."));
                }

                // Simulate restarting the observer
                using (var observer = new FileTradeObserver(publisher, path))
                {
                    publisher.Publish(new TradeLogEntry(DateTimeOffset.UtcNow,
                        TradeLogLevel.Information,"BotStopped", "Bot stopped successfully."));
                }

                string[] lines = File.ReadAllLines(path);

                Assert.That(lines, Has.Length.EqualTo(2));
                Assert.That(lines[0],Does.Contain("BotStarted"));
                Assert.That(lines[1],Does.Contain("BotStopped"));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }
    }
}
