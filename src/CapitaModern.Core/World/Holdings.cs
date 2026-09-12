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

    /// <summary>Кто держит такие здания в этой области.</summary>
    public IReadOnlyList<Company> OwnersOf(int region, BuildingType type) =>
        _owners.TryGetValue((region, type), out var list) ? list : [];

    /// <summary>Запомнить, что у компании тут что-то появилось.</summary>
    public void Note(Company company, int region, BuildingType type)
    {
        if (!_owners.TryGetValue((region, type), out var list)) _owners[(region, type)] = list = [];
        if (!list.Contains(company)) list.Add(company);
    }

    /// <summary>Забыть: у компании тут не осталось ничего.</summary>
    public void Forget(Company company, int region, BuildingType type)
    {
        if (_owners.TryGetValue((region, type), out var list)) list.Remove(company);
    }
}
