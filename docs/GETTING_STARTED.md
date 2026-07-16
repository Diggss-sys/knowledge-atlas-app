# Getting started — run Room Studio on your machine

*For the team. Windows folks with a beefy PC can grab the built exe; everyone on a Mac runs the
project from source in Unity (an M1/M2 MacBook handles it fine — the scene is light). ~30 min the
first time, almost all of it Unity importing.*

> **Which branch?** Until PR #2 merges, `main` does NOT contain the current engine/UI work.
> After cloning, check out **`paco/lighting-l0-l4`** (engine + fixes) or **`paco/ui-foundation`**
> (that plus the new operator UI). After the merge you'll just use the integration branch.

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
   — expect **92/92 green**. If not, screenshot the failures into Discord.

## Path B — Windows: the built app

Ask Paco for the current `RoomStudio.exe` folder (build output isn't committed). Unzip anywhere,
double-click `RoomStudio.exe` → "Load KA spec pair (adapter)" → "Walk control (desktop)".

## Path C — Mac .app (waiting on one test)

The macOS build target exists (**RoomGen ▸ Build macOS Application**) but no one has produced and
smoke-tested a .app on real hardware yet. First volunteer with an M2: run that menu item on your
Mac (or ask Paco after he adds Mac Build Support to his editor), then launch via right-click ▸ Open
(unsigned app). Report back and Path C becomes the normal route for non-dev Macs.

## Troubleshooting

- **Wrong Unity version** silently upgrades the project — install exactly 6000.3.16f1.
- **Pink/magenta materials**: the HDRP import didn't finish or bootstrap didn't run — run
  **RoomGen ▸ Bootstrap Project**, let the editor finish compiling.
- **Walk mode ignores input** in an old exe: that bug is fixed on the current branches (input
  system setting); re-pull and rebuild.
- **Textures missing** after checkout: run **RoomGen ▸ Fetch CC0 Materials** (they're
  deliberately gitignored; `unity/ASSETS.md` holds the hash locks).
