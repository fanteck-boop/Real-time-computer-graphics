"""Ex08: aggregate the three benchmark runs (Captures/Ex08/runs/run1..3) into a table (stdout) and chart PNGs."""
import csv, glob, statistics as st, collections
import matplotlib; matplotlib.use("Agg"); import matplotlib.pyplot as plt
base = "Captures/Ex08"
rows = collections.defaultdict(list)   # (scenario, variant) -> list of dict
for f in sorted(glob.glob(base + "/runs/run*/results_*.csv")):
    for r in csv.DictReader(open(f)): rows[(r["scenario"], r["variant"])].append(r)
label = {"Baseline": "Baseline", "SrpBatcher": "SRP Batcher", "Instancing": "GPU instancing", "Lod": "LOD",
         "LodInstancing": "LOD + instancing", "Grd": "GPU Resident Drawer", "GrdLod": "GRD + LOD"}
def agg(sc, v, k):
    xs = [float(r[k]) for r in rows[(sc, v)]]; return st.mean(xs), min(xs), max(xs)
table = {}
for sc in ("main", "small", "tiny"):
    print(f"\n## {sc}")
    print("variant | avg ms (mean, min-max of 3 runs) | FPS | draw calls | SetPass | triangles M | CPU frame ms")
    for v in label:
        if (sc, v) not in rows: continue
        a = agg(sc, v, "avg_ms"); d = agg(sc, v, "draw_calls")[0]; s = agg(sc, v, "setpass")[0]; t = agg(sc, v, "triangles_M")[0]; c = agg(sc, v, "cpu_frame_ms")[0]
        table[(sc, v)] = (a, d, s, t, c)
        print(f"{label[v]} | {a[0]:.2f} ({a[1]:.2f}-{a[2]:.2f}) | {1000/a[0]:.0f} | {d:.0f} | {s:.0f} | {t:.1f} | {c:.2f}")
col = {"Baseline": "#6b7280", "SrpBatcher": "#9ca3af", "Instancing": "#2563eb", "Lod": "#16a34a", "LodInstancing": "#7c3aed", "Grd": "#d97706", "GrdLod": "#dc2626"}
def panel(ax, sc, idx, title, ylabel, log=False, fmt="{:.0f}"):
    vs = [v for v in label if (sc, v) in table]
    vals = [table[(sc, v)][idx] if idx != 0 else table[(sc, v)][0][0] for v in vs]
    b = ax.bar([label[v] for v in vs], vals, color=[col[v] for v in vs])
    if idx == 0:
        for i, v in enumerate(vs):
            a = table[(sc, v)][0]; ax.errorbar(i, a[0], yerr=[[a[0] - a[1]], [a[2] - a[0]]], color="black", capsize=3, lw=1)
    if log: ax.set_yscale("log")
    ax.set_title(title, fontsize=11); ax.set_ylabel(ylabel); ax.tick_params(axis="x", rotation=40, labelsize=8)
    for r, x in zip(b, vals): ax.text(r.get_x() + r.get_width() / 2, x, fmt.format(x), ha="center", va="bottom", fontsize=8)
    ax.spines[["top", "right"]].set_visible(False)
fig, ax = plt.subplots(1, 3, figsize=(15, 4.8))
panel(ax[0], "main", 0, "Average frame time (lower is better)", "ms per frame (1920x1080)", fmt="{:.1f}")
panel(ax[1], "main", 1, "Draw calls per frame (log scale)", "draw calls", log=True)
panel(ax[2], "main", 3, "Triangles sent per frame (log scale)", "million triangles", log=True, fmt="{:.1f}")
fig.suptitle("Ex8 main scenario: 2 500 scanned Nisse (179k triangles each), RTX 4070 SUPER", fontsize=12)
plt.tight_layout(); plt.savefig(base + "/ex08_chart_main.png", dpi=120); plt.close()
fig, ax = plt.subplots(2, 2, figsize=(11, 8))
for j, (sc, t) in enumerate((("small", "3.6k-triangle mesh x 10 000"), ("tiny", "0.9k-triangle mesh x 20 000"))):
    panel(ax[0][j], sc, 0, f"{t}: frame time", "ms per frame", fmt="{:.2f}")
    panel(ax[1][j], sc, 1, f"{t}: draw calls (log)", "draw calls", log=True)
fig.suptitle("Ex8 supplementary: light meshes, where draw-call cost shows", fontsize=12)
plt.tight_layout(); plt.savefig(base + "/ex08_chart_light_meshes.png", dpi=120); plt.close()
