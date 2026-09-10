namespace CapitaModern.Core.Economy;

/// <summary>Кто кому и под сколько даст в долг за этот тик.</summary>
/// <param name="Want">Сколько не хватает. Ноль у тех, кто не занимает.</param>
/// <param name="Free">Сколько может дать сверх своих нужд.</param>
/// <param name="Premium">Надбавка за риск именно этого заёмщика, в сотых процента.</param>
/// <param name="MaxRate">Выше этой ставки заёмщик занимать не станет.</param>
public readonly record struct CreditOrder(
    byte Id,
    Treasury Treasury,
    byte Custody,
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

    /// <summary>Во что превращается отношение: неприязнь в −70 добавляет 560 к ставке,
    /// дружба в 60 сбавляет 480. Ниже безрисковой ставка при этом не падает.</summary>
    public const int PoliticsFactor = 8;

    /// <summary>Хуже этого отношения не дают вовсе, ни под какой процент.</summary>
    public const int Hostile = -50;

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
    /// <summary>Что политика делает со ставкой. Враждебным не дают вообще, своим дают
    /// дешевле — так Китай и заходит на рынки, куда Япония не пойдёт ни под какой
    /// процент.</summary>
    /// <returns>Надбавка к ставке, либо <c>null</c>, если давать не станут.</returns>
    public static int? PoliticsOn(int attitude)
    {
        if (attitude <= Hostile) return null;

        return -attitude * PoliticsFactor;
    }

    /// <param name="attitude">Как кредитор относится к заёмщику, от −100 до 100.</param>
    public Money Settle(Span<CreditOrder> orders, Func<byte, byte, int>? attitude = null)
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

                var politics = PoliticsOn(attitude?.Invoke(lenders[i].Id, borrower.Id) ?? 0);
                if (politics is null) continue; // враждебным не дают ни под какой процент

                // Ставка не опускается ниже безрисковой, как бы ни дружили.
                var rate = Math.Max(BaseRate, BaseRate + lenders[i].KeyRate + borrower.Premium + politics.Value);
                if (rate > borrower.MaxRate || rate > Ceiling) continue;

                var lent = free[i] < left ? free[i] : left;
                if (!lenders[i].Treasury.Reserves.TrySpend(lent)) continue;

                borrower.Treasury.Reserves.Add(Reserves.Incoming(borrower.Id, borrower.Custody, lent));
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
