using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyWorld.Sim.Chronicle;
using FantasyWorld.Sim.Events;

namespace FantasyWorld.Sim.Views;

/// <summary>
/// The JSON contract a client reads (sub-project 2 §2): camelCase names, enums as strings, entity refs as the
/// "char#12" strings the CLI prints, indented for diffs.
/// </summary>
public static class ViewJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new RefConverter() },
    };

    public static string Serialize<T>(T view) => JsonSerializer.Serialize(view, Options);

    private sealed class RefConverter : JsonConverter<EntityRef>
    {
        public override EntityRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Importance.ParseRef(reader.GetString() ?? throw new JsonException("Expected an entity like char#12."));

        public override void Write(Utf8JsonWriter writer, EntityRef value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}
