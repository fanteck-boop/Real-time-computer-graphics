"""Ex11: mesh analysis of the Nisse photogrammetry scan with trimesh + PyVista (off-screen).
Usage: python Ex11_mesh_analysis.py <model.glb> [out_dir]
Analysis 1: quadric decimation at several reductions. Analysis 2: mesh quality (topology + curvature)."""
import sys, os, time
import numpy as np, trimesh, pyvista as pv
pv.OFF_SCREEN = True
src = sys.argv[1]
out = sys.argv[2] if len(sys.argv) > 2 else r"C:\Users\trist\Desktop\Real-time-computer-graphics\Captures\Ex11"
os.makedirs(out, exist_ok=True)

scene = trimesh.load(src)
tm = trimesh.util.concatenate(list(scene.geometry.values())) if hasattr(scene, "geometry") else scene
raw_v = len(tm.vertices)
tm = trimesh.Trimesh(tm.vertices, tm.faces, process=False)   # drop texture, keep geometry
print(f"raw glb: {raw_v} verts, {len(tm.faces)} faces (verts are duplicated along UV seams)")
tm.merge_vertices()                                            # weld by position so topology is meaningful
tm.update_faces(tm.nondegenerate_faces()); tm.remove_unreferenced_vertices()
print(f"welded:  {len(tm.vertices)} verts, {len(tm.faces)} faces")

def to_pv(m):
    return pv.PolyData(np.asarray(m.vertices), np.hstack([np.full((len(m.faces), 1), 3), m.faces]).ravel())

def shot(mesh, name, scalars=None, cmap=None, title="", clim=None, edges=False, zoom=1.0):
    p = pv.Plotter(off_screen=True, window_size=(900, 1100))
    p.set_background("white")
    p.add_mesh(mesh, scalars=scalars, cmap=cmap, clim=clim, show_edges=edges, edge_color="black", line_width=0.3,
               color=None if scalars is not None else "lightgray", smooth_shading=not edges, show_scalar_bar=scalars is not None,
               scalar_bar_args=dict(title=title, color="black", vertical=False) if scalars is not None else None)
    p.add_text(title, font_size=11, color="black")
    p.view_vector((0.3, -1, 0.25))
    p.camera.zoom(zoom); p.screenshot(os.path.join(out, name)); p.close()

# the glb is Y-up; rotate to Z-up for display
R = trimesh.transformations.rotation_matrix(np.pi / 2, [1, 0, 0])
tm.apply_transform(R)

# ---------- Analysis 1: decimation ----------
base = to_pv(tm)
base_size = None
rows = []
for red in (0.0, 0.5, 0.9, 0.98, 0.995):
    t = time.time()
    m = base if red == 0 else base.decimate(red)   # vtkQuadricDecimation
    dt = time.time() - t
    fn = os.path.join(out, f"nisse_dec_{int(red*1000):03d}.ply")
    m.save(fn, binary=True)
    # Hausdorff-style error: mean distance of original vertices to decimated surface, % of bbox diag
    d = 0.0
    if red > 0:
        dist = np.abs(base.compute_implicit_distance(m)['implicit_distance'][::10])  # original verts -> decimated surface
        d = dist.mean() / np.linalg.norm(tm.extents) * 100
    rows.append((red, m.n_cells, m.n_points, os.path.getsize(fn) / 1e6, dt, d))
    shot(m, f"decimation_{int(red*1000):03d}.png", title=f"{int(red*100)}% reduction: {m.n_cells} tris", edges=red >= 0.9)
print("\nDECIMATION  reduction  faces  verts  file_MB(ply)  time_s  mean_err_%bbox_diag")
for r in rows: print(f"  {r[0]*100:6.1f}%  {r[1]:7d} {r[2]:7d}  {r[3]:7.2f}  {r[4]:5.2f}  {r[5]:.4f}")

# montage
import matplotlib; matplotlib.use("Agg"); import matplotlib.pyplot as plt
from PIL import Image
fig, ax = plt.subplots(1, 5, figsize=(20, 5.5))
for a, r in zip(ax, rows):
    a.imshow(Image.open(os.path.join(out, f"decimation_{int(r[0]*1000):03d}.png"))); a.axis("off")
    a.set_title(f"{r[0]*100:.1f}%  {r[1]:,} tris  {r[3]:.1f} MB", fontsize=10)
plt.tight_layout(); plt.savefig(os.path.join(out, "decimation_montage.png"), dpi=110); plt.close()

# ---------- Analysis 2: mesh quality ----------
e = tm.edges_sorted
uniq, counts = np.unique(e, axis=0, return_counts=True)
boundary = int((counts == 1).sum()); nonman = int((counts > 2).sum())
# boundary loops (holes) via connected components of boundary edges
import scipy.sparse as sp, scipy.sparse.csgraph as cg
be = uniq[counts == 1]
n = len(tm.vertices)
g = sp.coo_matrix((np.ones(len(be)), (be[:, 0], be[:, 1])), shape=(n, n))
nloops = cg.connected_components(g, directed=False)[0] - (n - len(np.unique(be)))
areas = tm.area_faces
ang = tm.face_angles
print("\nQUALITY")
print(f"  watertight={tm.is_watertight}  winding_consistent={tm.is_winding_consistent}")
print(f"  edges={len(uniq)}  boundary_edges={boundary}  non_manifold_edges={nonman}  boundary_loops(holes)~{nloops}")
print(f"  euler_number={tm.euler_number}  connected_components={len(tm.split(only_watertight=False))}")
print(f"  degenerate faces(area<1e-12)={(areas < 1e-12).sum()}  min area={areas.min():.3e}  min angle={np.degrees(ang.min()):.3f} deg")
print(f"  surface area={tm.area:.4f}  bbox extents={tm.extents}")
ps = to_pv(tm)
ps["boundary"] = np.zeros(ps.n_points)
ps["boundary"][np.unique(be)] = 1
bpts = ps.points[np.unique(be)]
curv = ps.curvature("mean")
lim = np.percentile(np.abs(curv), 95)
print(f"  mean curvature: median={np.median(curv):.2f}  p5/p95={np.percentile(curv,5):.1f}/{np.percentile(curv,95):.1f}")
shot(ps, "quality_curvature.png", scalars=np.clip(curv, -lim, lim), cmap="coolwarm", title="Mean curvature (clipped to p95)")
# boundary edges drawn on grey mesh
p = pv.Plotter(off_screen=True, window_size=(900, 1100)); p.set_background("white")
p.add_mesh(ps, color="lightgray", smooth_shading=True)
if len(bpts): p.add_points(bpts, color="red", point_size=4, render_points_as_spheres=True)
p.add_text(f"Boundary edges (red): {boundary} edges, {nloops} loops", font_size=11, color="black")
p.view_vector((0.3, -1, 0.25)); p.screenshot(os.path.join(out, "quality_boundary.png")); p.close()
# normals: face normal z-component colour + triangle area histogram
fig, ax = plt.subplots(1, 2, figsize=(11, 4))
ax[0].hist(np.log10(areas[areas > 0]), 80, color="steelblue"); ax[0].set_xlabel("log10 triangle area"); ax[0].set_title("Triangle area distribution")
ax[1].hist(np.degrees(ang.min(axis=1)), 60, color="indianred"); ax[1].set_xlabel("smallest angle per triangle (deg)"); ax[1].set_title("Triangle quality")
plt.tight_layout(); plt.savefig(os.path.join(out, "quality_histograms.png"), dpi=110)
print("figures in", out)
