namespace CapitaModern.Core.Economy;

/// <summary>Откуда бюджету пришло за тик.</summary>
public enum TaxKind
{
    Vat,
    Income,
    Payroll,
    Profit,
    Extraction,
    Excise,
    Tariff,
}

/// <summary>Государственный бюджет: налоги приходят, расходы уходят.</summary>
/// <remarks>
/// Это не касса продавца — та лежит в <see cref="Treasury"/> и держит оборот всей
/// экономики. Бюджет живёт отдельно и наполняется только налогами, как и положено.
///
/// Без него у государства не было дохода вовсе, и «казна» страны обнулялась за несколько
/// месяцев: платить зарплаты было чем, а собирать — нечем.
/// </remarks>
public sealed class Budget
{
    private readonly Money[] _income = new Money[Enum.GetValues<TaxKind>().Length];

    /// <summary>Остаток. Может быть отрицательным: дефицит — это норма, а не ошибка.</summary>
    public Money Balance { get; private set; }

    /// <summary>Сколько потрачено за тик.</summary>
    public Money Spent { get; private set; }

    /// <summary>Сколько собрано за тик по каждому налогу.</summary>
    public Money IncomeFrom(TaxKind kind) => _income[(int)kind];

    /// <summary>Всё собранное за тик.</summary>
    public Money Collected
    {
        get
        {
            var total = default(Money);
            foreach (var part in _income) total += part;

            return total;
        }
    }

    public void Collect(TaxKind kind, Money amount)
    {
        if (amount.Raw <= 0) return;

        _income[(int)kind] += amount;
        Balance += amount;
    }

    /// <summary>Тратит сколько получится и говорит, сколько ушло.</summary>
    public Money SpendUpTo(Money wanted)
    {
        if (wanted.Raw <= 0 || Balance.Raw <= 0) return default;

        var paid = wanted < Balance ? wanted : Balance;
        Balance -= paid;
        Spent += paid;

        return paid;
    }

    /// <summary>Счётчики живут один тик: остаток остаётся, потоки обнуляются.</summary>
    public void NewTick()
    {
        Array.Clear(_income);
        Spent = default;
    }
}
