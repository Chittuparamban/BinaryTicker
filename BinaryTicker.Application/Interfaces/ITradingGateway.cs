
using System;
using System.Threading;
using System.Threading.Tasks;
using BinaryTicker.Domain.Order;
using BinaryTicker.Application.Trading;

namespace BinaryTicker.Application.Interfaces
{
    public interface ITradingGateway
    {
        Task<TradingAccountSnapshot> GetAccountAsync(
            CancellationToken cancellationToken = default);

        Task<PositionSnapshot?> GetPositionAsync(
            string symbol,
            CancellationToken cancellationToken = default);

        Task<OrderSnapshot?> GetOrderByClientOrderIdAsync(
            string clientOrderId,
            CancellationToken cancellationToken = default);

        Task<bool> IsMarketOpenAsync(
            CancellationToken cancellationToken = default);

        Task<OrderSnapshot> SubmitOrderAsync(
            OrderIntent intent,
            CancellationToken cancellationToken = default);
    }
}
