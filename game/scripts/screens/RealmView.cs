using FantasyWorld.Client;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>A realm's page: ruler, laws and standing in the facts card; council, vassals, treaties, wars and eras.</summary>
public partial class RealmView : DocumentPage
{
    public RealmScreen Realm { get; init; } = null!;

    public override void _Ready()
    {
        Frame("RealmScreen", $"Realm · {Realm.Rank}", $"{Realm.Title}.",
            $"{Realm.Government} · {Realm.Regions} region{(Realm.Regions == 1 ? "" : "s")} · {Realm.Phase.ToLowerInvariant()}");
        Facts.AddChild(Ui.Kicker("At a glance"));
        if (Realm.Ruler is { } ruler) Fact("Ruler", ruler.Name, ruler.Ref);
        Fact("Capital", Realm.Capital.Label, Realm.Capital.Ref);
        if (Realm.Race is { } race) Fact("People", race);
        if (Realm.Festival is { } festival) Fact("Festival", festival);
        if (Realm.Liege is { } liege) Fact("Liege", liege.Name, liege.Ref);
        Fact("Succession", Realm.SuccessionLaw);
        if (Realm.Faith is { } faith) Fact("Faith", faith.Name);
        Fact("Magic", Realm.MagicLaw);
        Fact("Legitimacy · stability", $"{Realm.Legitimacy} · {Realm.Stability}");
        Fact("Treasury", $"{Realm.Treasury:N0} crowns");
        var show = Ui.Button("Show on map", "ShowOnMap", primary: true);
        show.Pressed += () => Host.Router.Open(Realm.Capital.Ref);
        var pad = new MarginContainer { Name = "Action" };
        pad.AddThemeConstantOverride("margin_top", 14);
        pad.AddChild(show);
        Facts.AddChild(pad);

        if (Realm.Stocks is { Count: > 0 } stocks)
        {
            var stores = Section("Stores");
            for (var i = 0; i < stocks.Count; i++)
            {
                var s = stocks[i];
                var line = $"{s.Amount:N0}" + (s.Yearly > 0 ? $" · yields {s.Yearly:N0} a year" : "") + (s.Short ? " · short" : "");
                Row(stores, char.ToUpperInvariant(s.Resource[0]) + s.Resource[1..], new LinkedText(line, Array.Empty<TextLink>()), i == stocks.Count - 1);
            }
        }
        if (Realm.Council.Count > 0)
        {
            var council = Section("Council");
            for (var i = 0; i < Realm.Council.Count; i++)
                Row(council, char.ToUpperInvariant(Realm.Council[i].Seat[0]) + Realm.Council[i].Seat[1..], Named(Realm.Council[i].Member), i == Realm.Council.Count - 1);
        }
        if (Realm.Vassals.Count > 0)
        {
            var vassals = Section("Vassals");
            for (var i = 0; i < Realm.Vassals.Count; i++) Row(vassals, "Vassal", Named(Realm.Vassals[i]), i == Realm.Vassals.Count - 1);
        }
        if (Realm.Treaties.Count > 0)
        {
            var treaties = Section("Treaties");
            for (var i = 0; i < Realm.Treaties.Count; i++)
                Row(treaties, $"Year {Realm.Treaties[i].SinceYear}", Named(Realm.Treaties[i].With, $"{Realm.Treaties[i].Kind} with "), i == Realm.Treaties.Count - 1);
        }
        if (Realm.Wars.Count > 0)
        {
            var wars = Section("Wars");
            for (var i = 0; i < Realm.Wars.Count; i++)
            {
                var war = Realm.Wars[i];
                Row(wars, war.Ended ? "Ended" : "Ongoing", Named(new Mention(war.War, war.Name), after: $" · as {war.Side}"), i == Realm.Wars.Count - 1);
            }
        }
        if (Realm.Eras.Count > 0)
        {
            var eras = Section("Eras");
            for (var i = 0; i < Realm.Eras.Count; i++)
            {
                var era = Realm.Eras[i];
                var row = Row(eras, $"Year {era.Year}", LinkedText.Plain(era.Phase.Length > 0 ? $"{era.Name} · {era.Phase.ToLowerInvariant()}" : era.Name), i == Realm.Eras.Count - 1);
                var eventId = era.EventId;
                row.GuiInput += input =>
                {
                    if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) Host.Router.Explain(eventId);
                };
            }
        }
    }
}
