# Antigravity — your contract for the team hub (v2)

*You own this page and its design — the GSAP treatment stays (Paco's call, and it's good work).
This v2 replaces the old `teamhub/` folder; everything now lives here in `team_hub/`.*

## What happened on day one (why this file exists)

The first version of the page invented its own status — Mac install said "no Unity required"
(the opposite of the tested path), the progress table had wrong owners and stale states, and the
team's #1 action (merging PR #2) was missing. Claude corrected the content on 2026-07-08. Nothing
wrong with the design; the *content* drifted because it was authored instead of synced. Hence:

## The one rule

**Sync, don't author.** The page's facts come exclusively from:
- `data/status.json` — structured state: metrics, milestones, per-person actions, run paths.
- `data/CHANGELOG.md` — the narrative log (newest entry first).

Claude updates both at every work checkpoint. Your update loop, each time they change:
1. Open `data/status.json` + the top entry of `data/CHANGELOG.md`.
2. Update the affected sections of `index.html` (install callouts, progress table, roster, callouts).
3. Bump the visible "Updated:" pill in the hero to `status.json`'s `updated_at`.
4. Never soften, reword, or drop an action item; link targets come from the data too.

If the page and the data disagree, the data wins. If the data itself looks wrong, leave a note in
`REQUESTS.md` (create it) — don't patch around it.

## Design notes (binding but light)

- Your palette + type stand as shipped (`DESIGN_SYSTEM.md`). Keep the page scannable: no motion on
  the table/roster content people skim for tasks.
- The reduced-motion guard in `index.html` (skips the loader + tweens under
  `prefers-reduced-motion`) must survive future edits — it's an accessibility requirement.
- Keep the "source of truth is the repo" callout and the hero's Updated pill.

## Boundaries

- Edit inside `team_hub/` only.
- `.github/workflows/deploy-team-hub.yml` was switched to **manual-only** (`workflow_dispatch`):
  GitHub Pages would publish the team's names and plans to the public internet, so deploys wait on
  an explicit team decision. Don't re-enable push triggers; infra changes go through Paco.
- Schema changes to `data/status.json`: request via `REQUESTS.md`, Claude versions the schema.
