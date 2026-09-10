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

    private static double Ordered(long price, long usual = 100, int elasticity = -50) =>
        Elasticity.Adjust(elasticity, Build.Whole(100), Money.FromWhole(price), Money.FromWhole(usual)).Exact;

    [Fact]
    public void UsualPriceChangesNothing()
    {
        Assert.Equal(100.0, Ordered(price: 100));
    }

    /// <summary>Отклик степенной: вдвое дороже при показателе 0.5 — это корень из двух.</summary>
    [Fact]
    public void TwiceTheUsualPriceCutsTheOrderByTheRoot()
    {
        Assert.Equal(70.7, Ordered(price: 200), 0.5);
    }

    [Fact]
    public void HalfTheUsualPriceRaisesTheOrder()
    {
        Assert.Equal(141.4, Ordered(price: 50), 1.0);
    }

    /// <summary>Лекарства берут почти вне зависимости от цены.</summary>
    [Fact]
    public void InelasticGoodBarelyMoves()
    {
        Assert.Equal(87.1, Ordered(price: 200, elasticity: -20), 0.5);
    }

    /// <summary>Дороже — продают больше: без этого курс валют не находит равновесия.</summary>
    [Fact]
    public void HigherPriceRaisesTheOffer()
    {
        Assert.Equal(123.1, Ordered(price: 200, elasticity: 30), 0.5);
    }

    /// <summary>Ради чего и менялась форма: вдали от обычной цены отклик должен быть
    /// сильным, а не упираться в предел.</summary>
    [Fact]
    public void FarFromUsualTheResponseIsStrong()
    {
        // Цена в сотую долю обычной при показателе 0.5 — это десятикратный заказ.
        Assert.Equal(1000.0, Ordered(price: 1, elasticity: -50), 20.0);
    }

    /// <summary>Потребтовары отзываются сильнее всего: вдесятеро дороже — вшестнадцать
    /// раз меньше.</summary>
    [Fact]
    public void ElasticGoodFallsSteeply()
    {
        Assert.Equal(6.3, Ordered(price: 1000, elasticity: -120), 0.3);
    }

    /// <summary>Предел остался страховкой от бесконечности, а не рабочим ограничением.</summary>
    [Fact]
    public void OrderStopsAtTheSafetyLimit()
    {
        Assert.Equal(100.0 * Elasticity.MaxFactor / Elasticity.Scale, Ordered(price: 1, elasticity: -120), 1.0);
    }

    [Fact]
    public void ZeroElasticityIgnoresThePrice()
    {
        Assert.Equal(100.0, Ordered(price: 500, elasticity: 0));
    }

    [Fact]
    public void RealDataGivesEveryGoodBothElasticities()
    {
        var world = WorldDataTests.Shared.Value;

        foreach (var good in Enum.GetValues<GoodType>())
        {
            Assert.True(world.Elasticity.Demand(good) < 0, $"у {good} нет эластичности спроса");
            Assert.True(world.Elasticity.Supply(good) > 0, $"у {good} нет эластичности предложения");
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
                new Dictionary<GoodType, int> { [Coal] = -50 },
                new Dictionary<GoodType, int> { [Coal] = 30 });

            world.Market.Prices.Shock(Coal, percent);

            // Два тика: на первом шахты только наполняют склад, торговать нечем.
            var simulation = new Simulation(world);
            simulation.Tick();
            simulation.Tick();

            return world.CountryById(2).State.Stock.Of(Coal);
        }

        // Вдвое дороже при показателе 0.5 — это корень из двух, а не половина.
        Assert.True(BoughtAfterShock(0) > BoughtAfterShock(100) * 6 / 5, "подорожание не срезало закупку");
    }
}
