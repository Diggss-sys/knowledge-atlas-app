# Getting started — run Room Studio on your machine

*For the team. Windows folks with a beefy PC can grab the built exe; everyone on a Mac runs the
project from source in Unity (an M1/M2 MacBook handles it fine — the scene is light). ~30 min the
first time, almost all of it Unity importing.*

> **Which branch?** `main` is current through PR #3 (engine, the operator studio, the participant
> runner). The Team Run app release and the Mac performance fixes are on **`paco/ui-foundation`**
> (PR #4, open — review pending) — check that branch out if you want the latest before it merges.

## Path A — Mac (and any dev machine): run from source

1. **Get the repo.** You need collaborator access (ask Diego).
   ```
   git clone https://github.com/Diggss-sys/knowledge-atlas-app.git
   cd knowledge-atlas-app
   git checkout paco/ui-foundation
   ```
2. **Install Unity Hub** from unity.com/download (Apple Silicon version on M-series Macs), sign in,
   pick the free **Personal** license.
3. **Install the editor, exact version 6000.3.16f1.** If the Hub's list doesn't show it, use the
   [Unity download archive](https://unity.com/releases/editor/archive) and click its "Install" Hub
   link. Modules: none required for Macs (add *Mac Build Support* only if you'll build the .app).
4. **Open the project**: Hub → Add → select the repo's `unity/` folder → open with 6000.3.16f1.
   First open imports HDRP — **10–20 minutes**, one time. If a prompt asks about entering Safe Mode
   while packages import, choose "Ignore".
5. **Bootstrap runs by itself** (creates the HDRP settings, scene, materials). If the Console shows
   "XR settings not ready", run **RoomGen ▸ Bootstrap Project** once — otherwise you don't need to.
6. **Fetch the real materials** (one time, needs internet): **RoomGen ▸ Fetch CC0 Materials**.
   Without this you get flat placeholder colours — everything still works.
7. **Run it**: open `Assets/RoomGen/Scenes/RoomStudio.unity` and press **Play**. Load the KA pair,
   walk it (WASD + mouse, Esc exits, Tab switches condition in the seam walk).
8. **Sanity check (optional but appreciated)**: Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All
   — expect **108/108 green** (plus 3/3 in Play Mode). If not, screenshot the failures into Discord.

## Path B — Windows: the built app

Ask Paco for the current `RoomStudio.exe` folder (build output isn't committed). Unzip anywhere,
double-click `RoomStudio.exe` → "Load KA spec pair (adapter)" → "Walk control (desktop)".

*(This is the dev/legacy studio build. If you just want to run a study session with no Unity setup
at all, see Path C — that's the app the team actually tests with.)*

## Path C — the participant app (no Unity, no source — Windows + Mac)

We now ship a released, double-clickable study app for both platforms — this is the real
no-dev-setup path, and it's what the team run uses. Full download links and step-by-step
instructions: **[docs/TEAM_RUN.md](TEAM_RUN.md)**.

Status: Windows is verified rendering. Mac launched and produced a valid full session for a real
tester (M3 Air, macOS 14.6), but that same run measured an unacceptably low frame rate (~5.7 fps) —
a Mac-specific performance tier has since been added and shipped, but **post-fix performance on real
Mac hardware is not yet confirmed**. If you're on a Mac, running it and sending back the `.perf.csv`
is exactly the data we need.

If you want to build the .app yourself instead (e.g. after making a change):
**RoomGen ▸ Build Participant App (Windows)** / **(macOS)** — the macOS one needs the *Mac Build
Support (Mono)* module in Unity Hub. First launch of an unsigned build on macOS: double-click once,
dismiss the "cannot be opened" warning, then **System Settings ▸ Privacy & Security ▸ "Open
Anyway."** (Right-click ▸ Open is unreliable on macOS 14+.)

## Troubleshooting

- **Wrong Unity version** silently upgrades the project — install exactly 6000.3.16f1.
- **Pink/magenta materials**: the HDRP import didn't finish or bootstrap didn't run — run
  **RoomGen ▸ Bootstrap Project**, let the editor finish compiling.
- **Walk mode ignores input** in an old exe: that bug is fixed on the current branches (input
  system setting); re-pull and rebuild.
- **Textures missing** after checkout: run **RoomGen ▸ Fetch CC0 Materials** (they're
  deliberately gitignored; `unity/ASSETS.md` holds the hash locks).
