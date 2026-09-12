namespace CapitaModern.Core.Economy;

/// <summary>Кто у кого купил. Одна сделка между двумя странами.</summary>
public readonly record struct Deal(byte Buyer, byte Seller, GoodAmount Amount, Money Paid);

/// <summary>Заявка на парную торговлю.</summary>
/// <param name="Ask">За сколько продавец отдаёт единицу в мировой мере.</param>
/// <param name="Bid">Сколько покупатель готов заплатить за единицу с доставкой.</param>
/// <param name="Quality">Каков товар этой страны, в сотых. Сотня — как у передовой.</param>
public readonly record struct MarketOrder(
    byte Country,
    Producer Trader,
    GoodAmount Offer,
    GoodAmount Want,
    Money Ask,
    Money Bid,
    int Quality = Efficiency.Scale);

/// <summary>Парная торговля: продавец и покупатель встречаются, а не сваливают в котёл.</summary>
/// <remarks>
/// Общий котёл был удобен и неверен. В нём нет пути от одного к другому, поэтому нельзя
/// ни посчитать расстояние, ни перекрыть дорогу, ни отказаться покупать у конкретной
/// страны. Всё это — половина смысла торговли.
///
/// Покупатель берёт не всё у самого дешёвого, а смесь: доля продавца тем больше, чем он
/// дешевле с доставкой. Так и устроена настоящая торговля — немецкий станок и китайский
/// станок это разные товары, даже если у нас они оба зовутся Components. Пока брали
/// только у самого дешёвого, весь мировой ввоз доставался одной стране, а вывоз выходил
/// втрое меньше настоящего.
/// </remarks>
public sealed class Exchange
{
    /// <summary>Насколько покупатель разборчив: степень, с которой падает доля продавца
    /// по мере того, как он дороже. Четвёрка — середина оценок внешней торговли, они
    /// разбросаны от двух до восьми.</summary>
    public const int Choosiness = 4;

    /// <summary>Сколько поставщиков у одного покупателя по одному товару. В жизни ввоз
    /// идёт от горстки стран, а не от всех двухсот: у большинства товаров первая десятка
    /// поставщиков закрывает почти весь ввоз. Заодно это и цена тика — сделок иначе
    /// выходит вдесятеро больше.</summary>
    public const int MaxOrigins = 12;

    /// <summary>Насколько качество отстаёт от отрыва в умении, в сотых. Корень четвёртой
    /// степени: страна, которая производит вчетверо ловчее, делает товар вдвое лучше, а
    /// не вчетверо. Иначе передовые забирали бы весь рынок при любой цене.</summary>
    public const int QualityFromSkill = 25;

    /// <summary>Вес по отношению цен, посчитанный заранее. Степень считается долго, а
    /// отношение — целое от нуля до <see cref="Powers.Scale"/>: пар за тик выходит больше
    /// миллиона, и без таблицы тик уходил в шестьдесят миллисекунд.</summary>
    private static readonly long[] Weights = BuildWeights();

    private readonly List<int> _buyers = [];
    private readonly List<int> _sellers = [];
    private readonly List<int> _fit = [];

    /// <summary>Ставки и номера страны отдельным массивом: Span нельзя захватить в
    /// сравнение для сортировки.</summary>
    private long[] _bidOf = new long[256];
    private byte[] _whoOf = new byte[256];

    /// <summary>Цена с доставкой и вес каждого продавца для нынешнего покупателя.</summary>
    private long[] _askOf = new long[256];
    private long[] _qualityOf = new long[256];
    private long[] _keyOf = new long[256];

    /// <summary>Сравнение по заранее посчитанному ключу. Одно на всю жизнь обмена.</summary>
    private readonly Comparison<int> _byKey;

    public Exchange() =>
        _byKey = (a, b) => _keyOf[a] != _keyOf[b]
            ? _keyOf[b].CompareTo(_keyOf[a])
            : _whoOf[a] - _whoOf[b];
    private long[] _priceOf = new long[256];
    private long[] _weightOf = new long[256];
    private long[] _stockOf = new long[256];

    /// <summary>Сколько товара покупатели не взяли, потому что дешевле их собственной
    /// цены никто не привозит. Отсюда видно, дорога ли дорога.</summary>
    public GoodAmount Refused { get; private set; }

    /// <summary>Сколько не взяли потому, что ни у кого не осталось товара.</summary>
    public GoodAmount Empty { get; private set; }


    /// <summary>Сводит заявки по одному товару.</summary>
    /// <param name="markup">Во сколько сотых дороже обходится дорога от продавца к
    /// покупателю: перевозка плюс пошлина. Отрицательное значит, что пути нет вовсе.
    /// Наценкой только выбирают, у кого брать — продавцу достаётся его цена, а дорога
    /// сгорает топливом и уходит хозяевам звеньев.</param>
    /// <remarks>Наценка, а не готовая цена: заявку пришлось бы передавать целиком, а пар
    /// за тик больше миллиона, и копирование двух структур на каждую стоило трети тика.
    /// </remarks>
    /// <param name="onDeal">Что делать со сделкой: списать, довезти, заплатить за проход.</param>
    /// <returns>Сколько товара перешло из рук в руки.</returns>
    public GoodAmount Settle(
        Span<MarketOrder> orders,
        Func<byte, byte, int> markup,
        Action<Deal> onDeal)
    {
        _buyers.Clear();
        _sellers.Clear();
        for (var i = 0; i < orders.Length; i++)
        {
            // Одна страна бывает и тем и другим сразу: Германия и ввозит станки, и вывозит.
            if (orders[i].Want.Raw > 0) _buyers.Add(i);
            if (orders[i].Offer.Raw > 0) _sellers.Add(i);
        }

        if (_buyers.Count == 0 || _sellers.Count == 0) return default;

        Grow(orders.Length);

        for (var i = 0; i < orders.Length; i++)
        {
            _bidOf[i] = orders[i].Bid.Raw;
            _whoOf[i] = orders[i].Country;
        }

        // Кто больше даёт, тот и выбирает первым: так товар достаётся тому, кому он нужнее.
        // При равной цене — по номеру страны, чтобы тик оставался воспроизводимым.
        var bid = _bidOf;
        var who = _whoOf;
        _buyers.Sort((a, b) => bid[b] != bid[a] ? bid[b].CompareTo(bid[a]) : who[a] - who[b]);

        Span<long> left = stackalloc long[orders.Length];
        for (var i = 0; i < orders.Length; i++) left[i] = orders[i].Offer.Raw;

        // Продавцов сортируем по цене один раз на товар: покупатель идёт по ним снизу
        // вверх и обрывает перебор, как только цена перевалила за его потолок. Дорога
        // цену только поднимает, значит дальше смотреть нечего.
        var ask = _askOf;
        for (var i = 0; i < orders.Length; i++)
        {
            ask[i] = orders[i].Ask.Raw;

            // Поправка на качество не зависит от покупателя — считаем её раз на продавца.
            // Через пары она проходила сто двадцать тысяч раз за тик вместо двухсот.
            _qualityOf[i] = QualityFactor(orders[i].Quality);
        }

        _sellers.Sort((a, b) => ask[a] != ask[b] ? ask[a].CompareTo(ask[b]) : who[a] - who[b]);

        var traded = default(GoodAmount);
        foreach (var b in _buyers) traded += Serve(orders, b, left, markup, onDeal);

        return traded;
    }

    /// <summary>Развозит заявку одного покупателя по продавцам.</summary>
    private GoodAmount Serve(
        Span<MarketOrder> orders,
        int buyer,
        Span<long> left,
        Func<byte, byte, int> markup,
        Action<Deal> onDeal)
    {
        var want = orders[buyer].Want.Raw;
        var cheapest = long.MaxValue;

        var home = orders[buyer].Country;
        var ceiling = orders[buyer].Bid.Raw;
        _fit.Clear();

        foreach (var s in _sellers)
        {
            // Список отсортирован по цене: дальше все дороже потолка, смотреть нечего.
            if (orders[s].Ask.Raw > ceiling) break;
            if (left[s] <= 0 && orders[s].Country != home) continue;

            var road = markup(orders[s].Country, home);
            if (road < 0) continue;

            var price = orders[s].Ask.Raw * (TradeCosts.Scale + road) / TradeCosts.Scale;

            // Дороже своей цены покупатель не берёт.
            if (price <= 0 || price > ceiling) continue;

            // Сравнивают не цену, а цену за качество: немецкий станок берут не потому,
            // что он дешевле. Платят при этом полную цену — качество только выбирает.
            _priceOf[s] = _qualityOf[s] == Powers.Scale
                ? price
                : Math.Max(1, price * Powers.Scale / _qualityOf[s]);
            _fit.Add(s);
            if (_priceOf[s] < cheapest) cheapest = _priceOf[s];
        }

        if (_fit.Count == 0)
        {
            Refused += new GoodAmount(want);

            return default;
        }

        // Доля продавца падает степенью от того, во сколько раз он дороже самого дешёвого.
        // Считаем в long, а не в Int128: цена с запасом влезает, а деление Int128 идёт
        // программно и стоило половины всей торговли.
        foreach (var s in _fit)
        {
            var ratio = Ratio(cheapest, _priceOf[s]);
            _weightOf[s] = Weights[ratio < 0 ? 0 : ratio > Powers.Scale ? Powers.Scale : ratio];
        }

        // Оставляем тех, от кого и правда что-то придёт: дешевизна без товара бесполезна,
        // а хвост из сотни стран с долей в тысячную ничего не меняет, кроме времени тика.
        if (_fit.Count > MaxOrigins)
        {
            var weight = _weightOf;
            var stock = _stockOf;
            foreach (var s in _fit) stock[s] = orders[s].Country == home ? want : left[s];

            // Ключ считаем заранее, а сравнение берём готовое: замыкание на каждый вызов
            // выходило в тысячи объектов за тик.
            foreach (var s in _fit) _keyOf[s] = weight[s] * Math.Min(stock[s], want);

            _fit.Sort(_byKey);
            _fit.RemoveRange(MaxOrigins, _fit.Count - MaxOrigins);
        }

        // Весов не больше MaxOrigins, каждый не больше Powers.Scale — сумма в long влезает.
        var weights = 0L;
        foreach (var s in _fit) weights += _weightOf[s];

        if (weights == 0) return default;

        var taken = default(GoodAmount);

        // Первым проходом — по долям, вторым остаток тому, у кого товар ещё есть: у
        // кого-то доля больше его запаса, и без второго прохода она бы пропала.
        for (var pass = 0; pass < 2 && want > 0; pass++)
        {
            foreach (var s in _fit)
            {
                if (want <= 0) break;

                var share = pass == 0 ? Part(want, _weightOf[s], weights) : want;
                if (pass > 0 && orders[s].Country == home) continue;

                // Своя доля никуда не едет: этот кусок потребления страна закрывает сама,
                // и склад с деньгами тут не при чём. Считается она наравне с чужими, иначе
                // ввоз вышел бы стопроцентным у всех.
                if (orders[s].Country == home)
                {
                    var mine = Math.Min(share, want);
                    want -= mine;
                    continue;
                }

                if (left[s] <= 0) continue;

                var amount = Math.Min(Math.Min(share, left[s]), want);
                if (amount <= 0) continue;

                var cost = new Money((long)((Int128)orders[s].Ask.Raw * amount / GoodAmount.Scale));
                onDeal(new Deal(home, orders[s].Country, new GoodAmount(amount), cost));

                left[s] -= amount;
                want -= amount;
                taken += new GoodAmount(amount);
            }
        }

        if (want > 0) Empty += new GoodAmount(want);

        return taken;
    }

    /// <summary>Во сколько раз первый дешевле второго, в долях <see cref="Powers.Scale"/>.
    /// </summary>
    private static long Ratio(long cheapest, long price)
    {
        if (price <= 0) return 0;

        return cheapest <= long.MaxValue / Powers.Scale
            ? cheapest * Powers.Scale / price
            : (long)((Int128)cheapest * Powers.Scale / price);
    }

    /// <summary>Доля заявки, приходящаяся на этого продавца.</summary>
    private static long Part(long want, long weight, long weights)
    {
        if (weights <= 0) return 0;

        return weight == 0 || want <= long.MaxValue / weight
            ? want * weight / weights
            : (long)((Int128)want * weight / weights);
    }

    /// <summary>Во сколько раз товар этой страны лучше обычного, в долях
    /// <see cref="Powers.Scale"/>. На это и делится цена: сравнивают цену за качество, а
    /// не за штуку.</summary>
    private static long QualityFactor(int quality)
    {
        if (quality <= 0 || quality == Efficiency.Scale) return Powers.Scale;

        return Powers.PowCached((long)quality * Powers.Scale / Efficiency.Scale, QualityFromSkill);
    }

    private static long[] BuildWeights()
    {
        var table = new long[Powers.Scale + 1];
        for (var i = 0; i <= Powers.Scale; i++) table[i] = Powers.Pow(i, Choosiness * 100);

        return table;
    }

    private void Grow(int size)
    {
        if (_bidOf.Length >= size) return;

        _bidOf = new long[size];
        _whoOf = new byte[size];
        _askOf = new long[size];
        _qualityOf = new long[size];
        _keyOf = new long[size];
        _priceOf = new long[size];
        _weightOf = new long[size];
        _stockOf = new long[size];
    }
}
