using CapitaModern.Core;

var ticks = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 100;

var sim = new Sim();

for (var i = 0; i < ticks; i++)
{
    sim.Tick();
}

Console.WriteLine($"day = {sim.Day}");
