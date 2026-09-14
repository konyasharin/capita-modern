namespace CapitaModern.Core.Economy;

/// <summary>Прожиточный минимум: сколько товара нужно на миллион человек за сутки.</summary>
/// <remarks>
/// Только минимум, и больше ничего. Сколько купят сверх него, решает доход, а не эта
/// ставка, — см. <see cref="Spending"/>: сперва минимум, затем доля свободных денег.
///
/// Прежде здесь была и сама нужда, и её зависимость от достатка: ставка умножалась на
/// «способность» в корзинах с упругостью по доходу. Из этого выходил спрос, который почти
/// не двигался — богатому доставалось чуть больше бедного, а лишние деньги оседали. Теперь
/// закон Энгеля получается сам собой из того, что минимум у всех один, а свободный доход
/// разный.
/// </remarks>
public sealed class Needs
{
    /// <summary>Обычная упругость по доходу, ×1 в сотых.</summary>
    public const int Scale = 100;

    private readonly IReadOnlyDictionary<GoodType, GoodAmount> _perMillion;
    private readonly IReadOnlyDictionary<GoodType, int> _byIncome;

    public Needs(
        IReadOnlyDictionary<GoodType, GoodAmount> perMillion,
        IReadOnlyDictionary<GoodType, int>? byIncome = null)
    {
        _perMillion = perMillion;
        _byIncome = byIncome ?? new Dictionary<GoodType, int>();
    }

    /// <summary>Что вообще потребляют и сколько по минимуму.</summary>
    public IReadOnlyDictionary<GoodType, GoodAmount> BaseRates => _perMillion;

    /// <summary>Насколько товар идёт за доходом, в сотых.</summary>
    /// <remarks>
    /// Минимум у всех один, а свободные деньги делятся по этой упругости: еда почти не
    /// идёт за доходом (богатый ест 3800 калорий против 2100), потребтовары идут быстрее
    /// дохода, услуги тоже.
    ///
    /// Без неё весь свободный доход уходил туда, где велик сам минимум, — а велик он в
    /// услугах, и люди просили их впятеро больше, чем мир способен дать.
    /// </remarks>
    public int ByIncome(GoodType good) => _byIncome.GetValueOrDefault(good, Scale);
}
