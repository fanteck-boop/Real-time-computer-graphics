"""Ex08/Ex09: make LOD meshes of the Nisse scan with Blender's Decimate (collapse) modifier.
Run: blender -b -P Ex08_make_lods.py -- <in.obj> <out_dir>
Steps: import, weld vertices by position (UVs stay as per-loop data so UV seams survive), smooth shade,
collapse-decimate to 10% (~25k tris), 2% (~5k tris) and 0.5% (~1.3k tris), export OBJ with UVs and normals, same axes as the source."""
import bpy, sys, os, bmesh
src, out = sys.argv[sys.argv.index("--") + 1:][:2]
os.makedirs(out, exist_ok=True)
for name, ratio in (("Nisse_LOD1", 0.10), ("Nisse_LOD2", 0.02), ("Nisse_LOD3", 0.005)):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.obj_import(filepath=src)
    ob = bpy.context.selected_objects[0]; bpy.context.view_layer.objects.active = ob
    me = ob.data
    print(name, "imported verts", len(me.vertices), "tris", len(me.polygons))
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bm.to_mesh(me); bm.free()
    print(name, "welded verts", len(me.vertices))
    for p in me.polygons: p.use_smooth = True
    m = ob.modifiers.new("dec", "DECIMATE"); m.decimate_type = "COLLAPSE"; m.ratio = ratio
    bpy.ops.object.modifier_apply(modifier="dec")
    print(name, "decimated verts", len(ob.data.vertices), "tris", len(ob.data.polygons))
    bpy.ops.wm.obj_export(filepath=os.path.join(out, name + ".obj"), export_selected_objects=True,
                          export_uv=True, export_normals=True, export_materials=False, apply_modifiers=True,
                          forward_axis="NEGATIVE_Z", up_axis="Y")
