
namespace BinaryTicker.Domain.Order
{
    public sealed class SellOrderCreator : OrderIntentCreator
    {
        public override OrderIntent Create(string symbol,decimal quantity, string clientOrderId)
        {
            return new OrderIntent(symbol, OrderSide.Sell, quantity,clientOrderId);
        }
    }
}

