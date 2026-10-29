using CapitaModern.Core.Economy;
using CapitaModern.Core.Map;

namespace CapitaModern.Core.World;

/// <summary>Всё состояние партии. Логика живёт в шагах симуляции, здесь только данные и поиск.</summary>
public sealed class GameWorld
{
    public required Catalog Catalog { get; init; }
    public required WorldMap Map { get; init; }
    public required RegionGrid Grid { get; init; }

    /// <summary>Страны по id, тот же байт, что в карте; пустые места — null.</summary>
    public required Country?[] CountryById { get; init; }

    /// <summary>Регионы по id; нулевой — пустое место.</summary>
    public required Region?[] RegionById { get; init; }

    public required IReadOnlyList<DivisionTemplate> Templates { get; init; }

    /// <summary>Сколько товара съедает за сутки миллион человек: (товар, ставка).</summary>
    public required (int Good, double Rate)[] Needs { get; init; }

    /// <summary>Насколько потребление каждого товара из <see cref="Needs"/> идёт за доходом на душу.</summary>
    public required double[] NeedElasticity { get; init; }

    public List<Company> Companies { get; } = [];
    public List<Project> Projects { get; } = [];
    public List<Division> Divisions { get; } = [];

    /// <summary>Пары стран в войне, меньший id первым.</summary>
    public HashSet<(int, int)> Wars { get; } = [];

    public DateOnly Date { get; set; } = new(2020, 1, 1);
    public int Day { get; set; }
    public int NextDivisionId { get; set; } = 1;

    public IEnumerable<Country> Countries => CountryById.OfType<Country>();
    public IEnumerable<Region> Regions => RegionById.OfType<Region>();

    public Region Region(int id) => RegionById[id] ?? throw new ArgumentException($"нет региона {id}");
    public Country Country(int id) => CountryById[id] ?? throw new ArgumentException($"нет страны {id}");

    public IEnumerable<Region> ControlledBy(int country) => Regions.Where(r => r.Controller == country);
    public IEnumerable<Region> OwnedBy(int country) => Regions.Where(r => r.Owner == country);

    public bool AtWar(int a, int b) => a != b && Wars.Contains(a < b ? (a, b) : (b, a));

    public IEnumerable<int> Enemies(int country) =>
        Wars.Where(w => w.Item1 == country || w.Item2 == country)
            .Select(w => w.Item1 == country ? w.Item2 : w.Item1);

    /// <summary>Регион под ячейкой карты, null — океан.</summary>
    public Region? RegionAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Grid.Width || y >= Grid.Height) return null;

        var id = Grid.Cell[y * Grid.Width + x];
        return id == RegionGrid.None ? null : RegionById[id];
    }

    public Company? CompanyOf(int id) => id == Company.State ? null : Companies[id];
}
