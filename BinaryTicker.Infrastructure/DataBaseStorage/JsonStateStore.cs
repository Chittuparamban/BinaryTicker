
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using BinaryTicker.Application.Interfaces;
using BinaryTicker.Application.Trading;

namespace BinaryTicker.Infrastructure.DatabaseStorage
{
    public sealed class JsonStateStore : ITradeStateStore, IDisposable
    {
        private string Filepath { get; }

        private SemaphoreSlim UpdateGate { get; }
        private JsonSerializerOptions Options { get; }

        public JsonStateStore(string filepath)
        {

            
            Filepath = Path.GetFullPath(filepath);

            //Create the directory 
            string directory = Path.GetDirectoryName(Filepath)!;

            Directory.CreateDirectory(directory);

            // Initialize the semaphore
            UpdateGate = new SemaphoreSlim(1, 1);

            // STEP 5: Initialize JSON options
            Options = new JsonSerializerOptions();

            Options.WriteIndented = true;

            Options.Converters.Add(new JsonStringEnumConverter());
        }


        // Load the saved trading state
        public async Task<TradeState?> LoadAsync(
            string accountId,
            string symbol,
            string timeframe,
            string strategyVersion,
            CancellationToken cancelToken = default)
        {
            // Prevent simultaneous access
            await UpdateGate.WaitAsync(cancelToken);

            try
            {
                // Return null if no saved file exists
                if (!File.Exists(Filepath))
                {
                    return null;
                }

                // Read JSON from the file
                string json = await File.ReadAllTextAsync(Filepath,cancelToken);

                // Convert JSON into a TradeState object
                TradeState? state = JsonSerializer.Deserialize<TradeState>(json,Options);

                if (state == null)
                {
                    throw new InvalidDataException("Saved trading state is empty.");
                }

                // Ensure the state belongs to this bot
                if (state.AccountId != accountId || state.Symbol != symbol.Trim().ToUpperInvariant() ||
                    state.Timeframe != timeframe ||state.StrategyVersion != strategyVersion)
                {
                    throw new InvalidDataException( "Saved state does not match this bot.");
                }

                return state;
            }
            finally
            {
                // Release the semaphore
                UpdateGate.Release();
            }
        }

        // Save the latest trading state
        public async Task SaveAsync(TradeState state,CancellationToken cancelToken = default)
        {
            // STEP 1: Validate the state
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            // STEP 2: Wait for exclusive access
            await UpdateGate.WaitAsync(cancelToken);

            // STEP 3: Create a unique temporary file path
            string temporaryPath = Filepath + "." + Guid.NewGuid().ToString("N") +".tmp";

            try
            {
                // STEP 4: Write the state to a temporary file
                await using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream,state,Options,cancelToken);

                    // Flush buffered data
                    await stream.FlushAsync(cancelToken);

                    // Request a flush to disk
                    stream.Flush(flushToDisk: true);
                }

                // STEP 5: Check cancellation before replacing
                cancelToken.ThrowIfCancellationRequested();

                // STEP 6: Replace the original state file
                File.Move(temporaryPath, Filepath,true);
            }
            finally
            {
                // Delete the temporary file if it still exists
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                // Release the semaphore
                UpdateGate.Release();
            }
        }

        // Release resources
        public void Dispose()
        {
            // Release the semaphore
            UpdateGate.Dispose();
        }
    }
}
