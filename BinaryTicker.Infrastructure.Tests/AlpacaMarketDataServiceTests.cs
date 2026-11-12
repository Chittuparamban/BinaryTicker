using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Alpaca.Markets;
using BinaryTicker.Domain;
using BinaryTicker.Infrastructure;
using NUnit.Framework;

namespace BinaryTicker.Infrastructure.Tests
{
    [TestFixture]
    public class AlpacaMarketDataServiceTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 11, 9, 16, 0, 0, TimeSpan.Zero);

        // TEST 1: Completed candle is accepted.
        [Test]
        public async Task CompletedCandle_IsIncluded()
        {
            AlpacaMarketDataService service = CreateService(CreateBar(57));

            IReadOnlyList<MarketBar> bars = await GetBars(service);

            Assert.That(bars, Has.Count.EqualTo(1));
            Assert.That(bars[0].CloseTimeUtc,
                Is.EqualTo(Now.AddMinutes(-2)));
        }

        // TEST 2: A too-recent candle is excluded.
        [Test]
        public async Task IncompleteCandle_IsExcluded()
        {
            AlpacaMarketDataService service = CreateService(
                CreateBar(57),
                CreateBar(59));

            IReadOnlyList<MarketBar> bars = await GetBars(service);

            Assert.That(bars, Has.Count.EqualTo(1));
            Assert.That(bars[0].OpenTimeUtc,
                Is.EqualTo(Now.AddMinutes(-3)));
        }

        // TEST 3: Bars are sorted and the latest duplicate wins.
        [Test]
        public async Task Bars_AreSortedAndDeduplicated()
        {
            AlpacaMarketDataService service = CreateService(
                CreateBar(55, 101m),
                CreateBar(50, 102m),
                CreateBar(55, 104m));

            IReadOnlyList<MarketBar> bars = await GetBars(service);

            Assert.That(bars, Has.Count.EqualTo(2));
            Assert.That(bars[0].OpenTimeUtc,
                Is.LessThan(bars[1].OpenTimeUtc));
            Assert.That(bars[1].Close, Is.EqualTo(104m));
        }

        // A small .NET proxy stands in for the SDK's IBar interface.
        private static IBar CreateBar(int minute, decimal close = 103m)
        {
            DateTime barTime = new DateTime(
                2026, 11, 9, 15, minute, 0, DateTimeKind.Utc);

            return CreateFake<IBar>(method => method.Name switch
            {
                "get_TimeUtc" => barTime,
                "get_Open" => 100m,
                "get_High" => 110m,
                "get_Low" => 90m,
                "get_Close" => close,
                "get_Volume" => 1000m,
                "get_Symbol" => "AAPL",
                _ => throw new NotSupportedException(
                    "Unexpected IBar call: " + method.Name)
            });
        }

        // Return predetermined SDK bars without any network request.
        private static AlpacaMarketDataService CreateService(
            params IBar[] inputBars)
        {
            IPage<IBar> page = CreateFake<IPage<IBar>>(method => method.Name switch
            {
                "get_Items" => inputBars,
                "get_NextPageToken" => null,
                "get_Symbol" => "AAPL",
                _ => throw new NotSupportedException(
                    "Unexpected IPage call: " + method.Name)
            });

            IAlpacaDataClient client = CreateFake<IAlpacaDataClient>(method =>
            {
                if (method.Name == "ListHistoricalBarsAsync")
                {
                    return Task.FromResult<IPage<IBar>>(page);
                }

                throw new NotSupportedException(
                    "Unexpected data-client call: " + method.Name);
            });

            AlpacaSettings settings = new AlpacaSettings(
                "testKey", "testSecret", "iex", "raw", null, true);

            return new AlpacaMarketDataService(
                client,
                settings,
                new FixedTimeProvider(Now));
        }

        private static async Task<IReadOnlyList<MarketBar>> GetBars(
            AlpacaMarketDataService service)
        {
            return await service.GetCompletedBarsAsync(
                "AAPL",
                "1Min",
                Now.AddMinutes(-20),
                Now);
        }

        // Create an object implementing a supplied interface using
        // built-in .NET functionality, instead of the Moq package.
        private static T CreateFake<T>(Func<MethodInfo, object?> handler)
            where T : class
        {
            T fake = DispatchProxy.Create<T, TestProxy>();
            ((TestProxy)(object)fake).Handler = handler;
            return fake;
        }

        // Public and non-sealed so DispatchProxy can derive a runtime type.
        public class TestProxy : DispatchProxy
        {
            public Func<MethodInfo, object?>? Handler { get; set; }

            protected override object? Invoke(
                MethodInfo? targetMethod,
                object?[]? args)
            {
                if (targetMethod == null || Handler == null)
                {
                    throw new InvalidOperationException(
                        "Test proxy has not been configured.");
                }

                return Handler(targetMethod);
            }
        }

        // Fixed time makes these tests deterministic.
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
