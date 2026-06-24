# Cloud rendering research — "server renders, thin app on a weak client"

*Question (Diego, 2026-06-11): can we run the heavy rendering on a server (the UCSD supercomputer, SDSC) so an average user downloads a light app and runs the project without strong hardware — like a video game streams from the cloud? Researched + cited. Verdict + the honest constraints below.*

## Short answer

- **The CONCEPT is real and mature: it's called pixel streaming / cloud gaming.** A server renders, encodes the frames to video, and streams them over WebRTC to a thin client (even a browser). The client needs no strong GPU. (Unity Render Streaming, Unreal Pixel Streaming, GeForce NOW, Xbox Cloud.) So "weak client, server does the rendering" — **yes, possible, proven.** ([Pixel Streaming guide](https://vagon.io/blog/what-is-pixel-streaming), [UE Pixel Streaming docs](https://dev.epicgames.com/documentation/unreal-engine/pixel-streaming-in-unreal-engine))
- **BUT it canNOT be the UCSD supercomputer.** An HPC supercomputer (SDSC Expanse) is the **wrong kind of machine** for this. Different infrastructure entirely (below).
- **And the economics make it a poor fit for "average user, at scale, cheap."** One GPU serves only a handful of users; cost is real and per-user.
- **For VR specifically: no** (latency wall, covered in VR_LIVE_EDITING.md).

## Why a supercomputer is the wrong tool (HPC ≠ cloud-gaming server)

| | HPC supercomputer (SDSC Expanse) | Cloud-gaming / pixel-streaming server |
|---|---|---|
| Built for | **batch** throughput; jobs queue, run, finish; latency between submit and result is tolerated | **low-latency interactive** real-time per-user video streams |
| Access model | Slurm job queue, shared GPU nodes, allocation hours | a live GPU session held open per active user, WebRTC out |
| Networking | InfiniBand between nodes (internal); not public inbound video | public low-latency edge + NVENC video encoders |
| Per-user realtime session | not how it works | the whole point |

The two are "fundamentally different: cloud gaming prioritizes low-latency interactive streaming to individual users, while HPC optimizes for maximum throughput with tolerance for latency between submission and completion" ([HPC vs cloud](https://resources.l-p.com/knowledge-center/cloud-computing-vs-high-performance-computing-comparison)). SDSC also isn't licensed/provisioned to host a public-facing 24/7 interactive app. **So pixel streaming would run on commercial cloud GPUs (AWS/Azure/GCP) or a service (Vagon, Streampixel) — never on Expanse.**

## The economics — why it doesn't "scale free"

- **One GPU serves only a few users at once.** NVENC caps consumer GPUs at 3–5 encode sessions; datacenter GPUs (T4/A10G) do ~2–4 instances each ([scaling/cost](https://www.strayspark.studio/blog/pixel-streaming-ue5-cloud-gaming-demo)). A study with 50 simultaneous participants ≈ 10–25 GPUs running.
- **Cost is per-user-hour:** ~$0.18–0.92 / user / hour on AWS g4dn; managed services ≈ €49.50 / concurrent user / month ([Streampixel](https://www.streampixel.io/), [economics](https://medium.com/@FernandoCampos/the-evil-economics-of-pixel-streaming-b2da13a00f51)).
- Blunt industry verdict: cloud GPUs are "~10,000× more expensive than on-device rendering, so using this for mass consumer projects is untenable" ([on-device vs streaming](https://alan-smithson.medium.com/pixel-streaming-vs-on-device-local-rendering-a-comprehensive-business-guide-c1ab20a06907)).
- **Bandwidth shifts to the participant:** ~5 Mbps (720p), 10–12 Mbps (1080p), 25+ Mbps (4K) **per user** ([bandwidth](https://developer.pureweb.io/framerate-bandwidth/)). You trade "needs a strong GPU" for "needs strong internet."
- **Latency is fine for our non-VR case:** WebRTC pixel streaming hits ~15–90 ms ([latency](https://www.programming-helper.com/tech/cloud-gaming-2026-latency-infrastructure-streaming)) — good enough for looking at a room + answering, slider edits, viewport nav. (Not good enough for VR head-tracking — that needs <20 ms motion-to-photon, impossible over public internet from a distant server.)

## The key insight — we probably don't need it

Our rooms are **light**: parametric Unity geometry (simple walls/floor/furniture), not a AAA game. The expensive part (global-illumination bake) is a **one-time offline** step, not per-frame. So:

- A **modest laptop runs our room locally just fine** — the realism stack (runtime light probe + ambient occlusion + tone mapping) is cheap at runtime. The "average user can't render it" problem is much smaller than for a real video game.
- Where SDSC genuinely helps is its **actual strength: offline batch pre-baking** — compute high-fidelity lightmaps / a library of room variants ahead of time, store them on **Cloudflare**, and the light client renders the already-baked room cheaply. That delivers "good rooms on weak hardware" **without** paying per-user streaming cost.

## Options, ranked for this project

1. **Local rendering + offline bakes (recommended).** Client renders locally (rooms are light); SDSC (if available) batch-bakes high-fidelity variants offline → Cloudflare → client downloads a light, pre-lit room. Cheapest, simplest, scales for free (no per-user GPU). Matches the current plan.
2. **Pixel streaming as an optional fallback for genuinely weak clients — via a commercial cloud-GPU service, NOT SDSC.** Real, works for the non-VR arm, but costs per concurrent user and needs participant bandwidth. Add only if a real need appears (e.g. Chromebook-only participants at small scale). Unity Render Streaming is the path.
3. **Stream from SDSC.** ✗ Not viable — wrong infrastructure (batch HPC, no per-user low-latency streaming stack, hosting not provisioned).

## Bottom line for Diego

- "Server renders, weak client downloads a light app" = **yes, possible — pixel streaming.** Real tech, used by cloud gaming.
- "Have the UCSD supercomputer do it" = **no** — an HPC supercomputer is the wrong machine; you'd use commercial cloud GPUs or a streaming service, and pay per active user.
- For *our* light parametric rooms, **local rendering + optional SDSC offline pre-baking is cheaper and simpler** and gets the same "runs on a normal computer" outcome — without a per-user GPU bill.
- VR via streaming stays off the table (latency).

## Sources
- Pixel streaming: [Vagon guide](https://vagon.io/blog/what-is-pixel-streaming) · [Unreal docs](https://dev.epicgames.com/documentation/unreal-engine/pixel-streaming-in-unreal-engine) · [2026 status](https://www.strayspark.studio/blog/pixel-streaming-ue5-cloud-gaming-demo)
- Economics / scaling: [the economics of pixel streaming](https://medium.com/@FernandoCampos/the-evil-economics-of-pixel-streaming-b2da13a00f51) · [on-device vs streaming](https://alan-smithson.medium.com/pixel-streaming-vs-on-device-local-rendering-a-comprehensive-business-guide-c1ab20a06907) · [Streampixel pricing](https://www.streampixel.io/)
- Bandwidth/latency: [framerate & bandwidth](https://developer.pureweb.io/framerate-bandwidth/) · [cloud gaming latency 2026](https://www.programming-helper.com/tech/cloud-gaming-2026-latency-infrastructure-streaming)
- HPC vs cloud: [comparison](https://resources.l-p.com/knowledge-center/cloud-computing-vs-high-performance-computing-comparison)
