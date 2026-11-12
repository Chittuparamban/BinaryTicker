
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Moq;
using NUnit.Framework;

using BinaryTicker.Application.Interfaces;
using BinaryTicker.Application.Trading;

using BinaryTicker.Domain;
using BinaryTicker.Domain.Fibonacci;
using BinaryTicker.Domain.Order;
using BinaryTicker.Domain.Risk;
using BinaryTicker.Domain.Strategies;

namespace BinaryTicker.Application.Tests
{
    [TestFixture]
    public class BotRunnerTests
    {
        // Creates a BotRunner without real Alpaca calls
        private static (
            BotRunner Runner,
            Mock<ITradingGateway> Gateway)
            CreateRunner(
                Action<TradingCycleResult>? observer = null,
                bool marketOpen = true)
        {
            // STEP 1: Create mock dependencies
            var gateway = new Mock<ITradingGateway>(
                MockBehavior.Strict);

            var marketData = new Mock<IMarketDataService>(
                MockBehavior.Strict);

            var store = new Mock<ITradeStateStore>(
                MockBehavior.Strict);

            var strategy = new Mock<ITradingStrategy>(
                MockBehavior.Strict);

            // STEP 2: Simulate a valid paper account
            gateway
                .Setup(g => g.GetAccountAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TradingAccountSnapshot(
                    "test-account",
                    10000m,
                    10000m,
                    true,
                    true,
                    DateTimeOffset.UtcNow));

            // STEP 3: Simulate no existing AAPL position
            gateway
                .Setup(g => g.GetPositionAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((PositionSnapshot?)null);

            // STEP 4: Simulate no open broker orders
            gateway
                .Setup(g => g.HasOpenOrdersAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            // STEP 5: Simulate market hours
            gateway
                .Setup(g => g.IsMarketOpenAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(marketOpen);

            // STEP 6: Simulate a tradable stock
            gateway
                .Setup(g => g.IsAssetTradableAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // STEP 7: No previously saved trading state
            store
                .Setup(s => s.LoadAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((TradeState?)null);

            // STEP 8: No completed market candles
            List<MarketBar> emptyBars = new List<MarketBar>();

            marketData
                .Setup(m => m.GetCompletedBarsAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(emptyBars);

            // STEP 9: Configure bot settings
            BotSettings settings = new BotSettings(
                "AAPL",
                "1Min",
                "fib-v1",
                TimeSpan.FromHours(1),
                TimeSpan.FromMinutes(3),
                200,
                1m);

            // STEP 10: Create the Fibonacci calculator
            FibonacciCalc calculator = new FibonacciCalc();

            // STEP 11: Create TradingCycleService
            // Paper submission is disabled in unit tests.
            TradingCycleService tradingCycle =
                new TradingCycleService(
                    marketData.Object,
                    gateway.Object,
                    store.Object,
                    strategy.Object,
                    calculator,
                    new RiskManager(),
                    new RiskLimits(
                        1000m,
                        10m,
                        5000m,
                        0.02m),
                    new BuyOrderCreator(),
                    new SellOrderCreator(),
                    settings,
                    TimeProvider.System,
                    enablePaperOrders: false);

            // STEP 12: Create BotRunner
            BotRunner runner = new BotRunner(
                tradingCycle,
                settings,
                observer);

            return (runner, gateway);
        }

        // TEST 1: Starting the bot executes a trading cycle
        [Test]
        public async Task StartAsync_StartsTradingLoop()
        {
            var firstCycle =
                new TaskCompletionSource<TradingCycleResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            var (runner, gateway) = CreateRunner(
                result => firstCycle.TrySetResult(result));

            try
            {
                // Initially stopped
                Assert.That(
                    runner.StateName,
                    Is.EqualTo("Stopped"));

                // Start the bot
                await runner.StartAsync();

                Assert.That(
                    runner.StateName,
                    Is.EqualTo("Running"));

                // Wait until the first cycle completes
                TradingCycleResult result =
                    await firstCycle.Task.WaitAsync(
                        TimeSpan.FromSeconds(5));

                // The market is open, but no candles exist.
                Assert.That(
                    result.Outcome,
                    Is.EqualTo(CycleOutcome.NoNewBar));

                // Verify market data was reached
                gateway.Verify(g => g.IsMarketOpenAsync(
                    It.IsAny<CancellationToken>()),
                    Times.AtLeastOnce());

                // No paper orders should be submitted
                gateway.Verify(g => g.SubmitOrderAsync(
                    It.IsAny<OrderIntent>(),
                    It.IsAny<CancellationToken>()),
                    Times.Never());
            }
            finally
            {
                await runner.StopAsync();
            }

            Assert.That(
                runner.StateName,
                Is.EqualTo("Stopped"));
        }

        // TEST 2: Repeated Start does not create another loop
        [Test]
        public async Task StartAsync_Twice_DoesNotCreateSecondLoop()
        {
            var firstCycle =
                new TaskCompletionSource<TradingCycleResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            var (runner, gateway) = CreateRunner(
                result => firstCycle.TrySetResult(result));

            try
            {
                // First start
                await runner.StartAsync();

                // Wait for the first cycle to complete
                await firstCycle.Task.WaitAsync(
                    TimeSpan.FromSeconds(5));

                // Second start should not create another loop
                await runner.StartAsync();

                Assert.That(
                    runner.StateName,
                    Is.EqualTo("Running"));

                // Only one trading cycle should have
                // requested account information so far.
                gateway.Verify(g => g.GetAccountAsync(
                    It.IsAny<CancellationToken>()),
                    Times.Once());
            }
            finally
            {
                await runner.StopAsync();
            }
        }

        // TEST 3: Stopping the bot twice is harmless
        [Test]
        public async Task StopAsync_Twice_RemainsStopped()
        {
            var (runner, _) = CreateRunner();

            try
            {
                await runner.StartAsync();

                Assert.That(
                    runner.StateName,
                    Is.EqualTo("Running"));

                // First stop
                await runner.StopAsync();

                Assert.That(
                    runner.StateName,
                    Is.EqualTo("Stopped"));

                // Second stop
                await runner.StopAsync();

                Assert.That(
                    runner.StateName,
                    Is.EqualTo("Stopped"));
            }
            finally
            {
                await runner.StopAsync();
            }
        }

        // TEST 4: Cannot recover while running
        [Test]
        public async Task RecoverAsync_WhileRunning_Throws()
        {
            var (runner, _) = CreateRunner();

            try
            {
                await runner.StartAsync();

                Assert.That(
                    runner.StateName,
                    Is.EqualTo("Running"));

                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await runner.RecoverAsync());
            }
            finally
            {
                await runner.StopAsync();
            }
        }

        // TEST 5: Recovery is permitted while stopped
        [Test]
        public async Task RecoverAsync_WhileStopped_DoesNotStartLoop()
        {
            TradingCycleResult? recoveryResult = null;

            var (runner, _) = CreateRunner(
                result => recoveryResult = result);

            // Recover without starting the bot
            await runner.RecoverAsync();

            Assert.That(
                runner.StateName,
                Is.EqualTo("Stopped"));

            Assert.That(
                recoveryResult,
                Is.Not.Null);

            Assert.That(
                recoveryResult!.Outcome,
                Is.EqualTo(CycleOutcome.Hold));
        }

        // TEST 6: A closed market should produce Hold
        [Test]
        public async Task StartAsync_WhenMarketClosed_ReturnsHold()
        {
            var firstCycle =
                new TaskCompletionSource<TradingCycleResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            var (runner, gateway) = CreateRunner(
                result => firstCycle.TrySetResult(result),
                marketOpen: false);

            try
            {
                await runner.StartAsync();

                TradingCycleResult result =
                    await firstCycle.Task.WaitAsync(
                        TimeSpan.FromSeconds(5));

                // Hold is correct when the market is closed.
                Assert.That(
                    result.Outcome,
                    Is.EqualTo(CycleOutcome.Hold));

                // No order should be submitted
                gateway.Verify(g => g.SubmitOrderAsync(
                    It.IsAny<OrderIntent>(),
                    It.IsAny<CancellationToken>()),
                    Times.Never());
            }
            finally
            {
                await runner.StopAsync();
            }
        }
    }
}
