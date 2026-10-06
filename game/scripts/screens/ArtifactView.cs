using FantasyWorld.Client;
using FantasyWorld.Sim.Views;
using Godot;

namespace FantasyWorld.Game;

/// <summary>An artifact's page: where it is, what it does, its lore and every change of hands.</summary>
public partial class ArtifactView : DocumentPage
{
    public ArtifactScreen Artifact { get; init; } = null!;

    public override void _Ready()
    {
        Frame("ArtifactScreen", $"Artifact · {Artifact.Kind}", $"{Artifact.Name}.", Artifact.Whereabouts);
        Facts.AddChild(Ui.Kicker("At a glance"));
        Fact("Material", Artifact.Material);
        Fact("Fame", Artifact.Fame.ToString());
        Fact("Powers", Artifact.Powers.Count == 0 ? "None known" : string.Join(", ", Artifact.Powers));
        if (Artifact.Curse is { } curse) Fact("Curse", curse);
        if (Artifact.Location is { } location)
        {
            var show = Ui.Button("Show on map", "ShowOnMap", primary: true);
            show.Pressed += () => Host.Router.Open(location.Ref);
            var pad = new MarginContainer { Name = "Action" };
            pad.AddThemeConstantOverride("margin_top", 14);
            pad.AddChild(show);
            Facts.AddChild(pad);
        }

        var lore = Section("Lore");
        var text = Ui.Title(Artifact.Lore.Length > 0 ? Artifact.Lore : "No song remembers it yet.", 22, "Lore", italic: false);
        lore.AddChild(text);
        var made = Section("Making");
        Row(made, "Made", Artifact.Made, last: true);
        if (Artifact.Provenance.Count > 0)
        {
            var hands = Section("Provenance");
            for (var i = 0; i < Artifact.Provenance.Count; i++)
            {
                var p = Artifact.Provenance[i];
                var words = p.To is { } to ? Named(to, $"{Capital(p.How)} → ") : LinkedText.Plain(Capital(p.How));
                Row(hands, p.Year > 0 ? $"Year {p.Year}" : "—", words, i == Artifact.Provenance.Count - 1);
            }
        }
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
