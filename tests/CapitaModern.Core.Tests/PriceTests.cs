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

    private static Money Moved(GoodAmount demand, GoodAmount available)
    {
        var prices = Start();
        prices.Move(GoodType.Coal, demand, available);

        return prices.Of(GoodType.Coal);
    }

    [Fact]
    public void EmptyStockRaisesPriceByTheFullStep()
    {
        Assert.Equal(Money.FromWhole(102), Moved(Build.Whole(10), default));
    }

    /// <summary>Спроса нет вовсе — перекос ровно −1, полный шаг вниз.</summary>
    [Fact]
    public void NobodyWantsItSoItGetsCheaperByTheFullStep()
    {
        Assert.Equal(Money.FromWhole(98), Moved(default, Build.Whole(10)));
    }

    /// <summary>Запаса ровно на норму — цену двигать незачем.</summary>
    [Fact]
    public void PriceHoldsAtTheTargetCover()
    {
        Assert.Equal(Money.FromWhole(100), Moved(Build.Whole(1), Build.Whole(Prices.TargetCoverDays)));
    }

    /// <summary>Половина нормы даёт треть перекоса, а не весь шаг: (40−20)/(40+20).</summary>
    [Fact]
    public void HalfTheStockMovesPriceByPartOfTheStep()
    {
        var expected = Money.FromWhole(100) + new Money(Money.Scale * 100 * 2 * 20 / (60 * 100));

        Assert.Equal(expected, Moved(Build.Whole(1), Build.Whole(Prices.TargetCoverDays / 2)));
    }

    /// <summary>Ни спроса, ни запаса — про товар ничего не известно, цена стоит.</summary>
    [Fact]
    public void NothingKnownMeansNoMove()
    {
        Assert.Equal(Money.FromWhole(100), Moved(default, default));
    }

    /// <summary>Цена не должна доехать до нуля: оттуда товар уже не оживёт. Останавливает
    /// её коридор вокруг стартовой — сотая часть от ста.</summary>
    [Fact]
    public void PriceNeverReachesZero()
    {
        var prices = Start();
        for (var tick = 0; tick < 5_000; tick++)
        {
            prices.Move(GoodType.Coal, default, Build.Whole(1));
        }

        Assert.Equal(Money.FromWhole(1), prices.Of(GoodType.Coal));
    }

    /// <summary>Вечная нехватка не должна разгонять цену без предела: пока нет торговли,
    /// упереться она обязана в потолок коридора.</summary>
    [Fact]
    public void EndlessShortageStopsAtTheCeiling()
    {
        var prices = Start();
        for (var tick = 0; tick < 5_000; tick++)
        {
            prices.Move(GoodType.Coal, Build.Whole(10), default);
        }

        Assert.Equal(Money.FromWhole(100 * Prices.MaxSwingTimes), prices.Of(GoodType.Coal));
    }

    /// <summary>Резкое движение приходит событием, а не ежедневной формулой.</summary>
    [Fact]
    public void ShockMovesPriceAtOnce()
    {
        var prices = Start();
        prices.Shock(GoodType.Oil, 400);

        Assert.Equal(Money.FromWhole(500), prices.Of(GoodType.Oil));
    }

    [Fact]
    public void ShockDownStopsAtTheFloor()
    {
        var prices = Start();
        prices.Shock(GoodType.Oil, -100);

        Assert.Equal(Money.FromWhole(1), prices.Of(GoodType.Oil));
    }

    /// <summary>Дешёвому товару коридор считается от его собственного старта, а не от
    /// общего числа.</summary>
    [Fact]
    public void CorridorIsCountedFromTheStartPrice()
    {
        var prices = Start(2);
        prices.Shock(GoodType.Oil, 100_000);

        Assert.Equal(Money.FromWhole(2 * Prices.MaxSwingTimes), prices.Of(GoodType.Oil));
        Assert.Equal(Money.FromWhole(2), prices.StartOf(GoodType.Oil));
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

    /// <summary>Тик двигает цену того, за чем стоит очередь, вверх.</summary>
    [Fact]
    public void ShortageInTheTickRaisesThePrice()
    {
        var world = Build.World(
            [Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mill] = 1 })],
            [Build.Country(1, prices: StartPrices())],
            Build.Catalog(Build.Info(Mill,
                inputs: new() { [GoodType.Coal] = Build.Whole(10) },
                outputs: new() { [GoodType.Metals] = Build.Whole(1) })));

        new Simulation(world).Tick();

        Assert.Equal(Money.FromWhole(102), world.CountryById(1).State.Prices.Of(GoodType.Coal));
    }

    /// <summary>Товар, который никто не заказывает, дешевеет — даже если его выпускают.
    /// Первый тик проходит впустую: склад ещё пуст, знать о товаре нечего.</summary>
    [Fact]
    public void UnwantedOutputGetsCheaper()
    {
        var world = Build.World(
            [Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 1 })],
            [Build.Country(1, prices: StartPrices())],
            Build.Catalog(Build.Info(Mine, outputs: new() { [GoodType.Coal] = Build.Whole(10) })));

        var simulation = new Simulation(world);
        simulation.Tick();
        simulation.Tick();

        Assert.Equal(Money.FromWhole(98), world.CountryById(1).State.Prices.Of(GoodType.Coal));
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
