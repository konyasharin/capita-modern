using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Politics;

namespace CapitaModern.Core.World;

/// <summary>Состояние партии. Владеет областями и странами, остальные берут их ссылкой.</summary>
public sealed class GameWorld
{
    public BuildingCatalog Buildings { get; }

    /// <summary>Мировой рынок: один на партию, цены на нём свои.</summary>
    public WorldMarket Market { get; }

    /// <summary>Насколько спрос отзывается на цену. Одна на мир: свойство товара,
    /// а не страны.</summary>
    public Elasticity Elasticity { get; }

    /// <summary>Кто кому даёт в долг. Один на партию, как и товарный рынок.</summary>
    public CreditMarket Credit { get; } = new();

    /// <summary>Кто кому друг. Пока влияет на кредит, дальше — на санкции и войну.</summary>
    public Relations Relations { get; }

    /// <summary>Насколько хорошо страна умеет производить. Вся разница в
    /// производительности между странами лежит здесь: завод везде одинаковый.</summary>
    public Efficiency Efficiency { get; }
    /// <summary>Что и сколько нужно населению. Зависит от дохода, а не только от числа душ.</summary>
    public readonly Needs Needs;

    private readonly Region[] _regions;
    private readonly Country[] _countries;
    private readonly Region[] _regionsById;
    private readonly Country[] _countriesById;
    private readonly Population[] _populationsByCountry;

    public GameWorld(
        Region[] regions,
        Country[] countries,
        BuildingCatalog buildings,
        Needs needs,
        WorldMarket market,
        Elasticity elasticity,
        Relations? relations = null,
        Efficiency? efficiency = null
    ) {
        Buildings = buildings;
        Market = market;
        Elasticity = elasticity;
        Relations = relations ?? new Relations();
        Efficiency = efficiency ?? new Efficiency();
        Needs = needs;
        _regions = regions;
        _countries = countries;

        _regionsById = new Region[regions.Max(region => region.Id) + 1];
        foreach (var region in regions) _regionsById[region.Id] = region;

        _countriesById = new Country[countries.Max(country => country.Id) + 1];
        foreach (var country in countries) _countriesById[country.Id] = country;

        _populationsByCountry = new Population[_countries.Max(country => country.Id) + 1];
        UpdatePopulations();
    }

    public IReadOnlyList<Region> Regions => _regions;
    public Region RegionById(int id) =>
        id > 0 && id < _regionsById.Length && _regionsById[id] is {} region ?
            region :
            throw new ArgumentOutOfRangeException(nameof(id), id, "Региона не найдено");

    public IReadOnlyList<Country> Countries => _countries;
    public Country CountryById(byte id) =>
        id < _countriesById.Length && _countriesById[id] is {} country ?
            country :
            throw new ArgumentOutOfRangeException(nameof(id), id, "Страны не найдено");

    public Population PopulationOf(byte country) =>
        country < _populationsByCountry.Length ?
            _populationsByCountry[country] :
            throw new ArgumentOutOfRangeException(nameof(country), country, "Страны не найдено");

    /// <summary>Сколько людей страна может поставить на производство.</summary>
    /// <remarks>Не всё население: дети, старики и те, кто занят вне нашей модели —
    /// в услугах и на стройке. В жизни рабочая сила примерно 44% населения.</remarks>
    public long WorkersOf(byte country) => PopulationOf(country).Whole * WorkingShare / 100;

    /// <summary>Доля населения в рабочей силе, в процентах.</summary>
    public const int WorkingShare = 44;

    public void UpdatePopulations()
    {
        Array.Clear(_populationsByCountry);
        foreach (var region in _regions)
        {
            _populationsByCountry[region.LargestOwner] += region.Demographics.Population;
        }
    }

    public void TransferCells(int region, byte from, byte to, int count)
    {
        _regionsById[region].TransferCells(from, to, count);
        UpdatePopulations();
    }
}
