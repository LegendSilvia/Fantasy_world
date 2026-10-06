using System.Numerics;
using FantasyWorld.Client;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Views;

namespace FantasyWorld.Sim.Tests.Client;

public class NavigationTests
{
    [Fact]
    public void TheCamera_OrbitsWithinLimits_AndEasesToItsGoal()
    {
        var rig = new OrbitRig();
        rig.Drag(0, 10_000);
        Assert.Equal(OrbitRig.MaxPitch, rig.GoalPitch);
        rig.Drag(0, -20_000);
        Assert.Equal(OrbitRig.MinPitch, rig.GoalPitch);
        rig.Scroll(100);
        Assert.Equal(OrbitRig.MinDistance, rig.GoalDistance);
        rig.Preset(Zoom.Region);
        rig.Focus(new Vector3(5, 0.1f, -0.3f));
        Assert.Equal(new Vector3(1, 0.1f, -0.3f), rig.GoalTarget);
        for (var i = 0; i < 120; i++) rig.Update(1 / 60f);
        Assert.Equal(OrbitRig.DistanceFor(Zoom.Region), rig.Distance, 2);
        Assert.Equal(Zoom.Region, rig.NearestZoom);
        Assert.True(rig.Eye.Y > rig.Target.Y);
        Assert.Equal(rig.Distance, Vector3.Distance(rig.Eye, rig.Target), 3);
    }

    [Fact]
    public void LinksOpenTheirPages_AndBackReturns()
    {
        var router = new ScreenRouter();
        var changes = new List<Screen>();
        router.Changed += changes.Add;
        router.Explain(42);
        router.Open(new EntityRef("char", 7));
        router.Open(new EntityRef("house", 3));
        Assert.Equal(new Screen(ScreenKind.Character, 7), router.Current);
        Assert.True(router.Back());
        Assert.Equal(new Screen(ScreenKind.Why, 42), router.Current);
        router.Home();
        Assert.False(router.CanGoBack);
        Assert.Equal(4, changes.Count);
        Assert.Equal(new Screen(ScreenKind.Map, Focus: new EntityRef("settlement", 1)), ScreenRouter.For(new EntityRef("settlement", 1)));
        Assert.Equal(new Screen(ScreenKind.Chronicle, Focus: new EntityRef("war", 2)), ScreenRouter.For(new EntityRef("war", 2)));
        Assert.Equal(new Screen(ScreenKind.Realm, 3), ScreenRouter.For(new EntityRef("realm", 3)));
    }

    [Fact]
    public void ThePalette_FollowsTheDesign()
    {
        Assert.Equal(new Vector3(0x1a / 255f, 0x1a / 255f, 0x18 / 255f), Palette.Ink);
        Assert.Equal(Palette.Conflict, Palette.For(Category.War));
        Assert.Equal(Palette.Realm, Palette.Heraldic("azure"));
        Assert.Equal(Palette.Muted, Palette.Heraldic("plaid"));
    }
}
