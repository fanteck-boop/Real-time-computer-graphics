"""Ex08 (IBL): render an equirectangular HDR environment map of a Nordic Christmas courtyard in Blender (Cycles).

Run from the project folder (Blender 5.3 was used):
    blender -b -P Docs/Ex08_make_environment.py                      (final: 4096 x 2048, 128 samples)
    blender -b -P Docs/Ex08_make_environment.py -- --res 2048 --samples 64   (quick test)

Writes:
    Assets/Textures/Ex8/Ex8_Environment.exr        32-bit float OpenEXR, the full map with the sun disc in it
    Assets/Textures/Ex8/Ex8_Environment_NoSun.exr  the same map with the sun disc switched off (for "sun as a light")
    Captures/Ex08_IBL/env_preview.png              2048 x 1024, tone mapped with AgX
    Captures/Ex08_IBL/step0_map_preview.png        1280 x 720, the map letterboxed, the brightest spot ringed
and prints the brightest region of the EXR (pixel, azimuth, elevation, peak value) as SUN ... lines.

Scene, Blender units are metres, Z is up. The probe (the panoramic camera) stands at (0, 0, 1.0) and looks along +Y,
so +Y is the middle of the image. Azimuth below is measured from +Y towards +X (to the right when you stand at the
probe and look along +Y), elevation from the horizon up.
"""
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector

# ---------------------------------------------------------------- settings
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
def arg(name, default):
    return type(default)(argv[argv.index(name) + 1]) if name in argv else default
RES_X = arg("--res", 4096)            # width, the height is half of it (2:1)
SAMPLES = arg("--samples", 128)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEX_DIR = os.path.join(ROOT, "Assets", "Textures", "Ex8")
CAP_DIR = os.path.join(ROOT, "Captures", "Ex08_IBL")
os.makedirs(TEX_DIR, exist_ok=True); os.makedirs(CAP_DIR, exist_ok=True)

PROBE = Vector((0.0, 0.0, 1.0))
SUN_AZ, SUN_EL = 60.0, 12.0           # degrees: low winter sun, between the yellow house and the red house
SUN_DIAMETER = 1.0                    # degrees (the real sun is 0.53, a little bigger is easier to see in reflections)
SUN_IRRADIANCE = 3.0                  # W/m2 on a surface facing the sun (Blender sun lamp "strength")
SUN_COLOUR = (1.0, 0.70, 0.42)        # warm, late afternoon

def direction(az_deg, el_deg):
    az, el = math.radians(az_deg), math.radians(el_deg)
    return Vector((math.sin(az) * math.cos(el), math.cos(az) * math.cos(el), math.sin(el)))
SUN_DIR = direction(SUN_AZ, SUN_EL)   # pointing from the probe towards the sun

# radiance of the sun disc so that disc radiance * disc solid angle = irradiance of the sun lamp
half = math.radians(SUN_DIAMETER / 2)
SUN_SOLID_ANGLE = 2 * math.pi * (1 - math.cos(half))
SUN_RADIANCE = SUN_IRRADIANCE / SUN_SOLID_ANGLE

# ---------------------------------------------------------------- empty scene, Cycles on the GPU if there is one
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
try:
    scene.render.engine = "CYCLES"
except TypeError as e:
    print("engine error", e)
prefs = bpy.context.preferences.addons["cycles"].preferences
for dev_type in ("OPTIX", "CUDA", "HIP", "METAL", "ONEAPI"):   # the enum is empty until devices are queried, so try
    try:
        prefs.compute_device_type = dev_type
    except TypeError:
        continue
    prefs.get_devices()
    gpus = [d for d in prefs.devices if d.type == dev_type]
    if gpus:
        for d in prefs.devices: d.use = d.type == dev_type
        scene.cycles.device = "GPU"
        print("DEVICE", dev_type, [d.name for d in gpus])
        break
scene.cycles.samples = SAMPLES
scene.cycles.use_denoising = True
scene.cycles.sample_clamp_direct = 0.0      # no clamping: keep the real HDR values
scene.cycles.sample_clamp_indirect = 10.0
scene.render.resolution_x = RES_X
scene.render.resolution_y = RES_X // 2
scene.render.resolution_percentage = 100
scene.render.film_transparent = False

# ---------------------------------------------------------------- helpers
def material(name, colour, rough=0.5, emit=None, strength=0.0, metallic=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*colour, 1)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metallic
    if emit:
        bsdf.inputs["Emission Color"].default_value = (*emit, 1)
        bsdf.inputs["Emission Strength"].default_value = strength
    return m

def add(obj, mat):
    obj.data.materials.append(mat)
    return obj

def box(loc, size, mat, rot_z=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=(0, 0, math.radians(rot_z)))
    o = bpy.context.active_object; o.scale = size
    return add(o, mat)

def cone(loc, r1, r2, depth, mat, verts=24):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=depth, location=loc)
    return add(bpy.context.active_object, mat)

def cyl(loc, r, depth, mat, verts=16):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc)
    return add(bpy.context.active_object, mat)

def ball(loc, r, mat, seg=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=seg // 2, radius=r, location=loc)
    o = bpy.context.active_object
    for p in o.data.polygons: p.use_smooth = True
    return add(o, mat)

def prism(name, length, width, height, mat):
    """Triangular roof prism, ridge along local X, base at z = 0."""
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    L, W = length / 2, width / 2
    v = [bm.verts.new(p) for p in ((-L, -W, 0), (L, -W, 0), (L, W, 0), (-L, W, 0), (-L, 0, height), (L, 0, height))]
    for f in ((0, 1, 5, 4), (2, 3, 4, 5), (0, 4, 3), (1, 2, 5), (0, 3, 2, 1)):
        bm.faces.new([v[i] for i in f])
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); scene.collection.objects.link(o)
    return add(o, mat)

# ---------------------------------------------------------------- materials
snow      = material("Snow", (0.70, 0.74, 0.82), 0.55)
falu_red  = material("FaluRed", (0.42, 0.05, 0.035), 0.8)
ochre     = material("Ochre", (0.78, 0.52, 0.16), 0.8)
white_w   = material("WhiteWood", (0.80, 0.80, 0.76), 0.7)
dark_wood = material("DarkWood", (0.10, 0.06, 0.04), 0.8)
roof_snow = material("RoofSnow", (0.85, 0.88, 0.95), 0.5)
fir       = material("Fir", (0.015, 0.09, 0.035), 0.85)
trunk     = material("Trunk", (0.12, 0.07, 0.04), 0.9)
iron      = material("Iron", (0.03, 0.03, 0.03), 0.4, metallic=1.0)
window    = material("Window", (0, 0, 0), 1.0, (1.0, 0.58, 0.22), 2.5)
lantern   = material("Lantern", (0, 0, 0), 1.0, (1.0, 0.50, 0.16), 15.0)
carrot    = material("Carrot", (0.8, 0.25, 0.02), 0.6)
coal      = material("Coal", (0.01, 0.01, 0.01), 0.5)
bulb_cols = [(1.0, 0.08, 0.05), (1.0, 0.75, 0.2), (0.1, 1.0, 0.2), (0.15, 0.35, 1.0), (1.0, 0.2, 0.8)]
bulbs     = [material("Bulb%d" % i, (0, 0, 0), 1.0, c, 10.0) for i, c in enumerate(bulb_cols)]

# ---------------------------------------------------------------- ground
bpy.ops.mesh.primitive_plane_add(size=600, location=(0, 0, 0))
add(bpy.context.active_object, snow)

# ---------------------------------------------------------------- houses
def house(cx, cy, facing_deg, w, d, h, wall, lights=False):
    """A house centred at (cx, cy), w wide (along its front), d deep, wall height h, front turned towards the probe."""
    parts = []
    parts.append(box((0, 0, h / 2), (w, d, h), wall))
    roof = prism("Roof", w + 0.6, d + 0.8, h * 0.55, roof_snow); roof.location = (0, 0, h); parts.append(roof)
    # windows on the front (local -Y side) and a door
    n = max(2, int(w // 2.2))
    for i in range(n):
        x = -w / 2 + (i + 0.5) * w / n
        if i == n // 2:
            parts.append(box((x, -d / 2 - 0.03, 1.0), (0.9, 0.08, 2.0), dark_wood))   # door
        else:
            parts.append(box((x, -d / 2 - 0.05, h * 0.55), (0.8, 0.06, 1.0), window))     # glowing glass, sticks out
            parts.append(box((x, -d / 2 - 0.02, h * 0.55), (1.0, 0.04, 1.2), white_w))  # white frame just behind it
    if lights:  # a string of fairy lights along the eave
        for i in range(int(w / 0.35)):
            x = -w / 2 + 0.2 + i * 0.35
            parts.append(ball((x, -d / 2 - 0.25, h - 0.05 - 0.08 * math.sin(i * 0.9) ** 2), 0.07, bulbs[i % len(bulbs)], 8))
    empty = bpy.data.objects.new("House", None); scene.collection.objects.link(empty)
    for p in parts: p.parent = empty
    empty.location = (cx, cy, 0)
    empty.rotation_euler = (0, 0, math.radians(facing_deg))
    return empty

def facing(cx, cy):
    """Rotation about Z that turns local -Y (the front) towards the probe."""
    return math.degrees(math.atan2(cx, -cy)) + 180.0

for (cx, cy, w, d, h, wall, lights) in [
    (0.0, 15.0, 10.0, 6.0, 4.2, ochre, True),      # yellow house straight ahead (image centre)
    (13.0, -1.5, 8.0, 6.0, 3.6, falu_red, True),   # red house to the right
    (-13.5, 4.0, 9.0, 6.0, 3.8, falu_red, False),  # red house to the left
    (-6.0, -14.0, 8.0, 5.5, 3.4, white_w, True),   # white house behind, left
    (9.5, -12.5, 12.0, 7.0, 5.0, falu_red, False), # big red barn behind, right
]:
    house(cx, cy, facing(cx, cy), w, d, h, wall, lights)

# ---------------------------------------------------------------- trees
def tree(x, y, height, lights=False):
    cyl((x, y, 0.35), 0.18, 0.7, trunk)
    for k, (z0, r) in enumerate(((0.5, 0.42), (0.38, 0.32), (0.26, 0.22))):
        cone((x, y, 0.6 + height * z0), height * r, 0.0, height * 0.55, fir)
    if lights:   # a spiral of coloured bulbs around the cone
        n = 70
        for i in range(n):
            t = i / n
            z = 0.9 + t * height * 0.82
            r = height * 0.40 * (1 - t) + 0.12
            a = t * 7 * 2 * math.pi
            ball((x + r * math.cos(a), y + r * math.sin(a), z), 0.07, bulbs[i % len(bulbs)], 8)
        ball((x, y, height + 0.9), 0.22, bulbs[1], 12)   # star on top

tree(-3.8, 6.5, 5.0, lights=True)      # the Christmas tree, ahead left
for (x, y, h) in [(-9.5, -6.5, 4.0), (6.0, -7.5, 3.5), (-10.5, 10.0, 4.5), (3.5, -10.0, 3.0), (-1.5, -9.0, 2.6)]:
    tree(x, y, h)

# distant forest on the horizon, kept away from the sun's azimuth
import random
random.seed(8)
for i in range(70):
    az = random.uniform(-180, 180)
    if abs((az - SUN_AZ + 180) % 360 - 180) < 14: continue
    dist = random.uniform(55, 85)
    h = random.uniform(7, 13)
    x, y = dist * math.sin(math.radians(az)), dist * math.cos(math.radians(az))
    cone((x, y, h / 2), h * 0.28, 0.0, h, fir, 10)

# ---------------------------------------------------------------- lanterns and a snowman
for az in (-125, -60, 20, 120, 170):
    x, y = 5.0 * math.sin(math.radians(az)), 5.0 * math.cos(math.radians(az))
    cyl((x, y, 0.8), 0.04, 1.6, iron, 8)
    box((x, y, 1.75), (0.22, 0.22, 0.3), lantern)
    cone((x, y, 1.98), 0.2, 0.0, 0.16, iron, 4)

sx, sy = -2.6, -4.2
for (z, r) in ((0.45, 0.5), (1.15, 0.36), (1.65, 0.25)):
    ball((sx, sy, z), r, snow, 24)
to_probe = (Vector((0, 0, 0)) - Vector((sx, sy, 0))).normalized()
nose = cone((sx + to_probe.x * 0.32, sy + to_probe.y * 0.32, 1.67), 0.05, 0.0, 0.3, carrot, 12)
nose.rotation_euler = to_probe.to_track_quat('Z', 'Y').to_euler()
side = Vector((-to_probe.y, to_probe.x, 0))
for s in (-1, 1):
    ball((sx + to_probe.x * 0.22 + side.x * 0.09 * s, sy + to_probe.y * 0.22 + side.y * 0.09 * s, 1.75), 0.03, coal, 8)

# ---------------------------------------------------------------- sun lamp (lights the courtyard, casts the shadows)
sun_data = bpy.data.lights.new("Sun", "SUN")
sun_data.energy = SUN_IRRADIANCE
sun_data.color = SUN_COLOUR
sun_data.angle = math.radians(SUN_DIAMETER)
sun = bpy.data.objects.new("Sun", sun_data); scene.collection.objects.link(sun)
sun.rotation_euler = SUN_DIR.to_track_quat('Z', 'Y').to_euler()   # the lamp shines along its -Z, i.e. away from SUN_DIR

# ---------------------------------------------------------------- world: evening sky gradient + sun disc
world = bpy.data.worlds.new("EveningSky"); scene.world = world
world.use_nodes = True
nt = world.node_tree; N = nt.nodes; Lk = nt.links
for n in list(N):
    if n.type != "OUTPUT_WORLD": N.remove(n)
out = next(n for n in N if n.type == "OUTPUT_WORLD")

def node(t, **inputs):
    n = N.new(t)
    for k, v in inputs.items(): setattr(n, k, v)
    return n
def math_node(op, a=None, b=None):
    n = node("ShaderNodeMath", operation=op)
    if a is not None: n.inputs[0].default_value = a
    if b is not None: n.inputs[1].default_value = b
    return n

tc = node("ShaderNodeTexCoord")
norm = node("ShaderNodeVectorMath", operation="NORMALIZE"); Lk.new(tc.outputs["Generated"], norm.inputs[0])
sep = node("ShaderNodeSeparateXYZ"); Lk.new(norm.outputs["Vector"], sep.inputs[0])
elev = math_node("MAXIMUM", None, 0.0); Lk.new(sep.outputs["Z"], elev.inputs[0])        # 0 at the horizon, 1 up
sqrt_e = math_node("POWER", None, 0.45); Lk.new(elev.outputs[0], sqrt_e.inputs[0])

ramp = node("ShaderNodeValToRGB"); Lk.new(sqrt_e.outputs[0], ramp.inputs[0])
el_ = ramp.color_ramp.elements
el_[0].position = 0.0; el_[0].color = (0.75, 0.48, 0.42, 1)     # pink-orange horizon
el_[1].position = 1.0; el_[1].color = (0.025, 0.05, 0.16, 1)    # deep blue zenith
mid = el_.new(0.35); mid.color = (0.20, 0.22, 0.40, 1)          # lavender band

dot = node("ShaderNodeVectorMath", operation="DOT_PRODUCT")
Lk.new(norm.outputs["Vector"], dot.inputs[0]); dot.inputs[1].default_value = SUN_DIR
glow_base = math_node("MAXIMUM", None, 0.0); Lk.new(dot.outputs["Value"], glow_base.inputs[0])
glow = math_node("POWER", None, 16.0); Lk.new(glow_base.outputs[0], glow.inputs[0])          # wide warm glow
glow_s = math_node("MULTIPLY", None, 1.2); Lk.new(glow.outputs[0], glow_s.inputs[0])
halo = math_node("POWER", None, 400.0); Lk.new(glow_base.outputs[0], halo.inputs[0])         # tight halo
halo_s = math_node("MULTIPLY", None, 6.0); Lk.new(halo.outputs[0], halo_s.inputs[0])
glow_sum = math_node("ADD"); Lk.new(glow_s.outputs[0], glow_sum.inputs[0]); Lk.new(halo_s.outputs[0], glow_sum.inputs[1])
glow_rgb = node("ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY")
glow_rgb.inputs["Factor"].default_value = 1.0
glow_rgb.inputs[6].default_value = (1.0, 1.0, 1.0, 1); glow_rgb.inputs[7].default_value = (1.0, 0.55, 0.22, 1)
glow_col = node("ShaderNodeVectorMath", operation="SCALE")
Lk.new(glow_rgb.outputs[2], glow_col.inputs[0]); Lk.new(glow_sum.outputs[0], glow_col.inputs["Scale"])

# the disc: 1 inside the sun's angular radius, 0 outside, only for camera rays (the sun lamp does the lighting)
disc = math_node("GREATER_THAN", None, math.cos(half)); Lk.new(dot.outputs["Value"], disc.inputs[0])
lp = node("ShaderNodeLightPath")
disc_cam = math_node("MULTIPLY"); Lk.new(disc.outputs[0], disc_cam.inputs[0]); Lk.new(lp.outputs["Is Camera Ray"], disc_cam.inputs[1])
disc_on = math_node("MULTIPLY", None, SUN_RADIANCE); Lk.new(disc_cam.outputs[0], disc_on.inputs[0])
disc_on.label = "SunDiscStrength"
disc_col = node("ShaderNodeVectorMath", operation="SCALE")
disc_col.inputs[0].default_value = SUN_COLOUR; Lk.new(disc_on.outputs[0], disc_col.inputs["Scale"])

add1 = node("ShaderNodeVectorMath", operation="ADD"); Lk.new(ramp.outputs["Color"], add1.inputs[0]); Lk.new(glow_col.outputs[0], add1.inputs[1])
add2 = node("ShaderNodeVectorMath", operation="ADD"); Lk.new(add1.outputs[0], add2.inputs[0]); Lk.new(disc_col.outputs[0], add2.inputs[1])
bg = node("ShaderNodeBackground"); Lk.new(add2.outputs[0], bg.inputs["Color"]); bg.inputs["Strength"].default_value = 1.0
Lk.new(bg.outputs["Background"], out.inputs["Surface"])

# ---------------------------------------------------------------- the probe: panoramic camera, equirectangular
cam_data = bpy.data.cameras.new("Probe")
cam_data.type = "PANO"
cam_data.panorama_type = "EQUIRECTANGULAR"   # in Blender 4.x and 5.x this lives on the camera data
cam = bpy.data.objects.new("Probe", cam_data); scene.collection.objects.link(cam)
cam.location = PROBE
cam.rotation_euler = (math.radians(90), 0, 0)   # X = 90 degrees: looks along +Y, Z stays up
scene.camera = cam

# ---------------------------------------------------------------- render and save
def set_format(fmt, depth):
    s = scene.render.image_settings
    s.file_format = fmt
    s.color_depth = depth
    if fmt == "OPEN_EXR":
        s.exr_codec = "ZIP"
        s.color_mode = "RGB"
    else:
        s.color_mode = "RGB"

try:
    scene.view_settings.view_transform = "AgX"     # only used for the PNG previews, the EXR stays linear
except TypeError as e:
    print("view transform error", e)

full_exr = os.path.join(TEX_DIR, "Ex8_Environment.exr")
nosun_exr = os.path.join(TEX_DIR, "Ex8_Environment_NoSun.exr")
set_format("OPEN_EXR", "32")
for path, strength in ((full_exr, SUN_RADIANCE), (nosun_exr, 0.0)):
    disc_on.inputs[1].default_value = strength
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("SAVED", path)

# ---------------------------------------------------------------- find the sun in the EXR (brightest region)
img = bpy.data.images.load(full_exr)
w, h = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)      # row 0 is the BOTTOM of the image
lum = 0.2126 * px[..., 0] + 0.7152 * px[..., 1] + 0.0722 * px[..., 2]
peak = float(lum.max())
ys, xs = np.nonzero(lum > 0.5 * peak)
cx, cy = xs.mean() + 0.5, ys.mean() + 0.5
u, v = cx / w, cy / h                       # u from the left, v from the bottom
found_az = (u - 0.5) * 360.0                # image centre = +Y, right half = +X side (checked against SUN_AZ below)
found_el = (v - 0.5) * 180.0
rgb_peak = px[ys, xs, :3].max(axis=0)
print("SUN pixels above half peak: %d, centre pixel x=%.1f y_from_top=%.1f of %dx%d" % (len(xs), cx, h - cy, w, h))
print("SUN u=%.4f v=%.4f -> azimuth %.2f deg, elevation %.2f deg (scene sun: azimuth %.1f, elevation %.1f)"
      % (u, v, found_az, found_el, SUN_AZ, SUN_EL))
print("SUN peak luminance %.1f, peak RGB (%.1f, %.1f, %.1f), disc radiance set %.1f, solid angle %.3e sr"
      % (peak, *rgb_peak, SUN_RADIANCE, SUN_SOLID_ANGLE))
sky = lum[(lum < 50)]
print("SKY mean luminance of the rest of the map %.3f, max %.3f" % (float(sky.mean()), float(sky.max())))
nosun = bpy.data.images.load(nosun_exr)
lum2 = np.array(nosun.pixels[:], dtype=np.float32).reshape(h, w, 4)[..., :3] @ np.array([0.2126, 0.7152, 0.0722], np.float32)
print("NOSUN peak luminance %.2f" % float(lum2.max()))

# ---------------------------------------------------------------- previews (AgX tone mapped PNG)
set_format("PNG", "8")
prev = bpy.data.images.load(full_exr); prev.scale(2048, 1024)
prev_path = os.path.join(CAP_DIR, "env_preview.png")
prev.save_render(prev_path, scene=scene)
print("SAVED", prev_path)

small = bpy.data.images.load(full_exr); small.scale(1280, 640)
tmp = os.path.join(CAP_DIR, "_tmp_small.png"); small.save_render(tmp, scene=scene)
sm = bpy.data.images.load(tmp)
a = np.array(sm.pixels[:], dtype=np.float32).reshape(640, 1280, 4)
canvas = np.zeros((720, 1280, 4), np.float32); canvas[..., 3] = 1.0; canvas[..., :3] = 0.06
canvas[40:680] = a
# ring around the brightest region, so the sun is easy to find
yy, xx = np.mgrid[0:720, 0:1280]
scx, scy = u * 1280, 40 + v * 640
r = np.hypot(xx + 0.5 - scx, yy + 0.5 - scy)
canvas[(r > 14) & (r < 17)] = (0.2, 1.0, 0.3, 1.0)
out_img = bpy.data.images.new("step0", 1280, 720)
out_img.pixels[:] = canvas.ravel()
out_img.filepath_raw = os.path.join(CAP_DIR, "step0_map_preview.png"); out_img.file_format = "PNG"
out_img.save()
os.remove(tmp)
print("SAVED step0_map_preview.png")

# ---------------------------------------------------------------- small copies for the reflection probe
# Unity's Lit shader only walks 6 mip levels down from the top for the roughest surface. From a 2048 cube face that
# stops at 32 x 32, still sharp, so rough spheres would look like mirrors. A 512 x 256 copy gives 256 faces, so the
# roughest surface reads a 4 x 4 face, which is properly blurry.
set_format("OPEN_EXR", "16")
for src in (full_exr, nosun_exr):
    small = bpy.data.images.load(src); small.scale(512, 256)
    dst = src.replace(".exr", "_Reflection.exr")
    small.save_render(dst, scene=scene)
    print("SAVED", dst)
