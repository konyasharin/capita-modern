using System.Text.Json;
using System.Text.Json.Serialization;

using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Loading;

/// <summary>В файлах количества в единицах, внутри — в сотых. Здесь единственное
/// место, где происходит умножение.</summary>
public sealed class GoodAmountJsonConverter : JsonConverter<GoodAmount>
{
    public override GoodAmount Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        GoodAmount.FromUnits(reader.GetInt64());

    public override void Write(Utf8JsonWriter writer, GoodAmount value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Raw / GoodAmount.Scale);
}
