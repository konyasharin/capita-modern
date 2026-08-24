using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Реальные административные области (Natural Earth admin-1), укрупнённые под размер
/// игровой ячейки. Единицы просмотра статистики, а не игровые сущности: владение
/// по-прежнему определяется полем под точкой, поэтому фронт может разрезать область.
/// </summary>
public readonly record struct MapRegion(
    int Id,
    byte Country,
    string Name,
    float CenterX,
    float CenterY,
    int Cells,
    int Population
);

public sealed class RegionMap
{
    public const ushort None = 0;

    /// <summary>Номер региона для каждой ячейки карты, 0 — океан.</summary>
    public ushort[] Cell { get; }

    public IReadOnlyList<MapRegion> Regions { get; }

    private RegionMap(ushort[] cell, IReadOnlyList<MapRegion> regions)
    {
        Cell = cell;
        Regions = regions;
    }

    /// <summary>
    /// Читает нарезку из data/map/regions.bin и описания из regions.json.
    /// </summary>
    /// <remarks>
    /// Геометрия возится готовой, а не восстанавливается на месте: у настоящих
    /// административных границ произвольная форма, из центра области её не вывести.
    /// </remarks>
    public static RegionMap FromData(WorldMap map, string json, byte[] binary)
    {
        var file = JsonSerializer.Deserialize<RegionsFile>(json, Options)
            ?? throw new InvalidDataException("regions.json: пустой файл");

        if (file.Width != map.Width || file.Height != map.Height)
        {
            throw new InvalidDataException(
                $"regions.json собран для карты {file.Width}x{file.Height}, "
                + $"а world.bin даёт {map.Width}x{map.Height}");
        }

        if (binary.Length < 12 || binary[0] != 'C' || binary[1] != 'M' || binary[2] != 'R' || binary[3] != '1')
        {
            throw new InvalidDataException("regions.bin: не тот формат, ожидался CMR1");
        }

        var width = BitConverter.ToInt32(binary.AsSpan(4, 4));
        var height = BitConverter.ToInt32(binary.AsSpan(8, 4));
        var cells = width * height;

        if (width != map.Width || height != map.Height || binary.Length != 12 + cells * 2)
        {
            throw new InvalidDataException($"regions.bin: размер не сходится ({width}x{height})");
        }

        var cell = new ushort[cells];
        Buffer.BlockCopy(binary, 12, cell, 0, cells * 2);

        var regions = new List<MapRegion>(file.Regions.Count);

        foreach (var d in file.Regions)
        {
            regions.Add(new MapRegion(d.Id, d.Country, d.Name, d.Lon, d.Lat, d.Cells, d.Population));
        }

        return new RegionMap(cell, regions);
    }

    /// <summary>Номер региона в двух каналах: 16 бит не влезают в один байт текстуры.</summary>
    public byte[] ToRg8()
    {
        var data = new byte[Cell.Length * 2];

        for (var i = 0; i < Cell.Length; i++)
        {
            data[i * 2] = (byte)(Cell[i] & 0xFF);
            data[i * 2 + 1] = (byte)(Cell[i] >> 8);
        }

        return data;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record RegionDto(
        int Id,
        byte Country,
        string Name,
        int Cells,
        float Lon,
        float Lat,
        int Population
    );

    private sealed record RegionsFile(
        int Width,
        int Height,
        [property: JsonPropertyName("regions")] List<RegionDto> Regions
    );
}
