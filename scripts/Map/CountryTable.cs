using System.Text.Json;
using System.Text.Json.Serialization;

/// <param name="Id">Тот же байт, что лежит в world.bin для каждой ячейки.</param>
/// <param name="Color">Индекс в палитре карты, а не сам цвет: соседи гарантированно
/// не совпадают, раскраска подобрана генератором.</param>
/// <param name="Cells">Площадь страны в ячейках карты.</param>
public sealed record MapCountry(
    int Id,
    string Name,
    string Iso,
    string Continent,
    int Color,
    int Cells
);

/// <summary>Справочник стран из data/map/countries.json.</summary>
public sealed class CountryTable
{
    private readonly MapCountry?[] _byId;

    public IReadOnlyList<MapCountry> All { get; }

    private CountryTable(IReadOnlyList<MapCountry> all)
    {
        All = all;
        _byId = new MapCountry?[all.Max(c => c.Id) + 1];

        foreach (var c in all)
        {
            _byId[c.Id] = c;
        }
    }

    public MapCountry? ById(int id) => id >= 0 && id < _byId.Length ? _byId[id] : null;

    public MapCountry? ByIso(string iso) =>
        All.FirstOrDefault(c => string.Equals(c.Iso, iso, StringComparison.OrdinalIgnoreCase));

    public static CountryTable FromJson(string json)
    {
        var meta = JsonSerializer.Deserialize<Meta>(json, Options)
            ?? throw new InvalidDataException("countries.json: пустой файл");

        if (meta.Countries.Count == 0)
        {
            throw new InvalidDataException("countries.json: нет ни одной страны");
        }

        return new CountryTable(meta.Countries);
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record Meta(
        int Width,
        int Height,
        [property: JsonPropertyName("countries")] List<MapCountry> Countries
    );
}
