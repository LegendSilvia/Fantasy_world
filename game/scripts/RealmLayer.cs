using System;
using System.Collections.Generic;
using System.Linq;
using FantasyWorld.Client;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>
/// Each independent realm's standard at its capital, always shown (whatever layers are on): a tall pole with a swallow-tailed
/// flag in the realm's two heraldic colours, taller than any host's banner and the larger the more land the realm holds,
/// growing with the camera's distance like the war marks so it reads at any zoom.
/// </summary>
public partial class RealmLayer : Node3D
{
    public const float StandardHeight = WarLayer.BannerHeight * 1.7f;

    private readonly Dictionary<EntityRef, Node3D> _standards = new();

    public IReadOnlyList<MapRealm> Realms { get; private set; } = Array.Empty<MapRealm>();

    private const string FlagShader = """
        shader_type spatial;
        render_mode cull_disabled;
        uniform vec4 colour : source_color = vec4(0.23, 0.44, 0.71, 1.0);
        uniform vec4 accent : source_color = vec4(0.9, 0.85, 0.7, 1.0);
        uniform float phase = 0.0;
        void vertex() {
            float wave = sin(UV.x * 6.0 - TIME * 2.4 + phase) * 0.004 * UV.x;
            VERTEX.y += wave;
        }
        void fragment() {
            // A swallow tail: the fly end is cut in a notch.
            if (UV.x > 0.78 && abs(UV.y - 0.5) < (UV.x - 0.78) * 1.2) discard;
            // A band of the second colour across the middle.
            ALBEDO = abs(UV.y - 0.5) < 0.12 ? accent.rgb : colour.rgb;
            ROUGHNESS = 0.85;
        }
        """;

    public override void _Process(double delta)
    {
        if (GetViewport()?.GetCamera3D() is not { } camera) return;
        foreach (var node in _standards.Values.Where(IsInstanceValid))
            node.Scale = Vector3.One * WarLayer.ScaleFor(camera.GlobalPosition.DistanceTo(node.GlobalPosition)) * (float)node.GetMeta("size", 1f);
    }

    public void Show(MapScreen map, Geography geography)
    {
        Realms = map.Realms ?? Array.Empty<MapRealm>();
        foreach (var node in _standards.Values.Where(IsInstanceValid)) node.QueueFree();
        _standards.Clear();
        var largest = Math.Max(1, Realms.Select(r => r.Regions).DefaultIfEmpty(1).Max());
        foreach (var realm in Realms)
        {
            var standard = Standard(realm);
            standard.Position = Diorama.ToGodot(Client.Projection.ToBlock(geography.Terrain, realm.Position, 0));
            // The great realms' standards stand a little taller than the small ones'.
            standard.SetMeta("size", 0.85f + 0.35f * realm.Regions / (float)largest);
            AddChild(standard);
            _standards[realm.Ref] = standard;
        }
    }

    /// <summary>The top of a realm's standard as drawn, for placing its name above it.</summary>
    public Vector3? TopOf(EntityRef realm) =>
        _standards.TryGetValue(realm, out var s) && IsInstanceValid(s) ? s.Position + new Vector3(0, (StandardHeight + 0.01f) * s.Scale.Y, 0) : null;

    private static Node3D Standard(MapRealm realm)
    {
        var node = new Node3D { Name = $"Standard{realm.Ref.Id}" };
        node.AddChild(new MeshInstance3D
        {
            Name = "Pole", Position = new Vector3(0, StandardHeight / 2, 0),
            Mesh = new CylinderMesh { TopRadius = 0.0016f, BottomRadius = 0.0022f, Height = StandardHeight, RadialSegments = 6 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Ui.C(BlockMesh.Hex(0x3d2f24)), Roughness = 0.8f },
        });
        node.AddChild(new MeshInstance3D
        {
            Name = "Crown", Position = new Vector3(0, StandardHeight + 0.004f, 0),
            Mesh = new PrismMesh { Size = new Vector3(0.008f, 0.008f, 0.008f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Ui.C(BlockMesh.Hex(0xd4af37)), Metallic = 0.7f, Roughness = 0.3f },
        });
        var flag = new ShaderMaterial { Shader = new Shader { Code = FlagShader } };
        flag.SetShaderParameter("colour", Ui.C(WarLayout.Colour(realm.Colour)));
        flag.SetShaderParameter("accent", Ui.C(Palette.Heraldic(realm.Accent)));
        flag.SetShaderParameter("phase", (float)(realm.Ref.Id % 5));
        node.AddChild(new MeshInstance3D
        {
            Name = "Flag", Position = new Vector3(0.026f, StandardHeight - 0.018f, 0), Rotation = new Vector3(Mathf.Pi / 2, 0, 0),
            Mesh = new PlaneMesh { Size = new Vector2(0.05f, 0.032f), SubdivideWidth = 12, SubdivideDepth = 2 },
            MaterialOverride = flag,
        });
        return node;
    }
}
