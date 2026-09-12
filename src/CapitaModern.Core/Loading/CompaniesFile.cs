namespace CapitaModern.Core.Loading;

/// <summary>Крупная компания страны из data/economy/companies.json.</summary>
/// <param name="Sector">Имя отрасли: Mining, Power, Heavy, Civil, Military, Services.</param>
public record NamedCompanyDto(string Name, string Sector);

/// <summary>Кого в стране знают по имени. Кого нет в списке — тому достаются
/// безымянные фирмы.</summary>
public record CompaniesFile(Dictionary<string, NamedCompanyDto[]> Companies);
