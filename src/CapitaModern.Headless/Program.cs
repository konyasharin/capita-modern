using CapitaModern.Core.Buildings;
using CapitaModern.Core.Loading;

string regionsJson = File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "map", "regions.json"));
string countriesJson = File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "map", "countries.json"));

string buildingsJson = File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "economy", "buildings.json"));

var world = WorldDataLoader.LoadWorld(countriesJson, regionsJson, buildingsJson);


Console.WriteLine(world.Buildings[BuildingType.Refinery].OptimalWorkers);
