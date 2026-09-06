using CapitaModern.Core.Economy;
using Xunit;

namespace CapitaModern.Core.Tests;

public class CountryTests
{
    [Fact]
    public void TreasuryTakesAndSpends()
    {
        var country = Build.Country(1);

        country.Receive(100);

        Assert.True(country.TrySpend(40));
        Assert.Equal(60, country.Balance);
    }

    [Fact]
    public void SpendingMoreThanThereIsLeavesTreasuryAlone()
    {
        var country = Build.Country(1);

        country.Receive(100);

        Assert.False(country.TrySpend(101));
        Assert.Equal(100, country.Balance);
    }

    [Fact]
    public void SpendingEverythingIsAllowed()
    {
        var country = Build.Country(1);

        country.Receive(100);

        Assert.True(country.TrySpend(100));
        Assert.Equal(0, country.Balance);
    }

    [Fact]
    public void NegativeMoneyIsRejected()
    {
        var country = Build.Country(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => country.Receive(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => country.TrySpend(-1));
    }

    [Fact]
    public void CountryCarriesItsOwnStock()
    {
        var country = Build.Country(1, new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Units(5) });

        Assert.Equal(Build.Units(5), country.Stock.Of(GoodType.Coal));
    }
}
