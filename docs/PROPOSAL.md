# Proposal — Controlled Single-Variable Room-Experiment Platform

*For Prof. David Kirsh (UCSD COGS 160, Track 3). Draft 2026-06-11. Every empirical claim below is cited to a real study — this document is meant to be checked, not taken on faith. Technical feasibility is stress-tested separately in [TECH_FEASIBILITY.md](TECH_FEASIBILITY.md).*

## 1. One-paragraph summary

We are building a **platform that lets a researcher (or an undergrad in two weeks) author a control/treatment room pair differing in exactly one variable, render it realistically in the browser via Unity WebGL, run a behavioral task, and export the data.** The scientific contribution is not any single finding — it is **enforced single-variable isolation** (a validator that mechanically refuses a confounded pair) plus a **reproducible, parametric stimulus pipeline**. This proposal grounds each manipulation in the literature, defines how we measure "realistic enough" as a number, specifies tasks and required sample sizes, and argues from evidence about whether non-immersive web-3D is a valid primary outcome or VR is required. (The project is 3D-only — interactive web-3D and native VR, never flat 2D images.)

## 2. Scientific grounding — the manipulations are literature-backed, not invented

Each variable the platform manipulates corresponds to a published effect. This is what makes the tool worth building: it operationalizes known paradigms as controlled, repeatable stimuli.

| Manipulation | Seminal finding | Citation |
|---|---|---|
| **Ceiling height** | "Cathedral effect": 10-ft ceilings prime *freedom* → relational/abstract processing; 8-ft prime *confinement* → item-specific/concrete processing. 3 experiments. | Meyers-Levy & Zhu 2007, *J. Consumer Research* 34(2):174–186 — [Oxford](https://academic.oup.com/jcr/article-abstract/34/2/174/1793118) |
| **Curvature / contour** | Curvilinear interiors judged more beautiful & pleasant than rectilinear; pleasantness explains ~58% of beauty variance; engages anterior cingulate cortex (fMRI). Note: contour did *not* shift approach-avoidance — a dissociation worth designing around. | Vartanian et al. 2013, *PNAS* 110(supp 2):10446–10453 — [PNAS](https://www.pnas.org/doi/10.1073/pnas.1301227110) |
| **Windows / nature view** | Surgical patients with a nature window view: ~0.8 days shorter stay, 22% less pain medication vs. a brick-wall view. The founding result of restorative-environment research. | Ulrich 1984, *Science* 224:420–421 — [Science](https://www.science.org/doi/10.1126/science.6143402) |
| **Natural light / restoration mechanism** | Attention Restoration Theory: natural content restores directed-attention capacity; the theoretical basis for window/lighting effects. | Kaplan 1995, *J. Environmental Psychology* 15:169–182 |
| **Wall texture / color** | Surface material + warmth manipulations sit under the same approach (treated as a held-or-varied surface variable in the contract). | (secondary; covered by the schema's `surfaces` block) |

**Observation for the proposal:** the platform's first three studies (ceiling, curvature, windows) each *replicate a landmark paper* with a modern, controlled, parametric stimulus — a defensible, non-hallucinated research program rather than a tech demo.

## 3. "Realistic enough" — measured, not asserted

Kirsh's bar is "realistic enough that it gives a reliable experience of being in the room." We make that a **number with a pass threshold** using validated presence instruments rather than a subjective call:

- **IPQ (Igroup Presence Questionnaire)** — 14 items, subscales: spatial presence, involvement, realness (Schubert 2001). Two decades of normative data exist for thresholding. — [IPQ review](https://www.frontiersin.org/articles/10.3389/fpsyg.2020.00349/full)
- **Slater-Usoh-Steed (SUS)** — 6 items, subjective "being there." Robust, simple to score.
- **Witmer-Singer PQ** — 19 items incl. a **sensory fidelity** subscale — directly relevant to our realism question.

**Protocol:** before any effect study, run a stimulus-validation pilot — participants rate a rendered room on IPQ; we set an a-priori threshold (e.g. spatial-presence subscale ≥ a target) the render must clear. This converts "does it look real?" into a gate with a defensible cutoff and is itself a publishable methods contribution.

## 4. Tasks, measures, and required sample size

**Task paradigms** (behavioral, matching Kirsh's list; each has standard references):
- **Concentration**: proofreading / arithmetic in-room → accuracy + time (the Meyers-Levy processing paradigm fits here).
- **Memory**: study in-room, test *outside* the room (Kirsh's point — once it's a memory test the room is no longer needed at test).
- **Navigation/pointing**: traverse, return, point to a remembered location → **angular error** as the dependent measure.
- **Adaptive preference**: A-vs-B paired comparison (~20 stimuli resolved in ~8 smart comparisons).

**Design + power:** within-subjects (repeated measures) maximizes power per participant via counterbalanced order (Latin square). Published tutorials put a repeated-measures ANOVA at **N ≈ 24** for a medium effect (f = 0.25, power 0.80, α 0.05) — [Brysbaert, *J. Cognition* 2019](https://journalofcognition.org/articles/10.5334/joc.72). **Observation:** an undergrad two-week study with ~24 participants is realistic, and the tool should bake counterbalancing + the seed-logged ordering in (it already does — see the determinism guardrail).

**Timing caveat (measured):** online platforms lag response-time recording by ~80–100 ms on average — [Bridges et al. timing mega-study, *PeerJ* 2020](https://peerj.com/articles/9414.pdf). Implication: rating and choice tasks are fine; *millisecond-precision* RT tasks need external chronometry or should be framed as accuracy-first. We state this limit up front rather than overclaiming.

## 5. Modality — is non-immersive web-3D a valid primary outcome, or is VR core? (his open question, answered with evidence)

The literature gives a **nuanced, evidence-based answer** rather than a guess:
- A direct comparison of **real environment vs VR vs 2D pictures** for room **memory** found real-world best, but **no difference between VR and 2D** except on one non-suggestive verbal task — [Springer *Virtual Reality* 2024](https://link.springer.com/article/10.1007/s10055-024-00999-w). → For memory/cognition tasks, **non-immersive screen viewing is a defensible primary outcome.** *(We deliver this as interactive real-time web-3D — the project is 3D-only; no flat 2D images/panoramas.)*
- For **restoration/affective** metrics, neither VR mode perfectly replicated in-situ results (cylindrical VR slightly better than HMD) — [restorative interior VR-vs-screen study, PMC 2025](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC11765425/). → Presence-/restoration-sensitive outcomes are where VR earns its keep.

**Recommendation to Kirsh:** ship **interactive web-3D as the primary outcome for cognitive/behavioral tasks** (real-time 3D rooms, no flat 2D), where evidence says non-immersive viewing holds; **native VR is the immersive end-goal arm** (live operator editing + restoration/presence studies), where immersion measurably adds. The platform's "modality is a recorded variable, never pooled" rule is exactly the methodological discipline this evidence demands. **The project is 3D-only — web-3D and VR, never 2D images.**

## 6. Landscape — what exists, and the gap we fill

Existing online-experiment platforms — **jsPsych, PsychoPy/PsychoJS, Gorilla, Lab.js, Labvanced, Pavlovia** — are excellent at *trial logic, timing, and hosting* ([platform precision review, *Behavior Research Methods* 2020](https://link.springer.com/article/10.3758/s13428-020-01501-5)). **None of them generate controlled 3D room stimuli or enforce single-variable isolation across a stimulus pair.** Kirsh's referenced **Max Planck VR rooms** are high-fidelity but architect-built per-scene (not parametric, not student-authorable).

**The gap = our contribution:** a *parametric room generator* + a *mechanical single-variable validator* feeding a standard experiment runner. We can even **export trials to jsPsych** rather than rebuild trial logic — interoperate, don't reinvent.

## 7. Technical feasibility (summary; full stress-test in TECH_FEASIBILITY.md)

Verdict: **buildable on the web, with a pre-defined native fallback.** Key constraints we have already designed around: Unity WebGL's ~2 GB heap ceiling and 50–100 MB build budget; no runtime baked global illumination (handled by a runtime-captured light-probe approximation); HiDPI sharpness and Brotli hosting on Cloudflare. The single largest risk — realism on a mid laptop — is gated by the §3 presence threshold with a native-app fallback if it fails. See [TECH_FEASIBILITY.md](TECH_FEASIBILITY.md).

## 8. Risks & open questions for Prof. Kirsh

1. **Effect-size targets per manipulation** — we have qualitative effects; do you have target effect sizes (or pilot data) so we can finalize N per study?
2. **Primary modality** — do you accept interactive web-3D as the primary outcome for the cognitive tasks (evidence in §5), with native VR as the immersive arm? (Project is 3D-only — no flat 2D.)
3. **Realism threshold** — what IPQ/presence cutoff would you consider "reliable experience of being in the room"? (We propose piloting to set it.)
4. **First study** — which single manipulation do you want as the flagship replication: ceiling height (easiest, strongest prior), curvature (your headline interest), or windows/light (strongest restoration literature)?
5. **MPI rooms** — can we get details on how the Max Planck stimuli were made / whether they are modifiable? It informs whether we interoperate or stay fully self-generated.

## Sources
- Meyers-Levy & Zhu 2007, *JCR* — [link](https://academic.oup.com/jcr/article-abstract/34/2/174/1793118) · [Cathedral effect overview](https://en.wikipedia.org/wiki/Cathedral_effect)
- Vartanian et al. 2013, *PNAS* — [link](https://www.pnas.org/doi/10.1073/pnas.1301227110)
- Ulrich 1984, *Science* — [link](https://www.science.org/doi/10.1126/science.6143402)
- Kaplan 1995 ART — [overview](https://positivepsychology.com/attention-restoration-theory/)
- Presence questionnaires review — [Frontiers 2020](https://www.frontiersin.org/articles/10.3389/fpsyg.2020.00349/full)
- VR vs 2D vs real memory — [Springer VR 2024](https://link.springer.com/article/10.1007/s10055-024-00999-w) · restoration VR vs screen — [PMC 2025](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC11765425/)
- Online timing/precision — [PeerJ 2020](https://peerj.com/articles/9414.pdf) · [BRM 2020](https://link.springer.com/article/10.3758/s13428-020-01501-5)
- Power analysis tutorial — [Brysbaert 2019](https://journalofcognition.org/articles/10.5334/joc.72)
