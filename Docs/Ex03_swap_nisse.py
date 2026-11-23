"""Ex3 Blender: replace the Nisse mesh in nisse_blender_product.blend with the updated scan and re-render.
Run: blender -b Captures/Ex03_Blender/nisse_blender_product.blend -P Docs/Ex03_swap_nisse.py -- <model.glb> <out.png>
Same placement as before: 0.30 m tall, centred in XY, lowest point on Z=0, roughness 0.55."""
import bpy, sys, mathutils
glb, out = sys.argv[sys.argv.index("--") + 1:][:2]
old = bpy.data.objects["Nisse"]
rot = old.rotation_euler.copy()
oldloc = old.location.copy()
before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=glb)
new = [o for o in bpy.data.objects if o not in before and o.type == 'MESH'][0]
for o in [o for o in bpy.data.objects if o not in before and o.type != 'MESH']:
    pass
# flatten any parent from the importer, bake transforms
bpy.context.view_layer.objects.active = new
for o in bpy.context.selected_objects: o.select_set(False)
new.select_set(True)
mw = new.matrix_world.copy(); new.parent = None; new.matrix_world = mw
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in [o for o in bpy.data.objects if o not in before and o is not new]: bpy.data.objects.remove(o)
bpy.data.objects.remove(old)
new.name = "Nisse"
s = 0.30 / new.dimensions.z
new.scale = (s, s, s); bpy.ops.object.transform_apply(scale=True)
bb = [new.matrix_world @ mathutils.Vector(c) for c in new.bound_box]
cx = sum(v.x for v in bb) / 8; cy = sum(v.y for v in bb) / 8; mz = min(v.z for v in bb)
new.location = (-cx, -cy, -mz)
bpy.ops.object.transform_apply(location=True)
for p in new.data.polygons: p.use_smooth = True
for slot in new.material_slots:
    bsdf = next((n for n in slot.material.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf: bsdf.inputs["Roughness"].default_value = 0.55
print("NEW", new.name, tuple(round(x, 3) for x in new.dimensions), len(new.data.polygons))
sc = bpy.context.scene
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_mainfile()
