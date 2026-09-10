namespace CapitaModern.Core.Economy;

/// <summary>Займы страны. Список, а не сумма: иначе не перезанять дорогое, не посчитать
/// плавающие и не объявить дефолт только по внешнему.</summary>
public sealed class Debt
{
    private readonly List<Loan> _loans = [];
    private int _nextId = 1;

    public IReadOnlyList<Loan> Loans => _loans;

    public Loan Take(LoanSource source, byte? lender, Money principal, RateKind rateKind, int rate)
    {
        var loan = new Loan(_nextId++, source, lender, principal, rateKind, rate);
        _loans.Add(loan);

        return loan;
    }

    /// <summary>Сколько должны, целиком или по одному источнику.</summary>
    public Money Owed(LoanSource? source = null)
    {
        var total = default(Money);
        foreach (var loan in _loans)
        {
            if (source is null || loan.Source == source) total += loan.Principal;
        }

        return total;
    }

    /// <summary>Долговая нагрузка в сотых: 200 означает «внешний долг равен двум годовым
    /// экспортам». За этой чертой в жизни начинается зона риска.</summary>
    public int BurdenToExports(Money yearlyExports)
    {
        if (yearlyExports.Raw <= 0) return Owed(LoanSource.Foreign).Raw > 0 ? int.MaxValue : 0;

        var burden = (Int128)Owed(LoanSource.Foreign).Raw * 100 / yearlyExports.Raw;

        return burden > int.MaxValue ? int.MaxValue : (int)burden;
    }

    /// <summary>Самый дорогой заём — его и надо гасить первым.</summary>
    public Loan? Priciest(int lenderKeyRate)
    {
        Loan? worst = null;
        foreach (var loan in _loans)
        {
            if (loan.Principal.Raw > 0 && (worst is null || loan.RateAt(lenderKeyRate) > worst.RateAt(lenderKeyRate)))
                worst = loan;
        }

        return worst;
    }

    /// <summary>Списывает долг по одному источнику и говорит, сколько списано кому.
    /// Кредиторы теряют ровно эти деньги — они уже у должника.</summary>
    public List<(byte? Lender, Money Lost)> Default(LoanSource source)
    {
        var lost = new List<(byte?, Money)>();
        foreach (var loan in _loans)
        {
            if (loan.Source != source || loan.Principal.Raw == 0) continue;

            lost.Add((loan.Lender, loan.Principal));
            loan.Repay(loan.Principal);
        }

        Forget();

        return lost;
    }

    /// <summary>Погашенные займы не должны копиться в списке до конца партии.</summary>
    public void Forget() => _loans.RemoveAll(loan => loan.Principal.Raw == 0);
}
