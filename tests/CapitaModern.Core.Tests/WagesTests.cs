using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Круг внутренних денег: государство платит зарплату, население покупает,
/// деньги возвращаются. Купить можно только на своё.</summary>
public class WagesTests
{
    private const BuildingType Farm = BuildingType.Farm;

    /// <summary>Все деньги населения: на руках и во вкладе.</summary>
    private static Money Wallet(Country country) => country.Households.Savings + country.Banks.Deposits;

    /// <summary>Ферма делает еду из ничего, население её ест. Цены единичные, чтобы
    /// считалось в уме: выпуск 10 еды — это добавленная стоимость в десятку.</summary>
    private static GameWorld Ready(long treasury, long savings, int labourShare = 55)
    {
        var country = new Country(
            1, "one", "ONE",
            new Producer(1, new Stock(new Dictionary<GoodType, GoodAmount>()),
                new Treasury(null, Money.FromWhole(treasury)), new Prices()),
            new Politics.Priorities(),
            Money.FromWhole(savings))
        { LabourShare = labourShare };

        return new GameWorld(
            [Build.Region(1, 1, new Dictionary<BuildingType, int> { [Farm] = 1 }, population: 1_000_000)],
            [country],
            Build.Catalog(Build.Info(Farm, outputs: new() { [GoodType.Food] = Build.Whole(10) },
                sector: Sector.Mining)),
            new Needs(new Dictionary<GoodType, GoodAmount> { [GoodType.Food] = Build.Whole(2) }),
            Build.Market(),
            new Elasticity());
    }

    [Fact]
    public void WagesMoveFromTreasuryToHouseholds()
    {
        var world = Ready(treasury: 10_000, savings: 0);
        var country = world.CountryById(1);

        new Simulation(world).Tick();

        // Выпуск 10 по цене 1, доля труда 55% — это 5.5 в кошелёк. Купить в первый тик
        // нечего: урожай ложится на склад уже после того, как население отоварилось.
        // Часть кошелька лежит во вкладе: у людей две копилки, а деньги те же.
        Assert.Equal(new Money(550), Wallet(country));
        Assert.Equal(Money.FromWhole(10_000) - new Money(550), country.State.Treasury.Balance);
    }

    /// <summary>В казне меньше, чем причитается: платят сколько есть, и это уже кризис
    /// бюджета, а не ошибка.</summary>
    [Fact]
    public void EmptyTreasuryPaysWhatItHas()
    {
        var world = Ready(treasury: 1, savings: 0);
        var country = world.CountryById(1);

        new Simulation(world).Tick();

        Assert.Equal(default, country.State.Treasury.Balance);
        Assert.Equal(Money.FromWhole(1), Wallet(country));
    }

    /// <summary>Ради этого всё и делалось: без денег еду не купить.</summary>
    [Fact]
    public void PeopleWithoutMoneyGoHungry()
    {
        var world = Ready(treasury: 0, savings: 0);
        var country = world.CountryById(1);

        var simulation = new Simulation(world);
        simulation.Tick();
        simulation.Tick();

        // Еда на складе есть, а купить не на что: склад не пустеет.
        Assert.True(country.State.Stock.Of(GoodType.Food) > default(GoodAmount), "еду съели бесплатно");
    }

    [Fact]
    public void PeopleWithMoneyEatAndPay()
    {
        var world = Ready(treasury: 0, savings: 1000);
        var country = world.CountryById(1);

        var simulation = new Simulation(world);
        simulation.Tick();
        simulation.Tick();

        // Сами сбережения при этом не тают: в казне пусто, зарплат нет, и вся выручка тем
        // же тиком возвращается владельцам — тем же людям.
        Assert.True(simulation.SalesOf(1) > default(Money), "население ничего не купило");
        Assert.True(simulation.ProfitOf(1) > default(Money), "выручка не вернулась владельцам");
    }

    /// <summary>Местные деньги не появляются ниоткуда: сколько было, столько и есть.</summary>
    [Fact]
    public void LocalMoneyIsConserved()
    {
        var world = Ready(treasury: 5000, savings: 3000);
        var country = world.CountryById(1);

        var simulation = new Simulation(world);

        long All() => country.State.Treasury.Balance.Raw + Wallet(country).Raw;

        var before = All();

        for (var tick = 0; tick < 100; tick++) simulation.Tick();

        Assert.Equal(before, All());
    }

    /// <summary>Зарплату платят с проданного, а не со всего выпуска: то, что легло на
    /// склад, денег в кассу не принесло.</summary>
    [Fact]
    public void WagesFollowWhatWasSold()
    {
        var world = Ready(treasury: 10_000, savings: 0);
        var country = world.CountryById(1);

        var simulation = new Simulation(world);
        simulation.Tick();

        // Первый тик считается по выпуску: продавать ещё нечего, полки пусты.
        Assert.Equal(new Money(550), country.Payroll);
        Assert.Equal(default, simulation.DemandOf(1));

        // Второй платит с выручки первого, а её не было.
        simulation.Tick();

        Assert.Equal(default, country.Payroll);
    }

    [Fact]
    public void BudgetIsSalesMinusWages()
    {
        var world = Ready(treasury: 10_000, savings: 10_000);
        var country = world.CountryById(1);

        var simulation = new Simulation(world);
        var before = country.State.Treasury.Balance;
        simulation.Tick();

        Assert.Equal(country.State.Treasury.Balance - before, simulation.BudgetOf(1));
    }

    [Fact]
    public void HigherLabourShareMeansHigherWages()
    {
        Money PaidAt(int share)
        {
            var world = Ready(treasury: 10_000, savings: 0, labourShare: share);
            new Simulation(world).Tick();

            return world.CountryById(1).Households.Savings;
        }

        Assert.True(PaidAt(80) > PaidAt(30), "доля труда ни на что не повлияла");
    }

    [Fact]
    public void NegativeSavingsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Households(new Money(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Households().Earn(new Money(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Households().SpendUpTo(new Money(-1)));
    }

    [Fact]
    public void RealWorldStartsWithMoneyOnBothSides()
    {
        var world = WorldDataTests.Shared.Value;

        Assert.True(world.Countries.Sum(c => c.State.Treasury.Balance.Exact) > 0, "у казны нет местных денег");
        Assert.True(world.Countries.Sum(c => c.Households.Savings.Exact) > 0, "у населения нет денег");
    }
}
