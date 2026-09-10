using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.World;
using Xunit;

namespace CapitaModern.Core.Tests;

/// <summary>Парная торговля: кто у кого покупает и почему не у самого дешёвого.</summary>
public class TradeTests
{
    private const GoodType Coal = GoodType.Coal;
    private const BuildingType Mine = BuildingType.CoalMine;
    private const BuildingType Mill = BuildingType.SteelMill;

    private static Producer Trader(int id, long coal = 0, long money = 0) => new(
        id,
        new Stock(new Dictionary<GoodType, GoodAmount> { [Coal] = Build.Whole(coal) }),
        new Treasury(Build.Cash(money)),
        new Prices());

    private static MarketOrder Buys(byte country, long coal, long upTo) =>
        new(country, Trader(country), default, Build.Whole(coal), default, Money.FromWhole(upTo));

    private static MarketOrder Sells(byte country, long coal, long price) =>
        new(country, Trader(country, coal: coal), Build.Whole(coal), default, Money.FromWhole(price), default);

    /// <summary>Дорога ничего не стоит: остаётся чистая цена продавца.</summary>
    private static Money AtSellerPrice(MarketOrder seller, MarketOrder buyer) => seller.Ask;

    private static List<Deal> Run(
        MarketOrder[] orders, Func<MarketOrder, MarketOrder, Money>? delivered = null)
    {
        var deals = new List<Deal>();
        new Exchange().Settle(orders, delivered ?? AtSellerPrice, deals.Add);

        return deals;
    }

    [Fact]
    public void SurplusFlowsToShortage()
    {
        var deals = Run([Sells(1, coal: 10, price: 100), Buys(2, coal: 10, upTo: 100)]);

        var deal = Assert.Single(deals);
        Assert.Equal(2, deal.Buyer);
        Assert.Equal(1, deal.Seller);
        Assert.Equal(Build.Whole(10), deal.Amount);
        // Десять единиц по сотне — тысяча, и ни копейки сверх цены продавца.
        Assert.Equal(Money.FromWhole(1000), deal.Paid);
    }

    /// <summary>Главное, чего не мог общий котёл: дальний дешёвый проигрывает ближнему.</summary>
    [Fact]
    public void NearSellerBeatsTheCheapDistantOne()
    {
        MarketOrder[] orders = [Sells(1, coal: 10, price: 50), Sells(2, coal: 10, price: 90), Buys(3, coal: 10, upTo: 200)];

        // У первого уголь вдвое дешевле, но дорога от него дороже втрое.
        var deals = Run(orders, (seller, _) => new Money(seller.Ask.Raw * (seller.Country == 1 ? 3 : 1)));

        Assert.Equal(2, Assert.Single(deals).Seller);
    }

    [Fact]
    public void BuyerRefusesWhatCostsMoreThanItsPrice()
    {
        var deals = Run([Sells(1, coal: 10, price: 100), Buys(2, coal: 10, upTo: 99)]);

        Assert.Empty(deals);
    }

    /// <summary>Нулевая цена доставки — знак, что пути нет вовсе.</summary>
    [Fact]
    public void UnreachableSellerIsSkipped()
    {
        MarketOrder[] orders = [Sells(1, coal: 10, price: 100), Sells(2, coal: 10, price: 150), Buys(3, coal: 10, upTo: 200)];

        var deals = Run(orders, (seller, _) => seller.Country == 1 ? default : seller.Ask);

        Assert.Equal(2, Assert.Single(deals).Seller);
    }

    /// <summary>Товара на одного: достанется тому, кто больше даёт.</summary>
    [Fact]
    public void HighestBidderBuysFirst()
    {
        MarketOrder[] orders = [Sells(1, coal: 10, price: 100), Buys(2, coal: 10, upTo: 120), Buys(3, coal: 10, upTo: 300)];

        var deals = Run(orders);

        Assert.Equal(3, Assert.Single(deals).Buyer);
    }

    /// <summary>Не хватило у одного — покупатель добирает у следующего, пока по карману.</summary>
    [Fact]
    public void BuyerMovesOnToTheNextSeller()
    {
        MarketOrder[] orders = [Sells(1, coal: 4, price: 100), Sells(2, coal: 6, price: 150), Buys(3, coal: 10, upTo: 200)];

        var deals = Run(orders);

        Assert.Equal(2, deals.Count);
        Assert.Equal(1, deals[0].Seller);
        Assert.Equal(Build.Whole(10), deals[0].Amount + deals[1].Amount);
    }

    [Fact]
    public void NobodyTradesWithoutOrders()
    {
        Assert.Empty(Run([]));
        Assert.Empty(Run([Sells(1, coal: 10, price: 100)]));
        Assert.Empty(Run([Buys(1, coal: 10, upTo: 100)]));
    }

    /// <summary>Тик целиком: у кого уголь лишний, тот продаёт, у кого нет — покупает,
    /// и закупается ровно до нормы запаса.</summary>
    /// <remarks>Тиков с запасом: первые уходят на то, чтобы цены разошлись. Пока разрыв
    /// между продавцом и покупателем не покрыл дорогу, возить невыгодно и никто не возит.</remarks>
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
        for (var tick = 0; tick < 20; tick++) simulation.Tick();

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
        Assert.Equal(Money.FromWhole(10_000_000), cutOff.State.Treasury.Reserves.Value);
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

        var before = world.Countries.Sum(country => country.State.Treasury.Reserves.Value.Raw);

        var simulation = new Simulation(world);
        for (var tick = 0; tick < 50; tick++) simulation.Tick();

        Assert.Equal(before, world.Countries.Sum(country => country.State.Treasury.Reserves.Value.Raw));
    }
}
