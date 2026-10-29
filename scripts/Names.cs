using System.Text.Json;
using CapitaModern.Core.World;
using Godot;

/// <summary>Русские названия стран и регионов из data/ui; чего нет — остаётся английское.</summary>
public sealed class Names
{
    private Dictionary<string, string> _countries = [];
    private Dictionary<string, string> _regions = [];

    public static Names Load() => new()
    {
        _countries = Read("res://data/ui/countries-ru.json"),
        _regions = Read("res://data/ui/regions-ru.json")
            .Concat(Read("res://data/ui/regions-extra-ru.json"))
            .GroupBy(pair => pair.Key)
            .ToDictionary(g => g.Key, g => g.Last().Value),
    };

    public string Of(Country country) => _countries.GetValueOrDefault(country.Iso, country.Name);
    public string Of(Region region) => _regions.GetValueOrDefault(region.Name, region.Name);

    private static Dictionary<string, string> Read(string path)
    {
        using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(path));
        return doc.RootElement.GetProperty("names").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? p.Name);
    }
}
