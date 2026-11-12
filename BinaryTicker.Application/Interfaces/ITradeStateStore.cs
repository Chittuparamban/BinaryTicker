
using System.Threading;
using System.Threading.Tasks;
using BinaryTicker.Application.Trading;

namespace BinaryTicker.Application.Interfaces
{
    public interface ITradeStateStore
    {
        Task<TradeState?> LoadAsync(string accId,string symbol,string timeframe,
            string strategyVer, CancellationToken cancelToken = default);

        Task SaveAsync(TradeState state, CancellationToken cancelToken = default);
    }
}