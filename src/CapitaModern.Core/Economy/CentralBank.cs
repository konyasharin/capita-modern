namespace CapitaModern.Core.Economy;

/// <summary>Печатный станок. Последний выход, когда занять не вышло, а платить надо.</summary>
/// <remarks>
/// Работает и имеет цену. Цена — количественная теория в самом грубом виде: сколько
/// денег добавили к массе, на столько же и подорожало. Для крупной эмиссии она как раз
/// верна, а мелкую всё равно съедает шум.
///
/// Настоящие случаи, по которым это проверять: Венесуэла, Зимбабве, Турция.
/// </remarks>
public sealed class CentralBank
{
    /// <summary>Сколько всего местных денег в стране. От неё считается цена печати:
    /// добавить триллион к десяти — это десять процентов, к тысяче — ничего.</summary>
    public Money Supply { get; private set; }

    /// <summary>Сколько напечатано за всю партию. Для показа и для доверия.</summary>
    public Money Printed { get; private set; }

    /// <summary>Каким способом печатали в последний раз.</summary>
    public EmissionKind LastKind { get; private set; }

    public CentralBank(Money supply)
    {
        if (supply < default(Money)) throw new ArgumentOutOfRangeException(nameof(supply));

        Supply = supply;
    }

    /// <summary>Создаёт деньги и говорит, на сколько сотых выросла масса.</summary>
    /// <returns>Рост в сотых: 5 означает, что всё подорожает на пять процентов.</returns>
    public int Emit(Money amount, EmissionKind kind)
    {
        if (amount < default(Money)) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount.Raw == 0) return 0;

        var before = Supply;
        Supply += amount;
        Printed += amount;
        LastKind = kind;

        if (before.Raw <= 0) return 100;

        var growth = (Int128)amount.Raw * 100 / before.Raw;

        return growth > int.MaxValue ? int.MaxValue : (int)growth;
    }
}
