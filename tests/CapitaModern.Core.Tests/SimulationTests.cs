using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

public class SimulationTests
{
    private const BuildingType Mine = BuildingType.CoalMine;
    private const BuildingType Mill = BuildingType.SteelMill;
    private const BuildingType Plant = BuildingType.ChemicalPlant;

    /// <summary>Шахта без входов: сколько заводов, столько и выпуска.</summary>
    [Fact]
    public void ProducesEveryTick()
    {
        var world = WorldWith(
            Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 3 }),
            Build.Country(1),
            Build.Info(Mine, outputs: new() { [GoodType.Coal] = Build.Units(10) }));

        new Simulation(world).Tick();

        Assert.Equal(Build.Units(30), world.CountryById(1).StockOf(GoodType.Coal));
    }

    [Fact]
    public void ProductionAddsUpOverTicks()
    {
        var world = WorldWith(
            Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 1 }),
            Build.Country(1),
            Build.Info(Mine, outputs: new() { [GoodType.Coal] = Build.Units(10) }));

        var simulation = new Simulation(world);
        for (var i = 0; i < 5; i++) simulation.Tick();

        Assert.Equal(Build.Units(50), world.CountryById(1).StockOf(GoodType.Coal));
    }

    /// <summary>Свежая продукция достаётся следующему тику, а не заводам в этом же.</summary>
    [Fact]
    public void ChainTakesOneTickPerStep()
    {
        var world = WorldWith(
            Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 1, [Mill] = 1 }),
            Build.Country(1),
            Build.Info(Mine, outputs: new() { [GoodType.Coal] = Build.Units(10) }),
            Build.Info(Mill, inputs: new() { [GoodType.Coal] = Build.Units(10) },
                             outputs: new() { [GoodType.Metals] = Build.Units(4) }));

        var simulation = new Simulation(world);
        simulation.Tick();

        Assert.Equal(default, world.CountryById(1).StockOf(GoodType.Metals));

        simulation.Tick();

        Assert.Equal(Build.Units(4), world.CountryById(1).StockOf(GoodType.Metals));
    }

    [Fact]
    public void MiningNeedsTheDeposit()
    {
        var world = WorldWith(
            Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 1 }),
            Build.Country(1),
            Build.Info(Mine, outputs: new() { [GoodType.Oil] = Build.Units(10) }, deposit: GoodType.Oil));

        new Simulation(world).Tick();

        Assert.Equal(default, world.CountryById(1).StockOf(GoodType.Oil));
    }

    [Fact]
    public void MiningWorksWhereTheDepositIs()
    {
        var region = Build.Region(1, 1,
            new Dictionary<BuildingType, int> { [Mine] = 1 },
            new Dictionary<GoodType, int> { [GoodType.Oil] = 400 });

        var world = WorldWith(region, Build.Country(1),
            Build.Info(Mine, outputs: new() { [GoodType.Oil] = Build.Units(10) }, deposit: GoodType.Oil));

        new Simulation(world).Tick();

        Assert.Equal(Build.Units(10), world.CountryById(1).StockOf(GoodType.Oil));
    }

    /// <summary>
    /// Нехватку делят по заказу: у одного завода десятая часть спроса, у четырёх — остальное.
    /// </summary>
    [Fact]
    public void ShortageIsSharedByDemand()
    {
        var world = WorldWith(
            Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mill] = 1, [Plant] = 4 }),
            Build.Country(1, new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Units(25) }),
            Build.Info(Mill, inputs: new() { [GoodType.Coal] = Build.Units(10) },
                             outputs: new() { [GoodType.Metals] = Build.Units(1) }),
            Build.Info(Plant, inputs: new() { [GoodType.Coal] = Build.Units(10) },
                              outputs: new() { [GoodType.Chemicals] = Build.Units(1) }));

        new Simulation(world).Tick();

        var country = world.CountryById(1);

        Assert.Equal(Build.Units(1) / 2, country.StockOf(GoodType.Metals));
        Assert.Equal(Build.Units(2), country.StockOf(GoodType.Chemicals));
        Assert.Equal(default, country.StockOf(GoodType.Coal));
    }

    /// <summary>Ради этого и вводилась дробная загрузка: раньше такой завод стоял.</summary>
    [Fact]
    public void LonePlantWorksOnPartOfItsCapacity()
    {
        var world = WorldWith(
            Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mill] = 1 }),
            Build.Country(1, new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Units(3) }),
            Build.Info(Mill, inputs: new() { [GoodType.Coal] = Build.Units(10) },
                             outputs: new() { [GoodType.Metals] = Build.Units(1) }));

        new Simulation(world).Tick();

        Assert.Equal(new GoodAmount(GoodAmount.Scale * 3 / 10), world.CountryById(1).StockOf(GoodType.Metals));
    }

    /// <summary>Заводы одной страны считаются вместе, где бы ни стояли.</summary>
    [Fact]
    public void PlantsInDifferentRegionsShareOneStock()
    {
        var world = new GameWorld(
            [
                Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mill] = 1 }),
                Build.Region(2, 1, new Dictionary<BuildingType, int> { [Mill] = 1 }),
            ],
            [Build.Country(1, new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Units(20) })],
            Build.Catalog(Build.Info(Mill,
                inputs: new() { [GoodType.Coal] = Build.Units(10) },
                outputs: new() { [GoodType.Metals] = Build.Units(1) })));

        new Simulation(world).Tick();

        Assert.Equal(Build.Units(2), world.CountryById(1).StockOf(GoodType.Metals));
    }

    [Fact]
    public void CountriesDoNotFeedEachOther()
    {
        var world = new GameWorld(
            [
                Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mill] = 1 }),
                Build.Region(2, 2, new Dictionary<BuildingType, int> { [Mill] = 1 }),
            ],
            [
                Build.Country(1, new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Units(10) }),
                Build.Country(2),
            ],
            Build.Catalog(Build.Info(Mill,
                inputs: new() { [GoodType.Coal] = Build.Units(10) },
                outputs: new() { [GoodType.Metals] = Build.Units(1) })));

        new Simulation(world).Tick();

        Assert.Equal(Build.Units(1), world.CountryById(1).StockOf(GoodType.Metals));
        Assert.Equal(default, world.CountryById(2).StockOf(GoodType.Metals));
    }

    [Fact]
    public void NothingToEatMeansNothingProduced()
    {
        var world = WorldWith(
            Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mill] = 1 }),
            Build.Country(1),
            Build.Info(Mill, inputs: new() { [GoodType.Coal] = Build.Units(10) },
                             outputs: new() { [GoodType.Metals] = Build.Units(1) }));

        new Simulation(world).Tick();

        Assert.Equal(default, world.CountryById(1).StockOf(GoodType.Metals));
    }

    /// <summary>Тик обязан быть воспроизводимым: одинаковый старт — одинаковый итог.</summary>
    [Fact]
    public void SameStartGivesSameResult()
    {
        static GoodAmount Run()
        {
            var world = WorldWith(
                Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 3, [Mill] = 2 }),
                Build.Country(1),
                Build.Info(Mine, outputs: new() { [GoodType.Coal] = Build.Units(7) }),
                Build.Info(Mill, inputs: new() { [GoodType.Coal] = Build.Units(10) },
                                 outputs: new() { [GoodType.Metals] = Build.Units(3) }));

            var simulation = new Simulation(world);
            for (var i = 0; i < 20; i++) simulation.Tick();

            return world.CountryById(1).StockOf(GoodType.Metals);
        }

        Assert.Equal(Run(), Run());
    }

    private static GameWorld WorldWith(Region region, Country country, params BuildingInfo[] infos) =>
        new([region], [country], Build.Catalog(infos));
}
