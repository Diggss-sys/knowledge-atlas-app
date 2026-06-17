# Meeting notes — Prof. Kirsh, June 2026

*Distilled from the raw transcript ([kirsh_meeting_transcript.pdf](kirsh_meeting_transcript.pdf)). These are the requirements that shaped the restart.*

## The use case (who this is for)

- Undergrads in the class design an experiment and get **two weeks to build the stimuli** — a room in which a task can be performed.
- Everything starts from a **control condition vs. treatment condition** design. The system must make that easy and keep it honest.
- Kirsh will curate **~25–50 candidate experiment ideas**; students pick one and motivate it. Replications with changed measures/tests are acceptable — his system flags findings that haven't been replicated enough.

## The manipulations (ranked by difficulty, his words)

1. **Ceiling height** — "easiest thing of all." (e.g. ~8 ft concentration vs >10 ft divergent/creative thinking.)
2. **Windows / natural lighting** — alter them in some way.
3. **Wall texture** — easy.
4. **Curved wall** — "not so easy." (The contour manipulation → see CURVED_WALLS_SUBPLAN.)

## The experiment types (behavioral, not physics-heavy)

- **Concentration tasks in the room** — proofreading, multiplying; performance compared across room variants.
- **Memory tests** — do the task in the room, test afterward *outside* the room ("if it's a memory test, I don't need the room anymore").
- **Navigation / pointing** — walk through a space, return to start, point to where a room was; the angular error is the measure. The most "physics-like" thing anyone would need.
- **Adaptive preference** — A-vs-B paired comparisons; ~20 stimuli smartly compared in ~8 questions.
- General pattern: **time-in-room doing something** (find something, look at something), then tests afterward.

## The requirements that matter

- **Realism bar:** the room must be "realistic enough that it gives you a reliable experience of being in the room." Paper-model / low-poly GitHub-Pages-VR fidelity is explicitly not enough.
- **Engine-agnostic:** "If you can do that in Unity, I have no special care — as long as the front end is such that we can do it." What he cares about is **how easy it is for a student (not a Unity user) to say 'make the ceiling high.'** Parametric models are the way.
- **One manipulation at a time:** the temple demo was criticized precisely for too many simultaneous changes ("an experiment has got to be controlled"). Single-variable isolation is the core discipline.
- **Distribution reality:** designing in Unity requires running Unity (heavy, bad on cheap Macs); *running* a built experiment is lighter. Making vs. rendering vs. running are distinct concerns — keep them separated.

## Why Infinigen was retired

- Doors/windows came out "terrible"; getting parameters out required tearing Infinigen apart, which nobody managed.
- Server-hosted assets had to be compressed hard (~0.5 GB/scene) → texture and lighting artifacts.
- The pivot logic: by the time a model reaches Blender it's just a 3D model — lighting can be applied in any engine. The generator isn't sacred.

## Summer plan (skunkworks)

- Multiple competing teams on the same problem, not coordinating: a Unity team, an Infinigen-salvage team, possibly a third using **Max Planck Institute VR rooms** (an architect/neuroscientist contact of Kirsh's with "gorgeous" hospital-room stimuli — he'll ask how they were made and whether they're modifiable).
- Students can join his lab as research assistants — 199 course credit in the fall (free, doesn't require summer enrollment).
- Floated idea: host parametric room variants and teleport participants between them (the VRChat pattern) — Kirsh: running the experiment "could be done many ways"; the hard part is **making the room**.

## Implications for this repo

1. The **front end is the product** — a student must manipulate one variable without touching an engine. That's the RoomSpec + **Unity slider UI** path (current plan; the old web-form idea is dropped).
2. The **validator is the science** — enforce the single-variable rule mechanically, since it's the thing he keeps correcting people on.
3. **Realism non-negotiable, delivered in 3D** — the fidelity floor rules out toy renderers; rooms are Unity-generated 3D (interactive web-3D + native VR), no flat 2D. Budget effort accordingly.

*(Note: items 1–4 are the notes-writer's implications, updated 2026-06-11 to the current Unity/VR/3D plan. Kirsh's actual statements above are unchanged. Current architecture: [docs/PHASE2_PLAN.md](../PHASE2_PLAN.md).)*
4. **Task + data collection are in scope** — students need to run a task *in* the room and capture responses; the platform owns that half too.
