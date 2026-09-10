using System.Text.Json;
using System.Text.Json.Serialization;
using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Loading;

/// <summary>Разбор json с общими настройками. Сам файл не читает — содержимое даёт
/// вызывающий.</summary>
public static class JsonReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,

        // Первый разбирает enum ("OilRig") и ключи словарей ({"Oil": 10}),
        // остальные переводят числа из целых единиц в доли.
        Converters =
        {
            new JsonStringEnumConverter(),
            new FixedJsonConverter<Goods>(),
            new FixedJsonConverter<Cash>(),
        },

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
