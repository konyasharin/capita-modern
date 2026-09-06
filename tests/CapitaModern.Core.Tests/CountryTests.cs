using CapitaModern.Core.Economy;
using Xunit;

namespace CapitaModern.Core.Tests;

public class CountryTests
{
    [Fact]
    public void TreasuryTakesAndSpends()
    {
        var country = Build.Country(1);

        country.Treasury.Receive(100);

        Assert.True(country.Treasury.TrySpend(40));
        Assert.Equal(60, country.Treasury.Balance);
    }

    [Fact]
    public void SpendingMoreThanThereIsLeavesTreasuryAlone()
    {
        var country = Build.Country(1);

        country.Treasury.Receive(100);

        Assert.False(country.Treasury.TrySpend(101));
        Assert.Equal(100, country.Treasury.Balance);
    }

    [Fact]
    public void SpendingEverythingIsAllowed()
    {
        var country = Build.Country(1);

        country.Treasury.Receive(100);

        Assert.True(country.Treasury.TrySpend(100));
        Assert.Equal(0, country.Treasury.Balance);
    }

    [Fact]
    public void NegativeMoneyIsRejected()
    {
        var country = Build.Country(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => country.Treasury.Receive(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => country.Treasury.TrySpend(-1));
    }

    [Fact]
    public void CountryCarriesItsOwnStock()
    {
        var country = Build.Country(1, new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Whole(5) });

        Assert.Equal(Build.Whole(5), country.Stock.Of(GoodType.Coal));
    }
}
