/*Adapter pattern.
Handle pagination, sorting, duplicates, timestamps,
incomplete bars and bounded read retries. 
Keep feed and adjustment mode consistent.
Construct the new regular MarketBar class; do not substitute fabricated data on failure.
**/


// This class implements the Adapter design
// retrieving historical stock prices from Alpaca and converting them into MarketBar objects
// two-way conversion is done here 
// Direction A: BinaryTicker → Alpaca SDK when TradingDataService wants historical candles.
// Direction B: Alpaca SDK → BinaryTicker converting the SDK's market bar objects for MarketBar class

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alpaca.Markets;
using Alpaca.Markets.Extensions;
using BinaryTicker.Application.Interfaces;
using BinaryTicker.Domain;

namespace BinaryTicker.Infrastructure
{
    public sealed class AlpacaMarketDataService : IMarketDataService
    {
        // initialise the read-only properties for fields 
        private IAlpacaDataClient Client {get ;}
        private AlpacaSettings Settings {get ;}
        private  TimeProvider TimeProvider{get ;}

        // Conservative delay after candle closing
        private static TimeSpan PublicationDelay
        {
            get
            {
                return TimeSpan.FromMinutes(1);
            }
        }



         // initialises fields with constructor and checks for null exceptions 
        public AlpacaMarketDataService(IAlpacaDataClient client, AlpacaSettings settings, TimeProvider timeProvider)
        {
            
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }
        else
        {
            Client = client;
        }

        if (settings is null)
        {
           throw new ArgumentNullException(nameof(settings));
        }
        else
        {
            Settings = settings;
        }

        if (timeProvider is null)
        {
           throw new ArgumentNullException(nameof(timeProvider));
        }
        else
        {
          TimeProvider = timeProvider;
        }

        Settings.ValidatePaperEnvironment();
        }


        public async Task<List<MarketBar>> GetCompletedBarsAsync(string symbol,string timeframe,
            DateTimeOffset from, DateTimeOffset to, CancellationToken cancelToken = default)
        {
            // Validate request inputs
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException( "Symbol cannot be empty.", nameof(symbol));

            if (from >= to)
                throw new ArgumentException("From time must be before To time.");

            symbol = symbol.Trim().ToUpper();

            // Convert BinaryTicker timeframe into SDK type
           TimeFrameInfo timeFrameInfo = GetTimeFrame(timeframe);
           BarTimeFrame barTimeFrame =  timeFrameInfo.AlpacaTimeFrame;
           TimeSpan duration = timeFrameInfo.Duration;

    // gets current UTC time
           DateTimeOffset now = TimeProvider.GetUtcNow();

         // Avoid requesting future data
           DateTimeOffset requestEnd;

            if (to < now)
            {
                requestEnd = to;
            }
            else
            {
                requestEnd = now;
            }

            if (requestEnd <= from)
                return new List<MarketBar>();

            // Construct Alpaca historical request
            HistoricalBarsRequest request = new HistoricalBarsRequest(symbol, from.UtcDateTime,
             requestEnd.UtcDateTime, barTimeFrame);

             request.Feed = GetFeed(Settings.DataFeed);
             request.Adjustment = GetAdjustment(Settings.AdjustmentPolicy);



            // Stores unique candles by starting time
            var barsByTime = new Dictionary<DateTimeOffset, MarketBar>();

            // retrieve historical bars from Alpaca
            // The extension handles API pagination
            await foreach (var bar in Client.GetHistoricalBarsAsAsyncEnumerable(request).WithCancellation(cancelToken))
            {
                cancelToken.ThrowIfCancellationRequested();

                DateTimeOffset openTime = new DateTimeOffset(DateTime.SpecifyKind(bar.TimeUtc, DateTimeKind.Utc));

                DateTimeOffset closeTime = openTime.Add(duration);

                // Exclude bars outside the range
                if (openTime < from || openTime >= to)
                    continue;

                // Exclude incomplete or recently closed bars
                if (closeTime.Add(PublicationDelay) > now)
                    continue;

                // Convert Alpaca IBar into Domain MarketBar
                MarketBar marketBar = new MarketBar(symbol: symbol, openTime: openTime, closeTime: closeTime,
                    open: bar.Open, high: bar.High,  low: bar.Low,close: bar.Close, volume: bar.Volume);

                // Deduplicate by candle opening timestamp
                barsByTime[openTime] = marketBar;
            }

            // return sorted, completed candles as list
            return barsByTime.Values.OrderBy(bar => bar.OpenTimeUtc).ToList();
        }

        private static TimeFrameInfo GetTimeFrame(string timeframe)
        {
            switch (timeframe)
            {
                case "1Min":
                return new TimeFrameInfo(BarTimeFrame.Minute,TimeSpan.FromMinutes(1));
                
                case "5Min":
                return new TimeFrameInfo(new BarTimeFrame(5, BarTimeFrameUnit.Minute), TimeSpan.FromMinutes(5));

                case "15Min":
                return new TimeFrameInfo(new BarTimeFrame(15, BarTimeFrameUnit.Minute),
                TimeSpan.FromMinutes(15));

                case "1Hour":
                return new TimeFrameInfo(BarTimeFrame.Hour,TimeSpan.FromHours(1));

                default:
                throw new ArgumentException("Invalid timeframe.",nameof(timeframe));
    }
}

        private static MarketDataFeed GetFeed(string feed)
        {
            switch(feed)
            {
                case "iex":
                return  MarketDataFeed.Iex;

                case "sip":
                return MarketDataFeed.Sip;

                default:
                throw new ArgumentException("Unsupported market data feed.");
            }
        }

        private static Adjustment GetAdjustment(string policy)
        {
            switch(policy)
            {
                case "raw":
                return Adjustment.Nothing;

                case "split":
                return  Adjustment.SplitsOnly;

                case "dividend":
                return  Adjustment.DividendsOnly;

                case "all": 
                return Adjustment.SplitsAndDividends;

                default:
                 throw new NotSupportedException("Adjustment policy is not supported in this adapter.");
            }
        }
    }
}
