using CapitaModern.Core;
using Godot;

public partial class Game : Node
{
    private readonly Sim _sim = new();

    public override void _Ready()
    {
        for (var i = 0; i < 5; i++)
        {
            _sim.Tick();
        }

        GD.Print($"day = {_sim.Day}");
    }
}
