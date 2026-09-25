namespace CapitaModern.Core.Economy;

/// <summary>Насколько хорошо страна умеет производить.</summary>
/// <remarks>
/// Завод в модели везде одинаковый, значит вся разница в производительности лежит здесь.
/// Без неё нет и сравнительного преимущества: специализироваться незачем, и страны
/// торгуют одними излишками вместо половины своего выпуска.
///
/// Три множителя не для красоты — дальше каждый заживёт своей жизнью: квалификация от
/// расходов на образование, технология от древа, состояние от износа и ремонта.
/// </remarks>
public sealed class Efficiency
{
    /// <summary>Хранится в сотых: 100 — как у передовой страны.</summary>
    public const int Scale = 100;

    /// <summary>Ниже не бывает: даже мотыга что-то производит.</summary>
    public const int Floor = 1;

    /// <summary>Средняя по миру равна Scale по построению: множитель перераспределяет
    /// производительность между странами, а не меняет мировой итог.</summary>
    public const int Average = Scale;

    private readonly int[] _skill;
    private readonly int[] _tech;
    private readonly int[] _condition;
    private readonly int[] _sensitivity;
    private readonly bool[] _inOutput;
    private readonly int[] _outputMean;

    /// <param name="sensitivity">Насколько отрасль зависит от умения, в сотых. У добычи
    /// низкая — нефть качают везде примерно одинаково; у электроники высокая.</param>
    /// <param name="inOutput">Отрасли, где умение оборачивается выпуском, а не экономией
    /// рук. Остальным оно сокращает штат.</param>
    /// <param name="outputMean">Средняя по миру в такой отрасли, взвешенная по выпуску.
    /// На неё выпуск и делится, иначе множитель поднял бы мировой итог.</param>
    public Efficiency(
        IReadOnlyDictionary<byte, (int Skill, int Tech, int Condition)>? byCountry = null,
        IReadOnlyDictionary<Sector, int>? sensitivity = null,
        IReadOnlySet<Sector>? inOutput = null,
        IReadOnlyDictionary<Sector, int>? outputMean = null,
        int countries = 256)
    {
        _skill = Filled(countries);
        _saved = new long[countries];
        Array.Fill(_saved, (long)Scale * Fine);
        _tech = Filled(countries);
        _condition = Filled(countries);
        _sensitivity = new int[Enum.GetValues<Sector>().Length];
        _inOutput = new bool[Enum.GetValues<Sector>().Length];
        Array.Fill(_sensitivity, Scale);

        _outputMean = new int[Enum.GetValues<Sector>().Length];
        Array.Fill(_outputMean, Scale);

        foreach (var sector in inOutput ?? new HashSet<Sector>()) _inOutput[(int)sector] = true;

        foreach (var (sector, mean) in outputMean ?? new Dictionary<Sector, int>())
        {
            if (mean > 0) _outputMean[(int)sector] = mean;
        }

        foreach (var (country, parts) in byCountry ?? new Dictionary<byte, (int, int, int)>())
        {
            _skill[country] = parts.Skill;
            _tech[country] = parts.Tech;
            _condition[country] = parts.Condition;
        }

        foreach (var (sector, value) in sensitivity ?? new Dictionary<Sector, int>())
        {
            _sensitivity[(int)sector] = value;
        }
    }

    public int Skill(byte country) => _skill[country];
    public int Tech(byte country) => _tech[country];
    public int Condition(byte country) => _condition[country];

    /// <summary>Общий множитель к выпуску, в сотых.</summary>
    public int Of(byte country) =>
        Math.Max(Floor, Skill(country) * Tech(country) / Scale * Condition(country) / Scale);

    /// <summary>То же, но с поправкой на отрасль. Отсюда и берётся специализация.</summary>
    /// <remarks>
    /// Чувствительность растягивает отрыв от среднего в обе стороны: в добыче все ближе
    /// друг к другу, в услугах разрыв шире. Поэтому бедной стране выгоднее копать, а
    /// богатой — держать банк, и никакой отдельной логики для этого не нужно.
    ///
    /// Растягивается степенью, а не в пунктах: отставание в жизни меряется в разах, и от
    /// прибавки в пунктах отстающая страна уходила в ноль и просила в сто раз больше рук.
    /// </remarks>
    public int Of(byte country, Sector sector)
    {
        var sensitivity = _sensitivity[(int)sector];
        if (sensitivity == Scale) return Of(country);

        // Средняя по миру — ровно Scale, от неё и считается отрыв.
        var ratio = (long)Of(country) * Powers.Scale / Scale;

        return (int)Math.Max(Floor, Powers.PowCached(ratio, sensitivity) * Scale / Powers.Scale);
    }

    /// <summary>Чем оборачивается умение в этой отрасли: выпуском или экономией рук.</summary>
    /// <remarks>
    /// В производстве — экономией: американская ферма с комбайном и индийская с полусотней
    /// людей дают одно и то же зерно. В услугах наоборот: американский банк не
    /// обслуживается втрое меньшим числом людей, в нём работает столько же, просто каждый
    /// приносит втрое больше. Стрижка в Цюрихе и в Дакке — час работы одного человека,
    /// разница только в цене.
    /// </remarks>
    public bool ShowsInOutput(Sector sector) => _inOutput[(int)sector];

    /// <summary>Сколько рук просит предприятие с таким штатом.</summary>
    /// <remarks>
    /// Тот же завод год от года обходится меньшим числом людей: это капиталовооружённость,
    /// и без неё рост упирается в население. Заводов становится больше, рук на них не
    /// прибавляется, занятость встаёт — у нас она вставала на 2800 млн при рабочей силе в
    /// 3240, и рост ВВП сползал с 4.6% к 1.1% на двадцатом году.
    ///
    /// Заработок людей от этого не страдает: он считается долей добавленной стоимости, а не
    /// числом занятых. Освободившиеся руки уходят на новые заводы — за тем и освобождаются.
    /// </remarks>
    public long HandsFor(byte country, Sector sector, long workers)
    {
        var own = ShowsInOutput(sector) ? workers : workers * Scale / Of(country, sector);

        return (long)((Int128)own * Scale * Fine / Deepening * Scale / _saved[country]);
    }

    /// <summary>Во сколько раз страна своими вложениями сократила штат против начала партии, в сотых.</summary>
    public int SavedHands(byte country) => (int)(_saved[country] / Fine);

    private readonly long[] _saved;

    /// <summary>Модернизация: из <paramref name="total"/> рук, что просят заводы страны, станет
    /// нужно на <paramref name="freed"/> меньше.</summary>
    public void SaveHands(byte country, long freed, long total)
    {
        if (freed <= 0 || total <= freed) return;

        _saved[country] = (long)((Int128)_saved[country] * total / (total - freed));
    }

    /// <summary>Во сколько раз больше выпуска даёт то же предприятие, в сотых.</summary>
    /// <remarks>Делится на среднюю по миру: множитель перераспределяет выпуск между
    /// странами, а мировой итог оставляет на месте — как и множитель для рук.</remarks>
    public int OutputTimes(byte country, Sector sector)
    {
        var own = ShowsInOutput(sector)
            ? Math.Max(Floor, Of(country, sector) * Scale / _outputMean[(int)sector])
            : Scale;

        return (int)((long)own * Progress / Scale);
    }

    /// <summary>Во сколько раз тот же завод даёт больше, чем в первый день партии.</summary>
    /// <remarks>
    /// Умение страны считается от мирового среднего и потому не может поднять мировой итог:
    /// оно перекладывает выпуск между странами. А в жизни половина роста приходится не на
    /// новые заводы, а на то, что старые год от года дают больше: приёмы, обучение, станок
    /// получше. В счетах это зовут общей производительностью факторов.
    ///
    /// Выпуску, а не экономии рук: сокращая штат, прогресс отнимал бы у людей заработок, а
    /// с ним и спрос — проверено, выходило хуже, чем без прогресса вовсе.
    /// </remarks>
    public int Progress => (int)(_progress / Fine);

    /// <summary>Сколько знаков хранится сверх <see cref="Scale"/>: за сутки прогресс меняет
    /// сотые доли процента, и без запаса он округлялся бы в ноль.</summary>
    private const long Fine = 1_000_000;

    private long _progress = (long)Scale * Fine;

    /// <summary>Во сколько раз тот же завод обходится меньшим числом людей, чем в первый
    /// день партии.</summary>
    /// <remarks>
    /// Считается отдельно от <see cref="Progress"/> и медленнее его. Пока это было одно
    /// число, руки убывали с той же скоростью, с какой прибавлялся выпуск, и занятость
    /// падала с 2590 до 1444 млн при жизненных 3240 — вдвое. Убывать они должны ровно так
    /// же быстро, как прибавляется число заводов: тогда занятость стоит на месте.
    /// </remarks>
    public int Deepening => (int)(_deepen / Fine);

    private long _deepen = (long)Scale * Fine;

    /// <summary>Двигает мировой прогресс на сутки. Доли — в десятитысячных за год: первая
    /// на выпуск, вторая на экономию рук.</summary>
    public void Advance(int perYear, int handsPerYear)
    {
        if (perYear > 0) _progress += _progress * perYear / (10_000L * 365);
        if (handsPerYear > 0) _deepen += _deepen * handsPerYear / (10_000L * 365);
    }

    private static int[] Filled(int countries)
    {
        var values = new int[countries];
        Array.Fill(values, Scale);

        return values;
    }
}
