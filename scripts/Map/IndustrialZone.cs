/// <summary>
/// Промзона — группировка для игрока, а не игровая единица: владение по-прежнему
/// определяется полем под конкретной точкой, поэтому граница может резать зону
/// пополам. См. docs/03-industry.md.
/// </summary>
/// <param name="Country">Страна, которой зона принадлежала при основании.</param>
/// <param name="Center">Центр в координатах ячеек карты.</param>
/// <param name="Size">Сторона квадрата в ячейках карты.</param>
public readonly record struct IndustrialZone(
    int Id,
    byte Country,
    float CenterX,
    float CenterY,
    float Size
);

public static class ZoneGenerator
{
    /// <summary>Одна зона на столько ячеек территории.</summary>
    private const int CellsPerZone = 620;

    private const int MaxZonesPerCountry = 45;
    private const float MinSize = 2.2f;
    private const float MaxSize = 3.8f;

    /// <summary>Минимальный зазор между центрами зон, в ячейках.</summary>
    private const float MinGap = 6f;

    /// <summary>
    /// Расставляет промзоны по суше детерминированно: одинаковый seed — одинаковая
    /// карта, иначе сейв и загрузка дадут разные города.
    /// </summary>
    public static List<IndustrialZone> Generate(WorldMap map, int seed)
    {
        var byCountry = new Dictionary<byte, List<int>>();

        for (var i = 0; i < map.Owner.Length; i++)
        {
            var owner = map.Owner[i];
            if (owner == WorldMap.Ocean)
            {
                continue;
            }

            if (!byCountry.TryGetValue(owner, out var cells))
            {
                cells = [];
                byCountry[owner] = cells;
            }

            cells.Add(i);
        }

        var random = new Random(seed);
        var zones = new List<IndustrialZone>();
        var placed = new List<(float X, float Y)>();

        foreach (var (country, cells) in byCountry.OrderBy(pair => pair.Key))
        {
            var target = Math.Clamp(cells.Count / CellsPerZone, 1, MaxZonesPerCountry);
            placed.Clear();

            // Попыток больше, чем зон: часть кандидатов отсеется по берегу и зазору.
            for (var attempt = 0; attempt < target * 12 && placed.Count < target; attempt++)
            {
                var cell = cells[random.Next(cells.Count)];
                var x = cell % map.Width;
                var y = cell / map.Width;

                if (!IsInland(map, x, y) || TooClose(placed, x, y))
                {
                    continue;
                }

                var size = MinSize + (float)random.NextDouble() * (MaxSize - MinSize);
                var cx = x + 0.5f + ((float)random.NextDouble() - 0.5f) * 0.4f;
                var cy = y + 0.5f + ((float)random.NextDouble() - 0.5f) * 0.4f;

                placed.Add((cx, cy));
                zones.Add(new IndustrialZone(zones.Count, country, cx, cy, size));
            }
        }

        return zones;
    }

    /// <summary>Зона целиком на суше: у берега она свисала бы в воду.</summary>
    private static bool IsInland(WorldMap map, int x, int y)
    {
        var reach = (int)MathF.Ceiling(MaxSize * 0.5f);

        if (x < reach || y < reach || x >= map.Width - reach || y >= map.Height - reach)
        {
            return false;
        }

        for (var dy = -reach; dy <= reach; dy++)
        {
            for (var dx = -reach; dx <= reach; dx++)
            {
                if (map.OwnerAt(x + dx, y + dy) == WorldMap.Ocean)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TooClose(List<(float X, float Y)> placed, int x, int y)
    {
        foreach (var (px, py) in placed)
        {
            if (Math.Abs(px - x) < MinGap && Math.Abs(py - y) < MinGap)
            {
                return true;
            }
        }

        return false;
    }
}
