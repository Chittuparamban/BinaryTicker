
using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using BinaryTicker.Application.Trading;
using BinaryTicker.Domain.Order;
using BinaryTicker.Infrastructure.DatabaseStorage;

namespace BinaryTicker.Infrastructure.Tests
{
    [TestFixture]
    public class JsonStateStoreTests
    {
        [Test]
        public async Task SaveAndLoad_PreservesPendingOrder()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "BinaryTickerTests",
                Guid.NewGuid().ToString("N"));

            string path = Path.Combine(directory, "state.json");

            try
            {
                TradeState state = TradeState.CreateNew(
                    "test-account",
                    "AAPL",
                    "1Min",
                    "fib-v1");

                OrderIntent order = new OrderIntent(
                    "AAPL",
                    OrderSide.Buy,
                    2m,
                    "test-order-001");

                state = state.WithPendingOrder(order);

                // First process: save
                using (var store = new JsonStateStore(path))
                {
                    await store.SaveAsync(state);
                }

                // Simulate restart: create another store
                using (var store = new JsonStateStore(path))
                {
                    TradeState? restored = await store.LoadAsync(
                        "test-account",
                        "AAPL",
                        "1Min",
                        "fib-v1");

                    Assert.That(restored, Is.Not.Null);

                    Assert.That(
                        restored!.PendingOrder, Is.Not.Null);

                    Assert.That(
                        restored.PendingOrder!.ClientOrderId,
                        Is.EqualTo("test-order-001"));

                    Assert.That(
                        restored.PendingOrder.Quantity,
                        Is.EqualTo(2m));
                }
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }
    }
}
