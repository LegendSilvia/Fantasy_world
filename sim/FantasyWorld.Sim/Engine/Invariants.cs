using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;

namespace FantasyWorld.Sim.Engine;

public sealed class InvariantViolationException : Exception
{
    public InvariantViolationException(IReadOnlyList<string> errors)
        : base("World invariants violated:" + Environment.NewLine + string.Join(Environment.NewLine, errors)) =>
        Errors = errors;

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>Split into partial files per subject; each Check* hook is optional.</summary>
public static partial class Invariants
{
    /// <summary>Checks log events from startIndex onward, plus (unless entities is false) every entity. Returns human-readable violations.</summary>
    public static IReadOnlyList<string> Check(World world, EventLog log, int startIndex = 0, bool entities = true)
    {
        var errors = new List<string>();
        CheckLog(world, log, startIndex, errors);
        if (!entities) return errors;
        CheckRegions(world, log, errors);
        CheckSettlements(world, log, errors);
        CheckCultures(world, errors);
        CheckPeople(world, log, errors);
        CheckArtifacts(world, log, errors);
        CheckRealms(world, log, errors);
        CheckStructures(world, log, errors);
        return errors;
    }

    static partial void CheckRealms(World world, EventLog log, List<string> errors);

    static partial void CheckStructures(World world, EventLog log, List<string> errors);

    static partial void CheckPeople(World world, EventLog log, List<string> errors);

    static partial void CheckArtifacts(World world, EventLog log, List<string> errors);

    private static void CheckCultures(World world, List<string> errors)
    {
        foreach (var region in world.Regions.Values)
        {
            if (region.CultureId != 0 && !world.Cultures.ContainsKey(region.CultureId))
                errors.Add($"Region #{region.Id} has unknown culture #{region.CultureId}.");
            if (region.FaithId != 0 && !world.Faiths.ContainsKey(region.FaithId))
                errors.Add($"Region #{region.Id} has unknown faith #{region.FaithId}.");
        }
        foreach (var culture in world.Cultures.Values)
            if (!world.Faiths.ContainsKey(culture.FaithId))
                errors.Add($"Culture #{culture.Id} has unknown faith #{culture.FaithId}.");
        foreach (var faith in world.Faiths.Values)
        {
            if (faith.Zeal is < 0 or > 100) errors.Add($"Faith #{faith.Id} has zeal {faith.Zeal} outside 0–100.");
            if (faith.ParentFaithId != 0 && !world.Faiths.ContainsKey(faith.ParentFaithId))
                errors.Add($"Faith #{faith.Id} split from unknown faith #{faith.ParentFaithId}.");
        }
        foreach (var realm in world.Realms.Values)
            if (realm.FaithId != 0 && !world.Faiths.ContainsKey(realm.FaithId))
                errors.Add($"Realm #{realm.Id} has unknown faith #{realm.FaithId}.");
    }

    private static void CheckLog(World world, EventLog log, int startIndex, List<string> errors)
    {
        var all = log.All;
        for (var i = Math.Max(0, startIndex); i < all.Count; i++)
        {
            var ev = all[i];
            if (i > 0)
            {
                var prev = all[i - 1];
                if (ev.Id <= prev.Id) errors.Add($"Event #{ev.Id} id does not increase after #{prev.Id}.");
                if (ev.Date < prev.Date) errors.Add($"Event #{ev.Id} is dated before #{prev.Id}.");
            }
            if (ev.Date > world.Date) errors.Add($"Event #{ev.Id} is dated in the future ({ev.Date} > {world.Date}).");
            if (ev.Causes.Count > EventLog.MaxCauses) errors.Add($"Event #{ev.Id} has {ev.Causes.Count} causes.");
            foreach (var cause in ev.Causes)
            {
                if (!log.TryGet(cause.EventId, out var c))
                    errors.Add($"Event #{ev.Id} cites missing cause #{cause.EventId}.");
                else if (c.Id >= ev.Id || c.Date > ev.Date)
                    errors.Add($"Event #{ev.Id} cites later event #{c.Id}.");
            }
        }
    }

    private static void CheckRegions(World world, EventLog log, List<string> errors)
    {
        foreach (var (id, region) in world.Regions)
        {
            var name = $"Region #{id}";
            if (region.Id != id) errors.Add($"{name} is stored under the wrong key ({region.Id}).");
            Range(errors, name, "fertility", region.Fertility);
            Range(errors, name, "danger", region.Danger);
            Range(errors, name, "ley power", region.LeyPower);
            if (!(region.Food >= 0)) errors.Add($"{name} has invalid food stores {region.Food}.");
            foreach (var sid in region.Settlements)
                if (!world.Settlements.TryGetValue(sid, out var s) || s.RegionId != id)
                    errors.Add($"{name} lists settlement #{sid}, which is not in it.");
            Conditions(errors, log, name, region.Conditions);
        }
    }

    private static void CheckSettlements(World world, EventLog log, List<string> errors)
    {
        foreach (var (id, s) in world.Settlements)
        {
            var name = $"Settlement #{id}";
            if (s.Id != id) errors.Add($"{name} is stored under the wrong key ({s.Id}).");
            if (!world.Regions.TryGetValue(s.RegionId, out var region) || !region.Settlements.Contains(id))
                errors.Add($"{name} is not listed by its region #{s.RegionId}.");
            if (s.Children < 0 || s.Adults < 0 || s.Elders < 0) errors.Add($"{name} has a negative population.");
            if (!(s.Wealth >= 0)) errors.Add($"{name} has invalid wealth {s.Wealth}.");
            Range(errors, name, "unrest", s.Unrest);
            Conditions(errors, log, name, s.Conditions);
        }
    }

    private static void Range(List<string> errors, string name, string what, double value)
    {
        if (!(value >= 0 && value <= 100)) errors.Add($"{name} has {what} {value} outside 0–100.");
    }

    private static void Conditions(List<string> errors, EventLog log, string name, SortedDictionary<string, long> conditions)
    {
        foreach (var (condition, eventId) in conditions)
            if (!log.TryGet(eventId, out _))
                errors.Add($"{name} condition '{condition}' cites missing event #{eventId}.");
    }
}
