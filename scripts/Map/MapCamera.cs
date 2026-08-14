using Godot;

/// <summary>
/// Камера карты: перетаскивание, зум колесом к курсору, клавиши. Всё движение
/// сглажено — целевые значения задаются вводом, а камера догоняет их экспоненциально.
/// </summary>
public partial class MapCamera : Camera2D
{
    private const float ZoomStep = 1.18f;
    // Приближаться нужно вплоть до внутренностей промзоны, поэтому предел высокий.
    private const float MaxZoom = 260f;
    private const float KeyPanSpeed = 900f;
    private const float Smoothing = 18f;

    private Vector2 _targetPos;
    private float _targetZoom = 1f;
    private float _minZoom = 0.2f;
    private bool _dragging;
    private Vector2 _mapSize = Vector2.One;

    public void SetBounds(Vector2 mapSize)
    {
        _mapSize = mapSize;
        _targetPos = mapSize * 0.5f;
        Position = _targetPos;

        FitToScreen();
        _targetZoom = _minZoom;
        Zoom = new Vector2(_targetZoom, _targetZoom);
    }

    /// <summary>Мгновенно навести камеру на точку карты с заданным приближением.</summary>
    public void FocusOn(Vector2 mapPoint, float zoom)
    {
        _targetZoom = Mathf.Clamp(zoom, _minZoom, MaxZoom);
        _targetPos = mapPoint;

        Clamp();

        Zoom = new Vector2(_targetZoom, _targetZoom);
        Position = _targetPos;
    }

    private void FitToScreen()
    {
        var screen = GetViewportRect().Size;
        // Весь мир должен помещаться на экране целиком: дальше отдаляться незачем.
        _minZoom = Mathf.Min(screen.X / _mapSize.X, screen.Y / _mapSize.Y);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } up:
                ZoomAt(up.Position, ZoomStep);
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } down:
                ZoomAt(down.Position, 1f / ZoomStep);
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Middle } click:
                _dragging = click.Pressed;
                break;

            case InputEventMouseMotion motion when _dragging:
                // Тянем саму карту, а не камеру: курсор держится за ту же точку мира.
                _targetPos -= motion.Relative / Zoom;
                Position -= motion.Relative / Zoom;
                Clamp();
                break;
        }
    }

    private void ZoomAt(Vector2 screenPoint, float factor)
    {
        var offset = screenPoint - GetViewportRect().Size * 0.5f;
        var anchor = _targetPos + offset / _targetZoom;

        _targetZoom = Mathf.Clamp(_targetZoom * factor, _minZoom, MaxZoom);

        // Мировая точка под курсором обязана остаться под курсором.
        _targetPos = anchor - offset / _targetZoom;

        Clamp();
    }

    public override void _Process(double delta)
    {
        var pan = Vector2.Zero;

        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) pan.X -= 1f;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) pan.X += 1f;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) pan.Y -= 1f;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) pan.Y += 1f;

        if (pan != Vector2.Zero)
        {
            _targetPos += pan.Normalized() * KeyPanSpeed * (float)delta / _targetZoom;
            Clamp();
        }

        var t = 1f - Mathf.Exp(-Smoothing * (float)delta);
        var zoom = Mathf.Lerp(Zoom.X, _targetZoom, t);

        Zoom = new Vector2(zoom, zoom);
        Position = Position.Lerp(_targetPos, t);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMSizeChanged && _mapSize != Vector2.One)
        {
            FitToScreen();
            _targetZoom = Mathf.Max(_targetZoom, _minZoom);
            Clamp();
        }
    }

    private void Clamp()
    {
        var half = GetViewportRect().Size * 0.5f / _targetZoom;
        var minX = Mathf.Min(half.X, _mapSize.X * 0.5f);
        var minY = Mathf.Min(half.Y, _mapSize.Y * 0.5f);

        _targetPos = new Vector2(
            Mathf.Clamp(_targetPos.X, minX, _mapSize.X - minX),
            Mathf.Clamp(_targetPos.Y, minY, _mapSize.Y - minY)
        );
    }
}
