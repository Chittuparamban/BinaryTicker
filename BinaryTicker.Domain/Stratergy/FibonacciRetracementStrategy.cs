
using System;
using System.Collections.Generic;
using BinaryTicker.Domain.Fibonacci;

namespace BinaryTicker.Domain.Strategies
{
    public sealed class FibonacciRetracementStrategy : ITradingStrategy
    {
        private SwingDetector Detector { get; }
        private FibonacciCalc Calculator { get; }
        private FibonacciSettings Settings { get; }

        // Temporary demonstration settings
        private bool DemonstrationMode { get; }
        private bool DemoBuyIssued { get; set; }
        private bool DemoSellIssued { get; set; }

        public FibonacciRetracementStrategy(SwingDetector detector, FibonacciCalc calculator,
            FibonacciSettings settings, bool demonstrationMode = false)
        {
            if (detector == null)
            {
                throw new ArgumentNullException(nameof(detector));
            }

            if (calculator == null)
            {
                throw new ArgumentNullException(nameof(calculator));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            Detector = detector;
            Calculator = calculator;
            Settings = settings;

            DemonstrationMode = demonstrationMode;
            DemoBuyIssued = false;
            DemoSellIssued = false;

            Settings.Validate();
        }

        public TradeSignal Evaluate(List<MarketBar> completedBars, decimal heldQuantity, FibonacciLevels? activeSetup)
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
                return new TradeSignal(SignalAction.Hold,"No completed market bars.",activeSetup);
            }

            // TEMP DEMO MODE
            if (DemonstrationMode)
            {
                return EvaluateDemonstration(completedBars, heldQuantity, activeSetup);
            }

            // NORMAL FIBONACCI STRATEGY
            MarketBar latestBar = completedBars[completedBars.Count - 1];

         //  Check Sell conditions
            if (heldQuantity > 0)
            {
                if (activeSetup == null)
                {
                    return new TradeSignal( SignalAction.Hold,"Position exists but its setup is unavailable.",null);
                }

                if (latestBar.Close >= activeSetup.High.Price)
                {
                    return new TradeSignal(SignalAction.Sell,"Price reached the swing high.",activeSetup);
                }

                if (latestBar.Close <= activeSetup.Low.Price)
                {
                    return new TradeSignal(SignalAction.Sell,"Price reached the swing low.",activeSetup);
                }

                
            }

            // detect newwer Fibonacci setup
            FibonacciLevels? newSetup =FindLatestSetup(completedBars);

            if (newSetup != null)
            {
                if (activeSetup == null || newSetup.High.PivotTimeUtc > activeSetup.High.PivotTimeUtc)
                {
                    activeSetup = newSetup;
                }
            }

            if (activeSetup == null)
            {
                return new TradeSignal(SignalAction.Hold,"No confirmed upward swing.", null);
            }

            if (completedBars.Count < 2)
            {
                return new TradeSignal(SignalAction.Hold,"Not enough candles to check an entry.",activeSetup);
            }

               // Calculate Fibonacci retracement zone
            decimal range =  activeSetup.High.Price - activeSetup.Low.Price;

            decimal upperZone =  activeSetup.High.Price - range * Settings.ZoneStartRatio;

            decimal lowerZone = activeSetup.High.Price -range * Settings.ZoneEndRatio;

            MarketBar previousBar = completedBars[completedBars.Count - 2];

            if (previousBar.CloseTimeUtc <activeSetup.High.ConfirmedAtUtc)
            {
                return new TradeSignal( SignalAction.Hold,"Waiting for candles after swing confirmation.",activeSetup);
            }

            // Check Buy condition
            bool enteredZone = previousBar.Close > upperZone &&  latestBar.Close <= upperZone &&
                latestBar.Close >= lowerZone;

            if (enteredZone)
            {
                return new TradeSignal(SignalAction.Buy,"Price entered the Fibonacci retracement zone.",activeSetup);
            }

            // Hold
            return new TradeSignal(SignalAction.Hold,"No trading condition satisfied.", activeSetup);
        }

        // Temporary method used for a single demonstration
        private TradeSignal EvaluateDemonstration(List<MarketBar> completedBars, decimal heldQuantity,
            FibonacciLevels? activeSetup)
        {
            // DEMO SELL:
            // Only try to sell after a demo Buy has been signalled
            if (heldQuantity > 0)
            {
                if (activeSetup == null)
                {
                    return new TradeSignal(SignalAction.Hold,  "DEMO: Existing position has no saved setup.",null);
                }

                if (!DemoBuyIssued || DemoSellIssued)
                {
                    return new TradeSignal(SignalAction.Hold,"DEMO: No Sell signal required.",activeSetup);
                }

                DemoSellIssued = true;

                return new TradeSignal( SignalAction.Sell,"DEMO ONLY: Forced Sell signal.", activeSetup);
            }

            // Do not repeatedly issue Buy signals
            if (DemoBuyIssued)
            {
                return new TradeSignal(SignalAction.Hold,"DEMO: Buy signal already generated.",activeSetup);
            }

            // Restore or detect an actual Fibonacci setup
            FibonacciLevels? newSetup =FindLatestSetup(completedBars);

            if (newSetup != null)
            {
                if (activeSetup == null ||newSetup.High.PivotTimeUtc > activeSetup.High.PivotTimeUtc)
                {
                    activeSetup = newSetup;
                }
            }

            // Keep a valid setup in saved TradeState
            if (activeSetup == null)
            {
                return new TradeSignal(SignalAction.Hold,"DEMO: Waiting for a confirmed Fibonacci setup.", null);
            }

            // DEMO BUY:
            // Ignore the normal retracement-zone entry condition
            DemoBuyIssued = true;

            return new TradeSignal(SignalAction.Buy,"DEMO ONLY: Forced Buy signal.",activeSetup);
        }

        // Find the latest confirmed upward swing
        private FibonacciLevels? FindLatestSetup(List<MarketBar> completedBars)
        {
            List<SwingPoint> points =  Detector.Detect(completedBars, Settings);

            SwingPoint? latestLow = null;
            FibonacciLevels? latestSetup = null;

            foreach (SwingPoint point in points)
            {
                if (point.Type == SwingType.Low)
                {
                    latestLow = point;
                }
                else if (point.Type == SwingType.High &&latestLow != null && point.Price > latestLow.Price)
                {
                    latestSetup =Calculator.Calculate(latestLow, point);
                }
            }

            return latestSetup;
        }
    }
}
