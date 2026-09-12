using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Арсенал: копится покупками и осыпается по сроку службы.</summary>
public class ArmyTests
{
    private const int Year = 365;

    [Fact]
    public void EmptyArsenalWearsNothing()
    {
        var army = new Army();

        Assert.Equal(default, army.Wear(GoodType.Armour, Year));
    }

    [Fact]
    public void AddedKitShowsUp()
    {
        var army = new Army();
        army.Add(GoodType.Missiles, GoodAmount.FromWhole(10));
        army.Add(GoodType.Missiles, GoodAmount.FromWhole(5));

        Assert.Equal(GoodAmount.FromWhole(15), army.Of(GoodType.Missiles));
    }

    /// <summary>За срок службы арсенал должен осыпаться почти целиком: списывают остаток,
    /// и на малых числах он упирается в ноль, а не в бесконечный хвост.</summary>
    [Fact]
    public void WearsOutOverServiceLife()
    {
        var army = new Army();
        army.Add(GoodType.Armour, GoodAmount.FromWhole(1000));

        for (var day = 0; day < Army.ServiceYears * Year; day++) army.Wear(GoodType.Armour, Year);

        Assert.True(army.Of(GoodType.Armour) < GoodAmount.FromWhole(400), "арсенал не осыпался");
        Assert.True(army.Of(GoodType.Armour).Raw >= 0, "арсенал ушёл в минус");
    }

    /// <summary>Мелочь не списывается: делить единицу на девять тысяч дней нечем.</summary>
    [Fact]
    public void TinyArsenalHoldsOn()
    {
        var army = new Army();
        army.Add(GoodType.SmallArms, new GoodAmount(100));

        Assert.Equal(default, army.Wear(GoodType.SmallArms, Year));
    }
}
