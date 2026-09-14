using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Цена ползёт к нормальному запасу. Проверяем знак, шаг и то, что она не
/// умирает в нуле.</summary>
public class PriceTests
{
    private const BuildingType Mine = BuildingType.CoalMine;
    private const BuildingType Mill = BuildingType.SteelMill;

    /// <summary>Стартовая цена 100 — на ней процент шага виден целым числом.</summary>
    private static Prices Start(long price = 100) =>
        new(Enum.GetValues<GoodType>().ToDictionary(good => good, _ => Money.FromWhole(price)));









    /// <summary>Резкое движение приходит событием, а не ежедневной формулой.</summary>
    [Fact]
    public void ShockMovesPriceAtOnce()
    {
        var prices = Start();
        prices.Shock(GoodType.Oil, 400);

        Assert.Equal(Money.FromWhole(500), prices.Of(GoodType.Oil));
    }



    [Fact]
    public void CostOfCountsFractionsOfAUnit()
    {
        var prices = Start();

        Assert.Equal(Money.FromWhole(250), prices.CostOf(GoodType.Coal, Build.Whole(5) / 2));
    }

    [Fact]
    public void UnknownGoodGetsTheDefaultPrice()
    {
        Assert.Equal(Prices.Default, new Prices().Of(GoodType.Aircraft));
    }

    /// <summary>За чем стоит очередь, то и дорожает: спроса вдесятеро больше, чем можно
    /// дать, — цена уходит выше обычной.</summary>
    [Fact]
    public void ShortageInTheTickRaisesThePrice()
    {
        var world = Build.World(
            [Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mill] = 1 })],
            [Build.Country(1, prices: StartPrices())],
            Build.Catalog(Build.Info(Mill,
                inputs: new() { [GoodType.Coal] = Build.Whole(10) },
                outputs: new() { [GoodType.Metals] = Build.Whole(1) })),
            demand: new Dictionary<GoodType, int> { [GoodType.Coal] = -50 },
            supply: new Dictionary<GoodType, int> { [GoodType.Coal] = 50 });

        new Simulation(world).Tick();

        Assert.True(world.CountryById(1).State.Prices.Of(GoodType.Coal) > Money.FromWhole(100),
            "уголь просят, а взять негде — цена не поднялась");
    }

    /// <summary>Товар, который никто не заказывает, дешевеет — даже если его выпускают.</summary>
    [Fact]
    public void UnwantedOutputGetsCheaper()
    {
        var world = Build.World(
            [Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 1 })],
            [Build.Country(1, prices: StartPrices())],
            Build.Catalog(Build.Info(Mine, outputs: new() { [GoodType.Coal] = Build.Whole(10) })),
            demand: new Dictionary<GoodType, int> { [GoodType.Coal] = -50 },
            supply: new Dictionary<GoodType, int> { [GoodType.Coal] = 50 });

        var simulation = new Simulation(world);
        simulation.Tick();
        simulation.Tick();

        Assert.True(world.CountryById(1).State.Prices.Of(GoodType.Coal) < Money.FromWhole(100),
            "уголь никому не нужен, а цена держится");
    }

    /// <summary>Шахта выдаёт уголь из ничего, завод превращает его в металл. ВВП — это
    /// уголь плюс металл минус съеденный уголь, а не выручка обоих.</summary>
    [Fact]
    public void ValueAddedIsOutputMinusWhatWentIntoIt()
    {
        var world = Build.World(
            [Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 1, [Mill] = 1 })],
            [
                Build.Country(1,
                    new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Whole(10) },
                    prices: StartPrices())
            ],
            Build.Catalog(
                Build.Info(Mine, outputs: new() { [GoodType.Coal] = Build.Whole(10) }),
                Build.Info(Mill, inputs: new() { [GoodType.Coal] = Build.Whole(10) },
                                 outputs: new() { [GoodType.Metals] = Build.Whole(4) })));

        var simulation = new Simulation(world);
        simulation.Tick();

        // 10 угля по 100 плюс 4 металла по 700 минус 10 угля, ушедших в передел.
        Assert.Equal(Money.FromWhole(2800), simulation.ValueAddedOf(1));
    }

    [Fact]
    public void ValueAddedIsZeroBeforeTheFirstTick()
    {
        var world = Build.World(
            [Build.Region(1, 1)], [Build.Country(1, prices: StartPrices())], Build.Catalog());

        Assert.Equal(default, new Simulation(world).ValueAddedOf(1));
    }

    private static Dictionary<GoodType, Money> StartPrices() => new()
    {
        [GoodType.Coal] = Money.FromWhole(100),
        [GoodType.Metals] = Money.FromWhole(700),
    };
}
