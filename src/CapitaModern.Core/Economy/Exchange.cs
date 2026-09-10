namespace CapitaModern.Core.Economy;

/// <summary>Кто у кого купил. Одна сделка между двумя странами.</summary>
public readonly record struct Deal(byte Buyer, byte Seller, GoodAmount Amount, Money Paid);

/// <summary>Заявка на парную торговлю.</summary>
/// <param name="Ask">За сколько продавец отдаёт единицу в мировой мере.</param>
/// <param name="Bid">Сколько покупатель готов заплатить за единицу с доставкой.</param>
public readonly record struct MarketOrder(
    byte Country,
    Producer Trader,
    GoodAmount Offer,
    GoodAmount Want,
    Money Ask,
    Money Bid);

/// <summary>Парная торговля: продавец и покупатель встречаются, а не сваливают в котёл.</summary>
/// <remarks>
/// Общий котёл был удобен и неверен. В нём нет пути от одного к другому, поэтому нельзя
/// ни посчитать расстояние, ни перекрыть дорогу, ни отказаться покупать у конкретной
/// страны. Всё это — половина смысла торговли.
///
/// Сводится так: покупатели идут от того, кто больше даёт, и каждый берёт у самого
/// дешёвого с доставкой. Дороже своей готовности платить не берёт никто — оттуда и
/// появляется отказ от дальнего дешёвого товара в пользу ближнего дорогого.
/// </remarks>
public sealed class Exchange
{
    private readonly List<int> _buyers = [];
    private readonly List<int> _sellers = [];
    private readonly List<byte> _tolls = [];

    /// <summary>Ставки и номера страны отдельным массивом: Span нельзя захватить в
    /// сравнение для сортировки.</summary>
    private long[] _bidOf = new long[256];
    private byte[] _whoOf = new byte[256];

    /// <summary>Сколько товара покупатели не взяли, потому что дешевле их собственной
    /// цены никто не привозит. Отсюда видно, дорога ли дорога.</summary>
    public GoodAmount Refused { get; private set; }

    /// <summary>Сколько не взяли потому, что ни у кого не осталось товара.</summary>
    public GoodAmount Empty { get; private set; }

    /// <summary>Сводит заявки по одному товару.</summary>
    /// <param name="delivered">Во что обойдётся единица от продавца покупателю: цена
    /// продавца плюс дорога и пошлина. Ею только выбирают, у кого брать — продавцу
    /// достаётся его цена, а дорога сгорает топливом и уходит хозяевам звеньев.</param>
    /// <param name="onDeal">Что делать со сделкой: списать, довезти, заплатить за проход.</param>
    /// <returns>Сколько товара перешло из рук в руки.</returns>
    public GoodAmount Settle(
        Span<MarketOrder> orders,
        Func<MarketOrder, MarketOrder, Money> delivered,
        Action<Deal> onDeal)
    {
        _buyers.Clear();
        _sellers.Clear();
        for (var i = 0; i < orders.Length; i++)
        {
            if (orders[i].Want.Raw > 0) _buyers.Add(i);
            else if (orders[i].Offer.Raw > 0) _sellers.Add(i);
        }

        if (_buyers.Count == 0 || _sellers.Count == 0) return default;

        if (_bidOf.Length < orders.Length)
        {
            _bidOf = new long[orders.Length];
            _whoOf = new byte[orders.Length];
        }

        for (var i = 0; i < orders.Length; i++)
        {
            _bidOf[i] = orders[i].Bid.Raw;
            _whoOf[i] = orders[i].Country;
        }

        // Кто больше даёт, тот и берёт первым: так товар достаётся тому, кому он нужнее.
        // При равной цене — по номеру страны, чтобы тик оставался воспроизводимым.
        var bid = _bidOf;
        var who = _whoOf;
        _buyers.Sort((a, b) => bid[b] != bid[a] ? bid[b].CompareTo(bid[a]) : who[a] - who[b]);

        Span<long> left = stackalloc long[orders.Length];
        for (var i = 0; i < orders.Length; i++) left[i] = orders[i].Offer.Raw;

        var traded = default(GoodAmount);
        foreach (var b in _buyers)
        {
            var want = orders[b].Want.Raw;

            while (want > 0)
            {
                var best = -1;
                var bestPrice = default(Money);
                foreach (var s in _sellers)
                {
                    if (left[s] <= 0) continue;

                    var price = delivered(orders[s], orders[b]);
                    if (price.Raw <= 0) continue;
                    if (best >= 0 && price >= bestPrice) continue;

                    best = s;
                    bestPrice = price;
                }

                // Ближе и дешевле никого: остальное покупатель не берёт.
                if (best < 0)
                {
                    Empty += new GoodAmount(want);
                    break;
                }

                if (bestPrice > orders[b].Bid)
                {
                    Refused += new GoodAmount(want);
                    break;
                }

                var amount = Math.Min(want, left[best]);
                var cost = new Money((long)((Int128)orders[best].Ask.Raw * amount / GoodAmount.Scale));

                var deal = new Deal(orders[b].Country, orders[best].Country, new GoodAmount(amount), cost);
                onDeal(deal);

                left[best] -= amount;
                want -= amount;
                traded += new GoodAmount(amount);
            }
        }

        return traded;
    }

    /// <summary>Список для сбора хозяев звеньев. Живёт на бирже, чтобы не сорить мусором
    /// на каждой сделке: сделок за тик тысячи.</summary>
    public List<byte> TollBuffer => _tolls;
}
