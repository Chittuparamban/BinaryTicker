
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Alpaca.Markets;
using BinaryTicker.Application.Interfaces;
using BinaryTicker.Application.Trading;
using BinaryTicker.Domain.Order;

// Prevent conflicts between BinaryTicker and Alpaca enums
using DomainOrderSide = BinaryTicker.Domain.Order.OrderSide;
using AppOrderStatus = BinaryTicker.Application.Trading.OrderStatus;
using SdkOrderSide = Alpaca.Markets.OrderSide;
using SdkOrderStatus = Alpaca.Markets.OrderStatus;

namespace BinaryTicker.Infrastructure
{
    public sealed class AlpacaTradingGateway : ITradingGateway
    {
        private readonly IAlpacaTradingClient _client;
        private readonly AlpacaSettings _settings;

        public AlpacaTradingGateway(
            IAlpacaTradingClient client,
            AlpacaSettings settings)
        {
            _client = client ??
                throw new ArgumentNullException(nameof(client));

            _settings = settings ??
                throw new ArgumentNullException(nameof(settings));

            _settings.ValidatePaperEnvironment();
        }

        // 1. Retrieve account information
        public async Task<TradingAccountSnapshot> GetAccountAsync(
            CancellationToken cancellationToken = default)
        {
            IAccount account = await _client.GetAccountAsync(
                cancellationToken);

            decimal buyingPower = account.BuyingPower ??
                throw new InvalidOperationException(
                    "Alpaca did not return buying power.");

            bool canTrade =
                (account.Status == AccountStatus.Active ||
                 account.Status == AccountStatus.PaperOnly) &&
                !account.IsTradingBlocked &&
                !account.IsAccountBlocked &&
                !account.TradeSuspendedByUser;

            return new TradingAccountSnapshot(
                account.AccountId.ToString(),
                account.TradableCash,
                buyingPower,
                canTrade,
                _settings.IsPaperTrading,
                DateTimeOffset.UtcNow);
        }

        // 2. Retrieve current position
        public async Task<PositionSnapshot?> GetPositionAsync(
            string symbol,
            CancellationToken cancellationToken = default)
        {
            symbol = ValidateSymbol(symbol);

            try
            {
                IPosition position =
                    await _client.GetPositionAsync(
                        symbol,
                        cancellationToken);

                if (position.Side != PositionSide.Long)
                {
                    throw new InvalidOperationException(
                        "BinaryTicker does not support short positions.");
                }

                decimal marketValue = position.MarketValue ??
                    throw new InvalidOperationException(
                        "Position market value is unavailable.");

                return new PositionSnapshot(
                    position.Symbol,
                    position.Quantity,
                    position.AverageEntryPrice,
                    marketValue,
                    DateTimeOffset.UtcNow);
            }
            catch (RestClientErrorException ex)
                when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                // Confirmed absence of position
                return null;
            }
        }

        // 3. Retrieve an order using its client order ID
        public async Task<OrderSnapshot?>
            GetOrderByClientOrderIdAsync(
                string clientOrderId,
                CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(clientOrderId))
            {
                throw new ArgumentException(
                    "Client order ID is required.",
                    nameof(clientOrderId));
            }

            try
            {
                // String overload looks up the CLIENT order ID.
                IOrder order = await _client.GetOrderAsync(
                    clientOrderId,
                    cancellationToken);

                return ConvertOrder(order);
            }
            catch (RestClientErrorException ex)
                when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                // Not found does not prove a prior POST failed.
                return null;
            }
        }

        // 4. Check market opening status
        public async Task<bool> IsMarketOpenAsync(
            CancellationToken cancellationToken = default)
        {
            IClock clock = await _client.GetClockAsync(
                cancellationToken);

            return clock.IsOpen;
        }

        // 5. Check whether the stock is tradable
        public async Task<bool> IsAssetTradableAsync(
            string symbol,
            CancellationToken cancellationToken = default)
        {
            symbol = ValidateSymbol(symbol);

            try
            {
                IAsset asset = await _client.GetAssetAsync(
                    symbol,
                    cancellationToken);

                return asset.IsTradable;
            }
            catch (RestClientErrorException ex)
                when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        // 6. Check whether another order is already open
        public async Task<bool> HasOpenOrdersAsync(
            string symbol,
            CancellationToken cancellationToken = default)
        {
            symbol = ValidateSymbol(symbol);

            ListOrdersRequest request = new ListOrdersRequest
            {
                OrderStatusFilter = OrderStatusFilter.Open
            }.WithSymbol(symbol);

            var orders = await _client.ListOrdersAsync(
                request,
                cancellationToken);

            return orders.Count > 0;
        }

        // 7. Submit a Buy or Sell market order
        public async Task<OrderSnapshot> SubmitOrderAsync(
            OrderIntent intent,
            CancellationToken cancellationToken = default)
        {
            if (intent is null)
            {
                throw new ArgumentNullException(nameof(intent));
            }

            _settings.ValidatePaperEnvironment();

            string symbol = ValidateSymbol(intent.Symbol);

            if (string.IsNullOrWhiteSpace(intent.ClientOrderId))
            {
                throw new ArgumentException(
                    "Client order ID is required.");
            }

            if (intent.Quantity <= 0 ||
                intent.Quantity != decimal.Truncate(intent.Quantity) ||
                intent.Quantity > long.MaxValue)
            {
                throw new ArgumentException(
                    "Quantity must be a supported positive whole number.");
            }

            long quantity = checked((long)intent.Quantity);

            // Convert BinaryTicker's OrderIntent into an SDK order
            MarketOrder request;

            if (intent.Side == DomainOrderSide.Buy)
            {
                request = MarketOrder.Buy(symbol, quantity);
            }
            else if (intent.Side == DomainOrderSide.Sell)
            {
                request = MarketOrder.Sell(symbol, quantity);
            }
            else
            {
                throw new ArgumentException(
                    "Unsupported order side.");
            }

            // Day market order with a stable client order ID
            request = request
                .WithDuration(TimeInForce.Day)
                .WithClientOrderId(intent.ClientOrderId)
                .WithExtendedHours(false);

            // IMPORTANT:
            // Submit exactly once. Do not retry an uncertain POST.
            IOrder submitted = await _client.PostOrderAsync(
                request,
                cancellationToken);

            return ConvertOrder(submitted);
        }

        // 8. Convert Alpaca IOrder into OrderSnapshot
        private static OrderSnapshot ConvertOrder(IOrder order)
        {
            if (order is null)
            {
                throw new ArgumentNullException(nameof(order));
            }

            DomainOrderSide side;

            if (order.OrderSide == SdkOrderSide.Buy)
            {
                side = DomainOrderSide.Buy;
            }
            else if (order.OrderSide == SdkOrderSide.Sell)
            {
                side = DomainOrderSide.Sell;
            }
            else
            {
                throw new InvalidOperationException(
                    "Unknown or missing broker order side.");
            }

            decimal quantity = order.Quantity ??
                throw new InvalidOperationException(
                    "Broker order quantity is unavailable.");

            string symbol = order.Symbol ??
                throw new InvalidOperationException(
                    "Broker order symbol is unavailable.");

            string clientOrderId = order.ClientOrderId ??
                throw new InvalidOperationException(
                    "Broker client order ID is unavailable.");

            DateTime updated = order.UpdatedAtUtc
                ?? order.CreatedAtUtc
                ?? throw new InvalidOperationException(
                    "Broker order timestamp is unavailable.");

            DateTimeOffset updatedUtc = new DateTimeOffset(
                DateTime.SpecifyKind(
                    updated,
                    DateTimeKind.Utc));

            return new OrderSnapshot(
                order.OrderId.ToString(),
                clientOrderId,
                symbol,
                side,
                quantity,
                order.FilledQuantity,
                order.AverageFillPrice,
                ConvertStatus(order.OrderStatus),
                order.OrderStatus.ToString(),
                updatedUtc);
        }

        // 9. Convert Alpaca status to BinaryTicker status
        private static AppOrderStatus ConvertStatus(
            SdkOrderStatus status)
        {
            switch (status)
            {
                case SdkOrderStatus.Accepted:
                    return AppOrderStatus.Accepted;

                case SdkOrderStatus.New:
                    return AppOrderStatus.New;

                case SdkOrderStatus.PendingNew:
                case SdkOrderStatus.PendingCancel:
                case SdkOrderStatus.PendingReplace:
                    return AppOrderStatus.Pending;

                case SdkOrderStatus.PartiallyFilled:
                    return AppOrderStatus.PartiallyFilled;

                case SdkOrderStatus.Filled:
                    return AppOrderStatus.Filled;

                case SdkOrderStatus.Canceled:
                    return AppOrderStatus.Canceled;

                case SdkOrderStatus.Rejected:
                    return AppOrderStatus.Rejected;

                case SdkOrderStatus.Expired:
                    return AppOrderStatus.Expired;

                default:
                    return AppOrderStatus.Unknown;
            }
        }

        // 10. Shared symbol validation
        private static string ValidateSymbol(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException(
                    "Stock symbol is required.",
                    nameof(symbol));
            }

            return symbol.Trim().ToUpperInvariant();
        }
    }
}
