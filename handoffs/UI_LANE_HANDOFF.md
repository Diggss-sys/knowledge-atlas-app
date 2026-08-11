# UI lane handoff — Paco → Michael

*Written 2026-07-27, at the point Paco hands the UI lane over. Everything here was verified against
`origin/main` at the time of writing; where the repo has drifted from what the docs claim, that's
called out rather than smoothed over.*

---

## 1. "UI" here is three products, not one

The most important thing to internalise first: this project has **three UI surfaces with three
different rulebooks**. Conflating them is the main way this lane goes wrong.

| Surface | Location | Audience | Design rule |
|---|---|---|---|
| **Operator studio** (Unity) | [`unity/Assets/RoomGen/UI/Operator/`](../unity/Assets/RoomGen/UI/Operator/) | the researcher authoring a study | Dense, efficient, branded. Designer's judgment governs. |
| **Participant runner** (Unity) | [`unity/Assets/RoomGen/UI/Runner/`](../unity/Assets/RoomGen/UI/Runner/) | the study subject | **Neutral by scientific requirement.** Grayscale, system font, no personality. |
| **Team hub** (web) | [`team_hub/`](../team_hub/) | the team | Warm, branded, animated. |

### The participant rule is the one you must never break

[`UI/Runner/runner.uss`](../unity/Assets/RoomGen/UI/Runner/runner.uss) is deliberately grayscale,
system-font, and brand-free. The participant also never sees a condition label ("control"/
"treatment") or an on-screen frame-rate counter.

That is **not** minimalism as taste. A styled or informative participant screen is a **stimulus
confound** — it changes what the subject experiences, which is the very thing the study measures. A
visible condition label is a demand cue. This rule is written into
[`handoffs/COORDINATION.md`](COORDINATION.md) as a scientific requirement, and there is a comment
saying so at the top of the stylesheet. If someone asks you to "make the participant screens look
nicer", the correct answer is no.

---

## 2. Team hub — design philosophy

The palette and component language came from **Antigravity ("Anti")**, a Google AI agent that
briefly owned this folder, derived from the lab's own site. The canonical reference is
[`team_hub/DESIGN_SYSTEM.md`](../team_hub/DESIGN_SYSTEM.md) — read that first. In summary:

- **Warm and academic, never harsh.** Cream `#f4f0e6` background, white cards, deep-forest `#2c3e38`
  text, sand `#e2ddd0` borders. No pure white, no pure black — that's what keeps it reading as
  "atlas" rather than "SaaS dashboard".
- **Burnt orange (`#d35400` / `#b35900`) is *the* action colour** — links and CTAs only. Don't spend
  it on decoration; it stops meaning "click me" the moment it's everywhere.
- **Serif headings (Georgia), system-sans body.** The serif does most of the academic character.
- **Callouts are the core component.** `.callout-teal` = good/done, `.callout-amber` =
  attention/blocked, `.callout-blue` = info. Left accent bar, 8px radius on the non-accent side.
- **8px radius on cards, 4px on small elements.** Max width 1200px; 250px sidebar + flexible main.
- **GSAP animation, with one hard rule — the "fail-safe cascade":** *never* hide elements with CSS
  `opacity: 0`. Let the HTML render fully visible by default, then animate from JS. Anti shipped a
  blank-white-page bug exactly once by violating this. There is also a `prefers-reduced-motion`
  guard that must survive future edits.

The same language was later ported into the **Unity operator panel**
([`UI/Shared/base.uss`](../unity/Assets/RoomGen/UI/Shared/base.uss)) so the two surfaces feel
related: same cream/forest/orange tokens, same 8px cards, same callout-style validation chips.

---

## 3. Three things that need attention now

### (a) The live hub lost the Team Run downloads

PR #5 (the Windows pivot) rewrote [`team_hub/index.html`](../team_hub/index.html) from ~500 lines to
199 and replaced the section structure. The live site now has `plan / acceptance / roles / delivery`
and **no Team Run section and no download buttons** — verified against the deployed page, not just
the source.

That matters because the whole team was asked to download the study app *from that page*. The links
still exist in [`docs/TEAM_RUN.md`](../docs/TEAM_RUN.md) and on the GitHub Release, so nothing is
lost — but anyone following the hub link right now finds no app. **Probably the first thing to fix.**

### (b) `status.json` and the page have drifted apart

The hub's own contract (the `_contract` field at the top of
[`team_hub/data/status.json`](../team_hub/data/status.json)) says that file is the *only* content
source and the page merely renders it. That discipline broke during the pivot: `status.json` still
contains a full `team_run` object with both download URLs and a `mac_status` field, but the current
page renders none of it.

Either restore the rendering or formally retire the contract — but don't leave it half-true. The
entire point of the contract was to stop the page drifting from reality, and a stale contract is
worse than none because people trust it.

### (c) GitHub Pages is configured unusually

- The site is served at the **root**: <https://diggss-sys.github.io/knowledge-atlas-app/>.
  `/team_hub/` now **404s**, so older links to that path are dead.
- There is a root `index.html` redirect (added when Pages served the repo root) that may now be
  redundant or actively conflicting — worth a look.
- **Deploy is manual only** — `workflow_dispatch` on
  [`.github/workflows/deploy-team-hub.yml`](../.github/workflows/deploy-team-hub.yml). That was
  deliberate: publishing the hub puts team names and plans on the public internet. The repo is now
  public so that horse has partly left, but keep deploys intentional.
- The `github-pages` **environment has a branch allowlist** (`main` + `paco/ui-foundation`).
  Dispatching from any other branch fails with **zero steps run and no error message** — it looks
  like a broken workflow but it's a permissions gate. Enabling Pages the first time also needs repo
  **admin** (Diego), not write access.

---

## 4. Unity operator UI — architecture to respect

The pattern is **thin views, testable view-models**, and it is load-bearing for the test suite.

| File | Role |
|---|---|
| [`OperatorPanelViewModel.cs`](../unity/Assets/RoomGen/UI/Operator/OperatorPanelViewModel.cs) | All the logic. Public surface is **plain types only** — the EditMode test assembly *cannot* reference Newtonsoft, so JSON stays private inside. Debounces slider input ~150 ms into one engine apply. |
| [`OperatorPanelController.cs`](../unity/Assets/RoomGen/UI/Operator/OperatorPanelController.cs) | A *dumb binder*. `Bind(root, vm)` wires sliders by matching `name == dotted spec path`. Makes no decisions. |
| [`OperatorPanel.uxml`](../unity/Assets/RoomGen/UI/Operator/OperatorPanel.uxml) | Layout. The left rail is a `ScrollView` and `.ka-panel` has `flex-shrink: 0` — both deliberate; without them UI Toolkit compresses panels into each other and text paints through buttons. |
| [`OperatorStudio.cs`](../unity/Assets/RoomGen/UI/Operator/OperatorStudio.cs) | Composition root: panel + real engine + two live preview renderers + walk mode. |

### Gotchas that will each cost you a day if rediscovered

1. **Screen-space `PanelSettings` must have `clearColor = false`.** If true, the panel clears the
   whole backbuffer every frame — even when hidden — and wipes the 3D walk view. Guarded by
   `ScenePanelSettingsTests`.
2. **A `UIDocument` needs its `PanelSettings` reference actually serialised.** A batchmode-generated
   scene once saved it as `null`; the panel bound to a detached root and rendered *nothing* — with
   every test green. Also guarded by a test.
3. **UI Toolkit does not paint in headless EditMode.** Screenshots require a *PlayMode* test that
   yields frames; that's why `Tests/PlayMode/` exists.
4. **Verify on a real display.** Both bugs above passed the entire automated suite. Use the
   **`RoomGen ▸ Live Smoke`** menu — it opens either scene and enters Play in one click. Treat this
   as mandatory for anything scene-level.

---

## 5. Where things live

| Thing | Path |
|---|---|
| Hub design system | [`team_hub/DESIGN_SYSTEM.md`](../team_hub/DESIGN_SYSTEM.md) |
| Hub page / styles / animation | [`team_hub/index.html`](../team_hub/index.html), [`css/style.css`](../team_hub/css/style.css), [`js/app.js`](../team_hub/js/app.js) |
| Hub data + narrative log | [`team_hub/data/status.json`](../team_hub/data/status.json), [`data/CHANGELOG.md`](../team_hub/data/CHANGELOG.md) |
| Unity shared styles (operator) | [`UI/Shared/base.uss`](../unity/Assets/RoomGen/UI/Shared/base.uss) |
| Operator UI | [`UI/Operator/`](../unity/Assets/RoomGen/UI/Operator/) |
| Participant UI (neutral) | [`UI/Runner/`](../unity/Assets/RoomGen/UI/Runner/) |
| Team-run instructions | [`docs/TEAM_RUN.md`](../docs/TEAM_RUN.md) |
| Performance targets | [`docs/PERFORMANCE.md`](../docs/PERFORMANCE.md) |
| UI ownership + contract rules | [`handoffs/COORDINATION.md`](COORDINATION.md) |
| Anti's old contract (historical, retired) | [`team_hub/ANTI_README.md`](../team_hub/ANTI_README.md), [`REQUESTS.md`](../team_hub/REQUESTS.md) |

**Retired workflow, for context:** the hub used to be maintained by Anti under a "sync, don't author"
contract — Claude wrote `status.json`, Anti rendered it, and `REQUESTS.md` was the request queue
between them. That arrangement ended on 2026-07-22; those files are kept as a historical record, not
as live process. Don't file tickets into `REQUESTS.md` expecting anyone to pick them up.

---

## 6. Open items being inherited

1. **Restore Team Run downloads to the hub** (§3a) — highest priority; people are actively blocked.
2. **Reconcile `status.json` ↔ page, or retire the contract** (§3b).
3. **Post-fix Mac performance is still unverified.** A tester's M3 Air ran at **5.7 fps**; a Mac
   quality tier shipped in response, but nobody has re-measured on real hardware. Target is ≥45 fps
   average / ≥30 fps minimum / control-vs-treatment within 10%. This is the one outstanding external
   acceptance item — see [`docs/PERFORMANCE.md`](../docs/PERFORMANCE.md).
4. **PR #6 is open** (`paco/pair-contract-parity`) — a strict single-variable coverage fix for the
   runtime gate, awaiting Diego's review.
5. **Furniture placement UI** is the next significant build, blocked on Diego's engine interface. The
   view-model layer can be started now against `MockSpecChannel`; selection and free X/Z placement
   must wait on him.

---

## 7. Scope — confirmed: both surfaces

**Settled 2026-07-27: Michael owns both the team hub (web) and the Unity operator UI.** That means
the upcoming furniture placement UI lands in this lane too, once Diego confirms the engine interface.

The two halves are genuinely different work — HTML/CSS/GSAP on one side, Unity UI Toolkit plus C#
view-models with a test suite on the other — so §8 is an onramp for the Unity half specifically.

---

## 8. Getting productive (the Unity half)

### Run the project

1. Open [`unity/`](../unity/) in Unity **exactly 6000.3.16f1** (a different version silently upgrades
   the project). First import takes 10–20 minutes, once.
2. Run **`RoomGen ▸ Fetch CC0 Materials`** once, with internet. Without it you get flat placeholder
   colours — everything still works, and one test self-skips.
3. Full setup detail, including the no-Unity paths: [`docs/GETTING_STARTED.md`](../docs/GETTING_STARTED.md).

### See the UI actually run

**`RoomGen ▸ Live Smoke ▸ Play Operator Studio`** (or **`Play Participant Runner`**) — opens the
scene and enters Play in one click. Use this constantly; per §4 it's the only thing that catches the
render bugs the test suite can't see.

### Run the tests

In-editor: **Window ▸ General ▸ Test Runner** → EditMode → Run All, then PlayMode → Run All. Expect
all green, with at most one skip (`AssetPipelineTests` self-skips when CC0 textures aren't fetched).

Headless, for a clean check or CI:

```
Unity.exe -runTests -batchmode -projectPath unity -testPlatform EditMode -testResults results.xml -logFile run.log
```

On Windows, PowerShell does **not** reliably wait on `Unity.exe` — use
`Start-Process -Wait -PassThru` and parse the `-testResults` XML. Never trust `$LASTEXITCODE` alone.

### What's yours vs what's Diego's

| Yours | Diego's (PR review required to touch) |
|---|---|
| [`UI/Operator/`](../unity/Assets/RoomGen/UI/Operator/), [`UI/Runner/`](../unity/Assets/RoomGen/UI/Runner/), [`UI/Shared/`](../unity/Assets/RoomGen/UI/Shared/) | `Runtime/Generation/`, `Runtime/Adapter/`, `Runtime/Validation/`, `Runtime/Gate/` |
| [`team_hub/`](../team_hub/), [`docs/TEAM_RUN.md`](../docs/TEAM_RUN.md) | `Runtime/Studio/RoomStudioController.cs` (the legacy IMGUI studio) |

Cross-surface edits go through the other lane's owner as reviewer — same spirit as the
contract-change rule in [`COORDINATION.md`](COORDINATION.md). Anything under `spec/` (schemas,
fixtures, presets) is a **contract change** with its own heavier ritual; don't edit those casually.

### Branch discipline

Feature branches (`michael/<thing>`), PR into `main`, **never push to `main` directly**. Merges are
reviewed by the other code contributor.

### Suggested first change

Restore the Team Run downloads to the hub (§3a). It's small, immediately visible to the whole team,
unblocks people who are currently hitting a dead end — and it walks you straight through the
`status.json` ↔ page relationship in §3b, which is the thing most worth understanding early.
