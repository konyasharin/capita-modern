using System.Text.Json;
using System.Text.Json.Serialization;

namespace CapitaModern.Core.Loading;

public static class JsonReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static List<T> Read<T>(string json)
    {
        return JsonSerializer.Deserialize<List<T>>(json, Options) ??
               throw new NullReferenceException("Ожидался массив, получен null");
    }
}
