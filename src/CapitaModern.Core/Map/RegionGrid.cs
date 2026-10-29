namespace CapitaModern.Core.Map;

/// <summary>Нарезка карты на регионы: номер региона в каждой ячейке и кто с кем граничит.</summary>
public sealed class RegionGrid
{
    public const ushort None = 0;

    public int Width { get; }
    public int Height { get; }

    /// <summary>Номер региона для каждой ячейки карты, 0 — океан.</summary>
    public ushort[] Cell { get; }

    /// <summary>Центр региона в ячейках карты, по индексу региона.</summary>
    public (float X, float Y)[] Center { get; }

    /// <summary>Соседи по суше, по индексу региона.</summary>
    public int[][] Neighbours { get; }

    private RegionGrid(int width, int height, ushort[] cell, (float, float)[] center, int[][] neighbours)
    {
        Width = width;
        Height = height;
        Cell = cell;
        Center = center;
        Neighbours = neighbours;
    }

    /// <summary>Читает regions.bin. Индекс региона в массивах совпадает с его id.</summary>
    public static RegionGrid FromBytes(ReadOnlySpan<byte> binary, int regionCount)
    {
        if (binary.Length < 12 || binary[0] != 'C' || binary[1] != 'M' || binary[2] != 'R' || binary[3] != '1')
        {
            throw new InvalidDataException("regions.bin: не тот формат, ожидался CMR1");
        }

        var width = BitConverter.ToInt32(binary[4..8]);
        var height = BitConverter.ToInt32(binary[8..12]);
        var cells = width * height;

        if (binary.Length != 12 + cells * 2)
        {
            throw new InvalidDataException($"regions.bin: размер не сходится ({width}x{height})");
        }

        var cell = new ushort[cells];
        for (var i = 0; i < cells; i++) cell[i] = BitConverter.ToUInt16(binary.Slice(12 + i * 2, 2));

        var size = regionCount + 1;
        var sumX = new double[size];
        var sumY = new double[size];
        var count = new int[size];
        var links = new HashSet<long>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var id = cell[y * width + x];
                if (id == None || id >= size) continue;

                sumX[id] += x;
                sumY[id] += y;
                count[id]++;

                // Мир замкнут по долготе: правый сосед последней колонки — первая.
                Link(links, id, cell[y * width + (x + 1) % width], size);
                if (y + 1 < height) Link(links, id, cell[(y + 1) * width + x], size);
            }
        }

        var lists = new List<int>[size];
        for (var i = 0; i < size; i++) lists[i] = [];

        foreach (var pair in links)
        {
            var a = (int)(pair >> 16);
            var b = (int)(pair & 0xFFFF);
            lists[a].Add(b);
            lists[b].Add(a);
        }

        var center = new (float, float)[size];
        for (var i = 0; i < size; i++)
        {
            center[i] = count[i] > 0 ? ((float)(sumX[i] / count[i]), (float)(sumY[i] / count[i])) : (0f, 0f);
        }

        return new RegionGrid(width, height, cell, center,
            lists.Select(list => list.OrderBy(n => n).ToArray()).ToArray());
    }

    private static void Link(HashSet<long> links, ushort a, ushort b, int size)
    {
        if (b == None || b == a || b >= size) return;

        var (lo, hi) = a < b ? (a, b) : (b, a);
        links.Add(((long)lo << 16) | hi);
    }

    /// <summary>Номер региона в двух байтах на ячейку — для текстуры.</summary>
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
