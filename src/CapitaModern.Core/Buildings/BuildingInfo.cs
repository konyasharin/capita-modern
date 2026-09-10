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

    /// <summary>Сколько лет служит, прежде чем осыплется.</summary>
    /// <remarks>У всех разный: здания сорок лет, гидростанция полвека, обычный завод
    /// восемнадцать. Одна константа на всех давала стройке втрое больший объём, чем
    /// нужно, — и мир не мог её прокормить.</remarks>
    public int LifeYears { get; init; } = 20;

    /// <summary>Человеко-дней на постройку. В стоимость материалов не входит: цемент это
    /// одно, а люди другое. Их цена — местная зарплата, поэтому в бедной стране та же
    /// стройка обходится дешевле, и это настоящее преимущество.</summary>
    public int BuildWorkers { get; init; }
    /// <summary>Без этого месторождения в области предприятие не работает. Пусто у всех,
    /// кроме добычи.</summary>
    public GoodType? RequiresDeposit { get; init; }
}
