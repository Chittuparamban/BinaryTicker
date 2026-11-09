
using System;
using Alpaca.Markets;
using System.Threading.Tasks;

namespace BinaryTicker
{
    internal static class Program
    {
        public static async Task Main()
        {
            try
            {
                var keyId = Environment.GetEnvironmentVariable(
                    "ALPACA_API_KEY_ID");

                var secretKey = Environment.GetEnvironmentVariable(
                    "ALPACA_API_SECRET_KEY");

                if (string.IsNullOrWhiteSpace(keyId) ||
                    string.IsNullOrWhiteSpace(secretKey))
                {
                    throw new InvalidOperationException(
                        "Alpaca environment variables are missing or empty.");
                }

                Console.WriteLine("Credentials loaded.");
                Console.WriteLine("Connecting to Alpaca Paper Trading...");

                using var client = Environments.Paper
                    .GetAlpacaTradingClient(
                        new SecretKey(keyId, secretKey));

                var clock = await client.GetClockAsync();

                if (clock != null)
                {
                    Console.WriteLine("Connected successfully!");

                    Console.WriteLine(
                        "Timestamp: {0}, NextOpen: {1}, NextClose: {2}",
                        clock.TimestampUtc,
                        clock.NextOpenUtc,
                        clock.NextCloseUtc);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Alpaca connection failed: {ex.Message}");
            }
        }
    }
}
