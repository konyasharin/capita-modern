using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Loading;
using CapitaModern.Core.World;
using Godot;

/// <summary>Ход времени и состояние мира. Один тик — сутки.</summary>
/// <remarks>
/// Мир грузится из тех же данных, что и в счётном прогоне: игра и сверка обязаны считать
/// одно и то же, иначе сверять нечего.
/// </remarks>
public partial class GameLoop : Node
{
    /// <summary>Первый день партии. Данные мира собраны на этот год.</summary>
    public static readonly DateTime Start = new(2020, 1, 1);

    /// <summary>Сколько секунд идут игровые сутки при обычной скорости.</summary>
    private const double SecondsPerDay = 1.0;

    private double _left = SecondsPerDay;

    public GameWorld World { get; private set; } = null!;
    public Simulation Simulation { get; private set; } = null!;

    /// <summary>Сколько суток прошло с начала партии.</summary>
    public int Day { get; private set; }

    public DateTime Today => Start.AddDays(Day);

    /// <summary>За кого играем. Пока задано намертво, дальше будет выбор в меню.</summary>
    public byte Player { get; private set; }

    /// <summary>Уточнённое имя: у карты есть свой Country, это не он.</summary>
    public CapitaModern.Core.World.Country PlayerCountry => World.CountryById(Player);

    public override void _Ready()
    {
        World = Load();
        Simulation = new Simulation(World);
        Player = World.Countries.First(country => country.Iso == "RUS").Id;
    }

    public override void _Process(double delta)
    {
        _left -= delta;
        if (_left > 0)
        {
            return;
        }

        _left += SecondsPerDay;
        Simulation.Tick();
        Day++;
    }

    /// <summary>Население страны игрока, человек.</summary>
    public long Population => World.PopulationOf(Player).Whole;

    /// <summary>Годовой выпуск в постоянных ценах: добавленная стоимость за сутки на год.
    /// Меряется постоянными ценами, иначе ВВП скакал бы вместе с ценами.</summary>
    public double YearlyOutput =>
        Simulation.ValueAddedOf(Player, _constant).Exact * DaysInYear;

    /// <summary>Сколько в стране единиц производства.</summary>
    public long Plants => World.RegionsOf(Player)
        .SelectMany(region => region.BuildingsCount)
        .Sum(pair => (long)pair.Value);

    /// <summary>Во сколько раз цены ушли от начала партии, в процентах.</summary>
    public double Inflation => (Simulation.PriceLevelOf(Player) - PriceLevel.Scale) * 100.0 / PriceLevel.Scale;

    /// <summary>Что лежит в казне, в местных деньгах.</summary>
    public double Treasury => PlayerCountry.State.Treasury.Balance.Exact;

    private const int DaysInYear = 365;

    private Prices _constant = null!;

    private GameWorld Load()
    {
        string Read(string folder, string name) =>
            Godot.FileAccess.GetFileAsString($"res://data/{folder}/{name}");

        var world = WorldDataLoader.LoadWorld(
            Read("map", "countries.json"),
            Read("map", "regions.json"),
            Read("economy", "buildings.json"),
            Read("economy", "start-industry.json"),
            Read("economy", "consumption.json"),
            Read("economy", "prices.json"),
            Read("economy", "reserves.json"),
            Read("economy", "goods.json"),
            Read("economy", "key-rates.json"),
            Read("politics", "blocs.json"),
            Read("economy", "efficiency.json"),
            Read("economy", "money-supply.json"),
            Read("economy", "trade-costs.json"),
            Read("map", "neighbours.json"),
            Read("map", "basins.json"));

        // Постоянные цены снимаются на старте: по ним потом и меряется выпуск.
        _constant = new Prices(Enum.GetValues<GoodType>().ToDictionary(good => good, world.Market.Prices.Of));

        return world;
    }
}
