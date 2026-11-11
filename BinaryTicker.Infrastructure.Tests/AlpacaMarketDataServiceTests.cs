
using System;
using System.Threading;
using System.Threading.Tasks;
using Alpaca.Markets;
using BinaryTicker.Infrastructure;
using Moq;
using NUnit.Framework;

namespace BinaryTicker.Infrastructure.Tests
{
    [TestFixture]
    public class AlpacaMarketDataServiceTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(
                2026, 11, 9, 16, 0, 0, TimeSpan.Zero);

        // TEST 1: Completed candle is accepted
        [Test]
        public async Task CompletedCandle_IsIncluded()
        {
            // Candle 15:57–15:58, plus 1-minute delay
            var service = CreateService(
                CreateBar(57));

            var bars = await GetBars(service);

            Assert.That(bars, Has.Count.EqualTo(1));
            Assert.That(bars[0].CloseTimeUtc,
                Is.EqualTo(Now.AddMinutes(-2)));
        }

        // TEST 2: Incomplete/recent candle is rejected
        [Test]
        public async Task IncompleteCandle_IsExcluded()
        {
            var service = CreateService(
                CreateBar(57),  // Complete
                CreateBar(59)); // Too recent

            var bars = await GetBars(service);

            Assert.That(bars, Has.Count.EqualTo(1));
            Assert.That(bars[0].OpenTimeUtc,
                Is.EqualTo(Now.AddMinutes(-3)));
        }

        // TEST 3: Sorting and duplicate removal
        [Test]
        public async Task Bars_AreSortedAndDeduplicated()
        {
            var service = CreateService(
                CreateBar(55, 101m),
                CreateBar(50, 102m),
                CreateBar(55, 104m));

            var bars = await GetBars(service);

            Assert.That(bars, Has.Count.EqualTo(2));

            Assert.That(bars[0].OpenTimeUtc,
                Is.LessThan(bars[1].OpenTimeUtc));

            // Last duplicate replaces the first
            Assert.That(bars[1].Close, Is.EqualTo(104m));
        }

        // Simulate Alpaca's IBar objects
        private static IBar CreateBar(
            int minute, decimal close = 103m)
        {
            var bar = new Mock<IBar>();

            bar.SetupGet(b => b.TimeUtc)
                .Returns(new DateTime(
                    2026, 11, 9, 15, minute, 0,
                    DateTimeKind.Utc));

            bar.SetupGet(b => b.Open).Returns(100m);
            bar.SetupGet(b => b.High).Returns(110m);
            bar.SetupGet(b => b.Low).Returns(90m);
            bar.SetupGet(b => b.Close).Returns(close);
            bar.SetupGet(b => b.Volume).Returns(1000m);
            bar.SetupGet(b => b.Symbol).Returns("AAPL");

            return bar.Object;
        }

        // Create adapter with a simulated SDK response
        private static AlpacaMarketDataService CreateService(
            params IBar[] inputBars)
        {
            var page = new Mock<IPage<IBar>>();

            page.SetupGet(p => p.Items).Returns(inputBars);
            page.SetupGet(p => p.NextPageToken)
                .Returns((string?)null);
            page.SetupGet(p => p.Symbol).Returns("AAPL");

            var client = new Mock<IAlpacaDataClient>();

            client.Setup(c => c.ListHistoricalBarsAsync(
                    It.IsAny<HistoricalBarsRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(page.Object);

            var settings = new AlpacaSettings("testKey", "testSecret", "iex", "raw", true, null);

            return new AlpacaMarketDataService(
                client.Object,
                settings,
                new FixedTimeProvider(Now));
        }

        private static async Task<System.Collections.Generic.IReadOnlyList<BinaryTicker.Domain.MarketBar>>
            GetBars(AlpacaMarketDataService service)
        {
            return await service.GetCompletedBarsAsync(
                "AAPL",
                "1Min",
                Now.AddMinutes(-20),
                Now);
        }

        // Fixed clock: prevents tests depending on real time
        private sealed class FixedTimeProvider : TimeProvider
        {
            private readonly DateTimeOffset _now;

            public FixedTimeProvider(DateTimeOffset now)
            {
                _now = now;
            }

            public override DateTimeOffset GetUtcNow()
            {
                return _now;
            }
        }
    }
}
