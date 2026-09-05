using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using Xunit;

namespace CapitaModern.Core.Tests;

public class TallyTests
{
    [Fact]
    public void AddAccumulates()
    {
        var tally = new Tally<GoodType, GoodAmount>();

        tally.Add(1, GoodType.Coal, Build.Units(5));
        tally.Add(1, GoodType.Coal, Build.Units(3));

        Assert.Equal(Build.Units(8), tally.Get(1, GoodType.Coal));
    }

    [Fact]
    public void SetOverwrites()
    {
        var tally = new Tally<GoodType, GoodAmount>();

        tally.Add(1, GoodType.Coal, Build.Units(5));
        tally.Set(1, GoodType.Coal, Build.Units(2));

        Assert.Equal(Build.Units(2), tally.Get(1, GoodType.Coal));
    }

    [Fact]
    public void UnknownKeyGivesZero()
    {
        var tally = new Tally<GoodType, GoodAmount>();

        tally.Add(1, GoodType.Coal, Build.Units(5));

        Assert.Equal(default, tally.Get(1, GoodType.Oil));
        Assert.Equal(default, tally.Get(2, GoodType.Coal));
    }

    [Fact]
    public void CountriesDoNotMix()
    {
        var tally = new Tally<GoodType, GoodAmount>();

        tally.Add(1, GoodType.Coal, Build.Units(5));
        tally.Add(2, GoodType.Coal, Build.Units(7));

        Assert.Equal(Build.Units(5), tally.Get(1, GoodType.Coal));
        Assert.Equal(Build.Units(7), tally.Get(2, GoodType.Coal));
    }

    [Fact]
    public void ClearEmpties()
    {
        var tally = new Tally<GoodType, GoodAmount>();

        tally.Add(1, GoodType.Coal, Build.Units(5));
        tally.Clear();

        Assert.Equal(default, tally.Get(1, GoodType.Coal));
        Assert.Empty(tally);
    }

    [Fact]
    public void ZeroIsAllowed()
    {
        var tally = new Tally<GoodType, GoodAmount>();

        tally.Add(1, GoodType.Coal, default);

        Assert.Equal(default, tally.Get(1, GoodType.Coal));
    }

    [Fact]
    public void NegativeThrows()
    {
        var tally = new Tally<GoodType, GoodAmount>();

        Assert.Throws<ArgumentOutOfRangeException>(() => tally.Add(1, GoodType.Coal, new GoodAmount(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => tally.Set(1, GoodType.Coal, new GoodAmount(-1)));
    }

    [Fact]
    public void EnumeratesEverythingOnce()
    {
        var tally = new Tally<GoodType, GoodAmount>();

        tally.Add(1, GoodType.Coal, Build.Units(5));
        tally.Add(1, GoodType.Oil, Build.Units(6));
        tally.Add(2, GoodType.Coal, Build.Units(7));

        var entries = tally.ToList();

        Assert.Equal(3, entries.Count);
        Assert.Contains(((byte)1, GoodType.Coal, Build.Units(5)), entries);
        Assert.Contains(((byte)1, GoodType.Oil, Build.Units(6)), entries);
        Assert.Contains(((byte)2, GoodType.Coal, Build.Units(7)), entries);
    }

    /// <summary>Постройки считаются штуками, а не количеством товара — тот же счётчик.</summary>
    [Fact]
    public void WorksWithPlainCounts()
    {
        var tally = new Tally<BuildingType, int>();

        tally.Add(1, BuildingType.SteelMill, 2);
        tally.Add(1, BuildingType.SteelMill, 3);

        Assert.Equal(5, tally.Get(1, BuildingType.SteelMill));
    }
}
