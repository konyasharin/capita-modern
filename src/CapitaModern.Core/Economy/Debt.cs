namespace CapitaModern.Core.Economy;

/// <summary>Займы страны. Список, а не сумма: иначе не перезанять дорогое, не посчитать
/// плавающие и не объявить дефолт только по внешнему.</summary>
public sealed class Debt
{
    private readonly List<Loan> _loans = [];
    private int _nextId = 1;

    public IReadOnlyList<Loan> Loans => _loans;

    /// <summary>Занимает. Заём на тех же условиях у того же кредитора доливается в
    /// прежний, а не заводит новый: иначе за год их набегают десятки тысяч, и ни
    /// показать игроку, ни перебрать.</summary>
    public Loan Take(LoanSource source, byte? lender, Money principal, RateKind rateKind, int rate)
    {
        foreach (var same in _loans)
        {
            if (same.Source != source || same.Lender != lender) continue;
            if (same.RateKind != rateKind || same.Rate != rate) continue;

            same.Capitalise(principal);

            return same;
        }

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

    /// <summary>Долговая нагрузка в сотых: 200 означает «внешний долг вдвое больше того,
    /// чем страна может по нему платить за год». За этой чертой в жизни зона риска.</summary>
    /// <param name="capacity">Чем платить: обычно годовой вывоз, а у резервной валюты
    /// ещё и часть своего выпуска — её деньги примут.</param>
    public int BurdenToExports(Money capacity)
    {
        if (capacity.Raw <= 0) return Owed(LoanSource.Foreign).Raw > 0 ? int.MaxValue : 0;

        var burden = (Int128)Owed(LoanSource.Foreign).Raw * 100 / capacity.Raw;

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
    /// <summary>Списывает долю каждого займа: долг переписан по договорённости.</summary>
    /// <remarks>
    /// В жизни до отказа платить доходит редко — раньше садятся за стол и переписывают
    /// долг: часть прощают, срок растягивают, ставку режут. Парижский клуб и МВФ этим и
    /// заняты, и половина всех долговых историй кончается так, а не отказом.
    ///
    /// Кредитор при этом теряет деньги, но меньше, чем потерял бы при отказе, — потому и
    /// соглашается.
    /// </remarks>
    /// <param name="cut">Какую долю тела списать, в сотых долях процента.</param>
    public Money Forgive(LoanSource source, int cut)
    {
        if (cut <= 0) return default;

        var forgiven = default(Money);
        foreach (var loan in _loans)
        {
            if (loan.Source != source || loan.Principal.Raw == 0) continue;

            forgiven += loan.Repay(new Money(loan.Principal.Raw * cut / 10_000));
        }

        Forget();

        return forgiven;
    }

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
