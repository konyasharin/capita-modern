using CapitaModern.Core.Data;

namespace CapitaModern.Core.Economy;

public sealed record Good(int Index, string Id, string Name, string Category, string Unit, double BasePrice)
{
    /// <summary>Услуги не хранятся: что не купили за неделю, то пропало.</summary>
    public bool Perishable => Id == "Services";
}

/// <param name="Inputs">Расход за сутки на одно здание: (товар, количество).</param>
/// <param name="Outputs">Выпуск за сутки на одно здание.</param>
/// <param name="Deposit">Месторождение, без которого здание не ставят; -1 — не нужно.</param>
public sealed record Building(
    int Index,
    string Type,
    string Name,
    string Sector,
    int Deposit,
    (int Good, double Amount)[] Inputs,
    (int Good, double Amount)[] Outputs,
    (int Good, double Amount)[] BuildCost,
    double Workers,
    double LifeYears);

/// <summary>Товары и рецепты. Всё по индексам: недельный расчёт гоняет их сотни тысяч раз.</summary>
public sealed class Catalog
{
    public IReadOnlyList<Good> Goods { get; }
    public IReadOnlyList<Building> Buildings { get; }

    private readonly Dictionary<string, int> _goodIndex;
    private readonly Dictionary<string, int> _buildingIndex;

    public Catalog(IReadOnlyList<GoodDto> goods, IReadOnlyList<BuildingDto> buildings, IReadOnlyDictionary<string, double> prices)
    {
        Goods = goods.Select((g, i) => new Good(i, g.Id, g.Name, g.Category, g.Unit, prices.GetValueOrDefault(g.Id, 1))).ToList();
        _goodIndex = Goods.ToDictionary(g => g.Id, g => g.Index);

        Buildings = buildings.Select((b, i) => new Building(
            i, b.Type, b.Name, b.Sector,
            b.RequiresDeposit is { } d ? GoodIndex(d) : -1,
            Pairs(b.Inputs), Pairs(b.Outputs), Pairs(b.BuildCost),
            b.OptimalWorkers, b.LifeYears)).ToList();
        _buildingIndex = Buildings.ToDictionary(b => b.Type, b => b.Index);
    }

    public int GoodIndex(string id) =>
        _goodIndex.TryGetValue(id, out var i) ? i : throw new InvalidDataException($"нет товара {id}");

    public int BuildingIndex(string type) =>
        _buildingIndex.TryGetValue(type, out var i) ? i : throw new InvalidDataException($"нет здания {type}");

    public bool TryGood(string id, out int index) => _goodIndex.TryGetValue(id, out index);

    private (int, double)[] Pairs(Dictionary<string, double> map) =>
        map.Select(pair => (GoodIndex(pair.Key), pair.Value)).ToArray();
}
