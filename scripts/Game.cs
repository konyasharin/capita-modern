using Godot;

public partial class Game : Node2D
{
    private WorldMapView _map = null!;
    private MapCamera _camera = null!;
    private Label _info = null!;

    public override void _Ready()
    {
        _map = GetNode<WorldMapView>("WorldMapView");
        _camera = GetNode<MapCamera>("MapCamera");
        _info = GetNode<Label>("Ui/Info");

        _camera.SetBounds(_map.MapSize);
    }

    public override void _Process(double delta)
    {
        var country = _map.CountryAt(_map.GetLocalMousePosition());

        _map.SetHover(country?.Id ?? 0);
        _info.Text = country is null ? string.Empty : $"{country.Name} · {country.Continent}";
    }
}
