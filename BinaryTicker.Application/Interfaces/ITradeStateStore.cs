
using System.Threading;
using System.Threading.Tasks;
using BinaryTicker.Application.Trading;

namespace BinaryTicker.Application.Interfaces
{
    public interface ITradeStateStore
    {
        Task<TradeState?> LoadAsync(
            string accountId,
            string symbol,
            string timeframe,
            string strategyVersion,
            CancellationToken cancellationToken = default);

        Task SaveAsync(
            TradeState state,
            CancellationToken cancellationToken = default);
    }
}