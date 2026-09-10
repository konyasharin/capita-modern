using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Loading;

/// <summary>Стартовая эффективность из data/economy/efficiency.json, в сотых.</summary>
public record EfficiencyFile
(
    Dictionary<string, EfficiencyDto> ByIso,
    Dictionary<Sector, int> Sensitivity
);

public record EfficiencyDto(int Skill, int Tech, int Condition);
