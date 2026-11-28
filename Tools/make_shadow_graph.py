"""Writes Assets/Shaders/Ex05/NisseShadow.shadergraph (noise dissolve with alpha clip, dark violet ember edge,
body darkening to black-violet, Fresnel rim, vertices that drift up like smoke) and the two particle graphs
NisseShadowSmoke.shadergraph (alpha blended puffs) and NisseShadowSpecks.shadergraph (additive glowing specks).
Run: python Tools/make_shadow_graph.py"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sg_lib import Graph, ROOT, ABS_WORLD, OBJECT, WORLD

g = Graph('lit')
pDis = g.prop_float('Dissolve', '_Dissolve', 0.0, 0.0, 1.0)
pNs = g.prop_float('Noise Scale', '_NoiseScale', 14.0, 1.0, 80.0)
pEw = g.prop_float('Edge Width', '_EdgeWidth', 0.07, 0.0, 0.3)
pEc = g.prop_color('Edge Color', '_EdgeColor', (0.3, 0.03, 0.45, 1.0))
pSc = g.prop_color('Shadow Color', '_ShadowColor', (0.025, 0.008, 0.04, 1.0), hdr=False)
pRc = g.prop_color('Rim Color', '_RimColor', (0.3, 0.1, 0.55, 1.0))
pRp = g.prop_float('Rim Power', '_RimPower', 2.2, 0.5, 8.0)
pWa = g.prop_float('Wisp Amount', '_WispAmount', 0.15, 0.0, 1.5)
pWs = g.prop_float('Wisp Scale', '_WispScale', 5.0, 0.5, 40.0)
pWv = g.prop_float('Wisp Speed', '_WispSpeed', 0.7, 0.0, 5.0)
pWd = g.prop_vec3('Wisp Direction', '_WispDir', (0.35, 1.0, 0.15))
pSm = g.prop_float('Smoothness', '_Smoothness', 0.08, 0.0, 1.0)

# The graph is split into six groups. Every group gets its own copies of the inputs it uses (the Dissolve property,
# Position), so no wire from a property crosses a group border. Shader Graph allows several property nodes for the
# same property; they all read the same material value, so the shader does exactly the same as with one.
# Positions are set at the end by grid_layout (the x, y given to node() here are ignored).

# ---- dissolve noise from world position (two planar projections averaged, so UV seams do not show) ----
g.set_group('Noise and clip')
pos = g.node('PositionNode', 0, 0, extra={'m_Space': ABS_WORLD, 'm_PositionSource': 0})
sp = g.node('SplitNode', 0, 0); g.link(pos, 'Out', sp, 'In')
cxy = g.node('CombineNode', 0, 0); g.link(sp, 'R', cxy, 'R'); g.link(sp, 'G', cxy, 'G')
czy = g.node('CombineNode', 0, 0); g.link(sp, 'B', czy, 'R'); g.link(sp, 'G', czy, 'G')
ns = g.pnode(pNs, 0, 0)
n1 = g.node('GradientNoiseNode', 0, 0); g.link(cxy, 'RG', n1, 'UV'); g.link(ns, 'Out', n1, 'Scale')
n2 = g.node('GradientNoiseNode', 0, 0); g.link(czy, 'RG', n2, 'UV'); g.link(ns, 'Out', n2, 'Scale')
fine = g.node('NoiseNode', 0, 0, values={'Scale': 120.0}); g.link(cxy, 'RG', fine, 'UV')   # Simple Noise, small grain
na = g.node('AddNode', 0, 0); g.link(n1, 'Out', na, 'A'); g.link(n2, 'Out', na, 'B')
nb = g.node('MultiplyNode', 0, 0, values={'B': 0.5}); g.link(na, 'Out', nb, 'A')
fl = g.node('LerpNode', 0, 0, values={'T': 0.18}); g.link(nb, 'Out', fl, 'A'); g.link(fine, 'Out', fl, 'B')
nr = g.node('RemapNode', 0, 0, values={'In Min Max': (0.22, 0.78), 'Out Min Max': (0.0, 1.0)})
g.link(fl, 'Out', nr, 'In')
noise = g.node('SaturateNode', 0, 0); g.link(nr, 'Out', noise, 'In')
dis = g.pnode(pDis, 0, 0)
thr = g.node('MultiplyNode', 0, 0, values={'B': 1.02}); g.link(dis, 'Out', thr, 'A')   # threshold, 1 removes all
g.target['m_AlphaClip'] = True
# noise and threshold leave the group through two vertical buses of Redirect nodes in the gap to the right of it
g.set_group('')
nHub, nUp, nDn = g.redirect(), g.redirect(), g.redirect()
tHub, tDn = g.redirect(), g.redirect()
g.link(noise, 'Out', nHub, 'In'); g.link(nHub, 'Out', nUp, 'In'); g.link(nHub, 'Out', nDn, 'In')
g.link(thr, 'Out', tHub, 'In'); g.link(tHub, 'Out', tDn, 'In')

# ---- glowing edge just above the threshold, and the emission sum ----
g.set_group('Ember edge and emission')
ew = g.pnode(pEw, 0, 0)
te = g.node('AddNode', 0, 0); g.link(tDn, 'Out', te, 'A'); g.link(ew, 'Out', te, 'B')
edge = g.node('SmoothstepNode', 0, 0)   # smoothstep(thr + w, thr, noise): 1 right at the cut, 0 further in
g.link(te, 'Out', edge, 'Edge1'); g.link(tDn, 'Out', edge, 'Edge2'); g.link(nDn, 'Out', edge, 'In')
dis2 = g.pnode(pDis, 0, 0)
on = g.node('MultiplyNode', 0, 0, values={'B': 25.0}); g.link(dis2, 'Out', on, 'A')
onS = g.node('SaturateNode', 0, 0); g.link(on, 'Out', onS, 'In')       # no edge when dissolve is 0
ec = g.pnode(pEc, 0, 0)
e1 = g.node('MultiplyNode', 0, 0); g.link(edge, 'Out', e1, 'A'); g.link(onS, 'Out', e1, 'B')
edgeE = g.node('MultiplyNode', 0, 0); g.link(ec, 'Out', edgeE, 'A'); g.link(e1, 'Out', edgeE, 'B')

# ---- rim ----
g.set_group('Rim light')
rp = g.pnode(pRp, 0, 0)
fres = g.node('FresnelNode', 0, 0); g.link(rp, 'Out', fres, 'Power')
dis3 = g.pnode(pDis, 0, 0)
ra = g.node('LerpNode', 0, 0, values={'A': 0.0, 'B': 1.0}); g.link(dis3, 'Out', ra, 'T')
rc = g.pnode(pRc, 0, 0)
r1 = g.node('MultiplyNode', 0, 0); g.link(fres, 'Out', r1, 'A'); g.link(ra, 'Out', r1, 'B')
rimE = g.node('MultiplyNode', 0, 0); g.link(rc, 'Out', rimE, 'A'); g.link(r1, 'Out', rimE, 'B')

g.set_group('Ember edge and emission')
em = g.node('AddNode', 0, 0)
g.set_group('')
rr1, rr2 = g.redirect(), g.redirect()      # rim wire runs below the ember group into the emission sum
g.link(rimE, 'Out', rr1, 'In'); g.link(rr1, 'Out', rr2, 'In')
g.link(edgeE, 'Out', em, 'A'); g.link(rr2, 'Out', em, 'B')
g.to_block(em, 'Out', 'SurfaceDescription.Emission')

# alpha and clip threshold: routed through a channel between the rows to the master stack
ra1, ra2, rc1, rc2 = g.redirect(), g.redirect(), g.redirect(), g.redirect()
g.link(nHub, 'Out', ra1, 'In'); g.link(ra1, 'Out', ra2, 'In')
g.link(tHub, 'Out', rc1, 'In'); g.link(rc1, 'Out', rc2, 'In')
g.to_block(ra2, 'Out', 'SurfaceDescription.Alpha')
g.to_block(rc2, 'Out', 'SurfaceDescription.AlphaClipThreshold')

# ---- base colour fades to black-purple ----
g.set_group('Colour fade')
bm = g.pnode(g.base_map, 0, 0)
tex = g.node('SampleTexture2DNode', 0, 0, extra={'m_TextureType': 0}); g.link(bm, 'Out', tex, 'Texture')
dis4 = g.pnode(pDis, 0, 0)
dk = g.node('MultiplyNode', 0, 0, values={'B': 2.5}); g.link(dis4, 'Out', dk, 'A')
dkS = g.node('SaturateNode', 0, 0); g.link(dk, 'Out', dkS, 'In')
scn = g.pnode(pSc, 0, 0)
base = g.node('LerpNode', 0, 0); g.link(tex, 'RGBA', base, 'A'); g.link(scn, 'Out', base, 'B'); g.link(dkS, 'Out', base, 'T')
g.to_block(base, 'Out', 'SurfaceDescription.BaseColor')
sm = g.pnode(pSm, 0, 0); g.to_block(sm, 'Out', 'SurfaceDescription.Smoothness')
g.block_value('SurfaceDescription.Metallic', 0.0)

# ---- vertex: smoke wisps (scrolling noise pushes vertices along the wisp direction) ----
g.set_group('Wisp noise (vertex)')
pos2 = g.node('PositionNode', 0, 0, extra={'m_Space': ABS_WORLD, 'm_PositionSource': 0})
sp2 = g.node('SplitNode', 0, 0); g.link(pos2, 'Out', sp2, 'In')
tm = g.node('TimeNode', 0, 0)
wv = g.pnode(pWv, 0, 0)
ts = g.node('MultiplyNode', 0, 0); g.link(tm, 'Time', ts, 'A'); g.link(wv, 'Out', ts, 'B')
xz = g.node('AddNode', 0, 0); g.link(sp2, 'R', xz, 'A'); g.link(sp2, 'B', xz, 'B')
ys = g.node('SubtractNode', 0, 0); g.link(sp2, 'G', ys, 'A'); g.link(ts, 'Out', ys, 'B')
cw = g.node('CombineNode', 0, 0); g.link(xz, 'Out', cw, 'R'); g.link(ys, 'Out', cw, 'G')
wsc = g.pnode(pWs, 0, 0)
wn = g.node('GradientNoiseNode', 0, 0); g.link(cw, 'RG', wn, 'UV'); g.link(wsc, 'Out', wn, 'Scale')
wn2 = g.node('PowerNode', 0, 0, values={'B': 2.0}); g.link(wn, 'Out', wn2, 'A')

g.set_group('Push vertices (vertex)')
inv = g.node('OneMinusNode', 0, 0); g.link(nUp, 'Out', inv, 'In')     # parts about to vanish stream more
w1 = g.node('MultiplyNode', 0, 0); g.link(wn2, 'Out', w1, 'A'); g.link(inv, 'Out', w1, 'B')
wa = g.pnode(pWa, 0, 0)
w2 = g.node('MultiplyNode', 0, 0); g.link(w1, 'Out', w2, 'A'); g.link(wa, 'Out', w2, 'B')
dis5 = g.pnode(pDis, 0, 0)
w3 = g.node('MultiplyNode', 0, 0); g.link(w2, 'Out', w3, 'A'); g.link(dis5, 'Out', w3, 'B')
wd = g.pnode(pWd, 0, 0)
off = g.node('MultiplyNode', 0, 0); g.link(wd, 'Out', off, 'A'); g.link(w3, 'Out', off, 'B')
pos3 = g.node('PositionNode', 0, 0, extra={'m_Space': ABS_WORLD, 'm_PositionSource': 0})
newp = g.node('AddNode', 0, 0); g.link(pos3, 'Out', newp, 'A'); g.link(off, 'Out', newp, 'B')
toObj = g.node('TransformNode', 0, 0,
               extra={'m_Conversion': {'from': ABS_WORLD, 'to': OBJECT}, 'm_ConversionType': 0, 'm_Normalize': False})
g.link(newp, 'Out', toObj, 'In')
g.to_block(toObj, 'Out', 'VertexDescription.Position')

# ---- layout: (column, row) of every node inside its group, then the groups in a 2 x 3 grid ----
cells = {
    'Wisp noise (vertex)': {pos2: (0, 0), tm: (0, 1), wv: (0, 2), sp2: (1, 0), ts: (1, 1), xz: (2, 0), ys: (2, 1),
                            cw: (3, 0), wsc: (3, 1), wn: (4, 0), wn2: (5, 0)},
    'Push vertices (vertex)': {inv: (0, 1), w1: (1, 0), wa: (1, 1), w2: (2, 0), dis5: (2, 1), wd: (3, 0), w3: (3, 1),
                               pos3: (4, 0), off: (4, 1), newp: (5, 0), toObj: (6, 0)},
    'Noise and clip': {pos: (0, 0), sp: (1, 0), cxy: (2, 0), czy: (2, 1), ns: (2, 2), n1: (3, 0), n2: (3, 1),
                       fine: (3, 2), na: (4, 0), nb: (5, 0), fl: (6, 0), nr: (7, 0), noise: (8, 0),
                       dis: (7, 1), thr: (8, 1)},
    'Colour fade': {bm: (0, 0), dis4: (0, 1), tex: (1, 0), dk: (1, 1), scn: (2, 0), dkS: (2, 1), base: (3, 0),
                    sm: (3, 1)},
    'Ember edge and emission': {ew: (0, 0), te: (1, 0), edge: (2, 1), dis2: (0, 2), on: (1, 2), onS: (2, 2),
                                ec: (3, 0), e1: (3, 1), edgeE: (4, 1), em: (5, 2)},
    'Rim light': {rp: (0, 0), dis3: (0, 1), fres: (1, 0), ra: (1, 1), rc: (2, 0), r1: (2, 1), rimE: (3, 0)},
}
arrangement = [['Wisp noise (vertex)', 'Push vertices (vertex)'],
               ['Noise and clip', 'Colour fade'],
               ['Rim light', 'Ember edge and emission']]
CH = 200                                   # height of the wiring channel between the second and third row
R = g.grid_layout(cells, arrangement, row_gaps=[130, CH + 90])
lx, ly, lw, lh = R['Noise and clip']
gap_x = R['Colour fade'][0] - (lx + lw)    # space between the two columns of groups
mid = lx + lw + gap_x / 2
right = g.layout_width
ex, ey, ew_, eh = R['Ember edge and emission']
ch_y = ey - 60 - CH / 2 - 45               # centre of the channel between row 2 and row 3 (group titles sit 60 above)
fx = right + 300                           # master stack
vx, vy, _, _ = R['Push vertices (vertex)']
g.place_context('vertex', fx, vy - 20)
fy = R['Colour fade'][1] - 20
g.place_context('fragment', fx, fy)
# alpha (noise) and clip (threshold): down into the channel in the gap between the columns, then right to the stack
def port_y(n, i):                           # y of the i-th port row of a node (measured: first row 50 below the top, 24 apart)
    return g._objs[n]['m_DrawState']['m_Position']['y'] + 50 + 24 * i


xa, xb = mid - 45, mid + 15                # the two vertical buses: noise (left) and threshold (right)
g.set_pos(nHub, xa, port_y(noise, 0) - 12); g.set_pos(nUp, xa, port_y(inv, 0) - 12); g.set_pos(nDn, xa, port_y(edge, 2) - 12)
g.set_pos(tHub, xb, port_y(thr, 0) - 12); g.set_pos(tDn, xb, port_y(edge, 1) - 12)
g.set_pos(ra1, xa, ch_y - 34); g.set_pos(ra2, fx - 150, ch_y - 34)
g.set_pos(rc1, xb, ch_y + 10); g.set_pos(rc2, fx - 110, ch_y + 10)
# rim: from the rim group, under the ember group, up into the emission sum
rx, ry, rw, rh = R['Rim light']
bottom = max(ey + eh, ry + rh) + 70
emx, emy = g._objs[em]['m_DrawState']['m_Position']['x'], g._objs[em]['m_DrawState']['m_Position']['y']
g.set_pos(rr1, mid - 28, bottom); g.set_pos(rr2, emx - 110, bottom)
g.write(ROOT + '/Assets/Shaders/Ex05/NisseShadow.shadergraph')


# ======================= particle graphs (URP Unlit, transparent) =======================
# Colour = texture x particle vertex colour x tint. Alpha = the product's A channel.
def particle_graph(name, alpha_mode, tint):
    pg = Graph('unlit')
    pt = pg.prop_tex('Main Tex', '_MainTex')
    pg.set_group('Texture x particle colour x tint')
    pc = pg.prop_color('Tint', '_Tint', tint)
    tn = pg.pnode(pt, -900, 0)
    tx = pg.node('SampleTexture2DNode', -750, 0, extra={'m_TextureType': 0}); pg.link(tn, 'Out', tx, 'Texture')
    vc = pg.node('VertexColorNode', -750, 250)
    m1 = pg.node('MultiplyNode', -500, 100); pg.link(tx, 'RGBA', m1, 'A'); pg.link(vc, 'Out', m1, 'B')
    tc = pg.pnode(pc, -500, 250)
    m2 = pg.node('MultiplyNode', -350, 150); pg.link(m1, 'Out', m2, 'A'); pg.link(tc, 'Out', m2, 'B')
    spl = pg.node('SplitNode', -200, 250); pg.link(m2, 'Out', spl, 'In')
    pg.to_block(m2, 'Out', 'SurfaceDescription.BaseColor')
    pg.to_block(spl, 'A', 'SurfaceDescription.Alpha')
    pg.target.update({'m_SurfaceType': 1, 'm_AlphaMode': alpha_mode, 'm_ZWriteControl': 2, 'm_RenderFace': 0,
                      'm_CastShadows': False, 'm_ReceiveShadows': False})
    G = 'Texture x particle colour x tint'
    R = pg.grid_layout({G: {tn: (0, 0), tx: (1, 0), vc: (1, 1), m1: (2, 0), tc: (2, 1), m2: (3, 0), spl: (4, 1)}}, [[G]])
    x, y, w, h = R[G]
    pg.place_context('vertex', x + w + 220, y - 230)          # vertex blocks are unused here; out of the way above
    pg.place_context('fragment', x + w + 220, y - 10)        # Base Color level with the colour output, Alpha below
    pg.write(ROOT + '/Assets/Shaders/Ex05/' + name + '.shadergraph')


particle_graph('NisseShadowSmoke', 0, (1.0, 1.0, 1.0, 1.0))     # alpha blend
particle_graph('NisseShadowSpecks', 2, (1.6, 1.6, 1.6, 1.0))    # additive, HDR tint so bloom picks the specks up
