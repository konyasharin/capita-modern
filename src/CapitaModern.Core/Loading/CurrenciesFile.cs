namespace CapitaModern.Core.Loading;

/// <summary>Валюта страны из data/economy/currencies.json.</summary>
/// <param name="Rate">Сколько местных единиц за доллар на старте партии.</param>
/// <param name="Code">Трёхбуквенный код: RUB, USD, EUR.</param>
/// <param name="Symbol">Знак для подписей. У кого его нет, там стоит код.</param>
public record CurrencyDto(double Rate, string Code, string Symbol, string Name);

/// <summary>Валюты всех стран. Кого нет в списке — доллар по курсу один к одному.</summary>
public record CurrenciesFile(Dictionary<string, CurrencyDto> Currencies);
