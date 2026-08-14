/// <summary>
/// Поле владения: сетка ячеек, где у каждой есть владелец и прочность контроля.
/// Граница страны — изолиния уровня 0.5, см. docs/01-map.md.
/// </summary>
public sealed class WorldMap
{
    public const byte Ocean = 0;

    /// <summary>Широта верхнего края сетки. Мир обрезан: Антарктиды нет.</summary>
    public const double LatTop = 84.0;

    public int Width { get; }
    public int Height { get; }

    /// <summary>Id владельца ячейки, 0 — океан.</summary>
    public byte[] Owner { get; }

    /// <summary>Прочность владения, 0..1. Ровно 0.5 — линия границы.</summary>
    public float[] Control { get; }

    public double DegPerCell => 360.0 / Width;

    private WorldMap(int width, int height, byte[] owner)
    {
        Width = width;
        Height = height;
        Owner = owner;
        Control = new float[owner.Length];

        for (var i = 0; i < owner.Length; i++)
        {
            Control[i] = owner[i] == Ocean ? 0f : 1f;
        }
    }

    public int Index(int x, int y) => y * Width + x;

    public byte OwnerAt(int x, int y) => Owner[y * Width + x];

    public (double Lon, double Lat) ToGeo(int x, int y) =>
        (x * DegPerCell - 180.0 + DegPerCell * 0.5, LatTop - (y + 0.5) * DegPerCell);

    /// <summary>
    /// Читает world.bin, сгенерированный tools/gen-world.mjs.
    /// Файл не читается сам: в игре его отдаёт Godot из res://, в консоли — File.
    /// </summary>
    public static WorldMap FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
        {
            throw new InvalidDataException("world.bin: файл короче заголовка");
        }

        if (data[0] != 'C' || data[1] != 'M' || data[2] != 'W' || data[3] != '1')
        {
            throw new InvalidDataException("world.bin: не тот формат, ожидался CMW1");
        }

        var width = BitConverter.ToInt32(data[4..8]);
        var height = BitConverter.ToInt32(data[8..12]);
        var cells = width * height;

        if (width <= 0 || height <= 0 || data.Length != 12 + cells)
        {
            throw new InvalidDataException($"world.bin: размер не сходится ({width}x{height})");
        }

        return new WorldMap(width, height, data[12..].ToArray());
    }
}
