/*Property	Meaning
Equity	Total value of the account's equity, as reported by Alpaca.
Cash	Cash balance reported by the account.
BuyingPower	Buying power currently reported by the account.
Status	Account status returned by the integration.
IsTradingBlocked	Whether the account reports that trading is blocked.
Why is this in Application instead of Infrastructure?

Alpaca returns its own SDK-specific account object.
We don't want our Avalonia interface and application workflow to depend on that object directly.
Infrastructure will translate Alpaca's response into this model.

The record declaration gives us a convenient way to represent data, while sealed prevents other classes from inheriting from this particular type.
**/
namespace BinaryTicker.Application.Accounts;

public sealed record TradingAccountSnapshot(
    decimal Equity,
    decimal Cash,
    decimal BuyingPower,
    string Status,
    bool IsTradingBlocked);