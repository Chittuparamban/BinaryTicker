using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
        // Create the real runner, but use simple in-memory test dependencies.
        // No Moq and no network calls to Alpaca.
        private static (BotRunner Runner, FakeTradingGateway Gateway) CreateRunner(
            Action<TradingCycleResult>? observer = null,
            bool marketOpen = true)
        {
            FakeTradingGateway gateway = new FakeTradingGateway(marketOpen);
            FakeMarketDataService marketData = new FakeMarketDataService();
            FakeTradeStateStore store = new FakeTradeStateStore();

            BotSettings settings = new BotSettings(
                "AAPL",
                "1Min",
                "fib-v1",
                TimeSpan.FromHours(1),
                TimeSpan.FromMinutes(3),
                200,
                1m);

            FibonacciCalc calculator = new FibonacciCalc();

            ITradingStrategy strategy = new FibonacciRetracementStrategy(
                new SwingDetector(),
                calculator,
                new FibonacciSettings(2, 2, 0.382m, 0.618m));

            TradingCycleService tradingCycle = new TradingCycleService(
                marketData,
                gateway,
                store,
                strategy,
                calculator,
                new RiskManager(),
                new RiskLimits(1000m, 10m, 5000m, 0.02m),
                new BuyOrderCreator(),
                new SellOrderCreator(),
                settings,
                TimeProvider.System,
                enablePaperOrders: false);

            BotRunner runner = new BotRunner(tradingCycle, settings, observer);
            return (runner, gateway);
        }

        [Test]
        public async Task StartAsync_StartsTradingLoop()
        {
            TaskCompletionSource<TradingCycleResult> firstCycle =
                new TaskCompletionSource<TradingCycleResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            var (runner, gateway) = CreateRunner(
                result => firstCycle.TrySetResult(result));

            try
            {
                Assert.That(runner.StateName, Is.EqualTo("Stopped"));

                await runner.StartAsync();
                Assert.That(runner.StateName, Is.EqualTo("Running"));

                TradingCycleResult result = await firstCycle.Task.WaitAsync(
                    TimeSpan.FromSeconds(5));

                Assert.That(result.Outcome, Is.EqualTo(CycleOutcome.NoNewBar));
                Assert.That(gateway.MarketOpenChecks, Is.GreaterThanOrEqualTo(1));
                Assert.That(gateway.SubmittedOrderCalls, Is.EqualTo(0));
            }
            finally
            {
                await runner.StopAsync();
            }

            Assert.That(runner.StateName, Is.EqualTo("Stopped"));
        }

        [Test]
        public async Task StartAsync_Twice_DoesNotCreateSecondLoop()
        {
            TaskCompletionSource<TradingCycleResult> firstCycle =
                new TaskCompletionSource<TradingCycleResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            var (runner, gateway) = CreateRunner(
                result => firstCycle.TrySetResult(result));

            try
            {
                await runner.StartAsync();
                await firstCycle.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await runner.StartAsync();

                Assert.That(runner.StateName, Is.EqualTo("Running"));
                Assert.That(gateway.GetAccountCalls, Is.EqualTo(1));
            }
            finally
            {
                await runner.StopAsync();
            }
        }

        [Test]
        public async Task StopAsync_Twice_RemainsStopped()
        {
            var (runner, _) = CreateRunner();

            try
            {
                await runner.StartAsync();
                await runner.StopAsync();
                await runner.StopAsync();

                Assert.That(runner.StateName, Is.EqualTo("Stopped"));
            }
            finally
            {
                await runner.StopAsync();
            }
        }

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

        [Test]
        public async Task RecoverAsync_WhileStopped_DoesNotStartLoop()
        {
            TradingCycleResult? recoveryResult = null;

            var (runner, _) = CreateRunner(
                result => recoveryResult = result);

            await runner.RecoverAsync();

            Assert.That(runner.StateName, Is.EqualTo("Stopped"));
            Assert.That(recoveryResult, Is.Not.Null);
            Assert.That(recoveryResult!.Outcome, Is.EqualTo(CycleOutcome.Hold));
        }

        [Test]
        public async Task StartAsync_WhenMarketClosed_ReturnsHold()
        {
            TaskCompletionSource<TradingCycleResult> firstCycle =
                new TaskCompletionSource<TradingCycleResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            var (runner, gateway) = CreateRunner(
                result => firstCycle.TrySetResult(result),
                marketOpen: false);

            try
            {
                await runner.StartAsync();

                TradingCycleResult result = await firstCycle.Task.WaitAsync(
                    TimeSpan.FromSeconds(5));

                Assert.That(result.Outcome, Is.EqualTo(CycleOutcome.Hold));
                Assert.That(gateway.SubmittedOrderCalls, Is.EqualTo(0));
            }
            finally
            {
                await runner.StopAsync();
            }
        }

        // Hand-written replacement for Mock<ITradingGateway>.
        private sealed class FakeTradingGateway : ITradingGateway
        {
            private readonly bool _marketOpen;
            private int _getAccountCalls;
            private int _marketOpenChecks;
            private int _submittedOrderCalls;

            public int GetAccountCalls => Volatile.Read(ref _getAccountCalls);
            public int MarketOpenChecks => Volatile.Read(ref _marketOpenChecks);
            public int SubmittedOrderCalls => Volatile.Read(ref _submittedOrderCalls);

            public FakeTradingGateway(bool marketOpen)
            {
                _marketOpen = marketOpen;
            }

            public Task<TradingAccountSnapshot> GetAccountAsync(
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref _getAccountCalls);

                TradingAccountSnapshot account = new TradingAccountSnapshot(
                    "test-account",
                    10000m,
                    10000m,
                    true,
                    true,
                    DateTimeOffset.UtcNow);

                return Task.FromResult(account);
            }

            public Task<PositionSnapshot?> GetPositionAsync(
                string symbol,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult<PositionSnapshot?>(null);
            }

            public Task<OrderSnapshot?> GetOrderByClientOrderIdAsync(
                string clientOrderId,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult<OrderSnapshot?>(null);
            }

            public Task<bool> IsMarketOpenAsync(
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref _marketOpenChecks);
                return Task.FromResult(_marketOpen);
            }

            public Task<bool> IsAssetTradableAsync(
                string symbol,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(true);
            }

            public Task<bool> HasOpenOrdersAsync(
                string symbol,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(false);
            }

            public Task<OrderSnapshot> SubmitOrderAsync(
                OrderIntent intent,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref _submittedOrderCalls);
                return Task.FromException<OrderSnapshot>(
                    new InvalidOperationException(
                        "This test must not submit trading orders."));
            }
        }

        // Hand-written replacement for Mock<IMarketDataService>.
        private sealed class FakeMarketDataService : IMarketDataService
        {
            public Task<List<MarketBar>> GetCompletedBarsAsync(
                string symbol,
                string timeframe,
                DateTimeOffset fromUtc,
                DateTimeOffset toUtc,
                CancellationToken cancellationToken = default)
            {
                // Empty market data makes the trading cycle return NoNewBar.
                return Task.FromResult(new List<MarketBar>());
            }
        }

        // Hand-written replacement for Mock<ITradeStateStore>.
        private sealed class FakeTradeStateStore : ITradeStateStore
        {
            private TradeState? _savedState;

            public Task<TradeState?> LoadAsync(
                string accountId,
                string symbol,
                string timeframe,
                string strategyVersion,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_savedState);
            }

            public Task SaveAsync(
                TradeState state,
                CancellationToken cancellationToken = default)
            {
                _savedState = state;
                return Task.CompletedTask;
            }
        }
    }
}
