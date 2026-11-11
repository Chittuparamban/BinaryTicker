
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Alpaca.Markets;
using BinaryTicker.Application.Interfaces;
using BinaryTicker.Application.Trading;
using BinaryTicker.Domain.Order;

// Avoid collisions with BinaryTicker's enums
using DomainOrderSide = BinaryTicker.Domain.Order.OrderSide;
using AppOrderStatus = BinaryTicker.Application.Trading.OrderStatus;
using SdkOrderSide = Alpaca.Markets.OrderSide;
using SdkOrderStatus = Alpaca.Markets.OrderStatus;

namespace BinaryTicker.Infrastructure
{
    public sealed class AlpacaTradingGateway : ITradingGateway
    {
        private IAlpacaTradingClient Client { get; }
        private AlpacaSettings Settings { get; }

        public AlpacaTradingGateway(
            IAlpacaTradingClient client,
            AlpacaSettings settings)
        {
            Client = client ??
                throw new ArgumentNullException(nameof(client));

            Settings = settings ??
                throw new ArgumentNullException(nameof(settings));

            Settings.ValidatePaperEnvironment();
        }

        // 1. Retrieve and convert account information
        public async Task<TradingAccountSnapshot> GetAccountAsync(
            CancellationToken cancellationToken = default)
        {
            IAccount account =
                await Client.GetAccountAsync(cancellationToken);

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
                Settings.IsPaperTrading,
                DateTimeOffset.UtcNow);
        }

        // 2. Retrieve the position for one symbol
        public async Task<PositionSnapshot?> GetPositionAsync(
            string symbol,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException(
                    "Symbol is required.", nameof(symbol));

            symbol = symbol.Trim().ToUpperInvariant();

            try
            {
                IPosition position =
                    await Client.GetPositionAsync(
                        symbol, cancellationToken);

                if (position.Side != PositionSide.Long)
                    throw new InvalidOperationException(
                        "BinaryTicker does not support short positions.");

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
                // Broker confirmed no position was found
                return null;
            }
        }

        // 3. Find an existing order by client order ID
        public async Task<OrderSnapshot?> GetOrderByClientOrderIdAsync(
            string clientOrderId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(clientOrderId))
                throw new ArgumentException(
                    "Client order ID is required.");

            try
            {
                IOrder order = await Client.GetOrderAsync(
                    clientOrderId, cancellationToken);

                return ConvertOrder(order);
            }
            catch (RestClientErrorException ex)
                when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        // 4. Check the market clock
        public async Task<bool> IsMarketOpenAsync(
            CancellationToken cancellationToken = default)
        {
            IClock clock =
                await Client.GetClockAsync(cancellationToken);

            return clock.IsOpen;
        }

        // 5. Submit an order to Alpaca Paper Trading
        public async Task<OrderSnapshot> SubmitOrderAsync(
            OrderIntent intent,
            CancellationToken cancellationToken = default)
        {
            if (intent is null)
                throw new ArgumentNullException(nameof(intent));

            // Defence-in-depth checks
            TradingAccountSnapshot account =
                await GetAccountAsync(cancellationToken);

            if (!account.CanTrade || !account.IsPaper)
                throw new InvalidOperationException(
                    "Paper account is not eligible to trade.");

            if (!await IsMarketOpenAsync(cancellationToken))
                throw new InvalidOperationException(
                    "Regular market is currently closed.");

            // Convert BinaryTicker order into Alpaca order
            MarketOrder request;

            if (intent.Side == DomainOrderSide.Buy)
            {
                request = MarketOrder.Buy(
                    intent.Symbol, (long)intent.Quantity);
            }
            else if (intent.Side == DomainOrderSide.Sell)
            {
                request = MarketOrder.Sell(
                    intent.Symbol, (long)intent.Quantity);
            }
            else
            {
                throw new ArgumentException(
                    "Unsupported order side.");
            }

            request.Duration = TimeInForce.Day;
            request.ClientOrderId = intent.ClientOrderId;
            request.ExtendedHours = false;

            // Actual broker operation
            IOrder submitted = await Client.PostOrderAsync(
                request, cancellationToken);

            // Convert SDK response into BinaryTicker snapshot
            return ConvertOrder(submitted);
        }

        // 6. Convert Alpaca's IOrder into OrderSnapshot
        private static OrderSnapshot ConvertOrder(IOrder order)
        {
            DomainOrderSide side;

            if (order.OrderSide == SdkOrderSide.Buy)
                side = DomainOrderSide.Buy;
            else if (order.OrderSide == SdkOrderSide.Sell)
                side = DomainOrderSide.Sell;
            else
                throw new InvalidOperationException(
                    "Unknown broker order side.");

            decimal quantity = order.Quantity ??
                throw new InvalidOperationException(
                    "Broker order quantity is unavailable.");

            string symbol = order.Symbol ??
                throw new InvalidOperationException(
                    "Broker order symbol is unavailable.");

            string clientOrderId = order.ClientOrderId ??
                throw new InvalidOperationException(
                    "Broker order client ID is unavailable.");

            DateTime updated = order.UpdatedAtUtc
                ?? order.CreatedAtUtc
                ?? throw new InvalidOperationException(
                    "Broker order timestamp is unavailable.");

            DateTimeOffset updatedUtc = new DateTimeOffset(
                DateTime.SpecifyKind(updated, DateTimeKind.Utc));

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

        // 7. Convert SDK order status into BinaryTicker status
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
    }
}
