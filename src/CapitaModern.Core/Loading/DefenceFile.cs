namespace CapitaModern.Core.Loading;

/// <summary>Военные расходы стран из data/politics/defence.json: доля ВВП в сотых долях
/// процента, как и ставки налогов.</summary>
public record DefenceFile(int DefaultShare, Dictionary<string, int> Share);
