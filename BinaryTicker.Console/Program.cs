using System;
using Alpaca.Markets;
using System.Threading.Tasks;

namespace BinaryTicker
{
    internal static class Program
    {
        public static async Task Main()
        {
            var keyId = Environment.GetEnvironmentVariable("APCA_API_KEY_ID")
                ?? throw new InvalidOperationException("APCA_API_KEY_ID is required.");
            var secretKey = Environment.GetEnvironmentVariable("APCA_API_SECRET_KEY")
                ?? throw new InvalidOperationException("APCA_API_SECRET_KEY is required.");

            var client = Environments.Paper
                .GetAlpacaTradingClient(new SecretKey(keyId, secretKey));

            var clock = await client.GetClockAsync();

            if (clock != null)
            {
                Console.WriteLine(
                    "Timestamp: {0}, NextOpen: {1}, NextClose: {2}",
                    clock.TimestampUtc, clock.NextOpenUtc, clock.NextCloseUtc);
            }
        }
    }
}