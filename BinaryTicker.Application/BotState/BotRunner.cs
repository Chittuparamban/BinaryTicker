
using System;
using System.Threading;
using System.Threading.Tasks;
using BinaryTicker.Application.BotState;

namespace BinaryTicker.Application.Trading
{
    public sealed class BotRunner
    {
        // Private properties
        private TradingCycleService TradingCycle { get; }
        private BotSettings Settings { get; }
        private Action<TradingCycleResult>? OnCycleCompleted { get; }

        private SemaphoreSlim StateLock { get; }
        private IBotState State { get; set; }

        private CancellationTokenSource? LoopCancellation { get; set; }
        private Task? LoopTask { get; set; }
        private Exception? LoopFailure { get; set; }

        public BotRunner(TradingCycleService tradingCycle, BotSettings settings,
            Action<TradingCycleResult>? onCycleCompleted = null)
        {
            if (tradingCycle == null)
            {
                throw new ArgumentNullException(nameof(tradingCycle));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            TradingCycle = tradingCycle;
            Settings = settings;
            OnCycleCompleted = onCycleCompleted;

            Settings.Validate();

            // Allow only one state operation at a time
            StateLock = new SemaphoreSlim(1, 1);

            // Bot starts in the Stopped state
            State = new StoppedState();

            LoopCancellation = null;
            LoopTask = null;
            LoopFailure = null;
        }

        // Return the current bot state
        public string StateName
        {
            get
            {
                return State.Name;
            }
        }

        // Allow Program.cs to monitor the trading loop
        public Task Completion
        {
            get
            {
                if (LoopTask == null)
                {
                    return Task.CompletedTask;
                }

                return LoopTask;
            }
        }

        // Return any unexpected execution failure
        public Exception? LastLoopFailure
        {
            get
            {
                return LoopFailure;
            }
        }

        // STEP 1: Start the bot
        public async Task StartAsync(CancellationToken ct = default)
        {
            await StateLock.WaitAsync(ct);

            try
            {
                await State.StartAsync(this, ct);
            }
            finally
            {
                StateLock.Release();
            }
        }

        // STEP 2: Stop the bot
        public async Task StopAsync(CancellationToken ct = default)
        {
            await StateLock.WaitAsync(ct);

            try
            {
                await State.StopAsync(this, ct);
            }
            finally
            {
                StateLock.Release();
            }
        }

        // STEP 3: Recover saved trading execution
        public async Task RecoverAsync(CancellationToken ct = default)
        {
            await StateLock.WaitAsync(ct);

            try
            {
                await State.RecoverAsync(this, ct);
            }
            finally
            {
                StateLock.Release();
            }
        }

        // Called by StoppedState to begin trading
        internal Task StartCoreAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // Prevent restarting after an unexpected failure
            if (LoopFailure != null)
            {
                throw new InvalidOperationException("The trading loop previously failed. " +
                    "Resolve the error and restart the application.",LoopFailure);
            }

            // Prevent multiple trading loops
            if (LoopTask != null && !LoopTask.IsCompleted)
            {
                throw new InvalidOperationException("Trading loop is already active.");
            }

            // Create a cancellation source for the loop
            LoopCancellation = new CancellationTokenSource();

            // Change state to Running
            State = new RunningState();

            // Begin asynchronous execution
            LoopTask = RunLoopAsync(LoopCancellation.Token);

            return Task.CompletedTask;
        }

        // Called by RunningState to stop trading
        internal async Task StopCoreAsync(CancellationToken ct)
        {
            // Request cancellation
            if (LoopCancellation != null)
            {
                LoopCancellation.Cancel();
            }

            Task? runningTask = LoopTask;

            try
            {
                if (runningTask != null)
                {
                    try
                    {
                        // Wait for the trading loop to finish
                        await runningTask.WaitAsync(ct);
                    }
                    catch (Exception)
                        when (runningTask.IsFaulted &&
                              LoopFailure != null &&
                              !ct.IsCancellationRequested)
                    {
                        // Preserve the original loop failure
                        // so Program.cs can inspect it.
                    }
                }
            }
            finally
            {
                // Only clean up after the loop has finished
                if (runningTask == null || runningTask.IsCompleted)
                {
                    if (LoopCancellation != null)
                    {
                        LoopCancellation.Dispose();
                    }

                    LoopCancellation = null;

                    // Return to Stopped state
                    State = new StoppedState();
                }
            }
        }

        // Called by StoppedState during startup recovery
        internal async Task RecoverCoreAsync(CancellationToken ct)
        {
            TradingCycleResult result =await TradingCycle.ReconcilePendingAsync(ct);

            // Notify registered observers
            if (OnCycleCompleted != null)
            {
                OnCycleCompleted(result);
            }

            // Do not start if recovery is incomplete
            if (result.Outcome == CycleOutcome.Blocked)
            {
                throw new InvalidOperationException("Bot recovery blocked: " + result.Reason);
            }
        }

        // STEP 4: Execute the automatic trading loop
        private async Task RunLoopAsync(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    // Check whether cancellation was requested
                    ct.ThrowIfCancellationRequested();

                    // Execute one trading cycle
                    TradingCycleResult result = await TradingCycle.RunOnceAsync(ct);

                    // Notify observers of the result
                    if (OnCycleCompleted != null)
                    {
                        OnCycleCompleted(result);
                    }

                    // Wait before checking the market again
                    await Task.Delay(Settings.PollInterval, ct);
                }
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                // Normal cancellation when stopping the bot
            }
            catch (Exception ex)
            {
                // Save unexpected errors
                LoopFailure = ex;                
                throw;
            }
        }
    }
}
