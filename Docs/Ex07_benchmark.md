# Exercise 7: benchmark of frustum culling and LOD (and mipmaps)

The exercise asks for two techniques, shown with and without them, with the difference in FPS, draw calls and the quality of the picture. The two techniques in `Assets/Scenes/Ex 7 Culling and Mips.unity` are **frustum culling** and **LOD**. Mipmapping is measured as a small extra.

Files:

- `Assets/Scripts/Ex7/Ex7Bench.cs`: the benchmark itself (runs in Play mode, only when the editor is started with `-ex7bench <folder>`).
- `Assets/Editor/RtcgEx7Bench.cs`: opens the scene and presses Play. It has a 15 minute watchdog so the editor always exits.
- `Docs/Ex07_bench_chart.py`: makes the chart and the side by side images from the CSV and the raw frames.
- Output in `Captures/Ex07_Bench/`: `results.csv`, `chart.png`, `with_without_culling.png`, `with_without_lod.png`, `mips_off_on.png`, and the raw frames in `raw/`.

Command:

```
Unity.exe -batchmode -projectPath <project> -executeMethod RtcgEx7Bench.Run -ex7bench Captures/Ex07_Bench -logFile Captures/_logs/ex7bench.log
python Docs/Ex07_bench_chart.py
```

No `-quit`, because Play mode has to keep running. The scene is never saved. Everything the benchmark changes (camera, LOD forcing, a material on the corridor floor) is changed at runtime only.

## Method

**Machine.** NVIDIA GeForce RTX 4070 SUPER, Intel Core i5-14600K, Direct3D 12, Unity 6000.6.0f1, URP. Quality level PC (LOD bias 2, anisotropic filtering forced on). The project's pipeline asset has the GPU Resident Drawer on, and it was left on. All numbers come from the Unity **editor** in batch mode, rendering into a 1920 x 1080 render texture. They are not from a built player or a window on screen.

**Camera path.** The player stands in the middle of the Nisse field (eye height 1.65 m, looking 6 degrees down) and turns one full circle, 1 degree per frame, 360 frames. The path only depends on the frame number, so every variant sees exactly the same views. The camera has a 60 degree vertical field of view, which is about 91 degrees wide at 16:9, so at any moment roughly a quarter of the field is in view. My own 6-plane test says 95 to 104 of the 372 objects (356 Nisse and 16 pillars) are inside the view, 99 on average. That is why culling matters here: about three quarters of the field is behind or beside the camera all the time.

**The four variants.**

| Variant | Frustum culling | LOD |
|---|---|---|
| Neither | defeated | every Nisse forced to LOD0 (the 178 000 triangle scan) |
| Culling only | normal | forced to LOD0 |
| LOD only | defeated | automatic |
| Both | normal | automatic (what the scene normally does) |

How culling is defeated: `Camera.cullingMatrix` is set to a fixed orthographic box, 300 m on each side, around the whole level. Unity tests every object against the planes of this matrix instead of the real frustum, so everything passes. The camera still renders with its normal projection, so the picture does not change. LOD still uses the real camera position and field of view. How LOD is switched off: `LODGroup.ForceLOD(0)` on all 356 LODGroups.

**Measuring.** vsync off, no frame rate cap. For each variant: 60 warm-up frames, then 360 timed frames. The timer is started and stopped right after a blocking 1 pixel read-back from the render texture, so the time includes the GPU finishing its work, not just the CPU handing it over. Draw calls, SetPass calls, instances and triangles come from `UnityStats` (the numbers in the Game view Stats window), averaged over the 360 frames. Three runs, and inside each run the four variants take turns, so slow drift in the machine hits all variants alike.

## Results (mean of 3 runs)

| Variant | ms per frame (sd) | FPS | Draw calls | Instances drawn | SetPass | Triangles (millions) |
|---|---|---|---|---|---|---|
| Neither | 6.69 (0.02) | 150 | 40 | 1482 | 11 | 127.1 |
| Culling only | 2.57 (0.03) | 390 | 30 | 398 | 9 | 33.0 |
| LOD only | 1.31 (0.13) | 762 | 46 | 1482 | 11 | 7.4 |
| Both | 1.14 (0.04) | 875 | 36 | 398 | 9 | 2.0 |

![chart](../Captures/Ex07_Bench/chart.png)

What it shows:

- **Culling alone** cuts the triangles from 127 to 33 million (3.9 times fewer) and the frame time from 6.7 to 2.6 ms. The number of instances drawn drops from 1482 to 398. That fits: about a quarter of the objects are in view. So culling behaves as expected, and the check that it really was defeated is that the instance count and the triangles go up by about 3.7 times when the culling matrix is replaced.
- **LOD alone** cuts the triangles from 127 to 7.4 million (17 times fewer) and the frame time to 1.3 ms. From the middle of the field most Nisse are 10 to 40 m away, where LOD2 (5 045 triangles) is picked instead of the 178 473 triangle scan. LOD helps more than culling here because the scan is so heavy.
- **Both together** gives 2.0 million triangles, 65 times fewer than neither, and 1.14 ms (875 FPS against 150).
- Between "LOD only" and "Both" the triangles still drop by 3.7 times, but the frame time only moves from 1.31 to 1.14 ms. Below about 1 ms the GPU is no longer the limit; the editor's own work per frame is. So the last step in the chart understates what culling would save in a heavier scene or a built game.

**Draw calls hardly change** (30 to 46). This is because of the GPU Resident Drawer: it draws all copies of the same mesh and material in one instanced draw call, so the draw call count follows the number of different meshes, not the number of objects. The "instances drawn" column is the better count of how many objects were sent to the GPU. It counts each object about twice (once for the depth pre-pass and once for the colour pass), plus the ground, walls and corridor. With culling defeated the draw calls even go up a little, because a few more different meshes (other LOD levels, the far walls and corridor) end up in view.

## Pictures

**`with_without_culling.png`**: a top view of the field at one heading (30 degrees). For this picture a camera above the field is given the player camera's culling matrix, so Unity culls the top view exactly as it culls the player's view. Whatever the engine would skip is simply missing from the top view. Nisse and pillars are coloured green so they show up from above. The yellow lines mark the edges of the player's view. Left, culling defeated: all objects are sent to the GPU, including everything behind the player. Right, normal culling: only the wedge in front of the player is drawn. A few objects just outside the yellow lines are still drawn, because the test uses each object's bounding box and errs on the side of drawing. The picture the player actually sees is the same in both cases (`raw/cull_off_player.png` and `raw/cull_on_player.png`). At this heading: 7.4 million triangles without culling, 1.9 million with.

**`with_without_lod.png`**: the player view from the spawn point, left with every Nisse forced to LOD0, right with automatic LOD. 35.0 million triangles against 2.4 million in this frame. The two pictures are almost impossible to tell apart. That is the point of LOD: the detail that is removed is smaller than a pixel. Up close the difference can be seen (`Captures/Ex07_Steps/extra_step2_lod0_left_lod3_right.png`).

**`mips_off_on.png`**: a 400 x 225 pixel crop of the middle distance of the corridor floor, enlarged 4 times without smoothing. Left: a copy of the floor texture with only mip level 0. Right: the normal texture with its Kaiser mip chain. Without mips the floor breaks up into rings and stripes (moiré), because each screen pixel picks one texel out of many. With mips the far floor turns into an even grey, which is the true average of the checker. The full frames with the crop marked are in `raw/mips_off_on_full.png`.

## Mipmaps: how and what was measured

The corridor floor texture (`Ex7_Corridor`, 1024 x 1024, Kaiser, trilinear) was not changed. The script makes a runtime copy of level 0 only, by a 1:1 `Graphics.Blit` into a render texture without mips (bilinear, same wrap and anisotropic setting), and puts it on a temporary copy of the floor material. The original material goes back afterwards. Same view for both (corridor start, 8 degrees down), 3 runs of 360 frames.

| Variant | Run 1 | Run 2 | Run 3 | Mean |
|---|---|---|---|---|
| Mips on (Kaiser) | 1.08 ms | 1.35 ms | 1.16 ms | 1.20 ms |
| Mips off (level 0 only) | 1.10 ms | 1.81 ms | 1.01 ms | 1.30 ms |

**The frame time difference is not measurable here.** The spread between runs (up to 0.8 ms) is much bigger than any difference between the two. One small texture on one floor at about 1 ms per frame is far from what limits this GPU, and the editor overhead dominates. In theory mips also save memory bandwidth, because far pixels read from small levels that stay in the texture cache; that would show with many large textures, not with one. So for mipmaps the result is about picture quality, not speed.

## Problems on the way

1. `WaitForEndOfFrame` never fires when the editor runs in batch mode. The first version waited on it and hung; it was killed by hand and rewritten to read results one frame later instead.
2. `Renderer.isVisible` was meant to count the objects the engine draws. With the GPU Resident Drawer on it reported 372 of 372 objects in every variant, also with culling on, so it is useless here. The instance count from `UnityStats` and the top view with the player's culling matrix are used instead, and they agree with my own 6-plane count (about 99 objects in view).
3. The corridor texture is stored as RGB 8 bit, a format Direct3D 12 cannot create as a separate texture, so a plain `Texture2D` copy failed. The blit into an RGBA render texture works.
4. `UnityStats.batches` does not exist in this Unity version; `instances` is recorded instead.

## Honest caveats

- Measured in the editor in batch mode, not in a built player. Absolute numbers will differ in a build. The ratios between the variants are the useful part.
- "Neither" is a constructed worst case. Nobody would ship with culling off. It is there to show what each technique saves.
- `UnityStats` counts triangles before back-face culling, and the stats are read in the frame after the one they describe. Over 360 frames of a slow turn this makes no difference to the averages.
- The frame time includes everything the editor does per frame. Below about 1.5 ms that overhead hides part of the GPU saving (see LOD only against Both).
- Run 2 of "LOD only" (1.46 ms against 1.25 and 1.23) and run 2 of the mip test are outliers, probably background work on the PC. They are kept in the means and visible in `results.csv`.
- The GPU time was not read from a GPU timer. That the heavy variants are GPU bound is inferred from the frame time following the triangle count.
