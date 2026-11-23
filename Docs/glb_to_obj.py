"""Convert a single-mesh glb (positions, normals, uv) to the OBJ layout used for Nisse.obj.
Usage: python glb_to_obj.py <in.glb> <out.obj>
Vertices are kept as in the glb (no welding), UVs are written as stored in the glb (verified against the first Nisse.obj),
face indices use v/vt/vn with the same index."""
import json, struct, sys
import numpy as np
src, dst = sys.argv[1:3]
d = open(src, 'rb').read()
jl = struct.unpack('<I', d[12:16])[0]
j = json.loads(d[20:20 + jl])
bin0 = 20 + jl + 8
buf = d[bin0:]
def acc(i):
    a = j['accessors'][i]; bv = j['bufferViews'][a['bufferView']]
    n = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3}[a['type']]
    dt = {5126: np.float32, 5125: np.uint32, 5123: np.uint16}[a['componentType']]
    off = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    stride = bv.get('byteStride', 0)
    if stride and stride != n * np.dtype(dt).itemsize:
        raise SystemExit('interleaved buffer not handled')
    return np.frombuffer(buf, dt, a['count'] * n, off).reshape(-1, n)
p = j['meshes'][0]['primitives'][0]
pos = acc(p['attributes']['POSITION']); nor = acc(p['attributes']['NORMAL']); uv = acc(p['attributes']['TEXCOORD_0']).copy()
idx = acc(p['indices']).reshape(-1, 3) + 1
# the old Nisse.obj used the raw glb v (verified against it), so no flip here
with open(dst, 'w', newline='\n') as f:
    f.write('\n'.join('v %.6f %.6f %.6f' % tuple(r) for r in pos) + '\n')
    f.write('\n'.join('vt %.6f %.6f' % tuple(r) for r in uv) + '\n')
    f.write('\n'.join('vn %.5f %.5f %.5f' % tuple(r) for r in nor) + '\n')
    f.write('\n'.join('f %d/%d/%d %d/%d/%d %d/%d/%d' % (a, a, a, b, b, b, c, c, c) for a, b, c in idx) + '\n')
print('verts', len(pos), 'tris', len(idx))
