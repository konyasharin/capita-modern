using CapitaModern.Core.Economy;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Банк раздаёт вклады и ничего не создаёт: сколько принесли, столько и есть.</summary>
public class BankTests
{
    [Fact]
    public void LendsOnlyWhatIsLeftAfterReserve()
    {
        var bank = new Bank();
        bank.Take(Money.FromWhole(1000));

        // Свободно не то, что принесли, а принесённое за вычетом резерва и умноженное на
        // мультипликатор: кредит создаёт вклад, и тот возвращается в банковскую систему.
        var free = Money.FromWhole(1000 * (100 - Bank.Reserve) / 100 * Bank.Multiplier);

        Assert.Equal(free, bank.Free);
        Assert.False(bank.Lend(free + Money.FromWhole(1)));
        Assert.True(bank.Lend(free));
        Assert.Equal(default, bank.Free);
    }

    [Fact]
    public void GivesBackNoMoreThanItHolds()
    {
        var bank = new Bank();
        bank.Take(Money.FromWhole(100));

        Assert.Equal(Money.FromWhole(100), bank.Give(Money.FromWhole(500)));
        Assert.Equal(default, bank.Deposits);
    }

    /// <summary>Невозврат съедает сперва доход банка, а потом и деньги вкладчиков.</summary>
    [Fact]
    public void WriteOffEatsEarningsThenDeposits()
    {
        var bank = new Bank();
        bank.Take(Money.FromWhole(1000));
        bank.Lend(Money.FromWhole(500));
        bank.Returned(default, Money.FromWhole(30));

        bank.WriteOff(Money.FromWhole(100));

        Assert.Equal(default, bank.Earned);
        Assert.Equal(Money.FromWhole(930), bank.Deposits);
        Assert.Equal(Money.FromWhole(400), bank.Lent);
    }

    [Fact]
    public void PayOutEmptiesEarnings()
    {
        var bank = new Bank();
        bank.Returned(default, Money.FromWhole(7));

        Assert.Equal(Money.FromWhole(7), bank.PayOut());
        Assert.Equal(default, bank.Earned);
    }
}
