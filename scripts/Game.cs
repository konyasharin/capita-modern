using Godot;

public partial class Game : Node2D
{
    public override void _Ready()
    {
        GetNode<MapCamera>("MapCamera").SetBounds(GetNode<WorldMapView>("WorldMapView").MapSize);
    }
}
