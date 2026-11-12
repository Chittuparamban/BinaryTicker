
namespace BinaryTicker.Domain.Order
{
    public sealed class BuyOrderCreator : OrderIntentCreator
    {
        public override OrderIntent Create(string symbol, decimal quantity,string clientOrderId)
        {
            return new OrderIntent(symbol, OrderSide.Buy, quantity, clientOrderId);
        }
    }
}
