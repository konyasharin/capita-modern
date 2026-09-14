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
    private readonly IReadOnlyDictionary<GoodType, GoodAmount> _perMillion;

    public Needs(IReadOnlyDictionary<GoodType, GoodAmount> perMillion) => _perMillion = perMillion;

    /// <summary>Что вообще потребляют и сколько по минимуму.</summary>
    public IReadOnlyDictionary<GoodType, GoodAmount> BaseRates => _perMillion;
}
