
using System.Threading;
using System.Threading.Tasks;
using BinaryTicker.Application.Trading;

namespace BinaryTicker.Application.BotState
{
    public sealed class StoppedState : IBotState
    {
        public string Name => "Stopped";

        public Task StartAsync(
            BotRunner runner,
            CancellationToken ct)
        {
            return runner.StartCoreAsync(ct);
        }

        public Task StopAsync(
            BotRunner runner,
            CancellationToken ct)
        {
            // Already stopped
            return Task.CompletedTask;
        }

        public Task RecoverAsync(
            BotRunner runner,
            CancellationToken ct)
        {
            return runner.RecoverCoreAsync(ct);
        }
    }
}
