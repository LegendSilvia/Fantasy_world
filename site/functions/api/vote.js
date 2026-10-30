// A player's answer (sub-project 13 N5: the players are the gods): grant or deny a prayer, or bless one side of a
// face-off; and what a viewer cares for (the owner, 2026-10-30: the videos follow the characters viewers like): follow a
// character (on/off), or a character's page seen. One vote per visitor per item; a second vote replaces the first. Visitors are told apart by a hash of
// their address and browser, never stored as such.
const CHOICES = { prayer: ["grant", "deny"], blessing: ["a", "b"], follow: ["on", "off"], view: ["seen"] };

async function voter(request) {
  const who = `${request.headers.get("cf-connecting-ip") ?? ""}|${request.headers.get("user-agent") ?? ""}`;
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(`fantasy-world|${who}`));
  return [...new Uint8Array(digest)].slice(0, 16).map(b => b.toString(16).padStart(2, "0")).join("");
}

export async function onRequestPost({ request, env }) {
  let body;
  try { body = await request.json(); } catch { return Response.json({ error: "bad json" }, { status: 400 }); }
  const { kind, item, choice } = body ?? {};
  const id = Number(item);
  if (!CHOICES[kind] || !CHOICES[kind].includes(choice) || !Number.isSafeInteger(id) || id <= 0)
    return Response.json({ error: "bad vote" }, { status: 400 });
  await env.PRAYERS.prepare(
    "INSERT INTO votes (kind, item, voter, choice, at) VALUES (?1, ?2, ?3, ?4, ?5) " +
    "ON CONFLICT (kind, item, voter) DO UPDATE SET choice = excluded.choice, at = excluded.at")
    .bind(kind, id, await voter(request), choice, Date.now()).run();
  return Response.json({ ok: true });
}
