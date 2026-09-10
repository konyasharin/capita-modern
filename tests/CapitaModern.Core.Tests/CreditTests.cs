using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Politics;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Заём — торг, а не выдача. Деньги при этом не появляются и не пропадают.</summary>
public class CreditTests
{
    private const GoodType Coal = GoodType.Coal;
    private const BuildingType Mine = BuildingType.CoalMine;
    private const BuildingType Mill = BuildingType.SteelMill;

    private static Money Whole(long n) => Money.FromWhole(n);

    private static Treasury Rich(long money) => new(Build.Cash(money));

    /// <summary>Хранитель у всех ничейный: тестам про кредит место хранения не важно.</summary>
    private const byte Nowhere = WorldMarket.WorldIssuer;

    private static CreditOrder Wants(byte id, Treasury treasury, long money, int premium = 0, int maxRate = 5000) =>
        new(id, treasury, Nowhere, Whole(money), default, 0, premium, maxRate);

    private static CreditOrder Offers(byte id, Treasury treasury, long money, int keyRate = 0) =>
        new(id, treasury, Nowhere, default, Whole(money), keyRate, 0, 5000);

    [Fact]
    public void LenderLosesExactlyWhatBorrowerGains()
    {
        var lender = Rich(1000);
        var borrower = Rich(0);
        CreditOrder[] orders = [Offers(1, lender, 1000), Wants(2, borrower, 400)];

        var given = new CreditMarket().Settle(orders);

        Assert.Equal(Whole(400), given);
        Assert.Equal(Whole(600), lender.Reserves.Value);
        Assert.Equal(Whole(400), borrower.Reserves.Value);
        Assert.Equal(Whole(400), borrower.Debt.Owed());
    }

    [Fact]
    public void BorrowingIsCappedByWhatLendersHave()
    {
        var lender = Rich(100);
        var borrower = Rich(0);

        new CreditMarket().Settle([Offers(1, lender, 100), Wants(2, borrower, 500)]);

        Assert.Equal(Whole(100), borrower.Debt.Owed());
        Assert.Equal(default, lender.Reserves.Value);
    }

    [Fact]
    public void SeveralLendersFillOneBorrower()
    {
        var borrower = Rich(0);

        new CreditMarket().Settle(
            [Offers(1, Rich(300), 300), Offers(2, Rich(300), 300), Wants(3, borrower, 500)]);

        Assert.Equal(Whole(500), borrower.Reserves.Value);
        Assert.Equal(2, borrower.Debt.Loans.Count);
    }

    /// <summary>Тот самый несостоявшийся аукцион.</summary>
    [Fact]
    public void AuctionFailsWhenTheRateIsTooHigh()
    {
        var lender = Rich(1000);
        var borrower = Rich(0);

        // Надбавка 4000 плюс безрисковая сотня — заёмщик согласен максимум на 2000.
        new CreditMarket().Settle([Offers(1, lender, 1000), Wants(2, borrower, 500, premium: 4000, maxRate: 2000)]);

        Assert.Equal(default, borrower.Debt.Owed());
        Assert.Equal(Whole(1000), lender.Reserves.Value);
    }

    [Fact]
    public void NobodyLendsWithoutLenders()
    {
        var borrower = Rich(0);

        Assert.Equal(default, new CreditMarket().Settle([Wants(1, borrower, 500)]));
    }

    [Fact]
    public void CountryDoesNotLendToItself()
    {
        var alone = Rich(1000);

        Assert.Equal(default, new CreditMarket().Settle([new CreditOrder(1, alone, Nowhere, Whole(100), Whole(500), 0, 0, 5000)]));
    }

    /// <summary>При нехватке денег первым получает самый надёжный.</summary>
    [Fact]
    public void SafestBorrowerIsServedFirst()
    {
        var safe = Rich(0);
        var risky = Rich(0);

        new CreditMarket().Settle(
            [Offers(1, Rich(300), 300), Wants(2, risky, 300, premium: 2000), Wants(3, safe, 300, premium: 100)]);

        Assert.Equal(Whole(300), safe.Debt.Owed());
        Assert.Equal(default, risky.Debt.Owed());
    }

    [Fact]
    public void CheaperLenderIsUsedFirst()
    {
        var cheap = Rich(200);
        var dear = Rich(200);
        var borrower = Rich(0);

        new CreditMarket().Settle(
            [Offers(1, dear, 200, keyRate: 1000), Offers(2, cheap, 200, keyRate: 0), Wants(3, borrower, 200)]);

        Assert.Equal(default, cheap.Reserves.Value);
        Assert.Equal(Whole(200), dear.Reserves.Value);
    }

    [Fact]
    public void HeavyDebtRaisesThePremium()
    {
        Assert.Equal(0, CreditMarket.PremiumFor(0, false));
        // Нагрузка 145% — около 6% годовых, как занимала Россия в 2020.
        Assert.Equal(580, CreditMarket.PremiumFor(145, false));
        // 256% — около 10%, как Турция.
        Assert.Equal(1024, CreditMarket.PremiumFor(256, false));
    }

    [Fact]
    public void RecentDefaulterPaysMuchMore()
    {
        Assert.True(CreditMarket.PremiumFor(100, true) > CreditMarket.PremiumFor(100, false) + 2000);
    }

    [Fact]
    public void PremiumNeverPassesTheCeiling()
    {
        Assert.Equal(CreditMarket.Ceiling, CreditMarket.PremiumFor(int.MaxValue, true));
    }

    [Fact]
    public void InterestIsAnnualRateSplitOverTheYear()
    {
        var loan = new Loan(1, LoanSource.Foreign, 1, Whole(36_500), RateKind.Fixed, 1000);

        // 36 500 под 10% годовых — это 10 в сутки.
        Assert.Equal(Whole(10), loan.InterestPerTick(0));
    }

    [Fact]
    public void FloatingRateFollowsTheLenderKeyRate()
    {
        var floater = new Loan(1, LoanSource.Foreign, 1, Whole(100), RateKind.Floating, 200);

        Assert.Equal(200, floater.RateAt(0));
        Assert.Equal(625, floater.RateAt(425));
    }

    [Fact]
    public void FixedRateIgnoresTheKeyRate()
    {
        var fixedLoan = new Loan(1, LoanSource.Foreign, 1, Whole(100), RateKind.Fixed, 200);

        Assert.Equal(200, fixedLoan.RateAt(425));
    }

    [Fact]
    public void UnpaidInterestGrowsTheDebt()
    {
        var loan = new Loan(1, LoanSource.Foreign, 1, Whole(100), RateKind.Fixed, 1000);
        loan.Capitalise(Whole(5));

        Assert.Equal(Whole(105), loan.Principal);
    }

    [Fact]
    public void BurdenIsDebtOverYearlyExports()
    {
        var debt = new Debt();
        debt.Take(LoanSource.Foreign, 1, Whole(200), RateKind.Fixed, 0);

        Assert.Equal(200, debt.BurdenToExports(Whole(100)));
        Assert.Equal(int.MaxValue, debt.BurdenToExports(default));
    }

    [Fact]
    public void PriciestLoanIsTheOneToRepayFirst()
    {
        var debt = new Debt();
        debt.Take(LoanSource.Foreign, 1, Whole(100), RateKind.Fixed, 300);
        var dear = debt.Take(LoanSource.Foreign, 2, Whole(100), RateKind.Fixed, 1400);

        Assert.Same(dear, debt.Priciest(0));
    }

    [Fact]
    public void RepaidLoansLeaveTheList()
    {
        var debt = new Debt();
        var loan = debt.Take(LoanSource.Foreign, 1, Whole(100), RateKind.Fixed, 0);
        loan.Repay(Whole(100));
        debt.Forget();

        Assert.Empty(debt.Loans);
    }

    [Fact]
    public void DefaultWipesForeignLoansOnly()
    {
        var debt = new Debt();
        debt.Take(LoanSource.Foreign, 1, Whole(500), RateKind.Fixed, 0);
        debt.Take(LoanSource.Domestic, null, Whole(300), RateKind.Fixed, 0);

        var lost = debt.Default(LoanSource.Foreign);

        Assert.Equal(default, debt.Owed(LoanSource.Foreign));
        Assert.Equal(Whole(300), debt.Owed(LoanSource.Domestic));
        Assert.Equal(Whole(500), lost.Single().Lost);
        Assert.Equal((byte)1, lost.Single().Lender);
    }

    [Fact]
    public void DefaultNamesEveryCreditor()
    {
        var debt = new Debt();
        debt.Take(LoanSource.Foreign, 1, Whole(100), RateKind.Fixed, 0);
        debt.Take(LoanSource.Foreign, 2, Whole(200), RateKind.Fixed, 0);

        var lost = debt.Default(LoanSource.Foreign);

        Assert.Equal(2, lost.Count);
        Assert.Equal(Whole(300), lost.Aggregate(default(Money), (sum, x) => sum + x.Lost));
    }

    [Fact]
    public void HostileLenderRefusesAtAnyRate()
    {
        Assert.Null(CreditMarket.PoliticsOn(CreditMarket.Hostile));
        Assert.Null(CreditMarket.PoliticsOn(-70));
        Assert.NotNull(CreditMarket.PoliticsOn(0));
    }

    [Fact]
    public void FriendsLendCheaperThanStrangers()
    {
        Assert.True(CreditMarket.PoliticsOn(Relations.Friendly) < CreditMarket.PoliticsOn(Relations.Neutral));
    }

    /// <summary>Тот самый пример: Япония не даст враждебной стране ни под какой процент,
    /// а Китаю она нейтральна, и он туда зайдёт.</summary>
    [Fact]
    public void UnfriendlyLenderIsSkippedAndAFriendlyOneStepsIn()
    {
        var japan = Rich(1000);
        var china = Rich(1000);
        var borrower = Rich(0);

        var relations = new Relations(new Dictionary<byte, Bloc>
        {
            [1] = Bloc.West,
            [2] = Bloc.China,
            [3] = Bloc.Russia,
        });

        new CreditMarket().Settle(
            [Offers(1, japan, 1000), Offers(2, china, 1000), Wants(3, borrower, 500)],
            relations.Between);

        Assert.Equal(Whole(1000), japan.Reserves.Value);
        Assert.Equal(Whole(500), china.Reserves.Value);
        Assert.Equal(Whole(500), borrower.Debt.Owed());
        Assert.Equal((byte)2, borrower.Debt.Loans.Single().Lender);
    }

    [Fact]
    public void FriendlyLendingIsNeverBelowTheRiskFreeRate()
    {
        var friend = Rich(1000);
        var borrower = Rich(0);

        var relations = new Relations(new Dictionary<byte, Bloc> { [1] = Bloc.West, [2] = Bloc.West });

        new CreditMarket().Settle([Offers(1, friend, 1000), Wants(2, borrower, 500)], relations.Between);

        Assert.Equal(CreditMarket.BaseRate, borrower.Debt.Loans.Single().Rate);
    }

    /// <summary>Тик целиком: у кого валюта кончилась, тот занимает и продолжает ввозить.</summary>
    [Fact]
    public void PoorCountryBorrowsAndKeepsImporting()
    {
        static (GoodAmount Coal, Money Owed) Run(bool lenderIsRich)
        {
            var world = Build.World(
                [
                    Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 100 }),
                    Build.Region(2, 2, new Dictionary<BuildingType, int> { [Mill] = 1 }),
                ],
                [Build.Country(1, money: lenderIsRich ? 10_000_000 : 0), Build.Country(2, money: 100)],
                Build.Catalog(
                    Build.Info(Mine, outputs: new() { [Coal] = Build.Whole(10) }),
                    Build.Info(Mill, inputs: new() { [Coal] = Build.Whole(10) },
                                     outputs: new() { [GoodType.Metals] = Build.Whole(1) })),
                new Dictionary<GoodType, Money> { [Coal] = Money.FromWhole(1) });

            var simulation = new Simulation(world);
            for (var tick = 0; tick < 30; tick++) simulation.Tick();

            var poor = world.CountryById(2);

            return (poor.State.Stock.Of(Coal), poor.State.Treasury.Debt.Owed());
        }

        var withLender = Run(lenderIsRich: true);
        var without = Run(lenderIsRich: false);

        Assert.True(withLender.Owed > default(Money), "бедная страна так и не заняла");
        Assert.True(withLender.Coal > without.Coal, "заём не помог ввозить больше");
    }

    /// <summary>Кто не может платить проценты по неподъёмному долгу — отказывается, и
    /// это стоит ему рынка на годы и обвала курса.</summary>
    [Fact]
    public void HopelessBorrowerDefaultsAndPaysForIt()
    {
        var world = Build.World(
            [
                Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 100 }),
                Build.Region(2, 2, new Dictionary<BuildingType, int> { [Mill] = 1 }),
            ],
            [Build.Country(1, money: 10_000_000), Build.Country(2)],
            Build.Catalog(
                Build.Info(Mine, outputs: new() { [Coal] = Build.Whole(10) }),
                Build.Info(Mill, inputs: new() { [Coal] = Build.Whole(10) },
                                 outputs: new() { [GoodType.Metals] = Build.Whole(1) })),
            new Dictionary<GoodType, Money> { [Coal] = Money.FromWhole(100) });

        var poor = world.CountryById(2);
        var simulation = new Simulation(world);

        // Второй ничего не вывозит: занимать он может, а отдавать нечем.
        for (var tick = 0; tick < 2000; tick++) simulation.Tick();

        Assert.True(poor.DefaultedOnDay > 0, "безнадёжный должник так и не отказался платить");
        Assert.True(poor.ExchangeRate > Money.FromWhole(2), "курс после отказа не обвалился");
    }

    /// <summary>Долг не создаёт денег: сколько в мире было, столько и осталось.</summary>
    [Fact]
    public void LendingKeepsWorldMoneyUnchanged()
    {
        var world = Build.World(
            [
                Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 7 }),
                Build.Region(2, 2, new Dictionary<BuildingType, int> { [Mill] = 3 }),
            ],
            [Build.Country(1, money: 1234), Build.Country(2, money: 567_890)],
            Build.Catalog(
                Build.Info(Mine, outputs: new() { [Coal] = Build.Whole(11) }),
                Build.Info(Mill, inputs: new() { [Coal] = Build.Whole(13) },
                                 outputs: new() { [GoodType.Metals] = Build.Whole(2) })),
            new Dictionary<GoodType, Money> { [Coal] = Money.FromWhole(3) });

        long Money0() => world.Countries.Sum(c => c.State.Treasury.Reserves.Value.Raw);
        var before = Money0();

        var simulation = new Simulation(world);
        for (var tick = 0; tick < 200; tick++) simulation.Tick();

        Assert.Equal(before, Money0());
    }
}
