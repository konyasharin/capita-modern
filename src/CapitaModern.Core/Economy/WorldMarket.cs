namespace CapitaModern.Core.Economy;

/// <summary>Один рынок на товар: кто в излишке — предлагает, кто в нехватке — просит,
/// цена одна.</summary>
/// <remarks>
/// Перебирать пары стран — 200 × 200 × 31 проверок за тик, это не пройдёт по времени.
/// Но дело не только в скорости: закон одной цены для торгуемых товаров так и работает,
/// а из рынка бесплатно выходит эмбарго — «страну отрезали по товару X».
///
/// Разные цены в странах при этом сохраняются: сделки идут по цене рынка, а местная цена
/// продавца продолжает двигаться от своего покрытия. Кто не смог закупиться, у того
/// покрытие осталось низким, а цена высокой.
/// </remarks>
public sealed class WorldMarket
{
    /// <summary>Сколько раз перераздавать остаток от тех, кому не хватило денег.</summary>
    private const int MoneyPasses = 3;

    /// <summary>Чья валюта приходит за экспорт. Ничья: мировая единица — мера, а не
    /// валюта страны. Какая валюта станет резервной, пусть решится само.</summary>
    public const byte WorldIssuer = 0;

    public Prices Prices { get; }

    public WorldMarket(Prices prices)
    {
        Prices = prices;
    }

    /// <summary>Сводит заявки и предложения по товару и двигает цену рынка.</summary>
    /// <returns>Сколько товара перешло из рук в руки.</returns>
    public GoodAmount Settle(GoodType good, Span<TradeOrder> orders)
    {
        var wanted = default(GoodAmount);
        var offered = default(GoodAmount);
        foreach (var order in orders)
        {
            wanted += order.Bid;
            offered += order.Offer;
        }

        // Цена двигается в любом случае: односторонний рынок — это тоже новость.
        Prices.MoveFromBalance(good, wanted, offered);
        if (wanted.Raw == 0 || offered.Raw == 0) return default;

        Span<long> bought = stackalloc long[orders.Length];
        var total = Buy(good, orders, bought, wanted.Raw, Math.Min(wanted.Raw, offered.Raw));
        if (total == 0) return default;

        var pot = Pay(good, orders, bought);
        Sell(good, orders, offered.Raw, total, pot);

        return new GoodAmount(total);
    }

    /// <summary>Раздаёт товар покупателям по доле заявки, урезая тех, кому не хватает
    /// денег. Их недобор следующим проходом уходит остальным.</summary>
    private long Buy(GoodType good, Span<TradeOrder> orders, Span<long> bought, long wanted, long tradable)
    {
        long left = tradable;
        long share = wanted;
        var total = 0L;

        for (var pass = 0; pass < MoneyPasses && left > 0 && share > 0; pass++)
        {
            var stillHungry = 0L;
            var taken = 0L;

            for (var i = 0; i < orders.Length; i++)
            {
                long rest = orders[i].Bid.Raw - bought[i];
                if (rest <= 0) continue;

                // left не уменьшается внутри прохода, иначе доли зависели бы от порядка.
                long want = Math.Min((long)((Int128)left * rest / share), rest);
                long gets = Math.Min(want, Affordable(good, orders[i].Trader, bought[i]));

                bought[i] += gets;
                taken += gets;

                // Урезали деньгами — в следующем проходе этот покупатель уже не участвует.
                if (gets == want) stillHungry += rest - gets;
            }

            if (taken == 0) break;

            total += taken;
            left -= taken;
            share = stillHungry;
        }

        return total;
    }

    /// <summary>Сколько ещё товара покупатель потянет сверх уже набранного.</summary>
    private long Affordable(GoodType good, Producer trader, long alreadyBought)
    {
        long price = Prices.Of(good).Raw;
        long rest = trader.Treasury.Reserves.Liquid.Raw - Prices.CostOf(good, new GoodAmount(alreadyBought)).Raw;

        return rest <= 0 ? 0 : (long)((Int128)rest * GoodAmount.Scale / price);
    }

    /// <summary>Списывает деньги с покупателей, выдаёт им товар и возвращает, сколько
    /// денег собралось.</summary>
    private Money Pay(GoodType good, Span<TradeOrder> orders, Span<long> bought)
    {
        var pot = default(Money);
        for (var i = 0; i < orders.Length; i++)
        {
            if (bought[i] == 0) continue;

            var cost = Prices.CostOf(good, new GoodAmount(bought[i]));
            if (!orders[i].Trader.Treasury.Reserves.TrySpend(cost))
                throw new InvalidOperationException("Покупателю не хватило денег, ошибка в расчёте доли");

            orders[i].Trader.Stock.Store(good, new GoodAmount(bought[i]));
            pot += cost;
        }

        return pot;
    }

    /// <summary>Забирает товар у продавцов и раздаёт им ровно ту сумму, что собрали
    /// покупатели.</summary>
    /// <remarks>Остатки от целочисленного деления разносятся по единице: иначе и товар,
    /// и деньги потихоньку исчезали бы из мира.</remarks>
    private void Sell(GoodType good, Span<TradeOrder> orders, long offered, long total, Money pot)
    {
        Span<long> sold = stackalloc long[orders.Length];
        long goodsLeft = total;
        for (var i = 0; i < orders.Length; i++)
        {
            if (orders[i].Offer.Raw == 0) continue;

            sold[i] = (long)((Int128)total * orders[i].Offer.Raw / offered);
            goodsLeft -= sold[i];
        }

        // Недостача меньше числа продавцов: каждое деление теряет меньше единицы.
        while (goodsLeft > 0)
        {
            var given = false;
            for (var i = 0; i < orders.Length && goodsLeft > 0; i++)
            {
                if (sold[i] >= orders[i].Offer.Raw) continue;

                sold[i]++;
                goodsLeft--;
                given = true;
            }

            if (!given) break;
        }

        Span<long> earned = stackalloc long[orders.Length];
        long moneyLeft = pot.Raw;
        for (var i = 0; i < orders.Length; i++)
        {
            if (sold[i] == 0) continue;

            earned[i] = (long)((Int128)pot.Raw * sold[i] / total);
            moneyLeft -= earned[i];
        }

        while (moneyLeft > 0)
        {
            var given = false;
            for (var i = 0; i < orders.Length && moneyLeft > 0; i++)
            {
                if (sold[i] == 0) continue;

                earned[i]++;
                moneyLeft--;
                given = true;
            }

            if (!given) break;
        }

        for (var i = 0; i < orders.Length; i++)
        {
            if (sold[i] == 0) continue;

            if (orders[i].Trader.Stock.TakeUpTo(good, new GoodAmount(sold[i])).Raw != sold[i])
                throw new InvalidOperationException("У продавца не оказалось товара, ошибка в расчёте доли");

            // Выручка ложится туда, где продавец решил держать резервы. Там её и
            // заморозят, если дойдёт до санкций.
            var trader = orders[i].Trader;
            trader.Treasury.Reserves.Add(
                Reserves.Incoming((byte)trader.Id, trader.Custody, new Money(earned[i])));
        }
    }
}
