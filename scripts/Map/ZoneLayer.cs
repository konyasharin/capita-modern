using Godot;

/// <summary>
/// Рамки промзон одним MultiMesh: нод на зону не создаётся. Появляются на
/// приближении и гаснут при отдалении — издали они только зашумляют карту.
/// </summary>
public partial class ZoneLayer : MultiMeshInstance2D
{
    private const float FadeStart = 5f;
    private const float FadeEnd = 14f;

    private ShaderMaterial _material = null!;
    private float _fade = -1f;

    public IReadOnlyList<IndustrialZone> Zones { get; private set; } = [];

    public void Build(WorldMap map, MapPalette palette, int seed)
    {
        var zones = ZoneGenerator.Generate(map, seed);
        Zones = zones;

        var multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            Mesh = new QuadMesh { Size = Vector2.One },
            InstanceCount = zones.Count,
        };

        for (var i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];

            multi.SetInstanceTransform2D(i, new Transform2D(
                0f,
                new Vector2(zone.Size, zone.Size),
                0f,
                new Vector2(zone.CenterX, zone.CenterY)
            ));
        }

        Multimesh = multi;

        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://scenes/map/zone.gdshader") };
        Material = _material;

        _material.SetShaderParameter("line_color", palette.Coast);
        _material.SetShaderParameter("fill_color", new Color(1f, 1f, 1f, 0.14f));

        SetFade(0f);

        var inWater = zones.Count(z => map.OwnerAt((int)z.CenterX, (int)z.CenterY) == WorldMap.Ocean);
        GD.Print($"промзон: {zones.Count}, центр в воде: {inWater}");
    }

    public override void _Process(double delta)
    {
        // Масштаб канваса и есть текущий зум камеры — отдельная связь не нужна.
        var zoom = GetViewportTransform().Scale.X;
        var t = Mathf.Clamp((zoom - FadeStart) / (FadeEnd - FadeStart), 0f, 1f);

        SetFade(t * t * (3f - 2f * t));
    }

    private void SetFade(float fade)
    {
        if (Mathf.IsEqualApprox(fade, _fade))
        {
            return;
        }

        _fade = fade;
        Visible = fade > 0.002f;
        _material.SetShaderParameter("fade", fade);
    }
}
