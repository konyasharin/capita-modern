using CapitaModern.Core.World;

namespace CapitaModern.Core.Loading;

/// <summary>
/// Разбор файлов мира. Принимает содержимое, а не пути: в игре файлы достаёт Godot
/// из <c>res://</c>, в консоли — обычный File, и ядро не должно знать разницы.
/// </summary>
public static class WorldDataLoader
{
    public static CountriesFile LoadCountriesFile(string json) => JsonReader.Read<CountriesFile>(json);
    public static RegionsFile LoadRegionsFile(string json) => JsonReader.Read<RegionsFile>(json);
}
