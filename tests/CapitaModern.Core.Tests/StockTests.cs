using CapitaModern.Core.Economy;
using Xunit;

namespace CapitaModern.Core.Tests;

public class StockTests
{
    private static Stock With(params (GoodType Good, long Units)[] items) =>
        new(items.ToDictionary(item => item.Good, item => Build.Units(item.Units)));

    [Fact]
    public void EmptyStockIsZeroNotAnError()
    {
        Assert.Equal(default, With().Of(GoodType.Coal));
    }

    [Fact]
    public void StoreAccumulates()
    {
        var stock = With();

        stock.Store(GoodType.Coal, Build.Units(5));
        stock.Store(GoodType.Coal, Build.Units(3));

        Assert.Equal(Build.Units(8), stock.Of(GoodType.Coal));
    }

    [Fact]
    public void StoringNegativeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => With().Store(GoodType.Coal, new GoodAmount(-1)));
    }

    [Fact]
    public void ConsumesWholeRecipeAtFullLoad()
    {
        var stock = With((GoodType.Coal, 10), (GoodType.IronOre, 10));

        var recipe = new Dictionary<GoodType, GoodAmount>
        {
            [GoodType.Coal] = Build.Units(2),
            [GoodType.IronOre] = Build.Units(3),
        };

        Assert.True(stock.TryConsume(recipe, Load.Full));
        Assert.Equal(Build.Units(8), stock.Of(GoodType.Coal));
        Assert.Equal(Build.Units(7), stock.Of(GoodType.IronOre));
    }

    [Fact]
    public void PartialLoadEatsPartOfTheRecipe()
    {
        var stock = With((GoodType.Coal, 10));
        var recipe = new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Units(2) };

        Assert.True(stock.TryConsume(recipe, Load.Full / 4));
        Assert.Equal(Build.Units(10) - Build.Units(2) / 4, stock.Of(GoodType.Coal));
    }

    [Fact]
    public void SeveralPlantsEatProportionally()
    {
        var stock = With((GoodType.Coal, 10));
        var recipe = new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Units(2) };

        Assert.True(stock.TryConsume(recipe, Load.Full * 3));
        Assert.Equal(Build.Units(4), stock.Of(GoodType.Coal));
    }

    /// <summary>Если хватает на руду, но не на уголь, руда должна остаться на складе.</summary>
    [Fact]
    public void MissingOneGoodCancelsTheWholeRecipe()
    {
        var stock = With((GoodType.Coal, 1), (GoodType.IronOre, 10));

        var recipe = new Dictionary<GoodType, GoodAmount>
        {
            [GoodType.Coal] = Build.Units(2),
            [GoodType.IronOre] = Build.Units(3),
        };

        Assert.False(stock.TryConsume(recipe, Load.Full));
        Assert.Equal(Build.Units(1), stock.Of(GoodType.Coal));
        Assert.Equal(Build.Units(10), stock.Of(GoodType.IronOre));
    }

    [Fact]
    public void ConsumingExactlyEverythingIsAllowed()
    {
        var stock = With((GoodType.Coal, 2));
        var recipe = new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Units(2) };

        Assert.True(stock.TryConsume(recipe, Load.Full));
        Assert.Equal(default, stock.Of(GoodType.Coal));
    }

    [Fact]
    public void ZeroLoadIsRejected()
    {
        var recipe = new Dictionary<GoodType, GoodAmount> { [GoodType.Coal] = Build.Units(1) };

        Assert.Throws<ArgumentOutOfRangeException>(() => With().TryConsume(recipe, 0));
    }

    [Fact]
    public void TakeUpToGivesEverythingWhenThereIsEnough()
    {
        var stock = With((GoodType.Food, 10));

        Assert.Equal(Build.Units(4), stock.TakeUpTo(GoodType.Food, Build.Units(4)));
        Assert.Equal(Build.Units(6), stock.Of(GoodType.Food));
    }

    /// <summary>Населению недостача не ошибка: берём сколько есть и сообщаем сколько вышло.</summary>
    [Fact]
    public void TakeUpToGivesWhatIsLeft()
    {
        var stock = With((GoodType.Food, 3));

        Assert.Equal(Build.Units(3), stock.TakeUpTo(GoodType.Food, Build.Units(10)));
        Assert.Equal(default, stock.Of(GoodType.Food));
    }

    [Fact]
    public void TakeUpToFromEmptyGivesNothing()
    {
        Assert.Equal(default, With().TakeUpTo(GoodType.Food, Build.Units(10)));
    }

    [Fact]
    public void TakingNegativeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => With().TakeUpTo(GoodType.Food, new GoodAmount(-1)));
    }
}
