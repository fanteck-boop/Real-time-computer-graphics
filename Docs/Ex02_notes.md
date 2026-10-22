# Exercise 2 notes: scanning Nisse, photogrammetry mesh against Gaussian splat

Built with Claude Code. Scene: `Assets/Scenes/Ex02_Capture.unity` (mesh on the left, splat on the right). Evidence: `Captures/Ex02/side_by_side.png`, `Captures/Ex02/nisse_mesh_a.png`, `nisse_mesh_b.png`.

## Data and pipeline
Source: 300 photos of the Nisse figure. Two reconstructions came from them.
1. Photogrammetry mesh: exported as `model.glb` with an 8192 x 8192 diffuse texture and a normal map. Converted to OBJ (positions, UVs with v flipped for the OBJ convention, normals) so Unity imports it without a plugin. Prefab `Assets/Nisse/Nisse.prefab`, scaled to 1 unit tall with the pivot at the base, URP Lit material with the texture.
2. Gaussian splat: trained with the Brush trainer (SfM poses via COLMAP, then optimisation), exported as `Nisse.ply` (Brush, SH degree 3). Imported with the aras-p UnityGaussianSplatting package (`Tools > Gaussian Splats > Create GaussianSplatAsset`, called by script through reflection, quality Medium). The package needs D3D12 or Vulkan; the render was made with `-force-d3d12`. Its URP renderer feature was added to `Assets/Settings/PC_Renderer.asset`.

## Comparison facts
| | Mesh | Splat |
|---|---|---|
| Primitive | 252,265 triangles, 145,922 vertices in the glb (146,860 after Unity import, UV seams) | 2,493,932 Gaussians |
| Appearance data | one diffuse texture 8192 x 8192 (Unity max size set to 4096), 13.3 MB jpeg | position, scale, rotation, opacity, colour and degree 3 spherical harmonics per Gaussian |
| Size on disk | OBJ 26 MB, texture 14 MB | PLY 561 MB, Unity asset 115 MB (Medium quality, compressed formats) |
| Lighting | real shading by Unity lights, casts and receives shadows | view dependent colour baked in, no real lighting or shadows |
| Edits | normal mesh tools | hard to edit, cutouts and selection tools only |

Visible differences: the mesh is clean, has a closed flat shape, and the beard area is ragged where photogrammetry failed on fine fibres. The splat keeps the soft beard and fabric detail and reflections, but contains the whole room: stool, table surface and floating blobs around Nisse. A box cutout is set on the renderer but did not remove the stool in the render, so the student should adjust it in the Inspector (the cutout is the child object NisseCutout). The splat file is in COLMAP space with arbitrary scale and y pointing down, so the scene rotates it 180 degrees and scales the Nisse core to 1 unit (core found by a density analysis of the .ply, the full bounds are about 900 units because of floaters).

## What to be able to explain
Photogrammetry: feature detection and matching, structure from motion (camera poses and sparse points), multi-view stereo (dense points), meshing, texture baking. Gaussian splatting: each point is an anisotropic 3D Gaussian with opacity and spherical harmonic colour, trained by differentiable rasterisation against the photos, rendered by sorting splats by depth and alpha blending in a compute shader. Trade offs: mesh is usable in game engines (collision, lighting, animation) and small; splat gives photoreal view dependent look but is heavy, noisy at the edges and not relightable. Terms: UV mapping, normal map, vertex normals, triangle count, LOD, spherical harmonics, alpha blending, floaters, COLMAP.
