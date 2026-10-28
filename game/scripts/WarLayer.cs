using Rgb = System.Numerics.Vector3;
using FantasyWorld.Client;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>
/// War on the diorama, drawn like a strategy map: campaign arrows that draw themselves in and then flow toward their
/// target, banners in each realm's colours that wave and march from region to region as days pass, front lines with
/// teeth facing the attacker, occupied land hatched in the conflict colour, and crossed swords where battles were
/// fought this past year. Geometry comes from <see cref="WarLayout"/>; this node only builds and animates it.
/// </summary>
public partial class WarLayer : Node3D
{
    public const float BannerHeight = 0.07f;

    /// <summary>Banners and battle marks grow with the camera's distance so they read at any zoom (1 at region zoom).</summary>
    public static float ScaleFor(float distance) => Math.Clamp(distance / 1.4f, 0.55f, 2.2f);

    public override void _Process(double delta)
    {
        if (GetViewport()?.GetCamera3D() is not { } camera) return;
        foreach (var node in Banners.GetChildren().OfType<Node3D>().Concat(Battles.GetChildren().OfType<Node3D>()))
        {
            if (node.HasMeta("growing")) continue;
            var s = ScaleFor(camera.GlobalPosition.DistanceTo(node.GlobalPosition));
            node.Scale = Vector3.One * (node.HasMeta("pulse") ? s * (1 + 0.18f * Mathf.Sin((float)Time.GetTicksMsec() / 260f)) : s);
        }
    }
    public const double MarchSeconds = 2.5;

    internal const string ArrowShader = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, depth_draw_never, depth_test_disabled;
        uniform float progress = 1.0;
        void fragment() {
            if (UV.y > progress) discard;
            float edge = abs(UV.x);
            float chevron = fract(UV.y * 12.0 - edge * 0.5 - TIME * 0.9);
            vec3 colour = COLOR.rgb * mix(1.0, 1.2, smoothstep(0.55, 0.75, chevron) * (1.0 - edge));
            // A darker rim keeps the arrow crisp against light ground and water.
            colour = mix(colour, COLOR.rgb * 0.55, smoothstep(0.82, 0.95, edge));
            ALBEDO = colour;
            ALPHA = COLOR.a;
        }
        """;

    private const string HatchShader = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, depth_draw_never;
        varying vec3 world;
        void vertex() { world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
        void fragment() {
            float stripe = step(0.6, fract((world.x + world.z) * 55.0));
            ALBEDO = COLOR.rgb;
            ALPHA = stripe * 0.6;
        }
        """;

    private const string FlagShader = """
        shader_type spatial;
        render_mode cull_disabled;
        uniform vec4 colour : source_color = vec4(0.23, 0.44, 0.71, 1.0);
        uniform float phase = 0.0;
        void vertex() {
            // The flag's free edge (UV.x = 1) ripples most; the edge at the pole stays put.
            float wave = sin(UV.x * 6.0 - TIME * 4.5 + phase) * 0.18 * UV.x;
            VERTEX.z += wave * 0.02;
            VERTEX.y += cos(UV.x * 5.0 - TIME * 4.0 + phase) * 0.06 * UV.x * 0.02;
        }
        void fragment() {
            float border = step(0.88, max(abs(UV.y - 0.5) * 2.0, UV.x));
            ALBEDO = mix(colour.rgb, vec3(0.97, 0.96, 0.93), border * 0.85);
            ROUGHNESS = 0.9;
        }
        """;

    private readonly Dictionary<EntityRef, Node3D> _banners = new();
    private readonly List<MapArmy> _armies = new();
    private readonly List<MapBattle> _battles = new();
    private Geography? _geography;

    public Node3D Arrows { get; private set; } = null!;
    public Node3D Banners { get; private set; } = null!;
    public Node3D Battles { get; private set; } = null!;
    public MeshInstance3D Fronts { get; private set; } = null!;
    public MeshInstance3D Hatch { get; private set; } = null!;
    public MeshInstance3D Outline { get; private set; } = null!;
    public IReadOnlyList<MapArmy> Armies => _armies;
    public IReadOnlyList<MapBattle> RecentBattles => _battles;

    public override void _Ready()
    {
        Name = "War";
        Hatch = new MeshInstance3D { Name = "Hatch" };
        Outline = new MeshInstance3D { Name = "Outline" };
        Fronts = new MeshInstance3D { Name = "Fronts" };
        Arrows = new Node3D { Name = "Arrows" };
        Banners = new Node3D { Name = "Banners" };
        Battles = new Node3D { Name = "Battles" };
        foreach (var child in new Node[] { Hatch, Outline, Fronts, Arrows, Battles, Banners }) AddChild(child);
    }

    /// <summary>Lays out a map's war; hosts already on the board march to their new place instead of jumping.</summary>
    public void Show(MapScreen map, Geography geography)
    {
        _geography = geography;
        _armies.Clear();
        _armies.AddRange(map.Armies ?? Array.Empty<MapArmy>());
        _battles.Clear();
        _battles.AddRange(map.Battles ?? Array.Empty<MapBattle>());

        var hatch = WarLayout.Occupation(map, geography);
        Hatch.Mesh = hatch is null ? null : Surface(hatch, new ShaderMaterial { Shader = new Shader { Code = HatchShader } });
        var attackers = map.Conflicts.SelectMany(c => c.Attackers).ToHashSet();
        var byAttackers = map.Regions.Count(r => r.Occupier is { } o && attackers.Contains(o)) >= map.Regions.Count(r => r.Occupier is not null) / 2.0;
        Outline.Mesh = Diorama.DashedRibbons(WarLayout.OccupationOutline(map, geography), byAttackers ? WarLayout.Attacker : WarLayout.Defender,
            dash: 1000, gap: 0, width: 0.0018f, halo: 0);

        var fronts = WarLayout.Fronts(map, geography);
        var frontMesh = new ArrayMesh();
        foreach (var front in fronts)
        {
            var line = Diorama.DashedRibbons(new[] { front.Line }, front.Colour, dash: 1000, gap: 0, width: 0.0022f, halo: 0.0042f);
            for (var s = 0; s < line.GetSurfaceCount(); s++) frontMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, line.SurfaceGetArrays(s), null, null, 0);
            for (var s = 0; s < line.GetSurfaceCount(); s++) frontMesh.SurfaceSetMaterial(frontMesh.GetSurfaceCount() - line.GetSurfaceCount() + s, line.SurfaceGetMaterial(s));
            if (front.Teeth.Triangles > 0) AddSurface(frontMesh, front.Teeth, Flat(front.Colour));
        }
        Fronts.Mesh = frontMesh;

        Ui.Clear(Arrows);
        foreach (var arrow in WarLayout.Arrows(map, geography))
        {
            var material = new ShaderMaterial { Shader = new Shader { Code = ArrowShader } };
            var node = new MeshInstance3D { Name = $"Arrow{arrow.Army.Id}", Mesh = Surface(arrow.Mesh, material) };
            Arrows.AddChild(node);
            // Arrows draw themselves in from the host toward its target.
            material.SetShaderParameter("progress", 0f);
            var draw = node.CreateTween();
            draw.TweenMethod(Callable.From<float>(p => material.SetShaderParameter("progress", p)), 0f, 1f, 1.2).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }

        var seen = new HashSet<EntityRef>();
        foreach (var army in _armies)
        {
            seen.Add(army.Ref);
            var at = Diorama.ToGodot(Client.Projection.ToBlock(geography.Terrain, army.Position, 0));
            if (_banners.TryGetValue(army.Ref, out var banner) && IsInstanceValid(banner))
            {
                if (banner.Position.DistanceTo(at) > 0.001f)
                    banner.CreateTween().TweenProperty(banner, "position", at, MarchSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            }
            else
            {
                banner = Banner(army);
                banner.Position = at;
                banner.Scale = Vector3.One * 0.01f;
                banner.SetMeta("growing", true);
                Banners.AddChild(banner);
                _banners[army.Ref] = banner;
                var grow = banner.CreateTween();
                grow.TweenProperty(banner, "scale", Vector3.One, 0.6).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
                var planted = banner;
                grow.TweenCallback(Callable.From(() => planted.RemoveMeta("growing")));
            }
        }
        foreach (var gone in _banners.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            if (IsInstanceValid(_banners[gone])) _banners[gone].QueueFree();
            _banners.Remove(gone);
        }

        Ui.Clear(Battles);
        foreach (var battle in _battles) Battles.AddChild(Swords(battle, geography, recent: battle.Year == map.Year));
    }

    /// <summary>Where a host's banner stands now (it may be marching), in block space.</summary>
    public Vector3? BannerAt(EntityRef army) => _banners.TryGetValue(army, out var b) && IsInstanceValid(b) ? b.Position : null;

    /// <summary>The top of a host's banner as drawn (banners grow with distance), for placing its tag above it.</summary>
    public Vector3? BannerTop(EntityRef army) =>
        _banners.TryGetValue(army, out var b) && IsInstanceValid(b) ? b.Position + new Vector3(0, (BannerHeight + 0.008f) * b.Scale.Y, 0) : null;

    /// <summary>A pole with a waving flag in the realm's colours and a finial.</summary>
    private static Node3D Banner(MapArmy army)
    {
        var node = new Node3D { Name = $"Banner{army.Ref.Id}" };
        var colour = WarLayout.Colour(army.Colour);
        node.AddChild(new MeshInstance3D
        {
            Name = "Pole", Position = new Vector3(0, BannerHeight / 2, 0),
            Mesh = new CylinderMesh { TopRadius = 0.0012f, BottomRadius = 0.0016f, Height = BannerHeight, RadialSegments = 6 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Ui.C(BlockMesh.Hex(0x5b4636)), Roughness = 0.8f },
        });
        node.AddChild(new MeshInstance3D
        {
            Name = "Finial", Position = new Vector3(0, BannerHeight + 0.003f, 0),
            Mesh = new SphereMesh { Radius = 0.003f, Height = 0.006f, RadialSegments = 8, Rings = 4 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Ui.C(BlockMesh.Hex(0xc9a227)), Metallic = 0.6f, Roughness = 0.35f },
        });
        var flag = new ShaderMaterial { Shader = new Shader { Code = FlagShader } };
        flag.SetShaderParameter("colour", Ui.C(colour));
        flag.SetShaderParameter("phase", (float)(army.Ref.Id % 7));
        node.AddChild(new MeshInstance3D
        {
            Name = "Flag", Position = new Vector3(0.016f, BannerHeight - 0.012f, 0), Rotation = new Vector3(Mathf.Pi / 2, 0, 0),
            Mesh = new PlaneMesh { Size = new Vector2(0.03f, 0.02f), SubdivideWidth = 10, SubdivideDepth = 2 },
            MaterialOverride = flag,
        });
        // A besieging host plants a second, lower pennant beside its banner.
        if (army.Besieging)
            node.AddChild(new MeshInstance3D
            {
                Name = "Siege", Position = new Vector3(-0.01f, 0.012f, 0.006f),
                Mesh = new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.006f, Height = 0.024f, RadialSegments = 4 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = Ui.C(Palette.Conflict), Roughness = 0.8f },
            });
        return node;
    }

    /// <summary>Crossed swords over a battlefield; this year's battles pulse.</summary>
    /// <summary>Crossed swords where a fight is (the owner, 2026-10-28): a battle or duel of this past year, or one under way
    /// (red-rimmed, pulsing); a duel's badge is smaller.</summary>
    private static Node3D Swords(MapBattle battle, Geography geography, bool recent)
    {
        var node = new Node3D
        {
            Name = $"Battle{battle.EventId}",
            Position = Diorama.ToGodot(Client.Projection.ToBlock(geography.Terrain, battle.Position, 0.018f)),
        };
        node.AddChild(Badge.Sprite(Client.Badges.Swords(battle.UnderWay), battle.Kind == MapBattle.Duel ? 0.03f : 0.04f, "Badge"));
        if (recent || battle.UnderWay) node.SetMeta("pulse", true);
        return node;
    }

    private static StandardMaterial3D Flat(Rgb colour) =>
        new() { AlbedoColor = Ui.C(colour), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled };

    internal static ArrayMesh Surface(MeshData data, Material material)
    {
        var mesh = new ArrayMesh();
        AddSurface(mesh, data, material);
        return mesh;
    }

    private static void AddSurface(ArrayMesh mesh, MeshData data, Material material)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Positions.Select(Diorama.ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals.Select(Diorama.ToGodot).ToArray();
        arrays[(int)Mesh.ArrayType.Color] = data.Colors.Select((c, i) => Ui.C(c, data.Alphas?[i] ?? 1).SrgbToLinear()).ToArray();
        if (data.Uvs is { } uvs) arrays[(int)Mesh.ArrayType.TexUV] = uvs.Select(u => new Vector2(u.X, u.Y)).ToArray();
        arrays[(int)Mesh.ArrayType.Index] = data.Indices;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
    }
}
