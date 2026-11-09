
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

                AlpacaSettings settings = new AlpacaSettings(keyId, secretKey, "iex", "raw", true, TimeSpan.FromSeconds(30));

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

                Console.WriteLine("Connected successfully!");

                Console.WriteLine("Timestamp: {0}, NextOpen: {1}, NextClose: {2}",
                        clock.TimestampUtc,clock.NextOpenUtc,clock.NextCloseUtc);
                
                // successful completion
                return 0;

            }

            catch (Exception ex)
            {
                Console.WriteLine($"Error occured. Connection failed due to: \n{ex.Message}");
                return 1;
            
            }
        }
    }
}
