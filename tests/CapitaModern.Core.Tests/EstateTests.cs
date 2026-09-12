using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Жилой фонд и дороги: копятся стройкой и ветшают по сроку.</summary>
public class EstateTests
{
    private const int Year = 365;

    [Fact]
    public void EmptyEstateWearsNothing()
    {
        var estate = new Estate();
        estate.Wear(Year);

        Assert.Equal(default, estate.Housing);
        Assert.Equal(default, estate.Roads);
    }

    [Fact]
    public void BuiltAddsUp()
    {
        var estate = new Estate();
        estate.Settle(GoodAmount.FromWhole(100));
        estate.Pave(GoodAmount.FromWhole(40));

        Assert.Equal(GoodAmount.FromWhole(100), estate.Housing);
        Assert.Equal(GoodAmount.FromWhole(40), estate.Roads);
    }

    /// <summary>Дорога ветшает быстрее дома: тридцать лет против полувека.</summary>
    [Fact]
    public void RoadsWearFasterThanHouses()
    {
        var estate = new Estate();
        estate.Settle(GoodAmount.FromWhole(1000));
        estate.Pave(GoodAmount.FromWhole(1000));

        for (var day = 0; day < Year * 10; day++) estate.Wear(Year);

        Assert.True(estate.Roads < estate.Housing, "дорога не обогнала дом по износу");
        Assert.True(estate.Housing < GoodAmount.FromWhole(1000), "дом не ветшает вовсе");
    }
}
