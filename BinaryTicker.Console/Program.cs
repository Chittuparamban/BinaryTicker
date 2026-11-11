
using System;
using Alpaca.Markets;
using System.Threading.Tasks;
using BinaryTicker.Infrastructure;

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

                AlpacaSettings settings = new AlpacaSettings(keyId, secretKey, "iex", "raw", TimeSpan.FromSeconds(30), true);

                Console.WriteLine("Credentials loaded.");
                Console.WriteLine("Connecting to Alpaca Paper Trading...");

                // validating before connecting
                settings.ValidatePaperEnvironment();

                // var is used to automatically assign the type of variable implicitly
                // connection process
                using var client = Environments.Paper.GetAlpacaTradingClient(new SecretKey(settings.KeyId, settings.SecretKey));

                var clock = await client.GetClockAsync();

                if (clock is null)
                {
                    throw new InvalidOperationException("Alpaca returned an empty clock response.");
                }

                Console.WriteLine("Connected successfully clock is shown");

                Console.WriteLine("Timestamp: {0}, NextOpen: {1}, NextClose: {2}",
                        clock.TimestampUtc,clock.NextOpenUtc,clock.NextCloseUtc);
            



                AlpacaTradingGateway gateway = new AlpacaTradingGateway(client, settings);

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