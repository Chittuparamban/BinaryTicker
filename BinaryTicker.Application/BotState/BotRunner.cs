
using System;
using System.Threading;
using System.Threading.Tasks;
using BinaryTicker.Application.BotState;

namespace BinaryTicker.Application.Trading
{
    public sealed class BotRunner
    {
        private readonly TradingCycleService _tradingCycle;
        private readonly BotSettings _settings;
        private readonly Action<TradingCycleResult>? _onCycleCompleted;

        private readonly SemaphoreSlim _stateLock = new(1, 1);

        private IBotState _state = new StoppedState();

        private CancellationTokenSource? _loopCancellation;
        private Task? _loopTask;

        public BotRunner(
            TradingCycleService tradingCycle,
            BotSettings settings,
            Action<TradingCycleResult>? onCycleCompleted = null)
        {
            _tradingCycle = tradingCycle ??
                throw new ArgumentNullException(nameof(tradingCycle));

            _settings = settings ??
                throw new ArgumentNullException(nameof(settings));

            _settings.Validate();
            _onCycleCompleted = onCycleCompleted;
        }

        public string StateName => _state.Name;

        // Allows Program.cs to observe loop failures
        public Task Completion => _loopTask ?? Task.CompletedTask;

        // Start the bot
        public async Task StartAsync(CancellationToken ct = default)
        {
            await _stateLock.WaitAsync(ct);

            try
            {
                await _state.StartAsync(this, ct);
            }
            finally
            {
                _stateLock.Release();
            }
        }

        // Stop the bot
        public async Task StopAsync(CancellationToken ct = default)
        {
            await _stateLock.WaitAsync(ct);

            try
            {
                await _state.StopAsync(this, ct);
            }
            finally
            {
                _stateLock.Release();
            }
        }

        // Recover saved execution while stopped
        public async Task RecoverAsync(CancellationToken ct = default)
        {
            await _stateLock.WaitAsync(ct);

            try
            {
                await _state.RecoverAsync(this, ct);
            }
            finally
            {
                _stateLock.Release();
            }
        }

        // Called by StoppedState
        internal Task StartCoreAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            _loopCancellation = new CancellationTokenSource();

            _state = new RunningState();

            // Starts the asynchronous trading loop
            _loopTask = RunLoopAsync(_loopCancellation.Token);

            return Task.CompletedTask;
        }

        // Called by RunningState
        internal async Task StopCoreAsync(CancellationToken ct)
        {
            // Request cancellation
            _loopCancellation?.Cancel();

            try
            {
                // Wait for the running cycle to finish
                if (_loopTask is not null)
                {
                    await _loopTask.WaitAsync(ct);
                }
            }
            finally
            {
                // Do not dispose resources while the loop
                // is still executing.
                if (_loopTask is null || _loopTask.IsCompleted)
                {
                    _loopCancellation?.Dispose();
                    _loopCancellation = null;

                    _state = new StoppedState();
                }
            }
        }

        // Called by StoppedState
internal async Task RecoverCoreAsync(CancellationToken ct)
{
    TradingCycleResult result =
        await _tradingCycle.ReconcilePendingAsync(ct);

    _onCycleCompleted?.Invoke(result);

    if (result.Outcome == CycleOutcome.Blocked)
    {
        throw new InvalidOperationException(
            $"Bot recovery blocked: {result.Reason}");
    }
}

        // Main asynchronous execution loop
        private async Task RunLoopAsync(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    // Execute one trading cycle
                    TradingCycleResult result =
                        await _tradingCycle.RunOnceAsync(ct);

                    // Notify the configured observer
                    _onCycleCompleted?.Invoke(result);

                    // Wait before running another cycle
                    await Task.Delay(_settings.PollInterval, ct);
                }
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                // Normal shutdown requested by StopAsync
            }
        }
    }
}
