"""Writes Assets/Shaders/Ex05/NisseShout.shadergraph (lit, shockwave ring on the Nisse) and
Assets/Shaders/Ex05/NisseShoutShell.shadergraph (transparent expanding shell that bends the background).
Run: python Tools/make_shout_graph.py"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sg_lib import Graph, ROOT, ABS_WORLD, OBJECT, VIEW, WORLD

OUT = ROOT + '/Assets/Shaders/Ex05/'

# ======================= NisseShout (lit) =======================
g = Graph('lit')
pOrigin = g.prop_vec3('Shout Origin', '_ShoutOrigin', (-0.9, 0.55, -0.35))
pTime = g.prop_float('Shout Time', '_ShoutTime', 0.8)
pSpeed = g.prop_float('Shout Speed', '_ShoutSpeed', 1.0, 0.1, 6.0)
pWidth = g.prop_float('Ring Width', '_ShoutWidth', 0.09, 0.01, 0.6)
pAmp = g.prop_float('Amplitude', '_ShoutAmplitude', 0.05, 0.0, 0.2)
pFreq = g.prop_float('Ripple Frequency', '_ShoutFrequency', 70.0, 0.0, 200.0)
pLife = g.prop_float('Life', '_ShoutLife', 2.2, 0.2, 6.0)
pRing = g.prop_color('Ring Color', '_RingColor', (3.2, 2.4, 1.2, 1.0))
pRimPow = g.prop_float('Rim Power', '_RimPower', 3.0, 0.5, 8.0)
pRimStr = g.prop_float('Rim Strength', '_RimStrength', 0.8, 0.0, 4.0)
pSmooth = g.prop_float('Smoothness', '_Smoothness', 0.25, 0.0, 1.0)

# --- band function, shared by the vertex and the fragment stage ---
pos = g.node('PositionNode', -2200, -300, extra={'m_Space': ABS_WORLD, 'm_PositionSource': 0})
org = g.pnode(pOrigin, -2200, -200)
dist = g.node('DistanceNode', -2000, -260)
g.link(pos, 'Out', dist, 'A'); g.link(org, 'Out', dist, 'B')
tim = g.pnode(pTime, -2200, -100); spd = g.pnode(pSpeed, -2200, -40)
radius = g.node('MultiplyNode', -2000, -80)            # ring radius = time * speed
g.link(tim, 'Out', radius, 'A'); g.link(spd, 'Out', radius, 'B')
x = g.node('SubtractNode', -1800, -200)                 # signed distance to the ring
g.link(dist, 'Out', x, 'A'); g.link(radius, 'Out', x, 'B')
ax = g.node('AbsoluteNode', -1650, -200)
g.link(x, 'Out', ax, 'In')
wid = g.pnode(pWidth, -1800, -60)
band = g.node('SmoothstepNode', -1500, -200, values={'Edge2': 0.0})   # smoothstep(width, 0, |x|): 1 on the ring, 0 outside
g.link(wid, 'Out', band, 'Edge1'); g.link(ax, 'Out', band, 'In')
life = g.pnode(pLife, -2000, 40)
tl = g.node('DivideNode', -1800, 40)
g.link(tim, 'Out', tl, 'A'); g.link(life, 'Out', tl, 'B')
om = g.node('OneMinusNode', -1650, 40); g.link(tl, 'Out', om, 'In')
fade = g.node('SaturateNode', -1500, 40); g.link(om, 'Out', fade, 'In')
bf = g.node('MultiplyNode', -1300, -120)                # band * fade
g.link(band, 'Out', bf, 'A'); g.link(fade, 'Out', bf, 'B')

# --- vertex stage: push vertices out along the normal ---
frq = g.pnode(pFreq, -1800, -420)
xf = g.node('MultiplyNode', -1650, -420); g.link(x, 'Out', xf, 'A'); g.link(frq, 'Out', xf, 'B')
sn = g.node('SineNode', -1500, -420); g.link(xf, 'Out', sn, 'In')
rip = g.node('MultiplyNode', -1350, -420, values={'B': 0.4}); g.link(sn, 'Out', rip, 'A')
rip2 = g.node('AddNode', -1200, -420, values={'B': 0.6}); g.link(rip, 'Out', rip2, 'A')
amp = g.pnode(pAmp, -1200, -330)
d1 = g.node('MultiplyNode', -1050, -380); g.link(rip2, 'Out', d1, 'A'); g.link(amp, 'Out', d1, 'B')
disp = g.node('MultiplyNode', -900, -300); g.link(d1, 'Out', disp, 'A'); g.link(bf, 'Out', disp, 'B')
nrm = g.node('NormalVectorNode', -900, -480, extra={'m_Space': WORLD})
off = g.node('MultiplyNode', -750, -400); g.link(nrm, 'Out', off, 'A'); g.link(disp, 'Out', off, 'B')
newp = g.node('AddNode', -600, -400); g.link(pos, 'Out', newp, 'A'); g.link(off, 'Out', newp, 'B')
toObj = g.node('TransformNode', -450, -400,
               extra={'m_Conversion': {'from': ABS_WORLD, 'to': OBJECT}, 'm_ConversionType': 0, 'm_Normalize': False})
g.link(newp, 'Out', toObj, 'In')
g.to_block(toObj, 'Out', 'VertexDescription.Position')

# --- fragment stage ---
bm = g.pnode(g.base_map, -900, 0)
tex = g.node('SampleTexture2DNode', -750, 0, extra={'m_TextureType': 0})
g.link(bm, 'Out', tex, 'Texture')
g.to_block(tex, 'RGBA', 'SurfaceDescription.BaseColor')

ring = g.pnode(pRing, -900, 250)
ringE = g.node('MultiplyNode', -600, 150); g.link(ring, 'Out', ringE, 'A'); g.link(bf, 'Out', ringE, 'B')
rp = g.pnode(pRimPow, -1100, 350)
fres = g.node('FresnelNode', -900, 330); g.link(rp, 'Out', fres, 'Power')
rimAmt = g.node('LerpNode', -900, 480, values={'A': 0.15, 'B': 1.0}); g.link(fade, 'Out', rimAmt, 'T')
rs = g.pnode(pRimStr, -900, 600)
r1 = g.node('MultiplyNode', -700, 380); g.link(fres, 'Out', r1, 'A'); g.link(rimAmt, 'Out', r1, 'B')
r2 = g.node('MultiplyNode', -550, 400); g.link(r1, 'Out', r2, 'A'); g.link(rs, 'Out', r2, 'B')
rimE = g.node('MultiplyNode', -400, 330); g.link(ring, 'Out', rimE, 'A'); g.link(r2, 'Out', rimE, 'B')
em = g.node('AddNode', -250, 220); g.link(ringE, 'Out', em, 'A'); g.link(rimE, 'Out', em, 'B')
g.to_block(em, 'Out', 'SurfaceDescription.Emission')
sm = g.pnode(pSmooth, -400, 100)
g.to_block(sm, 'Out', 'SurfaceDescription.Smoothness')
g.block_value('SurfaceDescription.Metallic', 0.0)
g.write(OUT + 'NisseShout.shadergraph')

# ======================= NisseShoutShell (unlit, transparent) =======================
# A sphere centred on the shout origin; the C# driver scales it to radius = time * speed.
# Fresnel makes only the silhouette of the sphere visible (a ring on screen). Inside that ring the
# Scene Color node is sampled at a shifted screen position, which bends whatever is behind it.
s = Graph('unlit')
sTime = s.prop_float('Shout Time', '_ShoutTime', 0.8)
sLife = s.prop_float('Life', '_ShoutLife', 2.2, 0.2, 6.0)
sPow = s.prop_float('Shell Power', '_ShellPower', 2.5, 0.5, 10.0)
sStr = s.prop_float('Distortion', '_Distortion', 0.12, 0.0, 0.2)
sTint = s.prop_color('Shell Tint', '_ShellTint', (0.35, 0.27, 0.14, 1.0))
sRip = s.prop_float('Ripple Scale', '_RippleScale', 18.0, 1.0, 80.0)

fr = s.node('FresnelNode', -1200, 0); pw = s.pnode(sPow, -1400, 40); s.link(pw, 'Out', fr, 'Power')
st = s.pnode(sTime, -1600, 300); sl = s.pnode(sLife, -1600, 360)
dv = s.node('DivideNode', -1400, 300); s.link(st, 'Out', dv, 'A'); s.link(sl, 'Out', dv, 'B')
om2 = s.node('OneMinusNode', -1250, 300); s.link(dv, 'Out', om2, 'In')
fd = s.node('SaturateNode', -1100, 300); s.link(om2, 'Out', fd, 'In')
# ripple: gradient noise on the mesh UV, scrolled by the shout time
uv = s.node('UVNode', -1600, 500)
tsc = s.node('MultiplyNode', -1450, 600, values={'B': 0.6}); s.link(st, 'Out', tsc, 'A')
uvo = s.node('AddNode', -1300, 520); s.link(uv, 'Out', uvo, 'A'); s.link(tsc, 'Out', uvo, 'B')
rsc = s.pnode(sRip, -1300, 640)
nz = s.node('GradientNoiseNode', -1150, 520); s.link(uvo, 'Out', nz, 'UV'); s.link(rsc, 'Out', nz, 'Scale')
nzr = s.node('RemapNode', -1000, 520, values={'In Min Max': (0.0, 1.0), 'Out Min Max': (0.4, 1.4)})
s.link(nz, 'Out', nzr, 'In')
k = s.node('MultiplyNode', -950, 150); s.link(fr, 'Out', k, 'A'); s.link(fd, 'Out', k, 'B')
k2 = s.node('MultiplyNode', -800, 250); s.link(k, 'Out', k2, 'A'); s.link(nzr, 'Out', k2, 'B')   # ring strength
# screen-space offset along the view-space normal
nv = s.node('NormalVectorNode', -950, -200, extra={'m_Space': VIEW})
str_ = s.pnode(sStr, -950, -80)
o1 = s.node('MultiplyNode', -750, -150); s.link(nv, 'Out', o1, 'A'); s.link(str_, 'Out', o1, 'B')
o2 = s.node('MultiplyNode', -600, -100); s.link(o1, 'Out', o2, 'A'); s.link(k2, 'Out', o2, 'B')
sp = s.node('ScreenPositionNode', -600, -250, extra={'m_ScreenSpaceType': 0})
suv = s.node('AddNode', -450, -200); s.link(sp, 'Out', suv, 'A'); s.link(o2, 'Out', suv, 'B')
sc = s.node('SceneColorNode', -300, -200); s.link(suv, 'Out', sc, 'UV')
tint = s.pnode(sTint, -450, 50)
glow = s.node('MultiplyNode', -300, 50); s.link(tint, 'Out', glow, 'A'); s.link(k2, 'Out', glow, 'B')
col = s.node('AddNode', -150, -100); s.link(sc, 'Out', col, 'A'); s.link(glow, 'Out', col, 'B')
s.to_block(col, 'Out', 'SurfaceDescription.BaseColor')
al = s.node('MultiplyNode', -300, 250, values={'B': 2.5}); s.link(k2, 'Out', al, 'A')
als = s.node('SaturateNode', -150, 250); s.link(al, 'Out', als, 'In')
s.to_block(als, 'Out', 'SurfaceDescription.Alpha')
s.target.update({'m_SurfaceType': 1, 'm_AlphaMode': 0, 'm_ZWriteControl': 2, 'm_RenderFace': 2,
                 'm_CastShadows': False, 'm_ReceiveShadows': False})
s.write(OUT + 'NisseShoutShell.shadergraph')
