
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
        // Private properties
        private IAlpacaTradingClient Client { get; }
        private AlpacaSettings Settings { get; }

        public AlpacaTradingGateway(IAlpacaTradingClient client, AlpacaSettings settings)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            Client = client;
            Settings = settings;

            Settings.ValidatePaperEnvironment();
        }

        // gets account information
        public async Task<TradingAccountSnapshot> GetAccountAsync( CancellationToken cancelToken = default)
        {
            IAccount account =
                await Client.GetAccountAsync(cancelToken);

            if (account.BuyingPower == null)
            {
                throw new InvalidOperationException("Alpaca did not return buying power.");
            }

            decimal buyingPower = account.BuyingPower.Value;

            bool canTrade = (account.Status == AccountStatus.Active || account.Status == AccountStatus.PaperOnly) &&
                !account.IsTradingBlocked && !account.IsAccountBlocked &&
                !account.TradeSuspendedByUser;

            return new TradingAccountSnapshot(account.AccountId.ToString(), account.TradableCash, buyingPower,
                canTrade, Settings.IsPaperTrading, DateTimeOffset.UtcNow);
        }

        // get current position
        public async Task<PositionSnapshot?> GetPositionAsync(string symbol, CancellationToken cancelToken = default)
        {
            symbol = ValidateSymbol(symbol);

            try
            {
                IPosition position = await Client.GetPositionAsync(symbol, cancelToken);

                if (position.Side != PositionSide.Long)
                {
                    throw new InvalidOperationException("BinaryTicker does not support short positions.");
                }

                if (position.MarketValue == null)
                {
                    throw new InvalidOperationException("Position market value is unavailable.");
                }

                decimal marketValue = position.MarketValue.Value;

                return new PositionSnapshot(position.Symbol, position.Quantity, position.AverageEntryPrice,
                    marketValue, DateTimeOffset.UtcNow);
            }
            catch (RestClientErrorException ex)
                when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                // Position does not exist
                return null;
            }
        }

        // Retrieve an order using its client order ID
        public async Task<OrderSnapshot?> GetOrderByClientOrderIdAsync(string clientOrderId, CancellationToken cancelToken = default)
        {
            if (string.IsNullOrWhiteSpace(clientOrderId))
            {
                throw new ArgumentException("Client order ID is required.",nameof(clientOrderId));
            }

            try
            {
                IOrder order = await Client.GetOrderAsync(clientOrderId, cancelToken);

                return ConvertOrder(order);
            }
            catch (RestClientErrorException ex)
                when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                // Not found does not prove an earlier POST failed
                return null;
            }
        }

        // Check whether the market is open
        public async Task<bool> IsMarketOpenAsync( CancellationToken cancellationToken = default)
        {
            IClock clock =
                await Client.GetClockAsync(cancellationToken);

            return clock.IsOpen;
        }

        // Check whether the stock is tradable
        public async Task<bool> IsAssetTradableAsync(string symbol, CancellationToken cancellationToken = default)
        {
            symbol = ValidateSymbol(symbol);

            try
            {
                IAsset asset = await Client.GetAssetAsync(symbol, cancellationToken);

                return asset.IsTradable;
            }
            catch (RestClientErrorException ex)
                when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        // check whether another order is already open
        public async Task<bool> HasOpenOrdersAsync(string symbol, CancellationToken cancellationToken = default)
        {
            symbol = ValidateSymbol(symbol);

            ListOrdersRequest request = new ListOrdersRequest();

            request.OrderStatusFilter = OrderStatusFilter.Open;
            request = request.WithSymbol(symbol);

            var orders = await Client.ListOrdersAsync(request, cancellationToken);

            return orders.Count > 0;
        }

        // submits a Buy or Sell market order
        public async Task<OrderSnapshot> SubmitOrderAsync(OrderIntent intent, CancellationToken cancellationToken = default)
        {
            if (intent == null)
            {
                throw new ArgumentNullException(nameof(intent));
            }

            // Only allow paper trading
            Settings.ValidatePaperEnvironment();

            string symbol = ValidateSymbol(intent.Symbol);

            if (string.IsNullOrWhiteSpace(intent.ClientOrderId))
            {
                throw new ArgumentException(
                    "Client order ID is required.");
            }

            if (intent.Quantity <= 0 || intent.Quantity != decimal.Truncate(intent.Quantity) ||
                intent.Quantity > long.MaxValue)
            {
                throw new ArgumentException("Quantity must be a supported positive whole number.");
            }

            long quantity = checked((long)intent.Quantity);

            // Convert BinaryTicker order to Alpaca order
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
                throw new ArgumentException("Unsupported order side.");
            }

            // Configure a DAY market order
            request = request.WithDuration(TimeInForce.Day);

            // Attach the unique client order ID
            request = request.WithClientOrderId(intent.ClientOrderId);

            // Normal market orders do not use extended hours
            request = request.WithExtendedHours(false);

            // Submit exactly once to Alpaca Paper
            // Do not automatically retry an uncertain submission
            IOrder submitted = await Client.PostOrderAsync( request, cancellationToken);

            return ConvertOrder(submitted);
        }

        // Converts an Alpaca order into a BinaryTicker OrderSnapshot
        private static OrderSnapshot ConvertOrder(IOrder order)
        {
            if (order == null)
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
                throw new InvalidOperationException("Unknown or missing broker order side.");
            }

            if (order.Quantity == null)
            {
                throw new InvalidOperationException("Broker order quantity is unavailable.");
            }

            decimal quantity = order.Quantity.Value;

            if (order.Symbol == null)
            {
                throw new InvalidOperationException("Broker order symbol is unavailable.");
            }

            string symbol = order.Symbol;

            if (order.ClientOrderId == null)
            {
                throw new InvalidOperationException("Broker client order ID is unavailable.");
            }

            string clientOrderId = order.ClientOrderId;

            DateTime updated;

            if (order.UpdatedAtUtc != null)
            {
                updated = order.UpdatedAtUtc.Value;
            }
            else if (order.CreatedAtUtc != null)
            {
                updated = order.CreatedAtUtc.Value;
            }
            else
            {
                throw new InvalidOperationException("Broker order timestamp is unavailable.");
            }

            DateTime utcDateTime = DateTime.SpecifyKind(updated, DateTimeKind.Utc);

            DateTimeOffset updatedUtc = new DateTimeOffset(utcDateTime);

            return new OrderSnapshot(order.OrderId.ToString(), clientOrderId, symbol,
                side, quantity, order.FilledQuantity,
                order.AverageFillPrice, ConvertStatus(order.OrderStatus),
                order.OrderStatus.ToString(), updatedUtc);
        }

        // 9. Convert Alpaca status to BinaryTicker status
        private static AppOrderStatus ConvertStatus(SdkOrderStatus status)
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

        // 10. Validate and normalize the stock symbol
        private static string ValidateSymbol(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException("Stock symbol is required.",nameof(symbol));
            }

            return symbol.Trim().ToUpperInvariant();
        }
    }
}
