
namespace BinaryTicker.Domain.Order
{
    public abstract class OrderIntentCreator
    {
        public abstract OrderIntent Create(
            string symbol,
            decimal quantity,
            string clientOrderId);
    }
}

