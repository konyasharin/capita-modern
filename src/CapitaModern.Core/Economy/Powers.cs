namespace CapitaModern.Core.Economy;

/// <summary>Возведение в дробную степень целыми числами.</summary>
/// <remarks>
/// Плавающей точки в модели нет намеренно: сейв должен сходиться с оригиналом до бита, а
/// у разных машин разные последние разряды. Здесь целая часть показателя берётся
/// умножением, дробная — двоичным разложением через целочисленный корень.
///
/// Нужно не только потреблению: производственная функция капитала тоже степенная.
/// </remarks>
public static class Powers
{
    /// <summary>Величины хранятся в десятитысячных: 10000 — это единица.</summary>
    public const long Scale = 10_000;

    /// <summary>Сколько двоичных разрядов дроби берём. Каждый — ещё один корень; после
    /// двенадцати добавка меньше погрешности самих данных.</summary>
    private const int FractionBits = 12;

    /// <summary>Возводит в степень <paramref name="exponent"/>, заданную в сотых:
    /// 140 означает 1.4.</summary>
    /// <summary>Уже посчитанные степени. За тик их запрашивают десятки тысяч раз, а
    /// разных пар «основание и показатель» среди них считанные сотни: считать заново —
    /// половина времени торговли. Своя память у каждого потока: тесты идут параллельно,
    /// а общий словарь не выдержал бы двух миров разом.</summary>
    [ThreadStatic]
    private static Dictionary<long, long>? _known;

    /// <summary>Дальше память чистится: ключей может набежать сколько угодно.</summary>
    private const int Remembered = 200_000;

    /// <summary>То же, что <see cref="Pow"/>, но помнит посчитанное.</summary>
    /// <remarks>Основание округляется до сотых доли: точнее незачем, а попаданий в
    /// память становится на порядок больше.</remarks>
    public static long PowCached(long value, int exponent)
    {
        if (value < 0 || exponent < 0 || exponent > 0xFFFF) return Pow(value, exponent);

        var rounded = value / 100 * 100;
        var key = rounded << 16 | (uint)exponent;

        var known = _known ??= new Dictionary<long, long>();
        if (known.TryGetValue(key, out var ready)) return ready;

        if (known.Count >= Remembered) known.Clear();

        var result = Pow(rounded, exponent);
        known[key] = result;

        return result;
    }

    public static long Pow(long value, int exponent)
    {
        if (value <= 0) return 0;
        if (exponent <= 0) return Scale;

        var result = Scale;
        for (var i = 0; i < exponent / 100; i++) result = result * value / Scale;

        // Дробная часть в двоичных долях: 0.5, 0.25, 0.125 — это последовательные корни.
        var fraction = (long)(exponent % 100) * (1 << FractionBits) / 100;
        var root = value;
        for (var bit = 1 << (FractionBits - 1); bit > 0 && fraction > 0; bit >>= 1)
        {
            root = Sqrt(root);
            if (fraction < bit) continue;

            result = result * root / Scale;
            fraction -= bit;
        }

        return result;
    }

    /// <summary>Корень в тех же десятитысячных.</summary>
    public static long Sqrt(long value)
    {
        if (value <= 0) return 0;

        // sqrt(v / Scale) × Scale — это sqrt(v × Scale), и всё остаётся целым.
        var target = value * Scale;
        var guess = target;
        var next = (guess + 1) / 2;

        // Ньютон: сходится за десяток шагов и всегда вниз, поэтому условие строгое.
        while (next < guess)
        {
            guess = next;
            next = (guess + target / guess) / 2;
        }

        return guess;
    }
}
