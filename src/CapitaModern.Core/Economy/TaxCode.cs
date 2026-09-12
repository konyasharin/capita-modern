namespace CapitaModern.Core.Economy;

/// <summary>Какие налоги берёт государство и по какой ставке.</summary>
/// <remarks>
/// Ставки в сотых долях процента, как и всё остальное в модели: 2000 — это двадцать
/// процентов. Ноль означает, что налога нет вовсе — отменить его игрок может так же
/// просто, как поднять.
///
/// Кривой Лаффера здесь нет и не будет отдельной формулой: она выходит сама. Поднятый НДС
/// удорожает товар, эластичность срезает заявку, покупают меньше — и сбор растёт медленнее
/// ставки. Поднятый налог на прибыль оставляет компаниям меньше на стройку, и выпуск
/// отстаёт. Оба хода в модели уже есть.
/// </remarks>
public sealed class TaxCode
{
    /// <summary>Налог на добавленную стоимость, с продаж населению.</summary>
    public int Vat { get; set; }

    /// <summary>Подоходный налог, удерживается из зарплаты.</summary>
    public int Income { get; set; }

    /// <summary>Взносы с фонда оплаты труда, платит работодатель сверх зарплаты.</summary>
    public int Payroll { get; set; }

    /// <summary>Налог на прибыль компаний.</summary>
    public int Profit { get; set; }

    /// <summary>Налог на добычу: с выручки шахт, промыслов и ферм.</summary>
    public int Extraction { get; set; }

    /// <summary>Акциз: с продажи топлива, товаров и лекарств населению.</summary>
    public int Excise { get; set; }

    /// <summary>Ставки России на 2020 год. Ими и заполняется мир: данных по всем двумстам
    /// странам нет, а одна знакомая раскладка лучше выдуманной.</summary>
    public static TaxCode Default() => new()
    {
        Vat = 2000,
        Income = 1300,
        Payroll = 3000,
        Profit = 2000,
        Extraction = 1000,
        Excise = 1000,
    };

    /// <summary>Сколько взять с суммы по ставке.</summary>
    public static Money Take(Money from, int rate) =>
        rate <= 0 || from.Raw <= 0 ? default : new Money((long)((Int128)from.Raw * rate / 10_000));
}
