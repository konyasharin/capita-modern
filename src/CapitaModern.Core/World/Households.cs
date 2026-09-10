using CapitaModern.Core.Economy;

namespace CapitaModern.Core.World;

/// <summary>Деньги населения страны.</summary>
/// <remarks>
/// Без них внутри страны денег не существует: производство идёт само собой, а еда берётся
/// со склада даром. С ними появляется ограничение, ради которого весь шаг и делается —
/// купить можно только на своё, и уровень жизни становится следствием, а не показателем.
/// </remarks>
public sealed class Households
{
    public Money Savings { get; private set; }

    public Households(Money savings = default)
    {
        if (savings < default(Money)) throw new ArgumentOutOfRangeException(nameof(savings));

        Savings = savings;
    }

    public void Earn(Money wages)
    {
        if (wages < default(Money)) throw new ArgumentOutOfRangeException(nameof(wages));

        Savings += wages;
    }

    /// <summary>Тратит сколько получится и говорит, сколько удалось. Недостача — не
    /// ошибка, а то, ради чего всё и заводилось.</summary>
    public Money SpendUpTo(Money wanted)
    {
        if (wanted < default(Money)) throw new ArgumentOutOfRangeException(nameof(wanted));

        var spent = wanted < Savings ? wanted : Savings;
        Savings -= spent;

        return spent;
    }
}
