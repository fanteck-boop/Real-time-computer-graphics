"""Exercise 2: isolate Nisse from the Brush splat using the COLMAP reconstruction (same world frame).
Needs numpy. Usage: python nisse_colmap.py "<folder with colmap/ and Nisse.ply>"
Writes Nisse_crop.ply (only Gaussians inside the Nisse volume) and nisse_frame.json next to the input."""
import json, sys, numpy as np
D = sys.argv[1]
S = D + r"\colmap\sparse\0"

def qR(q):
    w, x, y, z = q
    return np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
                     [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
                     [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])

# 1. camera poses: world-to-camera (R, t). Centre C = -R^T t, optical axis R^T z, camera up R^T (-y) (COLMAP y points down).
C, A, U = [], [], []
for l in [l for l in open(S + r"\images.txt") if not l.startswith('#')][0::2]:
    p = l.split(); R = qR(np.array(p[1:5], float)); t = np.array(p[5:8], float)
    C.append(-R.T @ t); A.append(R.T @ [0, 0, 1]); U.append(R.T @ [0, -1, 0])
C, A, U = map(np.array, (C, A, U))
# point closest (least squares) to all optical axes = what the cameras look at
M = sum(np.eye(3) - np.outer(a, a) for a in A); b = sum((np.eye(3) - np.outer(a, a)) @ c for a, c in zip(A, C))
X = np.linalg.solve(M, b)
up = U.mean(0); up /= np.linalg.norm(up)            # mean camera up = world up
e1 = np.cross(up, [0, 0, 1.]); e1 /= np.linalg.norm(e1); e2 = np.cross(up, e1)

# 2. Brush splat (same frame as COLMAP)
f = open(D + r"\Nisse.ply", 'rb'); hdr = b''
while b'end_header' not in hdr: hdr += f.readline()
off = len(hdr); lines = hdr.decode().split("\n")
props = [l.split()[2] for l in lines if l.startswith("property")]
n = int([l for l in lines if l.startswith("element vertex")][0].split()[2])
m = np.memmap(D + r"\Nisse.ply", dtype="<f4", mode="r", offset=off, shape=(n, len(props)))
ix = {p: i for i, p in enumerate(props)}
pos = np.array(m[:, [ix['x'], ix['y'], ix['z']]]); alpha = 1 / (1 + np.exp(-np.array(m[:, ix['opacity']])))
big = np.exp(np.array(m[:, [ix['scale_0'], ix['scale_1'], ix['scale_2']]])).max(1)
Q = pos - X; h = Q @ up; a = Q @ e1; bb = Q @ e2

# 3. Nisse axis from dense core, stool top (the plane the figure stands on) found from the height histogram (see notes)
core = (alpha > .3) & (h > -.2) & (h < .3) & (np.hypot(a, bb) < .25)
a0, b0 = a[core].mean(), bb[core].mean()
H_FLOOR, R_MAX, H_TOP = -0.265, 0.30, 0.60          # chosen from the side-view density plot and radial profile
keep = (np.hypot(a - a0, bb - b0) < R_MAX) & (h > H_FLOOR) & (h < H_TOP) & (alpha > .05) & (big < .08)
tip = h[keep & (alpha > .3)].max(); base = H_FLOOR
base_pt = X + a0 * e1 + bb[core].mean() * e2 + base * up   # base centre in splat coordinates
height = tip - base
out = dict(convergence=X.tolist(), up=up.tolist(), axis_a=a0, axis_b=b0, base_centre=base_pt.tolist(), tip_h=float(tip),
           floor_h=H_FLOOR, height_splat_units=float(height), kept=int(keep.sum()), total=n, cameras=len(C))
print(json.dumps(out, indent=1))
json.dump(out, open(D + r"\nisse_frame.json", "w"), indent=1)

# 4. write cropped PLY
hdr2 = hdr.replace(f"element vertex {n}".encode(), f"element vertex {int(keep.sum())}".encode())
with open(D + r"\Nisse_crop.ply", "wb") as g:
    g.write(hdr2); g.write(np.ascontiguousarray(m[np.flatnonzero(keep)]).tobytes())
