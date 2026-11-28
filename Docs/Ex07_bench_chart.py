"""Ex 7 benchmark figures. Reads Captures/Ex07_Bench/results.csv and the raw frames in Captures/Ex07_Bench/raw/,
writes chart.png, with_without_culling.png, with_without_lod.png and mips_off_on.png, and prints the mean table.
Run after the Unity benchmark:  python Docs/Ex07_bench_chart.py"""
import csv, math, os, re, statistics as st
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
D = os.path.join(ROOT, 'Captures', 'Ex07_Bench')
RAW = os.path.join(D, 'raw')
LOG = os.path.join(ROOT, 'Captures', '_logs', 'ex7bench.log')

rows = list(csv.DictReader(open(os.path.join(D, 'results.csv'))))
order = ['Neither', 'CullingOnly', 'LodOnly', 'Both', 'MipsOn_corridor', 'MipsOff_corridor']
label = {'Neither': 'Neither\n(no culling, LOD0)', 'CullingOnly': 'Culling only\n(LOD0)', 'LodOnly': 'LOD only\n(no culling)',
         'Both': 'Culling + LOD\n(project default)'}
agg = {}
for v in order:
    rs = [r for r in rows if r['variant'] == v]
    if not rs:
        continue
    ms = [float(r['mean_ms']) for r in rs]
    agg[v] = dict(runs=len(rs), ms=st.mean(ms), sd=st.stdev(ms) if len(ms) > 1 else 0.0, fps=1000 / st.mean(ms),
                  dc=st.mean(float(r['draw_calls']) for r in rs), inst=st.mean(float(r['instances']) for r in rs),
                  sp=st.mean(float(r['setpass']) for r in rs), tri=st.mean(float(r['triangles_M']) for r in rs))
print('| Variant | runs | ms/frame (sd) | FPS | draw calls | instances | SetPass | triangles (M) |')
for v, a in agg.items():
    print(f"| {v} | {a['runs']} | {a['ms']:.2f} ({a['sd']:.2f}) | {a['fps']:.0f} | {a['dc']:.0f} | {a['inst']:.0f} | {a['sp']:.0f} | {a['tri']:.2f} |")

# ---------------- chart ----------------
vs = ['Neither', 'CullingOnly', 'LodOnly', 'Both']
cols = ['#b5443b', '#d98b2b', '#4f7fbf', '#3f9a58']
fig, ax = plt.subplots(1, 3, figsize=(15, 5.2))
x = range(len(vs))
for i, (key, title, fmt) in enumerate([('ms', 'Frame time (ms, lower is better)', '{:.2f}'),
                                       ('tri', 'Triangles per frame (millions)', '{:.1f}'),
                                       ('dc', 'Draw calls per frame (GPU Resident Drawer on)', '{:.0f}')]):
    vals = [agg[v][key] for v in vs]
    b = ax[i].bar(x, vals, color=cols, yerr=[agg[v]['sd'] for v in vs] if key == 'ms' else None, capsize=4)
    for xi, val in zip(x, vals):
        ax[i].text(xi, val, fmt.format(val) + (f"\n{1000 / val:.0f} FPS" if key == 'ms' else ''), ha='center', va='bottom', fontsize=10)
    ax[i].set_xticks(list(x)); ax[i].set_xticklabels([label[v] for v in vs], fontsize=9)
    ax[i].set_title(title, fontsize=12); ax[i].set_ylim(0, max(vals) * 1.25)
    ax[i].spines[['top', 'right']].set_visible(False)
fig.suptitle('Ex 7: frustum culling and LOD, 356 Nisse + 16 pillars, one full turn from the centre of the field, '
             '1920x1080, mean of 3 runs (RTX 4070 SUPER, in the Unity editor)', fontsize=11)
fig.tight_layout(rect=(0, 0, 1, 0.94))
fig.savefig(os.path.join(D, 'chart.png'), dpi=110)

# ---------------- helpers ----------------
def font(sz):
    for f in ['C:/Windows/Fonts/segoeuib.ttf', 'C:/Windows/Fonts/arialbd.ttf']:
        if os.path.exists(f):
            return ImageFont.truetype(f, sz)
    return ImageFont.load_default()

def caption(img, lines, sz=34):
    d = ImageDraw.Draw(img); f = font(sz)
    h = (sz + 10) * len(lines) + 16
    d.rectangle([0, 0, img.width, h], fill=(15, 15, 20))
    for i, t in enumerate(lines):
        d.text((16, 8 + i * (sz + 10)), t, fill=(255, 255, 255), font=f)
    return img

def side(a, b, gap=12):
    out = Image.new('RGB', (a.width + b.width + gap, max(a.height, b.height)), (255, 255, 255))
    out.paste(a, (0, 0)); out.paste(b, (a.width + gap, 0))
    return out

log = open(LOG, encoding='utf-8', errors='ignore').read()
def stat(tag):
    m = re.search(r'EX7B image ' + tag + r':.*?triangles=(\d+)', log)
    return int(m.group(1)) if m else None
def infr(tag):
    m = re.search(r'EX7B image ' + tag + r': yaw=\S+ inFrustum=(\d+)', log)
    return int(m.group(1)) if m else None
instances = {}
for tag in ['cull_off_top', 'cull_on_top']:
    m = re.search(r'EX7B image ' + tag + r': draw_calls=\d+ instances=(\d+)', log)
    instances[tag] = int(m.group(1)) if m else None

# ---------------- culling: top views ----------------
cfg = dict(l.split('=') for l in open(os.path.join(RAW, 'top_camera.txt')).read().split())
S, P = float(cfg['ortho_size']), int(cfg['pixels'])
yaw, hfov, far = float(cfg['yaw']), float(cfg['hfov_deg']), float(cfg['far'])
def to_px(wx, wz):
    return (P / 2 + wx / (2 * S) * P, P / 2 - wz / (2 * S) * P)
tops = []
for tag, title in [('cull_off', 'Frustum culling defeated'), ('cull_on', 'Frustum culling on (normal)')]:
    im = Image.open(os.path.join(RAW, tag + '_top.png')).convert('RGB')
    d = ImageDraw.Draw(im)
    px, pz = float(cfg['player_x']), float(cfg['player_z'])
    for s in (-1, 1):
        a = math.radians(yaw + s * hfov / 2)
        d.line([to_px(px, pz), to_px(px + math.sin(a) * far, pz + math.cos(a) * far)], fill=(255, 220, 0), width=5)
    cx, cy = to_px(px, pz); d.ellipse([cx - 10, cy - 10, cx + 10, cy + 10], fill=(255, 220, 0))
    n = instances.get(tag + '_top')
    caption(im, [title, 'top view, green = drawn', f'yellow lines: player view ({hfov:.0f} deg wide)'], 32)
    tops.append(im)
img = side(tops[0], tops[1])
off_t, on_t = stat('cull_off'), stat('cull_on')
foot = Image.new('RGB', (img.width, 70), (15, 15, 20))
ImageDraw.Draw(foot).text((16, 14), f"Player camera at this heading: {off_t / 1e6:.1f} M triangles without culling, {on_t / 1e6:.1f} M with culling. "
                                     f"Objects inside the view: {infr('cull_on')} of 372. The picture the player sees is identical.", fill=(255, 255, 255), font=font(26))
full = Image.new('RGB', (img.width, img.height + foot.height)); full.paste(img, (0, 0)); full.paste(foot, (0, img.height))
full.save(os.path.join(D, 'with_without_culling.png'))

# ---------------- LOD ----------------
l0, la = stat('lod_forced0'), stat('lod_auto')
a = caption(Image.open(os.path.join(RAW, 'lod_forced0.png')).convert('RGB'), ['LOD forced to 0 (full scan everywhere)', f'{l0:,} triangles this frame'.replace(',', ' ')], 40)
b = caption(Image.open(os.path.join(RAW, 'lod_auto.png')).convert('RGB'), ['LOD automatic (project setting)', f'{la:,} triangles this frame'.replace(',', ' ')], 40)
side(a, b, 16).save(os.path.join(D, 'with_without_lod.png'))

# ---------------- mips: crop of the middle distance ----------------
off = Image.open(os.path.join(RAW, 'mips_off.png')).convert('RGB')
on = Image.open(os.path.join(RAW, 'mips_on.png')).convert('RGB')
box = (760, 420, 1160, 645)   # middle distance of the corridor floor, 400 x 225, shown 4x without smoothing
co = off.crop(box).resize((1600, 900), Image.NEAREST)
cn = on.crop(box).resize((1600, 900), Image.NEAREST)
mo, mn = agg.get('MipsOff_corridor'), agg.get('MipsOn_corridor')
caption(co, ['Mipmaps off (only level 0)', 'moire: false rings and stripes'], 44)
caption(cn, ['Mipmaps on (Kaiser, project setting)', 'far floor fades to grey'], 44)
side(co, cn, 16).save(os.path.join(D, 'mips_off_on.png'))
# full frames with the crop box marked, for reference
fo, fn = off.copy(), on.copy()
for im in (fo, fn):
    ImageDraw.Draw(im).rectangle(box, outline=(255, 220, 0), width=4)
side(caption(fo, ['Mipmaps off'], 40), caption(fn, ['Mipmaps on (Kaiser)'], 40), 16).save(os.path.join(RAW, 'mips_off_on_full.png'))
print('figures written to', D)
