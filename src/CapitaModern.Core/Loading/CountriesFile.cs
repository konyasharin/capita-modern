using System.Text.Json.Serialization;

namespace CapitaModern.Core.Loading;

/// <summary>Страна как она лежит в countries.json. Это форма файла, а не игровая
/// модель — здесь есть и поля карты вроде цвета.</summary>
/// <param name="Id">Тот же байт, что лежит в world.bin для каждой ячейки.</param>
/// <param name="Iso">Трёхбуквенный код: RUS, USA.</param>
/// <param name="Color">Цвет на карте. В игровую модель не идёт.</param>
/// <param name="Gdp">Настоящий ВВП 2020 года, млн долларов. В игре не считается —
/// нужен только для стартового состояния и сверки.</param>
public record CountryDto(
    byte Id,
    string Name,
    string Iso,
    int Color,
    int Population,
    long Gdp
);

/// <param name="Width">Размер карты, под которую собран файл. Сверяется с остальными,
/// чтобы поймать рассинхрон.</param>
public record CountriesFile(
    int Width,
    int Height,
    [property: JsonPropertyName("countries")] CountryDto[] Countries
);
