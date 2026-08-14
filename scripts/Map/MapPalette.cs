using System.Text.Json;
using Godot;

/// <summary>Цвета карты из data/map/palette.json.</summary>
public sealed class MapPalette
{
    public required Color Ocean { get; init; }
    public required Color OceanDeep { get; init; }
    public required Color OceanShelf { get; init; }
    public required Color Coast { get; init; }
    public required Color Border { get; init; }
    public required Color BorderWar { get; init; }
    public required Color Selected { get; init; }
    public required Color[] Countries { get; init; }

    public static MapPalette Load(string path)
    {
        var json = Godot.FileAccess.GetFileAsString(path);

        if (string.IsNullOrEmpty(json))
        {
            throw new InvalidDataException($"не читается {path}: {Godot.FileAccess.GetOpenError()}");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        static Color Hex(JsonElement value) =>
            new(value.GetString() ?? throw new InvalidDataException("ожидался цвет вида #rrggbb"));

        Color Read(string key) => Hex(root.GetProperty(key));

        var countries = root.GetProperty("countries")
            .EnumerateArray()
            .Select(Hex)
            .ToArray();

        return new MapPalette
        {
            Ocean = Read("ocean"),
            OceanDeep = Read("oceanDeep"),
            OceanShelf = Read("oceanShelf"),
            Coast = Read("coast"),
            Border = Read("border"),
            BorderWar = Read("borderWar"),
            Selected = Read("selected"),
            Countries = countries,
        };
    }
}
