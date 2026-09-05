using System.Text.Json;
using System.Text.Json.Serialization;

namespace CapitaModern.Core.Loading;

/// <summary>Разбор json с общими настройками. Сам файл не читает — содержимое даёт
/// вызывающий.</summary>
public static class JsonReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,

        // Первый разбирает enum ("OilRig") и ключи словарей ({"Oil": 10}),
        // второй переводит количества из единиц в сотые.
        Converters = { new JsonStringEnumConverter(), new GoodAmountJsonConverter() },

        // Файлы правятся руками: лишняя запятая и комментарий не должны ронять загрузку.
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static T Read<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options) ??
               throw new NullReferenceException($"Ожидался {typeof(T).FullName}, получен null");
    }
}
