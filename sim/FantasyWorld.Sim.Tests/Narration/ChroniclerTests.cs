using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Sim.Tests.Narration;

public class ChroniclerTests
{
    [Fact]
    public void EachWorld_HasOneChronicler_WhoseVoiceAndStyleTheBibleCarries()
    {
        var world = WorldGenerator.Generate(new WorldGenOptions(11, WorldSize.Tiny), DateTimeOffset.UnixEpoch);
        var chronicler = Chronicler.Of(world);
        Assert.Equal(chronicler, Chronicler.Of(world));
        Assert.EndsWith($" of the {world.Cultures.Values.First().Name}", chronicler.Name);
        var bible = WorldBible.Build(world);
        Assert.Contains(chronicler.Persona, bible);
        Assert.Contains("Never invent a person, place, title, number or date.", bible);
        Assert.Contains("British spelling", bible);

        var others = new ulong[] { 12, 13, 14, 15, 16 }
            .Select(seed => Chronicler.Of(WorldGenerator.Generate(new WorldGenOptions(seed, WorldSize.Tiny), DateTimeOffset.UnixEpoch))).ToList();
        Assert.True(others.Select(c => c.Temperament).Append(chronicler.Temperament).Distinct().Count() > 1, "worlds differ in their chroniclers");
    }
}
