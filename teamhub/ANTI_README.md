# Antigravity — your contract for the team hub

*You own the team-hub page. Everything you need is in this folder; everything outside it is
read-only context for you and off-limits to edit (enforced at system level, but honor it in
spirit too).*

## The one rule that matters

**Render, don't author.** The hub's content comes exclusively from:
- `status.json` — the structured state (metrics, milestones, per-person actions, run paths).
  If it isn't in this file, it doesn't go on the page.
- `CHANGELOG.md` — the narrative log for any "recent updates" section.

Claude (the other agent on this project) updates both at every work checkpoint; you re-render.
Never write status from your own reading of the repo — two authors of truth is how a status page
starts lying to the team. If the data looks stale or contradictory, surface a "last updated" date
prominently rather than patching the content yourself.

## Design language (binding)

The lab's official system — take tokens verbatim from
`https://xrlab.ucsd.edu/ka/160sp/_track_pages_shared.css` (`:root`):
cream `#F7F4EF` page (light theme only — the reference site has no dark mode), white cards,
ink `#2C2C2C`, muted `#6B6B6B`, borders `#D8D0C5`, navy `#1C3D3A` chrome with gold `#F5A623`
wordmark (+ subtitle `#7AACA0`), teal `#2A7868` = valid/primary, rose `#A84F6B` = blocked,
amber `#E8872A` = attention/CTA. Georgia for headings, Arial for body.
A previous static render exists for reference (same content as status.json v1); match its
information design: metrics strip → milestone strip → shipped table → per-person action cards →
run-the-app paths → source-of-truth footer.

## On the animation libraries (GSAP / anime.js / motion.dev / react-spring)

Paco passed these as design research. Honest guidance: this is a **status page** — its job is
scanability, so keep motion in the "barely there" class: fade/slide-in on section reveal, a subtle
count-up on metrics at most, `prefers-reduced-motion` respected. If the page is vanilla JS, use
**motion.dev (Motion One) or anime.js** (small, no build step); GSAP is fine but heavy for this;
**react-spring only if you build in React**. Do not animate the per-person cards' content —
people skim those for their tasks.

## Boundaries

- Edit only inside `teamhub/`. The Unity project, handoffs, spec/, and docs/ are other lanes.
- Don't rename or restructure `status.json` fields; if you need a schema change, leave a note in
  `teamhub/REQUESTS.md` (create it) and Claude will version the schema.
- Keep the "source of truth is the repo" banner — link handoffs/ and the PRs.
- The page must work as a plain local file (no server assumptions) unless Paco says otherwise.
