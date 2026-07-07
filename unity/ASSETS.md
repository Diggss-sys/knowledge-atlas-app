# unity/ASSETS.md — pinned CC0 asset locks (determinism guardrail)

*The committed lock for the fidelity lane. Textures themselves are **git-ignored** (see `.gitignore`) — this file is the source of truth. `RoomGen.Editor.AssetFetcher` downloads each zip from the URL, verifies it against the SHA-256 here, extracts the maps, and builds an HDRP/Lit material. Determinism holds because the hash pins the exact bytes and the material build is deterministic code.*

*Populated 2026-07-06 (L1). All assets CC0 from ambientCG (redistributable, no attribution burden). Re-run the fetcher after `RoomGen ▸ Bootstrap Project` so textured materials overwrite the flat bootstrap builtins.*

## How to fetch

Editor: menu **RoomGen ▸ Fetch CC0 Materials**, or headless:

```
Unity.exe -batchmode -quit -projectPath unity \
  -executeMethod RoomGen.Editor.AssetFetcher.FetchAll -logFile fetch.log
```

The fetcher looks for each `<AssetId>_2K-JPG.zip` in `unity/Assets/RoomGen/.cache/` first; if absent it downloads to there. Missing network → it logs and leaves the flat builtin in place (no hard failure).

## Texture pins (ambientCG, 2K-JPG)

Download URL pattern: `https://ambientcg.com/get?file=<AssetId>_2K-JPG.zip` (follows one redirect to the CDN).

Physical size = the real-world edge length the texture represents; drives world-scale tiling (material world size). ambientCG's API did not report a physical size for these ids, so the values below are **chosen defaults** for plausible architectural scale (documented, not API-derived) — determinism is preserved by the SHA-256 pin regardless.

| RoomSpec target material | ambientCG id | Physical size (m) | Maps in zip | SHA-256 (of the 2K-JPG zip) |
|---|---|---|---|---|
| `builtin.oak` (dining floor) | WoodFloor043 | 2.0 | Color, NormalGL, Roughness, AO, Metalness, Displacement | `5f9cab5c67eeeb441a6aa6744f9ebbd56186dfeab4f9c1d924220d3d43b73bdb` |
| `builtin.warm-white` (walls) | Plaster001 | 2.0 | Color, NormalGL, Roughness, Displacement | `9894765632cfa0feb3349b581825a9c224dee9efa1cebb8aba63bd1c7bc9fc3c` |
| `builtin.ceiling-white` (ceiling) | Plaster001 | 2.0 | (as above; tinted lighter) | `9894765632cfa0feb3349b581825a9c224dee9efa1cebb8aba63bd1c7bc9fc3c` |
| `cc0.carpet` (future rooms) | Carpet012 | 2.0 | Color, NormalGL, Roughness, AO, Displacement | `476528d0deb91b38608e7d2ef51991ba89b5c976e0ae726d655588728f5852c8` |
| `cc0.tile` (future rooms) | Tiles107 | 1.0 | Color, NormalGL, Roughness, AO, Displacement | `fa76887bb33af0972154700126d1f86d01eaed518341ce7411616c1d1202ac55` |
| `builtin.walnut` (table, legs) | Wood066 | 0.8 | Color, NormalGL, Roughness | `fa88e3f0cb03e940e6618056c4b4b10577488f01383a7d39ca85d62d13122800` |
| `builtin.fabric-charcoal` (chairs) | Fabric045 | 0.6 | Color, NormalGL, Roughness, AO | `e6ca2d15806601a0f9199981dd3c3efe80519ecd9d806910b2b0ab3a8320e770` |
| `builtin.metal-black` (handles) | Metal032 | 0.4 | Color, NormalGL, Roughness, Metalness | `4b8884843c490963d5734be036c35639d064e7a817eb61145322f69c6c19895a` |

Notes:
- Not every asset ships an AmbientOcclusion or Metalness map. The fetcher packs the HDRP MaskMap as **R=Metalness (0 if absent), G=AO (1 if absent), B=0.5 (detail neutral), A=Smoothness = 1 − Roughness**.
- NormalGL (OpenGL convention) is used and imported as a Unity NormalMap; NormalDX is ignored.
- `.usdc/.blend/.mtlx/.tres/.png` preview files in each zip are ignored.

## Furniture models (Poly Haven, CC0, FBX + 2K JPG textures)

Downloaded by hand into `unity/Assets/RoomGen/Resources/RoomGen/Furniture/src/<asset>/` (files are
gitignored like textures; these md5 pins are **Poly Haven's own** published hashes, verified on
download). `RoomGen ▸ FurnitureModelBuilder.BuildAll` (menu-less; `-executeMethod`) turns them into
placement prefabs at `Resources/RoomGen/Furniture/builtin-*.prefab` — the path FurnitureLayoutResolver
prefers over greybox. Blender-axis correction (-90° X) + fit-to-footprint scaling happen on the prefab
wrapper (see FurnitureModelBuilder.cs).

All files are the **2k** variants; md5s below are Poly Haven's published hashes, verified on download 2026-07-07. URL pattern: `https://dl.polyhaven.org/file/ph-assets/Models/{fbx|jpg}/2k/<id>/<file>`.

| Prefab | File | md5 |
|---|---|---|
| builtin-dining-chair | dining_chair_02_2k.fbx (save as dining_chair_02.fbx) | `702bc3c0cf6e6f88d4ae133218c7e7f3` |
| | textures/dining_chair_02_diff_2k.jpg | `be041803606b5bf7869f08ab58a1581f` |
| | textures/dining_chair_02_rough_2k.jpg | `0bd4578133e1e0493706422f19c3aa94` |
| builtin-dining-table | wooden_table_02_2k.fbx (save as wooden_table_02.fbx) | `123a41b3f2f31e89c8b3d345311c9a1e` |
| | textures/wooden_table_02_diff_2k.jpg | `2a346503ce15e4df59c0fd4ed1891047` |
| | textures/wooden_table_02_rough_2k.jpg | `50647bce6cdbba9eed1d7f7fab403d01` |
| builtin-sideboard | GothicCabinet_01_2k.fbx (save as GothicCabinet_01.fbx) | `d5852a16d7b04e708f6a52c2e11d231f` |
| | textures/GothicCabinet_01_diff_2k.jpg | `f53afdcbd60800e44df15030b6c46dad` |
| | textures/GothicCabinet_01_roughness_2k.jpg | `8af2bc96973772b49555229cc4fb423b` |

Table fit-scaled to the 2.15×1.05 slot footprint; sideboard to 1.72×0.97×0.50; chair keeps real-world
scale. Pendant deliberately stays the purpose-built emissive luminaire (L2); plant stays greybox.
Move the manual download into an AssetFetcher-style script when the lane owner takes over.

## Still to pin (remaining schema material enums, from docs/ASSET_SOURCING.md)

`paint` (PaintedPlaster016), `brick` (Bricks060), `concrete` (Concrete012), `marble` (Marble001), `glass` (shader-only, no texture). Add here with hashes when their rooms need them.
