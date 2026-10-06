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
    public const float FrameOffset = 0.08f;

    private Geography? _geography;
    private bool _dragging;
    private Vector2 _pressedAt;
    private readonly List<Marker> _markers = new();
    private readonly List<Pin> _pins = new();

    public OrbitRig Rig { get; } = new();
    public Camera3D Camera { get; private set; } = null!;
    public MeshInstance3D Block { get; private set; } = null!;
    public Node3D MarkerLayer { get; private set; } = null!;
    public Node3D SettlementLayer { get; private set; } = null!;
    public Node3D StructureLayer { get; private set; } = null!;
    public MapLayers Layers { get; private set; } = MapLayers.Default;
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
        AddChild(new DirectionalLight3D { Name = "Sun", RotationDegrees = new Vector3(-55, 35, 0), LightEnergy = 0.55f, ShadowEnabled = true });
        // A soft fill from the viewer's side keeps the strata walls readable.
        AddChild(new DirectionalLight3D { Name = "Fill", RotationDegrees = new Vector3(-15, 35, 0), LightEnergy = 0.25f });
        AddChild(new DirectionalLight3D { Name = "Back", RotationDegrees = new Vector3(-20, 215, 0), LightEnergy = 0.2f });
        AddChild(new WorldEnvironment
        {
            Name = "Environment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Ui.C(Palette.Ground),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.4f,
            },
        });
        Block = new MeshInstance3D { Name = "Block" };
        AddChild(Block);
        BorderLines = new MeshInstance3D { Name = "Borders" };
        AddChild(BorderLines);
        MarkerLayer = new Node3D { Name = "Markers" };
        AddChild(MarkerLayer);
        SettlementLayer = new Node3D { Name = "Settlements" };
        StructureLayer = new Node3D { Name = "Structures" };
        MarkerLayer.AddChild(SettlementLayer);
        MarkerLayer.AddChild(StructureLayer);
        PinLayer = new Node3D { Name = "Pins" };
        AddChild(PinLayer);
        UpdateCamera();
    }

    /// <summary>
    /// Builds the terrain block, its walls, the sea, the woods and the block's shadow (once per world). Woods leave
    /// clearings around the given points (today's settlements).
    /// </summary>
    public void Build(Geography geography, IEnumerable<Rgb>? clearings = null, int? resolution = null) =>
        Build(geography, Prepare(geography, clearings, resolution));

    /// <summary>The block's geometry and woods, computed without Godot so it can run on a worker thread.</summary>
    public sealed record Parts(MeshData Top, MeshData Sides, MeshData Water, IReadOnlyList<Client.Tree> Trees);

    public static Parts Prepare(Geography geography, IEnumerable<Rgb>? clearings = null, int? resolution = null)
    {
        var grid = geography.Terrain;
        // About one and a half vertices per tile: smooth on small worlds, detailed on large ones.
        var detail = resolution ?? Math.Clamp(grid.Width * 3 / 2, 192, 384);
        return new Parts(BlockMesh.Top(grid, detail), BlockMesh.Sides(grid, detail), BlockMesh.Water(grid), Scatter.Trees(grid, clearings));
    }

    public void Build(Geography geography, Parts parts)
    {
        _geography = geography;
        var mesh = new ArrayMesh();
        AddSurface(mesh, parts.Top, VertexColours());
        AddSurface(mesh, parts.Sides, VertexColours());
        Block.Mesh = mesh;

        var water = new ArrayMesh();
        AddSurface(water, parts.Water, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.18f,
            MetallicSpecular = 0.7f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        Child<MeshInstance3D>("Water").Mesh = water;

        var trees = parts.Trees;
        Child<MultiMeshInstance3D>("Broadleaves").Multimesh = Instances(
            new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 4 },
            trees.Where(t => t.Kind == TreeKind.Broadleaf).Select(t => (Place(t.Position + new Rgb(0, t.Height * 0.55f, 0), new Vector3(t.Height * 0.95f, t.Height, t.Height * 0.95f), 0), t.Colour)));
        Child<MultiMeshInstance3D>("Conifers").Multimesh = Instances(
            new CylinderMesh { TopRadius = 0, BottomRadius = 0.36f, Height = 1f, RadialSegments = 7, Rings = 1 },
            trees.Where(t => t.Kind == TreeKind.Conifer).Select(t => (Place(t.Position + new Rgb(0, t.Height * 0.5f, 0), new Vector3(t.Height, t.Height, t.Height), 0), t.Colour)));
        foreach (var name in new[] { "Broadleaves", "Conifers" }) Child<MultiMeshInstance3D>(name).MaterialOverride = Foliage();

        // A soft shadow on the ground beneath the floating block.
        Child<MeshInstance3D>("Shadow").Mesh = new PlaneMesh { Size = new Vector2(3.1f, 3.1f) };
        var shadow = Child<MeshInstance3D>("Shadow");
        shadow.Position = new Vector3(0.06f, -BlockMesh.BaseDepth - 0.12f, 0.06f);
        shadow.MaterialOverride = new StandardMaterial3D
        {
            AlbedoTexture = SoftShadow(), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1, 1, 1, 1),
        };
    }

    public int TreeCount => Child<MultiMeshInstance3D>("Broadleaves").Multimesh?.InstanceCount + Child<MultiMeshInstance3D>("Conifers").Multimesh?.InstanceCount ?? 0;

    /// <summary>Places settlements, structures and borders for a map: today's, or the given past year's.</summary>
    public void Show(MapScreen map, int? pastYear = null)
    {
        if (_geography is null) return;
        Ui.Clear(SettlementLayer);
        Ui.Clear(StructureLayer);
        _markers.Clear();
        _markers.AddRange(FantasyWorld.Client.Markers.For(map, _geography));
        var houses = _markers.Where(m => m.Shape == MarkerShape.Settlement).SelectMany(m => Scatter.Houses(m, _geography.Terrain)).ToList();
        SettlementLayer.AddChild(new MultiMeshInstance3D
        {
            Name = "Walls", MaterialOverride = Painted(),
            Multimesh = Instances(new BoxMesh { Size = Vector3.One }, houses.Select(h => (
                Place(h.Position + new Rgb(0, h.Size * (h.Tower ? 0.75f : 0.35f), 0), h.Tower ? new Vector3(h.Size, h.Size * 1.5f, h.Size) : new Vector3(h.Size, h.Size * 0.7f, h.Size * 0.8f), h.Yaw),
                h.Tower ? Stone : Plaster))),
        });
        SettlementLayer.AddChild(new MultiMeshInstance3D
        {
            Name = "Roofs", MaterialOverride = Painted(),
            Multimesh = Instances(new PrismMesh { Size = Vector3.One }, houses.Select(h => (
                Place(h.Position + new Rgb(0, h.Tower ? h.Size * 1.85f : h.Size * 0.95f, 0), h.Tower ? new Vector3(h.Size * 1.15f, h.Size * 0.75f, h.Size * 1.15f) : new Vector3(h.Size * 1.1f, h.Size * 0.5f, h.Size * 0.9f), h.Yaw),
                Roof(h.Roof)))),
        });
        foreach (var marker in _markers.Where(m => m.Shape != MarkerShape.Settlement)) StructureLayer.AddChild(MarkerNode(marker));
        BorderLines.Mesh = DashedRibbons(FantasyWorld.Client.Markers.BorderLines(map, _geography), Palette.Ink);
        ShownYear = pastYear;
    }

    /// <summary>The past year the markers show, or null for today.</summary>
    public int? ShownYear { get; private set; }

    /// <summary>Shows or hides the borders, settlements and structures (news tags are the screen's).</summary>
    public void SetLayers(MapLayers layers)
    {
        Layers = layers;
        BorderLines.Visible = layers.HasFlag(MapLayers.Borders);
        SettlementLayer.Visible = layers.HasFlag(MapLayers.Settlements);
        StructureLayer.Visible = layers.HasFlag(MapLayers.Structures);
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
        var candidates = _pins.Select(p => (p.Place, p.Position)).Concat(_markers
            .Where(m => m.Shape == MarkerShape.Settlement ? SettlementLayer.Visible : StructureLayer.Visible).Select(m => (m.Ref, m.Position)));
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
        var eye = Rig.Eye;
        // Never inside a mountain: keep the eye a little above the ground beneath it.
        if (_geography is not null && Math.Abs(eye.X) <= 1 && Math.Abs(eye.Z) <= 1)
            eye.Y = Math.Max(eye.Y, Math.Max(0, BlockMesh.HeightAt(_geography.Terrain, (eye.X + 1) / 2, (eye.Z + 1) / 2)) + 0.04f);
        Camera.Position = ToGodot(eye);
        Camera.LookAt(ToGodot(Rig.Target), Vector3.Up);
        // The screens' cards sit on the right, so the world is framed slightly left of centre.
        Camera.HOffset = Rig.Distance * FrameOffset;
    }

    private static readonly Rgb Plaster = BlockMesh.Hex(0xf1ebde);
    private static readonly Rgb Stone = BlockMesh.Hex(0xb9b1a2);

    /// <summary>Roofs take the holder's colour, softened toward terracotta so heraldry reads without shouting.</summary>
    private static Rgb Roof(Rgb heraldic)
    {
        var roof = Rgb.Lerp(heraldic, BlockMesh.Hex(0xb5654a), 0.3f);
        // Keep roofs mid-toned: sable (black) and argent (white) houses would read as holes and glare.
        var luma = 0.3f * roof.X + 0.59f * roof.Y + 0.11f * roof.Z;
        return Rgb.Clamp(roof * (Math.Clamp(luma, 0.32f, 0.62f) / Math.Max(0.01f, luma)), Rgb.Zero, Rgb.One);
    }

    /// <summary>Landmarks are pale obelisks, labyrinths a ring of standing walls, ruins a few broken stones.</summary>
    private static Node3D MarkerNode(Marker marker)
    {
        var node = new Node3D { Name = $"{marker.Ref.Kind}{marker.Ref.Id}", Position = ToGodot(marker.Position) };
        var s = marker.Size;
        switch (marker.Shape)
        {
            case MarkerShape.Landmark:
                node.AddChild(Solid(new BoxMesh { Size = new Vector3(s * 0.35f, s * 1.6f, s * 0.35f) }, Stone, s * 0.8f));
                node.AddChild(Solid(new PrismMesh { Size = new Vector3(s * 0.4f, s * 0.35f, s * 0.4f) }, marker.Colour, s * 1.78f));
                break;
            case MarkerShape.Labyrinth:
                for (var k = 0; k < 8; k++)
                {
                    var angle = k * Mathf.Tau / 8;
                    var wall = Solid(new BoxMesh { Size = new Vector3(s * 0.38f, s * 0.35f, s * 0.08f) }, Stone, s * 0.17f);
                    wall.Position += new Vector3(Mathf.Cos(angle) * s * 0.5f, 0, Mathf.Sin(angle) * s * 0.5f);
                    wall.Rotation = new Vector3(0, -angle + Mathf.Pi / 2, 0);
                    node.AddChild(wall);
                }
                node.AddChild(Solid(new CylinderMesh { TopRadius = s * 0.16f, BottomRadius = s * 0.16f, Height = s * 0.06f }, Rgb.Lerp(marker.Colour, Stone, 0.45f), s * 0.03f));
                break;
            default:
                for (var k = 0; k < 3; k++)
                {
                    var stone = Solid(new BoxMesh { Size = new Vector3(s * 0.22f, s * (0.25f + 0.2f * k), s * 0.22f) }, marker.Colour, s * 0.15f);
                    stone.Position += new Vector3((k - 1) * s * 0.3f, 0, (k % 2) * s * 0.2f);
                    stone.Rotation = new Vector3(0.15f * k, 0.6f * k, 0);
                    node.AddChild(stone);
                }
                break;
        }
        return node;
    }

    private static MeshInstance3D Solid(Mesh mesh, Rgb colour, float lift) =>
        new() { Mesh = mesh, Position = new Vector3(0, lift, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = Ui.C(colour), Roughness = 0.8f } };

    private static StandardMaterial3D Flat(Rgb colour) =>
        new() { AlbedoColor = Ui.C(colour), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };

    private static StandardMaterial3D VertexColours() => new() { VertexColorUseAsAlbedo = true, Roughness = 0.95f };

    /// <summary>
    /// Lines drawn at a constant thickness on screen whatever the zoom: each vertex carries the direction across its
    /// line (NORMAL) and which side it is on (UV.x), and the shader pushes it out in proportion to its distance from
    /// the camera. Width is in thousandths of the distance (about 1.6 px at 1440×900 per 0.001).
    /// </summary>
    private const string LineShader = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, depth_draw_never;
        uniform vec4 colour : source_color = vec4(0.1, 0.1, 0.09, 0.9);
        uniform float width = 0.0016;
        void vertex() {
            vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            float distance_to_eye = length(CAMERA_POSITION_WORLD - world);
            VERTEX += NORMAL * UV.x * width * distance_to_eye;
            VERTEX.y += 0.0008 * distance_to_eye;
        }
        void fragment() {
            ALBEDO = colour.rgb;
            ALPHA = colour.a;
        }
        """;

    /// <summary>Realm borders as dashed lines of constant screen width, ink over a pale halo so they read on any ground.</summary>
    private static ArrayMesh DashedRibbons(IReadOnlyList<IReadOnlyList<Rgb>> lines, Rgb colour, float dash = 0.014f, float gap = 0.008f)
    {
        var mesh = new ArrayMesh();
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        foreach (var line in lines)
        {
            var travelled = 0f;
            for (var i = 0; i + 1 < line.Count; i++)
            {
                var (a, b) = (ToGodot(line[i]), ToGodot(line[i + 1]));
                var length = a.DistanceTo(b);
                if (length < 1e-6f) continue;
                var across = new Vector3(-(b.Z - a.Z), 0, b.X - a.X).Normalized();
                for (var t = 0f; t < length;)
                {
                    var phase = (travelled + t) % (dash + gap);
                    var span = phase < dash ? Math.Min(dash - phase, length - t) : Math.Min(dash + gap - phase, length - t);
                    if (phase < dash)
                    {
                        var (p, q) = (a.Lerp(b, t / length), a.Lerp(b, (t + span) / length));
                        vertices.AddRange(new[] { p, q, p, q, q, p });
                        normals.AddRange(Enumerable.Repeat(across, 6));
                        uvs.AddRange(new[] { new Vector2(-1, 0), new Vector2(-1, 0), new Vector2(1, 0), new Vector2(-1, 0), new Vector2(1, 0), new Vector2(1, 0) });
                    }
                    t += Math.Max(span, 1e-5f);
                }
                travelled += length;
            }
        }
        if (vertices.Count == 0) return mesh;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        // The same dashes twice: a wide pale halo, then the ink line on top.
        foreach (var (tint, width, priority) in new[] { (Palette.Card, 0.0034f, 0), (colour, 0.0016f, 1) })
        {
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var material = new ShaderMaterial { Shader = new Shader { Code = LineShader }, RenderPriority = priority };
            material.SetShaderParameter("colour", Ui.C(tint, priority == 0 ? 0.75f : 0.95f));
            material.SetShaderParameter("width", width);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        }
        return mesh;
    }

    /// <summary>Instances of one mesh with per-instance transforms and colours (colours given in sRGB).</summary>
    private static MultiMesh Instances(Mesh mesh, IEnumerable<(Transform3D Transform, Rgb Colour)> items)
    {
        var list = items.ToList();
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = list.Count };
        for (var i = 0; i < list.Count; i++)
        {
            multi.SetInstanceTransform(i, list[i].Transform);
            multi.SetInstanceColor(i, Ui.C(list[i].Colour).SrgbToLinear());
        }
        return multi;
    }

    private static Transform3D Place(Rgb at, Vector3 scale, float yaw) =>
        new(new Basis(Vector3.Up, yaw).Scaled(scale), ToGodot(at));

    private static StandardMaterial3D Foliage() => new() { VertexColorUseAsAlbedo = true, Roughness = 1f };

    private static StandardMaterial3D Painted() => new() { VertexColorUseAsAlbedo = true, Roughness = 0.9f };

    /// <summary>A radial falloff from soft grey to clear, for the shadow under the block.</summary>
    private static ImageTexture SoftShadow(int size = 128)
    {
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                // A rounded square: the block is square, its shadow a soft square.
                var dx = Math.Max(0, Math.Abs((x + 0.5f) / size * 2 - 1) - 0.45f) / 0.55f;
                var dy = Math.Max(0, Math.Abs((y + 0.5f) / size * 2 - 1) - 0.45f) / 0.55f;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                var a = Mathf.Clamp(1 - d, 0, 1);
                image.SetPixel(x, y, new Color(0.1f, 0.1f, 0.09f, 0.22f * a * a));
            }
        return ImageTexture.CreateFromImage(image);
    }

    private T Child<T>(string name) where T : Node, new()
    {
        if (GetNodeOrNull<T>(name) is { } existing) return existing;
        var child = new T { Name = name };
        AddChild(child);
        return child;
    }

    private static void AddSurface(ArrayMesh mesh, MeshData data, Material material)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Positions.Select(ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals.Select(ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.Color] = data.Colors.Select((c, i) => Ui.C(c, data.Alphas?[i] ?? 1).SrgbToLinear()).ToArray();
        // Godot treats clockwise triangles as front faces; the client's indices are counter-clockwise.
        var indices = new int[data.Indices.Length];
        for (var i = 0; i < indices.Length; i += 3) (indices[i], indices[i + 1], indices[i + 2]) = (data.Indices[i], data.Indices[i + 2], data.Indices[i + 1]);
        arrays[(int)Mesh.ArrayType.Index] = indices;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
    }

    public static Vector3 ToGodot(Rgb v) => new(v.X, v.Y, v.Z);
}
