namespace CapitaModern.Core.Economy;

/// <summary>Сколько товара нужно населению. Зависит от дохода, а не только от числа душ.</summary>
/// <remarks>
/// Пока ставка была одна на всех, американец и эфиоп потребляли поровну. Из-за этого
/// богатой стране нечего было хотеть: профицит не рассасывался, а курс упирался в пол
/// коридора. Разрыв в жизни тридцатикратный.
///
/// Способность считается в корзинах, а не в деньгах: сколько базовых наборов покупает
/// дневной заработок. Так не нужен курс — сравнивать доходы через него сейчас нельзя, он
/// сам сломан, и вышел бы круг.
/// </remarks>
public sealed class Needs
{
    /// <summary>Способность в сотых: 100 — заработка ровно на одну корзину.</summary>
    public const int Scale = 100;

    /// <summary>Ниже обычного доход нужду не опускает.</summary>
    /// <remarks>
    /// Богатый хочет больше — это Энгель. Бедный хочет столько же, просто не может
    /// купить, и это уже считается деньгами в кошельке. Резать его ещё и здесь значило бы
    /// посчитать бедность дважды, а заодно скрыть голод: спрос бы съёжился под покупку, и
    /// нехватки будто бы не было.
    /// </remarks>
    public const int MinCapacity = Scale;

    /// <summary>Выше этого данных всё равно нет, а прямая уже врёт.</summary>
    public const int MaxCapacity = 5000;

    private readonly IReadOnlyDictionary<GoodType, GoodAmount> _perMillion;
    private readonly IReadOnlyDictionary<GoodType, int> _elasticity;

    public Needs(
        IReadOnlyDictionary<GoodType, GoodAmount> perMillion,
        IReadOnlyDictionary<GoodType, int>? elasticity = null)
    {
        _perMillion = perMillion;
        _elasticity = elasticity ?? new Dictionary<GoodType, int>();
    }

    /// <summary>Что вообще потребляют. Ставка при обычном доходе.</summary>
    public IReadOnlyDictionary<GoodType, GoodAmount> BaseRates => _perMillion;

    /// <summary>Насколько товар идёт за доходом, в сотых.</summary>
    public int ElasticityOf(GoodType good) => _elasticity.GetValueOrDefault(good, Scale);

    /// <summary>Сколько нужно за сутки на миллион человек при такой зажиточности.</summary>
    /// <param name="capacity">Сколько базовых корзин покупает дневной доход, в сотых.</param>
    public GoodAmount PerMillion(GoodType good, int capacity)
    {
        var rate = _perMillion.GetValueOrDefault(good);
        if (rate.Raw == 0) return default;

        var eased = Math.Clamp(capacity, MinCapacity, MaxCapacity);
        if (eased == Scale) return rate;

        var factor = Powers.Pow(eased * Powers.Scale / Scale, ElasticityOf(good));

        return new GoodAmount(rate.Raw * factor / Powers.Scale);
    }
}
