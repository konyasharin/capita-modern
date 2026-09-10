namespace CapitaModern.Core.Economy;

/// <summary>Один заём. Долг хранится списком, а не суммой, ради трёх вещей: перезанять
/// дорогое, посчитать плавающие при смене ключевой ставки и объявить дефолт только по
/// внешнему.</summary>
/// <param name="Rate">Сотые доли процента годовых у <see cref="RateKind.Fixed"/>,
/// надбавка к ключевой ставке кредитора у <see cref="RateKind.Floating"/>.</param>
public sealed class Loan
{
    public int Id { get; }
    public LoanSource Source { get; }

    /// <summary>Кто дал. Пусто у внутреннего рынка: там кредитор не страна.</summary>
    public byte? Lender { get; }

    public RateKind RateKind { get; }
    public int Rate { get; }

    /// <summary>Сколько ещё должны по телу займа.</summary>
    public Money Principal { get; private set; }

    public Loan(int id, LoanSource source, byte? lender, Money principal, RateKind rateKind, int rate)
    {
        if (principal < default(Money)) throw new ArgumentOutOfRangeException(nameof(principal));

        Id = id;
        Source = source;
        Lender = lender;
        Principal = principal;
        RateKind = rateKind;
        Rate = rate;
    }

    /// <summary>Гасит сколько получится и говорит, сколько ушло.</summary>
    public Money Repay(Money amount)
    {
        var paid = amount < Principal ? amount : Principal;
        Principal -= paid;

        return paid;
    }

    /// <summary>Нечем заплатить проценты — они уходят в тело займа. Долг начинает расти
    /// сам собой, и нагрузка вместе с ним; так и подходят к дефолту.</summary>
    public void Capitalise(Money interest)
    {
        if (interest < default(Money)) throw new ArgumentOutOfRangeException(nameof(interest));

        Principal += interest;
    }

    /// <summary>Ставка в сотых долях процента годовых.</summary>
    /// <param name="lenderKeyRate">Ключевая ставка кредитора; для фиксированных не важна.</param>
    public int RateAt(int lenderKeyRate) => RateKind == RateKind.Fixed ? Rate : lenderKeyRate + Rate;

    /// <summary>Проценты за сутки. Год — 365 тиков, ставка годовая.</summary>
    public Money InterestPerTick(int lenderKeyRate) =>
        new(Principal.Raw * RateAt(lenderKeyRate) / (100 * 100 * 365));
}
