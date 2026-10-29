using CapitaModern.Core.World;
using Godot;

/// <summary>Дивизии на карте: значок на регион и страну, в пути — между регионами.</summary>
public partial class DivisionLayer : Node2D
{
    private static readonly Vector2 Marker = new(26, 15);
    private const int FontSize = 11;

    private Session _session = null!;
    private float _zoom;

    public void Init(Session session)
    {
        _session = session;
        session.Changed += QueueRedraw;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        var zoom = GetViewport().GetCamera2D()?.Zoom.X ?? 1f;
        if (Mathf.IsEqualApprox(zoom, _zoom)) return;

        _zoom = zoom;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_session is null || _zoom <= 0) return;

        var world = _session.World;
        var width = world.Map.Width;
        var font = ThemeDB.FallbackFont;
        var player = _session.Game.Player;
        var selected = _session.Selected?.Id;

        var stacks = world.Divisions.Where(d => d.Alive).GroupBy(d => (d.Country, d.Region, d.Target));
        foreach (var stack in stacks)
        {
            var first = stack.First();
            var at = Where(world, first);
            var mine = first.Country == player;
            var fighting = stack.Any(d => d.InBattle);
            var organization = (float)stack.Average(d => d.Organization);

            foreach (var shift in new[] { -width, 0, width })
            {
                DrawSetTransform(at + new Vector2(shift, 0), 0, Vector2.One / _zoom);

                // Путь своих дивизий: линия до цели приказа.
                if (mine && first.Goal >= 0)
                {
                    var goal = Center(world.Region(first.Goal));
                    goal.X = at.X + Wrap(goal.X - at.X, width);
                    DrawLine(Vector2.Zero, (goal - at) * _zoom, new Color(1, 1, 1, 0.7f), 2);
                }

                var box = new Rect2(-Marker / 2, Marker);
                var edge = fighting ? new Color(0.95f, 0.25f, 0.2f) : mine && first.Region == selected ? new Color(1, 0.85f, 0.3f) : new Color(0.05f, 0.07f, 0.08f);
                DrawRect(box.Grow(1.5f), edge);
                DrawRect(box, _session.ColorOf(first.Country).Darkened(0.15f));
                DrawRect(new Rect2(box.Position.X, box.End.Y + 1.5f, box.Size.X * organization, 3), new Color(0.4f, 0.9f, 0.4f));
                DrawString(font, new Vector2(box.Position.X, box.Position.Y + 12), stack.Count().ToString(),
                    HorizontalAlignment.Center, Marker.X, FontSize, Colors.White);
            }
        }

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private static Vector2 Center(Region region) => new(region.X, region.Y);

    /// <summary>Где дивизия сейчас: в пути — на доле пути до соседнего региона.</summary>
    private static Vector2 Where(GameWorld world, Division division)
    {
        var from = Center(world.Region(division.Region));
        if (division.Target < 0 || division.TravelDays <= 0) return from;

        var to = Center(world.Region(division.Target));
        to.X = from.X + Wrap(to.X - from.X, world.Map.Width);
        return from.Lerp(to, (float)Math.Clamp(division.Progress / division.TravelDays, 0, 1));
    }

    /// <summary>Кратчайший сдвиг по долготе: карта замкнута.</summary>
    private static float Wrap(float dx, int width) => Mathf.PosMod(dx + width / 2f, width) - width / 2f;
}
