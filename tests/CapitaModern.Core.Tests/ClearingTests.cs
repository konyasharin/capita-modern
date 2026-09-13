using CapitaModern.Core.Economy;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Цена сходится к равновесию сразу и не раскачивается.</summary>
public class ClearingTests
{
    private static readonly Money Usual = Money.FromWhole(100);

    private static Money At(long wanted, long offered, int demand = 50, int supply = 50) =>
        Clearing.Price(Usual, Build.Whole(wanted), Build.Whole(offered), demand, supply);

    [Fact]
    public void BalancedMarketKeepsTheUsualPrice()
    {
        Assert.Equal(Usual, At(100, 100));
    }

    [Fact]
    public void ShortageRaisesThePrice()
    {
        Assert.True(At(200, 100) > Usual);
    }

    [Fact]
    public void GlutLowersThePrice()
    {
        Assert.True(At(100, 200) < Usual);
    }

    /// <summary>Вдвое больше спроса при таких упругостях — вдвое дороже.</summary>
    [Fact]
    public void PriceFollowsTheRatioThroughElasticity()
    {
        // Упругости по половине: сумма единица, значит цена идёт за отношением один к одному.
        var twice = At(200, 100);

        Assert.InRange(twice.Raw, Usual.Raw * 19 / 10, Usual.Raw * 21 / 10);
    }

    /// <summary>Главное свойство: цена не зависит от того, какой она была вчера, — значит
    /// и раскачиваться ей не от чего.</summary>
    [Fact]
    public void SameMarketGivesSamePriceEveryTime()
    {
        var first = At(150, 100);
        var again = At(150, 100);

        Assert.Equal(first, again);
    }

    /// <summary>Ровный ход спроса даёт ровный ход цены — без скачков и без догоняния.</summary>
    [Fact]
    public void SmoothDemandGivesSmoothPrice()
    {
        var was = At(100, 100);

        for (var step = 1; step <= 20; step++)
        {
            var now = At(100 + step, 100);

            // Каждый следующий шаг мельче двадцатой доли: цена идёт за спросом, а не прыгает.
            Assert.True(now > was, "цена не пошла за растущим спросом");
            Assert.True(now.Raw - was.Raw < Usual.Raw / 20, $"скачок цены на шаге {step}");

            was = now;
        }
    }

    [Fact]
    public void EmptyMarketKeepsThePrice()
    {
        Assert.Equal(Usual, At(0, 0));
    }

    /// <summary>Упругий товар меняется в цене меньше: замену ему найти легко.</summary>
    [Fact]
    public void ElasticGoodMovesLess()
    {
        var stiff = At(200, 100, demand: 20, supply: 20);
        var loose = At(200, 100, demand: 200, supply: 200);

        Assert.True(loose < stiff, "упругий товар подорожал не меньше неупругого");
    }
}
