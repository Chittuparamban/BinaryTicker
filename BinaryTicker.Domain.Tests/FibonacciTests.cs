
using System;
using System.Collections.Generic;
using NUnit.Framework;
using BinaryTicker.Domain;
using BinaryTicker.Domain.Fibonacci;

namespace BinaryTicker.Domain.Tests
{
    [TestFixture]
    public class FibonacciTests
    {
        private static readonly DateTimeOffset Start =
            new DateTimeOffset(
                2026, 11, 9, 14, 0, 0, TimeSpan.Zero);

        [Test]
        public void Calculator_ReturnsCorrectLevels()
        {
            SwingPoint low = new SwingPoint(
                SwingType.Low,
                100m,
                Start,
                Start.AddMinutes(3));

            SwingPoint high = new SwingPoint(
                SwingType.High,
                200m,
                Start.AddMinutes(4),
                Start.AddMinutes(7));

            FibonacciCalc calculator =
                new FibonacciCalc();

            FibonacciLevels levels =
                calculator.Calculate(low, high);

            Assert.That(levels.Prices[0.236m], Is.EqualTo(176.4m));
            Assert.That(levels.Prices[0.382m], Is.EqualTo(161.8m));
            Assert.That(levels.Prices[0.5m], Is.EqualTo(150m));
            Assert.That(levels.Prices[0.618m], Is.EqualTo(138.2m));
            Assert.That(levels.Prices[0.786m], Is.EqualTo(121.4m));
        }

        [Test]
        public void Detector_FindsConfirmedLowAndHigh()
        {
            List<MarketBar> bars = new List<MarketBar>
            {
                CreateBar(0, 9m, 12m),
                CreateBar(1, 6m, 10m),
                CreateBar(2, 8m, 13m),
                CreateBar(3, 11m, 17m),
                CreateBar(4, 9m, 14m)
            };

            FibonacciSettings settings =
                new FibonacciSettings(1, 1, 0.382m, 0.618m);

            SwingDetector detector = new SwingDetector();

            List<SwingPoint> points =
                detector.Detect(bars, settings);

            Assert.That(points, Has.Count.EqualTo(2));

            Assert.That(points[0].Type, Is.EqualTo(SwingType.Low));
            Assert.That(points[0].Price, Is.EqualTo(6m));
            Assert.That(points[0].ConfirmedAtUtc,
                Is.EqualTo(Start.AddMinutes(3)));

            Assert.That(points[1].Type, Is.EqualTo(SwingType.High));
            Assert.That(points[1].Price, Is.EqualTo(17m));
            Assert.That(points[1].ConfirmedAtUtc,
                Is.EqualTo(Start.AddMinutes(5)));
        }

        [Test]
        public void Detector_RejectsTiedPivots()
        {
            List<MarketBar> bars = new List<MarketBar>
            {
                CreateBar(0, 6m, 11m),
                CreateBar(1, 5m, 15m),
                CreateBar(2, 5m, 15m),
                CreateBar(3, 6m, 10m)
            };

            FibonacciSettings settings =
                new FibonacciSettings(1, 1, 0.382m, 0.618m);

            SwingDetector detector = new SwingDetector();

            List<SwingPoint> points =
                detector.Detect(bars, settings);

            Assert.That(points, Is.Empty);
        }

        [Test]
        public void Calculator_RejectsInvalidSwingOrder()
        {
            SwingPoint low = new SwingPoint(
                SwingType.Low,
                100m,
                Start.AddMinutes(5),
                Start.AddMinutes(7));

            SwingPoint high = new SwingPoint(
                SwingType.High,
                200m,
                Start,
                Start.AddMinutes(2));

            FibonacciCalc calculator =
                new FibonacciCalc();

            Assert.Throws<ArgumentException>(() =>
                calculator.Calculate(low, high));
        }

        private static MarketBar CreateBar(
            int minute, decimal low, decimal high)
        {
            DateTimeOffset openTime =
                Start.AddMinutes(minute);

            decimal middle = (low + high) / 2m;

            return new MarketBar(
                "AAPL",
                openTime,
                openTime.AddMinutes(1),
                middle,
                high,
                low,
                middle,
                1000m);
        }
    }
}
