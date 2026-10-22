# Exercise 2 notes: scanning Nisse, photogrammetry mesh against Gaussian splat

Built with Claude Code. Scene: `Assets/Scenes/Ex02_Capture.unity` (mesh on the left, splat on the right). Evidence: `Captures/Ex02/side_by_side.png` (final, D3D12), `side_by_side_d3d11_black.png` (the failure), `nisse_mesh_a.png`, `nisse_mesh_b.png`.

## Data and pipeline
Source: 300 photos of the Nisse figure. Two reconstructions came from them.
1. Photogrammetry mesh: exported as `model.glb` with an 8192 x 8192 diffuse texture and a normal map. Converted to OBJ (positions, UVs with v flipped for the OBJ convention, normals) so Unity imports it without a plugin. Prefab `Assets/Nisse/Nisse.prefab`, scaled to 1 unit tall with the pivot at the base, URP Lit material with the texture.
2. Gaussian splat: trained with the Brush trainer (SfM poses via COLMAP, then optimisation), exported as `Nisse.ply` (Brush, SH degree 3). Imported with the aras-p UnityGaussianSplatting package (`Tools > Gaussian Splats > Create GaussianSplatAsset`, called by script through reflection, quality Medium). Its URP renderer feature was added to `Assets/Settings/PC_Renderer.asset`.

## Comparison facts
| | Mesh | Splat |
|---|---|---|
| Primitive | 252,265 triangles, 145,922 vertices in the glb (146,860 after Unity import, UV seams) | 2,493,932 Gaussians |
| Appearance data | one diffuse texture 8192 x 8192 (Unity max size set to 4096), 13.3 MB jpeg | position, scale, rotation, opacity, colour and degree 3 spherical harmonics per Gaussian |
| Size on disk | OBJ 26 MB, texture 14 MB | PLY 561 MB, Unity asset 115 MB (Medium quality, compressed formats) |
| Lighting | real shading by Unity lights, casts and receives shadows | view dependent colour baked in, no real lighting or shadows |
| Edits | normal mesh tools | hard to edit, cutouts and selection tools only |

Visible differences: the mesh is clean, has a closed flat shape, and the beard area is ragged where photogrammetry failed on fine fibres. The splat keeps the soft beard and fabric detail and reflections, in the raw export it also contained the whole room (stool, table, floating blobs); see the isolation section below. 

## What to be able to explain
Photogrammetry: feature detection and matching, structure from motion (camera poses and sparse points), multi-view stereo (dense points), meshing, texture baking. Gaussian splatting: each point is an anisotropic 3D Gaussian with opacity and spherical harmonic colour, trained by differentiable rasterisation against the photos, rendered by sorting splats by depth and alpha blending in a compute shader. Trade offs: mesh is usable in game engines (collision, lighting, animation) and small; splat gives photoreal view dependent look but is heavy, noisy at the edges and not relightable. Terms: UV mapping, normal map, vertex normals, triangle count, LOD, spherical harmonics, alpha blending, floaters, COLMAP.

## Black screen in Play mode: root cause and fix
Symptom: Play in the editor showed a completely black Game view, mesh and background included.
Cause: the project listed Direct3D 11 first for Windows, so the editor started on D3D11 (log line `Version:  Direct3D 11.0 [level 11.1]`). The package compute shader `SplatUtilities.compute` declares `#pragma require wavebasic` and `waveballot` for its radix sort. Wave intrinsics need shader model 6, which Unity offers only on D3D12 and Vulkan. On D3D11 those kernels are not compiled, so the first call fails with `IndexOutOfRangeException: Invalid kernelIndex (0) passed, must be non-negative less than 19` (711 times in the log). The exception is thrown inside the URP render graph pass of `GaussianSplatURPFeature`, which aborts the whole camera render, which is why even the mesh disappears. My earlier renders used `-force-d3d12` and hid this.
Fix: `ProjectSettings.asset` now lists Direct3D 12 then Vulkan for Windows, with automatic selection off (`m_APIs: 1200000015000000i`, ids 18 and 21). The editor reads this only at start, so Unity must be restarted once. Check: Edit, Project Settings, Player, Other Settings, Auto Graphics API for Windows is unticked and Direct3D12 is first. Verified headless without any API flag: log says `Direct3D 12 [level 12.2]`, no exceptions, scene renders. Forcing `-force-d3d11` reproduces the black image and the kernelIndex errors (`Captures/Ex02/side_by_side_d3d11_black.png`).

## Isolating Nisse with the COLMAP data
Folder `colmap/sparse/0` in the Downloads folder holds text models: `cameras.txt` (285 pinhole cameras with their own intrinsics), `images.txt` (285 registered images, 574 lines, pose as quaternion plus translation, world to camera), `points3D.txt` (127,015 sparse points). Script: `Tools/nisse_colmap.py`.
1. Camera centres are C = -R^T t and optical axes R^T z. The least squares point closest to all 285 axes is (-0.0378, 0.2860, 0.7607): where the cameras look, which is Nisse. Cameras sit on a ring about 0.8 units from it.
2. COLMAP y points down, so the mean camera up vector (R^T (0,-1,0)) gives world up: (0.0380, -0.9992, -0.0119). It is tilted 2.3 degrees from the y axis.
3. The Brush .ply uses the same frame as the COLMAP model (the splat was initialised from and trained against these poses). Checked by plotting sparse points and splats in the same side view: the silhouettes coincide.
4. A side view density plot shows the stool top at 0.29 units below the convergence point (height -0.29 along up). Nisse stands on it, with base at -0.265, tip of the hat at +0.543, so the figure is 0.8082 splat units tall. The dense core axis is at (a, b) = (0.0081, -0.0036) in the plane perpendicular to up.
5. Crop volume: cylinder of radius 0.30 around that axis, heights -0.265 to 0.60, opacity above 0.05, Gaussian size below 0.08. This keeps 334,570 of 2,493,932 Gaussians. The crop is baked into a new PLY (`Nisse_crop.ply`, next to the original) and imported as asset `Assets/GaussianAssets/Nisse_crop.asset` (Medium, about 16 MB), so it does not depend on runtime cutout objects. Floor, stool, table and floaters are gone.
6. Scale: 1 / 0.8082 = 1.2373 so Nisse is 1 unit tall like the mesh.
7. Handedness: COLMAP data is right handed, Unity is left handed, and the package does not convert. Without a flip the splat was a mirror image (hat leaned the wrong way compared with the mesh). The splat object therefore has a negative x scale, which the package shader supports.
Final transform of `Nisse_Splat`: position (0.653, 0.689, -0.935), euler (0.68, 14.71, 177.82), scale (-1.2373, 1.2373, 1.2373). These put the base centre of the figure at (0.8, 0, 0), standing upright, facing the camera like the mesh (yaw of -20 degrees chosen by looking at renders). Camera at (0, 0.55, -2.3) looking at (0, 0.5, 0) sees both. Not verified: pressing Play in the student's own editor.

## Terms for the exam
Structure from Motion (SfM): from overlapping photos find features, match them and solve for camera poses and 3D points. Camera poses: rotation and translation per image (extrinsics), plus intrinsics such as focal length. Sparse point cloud: the 3D points triangulated from matched features. Bundle adjustment: joint nonlinear least squares refinement of all poses, intrinsics and points to minimise reprojection error. 3D Gaussian splatting initialises one Gaussian per sparse point and optimises them against the photos. Each Gaussian has a position, a covariance (built from scale and rotation, Sigma = R S S^T R^T, so it is always positive semi definite), opacity and spherical harmonic coefficients (degree 3 here) for view dependent colour. Rendering is rasterisation: project each 3D Gaussian to a 2D ellipse through the Jacobian of the projection, sort by depth, alpha blend front to back, all in real time. NeRF instead ray marches: for every pixel sample many points along a ray, query a neural network for density and colour, and integrate; it is smooth and compact but slow to render, while splats are explicit, fast and editable. The mesh route (multi view stereo, meshing, texture baking) gives a lit, collidable asset.
