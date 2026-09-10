using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Мировой рынок. Главное, что проверяется, — товар и деньги не исчезают.</summary>
public class TradeTests
{
    private const GoodType Coal = GoodType.Coal;
    private const BuildingType Mine = BuildingType.CoalMine;
    private const BuildingType Mill = BuildingType.SteelMill;

    /// <summary>Цена угля 100, чтобы стоимость партии считалась в уме.</summary>
    private static WorldMarket Market() => Build.Market(new() { [Coal] = Money.FromWhole(100) });

    private static Producer Trader(int id, long coal = 0, long money = 0) => new(
        id,
        new Stock(new Dictionary<GoodType, GoodAmount> { [Coal] = Build.Whole(coal) }),
        new Treasury(Money.FromWhole(money)),
        new Prices());

    private static TradeOrder Buys(Producer trader, long coal) =>
        new(trader, Build.Whole(coal), default);

    private static TradeOrder Sells(Producer trader, long coal) =>
        new(trader, default, Build.Whole(coal));

    private static long Coals(Producer trader) => trader.Stock.Of(Coal).Raw;
    private static long Cash(Producer trader) => trader.Treasury.Balance.Raw;

    [Fact]
    public void SurplusFlowsToShortage()
    {
        var seller = Trader(1, coal: 10);
        var buyer = Trader(2, money: 2000);
        TradeOrder[] orders = [Sells(seller, 10), Buys(buyer, 10)];

        var traded = Market().Settle(Coal, orders);

        Assert.Equal(Build.Whole(10), traded);
        Assert.Equal(default, seller.Stock.Of(Coal));
        Assert.Equal(Build.Whole(10), buyer.Stock.Of(Coal));
        // Десять единиц по сотне: покупатель отдал тысячу, продавец её получил.
        Assert.Equal(Money.FromWhole(1000), seller.Treasury.Balance);
        Assert.Equal(Money.FromWhole(1000), buyer.Treasury.Balance);
    }

    /// <summary>Числа нарочно неровные: на них и вылезают потери от целочисленного
    /// деления.</summary>
    [Fact]
    public void MoneyAndGoodsAreConserved()
    {
        Producer[] traders =
        [
            Trader(1, coal: 7), Trader(2, coal: 13), Trader(3, coal: 3),
            Trader(4, money: 999), Trader(5, money: 12_345), Trader(6, money: 71),
        ];
        TradeOrder[] orders =
        [
            Sells(traders[0], 7), Sells(traders[1], 13), Sells(traders[2], 3),
            Buys(traders[3], 11), Buys(traders[4], 4), Buys(traders[5], 9),
        ];

        var coalBefore = traders.Sum(Coals);
        var cashBefore = traders.Sum(Cash);

        Market().Settle(Coal, orders);

        Assert.Equal(coalBefore, traders.Sum(Coals));
        Assert.Equal(cashBefore, traders.Sum(Cash));
    }

    [Fact]
    public void PoorCountryBuysOnlyWhatItCanPay()
    {
        var seller = Trader(1, coal: 10);
        var buyer = Trader(2, money: 350);
        TradeOrder[] orders = [Sells(seller, 10), Buys(buyer, 10)];

        Market().Settle(Coal, orders);

        // На 350 при цене 100 берётся три с половиной единицы, и казна в ноль.
        Assert.Equal(Build.Whole(7) / 2, buyer.Stock.Of(Coal));
        Assert.Equal(default, buyer.Treasury.Balance);
        Assert.Equal(Build.Whole(13) / 2, seller.Stock.Of(Coal));
    }

    [Fact]
    public void BidsAreRationedProportionally()
    {
        var seller = Trader(1, coal: 20);
        var big = Trader(2, money: 100_000);
        var small = Trader(3, money: 100_000);
        TradeOrder[] orders = [Sells(seller, 20), Buys(big, 30), Buys(small, 10)];

        Market().Settle(Coal, orders);

        // Двадцать на сорок заказанных: три четверти и одна четверть.
        Assert.Equal(Build.Whole(15), big.Stock.Of(Coal));
        Assert.Equal(Build.Whole(5), small.Stock.Of(Coal));
    }

    /// <summary>Кому не хватило денег, того доля уходит остальным, а не пропадает.</summary>
    [Fact]
    public void MoneyLeftoverGoesToTheOthers()
    {
        var seller = Trader(1, coal: 10);
        var poor = Trader(2, money: 200);
        var rich = Trader(3, money: 100_000);
        TradeOrder[] orders = [Sells(seller, 10), Buys(poor, 10), Buys(rich, 10)];

        var traded = Market().Settle(Coal, orders);

        Assert.Equal(Build.Whole(10), traded);
        Assert.Equal(default, seller.Stock.Of(Coal));
        // По заявке богатому причиталась половина, но бедный своё не выбрал.
        Assert.True(rich.Stock.Of(Coal) > Build.Whole(5), "остаток не достался тому, у кого есть деньги");
    }

    [Fact]
    public void WorldPriceRisesWhenBidsExceedOffers()
    {
        var market = Market();
        var seller = Trader(1, coal: 1);
        var buyer = Trader(2, money: 100_000);

        market.Settle(Coal, [Sells(seller, 1), Buys(buyer, 100)]);

        Assert.True(market.Prices.Of(Coal) > Money.FromWhole(100), "цена рынка не выросла при нехватке");
    }

    [Fact]
    public void WorldPriceFallsWhenNobodyBuys()
    {
        var market = Market();

        market.Settle(Coal, [Sells(Trader(1, coal: 10), 10)]);

        Assert.True(market.Prices.Of(Coal) < Money.FromWhole(100), "цена рынка не упала без спроса");
    }

    [Fact]
    public void NobodyTradesWithoutOrders()
    {
        var market = Market();

        Assert.Equal(default, market.Settle(Coal, []));
        Assert.Equal(Money.FromWhole(100), market.Prices.Of(Coal));
    }

    /// <summary>Один продавец без покупателей ничего не теряет.</summary>
    [Fact]
    public void OneSidedMarketMovesNoGoods()
    {
        var seller = Trader(1, coal: 10);

        Assert.Equal(default, Market().Settle(Coal, [Sells(seller, 10)]));
        Assert.Equal(Build.Whole(10), seller.Stock.Of(Coal));
    }

    /// <summary>Тик целиком: у кого уголь лишний, тот продаёт, у кого нет — покупает,
    /// и закупается ровно до нормы запаса.</summary>
    [Fact]
    public void TickFillsTheBuyerUpToTheTargetCover()
    {
        var world = Build.World(
            [
                Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 100 }),
                Build.Region(2, 2, new Dictionary<BuildingType, int> { [Mill] = 1 }),
            ],
            [
                Build.Country(1),
                Build.Country(2, money: 10_000_000),
            ],
            Build.Catalog(
                Build.Info(Mine, outputs: new() { [Coal] = Build.Whole(10) }),
                Build.Info(Mill, inputs: new() { [Coal] = Build.Whole(10) },
                                 outputs: new() { [GoodType.Metals] = Build.Whole(1) })),
            new Dictionary<GoodType, Money> { [Coal] = Money.FromWhole(1) });

        var simulation = new Simulation(world);
        for (var tick = 0; tick < 10; tick++) simulation.Tick();

        var buyer = world.CountryById(2);

        // Завод просит 10 в сутки, норма — сорок суток, минус то, что он съел за тик.
        Assert.Equal(Build.Whole(10 * Prices.TargetCoverDays - 10), buyer.State.Stock.Of(Coal));
        Assert.True(buyer.State.Stock.Of(GoodType.Metals) > default(GoodAmount), "завод так и не заработал");
    }

    /// <summary>Отрезанная страна не покупает и не продаёт, даже когда всё остальное
    /// у неё есть.</summary>
    [Fact]
    public void CutOffCountryTradesNothing()
    {
        var world = Build.World(
            [
                Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 100 }),
                Build.Region(2, 2, new Dictionary<BuildingType, int> { [Mill] = 1 }),
            ],
            [
                Build.Country(1),
                Build.Country(2, money: 10_000_000),
            ],
            Build.Catalog(
                Build.Info(Mine, outputs: new() { [Coal] = Build.Whole(10) }),
                Build.Info(Mill, inputs: new() { [Coal] = Build.Whole(10) },
                                 outputs: new() { [GoodType.Metals] = Build.Whole(1) })),
            new Dictionary<GoodType, Money> { [Coal] = Money.FromWhole(1) });

        world.CountryById(2).TradeAccess.Block(Coal);

        var simulation = new Simulation(world);
        for (var tick = 0; tick < 10; tick++) simulation.Tick();

        var cutOff = world.CountryById(2);

        Assert.Equal(default, cutOff.State.Stock.Of(Coal));
        Assert.Equal(default, cutOff.State.Stock.Of(GoodType.Metals));
        Assert.Equal(Money.FromWhole(10_000_000), cutOff.State.Treasury.Balance);
    }

    [Fact]
    public void TickKeepsMoneyInTheWorld()
    {
        var world = Build.World(
            [
                Build.Region(1, 1, new Dictionary<BuildingType, int> { [Mine] = 7 }),
                Build.Region(2, 2, new Dictionary<BuildingType, int> { [Mill] = 3 }),
            ],
            [
                Build.Country(1, money: 1234),
                Build.Country(2, money: 567_890),
            ],
            Build.Catalog(
                Build.Info(Mine, outputs: new() { [Coal] = Build.Whole(11) }),
                Build.Info(Mill, inputs: new() { [Coal] = Build.Whole(13) },
                                 outputs: new() { [GoodType.Metals] = Build.Whole(2) })),
            new Dictionary<GoodType, Money> { [Coal] = Money.FromWhole(3) });

        var before = world.Countries.Sum(country => country.State.Treasury.Balance.Raw);

        var simulation = new Simulation(world);
        for (var tick = 0; tick < 50; tick++) simulation.Tick();

        Assert.Equal(before, world.Countries.Sum(country => country.State.Treasury.Balance.Raw));
    }
}
