
using System;
using System.Threading;
using System.Threading.Tasks;
using BinaryTicker.Application.Trading;

namespace BinaryTicker.Application.BotState
{
    public sealed class RunningState : IBotState
    {
        public string Name => "Running";

        public Task StartAsync(
            BotRunner runner,
            CancellationToken ct)
        {
            // Prevent a second trading loop
            return Task.CompletedTask;
        }

        public Task StopAsync(
            BotRunner runner,
            CancellationToken ct)
        {
            return runner.StopCoreAsync(ct);
        }

        public Task RecoverAsync(
            BotRunner runner,
            CancellationToken ct)
        {
            throw new InvalidOperationException(
                "Cannot recover while the bot is running.");
        }
    }
}
