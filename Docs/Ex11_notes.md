# Exercise 11: 3D mesh analysis of the Nisse scan (Python)

Built with Claude Code.

Script: `Docs/Ex11_mesh_analysis.py`. Libraries: trimesh 5.1.1 for topology and PyVista 0.49.1 (VTK) for decimation, curvature and off-screen rendering, on Python 3.14.7. Both installed from wheels with pip, no venv needed. Run: `python Ex11_mesh_analysis.py model.glb`. Figures are in `Captures/Ex11/`.

## Preprocessing
The glb has 145,922 vertices because vertices are duplicated along UV seams. Welding by position gives 126,286 vertices and the same 252,265 faces. Topology is only meaningful after welding.

## Analysis 1: quadric decimation (vtkQuadricDecimation)
| Reduction | Triangles | Vertices | PLY size | Mean error, % of bbox diagonal |
|---|---|---|---|---|
| 0 | 252,265 | 126,286 | 6.31 MB | 0 |
| 50% | 126,132 | 63,161 | 3.16 MB | 0.0035 |
| 90% | 25,225 | 12,658 | 0.63 MB | 0.0151 |
| 98% | 5,044 | 2,545 | 0.13 MB | 0.0477 |
| 99.5% | 1,260 | 639 | 0.03 MB | 0.1309 |

Error is the mean distance from a tenth of the original vertices to the decimated surface. Visually, 90% is nearly indistinguishable; the beard and hat tip lose detail at 98%; 99.5% is blocky. For a real-time asset 5k to 25k triangles with the texture and normal map carrying detail is a sensible target. See `decimation_montage.png`.

## Analysis 2: mesh quality
- Watertight: no. 325 boundary edges, forming one boundary loop by connected-component count (the red markers appear at the base features; this loop count is a rough estimate, treat as uncertain).
- Non-manifold edges: 0. Winding consistent: yes. Connected components: 1. Euler number -9.
- Degenerate faces: 0, but a minimum angle of 0.24 degrees, i.e. some sliver triangles, typical for photogrammetry.
- Surface area 0.7114 (glb units squared), bounding box 0.412 x 0.412 x 0.876.
- Mean curvature: median 9.5, 5th to 95th percentile -294 to 302. High curvature noise on the beard and base plate shows surface noise from reconstruction. Figures: `quality_curvature.png`, `quality_boundary.png`, `quality_histograms.png`.

## Terminology
- Vertex, edge, face; triangle mesh; manifold (each edge shared by exactly two faces); non-manifold edge (three or more); boundary edge (one face); hole = boundary loop; watertight = closed, no boundary edges.
- Euler characteristic V - E + F; genus.
- Winding order and face normals; vertex normals by averaging.
- Decimation (simplification): reduce triangle count while keeping shape. Quadric error metrics (Garland and Heckbert) collapse the edge that adds the least squared distance to the original planes.
- LOD (level of detail): swapping meshes by distance.
- Mean curvature: average of principal curvatures; sign shows convex versus concave.
- UV seams: why the vertex count in a file is higher than the geometric vertex count.
- Off-screen rendering: rendering without a window, used for reproducible figures.
