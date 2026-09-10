using CapitaModern.Core.Economy;
using Xunit;

namespace CapitaModern.Core.Tests;

public class CountryTests
{
    private static Money Cash(long thousands) => Money.FromWhole(thousands);

    [Fact]
    public void TreasuryTakesAndSpends()
    {
        var country = Build.Country(1);

        country.State.Treasury.Receive(Cash(100));

        Assert.True(country.State.Treasury.TrySpend(Cash(40)));
        Assert.Equal(Cash(60), country.State.Treasury.Balance);
    }

    [Fact]
    public void SpendingMoreThanThereIsLeavesTreasuryAlone()
    {
        var country = Build.Country(1);

        country.State.Treasury.Receive(Cash(100));

        Assert.False(country.State.Treasury.TrySpend(Cash(101)));
        Assert.Equal(Cash(100), country.State.Treasury.Balance);
    }

    [Fact]
    public void SpendingEverythingIsAllowed()
    {
        var country = Build.Country(1);

        country.State.Treasury.Receive(Cash(100));

        Assert.True(country.State.Treasury.TrySpend(Cash(100)));
        Assert.Equal(default, country.State.Treasury.Balance);
    }

    [Fact]
    public void NegativeMoneyIsRejected()
    {
        var country = Build.Country(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => country.State.Treasury.Receive(Cash(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => country.State.Treasury.TrySpend(Cash(-1)));
    }

    [Fact]
    public void CountryCarriesItsOwnStock()
    {
        var country = Build.Country(1, new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Whole(5) });

        Assert.Equal(Build.Whole(5), country.State.Stock.Of(GoodType.Coal));
    }
}
