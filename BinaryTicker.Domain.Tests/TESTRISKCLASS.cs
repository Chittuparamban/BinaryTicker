
using NUnit.Framework;
using BinaryTicker.Domain.Order;
using BinaryTicker.Domain.Risk;

namespace BinaryTicker.Domain.Tests
{
    [TestFixture]
    public class RiskManagerTests
    {
        [Test]
        public void ValidBuyOrder_ShouldBeAllowed()
        {
            RiskLimits limits = new RiskLimits(
                1000m, 10m, 5000m, 0.02m);

            OrderIntent intent = new OrderIntent(
                "AAPL",
                OrderSide.Buy,
                3m,
                "test-order-001");

            RiskManager riskManager = new RiskManager();

            var result = riskManager.Validate(
                intent: intent,
                limits: limits,
                marketPrice: 200m,
                availableBuyingPower: 10000m,
                heldQuantity: 0m,
                currentPositionValue: 0m,
                reservedSellQuantity: 0m,
                canTrade: true,
                isMarketOpen: true
            );

            Assert.That(result.Allowed, Is.True);
            Assert.That(result.Reason,
                Is.EqualTo("Buy order passed risk validation."));
        }
    }
}
