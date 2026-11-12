
using System;
using System.Collections.Generic;
using BinaryTicker.Domain.Fibonacci;

namespace BinaryTicker.Domain.Strategies
{
    public sealed class FibonacciRetracementStrategy : ITradingStrategy
    {
        // Private properties
        private SwingDetector Detector { get; }
        private FibonacciCalc Calculator { get; }
        private FibonacciSettings Settings { get; }

        public FibonacciRetracementStrategy(  SwingDetector detector, FibonacciCalc calculator,
            FibonacciSettings settings)
        {
            // Validate 
            if (detector == null)
            {
                throw new ArgumentNullException(nameof(detector));
            }

            Detector = detector;

          
            if (calculator == null)
            {
                throw new ArgumentNullException(nameof(calculator));
            }

            Calculator = calculator;

            
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            Settings = settings;

            Settings.Validate();
        }

        public TradeSignal Evaluate(List<MarketBar> completedBars, decimal heldQuantity,
            FibonacciLevels? activeSetup)
        {
          
            if (completedBars == null)
            {
                throw new ArgumentNullException(nameof(completedBars));
            }

            if (heldQuantity < 0)
            {
                throw new ArgumentException("Held quantity cannot be negative.");
            }

            if (completedBars.Count == 0)
            {
                return new TradeSignal(SignalAction.Hold,"No completed market bars.", activeSetup);
            }

            MarketBar latestBar = completedBars[completedBars.Count - 1];

           
            if (heldQuantity > 0)
            {
                if (activeSetup == null)
                {
                    return new TradeSignal( SignalAction.Hold, "Position exists but its setup is unavailable.",null);
                }

                if (latestBar.Close >= activeSetup.High.Price)
                {
                    return new TradeSignal(SignalAction.Sell,"Price reached the swing high.",activeSetup);
                }

                if (latestBar.Close <= activeSetup.Low.Price)
                {
                    return new TradeSignal(SignalAction.Sell,"Price reached the swing low.",activeSetup);
                }

                return new TradeSignal(SignalAction.Hold, "Position remains open.",activeSetup);
            }

            // STEP 2: Find the latest confirmed upward swing
            FibonacciLevels? newSetup = FindLatestSetup(completedBars);

            // Update setup only when a newer one exists
            if (newSetup != null)
            {
                if (activeSetup == null || newSetup.High.PivotTimeUtc > activeSetup.High.PivotTimeUtc)
                {
                    activeSetup = newSetup;
                }
            }

            if (activeSetup == null)
            {
                return new TradeSignal( SignalAction.Hold,"No confirmed upward swing.",null);
            }

            if (completedBars.Count < 2)
            {
                return new TradeSignal(SignalAction.Hold, "Not enough candles to check an entry.",activeSetup);
            }

            // STEP 3: Calculate retracement zone
            decimal range = activeSetup.High.Price - activeSetup.Low.Price;

            decimal upperZone = activeSetup.High.Price - range * Settings.ZoneStartRatio;

            decimal lowerZone =activeSetup.High.Price - range * Settings.ZoneEndRatio;

            MarketBar previousBar = completedBars[completedBars.Count - 2];

            // Wait until the swing high is confirmed
            if (previousBar.CloseTimeUtc < activeSetup.High.ConfirmedAtUtc)
            {
                return new TradeSignal(SignalAction.Hold,"Waiting for candles after swing confirmation.",activeSetup);
            }

            // STEP 4: Check for Buy signal
            bool enteredZone = previousBar.Close > upperZone && latestBar.Close <= upperZone &&
                latestBar.Close >= lowerZone;

            if (enteredZone)
            {
                return new TradeSignal(SignalAction.Buy,"Price entered the Fibonacci retracement zone.", activeSetup);
            }

            // STEP 5: Otherwise Hold
            return new TradeSignal(SignalAction.Hold,"No trading condition satisfied.", activeSetup);
        }

        // Find the most recent confirmed upward swing
        private FibonacciLevels? FindLatestSetup(List<MarketBar> completedBars)
        {
            List<SwingPoint> points = Detector.Detect(completedBars, Settings);

            SwingPoint? latestLow = null;
            FibonacciLevels? latestSetup = null;

            foreach (SwingPoint point in points)
            {
                if (point.Type == SwingType.Low)
                {
                    latestLow = point;
                }
                else if (point.Type == SwingType.High &&
                         latestLow != null &&
                         point.Price > latestLow.Price)
                {
                    latestSetup =
                        Calculator.Calculate(latestLow, point);
                }
            }

            return latestSetup;
        }
    }
}
