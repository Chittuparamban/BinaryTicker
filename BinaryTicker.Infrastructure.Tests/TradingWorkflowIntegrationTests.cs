
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

using BinaryTicker.Application.Events;
using BinaryTicker.Application.Interfaces;
using BinaryTicker.Application.Logging;
using BinaryTicker.Application.Trading;

using BinaryTicker.Domain;
using BinaryTicker.Domain.Fibonacci;
using BinaryTicker.Domain.Order;
using BinaryTicker.Domain.Risk;
using BinaryTicker.Domain.Strategies;

using BinaryTicker.Infrastructure.DatabaseStorage;
using BinaryTicker.Infrastructure.Logging;

namespace BinaryTicker.Infrastructure.Tests
{
    [TestFixture]
    public class TradingWorkflowIntegrationTests
    {
        private static readonly DateTimeOffset Start =
            new DateTimeOffset(
                2026, 11, 12, 14, 0, 0, TimeSpan.Zero);

        private static readonly DateTimeOffset Now =
            Start.AddMinutes(10).AddSeconds(30);

        // TEST 1: Full Fibonacci Buy preview
        [Test]
        public async Task BuySignal_PassesRiskAndSavesState()
        {
            string directory = CreateTestDirectory();

            string statePath =
                Path.Combine(directory, "state.json");

            string logPath =
                Path.Combine(directory, "events.jsonl");

            try
            {
                BotEventPublisher publisher =
                    new BotEventPublisher();

                Mock<ITradingGateway> gateway;

                using (var fileObserver =
                    new FileTradeObserver(publisher, logPath))
                using (var store = new JsonStateStore(statePath))
                {
                    TradingCycleService cycle =
                        CreateCycle(
                            store,
                            maxOrderValue: 1000m,
                            out gateway);

                    // First cycle: should produce a Buy preview
                    TradingCycleResult first =
                        await cycle.RunOnceAsync();

                    PublishResult(publisher, first);

                    Assert.That(
                        first.Outcome,
                        Is.EqualTo(CycleOutcome.Preview));

                    Assert.That(
                        first.Signal, Is.Not.Null);

                    Assert.That(
                        first.Signal!.Action,
                        Is.EqualTo(SignalAction.Buy));

                    // Same candle should not be processed twice
                    TradingCycleResult second =
                        await cycle.RunOnceAsync();

                    PublishResult(publisher, second);

                    Assert.That(
                        second.Outcome,
                        Is.EqualTo(CycleOutcome.NoNewBar));

                    // Never submit an order to Alpaca
                    gateway.Verify(g => g.SubmitOrderAsync(
                        It.IsAny<OrderIntent>(),
                        It.IsAny<CancellationToken>()),
                        Times.Never);
                }

                // Reopen JSON state after the store closes
                using (var restoredStore =
                    new JsonStateStore(statePath))
                {
                    TradeState? restored =
                        await restoredStore.LoadAsync(
                            "test-account",
                            "AAPL",
                            "1Min",
                            "fib-v1");

                    Assert.That(restored, Is.Not.Null);

                    Assert.That(
                        restored!.LastProcessedCandleUtc,
                        Is.EqualTo(Start.AddMinutes(10)));

                    Assert.That(restored.ActiveLow, Is.Not.Null);
                    Assert.That(restored.ActiveHigh, Is.Not.Null);

                    Assert.That(
                        restored.ActiveLow!.Price,
                        Is.EqualTo(100m));

                    Assert.That(
                        restored.ActiveHigh!.Price,
                        Is.EqualTo(200m));

                    Assert.That(
                        restored.PendingOrder,
                        Is.Null);
                }

                // Check the actual file observer output
                string[] logs = File.ReadAllLines(logPath);

                Assert.That(logs, Has.Length.EqualTo(2));

                Assert.That(
                    logs[0], Does.Contain("\"Preview\""));

                Assert.That(
                    logs[1], Does.Contain("\"NoNewBar\""));
            }
            finally
            {
                DeleteTestDirectory(directory);
            }
        }

        // TEST 2: RiskManager blocks the proposed Buy
        [Test]
        public async Task BuySignal_ExceedingRiskLimit_IsBlocked()
        {
            string directory = CreateTestDirectory();

            try
            {
                string statePath =
                    Path.Combine(directory, "state.json");

                using var store = new JsonStateStore(statePath);

                TradingCycleService cycle =
                    CreateCycle(
                        store,
                        maxOrderValue: 100m,
                        out var gateway);

                TradingCycleResult result =
                    await cycle.RunOnceAsync();

                Assert.That(
                    result.Signal?.Action,
                    Is.EqualTo(SignalAction.Buy));

                Assert.That(
                    result.Outcome,
                    Is.EqualTo(CycleOutcome.Blocked));

                Assert.That(
                    result.Reason,
                    Does.Contain("Order value exceeds limit"));

                // A rejected order must not be submitted
                gateway.Verify(g => g.SubmitOrderAsync(
                    It.IsAny<OrderIntent>(),
                    It.IsAny<CancellationToken>()),
                    Times.Never);

                TradeState? state = await store.LoadAsync(
                    "test-account",
                    "AAPL",
                    "1Min",
                    "fib-v1");

                // No pending order should be saved
                Assert.That(state, Is.Not.Null);
                Assert.That(state!.PendingOrder, Is.Null);
            }
            finally
            {
                // The using declaration is disposed at scope exit.
                // For Windows, directory cleanup is handled below.
                try
                {
                    DeleteTestDirectory(directory);
                }
                catch (IOException)
                {
                    // See cleanup note below.
                }
            }
        }

        // TEST 3: An unresolved pending order blocks execution
        [Test]
        public async Task UnknownPendingOrder_BlocksNewTrading()
        {
            string directory = CreateTestDirectory();

            try
            {
                string statePath =
                    Path.Combine(directory, "state.json");

                using (var store = new JsonStateStore(statePath))
                {
                    TradeState state = TradeState.CreateNew(
                        "test-account",
                        "AAPL",
                        "1Min",
                        "fib-v1");

                    OrderIntent pending = new OrderIntent(
                        "AAPL",
                        OrderSide.Buy,
                        1m,
                        "previous-order-001");

                    await store.SaveAsync(
                        state.WithPendingOrder(pending));

                    TradingCycleService cycle =
                        CreateCycle(
                            store,
                            maxOrderValue: 1000m,
                            out var gateway);

                    // Broker cannot confirm saved order
                    gateway.Setup(g =>
                        g.GetOrderByClientOrderIdAsync(
                            "previous-order-001",
                            It.IsAny<CancellationToken>()))
                        .ReturnsAsync((OrderSnapshot?)null);

                    TradingCycleResult result =
                        await cycle.RunOnceAsync();

                    Assert.That(
                        result.Outcome,
                        Is.EqualTo(CycleOutcome.Blocked));

                    // Must not submit another order
                    gateway.Verify(g => g.SubmitOrderAsync(
                        It.IsAny<OrderIntent>(),
                        It.IsAny<CancellationToken>()),
                        Times.Never);

                    TradeState? restored =
                        await store.LoadAsync(
                            "test-account",
                            "AAPL",
                            "1Min",
                            "fib-v1");

                    Assert.That(
                        restored!.PendingOrder!.ClientOrderId,
                        Is.EqualTo("previous-order-001"));
                }
            }
            finally
            {
                DeleteTestDirectory(directory);
            }
        }

        // Shared setup with real application/domain classes
        private static TradingCycleService CreateCycle(
            ITradeStateStore store,
            decimal maxOrderValue,
            out Mock<ITradingGateway> gateway)
        {
            gateway = new Mock<ITradingGateway>(
                MockBehavior.Strict);

            var marketData = new Mock<IMarketDataService>(
                MockBehavior.Strict);

            gateway.Setup(g => g.GetAccountAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TradingAccountSnapshot(
                    "test-account",
                    10000m,
                    10000m,
                    true,
                    true,
                    Now));

            gateway.Setup(g => g.GetPositionAsync(
                    "AAPL",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((PositionSnapshot?)null);

            gateway.Setup(g => g.IsMarketOpenAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // The interface returns Task<List<MarketBar>>
            List<MarketBar> bars = CreateMarketBars();

            marketData.Setup(m => m.GetCompletedBarsAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(bars);

            FibonacciSettings fibonacciSettings =
                new FibonacciSettings(
                    1, 1, 0.382m, 0.618m);

            FibonacciCalc calculator =
                new FibonacciCalc();

            ITradingStrategy strategy =
                new FibonacciRetracementStrategy(
                    new SwingDetector(),
                    calculator,
                    fibonacciSettings);

            BotSettings settings = new BotSettings(
                "AAPL",
                "1Min",
                "fib-v1",
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMinutes(3),
                10,
                1m);

            RiskLimits riskLimits = new RiskLimits(
                maxOrderValue,
                10m,
                5000m,
                0.02m);

            return new TradingCycleService(
                marketData.Object,
                gateway.Object,
                store,
                strategy,
                calculator,
                new RiskManager(),
                riskLimits,
                new BuyOrderCreator(),
                new SellOrderCreator(),
                settings,
                new FixedTimeProvider(Now),
                enablePaperOrders: false);
        }

        // Deterministic market data for Fibonacci detection
        private static List<MarketBar> CreateMarketBars()
        {
            return new List<MarketBar>
            {
                Bar(0, 120m, 140m, 130m),
                Bar(1, 110m, 135m, 120m),
                Bar(2, 100m, 130m, 110m),
                Bar(3, 120m, 150m, 140m),
                Bar(4, 140m, 180m, 170m),
                Bar(5, 160m, 200m, 190m),
                Bar(6, 150m, 190m, 170m),
                Bar(7, 148m, 180m, 165m),
                Bar(8, 145m, 175m, 170m),
                Bar(9, 140m, 165m, 155m)
            };
        }

        private static MarketBar Bar(
            int minute,
            decimal low,
            decimal high,
            decimal close)
        {
            DateTimeOffset openTime =
                Start.AddMinutes(minute);

            return new MarketBar(
                "AAPL",
                openTime,
                openTime.AddMinutes(1),
                close,
                high,
                low,
                close,
                1000m);
        }

        // Same event mapping used in Program.cs
        private static void PublishResult(
            BotEventPublisher publisher,
            TradingCycleResult result)
        {
            TradeLogLevel level =
                result.Outcome == CycleOutcome.Blocked
                    ? TradeLogLevel.Warning
                    : TradeLogLevel.Information;

            publisher.Publish(new TradeLogEntry(
                result.AtUtc,
                level,
                "TradingCycleCompleted",
                result.Reason,
                symbol: "AAPL",
                status: result.Outcome.ToString()));
        }

        private static string CreateTestDirectory()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "BinaryTickerIntegrationTests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void DeleteTestDirectory(
            string directory)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

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
