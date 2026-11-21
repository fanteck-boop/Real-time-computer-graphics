"""Ex11 (analysis 2): Difference of Normals (DoN) on the Nisse photogrammetry scan.

DoN (Ioannou et al. 2012): estimate the surface normal of every point with a small and a large support
radius, DoN = (n_small - n_large) / 2.  |DoN| lies in 0..1 and equals sin(theta/2), where theta is the angle
between the two normals.  It is large where fine-scale geometry departs from the large-scale shape (noise,
bumps, creases, hole edges) and near 0 on smooth surface: a band-pass filter on surface orientation.

Usage:  python Ex11_difference_of_normals.py [model.obj] [out_dir]
Run with the rtcg3d interpreter (Open3D, SciPy, PyVista, trimesh, matplotlib).
"""
import sys, os, time
import numpy as np
import open3d as o3d
import trimesh
import pyvista as pv
from scipy.spatial import cKDTree
from scipy.stats import spearmanr
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

pv.OFF_SCREEN = True
T0 = time.time()
SRC = sys.argv[1] if len(sys.argv) > 1 else r"C:\Users\trist\Desktop\Real-time-computer-graphics\Assets\Nisse\Nisse.obj"
OUT = sys.argv[2] if len(sys.argv) > 2 else r"C:\Users\trist\Desktop\Real-time-computer-graphics\Captures\Ex11\don"
os.makedirs(OUT, exist_ok=True)

# ---------------------------------------------------------------- 1. load, weld, point cloud
# Point cloud = the mesh vertices after welding UV-seam duplicates (same choice as Ex11 analysis 1).
# Why vertices and not uniform surface sampling: they are the real measured samples, they keep the mesh
# connectivity (needed for boundary edges), and the density is the scan's own, so radii mean the same
# thing as in the earlier numbers.  Uniform sampling would give even density but invents points.
tm = trimesh.load(SRC, force="mesh", process=False)
print(f"loaded {SRC}: {len(tm.vertices)} verts, {len(tm.faces)} faces")
# drop the texture/normal attributes first: otherwise merge_vertices refuses to merge vertices whose UVs differ
tm = trimesh.Trimesh(np.asarray(tm.vertices), np.asarray(tm.faces), process=False)
tm.merge_vertices()
tm.update_faces(tm.nondegenerate_faces())
tm.remove_unreferenced_vertices()
if tm.extents.argmax() == 1:        # Y-up asset -> Z-up for display (only if the tallest axis is Y)
    tm.apply_transform(trimesh.transformations.rotation_matrix(np.pi / 2, [1, 0, 0]))
V = np.asarray(tm.vertices)
F = np.asarray(tm.faces)
N = len(V)
diag = float(np.linalg.norm(tm.extents))
print(f"welded: {N} verts, {len(F)} faces, bbox extents {np.round(tm.extents, 3)}, diagonal {diag:.4f}")

# the mesh's own vertex normals (area-weighted face normals; winding is consistent) serve as the sign reference
ref_n = np.asarray(tm.vertex_normals)

pcd = o3d.geometry.PointCloud()
pcd.points = o3d.utility.Vector3dVector(V)

# ---------------------------------------------------------------- 2. normals at one radius
def normals_at(radius_frac):
    """PCA normal per point: Open3D collects all points within radius (KDTree radius search), builds the 3x3
    covariance of the neighbourhood and takes the eigenvector of the smallest eigenvalue as the normal.
    That eigenvector is only defined up to sign (+n and -n are both eigenvectors), so Open3D picks an
    arbitrary sign.  DoN subtracts two normals; if one is flipped the difference is ~2 instead of ~0, so it
    would measure sign flips, not geometry.  Fix: flip each normal so it points to the same side as the mesh's
    vertex normal (dot >= 0).  Both scales are oriented against the same reference, so they agree in sign."""
    p = o3d.geometry.PointCloud(pcd)
    p.estimate_normals(o3d.geometry.KDTreeSearchParamRadius(radius_frac * diag))
    n = np.asarray(p.normals).copy()
    flip = (n * ref_n).sum(1) < 0
    n[flip] *= -1
    return n, flip.mean()

radii = sorted({0.003, 0.005, 0.02, 0.03, 0.04})
cache, flips, ntime = {}, {}, {}
for r in radii:
    t = time.time()
    cache[r], flips[r] = normals_at(r)
    ntime[r] = time.time() - t
    print(f"  normals r={r*100:.1f}% diag ({r*diag:.4f}) in {ntime[r]:.1f}s, {flips[r]*100:.1f}% needed a sign flip")

# neighbour counts (small radius must contain enough points for a stable plane fit)
tree = cKDTree(V)
def median_neighbours(r):
    idx = np.random.default_rng(0).choice(N, 3000, replace=False)
    return float(np.median([len(x) for x in tree.query_ball_point(V[idx], r * diag)]))

def don(rs, rl):
    d = (cache[rs] - cache[rl]) / 2.0
    return np.linalg.norm(d, axis=1)

# ---------------------------------------------------------------- 3. several radius pairs
THR = 0.2    # |DoN| = sin(theta/2); 0.2 is a 23 degree angle between the two scales' normals
pairs = [(0.003, 0.02), (0.003, 0.03), (0.003, 0.04), (0.005, 0.02), (0.005, 0.03), (0.005, 0.04)]
res = {}
print(f"\n{'small%':>7} {'large%':>7} {'nn_small':>8} {'mean':>7} {'median':>7} {'p95':>7} {f'frac>{THR}':>9} {'time_s':>7}")
for rs, rl in pairs:
    t = time.time()
    d = don(rs, rl)
    dt = time.time() - t + ntime[rs] + ntime[rl]     # DoN itself plus the two normal estimations
    res[(rs, rl)] = dict(d=d, nn=median_neighbours(rs), mean=d.mean(), med=np.median(d),
                         p95=np.percentile(d, 95), frac=(d > THR).mean(), t=dt)
    r = res[(rs, rl)]
    print(f"{rs*100:7.1f} {rl*100:7.1f} {r['nn']:8.0f} {r['mean']:7.3f} {r['med']:7.3f} {r['p95']:7.3f} {r['frac']*100:8.1f}% {dt:7.1f}")

# ---------------------------------------------------------------- 5. hypothesis data (computed before the pick, reused)
e = tm.edges_sorted
uniq, cnt = np.unique(e, axis=0, return_counts=True)
be = uniq[cnt == 1]
bverts = np.unique(be)
print(f"\nboundary edges: {len(be)} (task says 325), boundary vertices: {len(bverts)}")
dist_b, _ = cKDTree(V[bverts]).query(V)       # distance of every vertex to the nearest boundary vertex

def boundary_contrast(d, near_frac=0.02):
    near = dist_b < near_frac * diag
    return d[near].mean(), d[~near].mean(), near.mean()

# patchy-normal proxy: mean dihedral angle between adjacent faces around each vertex (independent of DoN)
adj = tm.face_adjacency
ang = np.asarray(tm.face_adjacency_angles)     # radians between neighbouring face normals
edge_v = np.asarray(tm.face_adjacency_edges)
dih_sum = np.zeros(N); dih_cnt = np.zeros(N)
for k in range(2):
    np.add.at(dih_sum, edge_v[:, k], ang); np.add.at(dih_cnt, edge_v[:, k], 1)
dihedral = np.degrees(dih_sum / np.maximum(dih_cnt, 1))

print(f"\n{'small%':>7} {'large%':>7} {'near_bnd':>9} {'elsewhere':>10} {'ratio':>6} {'rho(dihedral)':>14}")
for k, r in res.items():
    a, b, _ = boundary_contrast(r["d"])
    r["rho"] = spearmanr(r["d"], dihedral)[0]
    r["bratio"] = a / b
    print(f"{k[0]*100:7.1f} {k[1]*100:7.1f} {a:9.3f} {b:10.3f} {a/b:6.2f} {r['rho']:14.3f}")

# ---------------------------------------------------------------- choose a pair (rule on data, not on the hypothesis)
# Need >= ~15 points in the small neighbourhood (stable plane fit), then take the pair whose |DoN|
# distribution is most spread out relative to its median (p95/median): the band-pass separates rough from
# smooth best, while a threshold of 0.2 still keeps the rough class a minority.
ok = [k for k, r in res.items() if r["nn"] >= 15 and 0.02 < r["frac"] < 0.4]
PICK = max(ok, key=lambda k: res[k]["p95"] / max(res[k]["med"], 1e-6))
RS, RL = PICK
print(f"\nPICK small={RS*100:.1f}% ({RS*diag:.4f}), large={RL*100:.1f}% ({RL*diag:.4f})  "
      f"[candidates {[(a*100, b*100) for a, b in ok]}]")
d = res[PICK]["d"]
# sign sanity check: after orientation, two normals that still point to opposite sides give |DoN| > 0.707
# (angle > 90 degrees).  Those points are where the sign reference was ambiguous or the surface is a real fold.
print(f"sign check: {(d > 0.7071).sum()} points ({(d > 0.7071).mean()*100:.2f}%) have the two normals more than 90 deg apart")

# ---------------------------------------------------------------- 4. segmentation
rough = d > THR
print(f"\nSEGMENTATION |DoN| > {THR}: rough/high-detail {rough.sum()} ({rough.mean()*100:.1f}%), "
      f"smooth {(~rough).sum()} ({(~rough).mean()*100:.1f}%)")
for t in (0.1, 0.2, 0.3, 0.5):
    print(f"   threshold {t}: {(d > t).sum():7d} points ({(d > t).mean()*100:5.1f}%)")

# ---------------------------------------------------------------- 5. hypothesis report
a, b, nearfrac = boundary_contrast(d)
print(f"\nHYPOTHESIS 1 boundary edges: points within 2% diag of a boundary vertex = {nearfrac*100:.2f}% of all points")
print(f"   mean |DoN| near boundary {a:.3f}  vs elsewhere {b:.3f}  (ratio {a/b:.2f})")
print(f"   mean |DoN| ON boundary vertices {d[bverts].mean():.3f}")
print(f"   share of rough points that lie near the boundary: {(rough & (dist_b < 0.02*diag)).sum()/rough.sum()*100:.1f}%")
print("   mean |DoN| by distance to boundary (% diag):")
edges_pct = [0, 0.25, 0.5, 1, 2, 4, 8, 100]
bins = []
for lo, hi in zip(edges_pct[:-1], edges_pct[1:]):
    m = (dist_b >= lo*diag/100) & (dist_b < hi*diag/100)
    bins.append((lo, hi, m.sum(), d[m].mean() if m.any() else np.nan))
    print(f"     {lo:5.2f} to {hi:6.2f}%: n={m.sum():6d}  mean |DoN| {bins[-1][3]:.3f}")
# the same, but a boundary-free reference: random seeds so we know what 'near X' does on interior surface
# (a boundary patch only bumps |DoN| if the one-sided neighbourhood tilts the large normal)
print(f"HYPOTHESIS 2 patchy normals: Spearman rho(|DoN|, mean dihedral angle at vertex) = {spearmanr(d, dihedral)[0]:.3f}")
q = np.percentile(dihedral, 90)
print(f"   mean |DoN| where dihedral is in top 10% (>{q:.1f} deg): {d[dihedral>q].mean():.3f}; rest: {d[dihedral<=q].mean():.3f}")
print(f"   fraction of rough points that are also in top-10% dihedral: {(rough & (dihedral>q)).sum()/rough.sum()*100:.1f}%")

# ---------------------------------------------------------------- 6. rendering (off-screen PyVista)
def to_pv():
    return pv.PolyData(V, np.hstack([np.full((len(F), 1), 3), F]).ravel())
mesh = to_pv()
CLIM = (0, float(np.percentile(d, 99)))

def shot(name, scalars, cmap, title, clim, azim_view, sbar_title, categories=None, annotations=None):
    p = pv.Plotter(off_screen=True, window_size=(900, 1200))
    p.set_background("white")
    kw = dict(scalars=scalars, cmap=cmap, clim=clim, smooth_shading=True, show_scalar_bar=True,
              scalar_bar_args=dict(title=sbar_title, color="black", vertical=False, title_font_size=16,
                                   label_font_size=14, n_labels=5, position_x=0.15, width=0.7, position_y=0.04))
    if categories:
        kw.update(annotations=annotations, categories=True, show_scalar_bar=True,
                  scalar_bar_args=dict(title=sbar_title, color="black", vertical=False, title_font_size=16,
                                       label_font_size=14, position_x=0.1, width=0.8, position_y=0.04, n_labels=0))
    p.add_mesh(mesh, **kw)
    p.add_text(title, font_size=11, color="black")
    p.view_vector(azim_view, viewup=(0, 0, 1))
    p.camera.zoom(1.2)
    p.screenshot(os.path.join(OUT, name)); p.close()

mesh.point_data["don"] = d
FRONT, BACK = (0.3, -1, 0.25), (-0.3, 1, 0.25)
ttl = f"|DoN|  small {RS*100:.1f}% / large {RL*100:.1f}% of bbox diagonal"
shot("don_front.png", "don", "turbo", ttl, CLIM, FRONT, "|DoN| (0 smooth, high = rough)")
shot("don_back.png", "don", "turbo", ttl + " (back)", CLIM, BACK, "|DoN| (0 smooth, high = rough)")

seg = rough.astype(float)
mesh.point_data["seg"] = seg
ttl2 = f"Segmentation: |DoN| > {THR}  rough {rough.mean()*100:.1f}%"
shot("don_segmentation_front.png", "seg", ["#d9d9d9", "#d62728"], ttl2, (0, 1), FRONT, "class",
     categories=True, annotations={0: "smooth", 1: "rough / high detail"})
shot("don_segmentation_back.png", "seg", ["#d9d9d9", "#d62728"], ttl2 + " (back)", (0, 1), BACK, "class",
     categories=True, annotations={0: "smooth", 1: "rough / high detail"})

# boundary vertices over the |DoN| map: front view and a view from below (the boundary is under the base plate)
mesh.point_data["don_c"] = np.clip(d, *CLIM)
for name, vec, up in (("don_boundary.png", FRONT, (0, 0, 1)), ("don_boundary_below.png", (0.3, -1, -0.9), (0, 0, 1))):
    p = pv.Plotter(off_screen=True, window_size=(900, 1200)); p.set_background("white")
    p.add_mesh(mesh, scalars="don_c", cmap="Greys", clim=CLIM, smooth_shading=True, show_scalar_bar=False)
    p.add_points(V[bverts], color="red", point_size=6, render_points_as_spheres=True)
    p.add_text(f"Boundary vertices (red) over |DoN| (dark grey = high), {len(be)} boundary edges", font_size=11, color="black")
    p.view_vector(vec, viewup=up); p.camera.zoom(1.2); p.screenshot(os.path.join(OUT, name)); p.close()

# histograms for all pairs (two highlighted: smallest and chosen)
fig, ax = plt.subplots(1, 2, figsize=(12, 4.2))
alt = (RS, 0.02 if RL != 0.02 else 0.04)
for k, c in ((PICK, "tab:red"), (alt, "tab:blue")):
    ax[0].hist(res[k]["d"], bins=100, range=(0, 1), histtype="stepfilled", alpha=0.45, color=c,
               label=f"small {k[0]*100:.1f}% / large {k[1]*100:.1f}%  (mean {res[k]['mean']:.3f})")
ax[0].axvline(THR, color="k", ls="--", lw=1, label=f"threshold {THR}")
ax[0].set_xlabel("|DoN|"); ax[0].set_ylabel("points"); ax[0].set_title("Histogram of |DoN| for two radius pairs")
ax[0].legend(fontsize=8)
ax[1].hist(res[PICK]["d"], bins=np.logspace(-3, 0, 80), color="tab:red", alpha=0.6, label="chosen pair")
ax[1].hist(res[alt]["d"], bins=np.logspace(-3, 0, 80), color="tab:blue", alpha=0.45, label="other pair")
ax[1].set_xscale("log"); ax[1].axvline(THR, color="k", ls="--", lw=1)
ax[1].set_xlabel("|DoN| (log axis)"); ax[1].set_title("Same data, log x axis"); ax[1].legend(fontsize=8)
plt.tight_layout(); plt.savefig(os.path.join(OUT, "don_histogram.png"), dpi=110); plt.close()

# boundary-distance profile
fig, ax = plt.subplots(figsize=(6.5, 4))
lab = [f"{lo}-{hi}" if hi < 100 else f">{lo}" for lo, hi, _, _ in bins]
ax.bar(lab, [x[3] for x in bins], color="tab:purple")
ax.axhline(b, color="k", ls="--", lw=1, label=f"elsewhere (>2%): {b:.3f}")
ax.set_xlabel("distance to nearest boundary vertex (% of bbox diagonal)"); ax.set_ylabel("mean |DoN|")
ax.set_title("|DoN| versus distance to a boundary edge"); ax.legend(); plt.xticks(fontsize=8)
plt.tight_layout(); plt.savefig(os.path.join(OUT, "don_boundary_profile.png"), dpi=110); plt.close()

print(f"\nfigures in {OUT}")
print(f"TOTAL RUNTIME {time.time()-T0:.1f}s")
