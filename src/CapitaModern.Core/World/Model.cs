namespace CapitaModern.Core.World;

/// <summary>Кто строит в стране. Промежуточные строи появятся вместе с законами.</summary>
public enum EconomicSystem
{
    /// <summary>Строят компании на свои деньги; государство может вкладываться само.</summary>
    Market,

    /// <summary>Частных компаний нет, строит только государство.</summary>
    Planned,
}

/// <summary>Сколько зданий одного типа стоит в регионе и чьи они.</summary>
public sealed class PlantStack(int building, int count, int company)
{
    public int Building { get; } = building;
    public int Count { get; set; } = count;

    /// <summary>Компания-владелец; <see cref="Company.State"/> — государство.</summary>
    public int Company { get; set; } = company;

    /// <summary>Какой долей мощности стопка работала на прошлой неделе.</summary>
    public double Load { get; set; }
}

public sealed class Region
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required int Owner { get; set; }

    /// <summary>Кто держит регион сейчас. На войне расходится с владельцем.</summary>
    public required int Controller { get; set; }

    public required long Population { get; set; }
    public required Dictionary<int, double> Deposits { get; init; }
    public required int[] Neighbours { get; init; }
    public required float X { get; init; }
    public required float Y { get; init; }

    public List<PlantStack> Plants { get; } = [];

    /// <summary>Связан ли регион со столицей своего держателя через его же регионы.</summary>
    public bool Supplied { get; set; } = true;

    public int Count(int building) => Plants.Where(p => p.Building == building).Sum(p => p.Count);
}

public sealed class Company
{
    public const int State = -1;

    public required int Id { get; init; }
    public required string Name { get; init; }
    public required int Country { get; set; }
    public required string Sector { get; init; }
    public double Cash { get; set; }
    public double ProfitWeek { get; set; }

    /// <summary>Прибыль за прошлую неделю — её видит интерфейс, текущая к концу недели обнуляется.</summary>
    public double ProfitLast { get; set; }

    /// <summary>Сколько недель подряд касса в минусе.</summary>
    public int WeeksInRed { get; set; }

    public bool Alive { get; set; } = true;
    public int BuiltAt { get; set; } = -100_000;
}

public sealed class Project
{
    public required int Building { get; init; }
    public required int Region { get; init; }
    public required int Country { get; init; }
    public required int Company { get; init; }
    public required double DaysTotal { get; init; }
    public double DaysDone { get; set; }
}

public sealed class Country
{
    public required int Id { get; init; }
    public required string Iso { get; init; }
    public required string Name { get; init; }
    public EconomicSystem System { get; set; } = EconomicSystem.Market;

    /// <summary>Строит ли государство само, без игрока.</summary>
    public bool AutoBuild { get; set; } = true;

    public int Capital { get; set; }
    public double TaxRate { get; set; } = 0.20;
    public double DefenceShare { get; set; }

    /// <summary>Казна в своих деньгах.</summary>
    public double Treasury { get; set; }

    public required double[] Stock { get; init; }

    /// <summary>Сколько товара съедает за сутки миллион её жителей: (товар, ставка), от дохода на душу.</summary>
    public (int Good, double Rate)[] Needs { get; set; } = [];

    /// <summary>То же на старте; нынешние нужды растут от него вместе с доходом.</summary>
    public (int Good, double Rate)[] Needs0 { get; set; } = [];

    /// <summary>Возможный выпуск на душу на старте, в базовых ценах: мерка достатка.</summary>
    public double IncomeStart { get; set; }

    /// <summary>Что дали бы за год все заводы страны на полном ходу, в базовых ценах.</summary>
    public double Potential { get; set; }

    /// <summary>Множитель цены товара к базовой: от дней запаса, двигается медленно.</summary>
    public required double[] PriceMult { get; init; }

    /// <summary>Сглаженный суточный расход товара — от него считаются дни запаса.</summary>
    public required double[] DailyUse { get; init; }

    /// <summary>Сколько всего просили за неделю: заводы, люди, стройка, армия.</summary>
    public required double[] DemandWeek { get; init; }

    /// <summary>Суточный расход при полной загрузке заводов и полных нуждах людей — мерка для полки.</summary>
    public required double[] CapacityUse { get; init; }

    /// <summary>Суточный выпуск при полной загрузке, с учётом рук.</summary>
    public required double[] CapacityOut { get; init; }

    /// <summary>Сглаженный суточный вывоз — его полка тоже учитывает.</summary>
    public required double[] ExportUse { get; init; }

    /// <summary>Какую долю просимого сырья заводы получили за неделю, по товарам.</summary>
    public required double[] Fill { get; init; }

    public required double[] ProducedWeek { get; init; }
    public required double[] ConsumedWeek { get; init; }
    public required double[] ImportedWeek { get; init; }
    public required double[] ExportedWeek { get; init; }

    /// <summary>Общий уровень цен, на старте единица.</summary>
    public double PriceLevel { get; set; } = 1.0;

    /// <summary>Инфляция в годовых долях.</summary>
    public double Inflation { get; set; } = 0.02;

    public double PrintedWeek { get; set; }
    public double TaxesWeek { get; set; }
    public double SpentWeek { get; set; }

    public double TaxesLast { get; set; }
    public double SpentLast { get; set; }

    /// <summary>ВВП за год в базовых ценах, тысячи долларов: меняется только с выпуском.</summary>
    public double GdpReal { get; set; }

    /// <summary>Доля нужд населения, которую удалось закрыть за неделю.</summary>
    public double Satisfaction { get; set; } = 1.0;

    /// <summary>Привычная нехватка, сглаженная за полгода: цены гонит только её рост.</summary>
    public double ShortageUsual { get; set; }

    public double Workforce0 { get; set; }
    public double WorkNeed0 { get; set; }

    public double Price(int good, double basePrice) => basePrice * PriceMult[good] * PriceLevel;
}
