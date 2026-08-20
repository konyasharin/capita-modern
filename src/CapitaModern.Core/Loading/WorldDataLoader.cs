using CapitaModern.Core.World;

namespace CapitaModern.Core.Loading;

public static class WorldDataLoader
{
    public static CountriesFile LoadCountriesFile(string json)
    {
        return JsonReader.Read<CountriesFile>(json);
    }
}
