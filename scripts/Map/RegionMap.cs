/// <summary>
/// Условное деление суши на регионы — единицы просмотра статистики, а не игровые
/// сущности. Владение по-прежнему определяется полем под точкой, поэтому граница
/// войны может разрезать регион пополам.
/// </summary>
public readonly record struct MapRegion(int Id, byte Country, float CenterX, float CenterY, int Cells);

public sealed class RegionMap
{
    public const ushort None = 0;

    /// <summary>Средний шаг между центрами регионов, в ячейках карты.</summary>
    private const int Step = 13;

    /// <summary>Ниже этого числа центров у страны выгоднее перебор, чем поиск по сетке.</summary>
    private const int SmallCountry = 24;

    /// <summary>Номер региона для каждой ячейки карты, 0 — океан.</summary>
    public ushort[] Cell { get; }

    public IReadOnlyList<MapRegion> Regions { get; }

    private RegionMap(ushort[] cell, IReadOnlyList<MapRegion> regions)
    {
        Cell = cell;
        Regions = regions;
    }

    /// <summary>
    /// Диаграмма Вороного по центрам, разбросанным равномерно с дрожанием: регионы
    /// выходят округлыми и близкими по размеру, но без сеточной регулярности.
    /// Ближайший центр ищется только среди своей страны, поэтому регионы никогда
    /// не пересекают государственную границу.
    /// </summary>
    public static RegionMap Build(WorldMap map, int seed)
    {
        var random = new Random(seed);

        var seedX = new List<int>();
        var seedY = new List<int>();
        var seedCountry = new List<byte>();
        var byCountry = new Dictionary<byte, List<int>>();

        void AddSeed(int x, int y, byte country)
        {
            var index = seedX.Count;

            seedX.Add(x);
            seedY.Add(y);
            seedCountry.Add(country);

            if (!byCountry.TryGetValue(country, out var list))
            {
                list = [];
                byCountry[country] = list;
            }

            list.Add(index);
        }

        for (var gy = 0; gy < map.Height; gy += Step)
        {
            for (var gx = 0; gx < map.Width; gx += Step)
            {
                var x = Math.Min(map.Width - 1, gx + random.Next(Step));
                var y = Math.Min(map.Height - 1, gy + random.Next(Step));
                var owner = map.OwnerAt(x, y);

                if (owner != WorldMap.Ocean)
                {
                    AddSeed(x, y, owner);
                }
            }
        }

        // Страна мельче шага сетки могла не получить ни одного центра — тогда её
        // ячейки остались бы вообще без региона.
        var missing = new Dictionary<byte, (int X, int Y)>();

        for (var i = 0; i < map.Owner.Length; i++)
        {
            var owner = map.Owner[i];

            if (owner != WorldMap.Ocean && !byCountry.ContainsKey(owner))
            {
                missing.TryAdd(owner, (i % map.Width, i / map.Width));
            }
        }

        foreach (var (country, point) in missing)
        {
            AddSeed(point.X, point.Y, country);
        }

        var buckets = BuildBuckets(map, seedX, seedY);
        var cells = new ushort[map.Owner.Length];
        var counts = new int[seedX.Count + 1];

        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var index = y * map.Width + x;
                var owner = map.Owner[index];

                if (owner == WorldMap.Ocean)
                {
                    continue;
                }

                var best = byCountry[owner].Count <= SmallCountry
                    ? NearestOf(byCountry[owner], seedX, seedY, x, y)
                    : NearestNear(buckets, seedX, seedY, seedCountry, owner, x, y, map);

                cells[index] = (ushort)(best + 1);
                counts[best + 1]++;
            }
        }

        var regions = new List<MapRegion>(seedX.Count);

        for (var i = 0; i < seedX.Count; i++)
        {
            regions.Add(new MapRegion(i + 1, seedCountry[i], seedX[i] + 0.5f, seedY[i] + 0.5f, counts[i + 1]));
        }

        return new RegionMap(cells, regions);
    }

    private static List<int>[] BuildBuckets(WorldMap map, List<int> seedX, List<int> seedY)
    {
        var wide = (map.Width + Step - 1) / Step;
        var high = (map.Height + Step - 1) / Step;
        var buckets = new List<int>[wide * high];

        for (var i = 0; i < seedX.Count; i++)
        {
            var slot = seedY[i] / Step * wide + seedX[i] / Step;

            (buckets[slot] ??= []).Add(i);
        }

        return buckets;
    }

    private static int NearestOf(List<int> candidates, List<int> seedX, List<int> seedY, int x, int y)
    {
        var best = candidates[0];
        var bestDistance = long.MaxValue;

        foreach (var i in candidates)
        {
            var dx = (long)(seedX[i] - x);
            var dy = (long)(seedY[i] - y);
            var distance = dx * dx + dy * dy;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private static int NearestNear(
        List<int>[] buckets,
        List<int> seedX,
        List<int> seedY,
        List<byte> seedCountry,
        byte country,
        int x,
        int y,
        WorldMap map)
    {
        var wide = (map.Width + Step - 1) / Step;
        var high = (map.Height + Step - 1) / Step;
        var cx = x / Step;
        var cy = y / Step;

        var best = -1;
        var bestDistance = long.MaxValue;

        for (var ring = 0; ring < wide + high; ring++)
        {
            for (var by = cy - ring; by <= cy + ring; by++)
            {
                if (by < 0 || by >= high)
                {
                    continue;
                }

                for (var bx = cx - ring; bx <= cx + ring; bx++)
                {
                    // Внутренние кольца уже просмотрены, обходим только рамку.
                    var onRing = ring == 0
                        || by == cy - ring || by == cy + ring
                        || bx == cx - ring || bx == cx + ring;

                    if (bx < 0 || bx >= wide || !onRing)
                    {
                        continue;
                    }

                    var bucket = buckets[by * wide + bx];
                    if (bucket is null)
                    {
                        continue;
                    }

                    foreach (var i in bucket)
                    {
                        if (seedCountry[i] != country)
                        {
                            continue;
                        }

                        var dx = (long)(seedX[i] - x);
                        var dy = (long)(seedY[i] - y);
                        var distance = dx * dx + dy * dy;

                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            best = i;
                        }
                    }
                }
            }

            // Ещё одно кольцо после находки: центр в соседнем бакете может оказаться ближе.
            if (best >= 0 && bestDistance <= (long)ring * ring * Step * Step)
            {
                break;
            }
        }

        return best;
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
}
