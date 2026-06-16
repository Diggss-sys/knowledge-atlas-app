# Technical feasibility — pipeline stress-test

*Internal doc. Adversarial check on "can we actually build this end-to-end without hitting a wall mid-project?" Each pipeline stage gets: the known failure mode (cited), our mitigation, and a go/no-go signal to watch during the tracer-bullet milestone. Companion to [RENDERING_RESEARCH.md](RENDERING_RESEARCH.md) and [PHASE2_PLAN.md](PHASE2_PLAN.md).*

## Verdict up front

**Buildable on the web.** No stage is impossible. Two stages carry real risk (realism on mid hardware; build size/memory on iOS) — both have a pre-agreed native-app fallback that reuses the same RoomSpec + C# generation code, so a fallback is a swap, not a rewrite. The riskiest *integration* (JS↔Unity bridge) is proven first, in the tracer bullet, before anything depends on it.

## Stage-by-stage

### 1. Runtime room generation (procedural mesh from RoomSpec) — ✅ LOW risk
- **Failure mode:** non-convex footprints (bowed-in walls) break naive triangulation; bad normals/UVs around curves.
- **Mitigation:** ear-clipping triangulation (handles concave), analytic arc normals, arc-length UVs — all standard, all pure-C# and EditMode-testable without a scene (RENDERING_RESEARCH.md §4). Mesh generation is the predecessor's proven model.
- **Go/no-go:** EditMode tests on the committed ceiling pair pass at M-U1/M-U2.

### 2. Lighting realism — runtime GI is the hard problem — ⚠ MEDIUM risk
- **Failure mode (cited):** Realtime GI/Enlighten is **not supported on WebGL**; baked lightmaps impossible for runtime geometry. Naive direct-only lighting looks flat/fake → fails Kirsh's bar. ([Unity GI docs](https://docs.unity3d.com/Manual/realtime-gi-using-enlighten.html))
- **Mitigation:** the captured-probe approximation (RenderProbe → SphericalHarmonicsL2 → ambientProbe) + SSAO + sky fill + ACES, lifted from the predecessor viewer which already shipped this trick. Runtime APIs verified to exist.
- **Go/no-go:** at M-U3, render the ceiling pair; if it doesn't read as a real room next to a reference photo, iterate materials/lighting; if still failing → native fallback (HDRP-grade lighting). This is the single most important gate.

### 3. Build size & memory — the iOS/mobile cliff — ⚠ MEDIUM risk
- **Failure mode (cited):** Unity WebGL heap can grow to **~2 GB then overflow the 32-bit TypedArray and crash**; assets should stay **under ~50–100 MB**; **iOS browsers impose strict WebGL memory limits** and may fail to load large builds. ([Unity memory blog](https://unity.com/blog/engine-platform/understanding-memory-in-unity-webgl), [Backtrace](https://backtrace.io/blog/memory-and-performance-issues-in-unity-webgl-builds))
- **Mitigation:** aggressive asset budget (channel-packed 2K textures, 1–2K HDRIs, greybox furniture v1); Brotli compression; cap textures; fixed initial heap; **target desktop/laptop browsers first** (participants on a computer, not phones). Treat iOS-Safari as best-effort, not a requirement.
- **Go/no-go:** measure compressed build size + peak heap at M-U5; budget = <100 MB Brotli, <1.5 GB peak heap on a mid laptop. Breach → trim assets or native fallback.

### 4. JS ↔ Unity bridge — the integration risk — ⚠ MEDIUM (proven first)
- **Failure mode:** the web editor's live controls must drive the Unity canvas; a broken/slow bridge makes "real-time editing" a lie. `.jslib` plumbing + SendMessage is fiddly and undocumented-by-example.
- **Mitigation:** the bridge is contract-frozen (VIEWER_BRIDGE.md) and **proven in the tracer-bullet milestone before any other workstream depends on it**; the web UI develops against a mock bridge so it never blocks. Full-spec push debounced (~150 ms) since the builder rebuilds wholesale anyway.
- **Go/no-go:** M-U5 — `ApplySpec` of the ceiling pair visibly changes the room; `unity:specApplied` round-trips; screenshot capture returns a PNG.

### 5. Canvas sharpness (HiDPI) — ✅ LOW risk
- **Failure mode (cited):** WebGL renders blurry on Retina/HiDPI if canvas pixel size ignores devicePixelRatio. ([Unity support](https://support.unity.com/hc/en-us/articles/214948483)) — bad for a *realism* study.
- **Mitigation:** set `devicePixelRatio` in the Unity instance config (cap at 2 for GPU cost); quality toggle for weak machines. One-line fix, known.

### 6. Hosting on Cloudflare (Worker + R2 + Pages) — ✅ LOW risk
- **Failure mode:** wrong `Content-Encoding` headers on `.br`/`.wasm` → load failure or no compression.
- **Mitigation:** exact `_headers` snippet in RENDERING_RESEARCH.md §6; predecessor already ran a Cloudflare Worker + R2 + D1 store (proven pattern to adapt). Decompression-fallback ON during bring-up.
- **Go/no-go:** build loads from Cloudflare Pages over HTTPS with Brotli active (check Network tab `content-encoding: br`).

### 7. Data capture (responses → D1) — ✅ LOW risk
- **Failure mode:** lost data if a tab closes before submit; CORS on cross-origin POST.
- **Mitigation:** POST each row to the Worker as it's produced (not just at end) + localStorage autosave + CSV fallback; Worker sets CORS headers. Schema-validated rows (response_log contract).
- **Go/no-go:** fake-participant session writes rows to D1; rows pass `validate_log`.

### 8. Online timing precision — ✅ LOW (scoped, not solved)
- **Failure mode (cited):** browser RT recording lags ~80–100 ms. ([PeerJ timing mega-study](https://peerj.com/articles/9414.pdf))
- **Mitigation:** scope, don't fight — ratings/choice/accuracy tasks are fine; document the limit; if ms-precision RT is ever required, that's a known boundary stated in PROPOSAL.md §4, not a surprise.

## Risk register (summary)

| Risk | Severity | When known | Fallback |
|---|---|---|---|
| Lighting realism fails Kirsh's bar | High | M-U3 | Native Unity app (HDRP-grade) |
| Build too big / iOS crash | Medium | M-U5 | Asset trim → native app; desktop-first scope |
| Bridge integration breaks live editing | Medium | M-U5 (tracer bullet) | Server-rendered stills instead of live (predecessor pattern) |
| GPU too weak on cheap laptops | Medium | M-U5 | DPR/quality toggle; native app |
| Data loss with remote participants | Low | runner build | Per-trial POST + localStorage + CSV |

**Bottom line:** the plan does not depend on anything that's been shown impossible. The two medium-high risks (lighting, size) are exactly what the tracer-bullet milestone is designed to expose **early and cheaply**, and both fall back onto a native build that reuses 90% of the code (the RoomSpec contract + C# generation). Proceed.

## Sources
- Unity WebGL memory — [Unity blog](https://unity.com/blog/engine-platform/understanding-memory-in-unity-webgl), [Backtrace](https://backtrace.io/blog/memory-and-performance-issues-in-unity-webgl-builds)
- Realtime GI unsupported on WebGL — [Unity Manual](https://docs.unity3d.com/Manual/realtime-gi-using-enlighten.html)
- HiDPI canvas — [Unity Support](https://support.unity.com/hc/en-us/articles/214948483)
- Online timing — [PeerJ 2020](https://peerj.com/articles/9414.pdf)
