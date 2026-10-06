using System.Text;

namespace FantasyWorld.Sim.Llm;

/// <summary>
/// Safety of text in prompts (spec §8.6): names and lore, including earlier LLM output, re-enter prompts only
/// as data, flattened to one line without tag characters and inside delimited sections the bible marks as data.
/// </summary>
public static class PromptText
{
    public static string Data(string? text, int max = 160)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(Math.Min(text.Length, max));
        var space = false;
        foreach (var ch in text)
        {
            if (sb.Length >= max) break;
            if (ch is '<' or '>' or '`' or '{' or '}' or '[' or ']') continue;
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                space = sb.Length > 0;
                continue;
            }
            if (space) sb.Append(' ');
            space = false;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    public static string Section(string tag, string body) => $"<{tag}>\n{body.TrimEnd()}\n</{tag}>";
}
