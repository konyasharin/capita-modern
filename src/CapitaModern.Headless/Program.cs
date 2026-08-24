using CapitaModern.Core.Buildings;
using CapitaModern.Core.Loading;

string json = File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "map", "regions.json"));

var file = WorldDataLoader.LoadRegionsFile(json);
Console.WriteLine(file.Regions[161].Deposits.Count);
