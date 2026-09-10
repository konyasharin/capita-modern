using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Общий уровень цен: его держат деньги на единицу выпуска.</summary>
public class PriceLevelTests
{
    private const GoodType Coal = GoodType.Coal;
    private const BuildingType Mine = BuildingType.CoalMine;

    [Fact]
    public void StartPricesMeanStartLevel()
    {
        Assert.Equal(PriceLevel.Scale, PriceLevel.Of(Money.FromWhole(500), Money.FromWhole(500)));
    }

    [Fact]
    public void TwiceTheNominalIsTwiceTheLevel()
    {
        Assert.Equal(2 * PriceLevel.Scale, PriceLevel.Of(Money.FromWhole(1000), Money.FromWhole(500)));
    }

    /// <summary>Выпуска нет — сравнивать нечего, и уровень остаётся стартовым.</summary>
    [Fact]
    public void NoOutputMeansNoLevel()
    {
        Assert.Equal(PriceLevel.Scale, PriceLevel.Of(Money.FromWhole(1000), default));
    }

    [Fact]
    public void MoreMoneyPullsTheLevelUp()
    {
        var target = PriceLevel.Target(
            supply: Money.FromWhole(200), supplyBefore: Money.FromWhole(100),
            real: Money.FromWhole(50), realBefore: Money.FromWhole(50));

        Assert.Equal(2 * PriceLevel.Scale, target);
    }

    [Fact]
    public void MoreOutputPullsTheLevelDown()
    {
        var target = PriceLevel.Target(
            supply: Money.FromWhole(100), supplyBefore: Money.FromWhole(100),
            real: Money.FromWhole(200), realBefore: Money.FromWhole(50));

        Assert.Equal(PriceLevel.Scale / 4, target);
    }

    /// <summary>Массы не было — печатать не с чего, уровень стартовый.</summary>
    [Fact]
    public void CountryWithoutMoneyHasNoTarget()
    {
        Assert.Equal(PriceLevel.Scale, PriceLevel.Target(
            Money.FromWhole(100), default, Money.FromWhole(50), Money.FromWhole(50)));
    }

    [Fact]
    public void RescaleKeepsPricesInProportion()
    {
        var prices = new Prices(new Dictionary<GoodType, Money>
        {
            [Coal] = Money.FromWhole(100),
            [GoodType.Oil] = Money.FromWhole(400),
        });

        prices.Rescale(3, 2);

        Assert.Equal(Money.FromWhole(150), prices.Of(Coal));
        Assert.Equal(Money.FromWhole(600), prices.Of(GoodType.Oil));
    }

    /// <summary>Тик целиком: у страны с постоянными деньгами и растущим выпуском цены
    /// идут вниз, а не упираются в подпорку коридора.</summary>
    [Fact]
    public void GrowingOutputOnFixedMoneyBringsPricesDown()
    {
        var world = Build.World(
            [Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 10 })],
            [Build.Country(1, prices: new() { [Coal] = Money.FromWhole(100) }, money: 1_000_000, supply: 1_000_000)],
            Build.Catalog(Build.Info(Mine, outputs: new() { [Coal] = Build.Whole(10) })),
            new Dictionary<GoodType, Money> { [Coal] = Money.FromWhole(100) });

        var country = world.CountryById(1);

        var simulation = new Simulation(world);
        for (var tick = 0; tick < 200; tick++) simulation.Tick();

        // Уголь никто не покупает, покрытие огромно — без якоря цена сползла бы на пол
        // коридора, в сотую от старта. Деньги этого не позволяют.
        Assert.True(country.State.Prices.Of(Coal) > Money.FromWhole(50),
            $"цена провалилась ниже, чем позволяют деньги: {country.State.Prices.Of(Coal).Exact}");
    }
}
