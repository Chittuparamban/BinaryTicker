
using System;
using System.Collections.Generic;
using BinaryTicker.Domain.Fibonacci;

namespace BinaryTicker.Domain.Strategies
{
    public sealed class FibonacciRetracementStrategy : ITradingStrategy
    {
        private readonly SwingDetector _detector;
        private readonly FibonacciCalc _calculator;
        private readonly FibonacciSettings _settings;

        public FibonacciRetracementStrategy(
            SwingDetector detector,
            FibonacciCalc calculator,
            FibonacciSettings settings)
        {
            _detector = detector ??
                throw new ArgumentNullException(nameof(detector));

            _calculator = calculator ??
                throw new ArgumentNullException(nameof(calculator));

            _settings = settings ??
                throw new ArgumentNullException(nameof(settings));

            _settings.Validate();
        }

        public TradeSignal Evaluate(
            List<MarketBar> completedBars,
            decimal heldQuantity,
            FibonacciLevels? activeSetup)
        {
            // Validate inputs
            if (completedBars is null)
                throw new ArgumentNullException(nameof(completedBars));

            if (heldQuantity < 0)
                throw new ArgumentException(
                    "Held quantity cannot be negative.");

            if (completedBars.Count == 0)
                return new TradeSignal(
                    SignalAction.Hold,
                    "No completed market bars.",
                    activeSetup);

            MarketBar latestBar = completedBars[^1];

            // STEP 1: Check for a possible exit
            if (heldQuantity > 0)
            {
                if (activeSetup is null)
                    return new TradeSignal(
                        SignalAction.Hold,
                        "Position exists but its setup is unavailable.",
                        null);

                if (latestBar.Close >= activeSetup.High.Price)
                    return new TradeSignal(
                        SignalAction.Sell,
                        "Price reached the swing high.",
                        activeSetup);

                if (latestBar.Close <= activeSetup.Low.Price)
                    return new TradeSignal(
                        SignalAction.Sell,
                        "Price reached the swing low.",
                        activeSetup);

                return new TradeSignal(
                    SignalAction.Hold,
                    "Position remains open.",
                    activeSetup);
            }

            // STEP 2: Find the latest confirmed upward swing
            FibonacciLevels? newSetup =
                FindLatestSetup(completedBars);

            // Update the setup only when a newer one exists
            if (newSetup is not null)
            {
                if (activeSetup is null ||
                    newSetup.High.PivotTimeUtc >
                    activeSetup.High.PivotTimeUtc)
                {
                    activeSetup = newSetup;
                }
            }

            if (activeSetup is null)
                return new TradeSignal(
                    SignalAction.Hold,
                    "No confirmed upward swing.",
                    null);

            if (completedBars.Count < 2)
                return new TradeSignal(
                    SignalAction.Hold,
                    "Not enough candles to check an entry.",
                    activeSetup);

            // STEP 3: Calculate the retracement zone
            decimal range =
                activeSetup.High.Price - activeSetup.Low.Price;

            decimal upperZone =
                activeSetup.High.Price -
                range * _settings.ZoneStartRatio;

            decimal lowerZone =
                activeSetup.High.Price -
                range * _settings.ZoneEndRatio;

            MarketBar previousBar = completedBars[^2];

            // Wait until the high has been confirmed
            if (previousBar.CloseTimeUtc <
                activeSetup.High.ConfirmedAtUtc)
            {
                return new TradeSignal(
                    SignalAction.Hold,
                    "Waiting for candles after swing confirmation.",
                    activeSetup);
            }

            // STEP 4: Check for a Buy signal
            bool enteredZone =
                previousBar.Close > upperZone &&
                latestBar.Close <= upperZone &&
                latestBar.Close >= lowerZone;

            if (enteredZone)
            {
                return new TradeSignal(
                    SignalAction.Buy,
                    "Price entered the Fibonacci retracement zone.",
                    activeSetup);
            }

            // STEP 5: Otherwise Hold
            return new TradeSignal(
                SignalAction.Hold,
                "No trading condition satisfied.",
                activeSetup);
        }

        // Find the most recent confirmed upward swing
        private FibonacciLevels? FindLatestSetup(
            List<MarketBar> completedBars)
        {
            List<SwingPoint> points =
                _detector.Detect(completedBars, _settings);

            SwingPoint? latestLow = null;
            FibonacciLevels? latestSetup = null;

            foreach (SwingPoint point in points)
            {
                if (point.Type == SwingType.Low)
                {
                    latestLow = point;
                }
                else if (point.Type == SwingType.High &&
                         latestLow is not null &&
                         point.Price > latestLow.Price)
                {
                    latestSetup =
                        _calculator.Calculate(latestLow, point);
                }
            }

            return latestSetup;
        }
    }
}
