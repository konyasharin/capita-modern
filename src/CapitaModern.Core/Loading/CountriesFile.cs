using System.Text.Json.Serialization;

namespace CapitaModern.Core.Loading;

public record CountryDto(
    byte Id,
    string Name,
    string Iso,
    int Color,
    int Population
);

public record CountriesFile(
    int Width,
    int Height,
    [property: JsonPropertyName("countries")] CountryDto[] Countries
);
