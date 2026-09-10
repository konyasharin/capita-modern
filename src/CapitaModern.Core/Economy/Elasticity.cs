namespace CapitaModern.Core.Economy;

/// <summary>Насколько спрос и предложение отзываются на цену.</summary>
/// <remarks>
/// Без этого цена ни на что не влияет, а курс валют не работает вовсе. Нужны обе
/// стороны: если отзывается только спрос, страна с дефицитом может закрыть его лишь
/// обнулив ввоз, и курс уезжает до упора вместо того, чтобы найти равновесие.
/// </remarks>
public sealed class Elasticity
{
    /// <summary>Значения хранятся в сотых: −40 означает «цена вдвое — берут на 40% меньше».</summary>
    public const int Scale = 100;

    /// <summary>Количество не может ни исчезнуть совсем, ни вырасти больше чем вдвое:
    /// формула прямая, а вдали от обычной цены прямая врёт.</summary>
    public const int MinFactor = 10;
    public const int MaxFactor = 200;

    /// <summary>Норму запаса можно двигать только в полтора раза в обе стороны.</summary>
    /// <remarks>На запасе в четыре дня вместо сорока завод просто встанет, а цена от
    /// низкого покрытия полезет вверх и уронит норму ещё сильнее. Это раскручивается,
    /// а не сходится, поэтому здесь коридор куда уже обычного.</remarks>
    public const int MinStockFactor = 50;
    public const int MaxStockFactor = 150;

    private readonly Dictionary<GoodType, int> _demand = new();
    private readonly Dictionary<GoodType, int> _supply = new();

    public Elasticity(
        IReadOnlyDictionary<GoodType, int>? demand = null,
        IReadOnlyDictionary<GoodType, int>? supply = null)
    {
        demand ??= new Dictionary<GoodType, int>();
        supply ??= new Dictionary<GoodType, int>();
        foreach (var good in Enum.GetValues<GoodType>())
        {
            _demand.Add(good, demand.GetValueOrDefault(good));
            _supply.Add(good, supply.GetValueOrDefault(good));
        }
    }

    /// <summary>Отрицательная: дороже — берут меньше. Ноль означает «цену не замечают».</summary>
    public int Demand(GoodType good) => _demand[good];

    /// <summary>Положительная: дороже — продают больше, даже залезая в свой запас.</summary>
    public int Supply(GoodType good) => _supply[good];

    /// <summary>Сколько на самом деле закажут или предложат при такой цене.</summary>
    /// <param name="elasticity">Из <see cref="Demand"/> или <see cref="Supply"/>.</param>
    /// <param name="usual">С чем сравнивать — обычная цена товара.</param>
    public static GoodAmount Adjust(
        int elasticity,
        GoodAmount amount,
        Money price,
        Money usual,
        int minFactor = MinFactor,
        int maxFactor = MaxFactor)
    {
        if (usual.Raw <= 0 || amount.Raw <= 0) return amount;

        // Отклонение цены от обычной, в сотых: 100 означает «вдвое дороже».
        long off = price.Raw * Scale / usual.Raw - Scale;
        long factor = Math.Clamp(Scale + elasticity * off / Scale, minFactor, maxFactor);

        return new GoodAmount(amount.Raw * factor / Scale);
    }
}
