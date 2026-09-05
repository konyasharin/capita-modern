using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Buildings;

/// <summary>Описание типа постройки из buildings.json, одно на весь мир.</summary>
public sealed class BuildingInfo
{
    public BuildingType Type { get; init; }

    /// <summary>Сколько чего съедает за один тик при полной загрузке рабочими.</summary>
    public Dictionary<GoodType, GoodAmount> Inputs { get; init; } = new();

    /// <summary>Сколько чего выдаёт за один тик при полной загрузке рабочими.</summary>
    public Dictionary<GoodType, GoodAmount> Outputs { get; init; } = new();

    /// <summary>Рабочих для полного выпуска. Меньше — выпуск ниже. Пока не используется.</summary>
    public int OptimalWorkers { get; init; }
    /// <summary>Без этого месторождения в области предприятие не работает. Пусто у всех,
    /// кроме добычи.</summary>
    public GoodType? RequiresDeposit { get; init; }
}
