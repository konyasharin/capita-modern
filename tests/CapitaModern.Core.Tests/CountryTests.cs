using CapitaModern.Core.Economy;
using Xunit;

namespace CapitaModern.Core.Tests;

public class CountryTests
{
    [Fact]
    public void EmptyStockIsZeroNotAnError()
    {
        Assert.Equal(default, Build.Country(1).StockOf(GoodType.Coal));
    }

    [Fact]
    public void StoreAccumulates()
    {
        var country = Build.Country(1);

        country.Store(GoodType.Coal, Build.Units(5));
        country.Store(GoodType.Coal, Build.Units(3));

        Assert.Equal(Build.Units(8), country.StockOf(GoodType.Coal));
    }

    [Fact]
    public void StoringNegativeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Build.Country(1).Store(GoodType.Coal, new GoodAmount(-1)));
    }

    [Fact]
    public void TreasuryTakesAndSpends()
    {
        var country = Build.Country(1);

        country.Receive(100);

        Assert.True(country.TrySpend(40));
        Assert.Equal(60, country.Balance);
    }

    [Fact]
    public void SpendingMoreThanThereIsLeavesTreasuryAlone()
    {
        var country = Build.Country(1);

        country.Receive(100);

        Assert.False(country.TrySpend(101));
        Assert.Equal(100, country.Balance);
    }

    [Fact]
    public void ConsumesWholeRecipeAtFullLoad()
    {
        var country = Build.Country(1, new Dictionary<GoodType, GoodAmount>
        {
            [GoodType.Coal] = Build.Units(10),
            [GoodType.IronOre] = Build.Units(10),
        });

        var recipe = new Dictionary<GoodType, GoodAmount>
        {
            [GoodType.Coal] = Build.Units(2),
            [GoodType.IronOre] = Build.Units(3),
        };

        Assert.True(country.TryConsume(recipe, Load.Full));
        Assert.Equal(Build.Units(8), country.StockOf(GoodType.Coal));
        Assert.Equal(Build.Units(7), country.StockOf(GoodType.IronOre));
    }

    [Fact]
    public void PartialLoadEatsPartOfTheRecipe()
    {
        var country = Build.Country(1, new Dictionary<GoodType, GoodAmount>
        {
            [GoodType.Coal] = Build.Units(10),
        });

        var recipe = new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Units(2) };

        Assert.True(country.TryConsume(recipe, Load.Full / 4));
        Assert.Equal(Build.Units(10) - Build.Units(2) / 4, country.StockOf(GoodType.Coal));
    }

    [Fact]
    public void SeveralPlantsEatProportionally()
    {
        var country = Build.Country(1, new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Units(10) });
        var recipe = new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Units(2) };

        Assert.True(country.TryConsume(recipe, Load.Full * 3));
        Assert.Equal(Build.Units(4), country.StockOf(GoodType.Coal));
    }

    /// <summary>
    /// Если хватает на руду, но не на уголь, руда должна остаться на складе.
    /// </summary>
    [Fact]
    public void MissingOneGoodCancelsTheWholeRecipe()
    {
        var country = Build.Country(1, new Dictionary<GoodType, GoodAmount>
        {
            [GoodType.Coal] = Build.Units(1),
            [GoodType.IronOre] = Build.Units(10),
        });

        var recipe = new Dictionary<GoodType, GoodAmount>
        {
            [GoodType.Coal] = Build.Units(2),
            [GoodType.IronOre] = Build.Units(3),
        };

        Assert.False(country.TryConsume(recipe, Load.Full));
        Assert.Equal(Build.Units(1), country.StockOf(GoodType.Coal));
        Assert.Equal(Build.Units(10), country.StockOf(GoodType.IronOre));
    }

    [Fact]
    public void ConsumingExactlyEverythingIsAllowed()
    {
        var country = Build.Country(1, new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Units(2) });
        var recipe = new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Units(2) };

        Assert.True(country.TryConsume(recipe, Load.Full));
        Assert.Equal(default, country.StockOf(GoodType.Coal));
    }

    [Fact]
    public void ZeroLoadIsRejected()
    {
        var country = Build.Country(1);
        var recipe = new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Units(1) };

        Assert.Throws<ArgumentOutOfRangeException>(() => country.TryConsume(recipe, 0));
    }
}
