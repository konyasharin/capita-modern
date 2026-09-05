using System.Text.Json;
using System.Text.Json.Serialization;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Loading;
using Xunit;

namespace CapitaModern.Core.Tests;

public class GoodAmountTests
{
    [Fact]
    public void FromUnitsStoresHundredths()
    {
        Assert.Equal(5 * GoodAmount.Scale, GoodAmount.FromUnits(5).Raw);
    }

    [Fact]
    public void UnitsGivesBackWhatWasPutIn()
    {
        Assert.Equal(5.0, GoodAmount.FromUnits(5).Units);
    }

    [Fact]
    public void UnitsShowsFractions()
    {
        Assert.Equal(0.5, new GoodAmount(GoodAmount.Scale / 2).Units);
    }

    [Fact]
    public void DefaultIsZero()
    {
        Assert.Equal(0, default(GoodAmount).Raw);
    }

    [Fact]
    public void AddsAndSubtracts()
    {
        var a = GoodAmount.FromUnits(7);
        var b = GoodAmount.FromUnits(3);

        Assert.Equal(GoodAmount.FromUnits(10), a + b);
        Assert.Equal(GoodAmount.FromUnits(4), a - b);
    }

    [Fact]
    public void SubtractionCanGoNegative()
    {
        Assert.True(GoodAmount.FromUnits(1) - GoodAmount.FromUnits(3) < default(GoodAmount));
    }

    [Fact]
    public void MultipliesAndDivides()
    {
        Assert.Equal(GoodAmount.FromUnits(12), GoodAmount.FromUnits(4) * 3);
        Assert.Equal(GoodAmount.FromUnits(4), GoodAmount.FromUnits(12) / 3);
    }

    [Fact]
    public void DivisionRoundsDown()
    {
        Assert.Equal(new GoodAmount(1), new GoodAmount(3) / 2);
    }

    [Fact]
    public void Compares()
    {
        var small = GoodAmount.FromUnits(1);
        var big = GoodAmount.FromUnits(2);

        Assert.True(small < big);
        Assert.True(big > small);
        Assert.True(small <= GoodAmount.FromUnits(1));
        Assert.True(small >= GoodAmount.FromUnits(1));
        Assert.Equal(small, GoodAmount.FromUnits(1));
    }

    /// <summary>Рецепт в файле записан единицами, а внутри должен стать сотыми.</summary>
    [Fact]
    public void JsonReadsUnitsAndScalesThem()
    {
        var recipe = JsonReader.Read<Dictionary<GoodType, GoodAmount>>("""{"Coal": 8}""");

        Assert.Equal(GoodAmount.FromUnits(8), recipe[GoodType.Coal]);
    }

    [Fact]
    public void JsonWritesUnitsBack()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter(), new GoodAmountJsonConverter() },
        };

        var json = JsonSerializer.Serialize(GoodAmount.FromUnits(8), options);

        Assert.Equal("8", json);
    }
}
