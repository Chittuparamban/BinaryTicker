using System;

namespace BinaryTicker.Domain
{
    public sealed class MarketBar
    {
        //Get-only properties:
       public string Symbol{ get;}
       public DateTimeOffset OpenTimeUtc {get;}
       public DateTimeOffset CloseTimeUtc{get; }
       public decimal Open{get; }
       public decimal High{get; }
       public decimal Low{get; }
       public decimal Close{get; }
       public decimal Volume{get; }

        public MarketBar( string symbol, DateTimeOffset openTime, DateTimeOffset closeTime,
         decimal open,decimal high,decimal low,decimal close, decimal volume )
        {

            // Validate symbol
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Stock symbol cannot be empty.");

            // Validate timestamp order
            if (closeTime <= openTime)
                throw new ArgumentException("Close time must be after open time.");

            // Validate prices
            // the logic being Low <= Open, Close <= High
            if (open <= 0 || high <= 0 ||
                low <= 0 || close <= 0)
                throw new ArgumentException("OHLC prices must be positive.");

            // Validate OHLC boundaries
            if (high < low || open > high || open < low ||
                close > high || close < low)
                throw new ArgumentException("Invalid OHLC price boundaries.");

            // Validate volume
            if (volume < 0)
                throw new ArgumentOutOfRangeException("Volume cannot be negative.");


            Symbol = symbol;
            OpenTimeUtc = openTime;
            CloseTimeUtc = closeTime;
            Open = open;
            High = high;
            Low = low;
            Close =close;
            Volume =volume;
        }  


    }
}