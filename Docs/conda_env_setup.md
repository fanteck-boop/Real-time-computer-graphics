# Conda environment for the 3D analysis exercise

## What it is
`rtcg3d` is a separate conda environment holding the libraries from the 3D Analysis and Statistics lecture, so they stay apart from the system Python (which has the Exercise 11 setup of trimesh and PyVista) and from the conda base environment. It uses Python 3.11 and the conda-forge channel only. Python 3.11 was chosen because Open3D lags behind the newest Python releases; every library below installed from conda-forge on 3.11 without pip.

## Activate
Anaconda Prompt: `conda activate rtcg3d`

Plain cmd: `C:\Users\trist\anaconda3\Scripts\activate.bat rtcg3d`

conda is not on PATH and `conda init` was not run, so a normal PowerShell or cmd window does not know the `conda` command. Without activating, call it by full path:

`C:\Users\trist\anaconda3\Scripts\conda.exe run -n rtcg3d python script.py`

Or call the interpreter directly: `C:\Users\trist\anaconda3\envs\rtcg3d\python.exe script.py`

## VS Code
Open the command palette, choose "Python: Select Interpreter", and pick `rtcg3d` (path `C:\Users\trist\anaconda3\envs\rtcg3d\python.exe`; use "Enter interpreter path" if it is not listed). For notebooks, pick the `rtcg3d` kernel in the top right; ipykernel is installed.

## Recreate or remove
Recreate from the exported file: `conda env create -f Tools/rtcg3d_environment.yml`. The file lists conda-forge only; add `--override-channels -c conda-forge` if conda asks about the defaults channel terms.

Remove: `conda env remove -n rtcg3d`

## Installed versions
Python 3.11.18, NumPy 2.4.6, SciPy 1.17.1, Matplotlib 3.11.2, OpenCV (cv2) 5.1.0, scikit-learn 1.9.1, trimesh 5.2.1, PyVista 0.49.2, vedo 2026.6.3, Open3D 0.20.0, ipykernel.

## What was verified
All libraries import. On Assets/Nisse/Nisse.obj (145,922 vertices, 252,265 triangles, same counts in every library):
- Open3D: loaded, `simplify_quadric_decimation` down to 25,000 triangles (it warns that UVs are not handled).
- trimesh: loaded, `is_watertight` is False, consistent with the Exercise 11 finding.
- SciPy `cKDTree`: 5 nearest neighbours of a vertex returned.
- PyVista: loaded and an off-screen screenshot rendered.
- vedo: loaded and an off-screen screenshot rendered.
No failures. Not installed: Polyscope, PlotOptiX, pyrender, pyntcloud (listed on the lecture slides as alternatives; PlotOptiX needs an Nvidia card). The Medium articles by the lecturer could not be fetched (HTTP 403), so nothing here is taken from them; the lecture slides name Part 1 and Part 2 of "Python Libraries for Mesh, Point Cloud, and Data Visualization" as the setup guides.

## Lecture concepts to be able to explain
**KDTree.** A binary tree that splits the points by one coordinate at a time, alternating X, Y, Z, until each cell holds few points. A nearest-neighbour query walks down from the root choosing left or right by comparing coordinates, then backs up the tree to check that no other branch holds a closer point. It replaces comparing against every point in a list.

**Octree.** Splits space into eight cubes at each level (a quadtree splits 2D space into four). Good for sparse 3D data, voxelization and spatial queries.

**Delaunay triangulation and Bowyer-Watson.** Triangulates a point set so no point lies inside any triangle's circumcircle. Bowyer-Watson starts with a huge triangle around all points, then inserts points one by one: triangles whose circumcircle contains the new point are marked bad, removed, and the resulting polygonal hole is reconnected to the new point. At the end, triangles touching the big outer triangle are deleted. Works best on planar or gently varying point sets.

**Ball pivoting.** Surface reconstruction for point clouds. A ball of fixed radius rests on three points, then pivots around an edge of that triangle until it hits another point, creating a new triangle each time. It stops where the ball would fall through (gaps too large or angles too steep). Starting with a larger radius and shrinking it captures finer detail.

**Alpha shapes.** A tight version of the convex hull built from a simplified Delaunay triangulation; the alpha parameter controls how tightly the shape wraps the points. The convex hull itself is simple and gives bounds.

**Poisson reconstruction.** Computes an implicit surface that fits the points and their normal directions, then extracts a watertight mesh from it. Complex, and needs oriented normals.

**Mesh quality metrics.** Per-face measures used to judge a mesh for games, visualization or simulation, and to guide smoothing, noise removal or cropping. The slides cover aspect ratio, Jacobian ratio (shape against an ideal element), warping factor (ideal 0, for quads), parallel deviation (ideal 0 for a square), maximum corner angle (ideal 60 degrees for a triangle, 90 for a quad) and skewness (closeness to equilateral or equiangular). They can be averaged or visualized per face.

**Smoothing.** Laplacian smoothing moves each vertex toward the average position of its neighbours, repeated for several iterations. It removes noise but shrinks the mesh; moving along the vertex normal limits shrinkage and warping.

**Simplification.** Reduces triangle count while keeping the overall shape, for real-time use. Vertex clustering divides space into a grid of cubes, maps all vertices in a cube to one representative and drops degenerate triangles. Vertex removal deletes a vertex and retriangulates the hole. Edge collapse merges two close vertices into one, and half-edge collapse removes one vertex and redirects its edges to the other.

## Exercise text
Choose one or more libraries, look through their examples, and implement something with your duck mesh: a visualization, animation, interactive application or processing that shows the mesh in a new way. Suggestions on the slide include KDTree or Octree analysis with colored regions, marching cubes, a raytraced animation, lighting animation, cutting the mesh with primitives, color space visualization, mesh quality metrics, PCA of the vertices with eigenvectors, Difference of Normals features, and voxelizing the mesh and animating the voxels.

Built with Claude Code
