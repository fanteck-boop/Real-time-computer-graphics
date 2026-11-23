"""Small helper for writing Unity Shader Graph (.shadergraph) files from Python.

Same idea as make_hologram_graph.py: every node, slot, property and block is copied from a donor
.shadergraph that ships inside Library/PackageCache, so the JSON layout always matches what this
Unity version expects. Only ids, positions, default values and edges are changed here.

Used by make_shout_graph.py and make_shadow_graph.py.
"""
import glob, json, re, uuid, copy, os

ROOT = r'C:/Users/trist/Desktop/Real-time-computer-graphics'
CACHE = ROOT + '/Library/PackageCache'
SG_PKG = glob.glob(CACHE + '/com.unity.shadergraph@*')[0]

# CoordinateSpace enum used by Position / Normal Vector / Transform nodes
OBJECT, VIEW, WORLD, TANGENT, ABS_WORLD = 0, 1, 2, 3, 4


def load(f):
    s = open(f, encoding='utf-8').read()
    try:
        return [json.loads(p) for p in re.split(r'\n\n(?=\{)', s)]
    except Exception:
        return []


def nid():
    return uuid.uuid4().hex


def ref(i):
    return {'m_Id': i}


# ---------------- donor scan ----------------
_DON_NODES = {}      # node type -> (node json, [slot json])
_DON_PROPS = {}      # property type -> property json
_DON_PNSLOT = {}     # property type -> slot json used by a PropertyNode of that property
_DON_BLOCKS = {}     # block name -> (block json, [slot json])
_files = sorted(glob.glob(SG_PKG + '/**/*.shadergraph', recursive=True)) + \
    sorted(glob.glob(CACHE + '/com.unity.render-pipelines.*/**/*.shadergraph', recursive=True))
for _f in _files:
    _objs = load(_f)
    _by = {o.get('m_ObjectId'): o for o in _objs}
    for _o in _objs:
        _t = _o.get('m_Type', '')
        _short = _t.split('.')[-1]
        if _short == 'BlockNode' and _o['m_Name'] not in _DON_BLOCKS:
            _DON_BLOCKS[_o['m_Name']] = (_o, [_by[x['m_Id']] for x in _o['m_Slots']])
        elif _short == 'PropertyNode':
            _p = _by.get(_o['m_Property']['m_Id'])
            if _p:
                _pt = _p['m_Type'].split('.')[-1]
                if _pt not in _DON_PNSLOT:
                    _DON_PNSLOT[_pt] = _by[_o['m_Slots'][0]['m_Id']]
                    _DON_NODES.setdefault('PropertyNode', (_o, []))
        elif _t.startswith('UnityEditor.ShaderGraph.') and _short.endswith('Node') and 'm_Slots' in _o \
                and _short not in _DON_NODES:
            _DON_NODES[_short] = (_o, [_by[x['m_Id']] for x in _o['m_Slots']])
        if _t.endswith('ShaderProperty') and _short not in _DON_PROPS:
            _DON_PROPS[_short] = _o


def _set_value(slot, v):
    """Set a slot default. v is a float (broadcast) or a tuple (x, y, z, w)."""
    cur = slot['m_Value']
    if isinstance(cur, dict):
        keys = list(cur.keys())
        if isinstance(v, (int, float)):
            slot['m_Value'] = {k: float(v) for k in keys}
        else:
            nv = dict(cur)
            for k, x in zip(keys, v):
                nv[k] = float(x)
            slot['m_Value'] = nv
    else:
        slot['m_Value'] = float(v)


class Graph:
    def __init__(self, kind='lit', path='RTCG'):
        tmpl = {'lit': '0_Lit Basic', 'unlit': 'Unlit Simple'}[kind]
        f = glob.glob(SG_PKG + '/GraphTemplates/Cross Pipeline/%s.shadergraph' % tmpl)[0]
        T = load(f)
        self.tby = {o['m_ObjectId']: o for o in T}
        self.graph = copy.deepcopy(T[0])
        self.graph['m_Path'] = path
        self.out, self.nodes, self.edges, self.props = [], [], [], []
        self.SLOTS = {}
        # blocks from the template (drop HDRP-only Bent Normal)
        self.blocks = {}
        for b in self.graph['m_VertexContext']['m_Blocks'] + self.graph['m_FragmentContext']['m_Blocks']:
            o = self.tby[b['m_Id']]
            if o['m_Name'] == 'SurfaceDescription.BentNormal':
                continue
            self.blocks[o['m_Name']] = o
            for s in o['m_Slots']:
                self.out.append(self.tby[s['m_Id']])
        # URP target only
        ut = [o for o in T if o['m_Type'].endswith('.UniversalTarget')][0]
        self.target = copy.deepcopy(ut)
        self.subtarget = copy.deepcopy(self.tby[ut['m_ActiveSubTarget']['m_Id']])
        self.base_map = None
        if kind == 'lit':
            bm = [o for o in T if o['m_Type'].endswith('Texture2DShaderProperty') and o['m_Name'] == 'Base Map'][0]
            self.base_map = copy.deepcopy(bm)
            self.props.append(self.base_map)

    # ---------- properties ----------
    def _prop(self, ptype, name, refname):
        p = copy.deepcopy(_DON_PROPS[ptype])
        p['m_ObjectId'] = nid()
        p['m_Guid'] = {'m_GuidSerialized': str(uuid.uuid4())}
        p['m_Name'] = name
        p['m_RefNameGeneratedByDisplayName'] = name
        p['m_DefaultReferenceName'] = '_' + name.replace(' ', '_')
        p['m_OverrideReferenceName'] = refname
        p['m_Hidden'] = False
        p['m_GeneratePropertyBlock'] = True
        self.props.append(p)
        return p

    def prop_float(self, name, refname, val, lo=None, hi=None):
        p = self._prop('Vector1ShaderProperty', name, refname)
        p['m_Value'] = float(val)
        if lo is not None:
            p['m_FloatType'] = 1
            p['m_RangeValues'] = {'x': float(lo), 'y': float(hi)}
        else:
            p['m_FloatType'] = 0
        p['m_SliderType'] = 0
        p['m_EnumNames'] = ['Default']
        return p

    def prop_color(self, name, refname, rgba, hdr=True):
        p = self._prop('ColorShaderProperty', name, refname)
        p['m_Value'] = {'r': rgba[0], 'g': rgba[1], 'b': rgba[2], 'a': rgba[3]}
        p['m_ColorMode'] = 1 if hdr else 0
        return p

    def prop_vec3(self, name, refname, xyz):
        p = self._prop('Vector3ShaderProperty', name, refname)
        p['m_Value'] = {'x': xyz[0], 'y': xyz[1], 'z': xyz[2], 'w': 0.0}
        return p

    def prop_tex(self, name, refname):
        p = self._prop('Texture2DShaderProperty', name, refname)
        p['m_Value'] = {'m_SerializedTexture': '', 'm_Guid': ''}
        p['isMainTexture'] = False
        p['m_DefaultType'] = 0
        return p

    # ---------- nodes ----------
    def node(self, tname, x, y, values=None, extra=None):
        n, sl = _DON_NODES[tname]
        n = copy.deepcopy(n)
        n['m_ObjectId'] = nid()
        n['m_Group'] = {'m_Id': ''}
        n['m_DrawState']['m_Position'].update({'x': float(x), 'y': float(y)})
        n['m_Slots'] = []
        names = {}
        for s in sl:
            s = copy.deepcopy(s)
            s['m_ObjectId'] = nid()
            if values and s['m_DisplayName'] in values:
                _set_value(s, values[s['m_DisplayName']])
            self.out.append(s)
            n['m_Slots'].append(ref(s['m_ObjectId']))
            names[s['m_DisplayName']] = s['m_Id']
        if extra:
            n.update(extra)
        self.out.append(n)
        self.nodes.append(n['m_ObjectId'])
        self.SLOTS[n['m_ObjectId']] = names
        return n['m_ObjectId']

    def pnode(self, prop, x, y):
        """Property node (the blackboard property dragged into the graph)."""
        n = copy.deepcopy(_DON_NODES['PropertyNode'][0])
        n['m_ObjectId'] = nid()
        n['m_Group'] = {'m_Id': ''}
        n['m_DrawState']['m_Position'].update({'x': float(x), 'y': float(y)})
        n['m_Property'] = ref(prop['m_ObjectId'])
        s = copy.deepcopy(_DON_PNSLOT[prop['m_Type'].split('.')[-1]])
        s['m_ObjectId'] = nid()
        s['m_DisplayName'] = prop['m_Name']
        s['m_ShaderOutputName'] = 'Out'
        self.out.append(s)
        n['m_Slots'] = [ref(s['m_ObjectId'])]
        self.out.append(n)
        self.nodes.append(n['m_ObjectId'])
        self.SLOTS[n['m_ObjectId']] = {'Out': s['m_Id']}
        return n['m_ObjectId']

    def link(self, a, aslot, b, bslot):
        self.edges.append({'m_OutputSlot': {'m_Node': ref(a), 'm_SlotId': self.SLOTS[a][aslot]},
                           'm_InputSlot': {'m_Node': ref(b), 'm_SlotId': self.SLOTS[b][bslot]}})

    def block(self, name):
        """Get (or add from a donor) a master-stack block, returns its object id."""
        if name not in self.blocks:
            b, sl = _DON_BLOCKS[name]
            b = copy.deepcopy(b)
            b['m_ObjectId'] = nid()
            b['m_Slots'] = []
            for s in sl:
                s = copy.deepcopy(s)
                s['m_ObjectId'] = nid()
                self.out.append(s)
                b['m_Slots'].append(ref(s['m_ObjectId']))
            self.blocks[name] = b
        return self.blocks[name]['m_ObjectId']

    def block_value(self, name, v):
        b = self.blocks[name] if name in self.blocks else None
        if b is None:
            self.block(name)
            b = self.blocks[name]
        sid = b['m_Slots'][0]['m_Id']
        for s in self.out:
            if s['m_ObjectId'] == sid:
                _set_value(s, v)

    def to_block(self, a, aslot, name):
        bid = self.block(name)
        self.edges.append({'m_OutputSlot': {'m_Node': ref(a), 'm_SlotId': self.SLOTS[a][aslot]},
                           'm_InputSlot': {'m_Node': ref(bid), 'm_SlotId': 0}})

    # ---------- write ----------
    def write(self, path):
        g = self.graph
        g['m_Properties'] = [ref(p['m_ObjectId']) for p in self.props]
        cat = {'m_SGVersion': 0, 'm_Type': 'UnityEditor.ShaderGraph.CategoryData', 'm_ObjectId': nid(),
               'm_Name': '', 'm_ChildObjectList': [ref(p['m_ObjectId']) for p in self.props]}
        g['m_CategoryData'] = [ref(cat['m_ObjectId'])]
        vb = [b for n, b in self.blocks.items() if n.startswith('VertexDescription.')]
        fb = [b for n, b in self.blocks.items() if n.startswith('SurfaceDescription.')]
        g['m_VertexContext']['m_Blocks'] = [ref(b['m_ObjectId']) for b in vb]
        g['m_FragmentContext']['m_Blocks'] = [ref(b['m_ObjectId']) for b in fb]
        g['m_Nodes'] = [ref(b['m_ObjectId']) for b in vb + fb] + [ref(i) for i in self.nodes]
        g['m_Edges'] = self.edges
        g['m_ActiveTargets'] = [ref(self.target['m_ObjectId'])]
        g['m_StickyNoteDatas'] = []
        g['m_GroupDatas'] = []
        objs = [g] + self.props + [cat] + vb + fb + self.out + [self.target, self.subtarget]
        seen, final = set(), []
        for o in objs:
            if o['m_ObjectId'] in seen:
                continue
            seen.add(o['m_ObjectId'])
            final.append(o)
        txt = '\n\n'.join(json.dumps(o, indent=4) for o in final) + '\n'
        os.makedirs(os.path.dirname(path), exist_ok=True)
        open(path, 'w', encoding='utf-8', newline='\n').write(txt)
        print('wrote', path, len(final), 'objects', len(self.edges), 'edges', len(self.nodes), 'nodes')
