using System.Numerics;

namespace FantasyWorld.Client;

/// <summary>The three zoom presets of the map HUD.</summary>
public enum Zoom { Realm, Region, Town }

/// <summary>
/// The god's-eye camera (sub-project 4 §3): orbits a target on the block (drag), descends and rises (scroll) between
/// limits, jumps to zoom presets or a point of interest, and eases toward its goal each frame. Angles in radians.
/// </summary>
public sealed class OrbitRig
{
    public const float MinPitch = 0.26f;   // ~15°: never skims the surface
    public const float MaxPitch = 1.40f;   // ~80°: almost straight down
    public const float MinDistance = 0.35f;
    public const float MaxDistance = 4.5f;
    public const float DragSpeed = 0.005f; // radians per pixel: a full turn takes a long drag, so the world never whirls
    public const float Ease = 5f;          // per second: settles in about half a second

    public static float DistanceFor(Zoom zoom) => zoom switch { Zoom.Realm => 3.4f, Zoom.Region => 1.4f, _ => 0.6f };

    public float Yaw { get; private set; } = 0.785f;
    public float Pitch { get; private set; } = 0.62f;
    public float Distance { get; private set; } = DistanceFor(Zoom.Realm);
    public Vector3 Target { get; private set; } = Vector3.Zero;

    public float GoalYaw { get; private set; } = 0.785f;
    public float GoalPitch { get; private set; } = 0.62f;
    public float GoalDistance { get; private set; } = DistanceFor(Zoom.Realm);
    public Vector3 GoalTarget { get; private set; } = Vector3.Zero;

    public void Drag(float dxPixels, float dyPixels)
    {
        GoalYaw -= dxPixels * DragSpeed;
        GoalPitch = Math.Clamp(GoalPitch + dyPixels * DragSpeed, MinPitch, MaxPitch);
    }

    /// <summary>Scroll steps: positive descends (closer), negative rises.</summary>
    public void Scroll(float steps) => GoalDistance = Math.Clamp(GoalDistance * MathF.Pow(0.85f, steps), MinDistance, MaxDistance);

    public void Preset(Zoom zoom) => GoalDistance = DistanceFor(zoom);

    public void Focus(Vector3 point, Zoom? zoom = null)
    {
        GoalTarget = new Vector3(Math.Clamp(point.X, -1, 1), point.Y, Math.Clamp(point.Z, -1, 1));
        if (zoom is { } z) Preset(z);
    }

    /// <summary>Sets the goal's turn round the target directly (a film's slow orbit), in radians.</summary>
    public void Turn(float yaw) => GoalYaw = yaw;

    /// <summary>Jumps to the goal at once, with no easing (a film starting mid-way, a cut).</summary>
    public void Snap() => (Yaw, Pitch, Distance, Target) = (GoalYaw, GoalPitch, GoalDistance, GoalTarget);

    public void Reset()
    {
        (GoalYaw, GoalPitch, GoalTarget) = (0.785f, 0.62f, Vector3.Zero);
        Preset(Zoom.Realm);
    }

    /// <summary>Eases toward the goal; call once per frame with the frame time.</summary>
    public void Update(float seconds)
    {
        var t = 1 - MathF.Exp(-Ease * Math.Max(0, seconds));
        Yaw += (GoalYaw - Yaw) * t;
        Pitch += (GoalPitch - Pitch) * t;
        Distance += (GoalDistance - Distance) * t;
        Target += (GoalTarget - Target) * t;
    }

    /// <summary>Where the camera sits, looking at <see cref="Target"/>.</summary>
    public Vector3 Eye => Target + Distance * new Vector3(MathF.Cos(Pitch) * MathF.Sin(Yaw), MathF.Sin(Pitch), MathF.Cos(Pitch) * MathF.Cos(Yaw));

    /// <summary>The zoom preset nearest the current distance (highlights the HUD's Realm/Region/Town buttons).</summary>
    public Zoom NearestZoom => Enum.GetValues<Zoom>().MinBy(z => Math.Abs(MathF.Log(DistanceFor(z) / Distance)));
}
