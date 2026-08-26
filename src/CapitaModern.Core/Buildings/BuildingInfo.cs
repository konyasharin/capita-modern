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
    /// <summary>Какое месторождение нужно в области, чтобы предприятие работало.
    /// Пусто у всех, кроме добычи: без этой проверки нефть польётся из Швейцарии.</summary>
    public GoodType? RequiresDeposit { get; init; }
}
