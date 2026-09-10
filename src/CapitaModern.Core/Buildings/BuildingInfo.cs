using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Buildings;

/// <summary>Описание типа постройки из buildings.json, одно на весь мир.</summary>
public sealed class BuildingInfo
{
    public BuildingType Type { get; init; }
    public Sector Sector { get; init; }
    /// <summary>Сколько чего съедает за один тик при полной загрузке.</summary>
    public Dictionary<GoodType, GoodAmount> Inputs { get; init; } = new();

    /// <summary>Сколько чего выдаёт за один тик при полной загрузке.</summary>
    public Dictionary<GoodType, GoodAmount> Outputs { get; init; } = new();

    /// <summary>Рабочих для полного выпуска при обычной эффективности. У отстающей
    /// страны тот же завод просит больше рук.</summary>
    public int OptimalWorkers { get; init; }

    /// <summary>Из чего строится. Стоит примерно три своих годовых выпуска — столько же,
    /// сколько капитал относится к выпуску в жизни.</summary>
    public Dictionary<GoodType, GoodAmount> BuildCost { get; init; } = new();
    /// <summary>Без этого месторождения в области предприятие не работает. Пусто у всех,
    /// кроме добычи.</summary>
    public GoodType? RequiresDeposit { get; init; }
}
