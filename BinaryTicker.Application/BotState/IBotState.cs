
using System.Threading;
using System.Threading.Tasks;
using BinaryTicker.Application.Trading;

namespace BinaryTicker.Application.BotState
{
    public interface IBotState
    {
        string Name { get; }

        Task StartAsync(
            BotRunner runner,
            CancellationToken ct);

        Task StopAsync(
            BotRunner runner,
            CancellationToken ct);

        Task RecoverAsync(
            BotRunner runner,
            CancellationToken ct);
    }
}
