# Brief for Anti — add a "Team Run" section to the hub

*From Paco + Claude, 2026-07-10. This is a work order, not content to paste verbatim. Build it in your
own design language; you own the hub's look.*

## The job

Teammates need to run a small study app on their own machine and send back the result files (this is
our machine-compatibility + first study dry-run). Add a clear, friendly **"Team Run"** section/page to
the hub that walks a non-technical teammate through it.

## Source of truth (do not invent, do not paraphrase loosely)

Everything factual comes from **`docs/TEAM_RUN.md`** in the repo. Pull the steps, the file names, the
save-folder paths, and the troubleshooting from there. If something isn't in that file, don't make it
up — leave a `TODO(Paco)` placeholder and flag it. In particular:

- The **download links are not live yet** (the app release is still being verified). Put a clear
  "Download — coming soon, pending build verification" state where the Windows download button will go,
  and a note that Mac users get the app from Michael. Do **not** fabricate a URL.

## What it should contain (from TEAM_RUN.md)

1. One-line what/why + the ~5-minute time estimate.
2. **Get the app** — Windows (download + "Run anyway" on the SmartScreen warning), Mac (from Michael,
   right-click → Open for unsigned).
3. **Do one session** — id → instructions → per room: Enter → walk (WASD + mouse, look around 15–30s) →
   Esc → rate 1–7. Four rooms.
4. **Send 3 files** — the "Open results folder" button → the three `response-*` files → post in team
   chat + say which machine.
5. A short "what we're checking" note (frame rate per machine; the participant never sees a counter).
6. Troubleshooting (black screen → report it; can't move → click to focus; stuck → Esc).

## Design guidance

- Use the existing hub design system (`team_hub/DESIGN_SYSTEM.md`) — cards, callouts, your GSAP
  entrance animations. The numbered steps read well as a callout list or a simple ordered card stack.
- A **status callout** at the top ("Status: waiting on the verified build — you'll get a ping when it's
  ready") so nobody starts before there's something to download.
- Keep it skimmable on a phone — several teammates will read this on mobile.

## Guardrails (please respect)

- **Do not deploy / publish.** GitHub Pages stays manual until the team okays going public (names +
  plans). Build the page; Paco flips the deploy switch.
- **Only touch `team_hub/`.** Don't edit `docs/`, the Unity project, or anything outside your folder.
- If you want to sync structured content, you can add a `team_hub/data/team_run.json` and read it — but
  the numbers/paths must match `docs/TEAM_RUN.md`.

## Definition of done

A teammate who has never opened the repo can read the hub section and complete a session + send their
files without asking a question — except "where do I download it," which is intentionally blocked until
the release is live.
