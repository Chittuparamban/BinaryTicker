/*
AlpacaMarketDataService.cs
Retrieves Alpaca bars and maps them into
MarketBar objects.
• sealed class implementing
IMarketDataService.
• GetCompletedBarsAsync(params…..)
• Returns Task<List<MarketBar>>.
• Constructor: configured data client, AlpacaSettings, TimeProvider.

Adapter pattern. Handle pagination,
sorting, duplicates, timestamps,
incomplete bars and all other “alien
code”. Construct the new
MarketBar.cs class.

>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>


Retrieves Alpaca bars and maps them into MarketBar objects.
Regular sealed class implementing IMarketDataService.
GetCompletedBarsAsync(string symbol, string timeframe, DateTimeOffset fromUtc,
 DateTimeOffset toUtc, CancellationToken cancellationToken)
→ Task<IReadOnlyList<MarketBar>>.
Constructor dependencies: configured data client, AlpacaSettings, TimeProvider.

Adapter pattern.
Handle pagination, sorting, duplicates, timestamps,
incomplete bars and bounded read retries. 
Keep feed and adjustment mode consistent.
Construct the new regular MarketBar class; do not substitute fabricated data on failure.
**/


// This class implements the Adapter design
// retrieving historical stock prices from Alpaca and converting them into MarketBar objects

