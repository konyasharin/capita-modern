namespace CapitaModern.Core.Economy;

/// <summary>Что удорожает ввоз сверх цены самого товара.</summary>
/// <remarks>
/// Без этого цемент возят через океан так же охотно, как микросхемы, и мировой экспорт
/// выходит вдвое больше настоящего. В жизни перевозка съедает у щебня половину цены, а у
/// электроники меньше процента — это и есть плотность стоимости.
///
/// Перевозка — настоящий расход: она сжигает топливо. Пошлина — не расход, а
/// перераспределение, и она никуда не девается, просто меняет поведение. Плата за проход
/// будет третьей, но ей нужен граф маршрутов, которого пока нет.
/// </remarks>
public sealed class TradeCosts
{
    /// <summary>Доли хранятся в сотых процента: 2500 — это четверть стоимости.</summary>
    public const int Scale = 10_000;

    private readonly int[] _freight;
    private readonly bool[] _landlocked;
    private readonly int[] _tariff;

    /// <param name="landlockedFactor">Насколько дороже каждая чужая граница на пути, в
    /// сотых процента. Море стоит около цента за тонно-километр, грузовик вдесятеро;
    /// Всемирный банк оценивает надбавку стран без моря примерно в полтора раза.</param>
    public TradeCosts(
        IReadOnlyDictionary<GoodType, int>? freight = null,
        IReadOnlySet<byte>? landlocked = null,
        IReadOnlyDictionary<byte, int>? tariff = null,
        int landlockedFactor = 100,
        int countries = 256)
    {
        LandlockedFactor = landlockedFactor;
        _freight = new int[Enum.GetValues<GoodType>().Length];
        _landlocked = new bool[countries];
        _tariff = new int[countries];

        foreach (var (good, share) in freight ?? new Dictionary<GoodType, int>()) _freight[(int)good] = share;
        foreach (var country in landlocked ?? new HashSet<byte>()) _landlocked[country] = true;
        foreach (var (country, rate) in tariff ?? new Dictionary<byte, int>()) _tariff[country] = rate;
    }

    public int LandlockedFactor { get; }

    /// <summary>Доля перевозки в стоимости товара, в сотых процента.</summary>
    public int FreightOf(GoodType good) => _freight[(int)good];

    /// <summary>Есть ли у страны выход к морю. У кого нет, тот везёт по суше, а это
    /// вчетверо-вдесятеро дороже за тонно-километр.</summary>
    public bool Landlocked(byte country) => _landlocked[country];

    /// <summary>Средняя применяемая пошлина на ввоз, в сотых процента.</summary>
    public int TariffOf(byte country) => _tariff[country];

    /// <summary>Во сколько ввоз обходится дороже самой цены, в сотых процента.</summary>
    /// <remarks>Перевозка плюс пошлина. Вывоз этого не платит: везёт и растаможивает
    /// покупатель. Чем дальше страна от моря, тем дороже: каждая чужая граница на пути
    /// добавляет свою долю.</remarks>
    /// <param name="route">Во что обходится путь до рынка, в сотых: ноль у тех, кто
    /// прямо в океане, и тем больше, чем больше чужих границ и проливов по дороге.</param>
    public int ImportMarkup(byte country, GoodType good, int route = 0) =>
        FreightOf(good) * (100 + route) / 100 + TariffOf(country);

    /// <summary>Сколько берёт за проход страна, через которую везут, в сотых процента.</summary>
    /// <remarks>В жизни это Суэц с Панамой и транзит по чужой земле: девять и пять
    /// миллиардов в год соответственно. Деньги идут владельцу пути, и это, в отличие от
    /// пошлины, настоящее перераспределение между странами.</remarks>
    public const int TransitFee = 200;
}
