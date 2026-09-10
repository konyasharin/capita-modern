namespace CapitaModern.Core.Loading;

/// <summary>Морские бассейны из data/map/basins.json: что с чем соединяется и чей берег
/// куда выходит.</summary>
public record BasinsFile
(
    StraitDto[] Straits,
    Dictionary<string, int[]> ByIso
);

/// <param name="Joins">Номера бассейнов, которые соединяет пролив. Их бывает больше двух:
/// у берега рядом с проливом попадаются мелкие заливы.</param>
public record StraitDto(string Name, string Owner, int[] Joins);
