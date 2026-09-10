namespace CapitaModern.Core.Economy;

/// <summary>Цены продавца за единицу товара. Дорожает то, чего мало на складе.</summary>
/// <remarks>
/// Цены принадлежат продавцу, а не стране: когда появятся компании, они разойдутся сами.
/// Это средняя цена, по которой платит вся промышленность страны, а не биржевая
/// котировка — потому она и ползёт, а не прыгает. Резкое приходит из <see cref="Shock"/>.
/// </remarks>
public sealed class Prices
{
    /// <summary>Потолок движения за тик, в процентах. Тик — сутки, так что 2% это ×1380
    /// за год: быстрее любого настоящего кризиса.</summary>
    public const int StepPercent = 2;

    /// <summary>Нормальный запас в сутках. В жизни запасы обрабатывающей промышленности
    /// держатся около полутора месяцев продаж.</summary>
    public const int TargetCoverDays = 40;

    /// <summary>Во сколько раз цена может уйти от стартовой в любую сторону.</summary>
    /// <remarks>
    /// Подпорка, а не экономика. Товар, которого в стране нет совсем, сейчас дорожает
    /// полным шагом вечно: купить его негде — торговли нет, — а отказаться от него
    /// заводы не умеют — цену они не видят. За пять лет такая цена переполнит long.
    /// Худшие настоящие эпизоды — это разы, максимум десятки раз, так что за сотней
    /// начинается не сигнал, а дыра в модели. Уйдёт, когда появятся торговля и спрос,
    /// который смотрит на цену.
    /// </remarks>
    public const int MaxSwingTimes = 100;

    /// <summary>Ниже цена не опускается ни при каком старте: ноль — состояние без выхода,
    /// из него товар уже никогда не подорожает настолько, чтобы его стали делать.</summary>
    public static readonly Money Floor = new(1);

    /// <summary>Цена товара, которого нет в стартовых данных.</summary>
    public static readonly Money Default = Money.FromWhole(1);

    private readonly Dictionary<GoodType, Money> _values = new();
    private readonly Dictionary<GoodType, Money> _start = new();

    public Prices(IReadOnlyDictionary<GoodType, Money>? startPrices = null)
    {
        startPrices ??= new Dictionary<GoodType, Money>();
        foreach (var good in Enum.GetValues<GoodType>())
        {
            var price = startPrices.GetValueOrDefault(good, Default);
            _values.Add(good, price);
            _start.Add(good, price);
        }
    }

    public Money Of(GoodType good) => _values[good];

    /// <summary>С чего цена начинала. Нужна как база: и для коридора, и потом для
    /// индекса цен.</summary>
    public Money StartOf(GoodType good) => _start[good];

    public void Set(GoodType good, Money price)
    {
        if (price < Floor) throw new ArgumentOutOfRangeException(nameof(price));

        _values[good] = price;
    }

    /// <summary>Сколько стоит такая партия товара.</summary>
    public Money CostOf(GoodType good, GoodAmount amount) =>
        new((long)((Int128)Of(good).Raw * amount.Raw / GoodAmount.Scale));

    /// <summary>Сдвигает цену за тик по тому, на сколько суток хватит запаса.</summary>
    /// <remarks>
    /// Сравнивается покрытие, а не склад со спросом напрямую: склад — это запас, спрос —
    /// расход за сутки. Делить одно на другое можно, только приведя к суткам, иначе цену
    /// задавал бы размер складов, а не нехватка.
    /// </remarks>
    /// <param name="demand">Сколько товара заказали за тик все — и заводы, и население.</param>
    /// <param name="available">Что лежало на складе на начало тика.</param>
    public void Move(GoodType good, GoodAmount demand, GoodAmount available)
    {
        // перекос = (цель − покрытие) / (цель + покрытие), где покрытие = наличие / спрос.
        // Числитель и знаменатель домножены на спрос, чтобы обойтись без дроби.
        long target = TargetCoverDays * demand.Raw;
        long over = target + available.Raw;
        if (over == 0) return; // ни спроса, ни запаса — двигать не от чего

        // Спроса нет вовсе: цель ноль, перекос ровно −1, цена падает полным шагом.
        long under = target - available.Raw;

        // Int128: цена на процент на перекос не влезает в long у дорогих товаров.
        // Деление обрубает дробь в обе стороны одинаково, так что цена не сползает сама.
        long raw = Of(good).Raw;
        long delta = (long)((Int128)raw * StepPercent * under / ((Int128)over * 100));

        // Дешёвый товар иначе застревает навсегда: два процента от копейки — ноль,
        // и цена больше не сдвинется ни вниз, ни обратно вверх.
        if (delta == 0 && under != 0) delta = under > 0 ? 1 : -1;

        _values[good] = Clamped(good, raw + delta);
    }

    /// <summary>Разовый сдвиг от события: эмбарго, удар по заводу, паника.</summary>
    /// <remarks>В жизни цена улетает от новости, а не оттого, что склад просел на процент.
    /// Всё резкое идёт отсюда, а <see cref="Move"/> только сползает к равновесию.</remarks>
    public void Shock(GoodType good, int percent)
    {
        long raw = Of(good).Raw;

        _values[good] = Clamped(good, raw + raw * percent / 100);
    }

    /// <summary>Держит цену в коридоре вокруг стартовой.</summary>
    private Money Clamped(GoodType good, long raw)
    {
        long start = _start[good].Raw;

        return new Money(Math.Clamp(raw, Math.Max(start / MaxSwingTimes, Floor.Raw), start * MaxSwingTimes));
    }
}
