/*
Understand this interface
An interface describes what a service must provide, not how it performs the work.

GetAccountAsync promises to retrieve account information asynchronously.
It returns a TradingAccountSnapshot once the operation completes.

The CancellationToken allows the caller to request cancellation—for example,
when the application is shutting down or the user leaves a screen.

Notice what the interface does not contain: no API key, HTTP endpoint or Alpaca SDK client.
Those details belong to Infrastructure.

The service does not submit orders yet.
We'll establish and test the read-only connection first.

**/
using BinaryTicker.Application.Accounts;

namespace BinaryTicker.Application.Abstractions;

public interface ITradingAccountService
{
    Task<TradingAccountSnapshot> GetAccountAsync(
        CancellationToken cancellationToken = default);
}
