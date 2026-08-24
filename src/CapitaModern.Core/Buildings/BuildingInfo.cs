using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Buildings;

/// <summary>
/// Описание типа постройки из data/economy/buildings.json. Одно на весь мир:
/// у построек нет индивидуальных характеристик, только тип.
/// </summary>
public sealed class BuildingInfo
{
    public BuildingType Type { get; init; }

    /// <summary>Сколько чего съедает за один тик при полной загрузке рабочими.</summary>
    public Dictionary<GoodType, int> Inputs { get; init; } = new();

    /// <summary>Сколько чего выдаёт за один тик при полной загрузке рабочими.</summary>
    public Dictionary<GoodType, int> Outputs { get; init; } = new();

    /// <summary>
    /// Численность, при которой выпуск равен <see cref="Outputs"/>.
    /// Рабочих меньше — выпуск пропорционально ниже.
    /// </summary>
    public int OptimalWorkers { get; init; }
}
