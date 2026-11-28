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
    sorted(glob.glob(CACHE + '/com.unity.render-pipelines.*/**/*.shadergraph', recursive=True)) + \
    sorted(glob.glob(CACHE + '/com.unity.render-pipelines.core@*/Samples~/**/*.shadergraph', recursive=True))
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
        elif _t.startswith('UnityEditor.ShaderGraph.') and (_short.endswith('Node') or _short == 'RedirectNodeData') and 'm_Slots' in _o \
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
        self.groups, self.cur_group = [], ''    # Shader Graph groups (optional, see set_group / auto_layout)
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
        n['m_Group'] = {'m_Id': self.cur_group}
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
        n['m_Group'] = {'m_Id': self.cur_group}
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

    def redirect(self):
        """Redirect (elbow) node: a pass-through point so a wire can be routed around groups. No effect on the shader."""
        nid_ = self.node('RedirectNodeData', 0, 0)
        self.SLOTS[nid_] = {'In': 0, 'Out': 1}
        return nid_

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

    # ---------- groups and layout ----------
    def set_group(self, title):
        """Nodes created after this call go into a Shader Graph group with this title ('' or None = no group)."""
        if not title:
            self.cur_group = ''
            return
        for g in self.groups:                  # same title again: continue that group
            if g['m_Title'] == title:
                self.cur_group = g['m_ObjectId']
                return
        g = {'m_SGVersion': 0, 'm_Type': 'UnityEditor.ShaderGraph.GroupData', 'm_ObjectId': nid(),
             'm_Title': title, 'm_Position': {'x': 0.0, 'y': 0.0}}
        self.groups.append(g)
        self.cur_group = g['m_ObjectId']

    def auto_layout(self, band_order=None):
        """Replaces the hand-typed node positions with a tidy layout. Only positions change.
        Each group is laid out on its own: every node sits one column right of the furthest node of the same group
        feeding it, and nodes without inputs (properties, Time, Position) sit just left of their first user.
        Groups are horizontal bands stacked top to bottom in band_order (group titles). A group starts to the right of
        every group that feeds it, so every wire, also between groups, runs left to right.
        Node sizes are estimated generously from the slot count and the preview, with wide gaps between columns for
        the value fields Shader Graph draws to the left of unconnected inputs."""
        objs = {o['m_ObjectId']: o for o in self.out}
        nodes = list(self.nodes)
        nset = set(nodes)
        grp = {n: objs[n]['m_Group']['m_Id'] for n in nodes}
        preds = {n: [] for n in nodes}
        succs = {n: [] for n in nodes}
        for e in self.edges:
            a, b = e['m_OutputSlot']['m_Node']['m_Id'], e['m_InputSlot']['m_Node']['m_Id']
            if a in nset and b in nset:
                preds[b].append(a)
                succs[a].append(b)

        def size(n):
            o = objs[n]
            t = o['m_Type'].split('.')[-1]
            sl = [objs[s['m_Id']] for s in o['m_Slots']]
            if t == 'PropertyNode':
                return 80 + 9 * len(sl[0]['m_DisplayName']), 44
            nin = sum(1 for s in sl if s['m_SlotType'] == 0)
            nout = sum(1 for s in sl if s['m_SlotType'] == 1)
            w = {'RemapNode': 300, 'TransformNode': 320, 'SampleTexture2DNode': 280, 'FresnelNode': 280,
                 'PositionNode': 280, 'GradientNoiseNode': 260, 'NoiseNode': 260, 'SmoothstepNode': 260,
                 'VertexColorNode': 260, 'TimeNode': 240}.get(t, 240)
            preview = o.get('m_PreviewExpanded') and o['m_DrawState'].get('m_Expanded', True)
            h = 60 + 28 * max(nin, nout) + (230 if preview else 0)
            return w, h
        sizes = {n: size(n) for n in nodes}
        GAP_X, GAP_Y, GROUP_GAP_X, BAND_GAP = 150, 40, 200, 150

        gids = [g['m_ObjectId'] for t in (band_order or []) for g in self.groups if g['m_Title'] == t]
        gids += [g['m_ObjectId'] for g in self.groups if g['m_ObjectId'] not in gids]
        if any(grp[n] == '' for n in nodes):
            gids.append('')
        gobj = {g['m_ObjectId']: g for g in self.groups}
        members = {g: [n for n in nodes if grp[n] == g] for g in gids}
        gids = [g for g in gids if members[g]]

        # local columns inside each group
        depth = {}

        def d(n):
            if n not in depth:
                lp = [p for p in preds[n] if grp[p] == grp[n]]
                depth[n] = 0 if not lp else 1 + max(d(p) for p in lp)
            return depth[n]
        for n in nodes:
            d(n)
        for n in nodes:
            lp = [p for p in preds[n] if grp[p] == grp[n]]
            ls = [s for s in succs[n] if grp[s] == grp[n]]
            if not lp and ls:
                depth[n] = min(depth[s] for s in ls) - 1
        colx, gwidth = {}, {}
        for g in gids:
            lo = min(depth[n] for n in members[g])
            for n in members[g]:
                depth[n] -= lo
            nc = max(depth[n] for n in members[g]) + 1
            cw = [0] * nc
            for n in members[g]:
                cw[depth[n]] = max(cw[depth[n]], sizes[n][0])
            xs = [0] * (nc + 1)
            for c in range(nc):
                xs[c + 1] = xs[c] + cw[c] + GAP_X
            colx[g] = (xs, cw)
            gwidth[g] = xs[nc] - GAP_X

        # group x: right of every group that feeds it
        feeds = {g: set() for g in gids}
        for n in nodes:
            for p in preds[n]:
                if grp[p] != grp[n]:
                    feeds[grp[n]].add(grp[p])
        gx = {}

        def x_of(g, seen=()):
            if g not in gx:
                assert g not in seen, 'groups feed each other in a loop'
                gx[g] = max([x_of(f, seen + (g,)) + gwidth[f] + GROUP_GAP_X for f in feeds[g]] or [0])
            return gx[g]
        for g in gids:
            x_of(g)

        # bands top to bottom. Inside a band: order each column by where its wires come from (forward pass), then
        # by which input port it feeds (backward pass), so wires cross as little as possible.
        in_port = {}                               # (from node, to node) -> row of the input port on the receiving node
        for e in self.edges:
            a, b = e['m_OutputSlot']['m_Node']['m_Id'], e['m_InputSlot']['m_Node']['m_Id']
            if a in nset and b in nset:
                ins = [objs[s['m_Id']]['m_Id'] for s in objs[b]['m_Slots'] if objs[s['m_Id']]['m_SlotType'] == 0]
                sid = e['m_InputSlot']['m_SlotId']
                in_port[(a, b)] = ins.index(sid) if sid in ins else 0
        y0 = 0.0
        cy, ytop = {}, {}
        for g in gids:
            top = y0 + 70                          # room for the group title
            xs, cw = colx[g]
            cols = [[n for n in members[g] if depth[n] == c] for c in range(len(cw))]

            def stack(col):
                y = top
                for n in col:
                    ytop[n] = y
                    cy[n] = y + sizes[n][1] / 2
                    y += sizes[n][1] + GAP_Y
                return y
            for col in cols:                       # forward: mean height of the nodes feeding it
                def fkey(n):
                    ps = [cy[p] for p in preds[n] if p in cy]
                    return (sum(ps) / len(ps)) if ps else top + nodes.index(n) * 0.001
                col.sort(key=fkey)
                stack(col)
            for col in reversed(cols):             # backward: height of the input ports it feeds in this group
                def bkey(n):
                    ss = [ytop[s] + 50 + 28 * in_port.get((n, s), 0) for s in succs[n] if grp[s] == g]
                    return (sum(ss) / len(ss)) if ss else cy[n]
                col.sort(key=bkey)
                stack(col)
            bottom = top
            for c, col in enumerate(cols):
                for n in col:
                    w = sizes[n][0]
                    objs[n]['m_DrawState']['m_Position'].update({'x': float(gx[g] + xs[c] + (cw[c] - w) / 2), 'y': float(ytop[n])})
                    bottom = max(bottom, ytop[n] + sizes[n][1] + GAP_Y)
            if g:
                gobj[g]['m_Position'] = {'x': float(gx[g] - 30), 'y': float(y0)}
            y0 = bottom + BAND_GAP

        # master stack at the right: vertex context level with the band that feeds it, fragment context below it
        def top_of(prefix):
            ys = [objs[e['m_OutputSlot']['m_Node']['m_Id']]['m_DrawState']['m_Position']['y'] for e in self.edges
                  if e['m_OutputSlot']['m_Node']['m_Id'] in nset
                  and self._block_name(e['m_InputSlot']['m_Node']['m_Id']).startswith(prefix)]
            return min(ys) if ys else 0.0
        x = float(max(gx[g] + gwidth[g] for g in gids) + GROUP_GAP_X)
        vy = top_of('VertexDescription')
        nvb = len([b for b in self.blocks if b.startswith('VertexDescription.')])
        fy = max(vy + 140 + 60 * nvb, top_of('SurfaceDescription'))
        self.graph['m_VertexContext']['m_Position'] = {'x': x, 'y': float(vy)}
        self.graph['m_FragmentContext']['m_Position'] = {'x': x, 'y': float(fy)}

    # ---------- hand-made grid layout ----------
    # Node sizes measured in the Shader Graph window of Unity 6000.6 (width, height in graph units).
    SIZE = {'MultiplyNode': (130, 118), 'AddNode': (130, 117), 'SubtractNode': (126, 117), 'PowerNode': (126, 118),
            'CombineNode': (125, 117), 'LerpNode': (130, 141), 'RemapNode': (128, 93), 'SaturateNode': (128, 94),
            'OneMinusNode': (128, 94), 'SplitNode': (120, 124), 'PositionNode': (206, 130), 'TimeNode': (79, 76),
            'GradientNoiseNode': (181, 152), 'NoiseNode': (181, 152), 'SampleTexture2DNode': (179, 152),
            'FresnelNode': (164, 142), 'SmoothstepNode': (208, 326), 'TransformNode': (212, 154),
            'VertexColorNode': (110, 76), 'RedirectNodeData': (56, 24)}

    def node_size(self, n):
        o = {x['m_ObjectId']: x for x in self.out}[n] if not hasattr(self, '_objs') else self._objs[n]
        t = o['m_Type'].split('.')[-1]
        if t == 'PropertyNode':
            name = [x for x in self.out if x['m_ObjectId'] == o['m_Slots'][0]['m_Id']][0]['m_DisplayName']
            return 80 + 6 * len(name), 34
        return self.SIZE.get(t, (140, 120))

    def set_pos(self, n, x, y):
        o = self._objs[n]
        o['m_DrawState']['m_Position'].update({'x': float(x), 'y': float(y)})

    def grid_layout(self, cells, arrangement, gap_x=100, gap_y=34, group_gap=(170, 120), row_gaps=None, align=None):
        """cells: {group title: {node: (col, row)}}. Inside a group, column widths and row heights come from the
        measured node sizes; nodes are left aligned in their column.
        arrangement: rows of group titles, [[left, right], ...]; groups in the first column are right aligned so
        their outputs end at the same x, the second column is left aligned.
        Returns {title: (x, y, w, h)} of every group's node area."""
        self._objs = {o['m_ObjectId']: o for o in self.out}
        title_id = {g['m_Title']: g for g in self.groups}
        local, size = {}, {}
        for title, cm in cells.items():
            ncol = max(c for c, r in cm.values()) + 1
            nrow = max(r for c, r in cm.values()) + 1
            cw, rh = [0] * ncol, [0] * nrow
            for n, (c, r) in cm.items():
                w, h = self.node_size(n)
                cw[c] = max(cw[c], w)
                rh[r] = max(rh[r], h)
            xs = [sum(cw[:c]) + gap_x * c for c in range(ncol)]
            ys = [sum(rh[:r]) + gap_y * r for r in range(nrow)]
            local[title] = {n: (xs[c], ys[r]) for n, (c, r) in cm.items()}
            size[title] = (xs[-1] + cw[-1], ys[-1] + rh[-1])
        ncols = max(len(r) for r in arrangement)
        colw = [max((size[row[k]][0] for row in arrangement if len(row) > k and row[k]), default=0) for k in range(ncols)]
        colx = [sum(colw[:k]) + group_gap[0] * k for k in range(ncols)]
        rects, y = {}, 0.0
        for ri, row in enumerate(arrangement):
            rh = max(size[t][1] for t in row if t)
            for k, t in enumerate(row):
                if not t:
                    continue
                w, h = size[t]
                x = colx[k] + (colw[k] - w if k == 0 else 0)
                for n, (lx, ly) in local[t].items():
                    self.set_pos(n, x + lx, y + ly)
                rects[t] = (x, y, w, h)
                title_id[t]['m_Position'] = {'x': float(x - 20), 'y': float(y - 60)}
            y += rh + (row_gaps[ri] if row_gaps and ri < len(row_gaps) else group_gap[1])
        self.layout_width = colx[-1] + colw[-1]
        return rects

    def place_context(self, which, x, y):
        self.graph['m_VertexContext' if which == 'vertex' else 'm_FragmentContext']['m_Position'] = {'x': float(x), 'y': float(y)}


    def _block_name(self, oid):
        for name, b in self.blocks.items():
            if b['m_ObjectId'] == oid:
                return name
        return ''

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
        g['m_GroupDatas'] = [ref(x['m_ObjectId']) for x in self.groups]
        objs = [g] + self.props + [cat] + self.groups + vb + fb + self.out + [self.target, self.subtarget]
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
