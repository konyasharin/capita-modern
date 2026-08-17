using CapitaModern.Core.Buildings;
using CapitaModern.Core.Loading;

string json = File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "economy", "buildings.json"));

var catalog = BuildingCatalog.FromJson(json);
Console.WriteLine(catalog[BuildingType.OilMiner].OptimalWorkers);
