
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
        private readonly string _filePath;
        private readonly FileStream _ownershipLock;
        private readonly SemaphoreSlim _gate = new(1, 1);

        private readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public JsonStateStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException(
                    "State file path is required.");

            _filePath = Path.GetFullPath(filePath);

            string directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);

            // Prevent two cooperating processes from
            // owning the same state file.
            _ownershipLock = new FileStream(
                _filePath + ".lock",
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }

        public async Task<TradeState?> LoadAsync(string accountId,string symbol,string timeframe,
            string strategyVersion,CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);

            try
            {
                if (!File.Exists(_filePath))
                    return null;

                string json = await File.ReadAllTextAsync(_filePath, cancellationToken);

                TradeState state =JsonSerializer.Deserialize<TradeState>(json, _options)
                    ?? throw new InvalidDataException(
                        "Saved trading state is empty.");

                // Never load another account or strategy's state.
                if (state.AccountId != accountId ||
                    state.Symbol != symbol.Trim().ToUpperInvariant() ||
                    state.Timeframe != timeframe ||
                    state.StrategyVersion != strategyVersion)
                {
                    throw new InvalidDataException(
                        "Saved state does not match this bot.");
                }

                return state;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task SaveAsync(
            TradeState state,
            CancellationToken cancellationToken = default)
        {
            if (state is null)
                throw new ArgumentNullException(nameof(state));

            await _gate.WaitAsync(cancellationToken);

            string temporaryPath =
                _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                await using (FileStream stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(
                        stream, state, _options, cancellationToken);

                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Replace the state only after the new file is written.
                File.Move(temporaryPath, _filePath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);

                _gate.Release();
            }
        }

        public void Dispose()
        {
            _ownershipLock.Dispose();
            _gate.Dispose();
        }
    }
}
