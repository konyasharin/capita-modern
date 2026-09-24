using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;

namespace CapitaModern.Core.World;

/// <summary>Частная компания: чем владеет, что умеет и сколько у неё денег.</summary>
/// <remarks>
/// Здания остаются на счету области — там их видит карта и оттуда идёт выпуск. Компания
/// держит свою долю того же счёта: сумма по компаниям равна счёту области. Полное
/// разделение складов и цен по компаниям придёт позже; сейчас важно другое — кто получает
/// прибыль и кто решает, что строить.
///
/// Ниша — это не украшение, а главное ограничение. Лесопромышленная компания не станет
/// строить ракетный завод, даже если он прибыльнее: у неё нет ни людей, ни связей, ни
/// понимания дела. Оттого страна и не перестраивается в одну самую выгодную отрасль за
/// пару лет, как это выходило, когда решение принимало государство целиком.
/// </remarks>
public sealed class Company
{
    public int Id { get; }
    public byte Country { get; }
    public string Name { get; }

    /// <summary>В каких отраслях работает. Одна у большинства, несколько у крупных.</summary>
    public IReadOnlyList<Sector> Focus => _focus;

    private readonly List<Sector> _focus;

    /// <summary>Берётся за новую отрасль. Так и растут конгломераты: скопив денег, идут в
    /// соседний передел, а не в самый прибыльный на свете.</summary>
    public void Expand(Sector sector)
    {
        if (_focus.Contains(sector)) return;

        _focus.Add(sector);
    }

    /// <summary>Свои деньги. Из них строит и с них платит налоги.</summary>
    public Money Cash { get; private set; }

    /// <summary>Сколько на складе страны товара, принадлежащего этой компании.</summary>
    /// <remarks>
    /// Склад один на страну — тот, что видит карта и с которого идёт торговля, — а доли в
    /// нём именные. Так устроен и настоящий элеватор: зерно ссыпают в общий бункер, а
    /// принадлежит оно тем, кто его привёз.
    ///
    /// Нужно это ради выручки: пока склад был безымянным, деньги за проданное делились
    /// между всеми по числу зданий, и завод, который сегодня стоял без сырья, получал
    /// столько же, сколько работавший.
    /// </remarks>
    public GoodAmount Holds(GoodType good) => new(_goods[(int)good]);

    /// <summary>Что у компании лежит на складе. Для показа и замера.</summary>
    public IEnumerable<(GoodType Good, GoodAmount Amount)> Goods
    {
        get
        {
            for (var i = 0; i < _goods.Length; i++)
            {
                if (_goods[i] > 0) yield return ((GoodType)i, new GoodAmount(_goods[i]));
            }
        }
    }

    /// <summary>Зачисляет сделанное.</summary>
    public void Store(GoodType good, GoodAmount amount)
    {
        if (amount.Raw <= 0) return;

        _goods[(int)good] += amount.Raw;
    }

    /// <summary>Списывает со склада сколько получится и говорит, сколько списало.</summary>
    public GoodAmount Take(GoodType good, GoodAmount amount)
    {
        var have = _goods[(int)good];
        var gone = Math.Min(amount.Raw, have);
        if (gone <= 0) return default;

        _goods[(int)good] = have - gone;

        return new GoodAmount(gone);
    }

    /// <summary>Во что обошёлся выпуск компании за этот тик. По нему делится прибыль.</summary>
    public Money MadeToday { get; private set; }

    public void NoteMade(Money worth) => MadeToday += worth;

    public void ForgetMade() => MadeToday = default;

    /// <summary>Подгоняет именные доли под то, что и правда лежит на складе страны.</summary>
    /// <remarks>
    /// Со склада берут все и отовсюду — заводы на сырьё, население, стройка, армия, вывоз.
    /// Списывать у хозяев в каждом из этих мест значило бы протянуть учёт через пол-модели;
    /// вместо этого раз в тик доли ужимаются или растягиваются под настоящий остаток. Кто
    /// сколько внёс, тот столько и потерял — как и должно быть в общем бункере.
    /// </remarks>
    public void FitAll(ReadOnlySpan<long> have, ReadOnlySpan<long> mine)
    {
        for (var i = 0; i < _goods.Length; i++)
        {
            var held = _goods[i];
            if (held <= 0 || mine[i] <= 0) continue;

            _goods[i] = (long)((Int128)held * have[i] / mine[i]);
        }
    }

    /// <summary>Добавляет свои доли к общему счёту.</summary>
    public void AddTo(Span<long> totals)
    {
        for (var i = 0; i < _goods.Length; i++) totals[i] += _goods[i];
    }

    /// <summary>Записывается в очередь продавцов по всем товарам, что у неё есть.</summary>
    public void OfferTo(List<Company>[] lists, int from)
    {
        for (var i = 0; i < _goods.Length; i++)
        {
            if (_goods[i] > 0) lists[from + i].Add(this);
        }
    }

    /// <summary>Насколько цена компании отличается от средней по стране, в сотых.
    /// Сотня — как у всех.</summary>
    /// <remarks>
    /// Не своя цена целиком, а отклонение от общей: общую двигают покрытие, денежный якорь
    /// и закон одной цены, и всё это должно продолжать работать. Компания решает только,
    /// дешевле она соседа или дороже.
    ///
    /// Дешевле продают те, у кого залежался товар, дороже — те, у кого его выметают. Отсюда
    /// и берётся конкуренция: покупатель идёт к дешёвому, дешёвый продаёт больше штук и
    /// меньше зарабатывает на каждой.
    /// </remarks>
    public int Edge(GoodType good) => _edge[(int)good];

    /// <summary>Цена как у всех.</summary>
    public const int Even = 100;

    /// <summary>Дальше не расходятся. Один и тот же товар на одном рынке не стоит у соседей
    /// вдвое по-разному: в жизни разброс цен на биржевой товар — единицы процентов, на
    /// розничный — полтора десятка.</summary>
    public const int Widest = 20;

    /// <summary>Двигает отклонение на шаг в нужную сторону.</summary>
    public void MoveEdge(GoodType good, int by) =>
        _edge[(int)good] = Math.Clamp(Edge(good) + by, Even - Widest / 2, Even + Widest);

    /// <summary>Что компания продала за этот тик, в деньгах. По нему делится прибыль.</summary>
    public Money SoldToday { get; private set; }

    public void NoteSold(Money worth) => SoldToday += worth;

    public void ForgetSold() => SoldToday = default;

    /// <summary>Склад и цены — массивами, а не словарями: их перебирают трижды за тик у
    /// каждой из восьми с половиной тысяч компаний, и на словарях это стоило миллисекунд.</summary>
    private readonly long[] _goods = new long[Goods32];
    private readonly int[] _edge = [.. Enumerable.Repeat(Even, Goods32)];

    private static readonly int Goods32 = Enum.GetValues<GoodType>().Length;

    /// <summary>Сколько чего у неё есть, по областям.</summary>
    private readonly Dictionary<(int Region, BuildingType Type), int> _buildings = [];

    /// <summary>Знают ли её по имени. У названных настоящее имя из жизни, у прочих —
    /// «RUS добыча 3»: показывать их вперемешку не стоит.</summary>
    public bool Known { get; }

    public Company(
        int id,
        byte country,
        string name,
        IReadOnlyList<Sector> focus,
        Money cash = default,
        bool known = false)
    {
        Id = id;
        Country = country;
        Name = name;
        _focus = [.. focus];
        Cash = cash;
        Known = known;
    }

    public IReadOnlyDictionary<(int Region, BuildingType Type), int> Buildings => _buildings;

    /// <summary>Берётся ли компания за эту отрасль.</summary>
    public bool Works(Sector sector) => Focus.Contains(sector);

    /// <summary>Указатель владения на весь мир. Ставится, когда мир заселяют компаниями;
    /// у тестовых миров его нет, и тогда компания просто никого не извещает.</summary>
    public Holdings? Ledger { get; set; }

    public void Add(int region, BuildingType type, int count)
    {
        if (count <= 0) return;

        _buildings[(region, type)] = _buildings.GetValueOrDefault((region, type)) + count;
        _byType[type] = _byType.GetValueOrDefault(type) + count;
        Size += count;
        Ledger?.Note(this, region, type);
    }

    /// <summary>Убирает сколько получится и говорит, сколько убрало.</summary>
    public int Remove(int region, BuildingType type, int count)
    {
        var have = _buildings.GetValueOrDefault((region, type));
        var gone = Math.Min(have, count);
        if (gone <= 0) return 0;

        var leftOfType = _byType.GetValueOrDefault(type) - gone;
        if (leftOfType > 0) _byType[type] = leftOfType;
        else _byType.Remove(type);

        Size -= gone;

        // Счёт по видам правим до указателя: он спрашивает, осталось ли что-то ещё.
        if (have == gone)
        {
            _buildings.Remove((region, type));
            Ledger?.Forget(this, region, type);
        }
        else
        {
            _buildings[(region, type)] = have - gone;
        }

        return gone;
    }

    /// <summary>Сколько таких зданий у компании в этой области.</summary>
    public int CountAt(int region, BuildingType type) => _buildings.GetValueOrDefault((region, type));

    public int CountOf(BuildingType type) => _byType.GetValueOrDefault(type);

    /// <summary>Чем компания владеет, по типам зданий.</summary>
    public IReadOnlyDictionary<BuildingType, int> ByType => _byType;

    /// <summary>Сколько чего у компании, без разбивки по областям. Считается по ходу, а не
    /// перебором: делёж прибыли идёт каждый тик, и перебор всех зданий всех компаний стоил
    /// втрое дороже самого тика.</summary>
    public IReadOnlyDictionary<BuildingType, int> Types => _byType;

    private readonly Dictionary<BuildingType, int> _byType = [];

    /// <summary>Всего зданий у компании. По нему делится прибыль страны.</summary>
    /// <remarks>Считается на лету, а не перебором: прибыль делится каждый тик, и перебор
    /// всех зданий всех компаний стоил втрое дороже самого тика.</remarks>
    public int Size { get; private set; }

    /// <summary>Сколько компания должна банкам своей страны.</summary>
    public Money Debt { get; private set; }

    /// <summary>На какой день она разорилась. Ноль — не разорялась.</summary>
    public int BrokeOnDay { get; private set; }

    /// <summary>Жива ли: разорившаяся не строит, не занимает и ничего не стоит.</summary>
    public bool Alive => BrokeOnDay == 0;

    /// <summary>Берёт в долг: деньги сразу в дело, долг на себя.</summary>
    public void Borrow(Money amount)
    {
        if (amount.Raw <= 0) return;

        Cash += amount;
        Debt += amount;
    }

    /// <summary>Гасит сколько может и говорит, сколько отдала.</summary>
    public Money Repay(Money wanted)
    {
        var paid = wanted < Cash ? wanted : Cash;
        if (paid > Debt) paid = Debt;
        if (paid.Raw <= 0) return default;

        Cash -= paid;
        Debt -= paid;

        return paid;
    }

    /// <summary>Проценты, которые нечем заплатить, уходят в тело долга.</summary>
    public void Capitalise(Money interest)
    {
        if (interest.Raw <= 0) return;

        Debt += interest;
    }

    /// <summary>Разорилась: долг списан, здания уходят тому, кто их подберёт.</summary>
    public void Break(int day)
    {
        BrokeOnDay = day;
        Debt = default;
        Cash = default;
    }

    public void Earn(Money amount)
    {
        if (amount.Raw <= 0) return;

        Cash += amount;
    }

    /// <summary>Отдаёт сколько может и говорит, сколько отдала. В отличие от
    /// <see cref="TrySpend"/> платит и неполностью: зарплату платят и частями.</summary>
    public Money Give(Money amount)
    {
        var paid = amount < Cash ? amount : Cash;
        if (paid.Raw <= 0) return default;

        Cash -= paid;

        return paid;
    }

    /// <summary>Во что обошлось купленное за этот тик. Вместе с проданным даёт добавленную
    /// стоимость компании — то, из чего платят зарплату и берут прибыль.</summary>
    public Money BoughtToday { get; private set; }

    public void NoteBought(Money worth) => BoughtToday += worth;

    public void ForgetBought() => BoughtToday = default;

    /// <summary>Добавленная стоимость за день, усреднённая за квартал. От неё платят зарплату.</summary>
    /// <remarks>Дневная рваная: в день закупки сырья минус, в день крупной продажи — вчетверо
    /// больше обычного. Зарплата шла за ней, и спрос людей с ценами прыгали следом.</remarks>
    public Money AddedCalm { get; private set; }

    public const int CalmDays = 90;

    public void NoteAdded(Money today) =>
        AddedCalm = AddedCalm.Raw == 0 ? today : new Money((AddedCalm.Raw * (CalmDays - 1) + today.Raw) / CalmDays);

    /// <summary>Тратит, если хватает. Не хватило — не тратит вовсе.</summary>
    public bool TrySpend(Money amount)
    {
        if (amount.Raw <= 0 || Cash - amount < default(Money)) return false;

        Cash -= amount;

        return true;
    }
}
