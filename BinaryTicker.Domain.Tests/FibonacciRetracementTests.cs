
using System;
using System.Collections.Generic;
using NUnit.Framework;
using BinaryTicker.Domain;
using BinaryTicker.Domain.Fibonacci;
using BinaryTicker.Domain.Strategies;

namespace BinaryTicker.Domain.Tests
{
    [TestFixture]
    public class FibonacciStrategyTests
    {
        private static readonly DateTimeOffset Start =
            new DateTimeOffset(
                2026, 11, 9, 14, 0, 0, TimeSpan.Zero);

        private static FibonacciRetracementStrategy
            CreateStrategy()
        {
            FibonacciSettings settings =
                new FibonacciSettings(
                    1, 1, 0.382m, 0.618m);

            return new FibonacciRetracementStrategy(
                new SwingDetector(),
                new FibonacciCalc(),
                settings);
        }

        [Test]
        public void PriceEntersZone_GeneratesBuy()
        {
            List<MarketBar> bars = new List<MarketBar>
            {
                Bar(0, 120m, 140m, 130m),
                Bar(1, 110m, 135m, 120m),
                Bar(2, 100m, 130m, 110m),
                Bar(3, 120m, 150m, 140m),
                Bar(4, 140m, 180m, 170m),
                Bar(5, 160m, 200m, 190m),
                Bar(6, 150m, 190m, 170m),
                Bar(7, 148m, 180m, 165m),
                Bar(8, 145m, 175m, 170m),
                Bar(9, 140m, 165m, 155m)
            };

            var strategy = CreateStrategy();

            TradeSignal signal =
                strategy.Evaluate(bars, 0m, null);

            Assert.That(signal.Action,
                Is.EqualTo(SignalAction.Buy));

            Assert.That(signal.ActiveSetup, Is.Not.Null);
        }

        [Test]
        public void PriceReachesSwingHigh_GeneratesSell()
        {
            FibonacciLevels setup = CreateSetup();

            List<MarketBar> bars = new List<MarketBar>
            {
                Bar(10, 180m, 205m, 195m),
                Bar(11, 190m, 210m, 205m)
            };

            var strategy = CreateStrategy();

            TradeSignal signal =
                strategy.Evaluate(bars, 2m, setup);

            Assert.That(signal.Action,
                Is.EqualTo(SignalAction.Sell));
        }

        [Test]
        public void NoNewEntry_PreservesSetupOnHold()
        {
            FibonacciLevels setup = CreateSetup();

            List<MarketBar> bars = new List<MarketBar>
            {
                Bar(10, 140m, 170m, 157m),
                Bar(11, 140m, 165m, 155m)
            };

            var strategy = CreateStrategy();

            TradeSignal signal =
                strategy.Evaluate(bars, 0m, setup);

            Assert.That(signal.Action,
                Is.EqualTo(SignalAction.Hold));

            Assert.That(signal.ActiveSetup,
                Is.SameAs(setup));
        }

        private static FibonacciLevels CreateSetup()
        {
            SwingPoint low = new SwingPoint(
                SwingType.Low,
                100m,
                Start,
                Start.AddMinutes(2));

            SwingPoint high = new SwingPoint(
                SwingType.High,
                200m,
                Start.AddMinutes(5),
                Start.AddMinutes(7));

            return new FibonacciCalc()
                .Calculate(low, high);
        }

        private static MarketBar Bar(
            int minute,
            decimal low,
            decimal high,
            decimal close)
        {
            DateTimeOffset openTime =
                Start.AddMinutes(minute);

            return new MarketBar(
                "AAPL",
                openTime,
                openTime.AddMinutes(1),
                close,
                high,
                low,
                close,
                1000m);
        }
    }
}
