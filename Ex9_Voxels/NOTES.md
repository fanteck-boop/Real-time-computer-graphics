# Exercise 9 notes: Voxel Nisse

## The idea

The Nisse scan is a smooth textured mesh. Here he is rebuilt from 11,436 small cubes, each coloured from the scan texture, so he looks like a figure built from coloured blocks. The cubes are not just a static picture. When you click on him, the program asks a KD-tree which cubes lie within a radius of the click, and those cubes are blown outwards, spin, and spring back into place. Pressing B scatters every cube into a cloud and then rebuilds him in a wave that starts at the boots and travels up to the tip of the hat. The direction of that wave is his main axis, found with PCA.

The point of it is to look at the mesh as data. The cubes show how coarse a voxel version of the scan is. The KD-tree view shows what a neighbourhood query actually returns. The PCA arrows show which direction the shape spreads out in the most. The distance colouring shows the result of a query as a field over the whole figure.

## What I chose from the list

The assignment says to pick two things from the library examples and apply them to my own mesh. I picked:

1. **Voxelise the mesh and animate the voxels.** The surface is turned into cubes with trimesh, the cubes are coloured from the texture with my own NumPy code, and every cube is animated each frame (blast, spin, damped spring, scatter and rebuild).
2. **KD-tree neighbourhood analysis with coloured regions.** Every click is a radius query on a KD-tree built over the cube centres. The found neighbours can be shown (sphere plus highlighted cubes), the timing is compared with brute force, and the cubes can be coloured by their distance from the clicked point.

PCA of the cube centres with the eigenvectors drawn as arrows is a small extra. It is also used for something: it decides the order of the rebuild.

The result is an animation and an interactive application at the same time.

## Link to the lecture articles

The course material lists Ivan Nikolov's articles. I only know their titles and which libraries they cover, I have not read them, and nothing here is copied from them.

- The voxel part relates to "How to Voxelize Meshes and Point Clouds in Python" (Open3D, Trimesh, PyVista, pyntcloud). I used trimesh for the voxelisation because it loads the OBJ with its UVs and gives the surface voxels directly.
- The neighbour part relates to "Neighborhood Analysis, KD-Trees, and Octrees for Meshes and Point Clouds in Python" (Open3D, PyVista, Vedo). I did not use those libraries' own KD-trees. The radius query is done by SciPy's `cKDTree`.
- The two overview articles on Python 3D visualisation libraries are where PyVista comes from as the viewer.

Which library does which job:

| Job | Library |
|---|---|
| Load the OBJ with its UVs, surface voxelisation (`mesh.voxelized`) | trimesh |
| Load and shrink the 8192 px texture | Pillow |
| Colour each voxel from the texture (surface sampling, UV lookup, averaging per voxel) | my own NumPy code |
| Radius query and nearest-neighbour fill-in | SciPy `cKDTree` |
| Brute-force comparison, PCA (covariance and `eigh`), spring physics, cube rotation | NumPy |
| Window, picking, timer, text, screenshots, video | PyVista / VTK |
| Distance colormap (inferno) | Matplotlib |
| Writing the MP4 | imageio-ffmpeg (through PyVista) |

## Environment

A new conda environment `rtcg_ex9`, Python 3.11.18 from conda-forge, libraries from PyPI with pip:

PyVista 0.49.3, VTK 9.7.2, SciPy 1.17.1, NumPy 2.4.6, trimesh 5.2.2, Pillow 13.0.0, Matplotlib 3.11.2, imageio 2.38.2, imageio-ffmpeg 0.8.1.

`environment.yml` holds the same list. The other environments (`base`, `rtcg3d`) were not touched.

## Theory

### Surface voxelisation

A voxel grid splits space into equal cubes of edge length *pitch*. Voxelising a mesh means marking the cubes the mesh occupies. There are two kinds. A surface voxelisation marks only the cubes the triangles pass through, so you get a hollow shell. A solid voxelisation also fills the inside, which needs to know what is inside, and that only works when the mesh is closed (watertight). The Nisse scan is not watertight (trimesh reports `is_watertight = False`), so filling would be unreliable. I only use the surface. trimesh does this by splitting the triangles until no edge is longer than the pitch and then marking the cell of every resulting vertex.

The cubes need colours, and a voxel has no UV of its own. My solution: scatter many random points over the surface (40 per voxel, 457,440 in total), each with its UV interpolated from its triangle, look up the texture colour of each point, and average the colours of all the points that fall inside the same voxel. Points are spread by triangle area so big triangles get more of them. A point inside a triangle is made with two random numbers and the square-root trick, which gives an even spread. The average acts like a small blur over the voxel, which is what you want when a 2048 px texture is squeezed into a cube 1/111 of his height. A voxel that got no sample (it can happen at thin parts) takes the colour of the nearest coloured voxel.

### KD-tree radius query

A KD-tree is a binary tree over points. The root splits all points into two halves along one axis, each half is split along the next axis, and so on until each leaf holds only a few points. Every node therefore covers a box of space. A radius query (all points within distance *r* of *p*) walks down from the root and skips every branch whose box is further than *r* from *p*. Only the leaves near the sphere are opened and their points tested. Brute force tests every point.

Building the tree costs time once (about 1.2 to 2.2 ms for the 11,436 voxel centres, 29 ms for the 105,188 mesh vertices). After that a query costs roughly the depth of the tree plus the number of points it returns, while brute force always costs the number of points in total.

Measured on this machine, median of 30 runs, query at the front of his chest:

| Points | Radius | Found | KD-tree | Brute force |
|---|---|---|---|---|
| 11,436 voxels | 0.04 | 101 | 0.017 ms | 0.203 ms |
| 11,436 voxels | 0.12 | 1,008 | 0.071 ms | 0.222 ms |
| 11,436 voxels | 0.40 | 10,094 | 0.397 ms | 0.195 ms |
| 105,188 vertices | 0.04 | 1,699 | 0.087 ms | 4.40 ms |
| 105,188 vertices | 0.12 | 14,812 | 0.540 ms | 4.02 ms |
| 105,188 vertices | 0.40 | 94,408 | 6.02 ms | 4.27 ms |

So the KD-tree wins clearly when the sphere is small compared with the object: 12 times faster on the voxels at r = 0.04 and 50 times on the full mesh. It loses when the sphere covers almost everything (r = 0.40 returns 88 to 90 percent of the points), because then it visits nearly every leaf and also has to build a Python list of the results, while NumPy's brute force is one vectorised pass. At this voxel count both are far below a millisecond, so for the app itself the difference does not matter for the frame rate. It would matter with many queries per frame or with the full-resolution mesh. The numbers jump a bit between runs (the default r = 0.12 query gave 0.04 to 0.07 ms in different runs), so read them as sizes, not exact values. The program checks that both methods return the same number of points.

### PCA eigenvectors

PCA finds the directions in which a point set spreads out the most. Take the cube centres, subtract their mean, and compute the 3x3 covariance matrix. Its eigenvectors are three perpendicular directions, and each eigenvalue is the variance of the points along its eigenvector. The eigenvector with the biggest eigenvalue (PC1) is the main axis of the shape.

For the Nisse, PC1 = (0.09, 0.99, -0.05) and explains 76 percent of the variance, PC2 15 percent, PC3 9 percent. PC1 is almost the Y axis, which confirms that height is along Y. It leans about 6 degrees because his hat bends to one side. The sign of an eigenvector is arbitrary, so the code flips PC1 to point up. Projecting every cube centre onto PC1 and scaling to 0..1 gives each cube a "height" that is 0 at the boots and 1 at the hat. The rebuild wave uses that value. The arrows drawn in the app are 3 standard deviations long (3 times the square root of the eigenvalue) in both directions.

### Damped spring

Every cube has an offset x from its home position and a velocity v. Each frame:

```
a = -k x - c v
v = v + a dt
x = x + v dt
```

The term -k x pulls the cube home (a spring), the term -c v removes energy (damping). Using the new velocity to update the position (semi-implicit Euler) keeps it stable where plain Euler would slowly gain energy. With k = 38 and c = 3.2 the natural frequency is sqrt(38) = 6.2 rad/s and the damping ratio is c / (2 sqrt(k)) = 0.26. That is underdamped: the cube overshoots its home a little and wobbles, with one wobble lasting about 1.05 s, and the swing shrinks like e^(-1.6 t), to 4 percent after 2 s. The spin angle uses the same spring. Because a blast only adds to the velocity, several blasts can overlap and simply add up. The time step is the real time between frames, capped at 1/20 s so a slow frame cannot make it unstable (the method is stable while dt times 6.2 is below 2).

Scatter and rebuild are not physics. They are scripted: each cube flies to a random target with a smooth ease (smoothstep), floats, and then flies home starting at a time given by its PCA height. Every cube needs 0.8 s to get home and the wave takes 2.2 s from boots to hat.

### Drawing many cubes fast

Making 11,436 separate actors, or rebuilding a glyph filter every frame, would be slow. Instead the program builds one PolyData that contains all the cubes (8 points and 6 quads per cube, 91,488 points in total) once. Each frame it computes the new corner positions with NumPy and writes them into the existing point array. VTK then only has to upload new positions. No normals are given, so VTK shades every quad flat, which gives the cubes hard edges. The rotation of each cube about its own random axis uses Rodrigues' formula, vectorised over all cubes.

## Parameters

| Name | Value | Meaning |
|---|---|---|
| Scaling | height 1.0 | The scan (0.825 units tall, far from the origin) is moved so the boots are at y = 0 and x, z are centred, then scaled to height 1 |
| `PITCH` | 0.009 | Voxel edge length, about 111 voxels from boots to hat |
| voxel count | 11,436 | Result of that pitch |
| `CUBE_FILL` | 0.88 | Drawn cube size relative to the pitch, leaves thin gaps |
| `SAMPLES_PER_VOXEL` | 40 | Surface samples per voxel for the colour average |
| `TEXTURE_REDUCE` | 4 | 8192 px texture shrunk to 2048 px in memory |
| `RADIUS` | 0.12 | Start blast radius |
| `RADIUS_STEP`, min, max | 0.02, 0.04, 0.40 | Up/Down keys |
| `SPRING_K` | 38 | Spring stiffness |
| `DAMPING` | 3.2 | Damping, ratio 0.26 |
| `BLAST_SPEED` | 2.6 | Outward speed at the blast centre, falls off as sqrt(1 - d/r), times a random 0.6 to 1.2 |
| upward kick | 0.35 | Added to the y of the blast direction before scaling |
| `SPIN_SPEED` | 22 rad/s | Largest spin a blast gives |
| `SCATTER_TIME`, `HOLD_TIME` | 0.9 s, 0.4 s | Fly out, then float |
| `SWEEP_TIME`, `FLIGHT_TIME` | 2.2 s, 0.8 s | Rebuild wave and per-cube flight |
| scatter distance | 0.12 to 0.5 plus noise 0.06 | Added to each cube, away from the point (0, 0.5, 0) |
| click threshold | 3 px | Press to release movement that still counts as a click |
| KD timing | median of 30 | Repeats per timing |
| PCA arrows | 3 standard deviations | Arrow length, cubes drawn at opacity 0.22 while shown |
| Window / video | 1280 x 720, 30 fps video, 13 s | |

## Measured performance

- Offscreen, 1280x720, with SSAA anti-aliasing, while a blast is running: 85 to 96 fps across runs (simulation, point update and render, 120 frames). Measured in `benchmark()`.
- In the window (`--selftest`): about 60 fps. The timer is set to 10 ms, so the limit is probably the Windows timer resolution or vsync rather than the work itself. I did not find out which.
- Startup: about 2.1 to 2.3 s for loading, voxelising and colouring.

## The code, part by part

The whole program is one file, `nisse_voxels.py`. Below is all of it, split into its sections.

### Header and imports

The docstring says what the program does and how to start it. Paths are built from the folder of the script, so it runs from anywhere.

```python
"""Exploding voxel Nisse.

Exercise 9 (Real-Time Computer Graphics, "Analysis of 3D objects").
The Nisse scan is turned into a few thousand small cubes coloured from his
texture. Click on him and a KD-tree finds the cubes near the click; they burst
out, spin and spring back. Press B to scatter him and rebuild him from the
boots up along his main PCA axis.

Run:            python nisse_voxels.py
Capture mode:   python nisse_voxels.py --capture     (offscreen, writes captures/)
Window test:    python nisse_voxels.py --selftest    (opens, clicks itself, closes)

Libraries: trimesh (load + surface voxels), SciPy cKDTree (radius queries),
PyVista/VTK (window, picking, animation), NumPy (everything else).
"""

import argparse
import time
from pathlib import Path

import numpy as np
import pyvista as pv
import trimesh
from matplotlib import colormaps
from PIL import Image
from scipy.spatial import cKDTree

HERE = Path(__file__).resolve().parent
MESH_PATH = HERE.parent / "Assets" / "Nisse" / "Nisse.obj"
TEXTURE_PATH = HERE.parent / "Assets" / "Nisse" / "Nisse_diffuse.jpeg"
CAPTURE_DIR = HERE / "captures"
```

### Parameters

All the numbers that change how it looks and moves are at the top, so they can be tuned without reading the rest.

```python
# ---------------------------------------------------------------- parameters
PITCH = 0.009            # voxel edge length (Nisse is scaled to height 1.0)
CUBE_FILL = 0.88         # cube size relative to the pitch, leaves small gaps
SAMPLES_PER_VOXEL = 40   # surface samples used to colour each voxel
TEXTURE_REDUCE = 4       # 8192 px texture -> 2048 px in memory

RADIUS = 0.12            # start blast radius
RADIUS_STEP = 0.02
RADIUS_MIN, RADIUS_MAX = 0.04, 0.40

SPRING_K = 38.0          # spring stiffness (1/s^2), pulls a cube home
DAMPING = 3.2            # velocity damping (1/s), zeta = 3.2 / (2*sqrt(38)) = 0.26
BLAST_SPEED = 2.6        # outward speed at the blast centre (units/s)
SPIN_SPEED = 22.0        # max angular speed given by a blast (rad/s)

SCATTER_TIME = 0.9       # seconds to fly out
HOLD_TIME = 0.4          # seconds floating before the rebuild starts
SWEEP_TIME = 2.2         # seconds for the rebuild wave to go boots -> hat
FLIGHT_TIME = 0.8        # seconds each cube needs to fly home

BG_TOP, BG_BOTTOM = "#1b2133", "#040509"
HIGHLIGHT = np.array([0.25, 1.0, 0.95])     # colour of KD-tree neighbours
```

### Loading and voxels

`load_nisse` loads the OBJ with `process=False` so trimesh does not merge or reorder vertices (that would break the link to the UVs), then moves and scales him. trimesh did not find a usable texture for this OBJ (it only made a 2x2 placeholder), so the JPEG is opened with Pillow. Pillow refuses images this big by default as a guard against "decompression bombs", hence `MAX_IMAGE_PIXELS = None`. `reduce(4)` shrinks it to 2048 px.

`sample_surface` is the random sampling described in the theory part. `einsum` applies the three barycentric weights to the three corners of every sampled triangle at once, both for positions and for UVs.

`texture_lookup` turns UVs into pixel positions. OBJ UVs have v = 0 at the bottom of the image, while image row 0 is the top, hence `1 - v`. I checked it on a render: hat red, beard white, boots red.

`voxelise` gets the surface voxels from trimesh, then finds which voxel every sample falls in. It gives each voxel cell one integer key, sorts the keys, and uses `searchsorted` to look up the key of every sample, which avoids a Python loop over 457,440 points. `bincount` with weights sums the colours per voxel and divides by the count.

```python
# ------------------------------------------------------------ loading + voxels
def load_nisse():
    """Load the scan, move boots to y=0, centre x/z, scale to height 1."""
    mesh = trimesh.load(MESH_PATH, process=False, force="mesh")
    lo, hi = mesh.bounds
    centre = np.array([(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2])
    mesh.apply_translation(-centre)
    mesh.apply_scale(1.0 / (hi[1] - lo[1]))
    Image.MAX_IMAGE_PIXELS = None          # the texture is 8192 x 8192
    texture = np.asarray(Image.open(TEXTURE_PATH).convert("RGB").reduce(TEXTURE_REDUCE))
    return mesh, texture


def sample_surface(mesh, n, rng):
    """Own code: n random points on the triangles (area weighted) with their UVs."""
    tris = mesh.faces
    p = mesh.area_faces / mesh.area_faces.sum()
    face = rng.choice(len(tris), size=n, p=p)
    r1, r2 = np.sqrt(rng.random(n)), rng.random(n)
    w = np.stack([1 - r1, r1 * (1 - r2), r1 * r2], axis=1)          # barycentric
    corners = tris[face]                                             # (n, 3)
    pts = np.einsum("nk,nkd->nd", w, mesh.vertices[corners])
    uvs = np.einsum("nk,nkd->nd", w, mesh.visual.uv[corners])
    return pts, uvs


def texture_lookup(texture, uv):
    """Nearest-pixel lookup. OBJ has v=0 at the bottom, images row 0 at the top."""
    h, w, _ = texture.shape
    x = np.clip((uv[:, 0] % 1.0) * (w - 1), 0, w - 1).astype(int)
    y = np.clip((1.0 - uv[:, 1] % 1.0) * (h - 1), 0, h - 1).astype(int)
    return texture[y, x] / 255.0


def voxelise(mesh, texture, pitch, seed=1):
    """Surface voxels from trimesh, coloured by averaging texture samples (own code)."""
    grid = mesh.voxelized(pitch=pitch)            # surface only, the scan is not watertight
    idx = grid.sparse_indices                     # (N, 3) integer cell indices
    origin = grid.transform[:3, 3]
    centres = origin + idx * pitch

    rng = np.random.default_rng(seed)
    pts, uvs = sample_surface(mesh, SAMPLES_PER_VOXEL * len(idx), rng)
    cols = texture_lookup(texture, uvs)

    # Give every cell one integer key, then find which voxel each sample falls in.
    dims = idx.max(axis=0) + 3
    key = lambda ijk: (ijk[:, 0] * dims[1] + ijk[:, 1]) * dims[2] + ijk[:, 2]
    vox_keys = key(idx)
    order = np.argsort(vox_keys)
    s_idx = np.round((pts - origin) / pitch).astype(int)
    s_keys = key(np.clip(s_idx, 0, dims - 1))
    pos = np.searchsorted(vox_keys[order], s_keys).clip(0, len(idx) - 1)
    hit = vox_keys[order][pos] == s_keys
    owner = order[pos[hit]]

    counts = np.bincount(owner, minlength=len(idx))
    colours = np.stack([np.bincount(owner, cols[hit, c], len(idx)) for c in range(3)], 1)
    have = counts > 0
    colours[have] /= counts[have, None]
    if (~have).any():                             # rare: borrow the nearest coloured voxel
        _, near = cKDTree(centres[have]).query(centres[~have])
        colours[~have] = colours[have][near]
    return centres, colours
```

### Analysis

`pca` is the covariance and eigenvector computation. `numpy.linalg.eigh` is for symmetric matrices and returns the eigenvalues smallest first, so they are reversed.

`timed` runs a function 30 times and returns the median time, which is less sensitive to one slow run than the mean. `Neighbourhood` holds the `cKDTree` and runs every query twice, once through the tree and once by brute force, so the numbers on screen are real measurements of the same query.

```python
# --------------------------------------------------------------- analysis
def pca(points):
    """Eigenvectors of the covariance matrix, largest first, PC1 pointing up."""
    mean = points.mean(axis=0)
    vals, vecs = np.linalg.eigh(np.cov((points - mean).T))
    vals, vecs = vals[::-1], vecs[:, ::-1].copy()
    if vecs[1, 0] < 0:
        vecs[:, 0] *= -1
    return mean, vals, vecs


def timed(fn, repeats=30):
    """Median run time of fn() in milliseconds, plus its last result."""
    times = []
    for _ in range(repeats):
        t = time.perf_counter()
        out = fn()
        times.append(time.perf_counter() - t)
    return 1000 * float(np.median(times)), out


class Neighbourhood:
    """KD-tree over the voxel rest positions, with a brute-force check for comparison."""

    def __init__(self, centres):
        self.centres = centres
        self.build_ms, self.tree = timed(lambda: cKDTree(centres), repeats=5)

    def query(self, point, r):
        kd_ms, idx = timed(lambda: np.array(self.tree.query_ball_point(point, r), dtype=int))
        bf_ms, bf = timed(lambda: np.flatnonzero(((self.centres - point) ** 2).sum(1) <= r * r))
        assert len(bf) == len(idx), "KD-tree and brute force disagree"
        dist = np.linalg.norm(self.centres[idx] - point, axis=1)
        return idx, dist, kd_ms, bf_ms
```

### Cube geometry

`build_cubes` makes the single PolyData with all cubes. A VTK face list is a flat list where every face starts with its number of corners, so each quad is stored as `4, a, b, c, d`. The cube corners are the 8 combinations of plus and minus one, scaled by half the cube size.

`cube_points` rotates every cube's corners about its own axis with Rodrigues' formula (v cos a + (k x v) sin a + k (k . v)(1 - cos a)) and then moves them to the cube's current position. All arrays have shape (cubes, 8, 3), so it is one NumPy expression for all 91,488 points.

```python
# --------------------------------------------------------------- cube geometry
CORNERS = np.array([[x, y, z] for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)], float)
QUADS = np.array([[0, 1, 3, 2], [4, 6, 7, 5], [0, 4, 5, 1],
                  [2, 3, 7, 6], [0, 2, 6, 4], [1, 5, 7, 3]])


def build_cubes(n, half):
    """One PolyData holding n cubes: 8 points and 6 quads each. Points are moved later."""
    faces = (QUADS[None] + 8 * np.arange(n)[:, None, None]).reshape(-1, 4)
    faces = np.hstack([np.full((len(faces), 1), 4), faces]).ravel()
    poly = pv.PolyData(np.zeros((8 * n, 3)), faces)
    poly.point_data["rgb"] = np.zeros((8 * n, 3), np.uint8)
    return poly, CORNERS * half


def cube_points(centres, corners, axes, angles):
    """Rotate every cube's corners about its own axis (Rodrigues) and move it."""
    c, s = np.cos(angles)[:, None, None], np.sin(angles)[:, None, None]
    k = axes[:, None, :]
    v = corners[None]
    rot = v * c + np.cross(k, v) * s + k * (v * k).sum(-1, keepdims=True) * (1 - c)
    return (rot + centres[:, None, :]).reshape(-1, 3)
```

### Animation

`VoxelMotion` keeps the state of every cube: offset, velocity, spin angle and spin speed, plus a fixed random rotation axis. `blast` adds velocity to the cubes the KD-tree found, pointing away from the click, strongest at the centre. `scatter` picks a random target for each cube and starts the scripted sequence. `step` runs either the spring or the scatter and rebuild script. When the rebuild is finished it resets to spring mode.

```python
# --------------------------------------------------------------- animation
def smooth(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3 - 2 * t)


class VoxelMotion:
    """Offset, velocity and spin of every cube. Rest position + offset = drawn position."""

    def __init__(self, centres, height, seed=2):
        n = len(centres)
        self.rng = np.random.default_rng(seed)
        self.rest = centres
        self.height = height                     # 0 at the boots, 1 at the hat (PCA)
        self.axes = self.rng.normal(size=(n, 3))
        self.axes /= np.linalg.norm(self.axes, axis=1, keepdims=True)
        self.reset()

    def reset(self):
        n = len(self.rest)
        self.offset = np.zeros((n, 3))
        self.vel = np.zeros((n, 3))
        self.angle = np.zeros(n)
        self.spin = np.zeros(n)
        self.mode, self.clock = "spring", 0.0

    def blast(self, point, idx, dist, r):
        """Push the neighbours away from the point, stronger near the centre."""
        d = self.rest[idx] + self.offset[idx] - point
        d /= np.linalg.norm(d, axis=1, keepdims=True) + 1e-9
        d[:, 1] += 0.35                                       # a little upward kick
        strength = BLAST_SPEED * (1 - dist / r) ** 0.5 * self.rng.uniform(0.6, 1.2, len(idx))
        self.vel[idx] += d * strength[:, None]
        self.spin[idx] += self.rng.uniform(-1, 1, len(idx)) * SPIN_SPEED

    def scatter(self):
        """Start the scatter + rebuild sequence."""
        n = len(self.rest)
        out = self.rest - np.array([0, 0.5, 0])                # away from his middle
        out /= np.linalg.norm(out, axis=1, keepdims=True) + 1e-9
        self.target = out * self.rng.uniform(0.12, 0.5, (n, 1)) + self.rng.normal(0, 0.06, (n, 3))
        self.target_angle = self.rng.uniform(-6, 6, n)
        self.start_offset = self.offset.copy()
        self.start_angle = self.angle.copy()
        self.mode, self.clock = "scatter", 0.0

    def step(self, dt):
        if self.mode == "spring":
            # semi-implicit Euler on a damped spring: a = -k x - c v
            self.vel += (-SPRING_K * self.offset - DAMPING * self.vel) * dt
            self.offset += self.vel * dt
            self.spin += (-SPRING_K * self.angle - DAMPING * self.spin) * dt
            self.angle += self.spin * dt
            return
        self.clock += dt
        t = self.clock
        if t < SCATTER_TIME + HOLD_TIME:                    # fly out, then float
            u = float(smooth(t / SCATTER_TIME))
            drift = 0.04 * np.sin(3 * t + 10 * self.height)[:, None]
            self.offset = self.start_offset * (1 - u) + (self.target + drift) * u
            self.angle = self.start_angle * (1 - u) + (self.target_angle + 0.6 * t) * u
        else:                                                # rebuild boots -> hat
            t -= SCATTER_TIME + HOLD_TIME
            u = smooth((t - self.height * SWEEP_TIME) / FLIGHT_TIME)
            hold = 1 - u
            drift = 0.04 * np.sin(3 * (t + SCATTER_TIME + HOLD_TIME) + 10 * self.height)
            self.offset = (self.target + drift[:, None]) * hold[:, None]
            self.angle = (self.target_angle + 0.6 * (t + SCATTER_TIME + HOLD_TIME)) * hold
            if t > SWEEP_TIME + FLIGHT_TIME:
                self.reset()

    def rebuild_progress(self):
        if self.mode != "scatter":
            return 1.0
        t = self.clock - SCATTER_TIME - HOLD_TIME
        return float(np.clip(t / (SWEEP_TIME + FLIGHT_TIME), 0, 1))
```

### The scene

`NisseApp` ties it together and works the same in the window and offscreen. It builds the voxels, the KD-tree, PCA and the motion state, then the actors: three coloured lights (warm key, cool fill, rim), the cubes, a dark disc as floor, a wireframe sphere for the KD-tree view (one unit sphere that is moved and scaled instead of rebuilt), the PCA arrows with labels, and the help text.

`update_colours` picks texture or distance colours and, when the KD view is on, mixes the found neighbours towards cyan. Colours are stored per point as bytes, 8 equal values per cube.

`pick` turns a pixel into a cube: a `vtkCellPicker` restricted to the cube actor returns the quad under the pixel, and because every cube has 6 quads, quad number // 6 is the cube number. The blast is centred on that cube's home position, so the KD-tree query is always on the undisturbed shape even if the cube was flying when clicked.

`tick` is the timer callback in the window. It measures the real time since the last frame for the physics and for the fps display.

```python
# --------------------------------------------------------------- the scene
HELP = ("click (no drag): blast   SPACE: blast at cursor   B: scatter + rebuild\n"
        "UP/DOWN: radius   M: colour mode   K: KD-tree view   X: PCA axes   N: reset   Q: quit")


class NisseApp:
    """Owns the data, the actors and the reactions to input. Works on- and offscreen."""

    def __init__(self, plotter):
        self.pl = plotter
        t = time.perf_counter()
        self.mesh, self.texture = load_nisse()
        centres, self.base_colours = voxelise(self.mesh, self.texture, PITCH)
        self.voxel_s = time.perf_counter() - t
        self.n = len(centres)

        self.nb = Neighbourhood(centres)
        self.mean, self.eigvals, self.eigvecs = pca(centres)
        h = (centres - self.mean) @ self.eigvecs[:, 0]
        self.motion = VoxelMotion(centres, (h - h.min()) / np.ptp(h))

        self.radius = RADIUS
        self.colour_mode = "texture"
        self.show_kd = False
        self.last = None                  # result of the last KD-tree query
        self.fps = 0.0
        self._t_last = None
        self._build_scene()

    # ---- scene setup
    def _build_scene(self):
        pl = self.pl
        pl.set_background(BG_BOTTOM, top=BG_TOP)
        pl.remove_all_lights()
        for pos, col, inten in [((2.5, 3.0, 3.0), "#fff1dc", 0.95),    # warm key
                                ((-3.0, 1.5, 1.5), "#9fb6ff", 0.45),   # cool fill
                                ((0.0, 2.5, -3.5), "#ffffff", 0.60)]:  # rim
            pl.add_light(pv.Light(position=pos, focal_point=(0, 0.5, 0), color=col,
                                  intensity=inten, light_type="scene light"))

        self.poly, self.corners = build_cubes(self.n, PITCH * CUBE_FILL / 2)
        self.actor = pl.add_mesh(self.poly, scalars="rgb", rgb=True, ambient=0.18,
                                 diffuse=0.9, specular=0.25, specular_power=18)
        pl.add_mesh(pv.Disc(center=(0, -0.006, 0), inner=0, outer=0.5, normal=(0, 1, 0),
                            c_res=96), color="#0c0f18", ambient=0.4, diffuse=0.6)

        # KD-tree sphere: one unit sphere, moved and scaled instead of rebuilt
        self.sphere = pl.add_mesh(pv.Sphere(radius=1.0, theta_resolution=28, phi_resolution=18),
                                  color="#5ff7ea", style="wireframe", opacity=0.35,
                                  line_width=1, lighting=False)
        self.sphere.SetVisibility(False)

        # PCA eigenvectors as arrows from the centroid, length 3 standard deviations
        self.pca_actors = []
        for i, col in enumerate(["#ff5a5a", "#6dff7a", "#5aa8ff"]):
            length = 3.0 * np.sqrt(self.eigvals[i])
            for sign in (1, -1):
                arrow = pv.Arrow(start=self.mean, direction=sign * self.eigvecs[:, i],
                                 scale=length, tip_length=0.15, tip_radius=0.045,
                                 shaft_radius=0.016)
                self.pca_actors.append(pl.add_mesh(arrow, color=col, ambient=0.5))
        share = 100 * self.eigvals / self.eigvals.sum()
        tips = [self.mean + 3.2 * np.sqrt(self.eigvals[i]) * self.eigvecs[:, i] for i in range(3)]
        self.pca_actors.append(pl.add_point_labels(
            np.array(tips), [f"PC{i + 1} {share[i]:.0f}%" for i in range(3)], font_size=14,
            text_color="white", shape=None, show_points=False, always_visible=True))
        self.show_pca(False)

        self.hud = pl.add_text("", position="upper_left", font_size=10, color="#d9dfeb",
                               font="courier")
        self.update_colours()
        self.update_geometry()
        self.update_hud()

    # ---- drawing
    def update_geometry(self):
        m = self.motion
        self.poly.points[:] = cube_points(m.rest + m.offset, self.corners, m.axes, m.angle)
        self.poly.Modified()

    def update_colours(self):
        if self.colour_mode == "texture":
            col = self.base_colours.copy()
        else:  # distance from the last query point (or the centroid before any click)
            p = self.last["point"] if self.last else self.mean
            d = np.linalg.norm(self.motion.rest - p, axis=1)
            col = colormaps["inferno"](1 - d / d.max())[:, :3]
        if self.show_kd and self.last is not None:
            col[self.last["idx"]] = 0.25 * col[self.last["idx"]] + 0.75 * HIGHLIGHT
        self.poly.point_data["rgb"][:] = np.repeat((col * 255).astype(np.uint8), 8, axis=0)
        self.poly.Modified()

    def update_sphere(self):
        visible = self.show_kd and self.last is not None
        self.sphere.SetVisibility(visible)
        if visible:
            self.sphere.position = self.last["point"]
            self.sphere.scale = (self.radius,) * 3

    def show_pca(self, on):
        self.pca_on = on
        for a in self.pca_actors:
            a.SetVisibility(on)
        self.actor.prop.opacity = 0.22 if on else 1.0    # see the axes through him

    def update_hud(self, visible=True):
        q = self.last
        query = (f"last query: {len(q['idx'])} neighbours   KD-tree {q['kd_ms']:.3f} ms   "
                 f"brute force {q['bf_ms']:.3f} ms" if q else "last query: none yet, click on him")
        text = (f"VOXEL NISSE    {self.fps:4.0f} fps\n"
                f"voxels {self.n}   pitch {PITCH}   radius r = {self.radius:.2f}\n"
                f"{query}\n"
                f"KD-tree build {self.nb.build_ms:.1f} ms   PC1 explains "
                f"{100 * self.eigvals[0] / self.eigvals.sum():.0f}% of the variance\n"
                f"colour: {self.colour_mode}   KD view: {'on' if self.show_kd else 'off'}   "
                f"PCA axes: {'on' if self.pca_on else 'off'}\n\n{HELP}")
        self.hud.SetText(2, text if visible else "")

    # ---- actions
    def query(self, point):
        idx, dist, kd_ms, bf_ms = self.nb.query(point, self.radius)
        self.last = dict(point=np.asarray(point, float), idx=idx, dist=dist,
                         kd_ms=kd_ms, bf_ms=bf_ms)
        self.update_colours()
        self.update_sphere()

    def blast(self, point):
        self.query(point)
        self.motion.blast(self.last["point"], self.last["idx"], self.last["dist"], self.radius)

    def pick(self, x, y):
        """Display pixel -> cube under it -> blast at that cube's rest position."""
        picker = pv._vtk.vtkCellPicker()
        picker.SetTolerance(0.0005)
        picker.PickFromListOn()
        picker.AddPickList(self.actor)
        if picker.Pick(x, y, 0, self.pl.renderer) and picker.GetCellId() >= 0:
            voxel = picker.GetCellId() // 6          # 6 quads per cube
            self.blast(self.motion.rest[voxel])
            return voxel
        return None

    def change_radius(self, step):
        self.radius = float(np.clip(self.radius + step, RADIUS_MIN, RADIUS_MAX))
        if self.last is not None:
            self.query(self.last["point"])

    def toggle_colour(self):
        self.colour_mode = "distance" if self.colour_mode == "texture" else "texture"
        self.update_colours()

    def toggle_kd(self):
        self.show_kd = not self.show_kd
        self.update_colours()
        self.update_sphere()

    def reset(self):
        self.motion.reset()
        self.last = None
        self.update_colours()
        self.update_sphere()

    def step(self, dt):
        self.motion.step(dt)
        self.update_geometry()

    def tick(self, _step=None):
        """Timer callback for the window: real time step, clamped for stability."""
        now = time.perf_counter()
        dt = 1 / 60 if self._t_last is None else min(now - self._t_last, 1 / 20)
        self._t_last = now
        self.fps = 0.9 * self.fps + 0.1 / max(dt, 1e-4)
        self.step(dt)
        self.update_hud()


def set_camera(pl, azimuth=0.0, elevation=12.0, distance=2.2, focus=(0, 0.48, 0)):
    """Orbit camera around the Nisse. azimuth 0 looks from +Z."""
    a, e = np.radians(azimuth), np.radians(elevation)
    f = np.array(focus)
    pl.camera.focal_point = f
    pl.camera.position = f + distance * np.array([np.sin(a) * np.cos(e), np.sin(e),
                                                  np.cos(a) * np.cos(e)])
    pl.camera.up = (0, 1, 0)
    pl.camera.view_angle = 30
```

### Interactive window

The window listens to left button press and release itself. If the mouse moved at most 3 pixels between them, it is a click and `pick` runs. PyVista's own camera rotation still gets the same events, so a drag rotates as normal and a click without movement blasts. Space does the same as a click at the cursor, in case a click is hard to do without moving (a touchpad, for example).

`_add_selftest` is only for testing without a person: it sends a press and release at the window centre through the interactor (the same observers and the same picker as a real click), starts a scatter, prints a report and closes the window.

```python
# --------------------------------------------------------------- interactive window
def run_window(selftest=False):
    pl = pv.Plotter(window_size=(1280, 720), title="Voxel Nisse")
    app = NisseApp(pl)
    set_camera(pl)

    # A left click blasts only when the mouse did not move, so dragging still rotates.
    # PyVista passes the interactor *style* to these callbacks, so ask the interactor.
    press = {}
    iren = pl.iren.interactor

    def on_press(_style, _event):
        press["xy"] = iren.GetEventPosition()

    def on_release(_style, _event):
        x, y = iren.GetEventPosition()
        x0, y0 = press.pop("xy", (x, y))
        if abs(x - x0) + abs(y - y0) <= 3:
            app.pick(x, y)

    pl.iren.add_observer("LeftButtonPressEvent", on_press)
    pl.iren.add_observer("LeftButtonReleaseEvent", on_release)

    def at_cursor():
        app.pick(*iren.GetEventPosition())

    pl.add_key_event("space", at_cursor)
    pl.add_key_event("b", app.motion.scatter)
    pl.add_key_event("Up", lambda: app.change_radius(RADIUS_STEP))
    pl.add_key_event("Down", lambda: app.change_radius(-RADIUS_STEP))
    pl.add_key_event("m", app.toggle_colour)
    pl.add_key_event("k", app.toggle_kd)
    pl.add_key_event("x", lambda: app.show_pca(not app.pca_on))
    pl.add_key_event("n", app.reset)
    # The interactor must exist before a timer is created, otherwise (seen with
    # PyVista 0.49.3 on Windows) the timer never fires and nothing animates.
    pl.iren.initialize()
    pl.add_timer_event(max_steps=10 ** 9, duration=10, callback=app.tick)

    if selftest:
        _add_selftest(pl, app)
    print(f"{app.n} voxels, built in {app.voxel_s:.1f} s")
    pl.show()


def _add_selftest(pl, app):
    """Inject a left press + release at the window centre through VTK, then quit."""
    report, t0 = {}, time.perf_counter()

    def check(_step):
        t = time.perf_counter() - t0
        iren = pl.iren.interactor
        if t > 1.5 and "clicked" not in report:
            w, h = pl.window_size
            iren.SetEventPosition(w // 2, h // 2)
            iren.InvokeEvent("LeftButtonPressEvent")
            iren.InvokeEvent("LeftButtonReleaseEvent")
            report["clicked"] = True
            report["hit"] = app.last is not None
            report["neighbours"] = len(app.last["idx"]) if app.last else 0
        if t > 1.8 and "max_offset_after_click" not in report and "clicked" in report:
            report["max_offset_after_click"] = round(float(np.abs(app.motion.offset).max()), 3)
        if t > 2.5 and "scatter" not in report:
            app.motion.scatter()
            report["scatter"] = True
        if t > 8.0 and "fps" not in report:
            report["fps"] = round(app.fps, 1)
            print("SELFTEST", report, flush=True)
            iren.TerminateApp()

    pl.add_timer_event(max_steps=10 ** 9, duration=50, callback=check)
```

### Capture mode and video

`run_capture` uses an offscreen plotter, drives the same `NisseApp` with fixed time steps and saves the screenshots. `benchmark` measures the frame rate. `record_video` plays a scripted 13 s sequence with a slowly swinging camera: a blast at the hat, an overlapping blast at the side, a bigger blast with the KD view on, then scatter and rebuild.

```python
# --------------------------------------------------------------- capture mode
FRONT = 0.0   # camera azimuth that looks at his face


def textured_mesh(app):
    """The original scan as a PyVista mesh with its texture, for step 0."""
    m = app.mesh
    faces = np.hstack([np.full((len(m.faces), 1), 3), m.faces]).ravel()
    poly = pv.PolyData(np.asarray(m.vertices), faces)
    poly.active_texture_coordinates = np.asarray(m.visual.uv)
    return poly, pv.Texture(app.texture)


def benchmark(app, frames=120):
    """Frames per second of simulation + geometry update + render while a blast runs."""
    app.blast(app.motion.rest[np.argmax(app.motion.rest[:, 1] - 2 * np.abs(app.motion.rest[:, 0]))])
    t = time.perf_counter()
    for _ in range(frames):
        app.step(1 / 30)
        app.pl.render()
    fps = frames / (time.perf_counter() - t)
    app.reset()
    return fps


def run_capture():
    CAPTURE_DIR.mkdir(exist_ok=True)
    pl = pv.Plotter(off_screen=True, window_size=(1280, 720))
    app = NisseApp(pl)
    pl.enable_anti_aliasing("ssaa")

    def shot(name):
        pl.render()
        pl.screenshot(str(CAPTURE_DIR / name))

    rest = app.motion.rest
    app.update_hud(visible=False)
    print(f"{app.n} voxels in {app.voxel_s:.1f} s, KD build {app.nb.build_ms:.2f} ms")

    # 0: the textured scan, voxels hidden
    poly, tex = textured_mesh(app)
    scan = pl.add_mesh(poly, texture=tex, ambient=0.25, diffuse=0.85, specular=0.1)
    app.actor.SetVisibility(False)
    set_camera(pl, FRONT + 25)
    shot("step0_mesh.png")
    scan.SetVisibility(False)
    app.actor.SetVisibility(True)

    # 1, 2: voxels, then the PCA axes
    shot("step1_voxels.png")
    app.show_pca(True)
    set_camera(pl, FRONT + 40, elevation=10, distance=2.7, focus=(0, 0.55, 0))
    shot("step2_pca_axes.png")
    app.show_pca(False)
    set_camera(pl, FRONT + 25)

    # 3: KD-tree query at his chest (front, between the beard halves), then blast it
    chest = rest[np.argmax(rest[:, 2] - 4 * np.abs(rest[:, 1] - 0.42) - 4 * np.abs(rest[:, 0]))]
    app.toggle_kd()
    app.query(chest)
    print(f"query: {len(app.last['idx'])} neighbours, KD {app.last['kd_ms']:.4f} ms, "
          f"brute {app.last['bf_ms']:.4f} ms")
    shot("step3a_kdtree_query.png")
    app.blast(chest)
    t = 0.0
    for name, until in [("step3b_blast_t1.png", 0.08), ("step3c_blast_t2.png", 0.22),
                        ("step3d_return.png", 3.5)]:
        while t < until:
            app.step(1 / 60)
            t += 1 / 60
        if name == "step3d_return.png":
            app.toggle_kd()
        shot(name)

    # 4: colour by distance from a click on the hat
    hat = rest[np.argmax(rest[:, 1])]
    app.query(hat)
    app.toggle_colour()
    shot("step4_distance_colours.png")
    app.toggle_colour()

    # 5: scatter and rebuild from the boots up
    app.motion.scatter()
    for name, cond in [("step5a_scatter.png", lambda m: m.clock >= SCATTER_TIME + HOLD_TIME * 0.5),
                       ("step5b_rebuild_half.png", lambda m: m.rebuild_progress() >= 0.45),
                       ("step5c_rebuilt.png", lambda m: m.mode == "spring")]:
        while not cond(app.motion):
            app.step(1 / 60)
        far = name != "step5c_rebuilt.png"
        set_camera(pl, FRONT + 25, distance=3.6 if far else 2.2,
                   focus=(0, 0.72 if far else 0.48, 0))
        shot(name)
    set_camera(pl, FRONT + 25)

    # benchmark, then 6: overview mid-blast with the help text
    fps = benchmark(app)
    app.fps = fps
    print(f"offscreen fps during a blast: {fps:.1f}")
    app.toggle_kd()
    app.blast(rest[np.argmax(rest[:, 0] - 2 * np.abs(rest[:, 1] - 0.4))])   # his hand
    for _ in range(9):
        app.step(1 / 60)
    app.update_hud()
    set_camera(pl, FRONT + 30, elevation=14, distance=2.3, focus=(-0.22, 0.5, 0))
    shot("step6_overview.png")
    app.toggle_kd()
    record_video(pl, app, fps)
    pl.close()


def record_video(pl, app, fps_measured, seconds=13.0, rate=30):
    """demo.mp4: slow orbit, two overlapping blasts, a KD view blast, scatter + rebuild."""
    app.reset()
    rest = app.motion.rest
    app.fps = fps_measured
    pl.open_movie(str(CAPTURE_DIR / "demo.mp4"), framerate=rate, quality=7)
    events = {
        1.0: lambda: app.blast(rest[np.argmax(rest[:, 1])]),                         # hat
        1.6: lambda: app.blast(rest[np.argmin(np.linalg.norm(rest - [0.15, 0.4, 0.1], axis=1))]),
        3.2: lambda: (app.toggle_kd(), app.change_radius(0.06),
                      app.blast(rest[np.argmin(np.linalg.norm(rest - [0, 0.15, 0.15], axis=1))])),
        5.0: lambda: (app.toggle_kd(), app.change_radius(-0.06)),
        5.6: app.motion.scatter,
    }
    for f in range(int(seconds * rate)):
        t = f / rate
        for when in [w for w in events if w <= t]:
            events.pop(when)()
        set_camera(pl, FRONT - 20 + 35 * np.sin(t * 0.35), elevation=12 + 4 * np.sin(t * 0.5),
                   distance=2.3 + 0.7 * (app.motion.mode == "scatter") * (1 - app.motion.rebuild_progress()))
        app.step(1 / rate)
        app.update_hud()
        pl.write_frame()


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--capture", action="store_true", help="render captures/ offscreen")
    ap.add_argument("--selftest", action="store_true", help="open the window, click, quit")
    args = ap.parse_args()
    if args.capture:
        run_capture()
    else:
        run_window(selftest=args.selftest)


if __name__ == "__main__":
    main()
```

## What was hard or went wrong

- **No texture from trimesh.** trimesh loaded the UVs but only made a 2x2 placeholder image, so the colours had to come from the JPEG opened by hand. Pillow also refused the 8192 x 8192 image until its size guard was turned off.
- **Screenshots did not update.** In offscreen mode the second screenshot was identical to the first (the textured mesh, not the voxels). `screenshot` did not render again after I changed the scene, so every capture now calls `render()` first.
- **PCA arrows hidden.** The arrows start at the centroid, which is inside him, and the cubes are opaque. Longer arrows alone did not help, so the cubes turn see-through (opacity 0.22) while the axes are shown.
- **Scatter too big.** The first scatter added distances up to 1.3 to positions that were already up to 1 from the centre, so the cloud filled the whole screen and the hat cubes went out of frame. Distances were cut to 0.12 to 0.5 and the camera pulls back during the scatter.
- **Wrong object in the mouse callback.** PyVista attaches the mouse observers to the interactor style, so the callback got the style and `GetEventPosition` failed. The position is now read from the interactor.
- **The timer never fired.** In the window, `add_timer_event` created a timer that never called back, so nothing moved, and even PyVista's own minimal timer example did nothing here. Calling `pl.iren.initialize()` before creating the timer fixed it. Seen with PyVista 0.49.3 on Windows 11. I did not find out whether it is a bug or how it is meant to be used.
- **The window frame rate is lower than offscreen** (about 60 against 85 to 96). See the performance part.

## What is not verified

- **A real mouse click by a person was not tested.** The self-test sends a VTK press and release event at the window centre. That goes through the same observers, the same click-or-drag check and the same picker as a real click, and it hit the Nisse (857 neighbours, cubes moved up to 0.32 units). But no operating-system mouse event was involved, and a real drag (to check that it rotates and does not blast) was not tested.
- **The keys were not pressed in the window.** Capture mode calls the same functions the keys call (scatter, radius, colour mode, KD view, PCA, reset), but the key bindings themselves, including Space, were not triggered by a keyboard.
- **`run_ex9.bat`** was run through `cmd /c` with `--selftest` and worked. It was not double-clicked in Explorer.
- **Why the window is capped near 60 fps** (timer resolution or vsync) is not known.
- **Colour accuracy** of the voxels was only judged by eye against the textured render (`step0_mesh.png` against `step1_voxels.png`). There is no number for it.
- **Only tested on this one Windows 11 machine.**
- **The lecture articles** were not read. Only their titles and library lists are known.

Built with Claude Code.
