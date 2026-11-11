
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Alpaca.Markets;
using BinaryTicker.Infrastructure;
using BinaryTicker.Infrastructure.DatabaseStorage;
using BinaryTicker.Infrastructure.Logging;
using BinaryTicker.Application.Trading;
using BinaryTicker.Application.Events;
using BinaryTicker.Application.Logging;
using BinaryTicker.Domain.Fibonacci;
using BinaryTicker.Domain.Order;
using BinaryTicker.Domain.Risk;
using BinaryTicker.Domain.Strategies;

namespace BinaryTicker
{
    internal static class Program
    {
        public static async Task<int> Main()
        {
            try
            {
                // STEP 1: Load Alpaca credentials
                string keyId = Environment.GetEnvironmentVariable("ALPACA_API_KEY_ID")
                    ?? throw new InvalidOperationException("Missing API Key ID.");

                string secretKey = Environment.GetEnvironmentVariable("ALPACA_API_SECRET_KEY")
                    ?? throw new InvalidOperationException("Missing Secret Key.");

                AlpacaSettings settings = new AlpacaSettings(
                    keyId, secretKey, "iex", "raw",
                    TimeSpan.FromSeconds(30), true);

                settings.ValidatePaperEnvironment();

                Console.WriteLine("Credentials loaded.");
                Console.WriteLine("Connecting to Alpaca Paper Trading...");

                // STEP 2: Create Alpaca Trading Client
                using var client = Environments.Paper.GetAlpacaTradingClient(
                    new SecretKey(settings.KeyId, settings.SecretKey));

                var marketClock = await client.GetClockAsync();

                if (marketClock is null)
                {
                    throw new InvalidOperationException(
                        "Alpaca returned an empty clock response.");
                }

                Console.WriteLine("Connected successfully!");

                Console.WriteLine(
                    "Timestamp: {0}, NextOpen: {1}, NextClose: {2}",
                    marketClock.TimestampUtc,
                    marketClock.NextOpenUtc,
                    marketClock.NextCloseUtc);

                // STEP 3: Create Alpaca Trading Gateway
                AlpacaTradingGateway gateway = new AlpacaTradingGateway(
                    client, settings);

                var account = await gateway.GetAccountAsync();

                Console.WriteLine($"Cash: {account.Cash}");
                Console.WriteLine($"Buying Power: {account.BuyingPower}");
                Console.WriteLine($"Can Trade: {account.CanTrade}");

                bool isOpen = await gateway.IsMarketOpenAsync();

                Console.WriteLine($"Market Open: {isOpen}");

                var position = await gateway.GetPositionAsync("AAPL");

                if (position is null)
                {
                    Console.WriteLine("No AAPL position found.");
                }
                else
                {
                    Console.WriteLine($"Symbol: {position.Symbol}");
                    Console.WriteLine($"Quantity: {position.Quantity}");
                    Console.WriteLine($"Average Entry Price: {position.AverageEntryPrice}");
                    Console.WriteLine($"Market Value: {position.MarketValue}");
                }

                // STEP 4: Create Alpaca Market Data Client
                using var dataClient = Environments.Paper.GetAlpacaDataClient(
                    new SecretKey(settings.KeyId, settings.SecretKey));

                TimeProvider timeProvider = TimeProvider.System;

                AlpacaMarketDataService marketData = new AlpacaMarketDataService(
                    dataClient, settings, timeProvider);

                // STEP 5: Configure Fibonacci Strategy
                FibonacciSettings fibonacciSettings = new FibonacciSettings(
                    2, 2, 0.382m, 0.618m);

                FibonacciCalc calculator = new FibonacciCalc();

                ITradingStrategy strategy = new FibonacciRetracementStrategy(
                    new SwingDetector(),
                    calculator,
                    fibonacciSettings);

                // STEP 6: Configure Risk Management
                RiskLimits riskLimits = new RiskLimits(
                    1000m, 10m, 5000m, 0.02m);

                // STEP 7: Configure Bot Settings
                BotSettings botSettings = new BotSettings(
                    "AAPL",
                    "1Min",
                    "fib-v1",
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromMinutes(3),
                    200,
                    1m);

                // STEP 8: Configure JSON State Storage
                string statePath = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "BinaryTicker",
                    "paper-state.json");

                using var stateStore = new JsonStateStore(statePath);

                // STEP 9: Create Trading Cycle Service
                TradingCycleService tradingCycle = new TradingCycleService(
                    marketData,
                    gateway,
                    stateStore,
                    strategy,
                    calculator,
                    new RiskManager(),
                    riskLimits,
                    new BuyOrderCreator(),
                    new SellOrderCreator(),
                    botSettings,
                    timeProvider,
                    enablePaperOrders: false);

                // STEP 10: Create Event Publisher
                BotEventPublisher publisher = new BotEventPublisher();

                // STEP 11: Subscribe Console Observer
                using var consoleObserver = new ConsoleTradeObserver(
                    publisher,
                    Console.Out);

                // STEP 12: Subscribe File Observer
                string logPath = Path.Combine(
                    Path.GetDirectoryName(statePath)!,
                    "trade-events.jsonl");

                using var fileObserver = new FileTradeObserver(
                    publisher,
                    logPath);

                // STEP 13: Create BotRunner
                BotRunner runner = new BotRunner(
                    tradingCycle,
                    botSettings,
                    result =>
                    {
                        TradeLogLevel level =
                            result.Outcome == CycleOutcome.Blocked
                                ? TradeLogLevel.Warning
                                : TradeLogLevel.Information;

                        TradeLogEntry entry = new TradeLogEntry(
                            timestampUtc: result.AtUtc,
                            level: level,
                            eventName: "TradingCycleCompleted",
                            message: result.Reason,
                            symbol: botSettings.Symbol,
                            status: result.Outcome.ToString(),
                            clientOrderId: result.Order?.ClientOrderId,
                            brokerOrderId: result.Order?.BrokerOrderId);

                        publisher.Publish(entry);
                    });

                // STEP 14: Configure Graceful Shutdown
                using var shutdown = new CancellationTokenSource();

                ConsoleCancelEventHandler cancelHandler = (sender, e) =>
                {
                    e.Cancel = true;
                    shutdown.Cancel();
                };

                Console.CancelKeyPress += cancelHandler;

                bool started = false;

                try
                {
                    // STEP 15: Publish Startup Event
                    publisher.Publish(new TradeLogEntry(
                        timeProvider.GetUtcNow(),
                        TradeLogLevel.Information,
                        "BotStarting",
                        "BinaryTicker startup requested.",
                        botSettings.Symbol));

                    // STEP 16: Recover Pending Orders
                    await runner.RecoverAsync(shutdown.Token);

                    // STEP 17: Start BotRunner
                    await runner.StartAsync(shutdown.Token);
                    started = true;

                    publisher.Publish(new TradeLogEntry(
                        timeProvider.GetUtcNow(),
                        TradeLogLevel.Information,
                        "BotStarted",
                        "BinaryTicker trading loop started.",
                        botSettings.Symbol));

                    Console.WriteLine("BinaryTicker is running.");
                    Console.WriteLine("Press Ctrl+C to stop.");

                    // STEP 18: Keep Running Until Cancellation or Failure
                    await runner.Completion.WaitAsync(shutdown.Token);
                }
                catch (OperationCanceledException)
                    when (shutdown.IsCancellationRequested)
                {
                    Console.WriteLine("Shutdown requested.");
                }
                finally
                {
                    try
                    {
                        // STEP 19: Stop BotRunner
                        if (started)
                        {
                            await runner.StopAsync();

                            // STEP 20: Publish Shutdown Event
                            publisher.Publish(new TradeLogEntry(
                                timeProvider.GetUtcNow(),
                                TradeLogLevel.Information,
                                "BotStopped",
                                "BinaryTicker trading loop stopped.",
                                botSettings.Symbol));
                        }
                    }
                    finally
                    {
                        // STEP 21: Unsubscribe Cancellation Handler
                        Console.CancelKeyPress -= cancelHandler;
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"BinaryTicker error: {ex.Message}");

                return 1;
            }
        }
    }
}
