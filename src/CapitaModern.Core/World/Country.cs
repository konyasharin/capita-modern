using CapitaModern.Core.Economy;
using CapitaModern.Core.Politics;

namespace CapitaModern.Core.World;

/// <summary>Государство. Территории здесь нет — её знает карта, склад и деньги лежат
/// у <see cref="State"/>.</summary>
public sealed class Country
{
    /// <summary>Тот же байт, что лежит в world.bin для каждой ячейки.</summary>
    public byte Id { get; }
    public string Name { get; }
    public string Iso { get; }

    /// <summary>Государство как хозяйствующий субъект — пока единственный в стране.
    /// Компании появятся такими же продавцами рядом с ним.</summary>
    public Producer State { get; }

    public Priorities Priorities { get; }

    /// <summary>Пускают ли на мировой рынок. По умолчанию везде да.</summary>
    public TradeAccess TradeAccess { get; } = new();

    /// <summary>Сколько местных денег за одну мировую единицу. Дороже — валюта слабее.</summary>
    /// <remarks>
    /// Мировая единица — доллар: цены мирового рынка и резервы считаются в нём. Стартовый
    /// курс берётся из жизни, поэтому в России цены сразу в рублях, а в Японии в иенах.
    ///
    /// Двигает его сальдо: у страны с дефицитом валюта дешевеет, импортное дорожает в
    /// местных деньгах, заявка через эластичность срезается, дефицит закрывается сам.
    /// </remarks>
    public Money ExchangeRate { get; private set; } = Money.FromWhole(1);

    /// <summary>Валюта страны: код и знак для подписей.</summary>
    public Currency Currency { get; init; } = Currency.Dollar;

    /// <summary>Печатный станок и денежная масса.</summary>
    public CentralBank Bank { get; init; } = new(default);

    /// <summary>Ключевая ставка в сотых долях процента: 425 — это 4.25% годовых.</summary>
    /// <remarks>Пока только хранится. Заработает с плавающими займами, и тогда же станет
    /// рычагом игрока: поднял против инфляции — вырос свой же процентный расход.</remarks>
    public int KeyRate { get; set; }

    /// <summary>Выше этой ставки страна занимать не станет, в сотых долях процента.</summary>
    public int MaxBorrowRate { get; set; } = 5000;

    /// <summary>Деньги населения. Государство платит ему зарплату, оно покупает у
    /// государства еду — на этом круге и держится внутренний оборот.</summary>
    public Households Households { get; }

    /// <summary>Весь фонд оплаты за прошедший тик.</summary>
    /// <remarks>Хранится целиком, а не на душу: подушевой доход — это доли копейки, и
    /// при делении заранее от него ничего не остаётся. Делим в месте применения.</remarks>
    public Money Payroll { get; set; }

    /// <summary>Какая доля добавленной стоимости уходит на оплату труда, в сотых.</summary>
    /// <remarks>В жизни около 55% по миру: США 58, Германия 60, Китай 47, Индия 51.
    /// Двигать её будут профсоюзы, законы и безработица.</remarks>
    public int LabourShare { get; set; } = 55;

    /// <summary>Средний вывоз за сутки, сглаженный за год. По нему считается долговая
    /// нагрузка: выручка одного тика скачет слишком сильно, чтобы на неё опираться.</summary>
    public Money ExportsPerDay { get; private set; }

    /// <summary>Средний ввоз за сутки, сглаженный так же. По нему считается, сколько
    /// валюты стране нужно держать: запас меряется сутками ввоза, а не оборота.</summary>
    public Money ImportsPerDay { get; private set; }

    /// <summary>Скользящее среднее за год: вчерашнее забывается на одну триста
    /// шестьдесят пятую.</summary>
    public void NoteExports(Money today, int daysInYear)
    {
        ExportsPerDay = new Money(
            (ExportsPerDay.Raw * (daysInYear - 1) + today.Raw) / daysInYear);
    }

    public void NoteImports(Money today, int daysInYear)
    {
        ImportsPerDay = new Money(
            (ImportsPerDay.Raw * (daysInYear - 1) + today.Raw) / daysInYear);
    }

    /// <summary>Тик, когда страна отказалась платить. Ноль — не отказывалась. После
    /// отказа на рынок несколько лет не пускают.</summary>
    public int DefaultedOnDay { get; set; }

    /// <summary>Курс на старте партии. От него считается коридор.</summary>
    public Money StartRate { get; private init; } = Money.FromWhole(1);

    /// <summary>Курс не может ни исчезнуть, ни улететь: коридор тот же, что у цен.</summary>
    public void MoveRate(Money to)
    {
        ExchangeRate = new Money(Math.Clamp(
            to.Raw, StartRate.Raw / Prices.MaxSwingTimes, StartRate.Raw * Prices.MaxSwingTimes));
    }

    public Country(byte id, string name, string iso, Producer state, Priorities priorities,
        Money savings = default, Money rate = default)
    {
        Households = new Households(savings);
        Id = id;
        Name = name;
        Iso = iso;
        State = state;
        Priorities = priorities;

        if (rate.Raw <= 0) rate = Money.FromWhole(1);

        ExchangeRate = rate;
        StartRate = rate;
    }
}
