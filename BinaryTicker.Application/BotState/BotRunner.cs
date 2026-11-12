
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

        // Keep the failure so the bot cannot restart unsafely
        private Exception? _loopFailure;

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

        // Allows Program.cs to observe execution failures
        public Task Completion => _loopTask ?? Task.CompletedTask;

        // Non-null when the trading loop failed unexpectedly
        public Exception? LastLoopFailure => _loopFailure;

        // STEP 1: Start the bot
        public async Task StartAsync(
            CancellationToken ct = default)
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

        // STEP 2: Stop the bot
        public async Task StopAsync(
            CancellationToken ct = default)
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

        // STEP 3: Recover previously saved execution
        public async Task RecoverAsync(
            CancellationToken ct = default)
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

            // Do not restart after an unexpected failure
            if (_loopFailure is not null)
            {
                throw new InvalidOperationException(
                    "The trading loop previously failed. " +
                    "Resolve the error and restart the application.",
                    _loopFailure);
            }

            // Prevent a second loop
            if (_loopTask is not null && !_loopTask.IsCompleted)
            {
                throw new InvalidOperationException(
                    "Trading loop is already active.");
            }

            _loopCancellation = new CancellationTokenSource();

            // Change lifecycle state
            _state = new RunningState();

            // Start one asynchronous execution loop
            _loopTask = RunLoopAsync(_loopCancellation.Token);

            return Task.CompletedTask;
        }

        // Called by RunningState
        internal async Task StopCoreAsync(CancellationToken ct)
        {
            // Request cancellation
            _loopCancellation?.Cancel();

            Task? runningTask = _loopTask;

            try
            {
                if (runningTask is not null)
                {
                    try
                    {
                        // Wait for current cycle to finish or cancel
                        await runningTask.WaitAsync(ct);
                    }
                    catch (Exception)
                        when (runningTask.IsFaulted &&
                              _loopFailure is not null &&
                              !ct.IsCancellationRequested)
                    {
                        // The original failure remains observable
                        // through Completion and LastLoopFailure.
                        // Do not mask it during cleanup.
                    }
                }
            }
            finally
            {
                // Dispose only after the task is finished
                if (runningTask is null || runningTask.IsCompleted)
                {
                    _loopCancellation?.Dispose();
                    _loopCancellation = null;

                    _state = new StoppedState();
                }
            }
        }

        // Called by StoppedState
        internal async Task RecoverCoreAsync(
            CancellationToken ct)
        {
            TradingCycleResult result =
                await _tradingCycle.ReconcilePendingAsync(ct);

            // Notify observers
            _onCycleCompleted?.Invoke(result);

            // Block startup if recovery is incomplete
            if (result.Outcome == CycleOutcome.Blocked)
            {
                throw new InvalidOperationException(
                    $"Bot recovery blocked: {result.Reason}");
            }
        }

        // STEP 4: Main trading loop
        private async Task RunLoopAsync(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    // Run one trading cycle
                    TradingCycleResult result =
                        await _tradingCycle.RunOnceAsync(ct);

                    // Notify console and file observers
                    _onCycleCompleted?.Invoke(result);

                    // Wait before the next trading cycle
                    await Task.Delay(_settings.PollInterval, ct);
                }
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                // Normal cancellation requested by StopAsync
            }
            catch (Exception ex)
            {
                // Preserve the failure for Program.cs
                _loopFailure = ex;

                // Stop execution and expose the original error
                throw;
            }
        }
    }
}
