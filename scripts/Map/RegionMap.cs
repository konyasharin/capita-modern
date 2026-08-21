using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Условное деление суши на регионы — единицы просмотра статистики, а не игровые
/// сущности. Владение по-прежнему определяется полем под точкой, поэтому граница
/// войны может разрезать регион пополам.
/// </summary>
public readonly record struct MapRegion(
    int Id,
    byte Country,
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
    /// Восстанавливает нарезку из data/map/regions.json: в файле лежат только центры,
    /// а принадлежность ячеек считается тем же правилом, что и в генераторе —
    /// ближайший центр своей страны, при равных расстояниях побеждает меньший id.
    /// Генерировать нарезку заново здесь нельзя: получилась бы другая, и регион
    /// в ядре перестал бы совпадать с регионом на экране.
    /// </summary>
    public static RegionMap FromJson(WorldMap map, string json)
    {
        var file = JsonSerializer.Deserialize<RegionsFile>(json, Options)
            ?? throw new InvalidDataException("regions.json: пустой файл");

        if (file.Width != map.Width || file.Height != map.Height)
        {
            throw new InvalidDataException(
                $"regions.json собран для карты {file.Width}x{file.Height}, "
                + $"а world.bin даёт {map.Width}x{map.Height}");
        }

        var dtos = file.Regions;
        var byCountry = new Dictionary<byte, List<int>>();

        for (var i = 0; i < dtos.Count; i++)
        {
            if (!byCountry.TryGetValue(dtos[i].Country, out var list))
            {
                list = [];
                byCountry[dtos[i].Country] = list;
            }

            list.Add(i);
        }

        var cell = new ushort[map.Owner.Length];
        var counts = new int[dtos.Count];

        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var at = y * map.Width + x;
                var country = map.Owner[at];

                if (country == WorldMap.Ocean || !byCountry.TryGetValue(country, out var seeds))
                {
                    continue;
                }

                var best = seeds[0];
                var bestDistance = long.MaxValue;

                foreach (var i in seeds)
                {
                    long dx = dtos[i].X - x;
                    long dy = dtos[i].Y - y;
                    var distance = dx * dx + dy * dy;

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = i;
                    }
                }

                cell[at] = (ushort)dtos[best].Id;
                counts[best]++;
            }
        }

        // Число ячеек в файле — контрольная сумма нарезки. Если оно не совпало,
        // правило разошлось с генератором, и дальше идти нельзя.
        for (var i = 0; i < dtos.Count; i++)
        {
            if (counts[i] != dtos[i].Cells)
            {
                throw new InvalidDataException(
                    $"регион {dtos[i].Id}: восстановлено {counts[i]} ячеек вместо {dtos[i].Cells}");
            }
        }

        var regions = new List<MapRegion>(dtos.Count);

        foreach (var d in dtos)
        {
            regions.Add(new MapRegion(d.Id, d.Country, d.X + 0.5f, d.Y + 0.5f, d.Cells, d.Population));
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

    private sealed record RegionDto(int Id, byte Country, int Cells, int X, int Y, int Population);

    private sealed record RegionsFile(
        int Width,
        int Height,
        [property: JsonPropertyName("regions")] List<RegionDto> Regions
    );
}
