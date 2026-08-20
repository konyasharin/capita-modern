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

    public static T Read<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options) ??
               throw new NullReferenceException($"Ожидался {typeof(T).FullName}, получен null");
    }
}
