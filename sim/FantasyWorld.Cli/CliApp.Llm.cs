using System.Globalization;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Core;
using FantasyWorld.Sim.Engine;
using FantasyWorld.Sim.Llm;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Persist;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;
using FantasyWorld.Sim.WorldGen;

namespace FantasyWorld.Cli;

public static partial class CliApp
{
    /// <summary>
    /// Loads a world with its LLM journal and settings, and the LLM decider, arbiter and inventor behind every
    /// system. Offline (provider none, or no credentials) they replay journaled answers and otherwise use the rules.
    /// </summary>
    private static (Simulator Sim, LlmGateway Gateway) LoadLive(WorldStore store, Func<LlmSettings, ILlmProvider?> providers)
    {
        var (world, log, queue) = store.Load();
        var settings = store.LoadLlmSettings();
        var gateway = new LlmGateway(settings.Enabled ? providers(settings) : null, settings, store.LoadJournal());
        var story = new StoryContext(world, log, gateway);
        var systems = DefaultSystems.Create(new LlmDecider(story), new LlmArbiter(story), new LlmInventor(story));
        return (new Simulator(world, log, queue, systems), gateway);
    }

    private static void PrintLlmSummary(TextWriter output, LlmGateway gateway, IReadOnlyList<LlmCall> fresh)
    {
        if (!gateway.Settings.Enabled && gateway.Replayed == 0) return;
        var spend = fresh.Sum(c => c.CostUsd);
        output.WriteLine($"LLM: {fresh.Count} new answer(s) (${spend.ToString("0.0000", CultureInfo.InvariantCulture)}), "
                         + $"{gateway.Replayed} replayed, {gateway.Fallbacks} by the rules{(gateway.Online ? "" : " (offline)")}.");
        foreach (var rejected in fresh.Where(c => c.Note.StartsWith("rejected: ", StringComparison.Ordinal)))
            output.WriteLine($"LLM answer rejected ({rejected.Tier.ToString().ToLowerInvariant()}): {rejected.Note["rejected: ".Length..]}.");
        if (gateway.Failure is { } failure) output.WriteLine($"LLM provider unavailable, continued offline: {failure}");
    }

    /// <summary>Claude names and describes a new world's races (sub-project 13); run before its history.</summary>
    private static int Invent(string path, TextWriter output, Func<LlmSettings, ILlmProvider?> providers)
    {
        using var store = OpenExisting(path);
        var (sim, gateway) = LoadLive(store, providers);
        var inventor = new LlmInventor(new StoryContext(sim.World, sim.Log, gateway));
        RaceGenerator.Invent(sim.World, inventor);
        // Every nation's festival, as Claude would have it (sub-project 13 N6).
        var rng = Rng.ForStream(sim.World.Seed, "invent/festivals", 0);
        foreach (var realm in Realms.Independent(sim.World).OrderBy(r => r.Id).ToList())
            realm.Festival = FestivalSystem.Invent(sim.World, realm, rng, inventor, sim.World.Date.Year);
        // The world's legend (the owner, 2026-10-29), told in full in its first episode.
        sim.World.Legend ??= inventor.Legend(Legends.Rules(sim.World, sim.Log.Count > 0 ? sim.Log.All[^1].Id : 0));
        var fresh = gateway.Journal.Pending.ToList();
        store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
        foreach (var race in sim.World.Races.Values) output.WriteLine($"{race.Name}: {race.Looks}. {race.Nature}");
        foreach (var realm in Realms.Independent(sim.World).Where(r => r.Festival is not null))
            output.WriteLine($"{Statecraft.Title(realm)}: {realm.Festival!.Name} (month {realm.Festival.Month}), {realm.Festival.Rite}.");
        PrintLlmSummary(output, gateway, fresh);
        return 0;
    }

    /// <summary>
    /// The world's legend, written if it has none (the owner, 2026-10-29): a world made before legends gets one, told in
    /// full in its next episode. A world that has one is left as it is.
    /// </summary>
    private static int Legend(string path, TextWriter output, Func<LlmSettings, ILlmProvider?> providers)
    {
        using var store = OpenExisting(path);
        var (sim, gateway) = LoadLive(store, providers);
        if (sim.World.Legend is null)
        {
            var inventor = new LlmInventor(new StoryContext(sim.World, sim.Log, gateway));
            sim.World.Legend = inventor.Legend(Legends.Rules(sim.World, sim.Log.Count > 0 ? sim.Log.All[^1].Id : 0));
            var fresh = gateway.Journal.Pending.ToList();
            store.Save(sim.World, sim.Log, sim.Queue, gateway.Journal);
            PrintLlmSummary(output, gateway, fresh);
        }
        output.WriteLine(sim.World.Legend.Summary);
        return 0;
    }

    /// <summary>The players' answers (sub-project 13 N5): a tally of votes applied to the world before its next history.</summary>
    private static int Answer(string path, string[] rest, TextWriter output)
    {
        if (rest.Length < 1) return UsageError(output);
        using var store = OpenExisting(path);
        var (world, log, queue) = store.Load();
        var ctx = new FantasyWorld.Sim.Engine.SimContext(world, log, queue, Rng.ForStream(world.Seed, "answers", world.Date.Day));
        var told = PrayerSystem.Apply(ctx, Tally.Parse(File.ReadAllText(rest[0])));
        store.Save(world, log, queue);
        output.WriteLine($"The gods answered: {told.Count(e => e.Type == "PrayerAnswered")} prayer(s) granted, "
                         + $"{told.Count(e => e.Type == "PrayerUnheeded")} unheeded, {told.Count(e => e.Type == "GodsBlessed")} side(s) blessed.");
        return 0;
    }

    private static int Tell(string path, string[] rest, TextWriter output, Func<LlmSettings, ILlmProvider?> providers)
    {
        if (rest.Length < 1) return UsageError(output);
        var eventId = long.Parse(rest[0], CultureInfo.InvariantCulture);
        var depth = int.Parse(Option(rest, "--depth") ?? "4", CultureInfo.InvariantCulture);
        using var store = OpenExisting(path);
        var (world, log, _) = store.Load();
        if (!log.TryGet(eventId, out _)) throw new KeyNotFoundException($"No event #{eventId}.");
        var settings = store.LoadLlmSettings();
        var gateway = new LlmGateway(settings.Enabled ? providers(settings) : null, settings, store.LoadJournal());
        output.WriteLine(new LlmNarrator(new StoryContext(world, log, gateway)).NarrateChain(new HistoryQuery(log).WhyChain(eventId, depth)));
        store.SaveJournal(gateway.Journal);
        return 0;
    }

    private static int Stats(string path, TextWriter output, Func<DateTimeOffset> clock)
    {
        using var store = OpenExisting(path);
        var (world, log, _) = store.Load();
        var journal = store.LoadJournal();
        output.WriteLine($"{world.Date}: {log.Count} events, {world.ActiveCharacters.Count()} living notables, "
                         + $"{world.Realms.Values.Count(r => !r.Destroyed && r.LiegeId == 0)} independent realms, "
                         + $"{world.Wars.Values.Count(w => !w.Ended)} wars under way, {world.Artifacts.Count} artifacts.");
        var monthAgo = clock().AddDays(-30);
        output.WriteLine($"LLM: {journal.Count} answers journaled ({string.Join(", ", journal.All.GroupBy(c => c.Tier).OrderBy(g => g.Key).Select(g => $"{g.Key.ToString().ToLowerInvariant()} {g.Count()}"))}); "
                         + $"spend ${journal.All.Sum(c => c.CostUsd).ToString("0.00", CultureInfo.InvariantCulture)} in all, "
                         + $"${journal.SpendSince(monthAgo).ToString("0.00", CultureInfo.InvariantCulture)} in the last 30 days.");
        return 0;
    }

    private static int LlmCommand(string path, string[] rest, TextWriter output)
    {
        using var store = OpenExisting(path);
        var settings = store.LoadLlmSettings();
        var models = new SortedDictionary<string, string>(settings.Models, StringComparer.Ordinal);
        for (var i = 0; i < rest.Length - 1; i++)
        {
            if (rest[i] != "--model") continue;
            var parts = rest[i + 1].Split('=', 2);
            if (parts.Length != 2 || !Enum.GetNames<ModelTier>().Contains(parts[0], StringComparer.OrdinalIgnoreCase) || parts[1].Length == 0)
                throw new FormatException($"Use --model <tier>=<model>, with tier decide, arbitrate, invent or narrate (got '{rest[i + 1]}').");
            models[parts[0].ToLowerInvariant()] = parts[1];
        }
        var provider = Option(rest, "--provider") ?? settings.Provider;
        if (provider is not ("none" or "anthropic" or "openrouter"))
            throw new FormatException($"Unknown provider '{provider}'. Use none, anthropic or openrouter.");
        settings = settings with
        {
            Provider = provider,
            Models = models,
            StoryBeatsPerDay = int.Parse(Option(rest, "--story-beats") ?? settings.StoryBeatsPerDay.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
            FightBeatsReserved = int.Parse(Option(rest, "--fight-beats") ?? settings.FightBeatsReserved.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
            NarrationsPerDay = int.Parse(Option(rest, "--narrations") ?? settings.NarrationsPerDay.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
            MonthlySpendCapUsd = decimal.Parse(Option(rest, "--cap") ?? settings.MonthlySpendCapUsd.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
        };
        if (settings.StoryBeatsPerDay < 0 || settings.NarrationsPerDay < 0 || settings.MonthlySpendCapUsd < 0)
            throw new FormatException("Budgets and the spend cap cannot be negative.");
        if (settings.FightBeatsReserved < 0 || settings.FightBeatsReserved > settings.StoryBeatsPerDay)
            throw new FormatException("--fight-beats must be between 0 and the story beats.");
        if (rest.Length > 0) store.SaveLlmSettings(settings);

        output.WriteLine($"provider: {settings.Provider}");
        foreach (var tier in Enum.GetValues<ModelTier>())
            output.WriteLine($"  {tier.ToString().ToLowerInvariant(),-9} {settings.ModelFor(tier)}");
        output.WriteLine($"budget: {settings.StoryBeatsPerDay} story beats ({settings.FightBeatsReserved} kept for fights) and {settings.NarrationsPerDay} narrations per day; "
                         + $"cap ${settings.MonthlySpendCapUsd.ToString("0.00", CultureInfo.InvariantCulture)} per 30 days");
        var variable = settings.Provider switch { "anthropic" => "ANTHROPIC_API_KEY", "openrouter" => "OPENROUTER_API_KEY", _ => null };
        if (variable is not null)
            output.WriteLine($"credentials: {variable} {(Environment.GetEnvironmentVariable(variable) is { Length: > 0 } ? "is set" : "is not set, so the game runs offline")}");
        return 0;
    }

    /// <summary>
    /// Live quality evals (sub-project 5 §5): narration graded by a judge on fixed seeds, or decisions graded for
    /// character and arbitrations checked against their odds. Spending stops at the cap; the report is saved as JSON.
    /// </summary>
    private static int Eval(string suite, string[] rest, TextWriter output, Func<LlmSettings, ILlmProvider?> providers)
    {
        if (suite is not ("narration" or "decisions")) return UsageError(output);
        var models = new SortedDictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < rest.Length - 1; i++)
        {
            if (rest[i] != "--model") continue;
            if (rest[i + 1].Split('=', 2) is not [var tier, var model] || model.Length == 0 || !Enum.GetNames<ModelTier>().Contains(tier, StringComparer.OrdinalIgnoreCase))
                throw new FormatException($"Use --model <tier>=<model>, with tier decide, arbitrate, invent or narrate (got '{rest[i + 1]}').");
            models[tier.ToLowerInvariant()] = model;
        }
        var settings = new LlmSettings { Provider = Option(rest, "--provider") ?? "anthropic", Models = models };
        if (providers(settings) is not { } provider)
        {
            output.WriteLine($"error: evals run live and need a provider; set the API key for {settings.Provider} (see fw llm).");
            return 2;
        }
        var cap = decimal.Parse(Option(rest, "--cap") ?? "2", CultureInfo.InvariantCulture);
        var count = int.Parse(Option(rest, suite == "narration" ? "--samples" : "--draws") ?? (suite == "narration" ? "8" : "40"), CultureInfo.InvariantCulture);
        if (cap <= 0 || count <= 0) throw new FormatException("--cap, --samples and --draws must be positive.");
        var seeds = Option(rest, "--seeds")?.Split(',').Select(s => ulong.Parse(s, CultureInfo.InvariantCulture)).ToList();
        var report = suite == "narration"
            ? QualityEvals.Narration(provider, settings, cap, count, seeds)
            : QualityEvals.Decisions(provider, settings, cap, count, seeds);
        var path = QualityEvals.Save(report, Option(rest, "--out") ?? "evals");
        output.WriteLine($"{report.Suite}: {report.Scores.Count} graded, {report.Fallbacks} fell back to the rules; "
                         + string.Join(", ", report.Means.Select(m => $"{m.Key} {m.Value.ToString("0.00", CultureInfo.InvariantCulture)}")));
        foreach (var check in report.Checks) output.WriteLine($"{check.Key}: {check.Value}");
        output.WriteLine($"spent ${report.SpendUsd.ToString("0.0000", CultureInfo.InvariantCulture)} of ${cap.ToString("0.00", CultureInfo.InvariantCulture)}"
                         + $"{(report.Capped ? " (stopped at the cap)" : "")}; saved {path}");
        return 0;
    }
}
