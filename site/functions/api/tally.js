// The players' answers so far, for the website and for the daily run (sub-project 13 N5):
// {"prayers": {"12": {"grant": 3, "deny": 1}}, "blessings": {"456": {"a": 2, "b": 0}}}. Counts only; no voter is shown.
export async function onRequestGet({ env }) {
  const { results } = await env.PRAYERS.prepare("SELECT kind, item, choice, COUNT(*) AS n FROM votes GROUP BY kind, item, choice").all();
  const tally = { prayers: {}, blessings: {} };
  for (const row of results ?? []) {
    const section = row.kind === "prayer" ? tally.prayers : row.kind === "blessing" ? tally.blessings : null;
    if (!section) continue;
    const entry = section[row.item] ??= row.kind === "prayer" ? { grant: 0, deny: 0 } : { a: 0, b: 0 };
    entry[row.choice] = row.n;
  }
  return Response.json(tally, { headers: { "cache-control": "no-store" } });
}
