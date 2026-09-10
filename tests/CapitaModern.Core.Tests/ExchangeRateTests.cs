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

    /// <summary>Страна 1 копает уголь и ничего не потребляет, страна 2 наоборот.</summary>
    private static GameWorld TwoCountries(long buyerMoney = 1_000_000_000) => Build.World(
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

    /// <summary>Ради чего всё и делалось: слабеющая валюта сама срезает ввоз.</summary>
    [Fact]
    public void WeakCurrencyCutsImports()
    {
        var world = TwoCountries();
        var simulation = new Simulation(world);

        simulation.Tick();
        simulation.Tick();
        var early = simulation.ImportsOf(2);

        for (var tick = 0; tick < 200; tick++) simulation.Tick();
        var late = simulation.ImportsOf(2);

        Assert.True(world.CountryById(2).ExchangeRate > Money.FromWhole(2), "курс почти не сдвинулся");
        Assert.True(late < early, "ослабевшая валюта не срезала ввоз");
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
