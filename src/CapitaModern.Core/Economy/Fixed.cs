using System.Numerics;

namespace CapitaModern.Core.Economy;

/// <summary>Во сколько долей единицы измеряется величина.</summary>
public interface IUnitScale
{
    static abstract int Scale { get; }
}

/// <summary>Товары: завод должен уметь работать не на полную и не округляться до нуля.</summary>
public readonly struct Goods : IUnitScale
{
    public static int Scale => 10000;
}

/// <summary>Люди: прирост в сутки дробный, а терять его нельзя.</summary>
public readonly struct People : IUnitScale
{
    public static int Scale => 1000;
}

/// <summary>
/// Дробная величина в целых числах: хранится долями единицы, считается точно.
/// </summary>
/// <remarks>
/// Плавающей точки нет намеренно — она копит ошибку за тысячи тиков и ломает
/// совпадение сейва с оригиналом. Шкала приходит типом, поэтому товары и людей
/// нельзя перепутать: это разные типы, а не одно число с разным смыслом.
/// </remarks>
/// <typeparam name="TUnit">Что измеряем — от него зависит дробность.</typeparam>
public readonly record struct Fixed<TUnit>(long Raw) :
    IAdditionOperators<Fixed<TUnit>, Fixed<TUnit>, Fixed<TUnit>>,
    IComparisonOperators<Fixed<TUnit>, Fixed<TUnit>, bool>
    where TUnit : IUnitScale
{
    /// <summary>Сколько долей в одной целой единице.</summary>
    public static int Scale => TUnit.Scale;

    public static Fixed<TUnit> FromWhole(long whole) => new(whole * TUnit.Scale);

    /// <summary>Целая часть — то, что имеет смысл показывать как количество.</summary>
    public long Whole => Raw / TUnit.Scale;

    /// <summary>Точное значение с дробью, для показа и проверок.</summary>
    public double Exact => (double)Raw / TUnit.Scale;

    public static Fixed<TUnit> operator +(Fixed<TUnit> a, Fixed<TUnit> b) => new(a.Raw + b.Raw);
    public static Fixed<TUnit> operator -(Fixed<TUnit> a, Fixed<TUnit> b) => new(a.Raw - b.Raw);
    public static Fixed<TUnit> operator *(Fixed<TUnit> a, long b) => new(a.Raw * b);
    public static Fixed<TUnit> operator /(Fixed<TUnit> a, long b) => new(a.Raw / b);
    public static bool operator <(Fixed<TUnit> a, Fixed<TUnit> b) => a.Raw < b.Raw;
    public static bool operator >(Fixed<TUnit> a, Fixed<TUnit> b) => a.Raw > b.Raw;
    public static bool operator <=(Fixed<TUnit> a, Fixed<TUnit> b) => a.Raw <= b.Raw;
    public static bool operator >=(Fixed<TUnit> a, Fixed<TUnit> b) => a.Raw >= b.Raw;
}
