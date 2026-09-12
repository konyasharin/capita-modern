using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Компания: долг, расширение ниши и разорение.</summary>
public class CompanyTests
{
    private static Company Made(Money cash = default) =>
        new(1, 1, "проба", [Sector.Mining], cash);

    [Fact]
    public void UnpaidInterestGrowsTheDebt()
    {
        var company = Made(Money.FromWhole(10));
        company.Borrow(Money.FromWhole(100));

        Assert.Equal(Money.FromWhole(110), company.Cash);
        Assert.Equal(Money.FromWhole(100), company.Debt);

        company.Capitalise(Money.FromWhole(5));

        Assert.Equal(Money.FromWhole(105), company.Debt);
    }

    /// <summary>Гасит сколько может, а не сколько просят.</summary>
    [Fact]
    public void RepaysOnlyWhatItHas()
    {
        var company = Made();
        company.Borrow(Money.FromWhole(100));
        company.TrySpend(Money.FromWhole(70));

        Assert.Equal(Money.FromWhole(30), company.Repay(Money.FromWhole(50)));
        Assert.Equal(Money.FromWhole(70), company.Debt);
    }

    [Fact]
    public void BrokenCompanyKeepsNothing()
    {
        var company = Made(Money.FromWhole(10));
        company.Borrow(Money.FromWhole(100));
        company.Break(day: 42);

        Assert.False(company.Alive);
        Assert.Equal(42, company.BrokeOnDay);
        Assert.Equal(default, company.Debt);
        Assert.Equal(default, company.Cash);
    }

    [Fact]
    public void ExpandsIntoNewSectorOnce()
    {
        var company = Made();

        company.Expand(Sector.Heavy);
        company.Expand(Sector.Heavy);

        Assert.Equal(2, company.Focus.Count);
        Assert.True(company.Works(Sector.Heavy));
    }

    /// <summary>Проданное уходит со счёта продавца целиком, а не наполовину.</summary>
    [Fact]
    public void RemoveTakesNoMoreThanThereIs()
    {
        var company = Made();
        company.Add(region: 3, BuildingType.CoalMine, 5);

        Assert.Equal(5, company.Remove(3, BuildingType.CoalMine, 9));
        Assert.Equal(0, company.Size);
        Assert.Empty(company.Buildings);
    }
}
