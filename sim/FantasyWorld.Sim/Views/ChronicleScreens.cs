using System.Globalization;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;
using FantasyWorld.Sim.Model;
using FantasyWorld.Sim.Narration;
using FantasyWorld.Sim.Query;
using FantasyWorld.Sim.Systems;

namespace FantasyWorld.Sim.Views;

public sealed record ChronicleRow(long EventId, Category Category, int Importance, LinkedText Text);

public sealed record ChronicleSeason(string Label, IReadOnlyList<ChronicleRow> Rows);

/// <summary>The "Full chronicle" view (sub-project 2 §3.5): every event in a span, newest season first, paged.</summary>
public sealed record ChronicleScreen(int FromYear, int ToYear, int Total, int Page, int Pages, IReadOnlyList<ChronicleSeason> Seasons)
{
    public const int PageSize = 50;

    public static ChronicleScreen Build(World world, EventLog log, int? fromYear = null, int? toYear = null,
        Category? only = null, int minImportance = 0, int page = 1)
    {
        var to = toYear ?? world.Date.Year;
        var from = fromYear ?? Math.Max(1, to - 9);
        var narrator = new TemplateNarrator(world);
        var nameOf = Names.Of(world);
        var events = log.All.Where(e => e.Date.Year >= from && e.Date.Year <= to && e.Importance >= minImportance
                                        && (only is null || Categories.Of(e) == only)).Reverse().ToList();
        var pages = Math.Max(1, (events.Count + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 1, pages);
        var seasons = events.Skip((page - 1) * PageSize).Take(PageSize)
            .GroupBy(e => Digest.SeasonLabel(e.Date))
            .Select(g => new ChronicleSeason(g.Key, g.Select(e => new ChronicleRow(e.Id, Categories.Of(e), e.Importance,
                LinkedText.Link(narrator.Sentence(e), LinkedText.Mentions(e), nameOf))).ToList()))
            .ToList();
        return new ChronicleScreen(from, to, events.Count, page, pages, seasons);
    }
}

public sealed record CouncilSeat(string Seat, Mention Member);

public sealed record TreatyRow(string Kind, Mention With, int SinceYear);

public sealed record WarRow(EntityRef War, string Name, string Side, bool Ended);

public sealed record EraRow(int Year, string Name, string Phase, long EventId);

/// <summary>A realm's page (sub-project 2 §3.5): who rules and by what law, its council, vassals, treaties, wars and eras.</summary>
public sealed record RealmScreen(
    EntityRef Ref, string Title, string Rank, string Government, string SuccessionLaw, string MagicLaw, Mention? Ruler, Place Capital,
    int Legitimacy, int Stability, int Treasury, string Phase, int Regions, Mention? Liege, IReadOnlyList<Mention> Vassals,
    IReadOnlyList<CouncilSeat> Council, IReadOnlyList<TreatyRow> Treaties, IReadOnlyList<WarRow> Wars, IReadOnlyList<EraRow> Eras, Mention? Faith = null)
{
    public static RealmScreen Build(World world, EventLog log, long realmId)
    {
        var realm = world.Realms.TryGetValue(realmId, out var found) ? found : throw new KeyNotFoundException($"No realm #{realmId}.");
        Mention Of(EntityRef r) => new(r, Names.Of(world)(r) ?? r.ToString());
        var capital = world.Settlements[realm.CapitalId];
        return new RealmScreen(realm.Ref, Statecraft.Title(realm), realm.Rank.ToString(), Words(realm.Government.ToString()),
            realm.SuccessionLaw.ToString(), realm.MagicLaw.ToString(),
            Realms.RulerOf(world, realm) is { } ruler ? Of(ruler.Ref) : null,
            new Place(capital.Ref, capital.Name, MapPoint.Of(world, capital.Ref)),
            (int)Math.Round(realm.Legitimacy), (int)Math.Round(realm.Stability), (int)Math.Round(realm.Treasury), realm.Phase.ToString(),
            Realms.Domain(world, realm).Count(),
            realm.LiegeId != 0 && world.Realms.TryGetValue(realm.LiegeId, out var liege) ? Of(liege.Ref) : null,
            Realms.VassalsOf(world, realm).Select(v => Of(v.Ref)).ToList(),
            realm.Council.Where(kv => world.Characters.ContainsKey(kv.Value)).Select(kv => new CouncilSeat(kv.Key, Of(world.Characters[kv.Value].Ref))).ToList(),
            realm.Treaties.Where(t => world.Realms.ContainsKey(t.With)).Select(t => new TreatyRow(t.Kind.ToString(), Of(world.Realms[t.With].Ref),
                log.TryGet(t.SinceEventId, out var since) ? since.Date.Year : 1)).ToList(),
            world.Wars.Values.Where(w => w.Involves(realm.Id)).OrderByDescending(w => w.Id)
                .Select(w => new WarRow(w.Ref, w.Name, w.IsAttacker(realm.Id) ? "attacker" : "defender", w.Ended)).ToList(),
            new HistoryQuery(log).Eras(realm.Ref).Select(e => new EraRow(e.Date.Year, e.Payload["era"], e.Payload.GetValueOrDefault("phase") ?? "", e.Id)).ToList(),
            world.Faiths.TryGetValue(realm.FaithId, out var faith) ? Of(faith.Ref) : null);
    }

    private static string Words(string pascal) => string.Concat(pascal.Select((ch, i) => i > 0 && char.IsUpper(ch) ? " " + char.ToLowerInvariant(ch) : ch.ToString()));
}

public sealed record ProvenanceRow(int Year, string How, Mention? From, Mention? To, long EventId);

/// <summary>An artifact's page (sub-project 2 §3.5): powers, a revealed curse, lore, where it is, how it was made and every change of hands.</summary>
public sealed record ArtifactScreen(
    EntityRef Ref, string Name, string Kind, string Material, int Fame, IReadOnlyList<string> Powers, string? Curse, string Lore,
    string Whereabouts, Place? Location, LinkedText Made, IReadOnlyList<ProvenanceRow> Provenance)
{
    public static ArtifactScreen Build(World world, EventLog log, long artifactId)
    {
        var x = world.Artifacts.TryGetValue(artifactId, out var found) ? found : throw new KeyNotFoundException($"No artifact #{artifactId}.");
        var nameOf = Names.Of(world);
        Mention? Of(long characterId) => world.Characters.TryGetValue(characterId, out var c) ? new Mention(c.Ref, Names.Full(world, c)) : null;
        var (whereabouts, location) = x.Place switch
        {
            ArtifactPlace.Held when world.Characters.TryGetValue(x.HolderId, out var holder) =>
                ($"Held by {Names.Full(world, holder)}", world.Settlements.TryGetValue(holder.SettlementId, out var s) ? new Place(s.Ref, s.Name, MapPoint.Of(world, s.Ref)) : null),
            ArtifactPlace.Treasury => ($"In the treasury of {world.Settlements[x.SettlementId].Name}",
                new Place(world.Settlements[x.SettlementId].Ref, world.Settlements[x.SettlementId].Name, MapPoint.Of(world, world.Settlements[x.SettlementId].Ref))),
            ArtifactPlace.Lost => ($"Lost in {world.Regions[x.RegionId].Name}",
                new Place(world.Regions[x.RegionId].Ref, world.Regions[x.RegionId].Name, MapPoint.Of(world, world.Regions[x.RegionId].Ref))),
            _ => ("Destroyed", (Place?)null),
        };
        var made = x.OriginEventId != 0 && log.TryGet(x.OriginEventId, out var origin)
            ? LinkedText.Link($"Year {origin.Date.Year}: {new TemplateNarrator(world).Sentence(origin)}", LinkedText.Mentions(origin), nameOf)
            : LinkedText.Plain("Made before recorded history.");
        return new ArtifactScreen(x.Ref, x.Name, x.Type.ToString(), x.Material, (int)Math.Round(x.Fame),
            x.Powers.Select(p => p.Kind == "attribute" ? $"+{p.Amount} {p.Attribute}" : p.Kind).ToList(),
            x.CurseRevealed ? x.Curse : null, x.Lore, whereabouts, location, made,
            x.Provenance.Select(p => new ProvenanceRow(log.TryGet(p.EventId, out var e) ? e.Date.Year : 0, p.How, Of(p.From), Of(p.To), p.EventId)).ToList());
    }
}
