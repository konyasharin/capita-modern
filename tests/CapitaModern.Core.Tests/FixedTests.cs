using System.Text.Json;
using System.Text.Json.Serialization;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Loading;
using Xunit;

namespace CapitaModern.Core.Tests;

public class FixedTests
{
    [Fact]
    public void FromWholeStoresFractions()
    {
        Assert.Equal(5 * GoodAmount.Scale, GoodAmount.FromWhole(5).Raw);
    }

    [Fact]
    public void ExactGivesBackWhatWasPutIn()
    {
        Assert.Equal(5.0, GoodAmount.FromWhole(5).Exact);
    }

    [Fact]
    public void ExactShowsFractions()
    {
        Assert.Equal(0.5, new GoodAmount(GoodAmount.Scale / 2).Exact);
    }

    [Fact]
    public void DefaultIsZero()
    {
        Assert.Equal(0, default(GoodAmount).Raw);
    }

    [Fact]
    public void AddsAndSubtracts()
    {
        var a = GoodAmount.FromWhole(7);
        var b = GoodAmount.FromWhole(3);

        Assert.Equal(GoodAmount.FromWhole(10), a + b);
        Assert.Equal(GoodAmount.FromWhole(4), a - b);
    }

    [Fact]
    public void SubtractionCanGoNegative()
    {
        Assert.True(GoodAmount.FromWhole(1) - GoodAmount.FromWhole(3) < default(GoodAmount));
    }

    [Fact]
    public void MultipliesAndDivides()
    {
        Assert.Equal(GoodAmount.FromWhole(12), GoodAmount.FromWhole(4) * 3);
        Assert.Equal(GoodAmount.FromWhole(4), GoodAmount.FromWhole(12) / 3);
    }

    [Fact]
    public void DivisionRoundsDown()
    {
        Assert.Equal(new GoodAmount(1), new GoodAmount(3) / 2);
    }

    [Fact]
    public void Compares()
    {
        var small = GoodAmount.FromWhole(1);
        var big = GoodAmount.FromWhole(2);

        Assert.True(small < big);
        Assert.True(big > small);
        Assert.True(small <= GoodAmount.FromWhole(1));
        Assert.True(small >= GoodAmount.FromWhole(1));
        Assert.Equal(small, GoodAmount.FromWhole(1));
    }

    /// <summary>Рецепт в файле записан единицами, а внутри должен стать долями.</summary>
    [Fact]
    public void JsonReadsWholeUnitsAndScalesThem()
    {
        var recipe = JsonReader.Read<Dictionary<GoodType, GoodAmount>>("""{"Coal": 8}""");

        Assert.Equal(GoodAmount.FromWhole(8), recipe[GoodType.Coal]);
    }

    /// <summary>Ставки потребления дробные: 1.06 еды на миллион человек.</summary>
    [Fact]
    public void JsonReadsFractions()
    {
        var rates = JsonReader.Read<Dictionary<GoodType, GoodAmount>>("""{"Food": 1.06}""");

        Assert.Equal(1.06, rates[GoodType.Food].Exact);
    }

    [Fact]
    public void PeopleHaveTheirOwnScale()
    {
        Assert.NotEqual(GoodAmount.Scale, Population.Scale);
        Assert.Equal(5, Population.FromWhole(5).Whole);
    }

    /// <summary>Целая часть отбрасывает дробь, а не округляет.</summary>
    [Fact]
    public void WholeCutsTheFraction()
    {
        Assert.Equal(1, new Population(Population.Scale * 3 / 2).Whole);
    }

    [Fact]
    public void JsonWritesUnitsBack()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter(), new FixedJsonConverter<Goods>() },
        };

        var json = JsonSerializer.Serialize(GoodAmount.FromWhole(8), options);

        Assert.Equal("8", json);
    }
}
