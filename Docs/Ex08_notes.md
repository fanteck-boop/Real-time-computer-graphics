# Ex8 notes: GPU instancing and LOD on thousands of Nisse

## What was built
Scene `Assets/Scenes/Ex08_Optimization.unity` with a grid of Nisse copies, a fixed camera path and a component `Assets/Scripts/Ex08Bench.cs`. In the editor, keys 1 to 7 switch variant and an overlay shows FPS, draw calls, SetPass calls and triangles. Headless benchmark: `Unity.exe -batchmode -executeMethod RtcgPart3.RunEx08 -ex08bench <dir> -ex08scenario main|small|tiny` (no -nographics). Results are in `Captures/Ex08` (CSV per scenario, three runs in `runs/`, screenshots, `ex08_chart_main.png`, `ex08_chart_light_meshes.png`, LOD tint images). Aggregation: `Docs/Ex08_chart.py`.

LOD meshes were made from the scan with Blender's Decimate (collapse) modifier, `Docs/Ex08_make_lods.py`: vertices welded by position first (UVs stay as per-loop data so UV seams survive), then ratios 10 percent, 2 percent and 0.5 percent. LOD0 is the full scan (178,672 triangles in the source file, 178,500 as Unity reports the imported mesh), LOD1 17,844, LOD2 3,568, LOD3 891. The scan was updated after the first measurements (base plate removed, fewer triangles), so every number here was measured again on the new mesh. The diffuse texture is now 8192 (it was capped at 4096 before). UVs and the texture carry over; I did not re-check the close-up render for texture damage at 8192. Normals are recalculated by the Unity importer because the scan normals are noisy.

## Setup and why these numbers
Main scenario: 2,500 copies on a 50 by 50 grid (spacing 1.2), 1920x1080, half-float render target, vsync off, no shadows, one directional light. Camera circles the crowd on a path that depends only on the frame index, so every variant sees identical views. 90 warm-up frames, then 240 sampled frames, bracketed by a blocking GPU read-back so the average covers real GPU work. Each scenario was run three times. 2,500 was chosen because the full-detail baseline still sends about 358 million triangles per frame, which lands in a measurable 40 FPS range, while still fitting in memory.
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
| 1 Baseline (no batching) | 24.92 | 40 | 2013 | 18 | 358.0 |
| 2 SRP Batcher | 24.88 | 40 | 2013 | 14 | 358.0 |
| 3 GPU instancing | 25.41 | 39 | 18 | 18 | 358.0 |
| 4 LOD | 2.34 | 428 | 2013 | 18 | 10.9 |
| 5 LOD + instancing | 1.59 | 629 | 27 | 18 | 10.9 |
| 6 GPU Resident Drawer | 25.38 | 39 | 16 | 8 | 358.0 |
| 7 Resident Drawer + LOD | 1.23 | 813 | 20 | 8 | 10.9 |

Run-to-run spread of the three runs was under 3 percent for variants 1 to 3 and 6. For the LOD variants it is larger in relative terms (variant 7 ranged 1.16 to 1.32 ms, variant 4 2.28 to 2.40), because the frames are so short. Reading: with the 179k-triangle scan the full-detail frame is still vertex bound on the GPU. Instancing cuts draw calls by about 110 times but cannot change how many triangles the GPU must process, so frame time does not move. LOD cuts triangles by 33 times and frame time by 10.6 times (variant 4) to 20 times (variant 7), with almost no visible difference (compare `main_Baseline_u0.png` and `main_Lod_u0.png`; `main_lod_tint.png` shows red LOD0, green LOD1, blue LOD2).

What changed against the first measurement on the 252k scan (28 FPS, 508.3 M triangles): triangles per frame dropped by 30 percent and the baseline got 30 percent faster (35.65 to 24.92 ms), so frame time still follows triangle count. The LOD gain is smaller than before (24 times then, 10.6 times for variant 4 now). The reason is that the LOD frame is now so cheap (2.34 ms) that it is no longer limited by triangles. With 2013 draw calls the CPU cost shows: variant 5 (instancing, 27 draws) is 1.5 times faster than variant 4 (1.59 against 2.34 ms), and variant 7 is the fastest. Before, the LOD variants were within about 15 percent of each other. So with the lighter mesh, once LOD is on, batching matters again. This is my reading from draw counts and frame times, not from a CPU profiler.

## Supplementary: light meshes (CPU bound), mean of 3 runs
| Scenario | Variant | ms/frame | Draw calls |
|---|---|---|---|
| 20,000 x 0.9k tris (LOD3) | Baseline | 10.53 | 15989 |
| | SRP Batcher | 6.04 | 15989 |
| | GPU instancing | 4.36 | 59 |
| | Resident Drawer | 1.63 | 71 |
| 10,000 x 3.6k tris (LOD2) | Baseline | 6.15 | 7988 |
| | SRP Batcher | 3.84 | 7988 |
| | GPU instancing | 3.65 | 42 |
| | Resident Drawer | 2.28 | 40 |

These two scenarios use LOD3 and LOD2 of the new scan, which are smaller than before (0.9k instead of 1.3k, 3.6k instead of 5k triangles), so they are not the same test as the old table. The baselines are also slower than before (10.53 against 6.41 ms and 6.15 against 3.67 ms) although the meshes are lighter, so something else changed. I did not find out what; the machine state or the 8192 texture are possible but untested. When meshes are cheap the CPU cost of submitting thousands of draw calls shows, and batching helps: instancing about 2.4 times and Resident Drawer about 6.5 times in the 0.9k case. The small-mesh instancing run spread is large (2.87 to 4.96 ms in the 3.6k case), so read that row loosely. In the 3.6k case the Resident Drawer still wins by 2.7 times. Conclusion: instancing and batching fix CPU draw-call overhead; LOD fixes GPU geometry load. Which one matters depends on what the bottleneck is.

## Terms to explain at the exam
Draw call: one CPU command asking the GPU to draw a mesh. Batching: merging work to cut draw calls or state changes. SetPass call: a shader pass or material state change. GPU instancing: one draw call renders many copies of the same mesh and material, per-instance data (matrix) in a buffer. SRP Batcher: keeps material data in persistent GPU buffers so draw calls with the same shader variant are cheap; it reduces cost per draw, not the number of draw calls. LOD: swap in simpler meshes as objects get small on screen. Culling: skip objects that cannot be seen (frustum culling here; LOD can also cull below a size). Vertex bound versus CPU bound: which part is the bottleneck.

## Not verified
Not measured in a built player or with a visible window. LOD popping in motion was not judged visually (only stills). GPU time was not read from a GPU timer; FrameTimingManager gave nothing, so the GPU-bound claim rests on frame time matching triangle load, not a GPU profiler.

Built with Claude Code
