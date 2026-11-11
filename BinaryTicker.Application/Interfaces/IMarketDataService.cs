using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using BinaryTicker.Domain;

namespace BinaryTicker.Application.Interfaces
{
    public interface IMarketDataService
{
    public Task<List<MarketBar>> GetCompletedBarsAsync( string symbol,
            string timeframe,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancelToken = default);
   // this interface depends on MarketBar.cs class, not on Alpaca.     

}
}
