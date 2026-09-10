namespace CapitaModern.Core.Economy;

/// <summary>Насколько спрос отзывается на цену.</summary>
/// <remarks>
/// Без этого цена ни на что не влияет: подешевевший товар никто не станет брать больше,
/// а подорожавший — меньше. От этого же зависит, заработает ли курс валют: он двигает
/// местную цену импортного, и дальше должна отозваться заявка.
/// </remarks>
public sealed class Elasticity
{
    /// <summary>Значения хранятся в сотых: −40 означает «цена вдвое — берут на 40% меньше».</summary>
    public const int Scale = 100;

    /// <summary>Заявка не может ни исчезнуть совсем, ни вырасти больше чем вдвое:
    /// формула прямая, а вдали от обычной цены прямая врёт.</summary>
    private const int MinFactor = 10;
    private const int MaxFactor = 200;

    private readonly Dictionary<GoodType, int> _values = new();

    public Elasticity(IReadOnlyDictionary<GoodType, int>? values = null)
    {
        values ??= new Dictionary<GoodType, int>();
        foreach (var good in Enum.GetValues<GoodType>())
        {
            _values.Add(good, values.GetValueOrDefault(good));
        }
    }

    /// <summary>Отрицательная: дороже — берут меньше. Ноль означает «цену не замечают».</summary>
    public int Of(GoodType good) => _values[good];

    /// <summary>Сколько на самом деле закажут при такой цене.</summary>
    /// <param name="usual">С чем сравнивать — обычная цена товара.</param>
    public GoodAmount Adjust(GoodType good, GoodAmount wanted, Money price, Money usual)
    {
        if (usual.Raw <= 0 || wanted.Raw <= 0) return wanted;

        // Отклонение цены от обычной, в сотых: 100 означает «вдвое дороже».
        long off = price.Raw * Scale / usual.Raw - Scale;
        long factor = Math.Clamp(Scale + Of(good) * off / Scale, MinFactor, MaxFactor);

        return new GoodAmount(wanted.Raw * factor / Scale);
    }
}
