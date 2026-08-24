using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;

namespace CapitaModern.Core.World;

/// <summary>
/// Условная единица деления суши: нужна только для просмотра статистики.
/// Владельца одним полем здесь нет — фронт может разрезать регион пополам,
/// поэтому принадлежность это доли ячеек. См. docs/03-industry.md.
/// </summary>
public sealed class Region
{
    public int Id { get; }

    /// <summary>Сколько ячеек карты в регионе. Выводится из долей, отдельно не задаётся.</summary>
    public int CellsCount { get; }

    public int Population { get; private set; }

    /// <summary>Сколько ячеек у какой страны. Сумма всегда равна CellsCount.</summary>
    private Dictionary<byte, int> Owned { get; }

    /// <summary>Счётчик построек по типам: экземпляров построек нет, только числа.</summary>
    private Dictionary<BuildingType, int> Buildings { get; }

    /// <summary>Потенциал добычи по товарам; чего нет, того в регионе не добыть.</summary>
    private Dictionary<GoodType, int> Deposits { get; }

    public Region(int id, int population, IReadOnlyDictionary<byte, int> owned, IReadOnlyDictionary<BuildingType, int> buildings, IReadOnlyDictionary<GoodType, int> deposits)
    {
        if (owned.Values.Sum() == 0) throw new ArgumentException("Регион без владельцев");

        Id = id;
        Population = population;
        Owned = new(owned);
        Buildings = new(buildings);
        Deposits = new(deposits);
        CellsCount = Owned.Values.Sum();
    }

    public int CellsOf(byte country) => Owned.GetValueOrDefault(country, 0);
    /// <summary>Доля 0..1 — для показа игроку. Делить постройки ею нельзя: округление
    /// накопит расхождение, для этого есть BuildingsOf с двумя аргументами.</summary>
    public float ShareOf(byte country) => (float)CellsOf(country) / CellsCount;
    /// <summary>Кому принадлежит бо́льшая часть. При равенстве побеждает больший id —
    /// правило произвольное, но обязано быть явным: порядок перебора словаря не
    /// гарантирован, а ответ должен быть одним и тем же всегда.</summary>
    public byte LargestOwner
    {
        get
        {
            byte best = 0;
            int bestCells = -1;
            foreach (var (country, cells) in Owned)
            {
                if (cells > bestCells || (cells == bestCells && country > best))
                {
                    best = country;
                    bestCells = cells;
                }
            }

            return best;
        }
    }
    public bool IsSplit => Owned.Count > 1;
    /// <summary>Единственный способ изменить владение: держит сумму долей равной
    /// CellsCount и не даёт уйти в минус.</summary>
    public void TransferCells(byte from, byte to, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (from == to) throw new ArgumentException($"Id стран одинаковые - {from} = {to}");

        if (!Owned.TryGetValue(from, out int fromCount))
            throw new InvalidOperationException($"Owned не содержит ключа {from}");
        if (count > fromCount)
            throw new InvalidOperationException($"Нельзя передать более {fromCount} ячеек");

        Owned[to] = Owned.TryGetValue(to, out int toCount) ? toCount + count : count;
        if (fromCount - count == 0) Owned.Remove(from);
        else Owned[from] = fromCount - count;
    }

    public int BuildingsOf(BuildingType type) => Buildings.GetValueOrDefault(type, 0);
    /// <summary>
    /// Сколько построек этого типа досталось стране при разрезанном регионе.
    /// </summary>
    /// <remarks>
    /// Метод спрашивают про одну страну, но считает он весь дележ: целые части
    /// раздаются по доле ячеек, а остаток — тем, кого сильнее обделили округлением
    /// (метод наибольших остатков). Иначе суммы по странам не сошлись бы с общим
    /// числом построек. Всё в целых числах: остаток от деления заменяет дробную часть.
    /// </remarks>
    public int BuildingsOf(BuildingType type, byte country)
    {
        var buildings = BuildingsOf(type);
        var cells = CellsOf(country);

        if (buildings == 0 || cells == 0) return 0;
        if (Owned.Count == 1) return buildings;

        var share = buildings * cells;
        var result = share / CellsCount;
        var remainder = share % CellsCount;

        var left = buildings;
        var ahead = 0;

        foreach (var (other, otherCells) in Owned)
        {
            var otherShare = buildings * otherCells;
            left -= otherShare / CellsCount;

            if (other == country) continue;

            var otherRemainder = otherShare % CellsCount;
            if (otherRemainder > remainder || (otherRemainder == remainder && other > country)) ahead++;
        }

        return ahead < left ? result + 1 : result;
    }
    public void AddBuildings(BuildingType type, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        Buildings[type] = Buildings.GetValueOrDefault(type, 0) + count;
    }
    public bool TryRemoveBuildings(BuildingType type, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (!Buildings.TryGetValue(type, out int currentCount) || currentCount < count) return false;
        if (currentCount - count == 0)
        {
            Buildings.Remove(type);
            return true;
        }

        Buildings[type] -= count;
        return true;
    }

    public int DepositOf(GoodType type) => Deposits.GetValueOrDefault(type, 0);
    public bool HasDeposit(GoodType type) => DepositOf(type) > 0;
}
