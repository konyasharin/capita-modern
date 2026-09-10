namespace CapitaModern.Core.Loading;

/// <summary>Ключевые ставки из data/economy/key-rates.json, в сотых долях процента.
/// Кого нет в списке — берёт <paramref name="DefaultRate"/>.</summary>
public record KeyRatesFile
(
    Dictionary<string, int> ByIso,
    int DefaultRate
);
