# Fantasy World — Narration Polish & Evals Design (sub-project 5)

**Status:** draft for review · **Builds on:** the sim core's narration and LLM layer (Plans 5–6), the views
(sub-project 2) and the client (sub-project 4)

## 1. Purpose

The world tells its story in three voices: templates (offline, always), the LLM narrator (headlines, digest,
chains) and the LLM's decisions and arbitrations (motives, accounts). Played on real worlds, the template voice
shows its seams: "In year 2, The Banners Age of Thonae began." repeats the headline in its body, realms lack their
article ("Kingdom of Wosmeim freed Neir"), every war reads the same, and numbers sit as digits in prose. The LLM
voice has never been measured. This sub-project makes the offline voice good enough to ship on its own, gives the
LLM voice a chronicler's style, and adds evals that keep both honest.

## 2. Principles

- **Templates are a product, not a fallback.** Most players will run offline; the template voice must read like a
  chronicle, not a log.
- **Deterministic variety.** Several phrasings per event type, chosen by a hash of the event id, so the same world
  always reads the same and no two adjacent lines sound alike.
- **Measured, not guessed.** Every voice has checks: structural checks in the test suite (fast, free), and
  rubric-graded evals of the LLM voice (opt-in, cost-capped, results stored).
- **No new determinism risk.** Narration never feeds back into the simulation; the journal still keys every LLM
  call.

## 3. Template voice

- **Grammar:** a small English layer: articles for titled realms ("the Kingdom of Wosmeim"), sentence-case joins
  (no capital after a comma), plurals, numbers in words up to twelve, ordinals for years in prose ("in the second
  year"), possessives.
- **Variety:** 2–4 phrasings for the 40 most common event types, chosen by event id; connective variety in chains
  ("because", "for", "after", "which led to").
- **Headline and body:** the digest's headline is a short title ("The Banners Age begins in Thonae"); the body adds
  what the title does not say (cause, place, consequence), never the title again.
- **The story of why:** a why chain told as one paragraph (oldest cause first), shown above the cards.

## 4. LLM voice

- **Chronicler persona:** each world gets a chronicler (name, temperament, era-appropriate diction) generated once
  and kept in the world bible; every narration prompt carries it.
- **Style guide:** short sentences, concrete nouns, names as given (never invented), no anachronisms, no modern
  idiom, at most one figure of speech per paragraph; British spelling to match the templates.
- **Guardrails:** narration may only use facts in the brief; names and numbers are checked against it, and a
  failing narration falls back to templates (and is logged for evals).

## 5. Evals

- **Structural (every test run):** no unfilled placeholders, no doubled words, no title repeated in body, every
  linked name present, sentence length bounds, digest of 3–8 headlines, chain paragraphs mention every root.
- **Fidelity (every test run, fake provider):** narration guardrails reject invented names and numbers.
- **Quality (opt-in, `fw eval narration|decisions`):** a sample of events, digests and chains from fixed seeds is
  narrated live and graded by a judge model against a rubric (faithful, clear, in voice, varied), with costs capped
  and results stored per world under `evals/`; decisions are graded for in-character choices, arbitrations for
  odds respected (a statistical check over many draws).

## 6. Testing

Golden narration files for fixed seeds (regenerated deliberately), the structural and fidelity checks above in
xUnit, and the opt-in quality evals documented with their cost.

## 7. Plan outline (~7 tasks)

1. English layer (articles, joins, numbers, ordinals, possessives) and template fixes.
2. Phrasing variety and chain connectives.
3. Digest headline/body split and the story-of-why paragraph (views and client).
4. Chronicler persona and the narration style guide in prompts.
5. Narration guardrails (facts-only checks, fallback, logging).
6. Structural and fidelity evals in the test suite; narration goldens.
7. `fw eval` for live quality evals with a judge, cost caps and stored results; README.
