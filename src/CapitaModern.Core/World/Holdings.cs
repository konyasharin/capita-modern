using CapitaModern.Core.Buildings;

namespace CapitaModern.Core.World;

/// <summary>Кто чем владеет в каждой области.</summary>
/// <remarks>
/// Указатель, а не второй счёт: сколько именно у кого — по-прежнему знает сама компания.
/// Здесь только «кто», и держится это ради износа и всего, что после него: найти хозяев
/// перебором всех компаний страны стоило тринадцать миллисекунд на тик, а понадобится это
/// каждый раз, когда здание рушится, строится или меняет хозяина.
/// </remarks>
public sealed class Holdings
{
    private readonly Dictionary<(int Region, BuildingType Type), List<Company>> _owners = [];
    private readonly Dictionary<(byte Country, BuildingType Type), List<Company>> _byCountry = [];

    /// <summary>Кто держит такие здания в этой области.</summary>
    public IReadOnlyList<Company> OwnersOf(int region, BuildingType type) =>
        _owners.TryGetValue((region, type), out var list) ? list : [];

    /// <summary>Кто держит такие здания в стране. Выпуск считается по стране, не по
    /// области, и делить его надо между этими.</summary>
    public IReadOnlyList<Company> OwnersIn(byte country, BuildingType type) =>
        _byCountry.TryGetValue((country, type), out var list) ? list : [];

    /// <summary>Запомнить, что у компании тут что-то появилось.</summary>
    public void Note(Company company, int region, BuildingType type)
    {
        if (!_owners.TryGetValue((region, type), out var list)) _owners[(region, type)] = list = [];
        if (!list.Contains(company)) list.Add(company);

        var key = (company.Country, type);
        if (!_byCountry.TryGetValue(key, out var mine)) _byCountry[key] = mine = [];
        if (!mine.Contains(company)) mine.Add(company);
    }

    /// <summary>Забыть: у компании тут не осталось ничего.</summary>
    public void Forget(Company company, int region, BuildingType type)
    {
        if (_owners.TryGetValue((region, type), out var list)) list.Remove(company);

        // Из страны вычёркиваем, только когда у компании не осталось таких зданий нигде.
        if (company.CountOf(type) > 0) return;
        if (_byCountry.TryGetValue((company.Country, type), out var mine)) mine.Remove(company);
    }
}
