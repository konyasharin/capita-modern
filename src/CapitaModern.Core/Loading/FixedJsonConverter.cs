using System.Text.Json;
using System.Text.Json.Serialization;
using CapitaModern.Core.Economy;

namespace CapitaModern.Core.Loading;

/// <summary>В файлах величины записаны целыми единицами, внутри хранятся долями.
/// Здесь единственное место, где происходит умножение.</summary>
public sealed class FixedJsonConverter<TUnit> : JsonConverter<Fixed<TUnit>>
    where TUnit : IUnitScale
{
    public override Fixed<TUnit> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        new((long)(reader.GetDecimal() * Fixed<TUnit>.Scale));

    public override void Write(Utf8JsonWriter writer, Fixed<TUnit> value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Exact);
}
