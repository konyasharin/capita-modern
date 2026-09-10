namespace CapitaModern.Core.Loading;

/// <summary>Стартовые деньги государства из data/economy/reserves.json, в миллионах
/// долларов. Кого нет в списке — считается по населению.</summary>
public record ReservesFile
(
    Dictionary<string, long> ByIso,
    long DefaultPerMillionPeople
);
