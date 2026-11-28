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
