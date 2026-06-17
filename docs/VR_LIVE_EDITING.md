# VR live-editing — the end goal: operator edits the room around an immersed subject

*The north-star use case (stated by Diego 2026-06-11): a **subject wears a VR headset inside the room**; a **second student uses a live UI (sliders, fun controls) to change the environment around them in real time**; everything stays **parametric**. This doc answers: can we do it, what breaks, what the fallbacks are. Researched 2026-06-11, cited. Companion to [RENDERING_RESEARCH.md](RENDERING_RESEARCH.md), [TECH_FEASIBILITY.md](TECH_FEASIBILITY.md).*

## Verdict

**Yes — buildable, and it reuses everything we've designed.** Three sub-problems, all solved tech:
1. **VR rendering** — Unity does this natively (OpenXR) and even in-browser (WebXR).
2. **Operator → subject live sync** — standard Unity multiplayer (Netcode / Photon); RoomSpec JSON is tiny, trivial to push.
3. **Parametric room** — already the entire architecture; the operator UI binds sliders to RoomSpec fields.

The catch is **not "can we"** — it's **comfort** (geometry changing around an immersed person) and **fidelity-vs-reach** (native headset app vs send-a-link WebXR). Both have clean fallbacks.

## BEST ARCHITECTURE (locked 2026-06-11) — all-Unity, editor in Unity

Decision: **the editor is Unity (not HTML). Rooms generated + rendered in Unity. The operator drives a Unity UI; the subject is in VR. Everything parametric.** The cleanest realization depends on one question — **same machine or two?**

### Best case — SINGLE Unity app, NO networking (PCVR / tethered headset)
If the VR headset is **tethered to the operator's PC** (PCVR: Quest Link, Valve Index, etc.), this is the simplest and highest-fidelity option:

```
                 ONE Unity app (URP + OpenXR), one running process, ONE strong PC
 ┌──────────────────────────────────────────────────────────────────────────┐
 │  OPERATOR view (PC monitor)            SUBJECT view (tethered headset)      │
 │  Unity UI Toolkit panel: sliders  ─┐   stereo VR camera, 90fps             │
 │  bound to RoomSpec fields          │                                        │
 │                                    ▼                                        │
 │           RoomSpec (in memory) ──► RoomBuilder.BuildFromSpec()              │
 │                                    rebuilds the ONE shared scene            │
 │           operator slider ──► instant rebuild ──► BOTH views update         │
 └──────────────────────────────────────────────────────────────────────────┘
   No network. No latency. No second build. Full PC-GPU fidelity. Shared scene = both see same room.
```
- Operator edits via a Unity **UI Toolkit** slider panel on the monitor; subject wears the tethered headset; **same process** → edits apply instantly to the shared scene both render.
- **Zero networking, zero latency, one build, full desktop-GPU quality** (the "good rooms from the source" requirement). 120fps even possible on PCVR.
- This is the recommended **best-case** target for the lab setup ("another person on the PC doing live changes" + subject in headset on that PC).

### Fallback — TWO Unity clients, networked (standalone Quest / two machines)
If the headset is **standalone** (untethered Quest) or operator + subject are on **different machines**, add Unity-native sync:

```
 OPERATOR (Unity desktop app, PC)       SUBJECT (Unity VR app, standalone Quest)
 UI Toolkit sliders → RoomSpec  ──NGO/Photon RPC (RoomSpec JSON, tiny)──►  BuildFromSpec()
```
- Unity **Netcode for GameObjects** or **Photon** carries the RoomSpec (a few KB) operator→subject. Same engine both ends = clean.
- Cost: a second build + campus-network NAT/firewall handling. Lower fidelity (standalone GPU).

### Support BOTH modes via one transport seam (locked 2026-06-11 — Diego wants both)

Diego wants **both** PCVR and standalone available (headsets may come later). Design for it now with a single abstraction so the second mode is additive, not a rewrite:

```
operator slider ─► RoomSpec ─► ISpecChannel.Apply(spec) ─► RoomBuilder.BuildFromSpec(spec)
                                   │
                   ┌───────────────┴───────────────┐
                   ▼                                ▼
        LocalChannel (PCVR)              NetworkChannel (standalone / 2-machine)
        same-process direct call         Netcode/Photon RPC, RoomSpec on the wire
        no network, no latency           untethered, lower fidelity
```

- **`ISpecChannel`** = one interface, `Apply(RoomSpec)`. Two implementations: `LocalChannel` (in-process, PCVR) and `NetworkChannel` (Netcode/Photon, standalone). Operator UI + `RoomBuilder` never know which is active. Same RoomSpec contract both ways.
- **One headset covers both:** Quest 3 + Link cable = **PCVR** when plugged into the strong Windows PC, **standalone** when unplugged. Buy once, both modes.
- **Build order:** Phase A = single-app **PCVR** (Windows build, `LocalChannel`) — best fidelity, simplest, proves the live-edit loop + realism with zero network risk. Phase B = add `NetworkChannel` + Android **standalone** subject build — drop-in behind the same interface; unlocks untethered + two-machine. Core (RoomSpec + RoomBuilder + UI) unchanged.

**Recommendation:** ship PCVR first, but **architect the `ISpecChannel` seam from day one** so standalone is a later add, not a redesign. Hardware: Quest 3 + Link to the existing strong Windows PC.

## Architecture — the RoomSpec contract carries over unchanged

```
 OPERATOR (student 2)              SYNC LAYER                 SUBJECT (student 1, in VR)
 ┌────────────────────┐      ┌──────────────────┐       ┌───────────────────────────┐
 │ Unity DESKTOP app  │      │ Netcode / Photon  │       │ Unity VR client (OpenXR)  │
 │ sliders/toggles    │──▶   │ or WebSocket relay │──▶    │ receives RoomSpec ->       │
 │ NOT in VR/room     │ JSON │ (RoomSpec is tiny) │ RPC   │ BuildFromSpec() rebuilds  │
 │ edits RoomSpec     │      └──────────────────┘       │ headset re-renders LOCALLY │
 └────────────────────┘                                  └───────────────────────────┘
   same ApplySpec(specJson) message as the single-browser bridge — now over the network
```

**Operator (clarified 2026-06-11):** sits at a **computer with a Unity desktop app** (sliders/toggles) — NOT in VR, NOT in the room. Subject in the headset witnesses changes live. Two Unity clients, one authority (operator) + one VR (subject), like a 2-player app. RoomSpec is the wire format.

**Key insight:** the JS↔Unity bridge (VIEWER_BRIDGE.md) **generalizes to a network bridge.** Same `ApplySpec(json)` contract — in the editor it's one browser talking to one canvas; in VR it's the operator's machine talking to the headset over Netcode/Photon. One contract, two transports. The whole platform was built around this.

## Sub-problem 1 — VR rendering: native vs WebXR

| Path | What | Pros | Cons |
|---|---|---|---|
| **Native Unity (OpenXR)** ← recommended for the end goal | Built app on Quest (standalone) or PC-VR | Best fidelity, full URP, no WebGL heap ceiling, robust | Per-headset deploy (Quest .apk / PCVR), not send-a-link |
| **WebXR** (De-Panther export) | Same WebGL build, enter VR from Quest Browser | Send-a-link, no install | Shares WebGL limits (~2GB heap, 50–100MB build); mobile/standalone perf weak; lower fidelity |

**Finding (cited):** Unity WebXR Export is stable Unity 2022–6, works on Quest 2/3 + Pico 4 browsers and tethered PC-VR ([De-Panther WebXR Export](https://de-panther.github.io/unity-webxr-export/)) — but it inherits all WebGL limits we already flagged. **For Kirsh's realism bar in VR, native wins.** This reinforces the existing native-fallback plan: **VR = the native arm; WebGL = the authoring + 2D-participant arm.** Same RoomSpec + same C# generation code feed both (decision #2 already reserved this).

## Sub-problem 2 — operator → subject live sync: solved

**Finding (cited):** Unity **Netcode for GameObjects** (RPCs, network variables, scene sync) and **Photon PUN** are the standard, mature ways to sync a multiplayer VR scene ([Unity Netcode](https://unity.com/features/netcode), [VR networking tools](https://livinginvr.com/unity-vr-multiplayer-networking-tools/)). Our payload is just a **RoomSpec JSON** (a few KB) sent operator→subject on each edit — trivially small. Operator = a non-VR "authority" client; subject = the VR client that rebuilds on receive. Latency on LAN/WebSocket is tens of ms — fine; edits aren't frame-critical.

**Simplest viable version:** skip heavy netcode — a **WebSocket relay** (even the Cloudflare Worker pattern we already have) carries RoomSpec from the operator's web UI to the headset client. Upgrade to Photon/Netcode only if we need avatars/voice/multi-user later.

## Sub-problem 3 — parametric: already the core

The whole platform is RoomSpec-parametric. Operator UI = sliders/toggles bound to RoomSpec fields (ceiling height, curviness, wall_bow, lighting natural/artificial/bounce, furniture). "Fun, easy" UI = the WEBUI_WORKSTREAM editor, repointed from the local Unity canvas to the networked VR client. **Nothing new in the contract.**

## The real risk — comfort: changing geometry around an immersed person

**Finding (cited):** altering the virtual world's stable physics causes sensory cue-conflict → cybersickness ([cybersickness playbook](https://medium.com/antaeus-ar/beating-cybersickness-the-complete-vr-ar-comfort-playbook-2025-59ea4e083b9f), [dynamic FOV mitigation, arXiv 2025](https://arxiv.org/pdf/2502.03419)). Walls/ceiling morphing *while the subject watches* is the danger case.

**Severity by edit type:**
- **Low risk** — lighting, color/material, furniture swaps, warmth/bounce. No self-motion cue. Edit live, freely.
- **Medium/high risk** — ceiling height, wall bow, curviness, room size. Geometry moves around the user.

**Mitigations (ranked):**
1. **Teleport-between-variants (Kirsh's own VRChat idea, and the safest).** Don't morph — pre-build control + treatment, blink/teleport the subject between them. Zero morph = zero motion sickness, AND it's a cleaner experiment (discrete conditions, not a slider the subject watches move). **Recommended default for geometry changes.**
2. **Change behind a fade/blink** — fade to black ~0.2s, rebuild, fade in. Subject never sees the morph. Good for operator-driven live geometry edits.
3. **Change while out of view / user stationary, FOV-limit during change** — reduce peripheral optic flow ([FOV restriction](https://arxiv.org/pdf/2502.03419)).
4. **Keep the subject still** — no locomotion during edits; subject stands/sits, operator changes the world. (Matches the use case.)

**Design rule:** live-morph the low-risk params freely; for geometry, default to **fade-swap or teleport-between-variants**. The operator UI shows a "comfort: live vs fade" toggle per edit.

## Other gotchas + workarounds

- **VR framerate budget (72–90 Hz).** A full room teardown+rebuild per slider drag will hitch in VR. Workaround: **debounce** edits; rebuild **async**; for continuous params (ceiling slider) **move/scale existing meshes** instead of full rebuild where possible. Full `BuildFromSpec` only on discrete changes.
- **Build size / heap (WebXR only).** The ~2GB heap + 50–100MB budget bites WebXR, not native. Another reason VR = native.
- **Two-machine setup.** Operator laptop + subject headset on same network = a 2-client session. Cloudflare relay handles them off-LAN too.
- **Determinism still holds.** Operator edits produce RoomSpecs; log every spec + timestamp = full reproducible record of what the subject saw when. Feeds the response log.

## What this means for the roadmap

- **VR is the north star, not an optional Phase 5.** Re-frame: the platform targets BOTH an **interactive web-3D arm** (authoring + cognitive-task participants — real-time 3D rooms, **never flat 2D/images**) AND a native VR arm (the live-edit immersive experience). The RoomSpec contract + C# generation are shared; only the transport (in-browser bridge vs network) and the renderer target (WebGL vs OpenXR) differ. **Both arms render real 3D — the project is 3D-only.**
- **Earliest VR proof (after the web tracer bullet):** native Unity OpenXR build on a Quest, RoomSpec pushed from a laptop web UI over WebSocket, operator drags the ceiling slider → subject sees it fade-swap. Small, proves the network bridge + comfort pattern.
- **No contract changes needed** — VR rides the same RoomSpec, same ApplySpec message, same validator.

## Compute topology — where the work actually runs (SDSC vs Cloudflare vs local)

Diego's model: heavy compute on the **UCSD supercomputer (SDSC)** → rooms to **Cloudflare** → local machine pulls + edits live. **One hard correction, grounded in physics:**

**Head-tracked VR rendering MUST be local. It cannot run on a supercomputer.** VR demands **motion-to-photon latency <20 ms** ([MTP requirement](https://thespatialstudio.de/en/xr-glossary/motion-to-photon-latency)). SDSC Expanse is a **batch HPC cluster** (Slurm jobs, shared GPU nodes, allocation hours — [Expanse guide](https://www.sdsc.edu/systems/expanse/user_guide.html)) — minutes-to-queue, internet round-trip away. It physically cannot deliver 90fps stereo head-tracked frames inside 20 ms. So the supercomputer is **not** in the live render loop, ever.

**Correct division of labor:**

| Layer | Job | Runs where |
|---|---|---|
| **Live VR render** | per-frame stereo, head tracking, 90fps | **LOCAL** on the headset/PCVR GPU. Non-negotiable. |
| **Live operator edits** | slider → RoomSpec → cheap local apply (move geo, change light/material) on the already-loaded room | **LOCAL** (operator app + VR client + light relay) |
| **Offline heavy bakes** | high-fidelity GI bakes, high-res lightmaps, batch 3D variant libraries (no 2D/panorama output — project is 3D-only) | **SDSC** (batch) — optional accelerator |
| **Room library / asset store** | saved RoomSpecs + SDSC-baked assets, served to clients | **Cloudflare** (R2 + Worker), the pattern we already have |

**Flow:** SDSC pre-bakes a high-fidelity variant library (offline, batch) → pushes assets to Cloudflare → local clients pull a room → operator edits it **live and locally** → cheap params apply instantly; saved variants go back to Cloudflare.

**What this means for the "no SDSC access" fallback:** local computing is **not** a slower version of the live experience — it **is** the live experience. SDSC only accelerates **offline** work: faster/higher-fidelity pre-baking of the 3D variant library. So:
- **Live VR editing does NOT depend on SDSC.** De-risks the whole end goal — we are not blocked waiting on supercomputer access.
- **SDSC = quality + scale booster** for the offline asset farm. Without it: bake locally, slower to build the *library*, but the live experience is identical.
- The runtime light-probe approximation (RENDERING_RESEARCH.md §3) is exactly what lets live editing skip the re-bake — that's why we don't need SDSC in the loop.

**One real remote-render option (noted, not our path):** cloud VR streaming (NVIDIA CloudXR) CAN render on a remote GPU and stream to a headset — but needs a **low-latency edge GPU + strong network**, not a batch supercomputer ([CloudXR](https://developer.nvidia.com/blog/stream-high-fidelity-spatial-computing-content-to-any-device-with-nvidia-cloudxr-6-0/)). Different beast from SDSC; adds latency risk. Skip for v1.

## Performance budget — sustaining framerate in VR

Target **90 fps native** (8.3 ms/eye, rendered ×2). 72 = floor. **120 = PCVR-only bonus, not a standalone requirement** — chasing 120 on Quest standalone sacrifices the realism Kirsh demands.

**Our scene favors us:** the room is **static between operator edits** — expensive lighting (bounce probe, GI) is captured **once per edit**, not per frame. Steady-state = cheap static room. Cost is an **edit-time spike** (rebuild + reprobe), hidden by debounce + async + fade-swap.

**Levers (biggest first):**
1. **Single-pass / multiview stereo** — both eyes one pass. Mandatory. ~2×.
2. **Fixed Foveated Rendering (FFR)** — Quest hardware, low-res periphery.
3. **Application SpaceWarp (AppSW)** — synth frames, render ~60 → present ~120. The only realistic path to 120-ish on standalone.
4. **Static between edits** — bake to probe once, no per-frame GI. (Already our design.)
5. **One realtime directional light + capped shadow casters**; rest probe/baked.
6. **Forward+ renderer** (never deferred on mobile VR); **MSAA** (cheap on Quest tile GPU).
7. **GPU instancing + static batching** → few draw calls (our walls are simple).
8. **Low poly + ASTC 2K textures + mips**; greybox furniture v1 helps.
9. **Debounce rebuilds** — the edit spike must not stall steady-state.

**Verdict:** build to **90fps native, FFR + single-pass + AppSW on**; 120 = PCVR bonus.

## Sources
- Unity WebXR Export status — [De-Panther](https://de-panther.github.io/unity-webxr-export/), [GitHub](https://github.com/De-Panther/unity-webxr-export)
- SDSC Expanse (batch HPC) — [user guide](https://www.sdsc.edu/systems/expanse/user_guide.html) · VR motion-to-photon <20ms — [MTP](https://thespatialstudio.de/en/xr-glossary/motion-to-photon-latency) · cloud VR streaming — [CloudXR](https://developer.nvidia.com/blog/stream-high-fidelity-spatial-computing-content-to-any-device-with-nvidia-cloudxr-6-0/)
- VR networking — [Unity Netcode](https://unity.com/features/netcode), [VR multiplayer tools](https://livinginvr.com/unity-vr-multiplayer-networking-tools/)
- Cybersickness in dynamic environments — [comfort playbook](https://medium.com/antaeus-ar/beating-cybersickness-the-complete-vr-ar-comfort-playbook-2025-59ea4e083b9f), [dynamic FOV/optic-flow mitigation, arXiv 2025](https://arxiv.org/pdf/2502.03419)
