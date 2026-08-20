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
    public int Id { get; init; }
    public int CellsCount { get; init; }
    public int Population { get; init; }

    private Dictionary<byte, int> Owned { get; init; } = new();
    private Dictionary<BuildingType, int> Buildings { get; init; } = new();
    private Dictionary<GoodType, int> Deposits { get; init; } = new();
}
