namespace CapitaModern.Core.Loading;

/// <summary>Стартовые деньги государства из data/economy/reserves.json, в миллионах
/// долларов. Кого нет в списке — считается по населению.</summary>
/// <param name="Composition">В чьих валютах лежат резервы, в процентах. Доли одни на
/// весь мир: тонкости тут не нужны, важно лишь у кого именно они лежат.</param>
/// <param name="GoldShare">Доля золота, в процентах. Лежит дома, поэтому не отнять —
/// ради этого его и держат.</param>
public record ReservesFile
(
    Dictionary<string, long> ByIso,
    long DefaultPerMillionPeople,
    Dictionary<string, int> Composition,
    int GoldShare
);
