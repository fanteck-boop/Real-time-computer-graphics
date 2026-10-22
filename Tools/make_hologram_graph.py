"""Writes Assets/Shaders/Ex06/NisseHologram.shadergraph by reusing the node/slot JSON that
Unity's own shipped templates/samples use (donor files in the shadergraph package)."""
import glob, json, re, uuid, copy, os
PKG = glob.glob(r'C:/Users/trist/Desktop/Real-time-computer-graphics/Library/PackageCache/com.unity.shadergraph@*')[0]
OUT = r'C:/Users/trist/Desktop/Real-time-computer-graphics/Assets/Shaders/Ex06/NisseHologram.shadergraph'


def load(f):
    s = open(f, encoding='utf-8').read()
    return [json.loads(p) for p in re.split(r'\n\n(?=\{)', s)]


def nid(): return uuid.uuid4().hex


donors = {}
for f in glob.glob(PKG + '/**/*.shadergraph', recursive=True):
    objs = load(f); by = {o['m_ObjectId']: o for o in objs}
    for o in objs:
        t = o['m_Type'].split('.')[-1]
        if t.endswith('Node') and 'm_Slots' in o and t not in donors and o['m_Type'].startswith('UnityEditor.ShaderGraph.'):
            donors[t] = (o, [by[x['m_Id']] for x in o['m_Slots']])
tmpl = [x for x in glob.glob(PKG + '/GraphTemplates/**/*.shadergraph', recursive=True) if 'Unlit Simple' in x][0]
T = load(tmpl); tby = {o['m_ObjectId']: o for o in T}
graph = copy.deepcopy(T[0])

out = []
nodes = []
edges = []
SLOTS = {}   # object id -> {display name: slot id}


def ref(i): return {'m_Id': i}


def add_node(tname, x, y, values=None, extra=None):
    n, sl = donors[tname]
    n = copy.deepcopy(n); n['m_ObjectId'] = nid()
    n['m_DrawState']['m_Position'].update({'x': x, 'y': y})
    n['m_Slots'] = []
    for s in sl:
        s = copy.deepcopy(s); s['m_ObjectId'] = nid()
        if values and s['m_DisplayName'] in values:
            v = values[s['m_DisplayName']]['x']
            s['m_Value'] = {k: v for k in s['m_Value']} if isinstance(s['m_Value'], dict) else v
        out.append(s); n['m_Slots'].append(ref(s['m_ObjectId']))
    if extra: n.update(extra)
    out.append(n); nodes.append(n['m_ObjectId'])
    SLOTS[n['m_ObjectId']] = {s['m_DisplayName']: s['m_Id'] for s in sl}
    return n['m_ObjectId']


def v4(v): return {'x': v, 'y': v, 'z': v, 'w': v}


def connect(a, aslot, b, bslot):
    edges.append({'m_OutputSlot': {'m_Node': ref(a), 'm_SlotId': SLOTS[a][aslot]},
                  'm_InputSlot': {'m_Node': ref(b), 'm_SlotId': SLOTS[b][bslot]}})


def connect_block(a, aslot, blk):
    edges.append({'m_OutputSlot': {'m_Node': ref(a), 'm_SlotId': SLOTS[a][aslot]},
                  'm_InputSlot': {'m_Node': ref(blk), 'm_SlotId': 0}})


# ---- properties ----
colorProp = copy.deepcopy(tby['e1167edcb64d4545a552ffaad7339a33'])
colorProp['m_Name'] = 'Hologram Color'; colorProp['m_RefNameGeneratedByDisplayName'] = 'Hologram Color'
colorProp['m_DefaultReferenceName'] = '_Hologram_Color'; colorProp['m_OverrideReferenceName'] = '_HoloColor'
colorProp['m_Value'] = {'r': 0.1, 'g': 0.75, 'b': 1.0, 'a': 1.0}
colorProp['m_Guid']['m_GuidSerialized'] = str(uuid.uuid4())
fl = load(glob.glob(PKG + '/GraphTemplates/BuiltIn/BuiltIn Lit Basic.shadergraph')[0])
floatTpl = [o for o in fl if o['m_Type'].endswith('Vector1ShaderProperty')][0]


def fprop(name, ref_, val, lo, hi):
    p = copy.deepcopy(floatTpl); p['m_ObjectId'] = nid(); p['m_Guid']['m_GuidSerialized'] = str(uuid.uuid4())
    p['m_Name'] = name; p['m_RefNameGeneratedByDisplayName'] = name; p['m_DefaultReferenceName'] = ref_
    p['m_OverrideReferenceName'] = ref_; p['m_Value'] = val; p['m_FloatType'] = 1
    p['m_RangeValues'] = {'x': lo, 'y': hi}; p['m_SliderType'] = 0; p['m_EnumNames'] = ['Default']
    return p


pPow = fprop('Rim Power', '_RimPower', 2.5, 0.5, 8.0)
pDen = fprop('Scanline Density', '_ScanDensity', 90.0, 5.0, 250.0)
pSpd = fprop('Scroll Speed', '_ScrollSpeed', 0.4, -3.0, 3.0)
props = [colorProp, pPow, pDen, pSpd]
out += props
graph['m_Properties'] = [ref(p['m_ObjectId']) for p in props]
cat = copy.deepcopy(tby['ff02231eb4ff497eb720a5799fe258bf'])
cat['m_ChildObjectList'] = [ref(p['m_ObjectId']) for p in props]
out.append(cat); graph['m_CategoryData'] = [ref(cat['m_ObjectId'])]

pn = tby['30d45b0432b54383ac099a71b07d4ace']; pnSlot = tby['846a708b88174656a22c067f1b196c82']
floatSlotTpl = [s for s in donors['SplitNode'][1] if s['m_DisplayName'] == 'R'][0]


def add_prop_node(p, x, y):
    n = copy.deepcopy(pn); n['m_ObjectId'] = nid(); n['m_DrawState']['m_Position'].update({'x': x, 'y': y})
    n['m_Property'] = ref(p['m_ObjectId'])
    s = copy.deepcopy(pnSlot if p is colorProp else floatSlotTpl)
    if p is not colorProp:
        s['m_DisplayName'] = 'Out'; s['m_ShaderOutputName'] = 'Out'; s['m_SlotType'] = 1; s['m_Id'] = 0
    s['m_ObjectId'] = nid(); out.append(s); n['m_Slots'] = [ref(s['m_ObjectId'])]
    out.append(n); nodes.append(n['m_ObjectId'])
    SLOTS[n['m_ObjectId']] = {'Out': s['m_Id']}
    return n['m_ObjectId']


# ---- fragment graph ----
pos = add_node('PositionNode', -1400, 0, extra={'m_Space': 2, 'm_PositionSource': 0})
split = add_node('SplitNode', -1250, 0)
t = add_node('TimeNode', -1400, 160)
dens = add_prop_node(pDen, -1250, 120); spd = add_prop_node(pSpd, -1250, 200)
mY = add_node('MultiplyNode', -1100, 0)
mT = add_node('MultiplyNode', -1100, 160)
addYT = add_node('AddNode', -950, 60)
frac = add_node('FractionNode', -820, 60)
step = add_node('StepNode', -700, 60, {'Edge': v4(0.5)})
mScan = add_node('MultiplyNode', -560, 60, {'B': v4(0.7)})
fres = add_node('FresnelNode', -1100, 320)
rim = add_prop_node(pPow, -1250, 380)
addA = add_node('AddNode', -420, 120)
addB = add_node('AddNode', -280, 120, {'B': v4(0.05)})
sat = add_node('SaturateNode', -150, 120)
mF = add_node('MultiplyNode', -560, 330, {'B': v4(2.0)})
addC = add_node('AddNode', -420, 330, {'B': v4(0.5)})
tint = add_prop_node(colorProp, -420, 440)
mC = add_node('MultiplyNode', -280, 380)

connect(pos, 'Out', split, 'In')
connect(split, 'G', mY, 'A'); connect(dens, 'Out', mY, 'B')
connect(t, 'Time', mT, 'A'); connect(spd, 'Out', mT, 'B')
connect(mY, 'Out', addYT, 'A'); connect(mT, 'Out', addYT, 'B')
connect(addYT, 'Out', frac, 'In'); connect(frac, 'Out', step, 'In')
connect(step, 'Out', mScan, 'A')
connect(rim, 'Out', fres, 'Power')
connect(mScan, 'Out', addA, 'A'); connect(fres, 'Out', addA, 'B')
connect(addA, 'Out', addB, 'A'); connect(addB, 'Out', sat, 'In')
connect(fres, 'Out', mF, 'A'); connect(mF, 'Out', addC, 'A')
connect(tint, 'Out', mC, 'A'); connect(addC, 'Out', mC, 'B')

blocks = [o for o in T if o['m_Type'].endswith('BlockNode')]
bn = {b['m_Name']: b for b in blocks}
connect_block(mC, 'Out', bn['SurfaceDescription.BaseColor']['m_ObjectId'])
connect_block(sat, 'Out', bn['SurfaceDescription.Alpha']['m_ObjectId'])
emi = bn['SurfaceDescription.Emission']
blocks = [b for b in blocks if b is not emi]
for b in blocks:
    for x in b['m_Slots']: out.append(tby[x['m_Id']])
out += blocks
graph['m_FragmentContext']['m_Blocks'] = [ref(bn['SurfaceDescription.BaseColor']['m_ObjectId']),
                                           ref(bn['SurfaceDescription.Alpha']['m_ObjectId'])]
graph['m_Nodes'] = [ref(b['m_ObjectId']) for b in blocks] + [ref(i) for i in nodes]
graph['m_Edges'] = edges
graph['m_Path'] = 'RTCG'

target = copy.deepcopy(tby['4cea9c2856c146a2b910b1d7e03fad96']); sub = tby['ce2204bd47514b1e9e0688249c8e6068']
target['m_SurfaceType'] = 1; target['m_AlphaMode'] = 2; target['m_ZWriteControl'] = 2; target['m_RenderFace'] = 0
target['m_CastShadows'] = False; target['m_ReceiveShadows'] = False
out += [target, sub]; graph['m_ActiveTargets'] = [ref(target['m_ObjectId'])]

seen = set(); final = []
for o in [graph] + out:
    if o['m_ObjectId'] in seen: continue
    seen.add(o['m_ObjectId']); final.append(o)
txt = '\n\n'.join(json.dumps(o, indent=4) for o in final) + '\n'
os.makedirs(os.path.dirname(OUT), exist_ok=True)
open(OUT, 'w', encoding='utf-8', newline='\n').write(txt)
print('wrote', OUT, len(final), 'objects', len(edges), 'edges')
