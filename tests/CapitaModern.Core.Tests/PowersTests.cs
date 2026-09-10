using CapitaModern.Core.Economy;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Дробная степень целыми числами. Проверяется по значениям, которые известны
/// точно: иначе в такой арифметике легко ошибиться и не заметить.</summary>
public class PowersTests
{
    private static long Of(double value) => (long)(value * Powers.Scale);
    private static double Read(long raw) => (double)raw / Powers.Scale;

    [Theory]
    [InlineData(4.0, 2.0)]
    [InlineData(100.0, 10.0)]
    [InlineData(2.0, 1.4142)]
    [InlineData(0.25, 0.5)]
    public void SquareRootIsExactEnough(double value, double expected)
    {
        Assert.Equal(expected, Read(Powers.Sqrt(Of(value))), 3);
    }

    [Theory]
    [InlineData(30.0, 140, 116.9)]
    [InlineData(30.0, 30, 2.77)]
    [InlineData(30.0, 100, 30.0)]
    [InlineData(4.0, 50, 2.0)]
    [InlineData(8.0, 200, 64.0)]
    [InlineData(0.5, 140, 0.379)]
    public void PowMatchesKnownValues(double value, int exponent, double expected)
    {
        // Сотые доли процента набегают на корнях, поэтому сверка с допуском в процент.
        Assert.Equal(expected, Read(Powers.Pow(Of(value), exponent)), expected / 100);
    }

    [Fact]
    public void ZeroExponentIsOne()
    {
        Assert.Equal(Powers.Scale, Powers.Pow(Of(7), 0));
    }

    [Fact]
    public void OneStaysOne()
    {
        Assert.Equal(Powers.Scale, Powers.Pow(Powers.Scale, 140));
    }

    [Fact]
    public void NothingBreaksOnZero()
    {
        Assert.Equal(0, Powers.Pow(0, 140));
        Assert.Equal(0, Powers.Sqrt(0));
    }

    /// <summary>Одинаковый вход — одинаковый выход, сколько ни считай.</summary>
    [Fact]
    public void PowIsReproducible()
    {
        Assert.Equal(Powers.Pow(Of(13.7), 137), Powers.Pow(Of(13.7), 137));
    }
}
