using Rgb = System.Numerics.Vector3;
using FantasyWorld.Client;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>
/// The floating world block (sub-project 4 §3): terrain, sea, strata walls, markers, realm borders and digest pins,
/// seen through the orbit camera. Geography is built once; markers and borders are rebuilt when the map changes.
/// Drag to orbit, scroll to descend; clicking near a marker raises <see cref="Picked"/>.
/// </summary>
public partial class Diorama : Node3D
{
    public const float PickRadius = 16f;

    private Geography? _geography;
    private bool _dragging;
    private Vector2 _pressedAt;
    private readonly List<Marker> _markers = new();
    private readonly List<Pin> _pins = new();

    public OrbitRig Rig { get; } = new();
    public Camera3D Camera { get; private set; } = null!;
    public MeshInstance3D Block { get; private set; } = null!;
    public Node3D MarkerLayer { get; private set; } = null!;
    public Node3D PinLayer { get; private set; } = null!;
    public MeshInstance3D BorderLines { get; private set; } = null!;
    public IReadOnlyList<Marker> Markers => _markers;
    public IReadOnlyList<Pin> Pins => _pins;

    /// <summary>A marker or pin was clicked.</summary>
    public event Action<EntityRef>? Picked;

    public override void _Ready()
    {
        Name = "Diorama";
        Camera = new Camera3D { Name = "Camera", Fov = 32, Near = 0.01f, Far = 50 };
        AddChild(Camera);
        AddChild(new DirectionalLight3D { Name = "Sun", RotationDegrees = new Vector3(-55, 35, 0), LightEnergy = 0.6f, ShadowEnabled = true });
        // A soft fill from the viewer's side keeps the strata walls readable.
        AddChild(new DirectionalLight3D { Name = "Fill", RotationDegrees = new Vector3(-15, 35, 0), LightEnergy = 0.45f });
        AddChild(new WorldEnvironment
        {
            Name = "Environment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Ui.C(Palette.Ground),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.55f,
            },
        });
        Block = new MeshInstance3D { Name = "Block" };
        AddChild(Block);
        BorderLines = new MeshInstance3D { Name = "Borders" };
        AddChild(BorderLines);
        MarkerLayer = new Node3D { Name = "Markers" };
        AddChild(MarkerLayer);
        PinLayer = new Node3D { Name = "Pins" };
        AddChild(PinLayer);
        UpdateCamera();
    }

    /// <summary>Builds the terrain block, its walls and the sea (once per world).</summary>
    public void Build(Geography geography, int resolution = 128)
    {
        _geography = geography;
        var mesh = new ArrayMesh();
        AddSurface(mesh, BlockMesh.Top(geography.Terrain, resolution), VertexColours());
        AddSurface(mesh, BlockMesh.Sides(geography.Terrain, resolution), VertexColours());
        Block.Mesh = mesh;
        var water = new ArrayMesh();
        AddSurface(water, BlockMesh.WaterPlane(), new StandardMaterial3D
        {
            AlbedoColor = Ui.C(BlockMesh.Water, 0.72f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.3f,
        });
        var sea = GetNodeOrNull<MeshInstance3D>("Water") ?? new MeshInstance3D { Name = "Water" };
        sea.Mesh = water;
        if (sea.GetParent() is null) AddChild(sea);
    }

    /// <summary>Places settlements, structures and borders for a map (today's or a past year's).</summary>
    public void Show(MapScreen map)
    {
        if (_geography is null) return;
        Ui.Clear(MarkerLayer);
        _markers.Clear();
        _markers.AddRange(FantasyWorld.Client.Markers.For(map, _geography));
        foreach (var marker in _markers) MarkerLayer.AddChild(MarkerNode(marker));
        BorderLines.Mesh = DashedLines(FantasyWorld.Client.Markers.Borders(map, _geography), Palette.Ink);
    }

    /// <summary>The digest's pins as stems over their places (the screen draws their numbered labels).</summary>
    public void ShowPins(IEnumerable<Pin> pins)
    {
        Ui.Clear(PinLayer);
        _pins.Clear();
        _pins.AddRange(pins);
        foreach (var pin in _pins)
        {
            var node = new Node3D { Name = $"Pin{pin.Number}", Position = ToGodot(pin.Position) };
            node.AddChild(new MeshInstance3D
            {
                Name = "Stem", Position = new Vector3(0, -Client.Markers.PinLift / 2, 0),
                Mesh = new CylinderMesh { TopRadius = 0.0015f, BottomRadius = 0.0015f, Height = Client.Markers.PinLift },
                MaterialOverride = Flat(Palette.Ink),
            });
            node.AddChild(new MeshInstance3D { Name = "Head", Mesh = new SphereMesh { Radius = 0.006f, Height = 0.012f }, MaterialOverride = Flat(Palette.Ink) });
            PinLayer.AddChild(node);
        }
    }

    /// <summary>Eases the camera to a place on the map.</summary>
    public void Focus(MapPoint point, Zoom? zoom = null)
    {
        if (_geography is null) return;
        var at = Client.Projection.ToBlock(_geography.Terrain, point, 0);
        Rig.Focus(at, zoom);
    }

    /// <summary>The marker or pin drawn nearest a screen point, within <see cref="PickRadius"/> pixels.</summary>
    public EntityRef? Pick(Vector2 screen)
    {
        var candidates = _pins.Select(p => (p.Place, p.Position)).Concat(_markers.Select(m => (m.Ref, m.Position)));
        EntityRef? best = null;
        var bestDistance = PickRadius;
        foreach (var (entity, position) in candidates)
        {
            var world = ToGodot(position);
            if (Camera.IsPositionBehind(world)) continue;
            var distance = Camera.UnprojectPosition(world).DistanceTo(screen);
            if (distance < bestDistance) (best, bestDistance) = (entity, distance);
        }
        return best;
    }

    /// <summary>Where a block-space point appears on screen.</summary>
    public Vector2 ScreenOf(Rgb position) => Camera.UnprojectPosition(ToGodot(position));

    public override void _Process(double delta)
    {
        Rig.Update((float)delta);
        UpdateCamera();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed) (_dragging, _pressedAt) = (true, button.Position);
                else
                {
                    _dragging = false;
                    if (button.Position.DistanceTo(_pressedAt) < 4 && Pick(button.Position) is { } picked) Picked?.Invoke(picked);
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                Rig.Scroll(1);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                Rig.Scroll(-1);
                break;
            case InputEventMouseMotion motion when _dragging:
                Rig.Drag(motion.Relative.X, motion.Relative.Y);
                break;
        }
    }

    private void UpdateCamera()
    {
        if (Camera is null) return;
        Camera.Position = ToGodot(Rig.Eye);
        Camera.LookAt(ToGodot(Rig.Target), Vector3.Up);
    }

    private static Node3D MarkerNode(Marker marker)
    {
        var node = new Node3D { Name = $"{marker.Ref.Kind}{marker.Ref.Id}", Position = ToGodot(marker.Position) };
        var size = marker.Size;
        switch (marker.Shape)
        {
            case MarkerShape.Settlement:
                node.AddChild(Solid(new BoxMesh { Size = new Vector3(size, size * 0.8f, size) }, marker.Colour, size * 0.4f));
                break;
            case MarkerShape.Labyrinth:
                node.AddChild(Diamond(size * 1.4f, Palette.Card, size));
                node.AddChild(Diamond(size * 0.8f, marker.Colour, size * 1.05f));
                break;
            default:
                node.AddChild(Diamond(size, marker.Colour, size));
                break;
        }
        return node;
    }

    private static MeshInstance3D Diamond(float size, Rgb colour, float lift)
    {
        var diamond = Solid(new BoxMesh { Size = new Vector3(size, size, size) }, colour, lift);
        diamond.RotationDegrees = new Vector3(45, 0, 45);
        return diamond;
    }

    private static MeshInstance3D Solid(Mesh mesh, Rgb colour, float lift) =>
        new() { Mesh = mesh, Position = new Vector3(0, lift, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = Ui.C(colour), Roughness = 0.8f } };

    private static StandardMaterial3D Flat(Rgb colour) =>
        new() { AlbedoColor = Ui.C(colour), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };

    private static StandardMaterial3D VertexColours() => new() { VertexColorUseAsAlbedo = true, Roughness = 0.95f };

    /// <summary>Every other segment, so borders read as the design's dashed lines.</summary>
    private static ArrayMesh DashedLines(IReadOnlyList<Segment> segments, Rgb colour)
    {
        var mesh = new ArrayMesh();
        var points = new List<Vector3>();
        for (var i = 0; i < segments.Count; i += 2)
        {
            points.Add(ToGodot(segments[i].From));
            points.Add(ToGodot(segments[i].To));
        }
        if (points.Count == 0) return mesh;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = points.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        mesh.SurfaceSetMaterial(0, Flat(colour));
        return mesh;
    }

    private static void AddSurface(ArrayMesh mesh, MeshData data, Material material)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Positions.Select(ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals.Select(ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.Color] = data.Colors.Select(c => Ui.C(c).SrgbToLinear()).ToArray();
        // Godot treats clockwise triangles as front faces; the client's indices are counter-clockwise.
        var indices = new int[data.Indices.Length];
        for (var i = 0; i < indices.Length; i += 3) (indices[i], indices[i + 1], indices[i + 2]) = (data.Indices[i], data.Indices[i + 2], data.Indices[i + 1]);
        arrays[(int)Mesh.ArrayType.Index] = indices;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
    }

    public static Vector3 ToGodot(Rgb v) => new(v.X, v.Y, v.Z);
}
