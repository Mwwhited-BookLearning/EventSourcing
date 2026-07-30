# Protocol: splitting large multi-file work across parallel agents

Used repeatedly this session for work that's (a) too large for one
pass, (b) naturally decomposes into independent units (one per domain,
one per ADR range, one per review lens), and (c) needs each unit
grounded in real research/verification, not just template-filling.
Real examples: building all 15 `docs/domains/*.md` files, adding
synonyms to all 15 domain glossaries, the compliance-review pass across
74 ADRs, the three-lens design review (propagation / fresh-eyes
contradictions / buildability).

## When to use this vs. just doing it inline

- **Use parallel background agents** when the work splits into ≥4
  genuinely independent units (different files, no shared state) and
  each unit needs real judgment/research, not just mechanical
  repetition of one edit.
- **Don't** reach for this for a single file, a quick fix, or anything
  where the units aren't actually independent (e.g. if unit B's content
  depends on what unit A decides) — do those sequentially instead.
- This is the `Agent` tool used directly, run in background, **not**
  the `Workflow` tool — `Workflow` requires the user to have explicitly
  opted into multi-agent orchestration (an "ultracode" trigger or an
  explicit request in their own words); don't reach for it just because
  a task is large.

## How to split

- **Group by disjoint file ownership first**, batch size second. Every
  agent in a batch must own a set of files no other concurrently-
  running agent touches — this session's domain-glossary batches were
  split 4/4/4/3 specifically so no two agents ever raced on the same
  file.
- **4 batches of ~4 units is a reasonable default** for ~15-16 units;
  scale the batch count to keep each agent's per-file workload roughly
  even, not strictly equal.
- **Give every agent the same shared context** (which format/template
  to follow, which files are off-limits, which project conventions
  apply) rather than assuming they'll infer it — each agent starts with
  zero memory of this conversation.

## What every dispatch prompt needs

1. The repo root and a pointer to read `CLAUDE.md` first.
2. The exact list of files this agent owns — and an explicit
   "do not touch X" list for files another concurrent agent owns or a
   later consolidation step owns (this session's phrasing: "another
   task owns that").
3. The real template/example to match (a specific existing file to
   diff against, not just a description).
4. The verification bar (see `verify-before-citing.md`) — restate it,
   don't assume it's implied.
5. A required final-report format — one line per file/unit, with a
   clear verdict, so results can be scanned and consolidated quickly
   rather than re-read in full. Ask explicitly for "anything you
   checked but decided NOT to do, and why" — this is where the most
   valuable findings tend to surface (a near-miss synonym correctly
   rejected, a citation that couldn't be verified).

## After the batch returns

- **Consolidate centrally, don't let agents touch shared files.**
  Anything that needs a shared file (`references.md`, `CLAUDE.md`,
  `docs/10-open-questions.md`) should be reported back by each agent,
  not edited by them — merge it yourself afterward to avoid concurrent-
  write conflicts on the one file every batch wants to touch.
- **Spot-check, don't blindly trust the "done" report.** An agent's
  summary describes what it intended to do; verify a sample of its
  actual edits before reporting the batch complete.
