using Godot;

/// <summary>
/// Утилита разработки: снять кадр и выйти.
///   godot --path . -- --shot=C:/tmp/map.png --shot-frame=40
/// Без аргумента --shot нода немедленно удаляется и ничего не делает.
/// </summary>
public partial class DevScreenshot : Node
{
    private string? _path;
    private int _framesLeft = 30;
    private Vector2? _focus;
    private float _zoom = 1f;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shot=", StringComparison.Ordinal))
            {
                _path = arg["--shot=".Length..];
            }
            else if (arg.StartsWith("--shot-frame=", StringComparison.Ordinal)
                && int.TryParse(arg["--shot-frame=".Length..], out var frames))
            {
                _framesLeft = frames;
            }
            else if (arg.StartsWith("--shot-focus=", StringComparison.Ordinal))
            {
                var parts = arg["--shot-focus=".Length..].Split(',');
                if (parts.Length == 3
                    && float.TryParse(parts[0], out var x)
                    && float.TryParse(parts[1], out var y)
                    && float.TryParse(parts[2], out var zoom))
                {
                    _focus = new Vector2(x, y);
                    _zoom = zoom;
                }
            }
        }

        if (_path is null)
        {
            QueueFree();
            return;
        }

        // Замер имеет смысл только без вертикальной синхронизации.
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
    }

    public override void _Process(double delta)
    {
        // Фокус ставится здесь, а не в _Ready: камера получает границы карты
        // только после _Ready родителя, то есть уже после нашего.
        if (_focus is { } point)
        {
            GetParent().GetNode<MapCamera>("MapCamera").FocusOn(point, _zoom);
            _focus = null;
        }

        if (--_framesLeft > 0)
        {
            return;
        }

        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(_path);

        GD.Print($"fps {Engine.GetFramesPerSecond()}");
        GD.Print(error == Error.Ok ? $"снимок: {_path}" : $"снимок не удался: {error}");
        GetTree().Quit();
    }
}
