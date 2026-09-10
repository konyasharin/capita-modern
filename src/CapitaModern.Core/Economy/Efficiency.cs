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

    /// <param name="sensitivity">Насколько отрасль зависит от умения, в сотых. У добычи
    /// низкая — нефть качают везде примерно одинаково; у электроники высокая.</param>
    public Efficiency(
        IReadOnlyDictionary<byte, (int Skill, int Tech, int Condition)>? byCountry = null,
        IReadOnlyDictionary<Sector, int>? sensitivity = null,
        int countries = 256)
    {
        _skill = Filled(countries);
        _tech = Filled(countries);
        _condition = Filled(countries);
        _sensitivity = new int[Enum.GetValues<Sector>().Length];
        Array.Fill(_sensitivity, Scale);

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
    /// друг к другу, в электронике разрыв шире. Поэтому бедной стране выгоднее копать, а
    /// богатой — делать сложное, и никакой отдельной логики для этого не нужно.
    /// </remarks>
    public int Of(byte country, Sector sector)
    {
        // Средняя по миру — ровно Scale, от неё и считается отрыв.
        var off = (long)(Of(country) - Scale) * _sensitivity[(int)sector] / Scale;

        return (int)Math.Max(Floor, Scale + off);
    }

    private static int[] Filled(int countries)
    {
        var values = new int[countries];
        Array.Fill(values, Scale);

        return values;
    }
}
