# Exercise 11: 3D mesh analysis of the Nisse scan (Python)

Built with Claude Code.

The Nisse scan was later replaced by a cleaned version (178,672 triangles, base plate removed). Everything below is from rerunning the same script on the new glb. The first run, on the old mesh, had 252,265 triangles, 325 boundary edges and 1 connected component.

Script: `Docs/Ex11_mesh_analysis.py`. Libraries: trimesh 5.1.1 for topology and PyVista 0.49.1 (VTK) for decimation, curvature and off-screen rendering, on Python 3.14.7. Both installed from wheels with pip, no venv needed. Run: `python Ex11_mesh_analysis.py model.glb`. Figures are in `Captures/Ex11/`.

## Preprocessing
The glb has 105,215 vertices because vertices are duplicated along UV seams. Welding by position gives 89,799 vertices and 178,534 faces (the glb has 105,215 vertices and 178,672 faces; 138 degenerate faces are dropped when welding). Topology is only meaningful after welding.

## Analysis 1: quadric decimation (vtkQuadricDecimation)
| Reduction | Triangles | Vertices | PLY size | Mean error, % of bbox diagonal |
|---|---|---|---|---|
| 0 | 178,534 | 89,799 | 4.48 MB | 0 |
| 50% | 89,266 | 44,698 | 2.23 MB | 0.0037 |
| 90% | 17,853 | 8,945 | 0.45 MB | 0.0167 |
| 98% | 3,570 | 1,791 | 0.09 MB | 0.0496 |
| 99.5% | 891 | 447 | 0.02 MB | 0.1441 |

Error is the mean distance from a tenth of the original vertices to the decimated surface. Visually, 90% is nearly indistinguishable; the beard and hat tip lose detail at 98%; 99.5% is blocky. The visual remarks above are from the first run on the old mesh and I did not re-inspect every montage tile for the new one. For a real-time asset 3.5k to 18k triangles with the texture and normal map carrying detail is a sensible target. See `decimation_montage.png`.

## Analysis 2: mesh quality
- Watertight: no. 1,078 boundary edges, about 3 boundary loops (loop count is a rough estimate, treat as uncertain). Almost all of the boundary is one open rim at the bottom of the boots, where the base plate was cut away: 1,072 of the boundary vertices lie in the lowest eighth of the height, 6 at the hat tip.
- Non-manifold edges: 0. Winding consistent: yes. Connected components: 2. The main one has 178,507 faces. The other is a 27-face speck about 2.6 mm across (in glb units 0.0026) floating near the boots, leftover from the scan. Euler number -7.
- Degenerate faces (area below 1e-12): 0, but a minimum angle of 0.002 degrees, i.e. some sliver triangles, typical for photogrammetry.
- Surface area 0.4765 (glb units squared), bounding box 0.395 x 0.238 x 0.825 (width, depth, height, after rotating to Z-up). The old mesh was 0.412 x 0.412 x 0.876, so removing the base plate shrank the footprint and the height.
- Mean curvature: median 9.71, 5th to 95th percentile -291 to 297. High curvature noise on the beard shows surface noise from reconstruction. Figures: `quality_curvature.png`, `quality_boundary.png`, `quality_histograms.png`.

## Terminology
- Vertex, edge, face; triangle mesh; manifold (each edge shared by exactly two faces); non-manifold edge (three or more); boundary edge (one face); hole = boundary loop; watertight = closed, no boundary edges.
- Euler characteristic V - E + F; genus.
- Winding order and face normals; vertex normals by averaging.
- Decimation (simplification): reduce triangle count while keeping shape. Quadric error metrics (Garland and Heckbert) collapse the edge that adds the least squared distance to the original planes.
- LOD (level of detail): swapping meshes by distance.
- Mean curvature: average of principal curvatures; sign shows convex versus concave.
- UV seams: why the vertex count in a file is higher than the geometric vertex count.
- Off-screen rendering: rendering without a window, used for reproducible figures.
