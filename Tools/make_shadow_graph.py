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

# ---- dissolve noise from world position (two planar projections averaged, so UV seams do not show) ----
pos = g.node('PositionNode', -2600, 0, extra={'m_Space': ABS_WORLD, 'm_PositionSource': 0})
sp = g.node('SplitNode', -2450, 0); g.link(pos, 'Out', sp, 'In')
cxy = g.node('CombineNode', -2300, -80); g.link(sp, 'R', cxy, 'R'); g.link(sp, 'G', cxy, 'G')
czy = g.node('CombineNode', -2300, 80); g.link(sp, 'B', czy, 'R'); g.link(sp, 'G', czy, 'G')
ns = g.pnode(pNs, -2300, 220)
n1 = g.node('GradientNoiseNode', -2100, -80); g.link(cxy, 'RG', n1, 'UV'); g.link(ns, 'Out', n1, 'Scale')
n2 = g.node('GradientNoiseNode', -2100, 80); g.link(czy, 'RG', n2, 'UV'); g.link(ns, 'Out', n2, 'Scale')
fine = g.node('NoiseNode', -2100, 240, values={'Scale': 120.0}); g.link(cxy, 'RG', fine, 'UV')   # Simple Noise, small grain
na = g.node('AddNode', -1950, 0); g.link(n1, 'Out', na, 'A'); g.link(n2, 'Out', na, 'B')
nb = g.node('MultiplyNode', -1800, 0, values={'B': 0.5}); g.link(na, 'Out', nb, 'A')
fl = g.node('LerpNode', -1650, 60, values={'T': 0.18}); g.link(nb, 'Out', fl, 'A'); g.link(fine, 'Out', fl, 'B')
nr = g.node('RemapNode', -1500, 60, values={'In Min Max': (0.22, 0.78), 'Out Min Max': (0.0, 1.0)})
g.link(fl, 'Out', nr, 'In')
noise = g.node('SaturateNode', -1350, 60); g.link(nr, 'Out', noise, 'In')

dis = g.pnode(pDis, -1500, 260)
thr = g.node('MultiplyNode', -1350, 260, values={'B': 1.02}); g.link(dis, 'Out', thr, 'A')   # threshold, 1 removes all
g.to_block(noise, 'Out', 'SurfaceDescription.Alpha')
g.to_block(thr, 'Out', 'SurfaceDescription.AlphaClipThreshold')
g.target['m_AlphaClip'] = True

# ---- glowing edge just above the threshold ----
ew = g.pnode(pEw, -1350, 380)
te = g.node('AddNode', -1200, 330); g.link(thr, 'Out', te, 'A'); g.link(ew, 'Out', te, 'B')
edge = g.node('SmoothstepNode', -1050, 260)   # smoothstep(thr + w, thr, noise): 1 right at the cut, 0 further in
g.link(te, 'Out', edge, 'Edge1'); g.link(thr, 'Out', edge, 'Edge2'); g.link(noise, 'Out', edge, 'In')
on = g.node('MultiplyNode', -1050, 420, values={'B': 25.0}); g.link(dis, 'Out', on, 'A')
onS = g.node('SaturateNode', -900, 420); g.link(on, 'Out', onS, 'In')       # no edge when dissolve is 0
ec = g.pnode(pEc, -900, 160)
e1 = g.node('MultiplyNode', -750, 260); g.link(edge, 'Out', e1, 'A'); g.link(onS, 'Out', e1, 'B')
edgeE = g.node('MultiplyNode', -600, 220); g.link(ec, 'Out', edgeE, 'A'); g.link(e1, 'Out', edgeE, 'B')

# ---- rim ----
rp = g.pnode(pRp, -1050, 600)
fres = g.node('FresnelNode', -900, 580); g.link(rp, 'Out', fres, 'Power')
ra = g.node('LerpNode', -900, 720, values={'A': 0.0, 'B': 1.0}); g.link(dis, 'Out', ra, 'T')
rc = g.pnode(pRc, -750, 700)
r1 = g.node('MultiplyNode', -700, 600); g.link(fres, 'Out', r1, 'A'); g.link(ra, 'Out', r1, 'B')
rimE = g.node('MultiplyNode', -550, 620); g.link(rc, 'Out', rimE, 'A'); g.link(r1, 'Out', rimE, 'B')
em = g.node('AddNode', -400, 400); g.link(edgeE, 'Out', em, 'A'); g.link(rimE, 'Out', em, 'B')
g.to_block(em, 'Out', 'SurfaceDescription.Emission')

# ---- base colour fades to black-purple ----
bm = g.pnode(g.base_map, -900, -250)
tex = g.node('SampleTexture2DNode', -750, -250, extra={'m_TextureType': 0}); g.link(bm, 'Out', tex, 'Texture')
dk = g.node('MultiplyNode', -750, -60, values={'B': 2.5}); g.link(dis, 'Out', dk, 'A')
dkS = g.node('SaturateNode', -600, -60); g.link(dk, 'Out', dkS, 'In')
scn = g.pnode(pSc, -600, -140)
base = g.node('LerpNode', -450, -200); g.link(tex, 'RGBA', base, 'A'); g.link(scn, 'Out', base, 'B'); g.link(dkS, 'Out', base, 'T')
g.to_block(base, 'Out', 'SurfaceDescription.BaseColor')
sm = g.pnode(pSm, -450, -20); g.to_block(sm, 'Out', 'SurfaceDescription.Smoothness')
g.block_value('SurfaceDescription.Metallic', 0.0)

# ---- vertex: smoke wisps (scrolling noise pushes vertices along the wisp direction) ----
tm = g.node('TimeNode', -2600, -500)
wv = g.pnode(pWv, -2600, -380)
ts = g.node('MultiplyNode', -2450, -450); g.link(tm, 'Time', ts, 'A'); g.link(wv, 'Out', ts, 'B')
xz = g.node('AddNode', -2300, -600); g.link(sp, 'R', xz, 'A'); g.link(sp, 'B', xz, 'B')
ys = g.node('SubtractNode', -2300, -450); g.link(sp, 'G', ys, 'A'); g.link(ts, 'Out', ys, 'B')
cw = g.node('CombineNode', -2150, -520); g.link(xz, 'Out', cw, 'R'); g.link(ys, 'Out', cw, 'G')
wsc = g.pnode(pWs, -2150, -380)
wn = g.node('GradientNoiseNode', -2000, -520); g.link(cw, 'RG', wn, 'UV'); g.link(wsc, 'Out', wn, 'Scale')
wn2 = g.node('PowerNode', -1850, -520, values={'B': 2.0}); g.link(wn, 'Out', wn2, 'A')
inv = g.node('OneMinusNode', -1850, -380); g.link(noise, 'Out', inv, 'In')     # parts about to vanish stream more
w1 = g.node('MultiplyNode', -1700, -480); g.link(wn2, 'Out', w1, 'A'); g.link(inv, 'Out', w1, 'B')
wa = g.pnode(pWa, -1700, -350)
w2 = g.node('MultiplyNode', -1550, -450); g.link(w1, 'Out', w2, 'A'); g.link(wa, 'Out', w2, 'B')
w3 = g.node('MultiplyNode', -1400, -450); g.link(w2, 'Out', w3, 'A'); g.link(dis, 'Out', w3, 'B')
wd = g.pnode(pWd, -1400, -560)
off = g.node('MultiplyNode', -1250, -500); g.link(wd, 'Out', off, 'A'); g.link(w3, 'Out', off, 'B')
newp = g.node('AddNode', -1100, -500); g.link(pos, 'Out', newp, 'A'); g.link(off, 'Out', newp, 'B')
toObj = g.node('TransformNode', -950, -500,
               extra={'m_Conversion': {'from': ABS_WORLD, 'to': OBJECT}, 'm_ConversionType': 0, 'm_Normalize': False})
g.link(newp, 'Out', toObj, 'In')
g.to_block(toObj, 'Out', 'VertexDescription.Position')

g.write(ROOT + '/Assets/Shaders/Ex05/NisseShadow.shadergraph')


# ======================= particle graphs (URP Unlit, transparent) =======================
# Colour = texture x particle vertex colour x tint. Alpha = the product's A channel.
def particle_graph(name, alpha_mode, tint):
    pg = Graph('unlit')
    pt = pg.prop_tex('Main Tex', '_MainTex')
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
    pg.write(ROOT + '/Assets/Shaders/Ex05/' + name + '.shadergraph')


particle_graph('NisseShadowSmoke', 0, (1.0, 1.0, 1.0, 1.0))     # alpha blend
particle_graph('NisseShadowSpecks', 2, (1.6, 1.6, 1.6, 1.0))    # additive, HDR tint so bloom picks the specks up
