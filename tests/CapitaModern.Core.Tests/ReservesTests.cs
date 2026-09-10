using CapitaModern.Core.Economy;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Резервы — набор вложений, а не одно число. Эмитент хранится ради заморозки.</summary>
public class ReservesTests
{
    private const byte Usa = 1;
    private const byte Germany = 2;

    private static Money Whole(long n) => Money.FromWhole(n);

    private static Reserves With(params (ReserveKind Kind, byte Issuer, long Amount)[] parts) =>
        new(parts.Select(p => new Reserve(p.Kind, p.Issuer, Whole(p.Amount))));

    [Fact]
    public void EmptyReservesBuyNothing()
    {
        var reserves = new Reserves();

        Assert.Equal(default, reserves.Liquid);
        Assert.False(reserves.TrySpend(Whole(1)));
    }

    [Fact]
    public void HoldingsOfTheSameIssuerAddUp()
    {
        var reserves = With((ReserveKind.ForeignCurrency, Usa, 100), (ReserveKind.ForeignCurrency, Usa, 50));

        Assert.Single(reserves.Held);
        Assert.Equal(Whole(150), reserves.Value);
    }

    [Fact]
    public void DifferentIssuersAreKeptApart()
    {
        var reserves = With((ReserveKind.ForeignCurrency, Usa, 100), (ReserveKind.ForeignCurrency, Germany, 50));

        Assert.Equal(2, reserves.Held.Count);
        Assert.Equal(Whole(150), reserves.Liquid);
    }

    [Fact]
    public void SpendingTakesFromEverythingLiquid()
    {
        var reserves = With((ReserveKind.ForeignCurrency, Usa, 100), (ReserveKind.ForeignCurrency, Germany, 50));

        Assert.True(reserves.TrySpend(Whole(120)));
        Assert.Equal(Whole(30), reserves.Value);
    }

    [Fact]
    public void SpendingMoreThanThereIsLeavesReservesAlone()
    {
        var reserves = With((ReserveKind.ForeignCurrency, Usa, 100));

        Assert.False(reserves.TrySpend(Whole(101)));
        Assert.Equal(Whole(100), reserves.Value);
    }

    /// <summary>Тот самый рычаг: заморозили доллары, и половина резервов бесполезна.</summary>
    [Fact]
    public void FrozenIssuerCannotBeSpent()
    {
        var reserves = With((ReserveKind.ForeignCurrency, Usa, 100), (ReserveKind.ForeignCurrency, Germany, 40));
        reserves.Freeze(Usa);

        Assert.Equal(Whole(40), reserves.Liquid);
        Assert.Equal(Whole(140), reserves.Value);
        Assert.False(reserves.TrySpend(Whole(50)));
        Assert.True(reserves.TrySpend(Whole(40)));
    }

    /// <summary>Ради этого золото и держат: оно лежит дома, отнять его нельзя.</summary>
    [Fact]
    public void MetalAndCryptoSurviveAFreeze()
    {
        var reserves = With(
            (ReserveKind.ForeignCurrency, Usa, 100),
            (ReserveKind.Metal, Usa, 30),
            (ReserveKind.Crypto, Usa, 10));
        reserves.Freeze(Usa);

        Assert.Equal(Whole(40), reserves.Liquid);
    }

    [Fact]
    public void UnfreezingGivesTheMoneyBack()
    {
        var reserves = With((ReserveKind.ForeignCurrency, Usa, 100));
        reserves.Freeze(Usa);
        reserves.Unfreeze(Usa);

        Assert.Equal(Whole(100), reserves.Liquid);
    }

    [Fact]
    public void NegativeAmountIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Reserves().Add(ReserveKind.ForeignCurrency, Usa, new Money(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Reserves().TrySpend(new Money(-1)));
    }

    /// <summary>Настоящие данные: резервы разложены по валютам и сходятся с суммой.</summary>
    [Fact]
    public void RealDataSplitsReservesByIssuer()
    {
        var world = WorldDataTests.Shared.Value;
        var usa = world.Countries.First(country => country.Iso == "USA");

        Assert.True(usa.State.Treasury.Reserves.Held.Count > 1, "резервы не разложены по валютам");
        Assert.Equal(
            usa.State.Treasury.Reserves.Value,
            usa.State.Treasury.Reserves.Held.Aggregate(default(Money), (sum, held) => sum + held.Amount));
    }

    /// <summary>Заморозка доллара должна ощутимо бить по чужим резервам: в нём их больше
    /// половины.</summary>
    [Fact]
    public void FreezingTheDollarHurtsRealCountries()
    {
        var world = WorldDataTests.Shared.Value;
        var usa = world.Countries.First(country => country.Iso == "USA").Id;
        var russia = world.Countries.First(country => country.Iso == "RUS").State.Treasury.Reserves;

        var before = russia.Liquid;
        russia.Freeze(usa);
        var after = russia.Liquid;
        russia.Unfreeze(usa);

        Assert.True(after.Raw * 2 < before.Raw, "доллар должен быть больше половины резервов");
    }
}
