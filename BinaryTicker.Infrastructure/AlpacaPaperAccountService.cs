namespace BinaryTicker.Infrastructure;

public class Class1
{

}
/*
By sending a GET request to our /v2/account endpoint,
you can see various information about your account,
such as the amount of buying power available

using Alpaca.Markets;
using System;

var client = Environments.Paper.GetAlpacaTradingClient(
    new SecretKey("YOUR_API_KEY", "YOUR_API_SECRET"));

var account = await client.GetAccountAsync();
if (account.IsTradingBlocked)
{
    Console.WriteLine("Account is currently restricted from trading.");
}

Console.WriteLine($"{account.BuyingPower} is available as buying power.");
**/

/*
View Gain/Loss of Portfolio
You can use the information from the account endpoint to
do things like calculating the daily profit or loss of your account.


from alpaca.trading.client import TradingClient
from alpaca.trading.requests import GetAssetsRequest

trading_client = TradingClient('api-key', 'secret-key')

# Get our account information.
account = trading_client.get_account()

# Check our current balance vs. our balance at the last market close
balance_change = float(account.equity) - float(account.last_equity)
print(f'Today\'s portfolio balance change: ${balance_change}')

**/