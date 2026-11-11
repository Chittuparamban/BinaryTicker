
using System.Collections.Generic;
using BinaryTicker.Domain.Fibonacci;

namespace BinaryTicker.Domain.Strategies
{
    public interface ITradingStrategy
    {
        TradeSignal Evaluate(
            List<MarketBar> completedBars,
            decimal heldQuantity,
            FibonacciLevels? activeSetup);
    }
}
