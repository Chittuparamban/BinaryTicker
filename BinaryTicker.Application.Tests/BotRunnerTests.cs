
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
                Action<TradingCycleResult>? observer = null)
        {
            var gateway = new Mock<ITradingGateway>();
            var marketData = new Mock<IMarketDataService>();
            var store = new Mock<ITradeStateStore>();
            var strategy = new Mock<ITradingStrategy>();

            // Simulate an eligible paper-trading account
            gateway.Setup(g => g.GetAccountAsync(
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TradingAccountSnapshot(
                    "test-account",
                    10000m,
                    10000m,
                    true,
                    true,
                    DateTimeOffset.UtcNow));

            // No previously saved trading state
            store.Setup(s => s.LoadAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((TradeState?)null);

            // No completed market bars for this test

List<MarketBar> emptyBars = new List<MarketBar>();

marketData
    .Setup(m => m.GetCompletedBarsAsync(
        It.IsAny<string>(),
        It.IsAny<string>(),
        It.IsAny<DateTimeOffset>(),
        It.IsAny<DateTimeOffset>(),
        It.IsAny<CancellationToken>()))
    .ReturnsAsync(emptyBars);


            BotSettings settings = new BotSettings(
                "AAPL",
                "1Min",
                "fib-v1",
                TimeSpan.FromHours(1),
                TimeSpan.FromMinutes(3),
                200,
                1m);

            var calculator = new FibonacciCalc();

            var tradingCycle = new TradingCycleService(
                marketData.Object,
                gateway.Object,
                store.Object,
                strategy.Object,
                calculator,
                new RiskManager(),
                new RiskLimits(
                    1000m, 10m, 5000m, 0.02m),
                new BuyOrderCreator(),
                new SellOrderCreator(),
                settings,
                TimeProvider.System,
                enablePaperOrders: false);

            var runner = new BotRunner(
                tradingCycle,
                settings,
                observer);

            return (runner, gateway);
        }

        // TEST 1: Bot starts and executes one cycle
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
                Assert.That(
                    runner.StateName, Is.EqualTo("Stopped"));

                await runner.StartAsync();

                Assert.That(
                    runner.StateName, Is.EqualTo("Running"));

                TradingCycleResult result =
                    await firstCycle.Task.WaitAsync(
                        TimeSpan.FromSeconds(5));

                Assert.That(
                    result.Outcome,
                    Is.EqualTo(CycleOutcome.NoNewBar));

                gateway.Verify(g => g.SubmitOrderAsync(
                    It.IsAny<OrderIntent>(),
                    It.IsAny<CancellationToken>()),
                    Times.Never);
            }
            finally
            {
                await runner.StopAsync();
            }

            Assert.That(
                runner.StateName, Is.EqualTo("Stopped"));
        }

        // TEST 2: Repeated Start does not create another loop
        [Test]
        public async Task StartAsync_Twice_DoesNotCreateSecondLoop()
        {
            var (runner, gateway) = CreateRunner();

            try
            {
                await runner.StartAsync();
                await runner.StartAsync();

                Assert.That(
                    runner.StateName, Is.EqualTo("Running"));

                // Only the first loop should request the account
                gateway.Verify(g => g.GetAccountAsync(
                    It.IsAny<CancellationToken>()),
                    Times.Once);
            }
            finally
            {
                await runner.StopAsync();
            }
        }

        // TEST 3: Stopping twice is harmless
        [Test]
        public async Task StopAsync_Twice_RemainsStopped()
        {
            var (runner, _) = CreateRunner();

            await runner.StartAsync();
            await runner.StopAsync();
            await runner.StopAsync();

            Assert.That(
                runner.StateName, Is.EqualTo("Stopped"));
        }

        // TEST 4: Recovery is forbidden while running
        [Test]
        public async Task RecoverAsync_WhileRunning_Throws()
        {
            var (runner, _) = CreateRunner();

            try
            {
                await runner.StartAsync();

                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await runner.RecoverAsync());
            }
            finally
            {
                await runner.StopAsync();
            }
        }

        // TEST 5: Recovery is allowed while stopped
        [Test]
        public async Task RecoverAsync_WhileStopped_DoesNotStartLoop()
        {
            TradingCycleResult? recoveryResult = null;

            var (runner, _) = CreateRunner(
                result => recoveryResult = result);

            await runner.RecoverAsync();

            Assert.That(
                runner.StateName, Is.EqualTo("Stopped"));

            Assert.That(recoveryResult, Is.Not.Null);

            Assert.That(
                recoveryResult!.Outcome,
                Is.EqualTo(CycleOutcome.Hold));
        }
    }
}
