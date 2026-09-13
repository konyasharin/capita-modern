using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Курс идёт за сальдо и через эластичность сам закрывает дефицит.</summary>
public class ExchangeRateTests
{
    private const GoodType Coal = GoodType.Coal;
    private const BuildingType Mine = BuildingType.CoalMine;
    private const BuildingType Mill = BuildingType.SteelMill;

    /// <param name="buyerMoney">Резервы покупателя. Не миллиард: правило достаточности
    /// смотрит на запас против сорокадневного ввоза, и при бездонных резервах курс
    /// укрепляется, сколько бы страна ни ввозила.</param>
    /// <summary>Страна 1 копает уголь и ничего не потребляет, страна 2 наоборот.</summary>
    private static GameWorld TwoCountries(long buyerMoney = 200_000) => Build.World(
        [
            Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 100 }),
            Build.Region(2, 2, new Dictionary<BuildingType, int> { [Mill] = 1 }),
        ],
        [Build.Country(1), Build.Country(2, money: buyerMoney)],
        Build.Catalog(
            Build.Info(Mine, outputs: new() { [Coal] = Build.Whole(10) }),
            Build.Info(Mill, inputs: new() { [Coal] = Build.Whole(10) },
                             outputs: new() { [GoodType.Metals] = Build.Whole(1) })),
        new Dictionary<GoodType, Money> { [Coal] = Money.FromWhole(100) },
        new Dictionary<GoodType, int> { [Coal] = -50 },
        new Dictionary<GoodType, int> { [Coal] = 30 });

    [Fact]
    public void RateStartsAtOne()
    {
        Assert.Equal(Money.FromWhole(1), Build.Country(1).ExchangeRate);
    }

    [Fact]
    public void DeficitWeakensTheCurrency()
    {
        var world = TwoCountries();
        var simulation = new Simulation(world);
        for (var tick = 0; tick < 10; tick++) simulation.Tick();

        // Второй только ввозит: его валюта должна подешеветь, то есть курс вырасти.
        Assert.True(world.CountryById(2).ExchangeRate > Money.FromWhole(1), "валюта импортёра не подешевела");
    }

    [Fact]
    public void SurplusStrengthensTheCurrency()
    {
        var world = TwoCountries();
        var simulation = new Simulation(world);
        for (var tick = 0; tick < 10; tick++) simulation.Tick();

        Assert.True(world.CountryById(1).ExchangeRate < Money.FromWhole(1), "валюта экспортёра не подорожала");
    }

    [Fact]
    public void NoTradeMeansNoMove()
    {
        var world = Build.World(
            [Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 1 })],
            [Build.Country(1)],
            Build.Catalog(Build.Info(Mine, outputs: new() { [Coal] = Build.Whole(10) })));

        for (var tick = 0; tick < 10; tick++) new Simulation(world).Tick();

        Assert.Equal(Money.FromWhole(1), world.CountryById(1).ExchangeRate);
    }

    /// <summary>Курс идёт не только за сальдо, но и за уровнем цен: у кого всё
    /// подорожало вдвое против соседа, у того валюта дешевеет.</summary>
    [Fact]
    public void RateFollowsThePriceLevel()
    {
        var world = TwoCountries();
        var simulation = new Simulation(world);
        simulation.Tick();

        var before = world.CountryById(2).ExchangeRate;
        world.CountryById(2).State.Prices.Rescale(2, 1);

        // Десять тиков, а не сто: держать цены вдвое выше мировых страна и не может —
        // закон одной цены вернёт их назад, а курс к тому времени уже отреагирует.
        for (var tick = 0; tick < 10; tick++) simulation.Tick();

        Assert.True(world.CountryById(2).ExchangeRate > before,
            "цены выросли вдвое, а валюта не подешевела");
    }

    /// <summary>Валюта, нарезанная тысячей за доллар, такой и остаётся: паритет считают
    /// от её собственного старта, а не от единицы.</summary>
    [Fact]
    public void CheapCurrencyDoesNotClimbToOne()
    {
        // Третья страна живёт тем же, что и вторая, но её деньги нарезаны тысячей за
        // доллар — значит и цены у неё в тысячу раз крупнее.
        var world = Build.World(
            [
                Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 100 }),
                Build.Region(2, 2, new Dictionary<BuildingType, int> { [Mill] = 1 }),
                Build.Region(3, 3, new Dictionary<BuildingType, int> { [Mill] = 1 }),
            ],
            [
                Build.Country(1),
                Build.Country(2, money: 200_000),
                Build.Country(3, money: 200_000, rate: 1000,
                    prices: new() { [Coal] = Money.FromWhole(100_000) }),
            ],
            Build.Catalog(
                Build.Info(Mine, outputs: new() { [Coal] = Build.Whole(10) }),
                Build.Info(Mill, inputs: new() { [Coal] = Build.Whole(10) },
                                 outputs: new() { [GoodType.Metals] = Build.Whole(1) })),
            new Dictionary<GoodType, Money> { [Coal] = Money.FromWhole(100) });

        var pricey = world.CountryById(3);
        var simulation = new Simulation(world);
        for (var tick = 0; tick < 300; tick++) simulation.Tick();

        // Сравниваем с соседкой, чья валюта нарезана единицей: дорогая должна остаться
        // заметно дороже, а паритет к единице сравнял бы их.
        var plain = world.CountryById(2);

        Assert.True(pricey.ExchangeRate.Raw > plain.ExchangeRate.Raw * 10,
            $"дорогая валюта сравнялась с обычной: {pricey.ExchangeRate.Exact} против {plain.ExchangeRate.Exact}");
    }

    [Fact]
    public void RateStaysInsideTheCorridor()
    {
        var world = TwoCountries();
        var simulation = new Simulation(world);
        for (var tick = 0; tick < 2000; tick++) simulation.Tick();

        foreach (var country in world.Countries)
        {
            Assert.True(country.ExchangeRate.Raw <= Money.FromWhole(1).Raw * Prices.MaxSwingTimes,
                $"{country.Iso} пробил потолок коридора");
            Assert.True(country.ExchangeRate.Raw >= Money.FromWhole(1).Raw / Prices.MaxSwingTimes,
                $"{country.Iso} пробил пол коридора");
        }
    }

    [Fact]
    public void KeyRateIsKeptButDoesNothingYet()
    {
        var country = Build.Country(1);
        country.KeyRate = 425;

        Assert.Equal(425, country.KeyRate);
    }
}
