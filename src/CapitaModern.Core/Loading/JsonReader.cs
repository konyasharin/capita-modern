using System.Text.Json;
using System.Text.Json.Serialization;

namespace CapitaModern.Core.Loading;

/// <summary>
/// Разбор JSON с настройками, общими для всех файлов данных.
/// Файл не читает: путь и способ чтения знает вызывающий, ядру они неизвестны.
/// </summary>
public static class JsonReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,

        // Без этого не разберутся ни значения enum ("OilRig"), ни ключи словарей
        // ({"Oil": 10}) — а на них держатся рецепты и месторождения.
        Converters = { new JsonStringEnumConverter() },

        // Файлы данных правятся руками, поэтому висящая запятая и комментарий
        // не должны ронять загрузку.
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static T Read<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options) ??
               throw new NullReferenceException($"Ожидался {typeof(T).FullName}, получен null");
    }
}
