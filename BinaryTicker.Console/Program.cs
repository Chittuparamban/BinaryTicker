
using System;
using System.IO;
using Alpaca.Markets;
using BinaryTicker.Infrastructure;
using BinaryTicker.Application.Trading;
using BinaryTicker.Domain.Fibonacci;
using BinaryTicker.Domain.Order;
using BinaryTicker.Domain.Risk;
using BinaryTicker.Domain.Strategies;
using BinaryTicker.Infrastructure.DatabaseStorage;
using System.Threading;
using BinaryTicker.Application.Trading;

namespace BinaryTicker
{
    internal static class Program
    {
        public static async Task<int> Main()
        {
            try
            {
                string keyId = Environment.GetEnvironmentVariable("ALPACA_API_KEY_ID")
                    ?? throw new InvalidOperationException("Missing API Key ID.");

                string secretKey = Environment.GetEnvironmentVariable("ALPACA_API_SECRET_KEY")
                    ?? throw new InvalidOperationException("Missing Secret Key.");

                AlpacaSettings settings = new AlpacaSettings(
                    keyId, secretKey, "iex", "raw",
                    TimeSpan.FromSeconds(30), true);

                Console.WriteLine("Credentials loaded.");
                Console.WriteLine("Connecting to Alpaca Paper Trading...");

                // Validating before connecting
                settings.ValidatePaperEnvironment();

                // Connection process
                using var client = Environments.Paper.GetAlpacaTradingClient(
                    new SecretKey(settings.KeyId, settings.SecretKey));

                var clock = await client.GetClockAsync();

                if (clock is null)
                {
                    throw new InvalidOperationException(
                        "Alpaca returned an empty clock response.");
                }

                Console.WriteLine("Connected successfully clock is shown");

                Console.WriteLine(
                    "Timestamp: {0}, NextOpen: {1}, NextClose: {2}",
                    clock.TimestampUtc,
                    clock.NextOpenUtc,
                    clock.NextCloseUtc);

                // Create Alpaca Trading Gateway
                AlpacaTradingGateway gateway = new AlpacaTradingGateway(
                    client, settings);

                // Retrieve account information
                var account = await gateway.GetAccountAsync();

                Console.WriteLine("Connected successfully for AlpacaTradingGateway!");
                Console.WriteLine($"Cash: {account.Cash}");
                Console.WriteLine($"Buying Power: {account.BuyingPower}");
                Console.WriteLine($"Can Trade: {account.CanTrade}");

                // Check market status
                bool isOpen = await gateway.IsMarketOpenAsync();

                Console.WriteLine($"Market Open: {isOpen}");

                // Retrieve existing position
                var position = await gateway.GetPositionAsync("AAPL");

                if (position == null)
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
                using var dataClient = Environments.Paper.GetAlpacaDataClient(
                    new SecretKey(settings.KeyId, settings.SecretKey));

                TimeProvider timeProvider = TimeProvider.System;

                // Data adapter
                var marketData = new AlpacaMarketDataService(
                    dataClient, settings, timeProvider);

                // Fibonacci strategy
                var fibonacciSettings = new FibonacciSettings(
                    2, 2, 0.382m, 0.618m);

                var calculator = new FibonacciCalc();

                ITradingStrategy strategy = new FibonacciRetracementStrategy(
                    new SwingDetector(),
                    calculator,
                    fibonacciSettings);

                // Trading risk configuration
                var riskLimits = new RiskLimits(
                    1000m, 10m, 5000m, 0.02m);

                // Runtime configuration
                var botSettings = new BotSettings(
                    "AAPL",
                    "1Min",
                    "fib-v1",
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromMinutes(3),
                    200,
                    1m);

                // Store state outside the source-code repository
                string statePath = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "BinaryTicker",
                    "paper-state.json");

                using var stateStore = new JsonStateStore(statePath);

                // Create trading workflow
                var tradingCycle = new TradingCycleService(
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




// Create the bot runner
BotRunner runner = new BotRunner(
    tradingCycle,
    botSettings,
    result =>
    {
        Console.WriteLine(
            $"[{result.AtUtc:HH:mm:ss}] " +
            $"Outcome: {result.Outcome} | " +
            $"Reason: {result.Reason}");
    });

// Used to stop the application with Ctrl+C
using var shutdown = new CancellationTokenSource();

ConsoleCancelEventHandler cancelHandler = (sender, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};

Console.CancelKeyPress += cancelHandler;

try
{
    // Reconcile any saved pending order first
    await runner.RecoverAsync(shutdown.Token);

    // Start the trading loop
    await runner.StartAsync(shutdown.Token);

    Console.WriteLine("BinaryTicker is running.");
    Console.WriteLine("Press Ctrl+C to stop.");

    // Remain active until cancellation or loop failure
    await runner.Completion.WaitAsync(shutdown.Token);
}
catch (OperationCanceledException)
    when (shutdown.IsCancellationRequested)
{
    Console.WriteLine("Shutdown requested.");
}
finally
{
    Console.CancelKeyPress -= cancelHandler;

    // Stop and await the trading loop
    await runner.StopAsync();

    Console.WriteLine("BinaryTicker stopped.");
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
