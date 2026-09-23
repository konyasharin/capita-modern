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
    /// <summary>Завод сбавляет, когда товар залежался: склад вдвое выше нормы запаса —
    /// работает вполсилы, а не в никуда.</summary>
    [Fact]
    public void FullShelfSlowsThePlantDown()
    {
        var world = Build.World(
            [Build.Region(1, 1, new Dictionary<BuildingType, int> { [BuildingType.CoalMine] = 10 })],
            [Build.Country(1)],
            Build.Catalog(Build.Info(BuildingType.CoalMine,
                outputs: new() { [GoodType.Coal] = Build.Whole(10) })),
            new Dictionary<GoodType, Money> { [GoodType.Coal] = Money.FromWhole(1) });

        var simulation = new Simulation(world);
        simulation.Tick();

        var atFirst = simulation.OutputOf(1, GoodType.Coal);
        Assert.True(atFirst.Raw > 0, "шахта не дала угля даже в первый тик");

        // Уголь никому не нужен: за сто тиков склад уходит далеко за норму.
        for (var tick = 0; tick < 100; tick++) simulation.Tick();

        Assert.True(simulation.OutputOf(1, GoodType.Coal) < atFirst,
            "склад давно полон, а шахта копает в прежнюю силу");
    }

    [Fact]
    public void ProducesEveryTick()
    {
        var world = WorldWith(
            Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 3 }),
            Build.Country(1),
            Build.Info(Mine, outputs: new() { [GoodType.Coal] = Build.Whole(10) }));

        new Simulation(world).Tick();

        Assert.Equal(Build.Whole(30), world.CountryById(1).State.Stock.Of(GoodType.Coal));
    }

    [Fact]
    public void ProductionAddsUpOverTicks()
    {
        var world = WorldWith(
            Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 1 }),
            Build.Country(1),
            Build.Info(Mine, outputs: new() { [GoodType.Coal] = Build.Whole(10) }));

        var simulation = new Simulation(world);
        for (var i = 0; i < 5; i++) simulation.Tick();

        // Первый тик шахта работает на полную, а дальше — в самую малую силу: уголь в этом
        // мире никому не нужен вовсе, и склад сразу выше нормы запаса.
        Assert.Equal(new GoodAmount(104000), world.CountryById(1).State.Stock.Of(GoodType.Coal));
    }

    /// <summary>Свежая продукция достаётся следующему тику, а не заводам в этом же.</summary>
    [Fact]
    public void ChainTakesOneTickPerStep()
    {
        var world = WorldWith(
            Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 1, [Mill] = 1 }),
            Build.Country(1),
            Build.Info(Mine, outputs: new() { [GoodType.Coal] = Build.Whole(10) }),
            Build.Info(Mill, inputs: new() { [GoodType.Coal] = Build.Whole(10) },
                             outputs: new() { [GoodType.Metals] = Build.Whole(4) }));

        var simulation = new Simulation(world);
        simulation.Tick();

        Assert.Equal(default, world.CountryById(1).State.Stock.Of(GoodType.Metals));

        simulation.Tick();

        // Почти четыре, а не ровно: залежь сверх нормы запаса понемногу портится, и на втором
        // тике металл теряет пару десятитысячных. Тест про срок передела, а не про них.
        Assert.True(world.CountryById(1).State.Stock.Of(GoodType.Metals) > Build.Whole(3),
            $"передел не дал металла за второй тик: {world.CountryById(1).State.Stock.Of(GoodType.Metals).Exact}");
    }

    [Fact]
    public void MiningNeedsTheDeposit()
    {
        var world = WorldWith(
            Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 1 }),
            Build.Country(1),
            Build.Info(Mine, outputs: new() { [GoodType.Oil] = Build.Whole(10) }, deposit: GoodType.Oil));

        new Simulation(world).Tick();

        Assert.Equal(default, world.CountryById(1).State.Stock.Of(GoodType.Oil));
    }

    [Fact]
    public void MiningWorksWhereTheDepositIs()
    {
        var region = Build.Region(1, 1,
            new Dictionary<BuildingType, int> { [Mine] = 1 },
            new Dictionary<GoodType, int> { [GoodType.Oil] = 400 });

        var world = WorldWith(region, Build.Country(1),
            Build.Info(Mine, outputs: new() { [GoodType.Oil] = Build.Whole(10) }, deposit: GoodType.Oil));

        new Simulation(world).Tick();

        Assert.Equal(Build.Whole(10), world.CountryById(1).State.Stock.Of(GoodType.Oil));
    }

    /// <summary>
    /// Нехватку делят по заказу: у одного завода десятая часть спроса, у четырёх — остальное.
    /// </summary>
    [Fact]
    public void ShortageIsSharedByDemand()
    {
        var world = WorldWith(
            Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mill] = 1, [Plant] = 4 }),
            Build.Country(1, new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Whole(25) }),
            Build.Info(Mill, inputs: new() { [GoodType.Coal] = Build.Whole(10) },
                             outputs: new() { [GoodType.Metals] = Build.Whole(1) }),
            Build.Info(Plant, inputs: new() { [GoodType.Coal] = Build.Whole(10) },
                              outputs: new() { [GoodType.Chemicals] = Build.Whole(1) }));

        new Simulation(world).Tick();

        var country = world.CountryById(1);

        Assert.Equal(Build.Whole(1) / 2, country.State.Stock.Of(GoodType.Metals));
        Assert.Equal(Build.Whole(2), country.State.Stock.Of(GoodType.Chemicals));
        Assert.Equal(default, country.State.Stock.Of(GoodType.Coal));
    }

    /// <summary>Ради этого и вводилась дробная загрузка: раньше такой завод стоял.</summary>
    [Fact]
    public void LonePlantWorksOnPartOfItsCapacity()
    {
        var world = WorldWith(
            Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mill] = 1 }),
            Build.Country(1, new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Whole(3) }),
            Build.Info(Mill, inputs: new() { [GoodType.Coal] = Build.Whole(10) },
                             outputs: new() { [GoodType.Metals] = Build.Whole(1) }));

        new Simulation(world).Tick();

        Assert.Equal(new GoodAmount(GoodAmount.Scale * 3 / 10), world.CountryById(1).State.Stock.Of(GoodType.Metals));
    }

    /// <summary>Заводы одной страны считаются вместе, где бы ни стояли.</summary>
    [Fact]
    public void PlantsInDifferentRegionsShareOneStock()
    {
        var world = Build.World(
            [
                Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mill] = 1 }),
                Build.Region(2, 1, new Dictionary<BuildingType, int> { [Mill] = 1 }),
            ],
            [Build.Country(1, new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Whole(20) })],
            Build.Catalog(Build.Info(Mill,
                inputs: new() { [GoodType.Coal] = Build.Whole(10) },
                outputs: new() { [GoodType.Metals] = Build.Whole(1) })));

        new Simulation(world).Tick();

        Assert.Equal(Build.Whole(2), world.CountryById(1).State.Stock.Of(GoodType.Metals));
    }

    [Fact]
    public void CountriesDoNotFeedEachOther()
    {
        var world = Build.World(
            [
                Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mill] = 1 }),
                Build.Region(2, 2, new Dictionary<BuildingType, int> { [Mill] = 1 }),
            ],
            [
                Build.Country(1, new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Whole(10) }),
                Build.Country(2),
            ],
            Build.Catalog(Build.Info(Mill,
                inputs: new() { [GoodType.Coal] = Build.Whole(10) },
                outputs: new() { [GoodType.Metals] = Build.Whole(1) })));

        new Simulation(world).Tick();

        Assert.Equal(Build.Whole(1), world.CountryById(1).State.Stock.Of(GoodType.Metals));
        Assert.Equal(default, world.CountryById(2).State.Stock.Of(GoodType.Metals));
    }

    [Fact]
    public void NothingToEatMeansNothingProduced()
    {
        var world = WorldWith(
            Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mill] = 1 }),
            Build.Country(1),
            Build.Info(Mill, inputs: new() { [GoodType.Coal] = Build.Whole(10) },
                             outputs: new() { [GoodType.Metals] = Build.Whole(1) }));

        new Simulation(world).Tick();

        Assert.Equal(default, world.CountryById(1).State.Stock.Of(GoodType.Metals));
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
                Build.Info(Mine, outputs: new() { [GoodType.Coal] = Build.Whole(7) }),
                Build.Info(Mill, inputs: new() { [GoodType.Coal] = Build.Whole(10) },
                                 outputs: new() { [GoodType.Metals] = Build.Whole(3) }));

            var simulation = new Simulation(world);
            for (var i = 0; i < 20; i++) simulation.Tick();

            return world.CountryById(1).State.Stock.Of(GoodType.Metals);
        }

        Assert.Equal(Run(), Run());
    }

    private static GameWorld WorldWith(Region region, Country country, params BuildingInfo[] infos) =>
        Build.World([region], [country], Build.Catalog(infos));
}
