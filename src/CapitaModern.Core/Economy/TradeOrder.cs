namespace CapitaModern.Core.Economy;

/// <summary>Что продавец хочет от рынка по одному товару за тик. Одно из двух всегда
/// ноль: докупают до нормы запаса либо продают то, что сверх неё.</summary>
public readonly record struct TradeOrder(Producer Trader, GoodAmount Bid, GoodAmount Offer);
