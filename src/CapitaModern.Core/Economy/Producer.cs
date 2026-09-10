namespace CapitaModern.Core.Economy;

/// <summary>
/// Хозяйствующий субъект: у кого есть склад, деньги и свои цены.
/// </summary>
/// <remarks>
/// Сейчас на страну приходится один — государство. Компании появятся такими же
/// продавцами, и цены начнут различаться сами: они принадлежат продавцу, а не стране.
/// Место входит сюда же через <see cref="RegionId"/>, поэтому разные цены в разных
/// областях не потребуют нового измерения — достаточно продавцов с местом.
/// </remarks>
public sealed class Producer
{
    /// <summary>Свой номер, отдельный от номера страны: продавцов станет больше, чем стран.</summary>
    public int Id { get; }

    /// <summary>Где стоит. Пусто у государства — оно охватывает всю страну.</summary>
    public int? RegionId { get; }

    public Stock Stock { get; }
    public Treasury Treasury { get; }

    /// <summary>Свои цены. У каждого продавца отдельные — иначе компании не смогут
    /// торговать друг с другом.</summary>
    public Prices Prices { get; }

    public Producer(int id, Stock stock, Treasury treasury, Prices prices, int? regionId = null)
    {
        Id = id;
        RegionId = regionId;
        Stock = stock;
        Treasury = treasury;
        Prices = prices;
    }
}
