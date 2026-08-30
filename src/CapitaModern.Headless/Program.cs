using CapitaModern.Core.Buildings;
using CapitaModern.Core.Economy;
using CapitaModern.Core.Loading;
using CapitaModern.Core.World;

string regionsJson = File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "map", "regions.json"));
string countriesJson = File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "map", "countries.json"));

string buildingsJson = File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "economy", "buildings.json"));
string startBuildingsJson = File.ReadAllText(Path.Combine(RepoPaths.GetRepoRoot(), "data", "economy", "start-industry.json"));

var world = WorldDataLoader.LoadWorld(countriesJson, regionsJson, buildingsJson, startBuildingsJson);
var russia = world.Countries.First(c => c.Iso == "RUS");

var simulation = new Simulation(world);

foreach (var tick in Enumerable.Range(0, 30))
{
    simulation.Tick();
    if (tick + 1 is 1 or 5 or 10 or 30)
    {
        Console.WriteLine($"Simulation tick #{tick + 1}");
        foreach (var good in Enum.GetValues<GoodType>())
        {
            var amount = russia.StockOf(good);
            Console.WriteLine($"{good}: {amount}");
        }
    }
}
