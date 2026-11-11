
using NUnit.Framework;
using BinaryTicker.Domain.Order;

namespace BinaryTicker.Domain.Tests
{
    [TestFixture]
    public class OrderCreatorTests
    {
        [Test]
        public void BuyCreator_CreatesBuyOrder()
        {
            OrderIntentCreator creator = new BuyOrderCreator();

            OrderIntent order = creator.Create(
                "AAPL",
                3m,
                "test-buy-001");

            Assert.That(order.Side, Is.EqualTo(OrderSide.Buy));
            Assert.That(order.Symbol, Is.EqualTo("AAPL"));
            Assert.That(order.Quantity, Is.EqualTo(3m));
            Assert.That(order.ClientOrderId,
                Is.EqualTo("test-buy-001"));
        }

        [Test]
        public void SellCreator_CreatesSellOrder()
        {
            OrderIntentCreator creator = new SellOrderCreator();

            OrderIntent order = creator.Create(
                "AAPL",
                2m,
                "test-sell-001");

            Assert.That(order.Side, Is.EqualTo(OrderSide.Sell));
            Assert.That(order.Quantity, Is.EqualTo(2m));
        }

        [Test]
        public void BuyCreator_InvalidQuantity_ThrowsException()
        {
            OrderIntentCreator creator = new BuyOrderCreator();

            Assert.Throws<System.ArgumentException>(() =>
                creator.Create("AAPL", -1m, "test-invalid"));
        }
    }
}
