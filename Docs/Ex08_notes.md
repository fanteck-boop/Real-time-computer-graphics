# Ex8 notes: GPU instancing and LOD on thousands of Nisse

## What was built
Scene `Assets/Scenes/Ex08_Optimization.unity` with a grid of Nisse copies, a fixed camera path and a component `Assets/Scripts/Ex08Bench.cs`. In the editor, keys 1 to 7 switch variant and an overlay shows FPS, draw calls, SetPass calls and triangles. Headless benchmark: `Unity.exe -batchmode -executeMethod RtcgPart3.RunEx08 -ex08bench <dir> -ex08scenario main|small|tiny` (no -nographics). Results are in `Captures/Ex08` (CSV per scenario, three runs in `runs/`, screenshots, `ex08_chart_main.png`, `ex08_chart_light_meshes.png`, LOD tint images). Aggregation: `Docs/Ex08_chart.py`.

LOD meshes were made from the scan with Blender's Decimate (collapse) modifier, `Docs/Ex08_make_lods.py`: vertices welded by position first (UVs stay as per-loop data so UV seams survive), then ratios 10 percent, 2 percent and 0.5 percent. LOD0 is the full scan (252,265 triangles), LOD1 25,226, LOD2 5,045, LOD3 1,260. UVs and the texture carry over; the close-up render shows no visible texture damage. Normals are recalculated by the Unity importer because the scan normals are noisy.

## Setup and why these numbers
Main scenario: 2,500 copies on a 50 by 50 grid (spacing 1.2), 1920x1080, half-float render target, vsync off, no shadows, one directional light. Camera circles the crowd on a path that depends only on the frame index, so every variant sees identical views. 90 warm-up frames, then 240 sampled frames, bracketed by a blocking GPU read-back so the average covers real GPU work. Each scenario was run three times. 2,500 was chosen because the full-detail baseline already sends about 508 million triangles per frame, which lands in a measurable 28 FPS range, while still fitting in memory.
LOD thresholds (screen height): LOD0 above 30 percent, LOD1 above 10 percent, LOD2 above 2 percent, culled below.

Machine: RTX 4070 SUPER, Direct3D 11, Unity 6000.6.0f1 URP 17.6, run inside the editor in batch mode with a render texture, not a built player and not a displayed window. Absolute numbers will differ in a build; ratios should hold.

## Important pitfalls found
1. The project pipeline asset has the GPU Resident Drawer on (it draws plain MeshRenderers through BatchRendererGroup). It was switched off in memory for variants 1 to 5 and restored afterwards; PC_RPAsset and PC_Renderer were checked by hash and are unchanged on disk. Variants 6 and 7 are what the project does by default.
2. The SRP Batcher takes precedence over GPU instancing for compatible shaders. Instancing was therefore measured with the SRP Batcher off and "Enable GPU Instancing" on the material (`Nisse_instanced.mat`, same shader and texture as `Nisse.mat`). Unity's stats confirm: instanced variant has 0 SRP Batcher draws and standard instanced draws instead.
3. Changing the Resident Drawer state recreates the pipeline and resets the SRP Batcher flag, so the benchmark re-applies it every frame. The first attempt had this bug (baseline silently ran with the SRP Batcher on); it was found from the stats and fixed.
4. The Profiler recorders "Draw Calls Count" and "Batches Count" returned 0 in this setup, so draw calls come from `UnityStats.drawCalls` (the "Batches" number in the Game view Stats window). Triangles, vertices and SetPass came from both sources and agree. Triangles are counted before back-face culling. Per-frame pacing percentiles were noisy and are left out of the table; they are in the CSV.
5. I used GameObjects with Unity's own culling rather than Graphics.RenderMeshInstanced, because RenderMeshInstanced bypasses frustum culling and would make the comparison unfair.

## Results, main scenario (2,500 full-detail Nisse, mean of 3 runs)
| Variant | ms/frame | FPS | Draw calls | SetPass | Triangles (M) |
|---|---|---|---|---|---|
| 1 Baseline (no batching) | 35.65 | 28 | 2023 | 19 | 508.3 |
| 2 SRP Batcher | 35.65 | 28 | 2023 | 15 | 508.3 |
| 3 GPU instancing | 35.72 | 28 | 19 | 19 | 508.3 |
| 4 LOD | 1.47 | 679 | 2023 | 19 | 15.8 |
| 5 LOD + instancing | 1.29 | 773 | 27 | 19 | 15.8 |
| 6 GPU Resident Drawer | 35.92 | 28 | 10 | 8 | 508.3 |
| 7 Resident Drawer + LOD | 1.33 | 754 | 14 | 8 | 15.8 |

Run-to-run spread was under 2 percent. Reading: with the 252k-triangle scan the frame is vertex bound on the GPU. Instancing cuts draw calls by about 100 times but cannot change how many triangles the GPU must process, so frame time does not move. LOD cuts triangles by 32 times and frame time by 24 times, with almost no visible difference (compare `main_Baseline_u0.png` and `main_Lod_u0.png`; `main_lod_tint.png` shows red LOD0, green LOD1, blue LOD2).

## Supplementary: light meshes (CPU bound), mean of 3 runs
| Scenario | Variant | ms/frame | Draw calls |
|---|---|---|---|
| 20,000 x 1.3k tris | Baseline | 6.41 | 16044 |
| | SRP Batcher | 4.92 | 16044 |
| | GPU instancing | 4.13 | 59 |
| | Resident Drawer | 2.00 | 12 |
| 10,000 x 5k tris | Baseline | 3.67 | 8015 |
| | SRP Batcher | 3.10 | 8015 |
| | GPU instancing | 3.07 | 42 |
| | Resident Drawer | 3.03 | 10 |

When meshes are cheap the CPU cost of submitting thousands of draw calls shows, and batching helps: instancing about 1.55 times, Resident Drawer about 3.2 times in the 1.3k case. At 5k triangles the GPU becomes the limit again and the gains shrink. Conclusion: instancing and batching fix CPU draw-call overhead; LOD fixes GPU geometry load. Which one matters depends on what the bottleneck is.

## Terms to explain at the exam
Draw call: one CPU command asking the GPU to draw a mesh. Batching: merging work to cut draw calls or state changes. SetPass call: a shader pass or material state change. GPU instancing: one draw call renders many copies of the same mesh and material, per-instance data (matrix) in a buffer. SRP Batcher: keeps material data in persistent GPU buffers so draw calls with the same shader variant are cheap; it reduces cost per draw, not the number of draw calls. LOD: swap in simpler meshes as objects get small on screen. Culling: skip objects that cannot be seen (frustum culling here; LOD can also cull below a size). Vertex bound versus CPU bound: which part is the bottleneck.

## Not verified
Not measured in a built player or with a visible window. LOD popping in motion was not judged visually (only stills). GPU time was not read from a GPU timer; FrameTimingManager gave nothing, so the GPU-bound claim rests on frame time matching triangle load, not a GPU profiler.

Built with Claude Code
