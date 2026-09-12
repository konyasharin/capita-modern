namespace CapitaModern.Core.Economy;

/// <summary>Банк страны: принимает вклады населения и раздаёт их компаниям.</summary>
/// <remarks>
/// Один на страну, как и государство: разделять их по компаниям пока не на чем — вкладчик
/// в модели один, всё население сразу.
///
/// Банк не печатает денег. Он берёт сбережения людей и отдаёт их в дело, а обратно несёт
/// проценты. Без него компания могла расти только из своей прибыли, а в жизни завод строят
/// на заёмное куда чаще, чем на накопленное.
///
/// Ставка по вкладам ниже ставки по кредитам, и разница — доход банка. Она же и мера того,
/// насколько дорого в стране достаются деньги: чем выше ключевая ставка, тем дороже заём и
/// тем меньше строек.
/// </remarks>
public sealed class Bank
{
    /// <summary>Насколько кредит дороже вклада, в сотых долях процента. В жизни разница у
    /// банков около трёх-четырёх процентов.</summary>
    public const int Margin = 350;

    /// <summary>Какую долю вкладов банк держит наличными и не раздаёт. Норма резервов:
    /// вкладчик должен иметь возможность забрать своё.</summary>
    public const int Reserve = 10;

    /// <summary>Сколько лет на погашение кредита компании.</summary>
    public const int LoanYears = 7;

    /// <summary>Вклады населения. Отсюда банк и берёт то, что раздаёт.</summary>
    public Money Deposits { get; private set; }

    /// <summary>Что роздано компаниям и ещё не возвращено.</summary>
    public Money Lent { get; private set; }

    /// <summary>Что банк заработал и ещё не отдал вкладчикам.</summary>
    public Money Earned { get; private set; }

    /// <summary>Сколько банк может раздать сверх уже розданного.</summary>
    public Money Free
    {
        get
        {
            var usable = new Money(Deposits.Raw * (100 - Reserve) / 100);

            return usable > Lent ? usable - Lent : default;
        }
    }

    /// <summary>Принял вклад.</summary>
    public void Take(Money amount)
    {
        if (amount.Raw <= 0) return;

        Deposits += amount;
    }

    /// <summary>Вкладчик забрал. Возвращает, сколько удалось отдать.</summary>
    public Money Give(Money wanted)
    {
        var paid = wanted < Deposits ? wanted : Deposits;
        if (paid.Raw <= 0) return default;

        Deposits -= paid;

        return paid;
    }

    /// <summary>Выдал кредит.</summary>
    public bool Lend(Money amount)
    {
        if (amount.Raw <= 0 || amount > Free) return false;

        Lent += amount;

        return true;
    }

    /// <summary>Кредит вернули: тело уменьшает выданное, проценты идут в доход.</summary>
    public void Returned(Money principal, Money interest)
    {
        Lent = Lent - principal < default(Money) ? default : Lent - principal;
        Earned += interest;
    }

    /// <summary>Заёмщик разорился: выданное ему не вернётся никогда.</summary>
    public void WriteOff(Money principal)
    {
        Lent = Lent - principal < default(Money) ? default : Lent - principal;

        // Убыток гасится сперва заработанным, остальное — из вкладов. Так и происходит:
        // невозврат съедает капитал банка, а за ним и деньги вкладчиков.
        var loss = principal;
        var fromEarned = loss < Earned ? loss : Earned;

        Earned -= fromEarned;
        loss -= fromEarned;

        if (loss.Raw > 0) Deposits = Deposits - loss < default(Money) ? default : Deposits - loss;
    }

    /// <summary>Раздаёт заработанное вкладчикам и обнуляет счётчик.</summary>
    public Money PayOut()
    {
        var paid = Earned;
        Earned = default;

        return paid;
    }
}
