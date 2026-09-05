using System.Numerics;

namespace CapitaModern.Core.Economy;

/// <summary>Количество товара. Внутри хранится в сотых долях единицы, чтобы завод мог
/// работать не на полную и не округляться до нуля.</summary>
public readonly record struct GoodAmount(long Raw) :
    IAdditionOperators<GoodAmount, GoodAmount, GoodAmount>,
    IComparisonOperators<GoodAmount, GoodAmount, bool>
{
    /// <summary>Сколько сотых в одной единице товара.</summary>
    public const int Scale = 10000;

    public static GoodAmount FromUnits(long units) => new(units * Scale);
    public double Units => (double)Raw / Scale;

    public static GoodAmount operator +(GoodAmount a, GoodAmount b) => new(a.Raw + b.Raw);
    public static GoodAmount operator -(GoodAmount a, GoodAmount b) => new(a.Raw - b.Raw);
    public static GoodAmount operator *(GoodAmount a, long b) => new(a.Raw * b);
    public static GoodAmount operator /(GoodAmount a, long b) => new(a.Raw / b);
    public static bool operator <(GoodAmount a, GoodAmount b) => a.Raw < b.Raw;
    public static bool operator >(GoodAmount a, GoodAmount b) => a.Raw > b.Raw;
    public static bool operator <=(GoodAmount a, GoodAmount b) => a.Raw <= b.Raw;
    public static bool operator >=(GoodAmount a, GoodAmount b) => a.Raw >= b.Raw;
}
