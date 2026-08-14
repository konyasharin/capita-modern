using System.Text.Json;
using System.Text.Json.Serialization;

namespace CapitaModern.Core.Map;

public sealed record Country(
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
    private readonly Country?[] _byId;

    public IReadOnlyList<Country> All { get; }

    private CountryTable(IReadOnlyList<Country> all)
    {
        All = all;
        _byId = new Country?[all.Max(c => c.Id) + 1];

        foreach (var c in all)
        {
            _byId[c.Id] = c;
        }
    }

    public Country? ById(int id) => id >= 0 && id < _byId.Length ? _byId[id] : null;

    public Country? ByIso(string iso) =>
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
        [property: JsonPropertyName("countries")] List<Country> Countries
    );
}
