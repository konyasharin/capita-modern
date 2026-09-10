namespace CapitaModern.Core.Economy;

/// <summary>Кто кому и под сколько даст в долг за этот тик.</summary>
/// <param name="Want">Сколько не хватает. Ноль у тех, кто не занимает.</param>
/// <param name="Free">Сколько может дать сверх своих нужд.</param>
/// <param name="Premium">Надбавка за риск именно этого заёмщика, в сотых процента.</param>
/// <param name="MaxRate">Выше этой ставки заёмщик занимать не станет.</param>
public readonly record struct CreditOrder(
    byte Id,
    Treasury Treasury,
    Money Want,
    Money Free,
    int KeyRate,
    int Premium,
    int MaxRate);

/// <summary>Мировой кредитный рынок. Заём — это торг, а не выдача: если согласных нет,
/// аукцион не состоится.</summary>
/// <remarks>
/// Даёт не воздух, а страна со свободными резервами. Поэтому деньги в мире сохраняются,
/// и это буквально капитальный счёт: приток капитала финансирует чужой дефицит.
/// </remarks>
public sealed class CreditMarket
{
    /// <summary>Безрисковая ставка, сотые доли процента. Около неё занимали надёжные
    /// государства в 2020 году.</summary>
    public const int BaseRate = 100;

    /// <summary>Во сколько превращается долговая нагрузка. Нагрузка 145% даёт около 6%
    /// годовых, 256% — около 10%: примерно так занимали Россия и Турция.</summary>
    public const int BurdenFactor = 4;

    /// <summary>Надбавка тому, кто недавно отказался платить.</summary>
    public const int DefaultPenalty = 3000;

    /// <summary>Выше не даёт никто и никому.</summary>
    public const int Ceiling = 8000;

    /// <summary>Надбавка за риск: чем больше должен и чем свежее отказ, тем дороже.</summary>
    /// <param name="burden">Внешний долг к годовому экспорту, в процентах.</param>
    public static int PremiumFor(int burden, bool recentlyDefaulted)
    {
        var premium = (int)Math.Min(Ceiling, (long)Math.Max(0, burden) * BurdenFactor);

        return recentlyDefaulted ? Math.Min(Ceiling, premium + DefaultPenalty) : premium;
    }

    /// <summary>Сводит заявки. Заёмщики идут от самых надёжных, кредиторы — от самых
    /// дешёвых; кончились согласные раньше суммы, значит аукцион не состоялся.</summary>
    /// <returns>Сколько денег роздано.</returns>
    public Money Settle(Span<CreditOrder> orders)
    {
        var borrowers = new List<CreditOrder>();
        var lenders = new List<CreditOrder>();
        foreach (var order in orders)
        {
            if (order.Want.Raw > 0) borrowers.Add(order);
            else if (order.Free.Raw > 0) lenders.Add(order);
        }

        if (borrowers.Count == 0 || lenders.Count == 0) return default;

        // Надёжным дают первым: при нехватке денег рискованные остаются ни с чем.
        borrowers.Sort((a, b) => a.Premium != b.Premium ? a.Premium - b.Premium : a.Id - b.Id);
        lenders.Sort((a, b) => a.KeyRate != b.KeyRate ? a.KeyRate - b.KeyRate : a.Id - b.Id);

        var free = new Money[lenders.Count];
        for (var i = 0; i < lenders.Count; i++) free[i] = lenders[i].Free;

        var given = default(Money);
        foreach (var borrower in borrowers)
        {
            var left = borrower.Want;

            for (var i = 0; i < lenders.Count && left.Raw > 0; i++)
            {
                if (free[i].Raw <= 0 || lenders[i].Id == borrower.Id) continue;

                var rate = BaseRate + lenders[i].KeyRate + borrower.Premium;
                if (rate > borrower.MaxRate || rate > Ceiling) break; // дальше только дороже

                var lent = free[i] < left ? free[i] : left;
                if (!lenders[i].Treasury.Reserves.TrySpend(lent)) continue;

                borrower.Treasury.Reserves.Add(ReserveKind.ForeignCurrency, WorldMarket.WorldIssuer, lent);
                // Заём плавающий: в валюте кредитора, значит и ставка идёт за его
                // ключевой. Фиксированная останется облигациям, когда они появятся.
                borrower.Treasury.Debt.Take(
                    LoanSource.Foreign, lenders[i].Id, lent, RateKind.Floating, BaseRate + borrower.Premium);

                free[i] -= lent;
                left -= lent;
                given += lent;
            }
        }

        return given;
    }
}
