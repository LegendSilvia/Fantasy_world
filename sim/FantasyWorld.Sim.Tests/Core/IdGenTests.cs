using System.Text.Json;
using FantasyWorld.Sim.Core;

namespace FantasyWorld.Sim.Tests.Core;

public class IdGenTests
{
    [Fact]
    public void Take_CountsPerKindFromOne()
    {
        var ids = new IdGen();
        Assert.Equal(1, ids.Take("region"));
        Assert.Equal(2, ids.Take("region"));
        Assert.Equal(1, ids.Take("settlement"));
        Assert.Equal(3, ids.Peek("region"));
    }

    [Fact]
    public void JsonRoundTrip_ContinuesTheSequence()
    {
        var ids = new IdGen();
        ids.Take("region");
        ids.Take("region");
        var copy = JsonSerializer.Deserialize<IdGen>(JsonSerializer.Serialize(ids))!;
        Assert.Equal(3, copy.Take("region"));
        Assert.Same(StringComparer.Ordinal, copy.Next.Comparer);
    }
}
