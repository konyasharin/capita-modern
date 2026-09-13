using CapitaModern.Core.Economy;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Спрос идёт за доходом и за ценой, а закон Энгеля выходит сам собой.</summary>
public class SpendingTests
{
    private static readonly Money Floor = Money.FromWhole(100);
    private static readonly Money Price = Money.FromWhole(10);
    private static readonly GoodAmount Least = Build.Whole(5);

    private static GoodAmount At(long income, int share = 20) =>
        Spending.Wanted(Money.FromWhole(income), Floor, Price, Least, share);

    [Fact]
    public void PoorBuyOnlyTheirShareOfTheFloor()
    {
        // Дохода на половину минимума — купят половину нужного, и это голод.
        var half = Spending.Wanted(Money.FromWhole(50 * 100 / Spending.Spends), Floor, Price, Least, 20);

        Assert.True(half < Least, "бедняк купил не меньше минимума");
        Assert.True(half.Raw > 0, "бедняк не купил вовсе ничего");
    }

    [Fact]
    public void RicherBuyMore()
    {
        Assert.True(At(1000) > At(200), "с ростом дохода покупают не больше");
    }

    [Fact]
    public void DearerMeansLess()
    {
        var cheap = Spending.Wanted(Money.FromWhole(1000), Floor, Money.FromWhole(10), Least, 20);
        var dear = Spending.Wanted(Money.FromWhole(1000), Floor, Money.FromWhole(20), Least, 20);

        Assert.True(dear < cheap, "подорожание не убавило покупок");
    }

    /// <summary>Главное: доля минимума в расходах падает с ростом дохода. Это и есть Энгель.</summary>
    [Fact]
    public void EngelLawComesOutByItself()
    {
        static double ShareOfFloor(long income)
        {
            var bought = Spending.Wanted(Money.FromWhole(income), Floor, Price, Least, 20);
            var spent = Money.FromWhole(income).Exact * Spending.Spends / 100;

            return spent <= 0 ? 0 : Least.Exact * Price.Exact / spent;
        }

        var poor = ShareOfFloor(150);
        var middle = ShareOfFloor(500);
        var rich = ShareOfFloor(2000);

        Assert.True(poor > middle, "у бедного доля минимума не выше, чем у среднего");
        Assert.True(middle > rich, "у среднего доля минимума не выше, чем у богатого");
    }

    /// <summary>Ровный рост дохода даёт ровный рост спроса: скачкам взяться неоткуда.</summary>
    [Fact]
    public void SmoothIncomeGivesSmoothDemand()
    {
        var was = At(200);

        for (var step = 1; step <= 20; step++)
        {
            var now = At(200 + step * 10);

            Assert.True(now > was, $"спрос не пошёл за доходом на шаге {step}");
            Assert.True(now.Raw - was.Raw < Least.Raw, $"скачок спроса на шаге {step}");

            was = now;
        }
    }

    [Fact]
    public void NoIncomeMeansNoPurchase()
    {
        Assert.Equal(default, At(0));
    }
}
