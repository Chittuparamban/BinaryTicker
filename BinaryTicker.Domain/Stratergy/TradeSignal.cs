
using System;
using BinaryTicker.Domain.Fibonacci;

namespace BinaryTicker.Domain.Strategies
{
    public sealed class TradeSignal
    {
        public SignalAction Action { get; }
        public string Reason { get; }
        public FibonacciLevels? ActiveSetup { get; }

        public TradeSignal(SignalAction action,string reason,FibonacciLevels? activeSetup)
        {
            if (!Enum.IsDefined(action))
                throw new ArgumentException("Invalid trading action.");

            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A reason is required.");

            if (action != SignalAction.Hold && activeSetup is null)
                throw new ArgumentException("A trading signal requires an active setup.");

            Action = action;
            Reason = reason;
            ActiveSetup = activeSetup;
        }
    }
}
