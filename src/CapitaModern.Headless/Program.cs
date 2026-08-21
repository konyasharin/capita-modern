using CapitaModern.Core.Buildings;
using CapitaModern.Core.Loading;

string json = File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "map", "countries.json"));

var file = WorldDataLoader.LoadCountriesFile(json);
Console.WriteLine(file.Countries[161].Name);
