# Render path: ray tracing is welcome — as a tier, not the default

*Draft for Paco to send Diego (you already flagged the Mac point in chat — this is the technical
backing). Not "don't do ray tracing" — it's a great demo-PC wow. Two constraints on how it lands.*

## TL;DR

Ray tracing sounds awesome for the lab demo PC. Two hard constraints so it doesn't break the study:

1. **It can't be the default / only path — the participant Macs can't run it.**
2. **Data collection stays on ONE render path — and that path is the calibrated raster one.**

So: RT as an **optional quality tier**, auto-detected, with the current raster path fully intact and
still the path we collect data on.

## 1. HDRP ray tracing is DXR-only — no Apple-Silicon path

Unity HDRP's ray tracing is built on **DXR (DirectX Raytracing)**: it requires Windows + DX12 + an
RTX-class GPU. There is **no Metal / Apple-Silicon implementation** — an M2 MacBook literally cannot
run HDRP RT (the feature reports unsupported and effects are skipped or the pipeline errors).

Our participant machines are M2 Macs (that's the whole point of the perf/team-run work). So if RT
ships as anything other than an optional tier, the participant arm dies on the machines it's meant to
run on. Concretely: gate it behind a capability check (`SystemInfo.supportsRayTracing`) and keep the
raster path as the default everything falls back to.

## 2. One render path per study — and RT would un-calibrate the pair

Subtler and more important for the science. Our L3 result — the matched-luminance calibration where
the 2.4 m and 3.2 m ceilings read within 4.7% at eye height — was **measured on the raster path**. Ray
tracing changes where light actually goes (real bounce, real reflections), which can silently break
that match: the taller room might now read brighter, and brightness riding along with the
manipulation is exactly the confound the whole instrument exists to prevent.

So the rule: **all data-collection sessions run the same render path.** If RT ever becomes the study
path, the luminance calibration gets re-measured under RT first. For the Aug 1 demo, raster is the
study path (it's calibrated and it runs on the Macs); RT is eye-candy for the demo PC only.

## The ask, concretely

- Keep the raster path as-is and default. Don't remove or restructure it under RT.
- Put RT behind an auto-detected quality tier (`SystemInfo.supportsRayTracing`), off on anything that
  can't run it — never on the participant path.
- If you want RT to eventually be the study path, that's a separate decision that comes with re-running
  the L3 calibration under it (happy to help wire that measurement).

Everything else about the realism pass (curved walls, time-of-day sun, SSGI bounce) is raster and
lands cleanly — this note is only about the RT tier.
