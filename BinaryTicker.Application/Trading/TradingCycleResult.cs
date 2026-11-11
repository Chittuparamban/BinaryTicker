
using System;
using BinaryTicker.Domain.Strategies;

namespace BinaryTicker.Application.Trading
{
    public enum CycleOutcome
    {
        NoNewBar,
        Hold,
        Blocked,
        Preview,
        Reconciled,
        Submitted
    }

    public sealed class TradingCycleResult
    {
        public CycleOutcome Outcome { get; }
        public string Reason { get; }
        public DateTimeOffset AtUtc { get; }
        public TradeSignal? Signal { get; }
        public OrderSnapshot? Order { get; }

        public TradingCycleResult(
            CycleOutcome outcome,
            string reason,
            DateTimeOffset atUtc,
            TradeSignal? signal = null,
            OrderSnapshot? order = null)
        {
            Outcome = outcome;
            Reason = reason;
            AtUtc = atUtc;
            Signal = signal;
            Order = order;
        }
    }
}
