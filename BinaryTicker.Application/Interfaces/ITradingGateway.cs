
using System.Threading;
using System.Threading.Tasks;
using BinaryTicker.Application.Trading;
using BinaryTicker.Domain.Order;

namespace BinaryTicker.Application.Interfaces
{
    public interface ITradingGateway
    {
        // Retrieve paper account information
        Task<TradingAccountSnapshot> GetAccountAsync( CancellationToken cancelToken = default);

        // Retrieve current stock position
        Task<PositionSnapshot?> GetPositionAsync(string symbol,CancellationToken cancelToken = default);

        // Retrieve an order using BinaryTicker's client order ID
        Task<OrderSnapshot?> GetOrderByClientOrderIdAsync( string clientOrderId,CancellationToken cancelToken = default);

        // Check whether the stock market is open
        Task<bool> IsMarketOpenAsync(CancellationToken cancelToken = default);

        // Check whether Alpaca permits trading this stock
        Task<bool> IsAssetTradableAsync(string symbol,CancellationToken cancelToken = default);

        // Check for existing open broker orders
        Task<bool> HasOpenOrdersAsync(string symbol,CancellationToken cancelToken = default);

        // Submit a Buy or Sell order
        Task<OrderSnapshot> SubmitOrderAsync(OrderIntent intent,CancellationToken cancelToken = default);
    }
}
