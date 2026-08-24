using System.Text.Json.Serialization;

namespace CapitaModern.Core.Loading;

/// <summary>
/// Страна как она лежит в data/map/countries.json. Это форма файла, а не доменная
/// модель: сюда попадают и поля карты вроде цвета, которым в игре делать нечего.
/// </summary>
/// <param name="Id">Тот же байт, что лежит в world.bin для каждой ячейки.</param>
/// <param name="Iso">Трёхбуквенный код (RUS, USA) — удобный ключ для поиска.</param>
/// <param name="Color">Индекс цвета в палитре карты. В домен не переносится.</param>
public record CountryDto(
    byte Id,
    string Name,
    string Iso,
    int Color,
    int Population
);

/// <param name="Width">Размер карты, под которую собран файл: сверяется с другими
/// файлами, чтобы поймать «перегенерировал карту, забыл справочник».</param>
public record CountriesFile(
    int Width,
    int Height,
    [property: JsonPropertyName("countries")] CountryDto[] Countries
);
