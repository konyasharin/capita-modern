namespace CapitaModern.Core.Economy;

/// <summary>Валюта страны: чем подписывать её деньги.</summary>
/// <remarks>Курс живёт отдельно, в <see cref="World.Country.ExchangeRate"/>: он меняется
/// каждый тик, а код со знаком — нет.</remarks>
/// <param name="Symbol">Знак для подписей. У кого своего нет, там стоит код.</param>
public readonly record struct Currency(string Code, string Symbol, string Name)
{
    /// <summary>Мировая мера: в ней считаются цены мирового рынка и резервы.</summary>
    public static readonly Currency Dollar = new("USD", "$", "United States dollar");
}
