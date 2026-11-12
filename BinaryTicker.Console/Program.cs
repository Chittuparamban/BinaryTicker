
using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

using Alpaca.Markets;

using BinaryTicker.Application.Events;
using BinaryTicker.Application.Logging;
using BinaryTicker.Application.Trading;

using BinaryTicker.Domain.Fibonacci;
using BinaryTicker.Domain.Order;
using BinaryTicker.Domain.Risk;
using BinaryTicker.Domain.Strategies;

using BinaryTicker.Infrastructure;
using BinaryTicker.Infrastructure.DatabaseStorage;
using BinaryTicker.Infrastructure.Logging;

namespace BinaryTicker
{
    internal static class Program
    {
        public static async Task<int> Main()
        {
            try
            {
                // Load Alpaca credentials
                string keyId = Environment.GetEnvironmentVariable( "ALPACA_API_KEY_ID")
                    ?? throw new InvalidOperationException("Missing API Key ID.");

                string secretKey =  Environment.GetEnvironmentVariable( "ALPACA_API_SECRET_KEY")
                    ?? throw new InvalidOperationException("Missing Secret Key.");

                AlpacaSettings settings = new AlpacaSettings(keyId,secretKey,"iex","raw",TimeSpan.FromSeconds(30),true);

                settings.ValidatePaperEnvironment();

                Console.WriteLine("Credentials loaded.");
                Console.WriteLine("Connecting to Alpaca Paper Trading...");

                // Create Alpaca Trading Client
                using var client =Environments.Paper.GetAlpacaTradingClient(new SecretKey(settings.KeyId,settings.SecretKey));

                var marketClock = await client.GetClockAsync();

                if (marketClock is null)
                {
                    throw new InvalidOperationException("Alpaca returned an empty clock response.");
                }

                Console.WriteLine("Connected successfully");

                Console.WriteLine("Timestamp: {0}, NextOpen: {1}, NextClose: {2}",
                    marketClock.TimestampUtc,marketClock.NextOpenUtc,marketClock.NextCloseUtc);

                // Create Trading Gateway
                AlpacaTradingGateway gateway =new AlpacaTradingGateway(client, settings);

                var account = await gateway.GetAccountAsync();

                Console.WriteLine($"Cash: {account.Cash}");
                Console.WriteLine($"Buying Power: {account.BuyingPower}");
                Console.WriteLine($"Can Trade: {account.CanTrade}");

                bool isOpen = await gateway.IsMarketOpenAsync();

                Console.WriteLine($"Market Open: {isOpen}");

                // Check existing position
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

                // Create Alpaca Market Data Client
                using var dataClient =Environments.Paper.GetAlpacaDataClient(new SecretKey(settings.KeyId,settings.SecretKey));

                TimeProvider timeProvider = TimeProvider.System;

                AlpacaMarketDataService marketData =new AlpacaMarketDataService(dataClient,settings,timeProvider);

                // configure Fibonacci Strategy
                FibonacciSettings fibonacciSettings = new FibonacciSettings( 2, 2,  0.382m, 0.618m);

                FibonacciCalc calculator = new FibonacciCalc();

                ITradingStrategy strategy = new FibonacciRetracementStrategy(
                        new SwingDetector(),calculator, fibonacciSettings);

                // configure Risk Management
                RiskLimits riskLimits =new RiskLimits(
                        1000m,
                        10m,
                        5000m,
                        0.02m);

                //Bot Settings
                BotSettings botSettings = new BotSettings("AAPL","1Min","fib-v1", TimeSpan.FromSeconds(30),
                        TimeSpan.FromMinutes(3),200, 1m);

                // configure JSON State Storage
                string statePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BinaryTicker","paper-state.json");

                using var stateStore = new JsonStateStore(statePath);

                // Create Event Publisher
                BotEventPublisher publisher = new BotEventPublisher();

                // Subscribe Console Observer
                using var consoleObserver = new ConsoleTradeObserver( publisher, Console.Out);

                // Subscribe File Observer

                string logPath = Path.Combine(Path.GetDirectoryName(statePath)!, "trade-events.jsonl");

                using var fileObserver =new FileTradeObserver( publisher,logPath);

                // Create Trading Cycle Service
                // The publisher already has both observers.
                TradingCycleService tradingCycle =new TradingCycleService(marketData,
                        gateway,stateStore, strategy, calculator, new RiskManager(),
                        riskLimits,new BuyOrderCreator(), new SellOrderCreator(),
                        botSettings, timeProvider, enablePaperOrders: true, publisher: publisher);

                // Create BotRunner
                
                void OnTradingCycleCompleted(TradingCycleResult result)
                {
                    TradeLogLevel level = TradeLogLevel.Information;
                    if (result.Outcome == CycleOutcome.Blocked)
                    {
                        level = TradeLogLevel.Warning;
                    }

                     TradeLogEntry entry = new TradeLogEntry(timestampUtc: result.AtUtc,level: level,
        eventName: "TradingCycleCompleted", message: result.Reason, symbol: botSettings.Symbol,
        status: result.Outcome.ToString(),  clientOrderId: result.Order?.ClientOrderId,
        brokerOrderId: result.Order?.BrokerOrderId);

                    publisher.Publish(entry);
                }

                BotRunner runner = new BotRunner(tradingCycle,botSettings,OnTradingCycleCompleted);

                // Configure Graceful Shutdown
                using var shutdown = new CancellationTokenSource();
                ConsoleCancelEventHandler cancelHandler =(sender, e) =>
                    {
                        e.Cancel = true;
                        shutdown.Cancel();
                    };

                Console.CancelKeyPress += cancelHandler;

                bool started = false;

                try
                {
                    // publishing starting Event
                    publisher.Publish(new TradeLogEntry(timeProvider.GetUtcNow(),
                        TradeLogLevel.Information,"BotStarting","BinaryTicker startup requested.",
                        botSettings.Symbol));

                    // recover saved pending orders
                    await runner.RecoverAsync(shutdown.Token);

                    // Start Trading Loop
                    await runner.StartAsync(shutdown.Token);
                    started = true;

                    // Surface any immediate loop failure
                    if (runner.Completion.IsFaulted)
                    {
                        await runner.Completion;
                    }

                    publisher.Publish(new TradeLogEntry(
                        timeProvider.GetUtcNow(),TradeLogLevel.Information,
                        "BotStarted","BinaryTicker trading loop started.",botSettings.Symbol));

                    Console.WriteLine("BinaryTicker is running.");
                    Console.WriteLine("Press Ctrl+C to stop.");

                    //Wait until shutdown or failure
                    await runner.Completion.WaitAsync(shutdown.Token);
                }
                catch (OperationCanceledException)
                    when (shutdown.IsCancellationRequested)
                {
                    Console.WriteLine("Shutdown requested.");
                }
                finally
                {
                    // Remove cancellation handler
                    Console.CancelKeyPress -= cancelHandler;

                    // STEP 20: Stop the bot
                    if (started)
                    {
                        await runner.StopAsync();

                        if (runner.LastLoopFailure is null)
                        {
                            // STEP 21: Publish Shutdown Event
                            publisher.Publish(new TradeLogEntry( timeProvider.GetUtcNow(),
                                TradeLogLevel.Information,"BotStopped","BinaryTicker trading loop stopped.",
                                botSettings.Symbol));

                            Console.WriteLine("BinaryTicker stopped.");
                        }
                        else
                        {
                            // Do not claim a failed bot stopped normally
                            Console.Error.WriteLine("Trading loop stopped following an error.");
                        }
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
