using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Спрос отзывается на цену. Без этого курс валют будет двигаться и ни на что
/// не влиять.</summary>
public class ElasticityTests
{
    private const GoodType Coal = GoodType.Coal;
    private const BuildingType Mill = BuildingType.SteelMill;

    private static GoodAmount Ordered(long price, long usual = 100, int elasticity = -50)
    {
        var table = new Elasticity(new Dictionary<GoodType, int> { [Coal] = elasticity });

        return table.Adjust(Coal, Build.Whole(100), Money.FromWhole(price), Money.FromWhole(usual));
    }

    [Fact]
    public void UsualPriceChangesNothing()
    {
        Assert.Equal(Build.Whole(100), Ordered(price: 100));
    }

    [Fact]
    public void TwiceTheUsualPriceHalvesTheOrder()
    {
        Assert.Equal(Build.Whole(50), Ordered(price: 200));
    }

    [Fact]
    public void HalfTheUsualPriceRaisesTheOrder()
    {
        // Цена ниже обычной на 50% — заказ выше на 25%.
        Assert.Equal(Build.Whole(125), Ordered(price: 50));
    }

    /// <summary>Лекарства берут почти вне зависимости от цены.</summary>
    [Fact]
    public void InelasticGoodBarelyMoves()
    {
        Assert.Equal(Build.Whole(80), Ordered(price: 200, elasticity: -20));
    }

    /// <summary>Потребтовары отзываются сильнее всего, но заказ не может исчезнуть.</summary>
    [Fact]
    public void OrderNeverFallsToNothing()
    {
        Assert.Equal(Build.Whole(10), Ordered(price: 1000, elasticity: -120));
    }

    [Fact]
    public void OrderNeverMoreThanDoubles()
    {
        Assert.Equal(Build.Whole(200), Ordered(price: 1, elasticity: -120));
    }

    [Fact]
    public void ZeroElasticityIgnoresThePrice()
    {
        Assert.Equal(Build.Whole(100), Ordered(price: 500, elasticity: 0));
    }

    [Fact]
    public void RealDataGivesEveryGoodAnElasticity()
    {
        var world = WorldDataTests.Shared.Value;

        foreach (var good in Enum.GetValues<GoodType>())
        {
            Assert.True(world.Elasticity.Of(good) < 0, $"у {good} нет эластичности");
        }
    }

    /// <summary>В тике: подорожавший уголь закупают меньше.</summary>
    [Fact]
    public void ExpensiveGoodIsBidLessInTheTick()
    {
        static GoodAmount BoughtAfterShock(int percent)
        {
            var world = Build.World(
                [
                    Build.Region(1, 1, new Dictionary<BuildingType, int> { [BuildingType.CoalMine] = 100 }),
                    Build.Region(2, 2, new Dictionary<BuildingType, int> { [Mill] = 1 }),
                ],
                [Build.Country(1), Build.Country(2, money: 1_000_000_000)],
                Build.Catalog(
                    Build.Info(BuildingType.CoalMine, outputs: new() { [Coal] = Build.Whole(10) }),
                    Build.Info(Mill, inputs: new() { [Coal] = Build.Whole(10) },
                                     outputs: new() { [GoodType.Metals] = Build.Whole(1) })),
                new Dictionary<GoodType, Money> { [Coal] = Money.FromWhole(100) },
                new Dictionary<GoodType, int> { [Coal] = -50 });

            world.Market.Prices.Shock(Coal, percent);

            // Два тика: на первом шахты только наполняют склад, торговать нечем.
            var simulation = new Simulation(world);
            simulation.Tick();
            simulation.Tick();

            return world.CountryById(2).State.Stock.Of(Coal);
        }

        Assert.True(BoughtAfterShock(0) > BoughtAfterShock(100) * 3 / 2, "подорожание не срезало закупку");
    }
}
