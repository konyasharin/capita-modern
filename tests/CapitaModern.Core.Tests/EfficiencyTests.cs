using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Эффективность меняет не выпуск, а число рук. Завод везде одинаковый,
/// американская ферма с комбайном и индийская с полусотней людей дают одно и то же.</summary>
public class EfficiencyTests
{
    private const byte Rich = 1;
    private const byte Poor = 2;

    private static Efficiency Table(IReadOnlyDictionary<Sector, int>? sensitivity = null) => new(
        new Dictionary<byte, (int, int, int)>
        {
            [Rich] = (200, 200, 200),
            [Poor] = (50, 50, 50),
        },
        sensitivity);

    [Fact]
    public void UnknownCountryIsAverage()
    {
        Assert.Equal(Efficiency.Average, new Efficiency().Of(99));
    }

    [Fact]
    public void TotalIsTheProductOfThree()
    {
        // 2 × 2 × 2 = 8 против 0.5 × 0.5 × 0.5 = 0.125.
        Assert.Equal(800, Table().Of(Rich));
        Assert.Equal(12, Table().Of(Poor));
    }

    [Fact]
    public void EfficiencyNeverReachesZero()
    {
        var table = new Efficiency(new Dictionary<byte, (int, int, int)> { [Rich] = (0, 0, 0) });

        Assert.Equal(Efficiency.Floor, table.Of(Rich));
    }

    /// <summary>Чувствительная отрасль растягивает отрыв в обе стороны — отсюда и
    /// специализация.</summary>
    [Fact]
    public void SensitiveSectorWidensTheGap()
    {
        var table = Table(new Dictionary<Sector, int> { [Sector.Mining] = 50, [Sector.Military] = 200 });

        Assert.True(table.Of(Rich, Sector.Military) > table.Of(Rich, Sector.Mining));
        Assert.True(table.Of(Poor, Sector.Military) < table.Of(Poor, Sector.Mining));
    }

    [Fact]
    public void AverageCountryIsUnmovedByAnySector()
    {
        var table = new Efficiency(null, new Dictionary<Sector, int> { [Sector.Military] = 200 });

        Assert.Equal(Efficiency.Average, table.Of(5, Sector.Military));
    }

    /// <summary>Отстающей стране тот же завод обходится в большее число рук, и когда их
    /// не хватает — завод стоит.</summary>
    [Fact]
    public void ShortOfHandsCutsTheLoad()
    {
        static GoodAmount MinedBy(byte owner, int population)
        {
            var world = new GameWorld(
                [Build.Region(1, owner, new Dictionary<BuildingType, int> { [BuildingType.CoalMine] = 1 },
                    population: population)],
                [Build.Country(owner)],
                Build.Catalog(Build.Info(BuildingType.CoalMine,
                    outputs: new() { [GoodType.Coal] = Build.Whole(10) }, workers: 10_000)),
                new Needs(new Dictionary<GoodType, GoodAmount>()),
                Build.Market(),
                new Elasticity(),
                null,
                new Efficiency(new Dictionary<byte, (int, int, int)>
                {
                    [Rich] = (Efficiency.Scale, Efficiency.Scale, Efficiency.Scale),
                    [Poor] = (50, Efficiency.Scale, Efficiency.Scale),
                }));

            new Simulation(world).Tick();

            return world.CountryById(owner).State.Stock.Of(GoodType.Coal);
        }

        // Шахте нужны десять тысяч рук; отстающей стране на тот же завод их надо вдвое
        // больше. При населении в тридцать тысяч рабочей силы хватает только первой.
        Assert.True(MinedBy(Rich, 30_000) > MinedBy(Poor, 30_000), "нехватка рук не срезала выпуск");
        Assert.Equal(MinedBy(Rich, 30_000), MinedBy(Poor, 60_000));
    }

    [Fact]
    public void RealDataGivesEveryCountryAnEfficiency()
    {
        var world = WorldDataTests.Shared.Value;

        foreach (var country in world.Countries)
        {
            Assert.True(world.Efficiency.Of(country.Id) > 0, $"у {country.Iso} нет эффективности");
        }
    }

    /// <summary>Ради этого шага всё и делалось: разброс должен стать реалистичным.</summary>
    [Fact]
    public void RealDataSpreadsProductivityLikeTheWorldDoes()
    {
        var world = WorldDataTests.Shared.Value;
        var best = world.Countries.Max(c => world.Efficiency.Of(c.Id));
        var worst = world.Countries.Min(c => world.Efficiency.Of(c.Id));

        Assert.InRange(best / (double)worst, 20, 100);
    }
}
