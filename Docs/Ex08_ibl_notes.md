# Ex 8: Image-based lighting from a home-made environment map

The task: get an equirectangular environment map and use it in Unity to show the image-based lighting ideas from the lecture. I rendered the map myself in Blender (a Nordic Christmas courtyard at sunset) and built a Unity scene where each IBL ingredient can be switched on and off: the background, the diffuse light, the reflections, the sun as a real light, and rotating the whole environment.

Files:

- `Docs/Ex08_make_environment.py`: the Blender script that builds the courtyard and renders the map.
- `Assets/Textures/Ex8/Ex8_Environment.exr`: the map, 4096 x 2048, 32-bit float OpenEXR, sun included.
- `Assets/Textures/Ex8/Ex8_Environment_NoSun.exr`: the same map with the sun disc switched off.
- `Assets/Textures/Ex8/Ex8_Environment_Reflection.exr` and `..._NoSun_Reflection.exr`: 512 x 256 copies for the reflection probe (see "What went wrong").
- `Assets/Scripts/Ex8/IblController.cs`, `OrbitCamera.cs`, `IblHud.cs`: the runtime scripts.
- `Assets/Editor/RtcgEx8Scene.cs`: the editor script with `ImportMap`, `BuildScene` and `CaptureSteps`.
- `Assets/Scenes/Ex 8 IBL.unity`: the scene. Materials, the volume profile and a small black cubemap are in `Assets/Materials/Ex8/`.
- `Captures/Ex08_IBL/`: the pictures referred to below.

## 1. Making the map in Blender

![the map](../Captures/Ex08_IBL/env_preview.png)

### The scene

Everything is made from primitives by the script, so you can rerun it and get the same map. Units are metres and Z is up.

- Snow ground: a 600 m plane, colour (0.70, 0.74, 0.82), roughness 0.55.
- Five houses around the courtyard, 13 to 15 m from the centre: an ochre one straight ahead, three falu red ones and a white one behind. Walls are boxes, roofs are triangular prisms with white snow material, the windows are emissive boxes (colour (1.0, 0.58, 0.22), strength 2.5) with a white frame behind them. Three houses have a string of coloured bulbs along the eave.
- A Christmas tree 7 m away (three stacked cones on a trunk) with a spiral of 70 coloured bulbs and a star. Five smaller firs, and about 60 dark cones 55 to 85 m away as a forest on the horizon. No forest is placed within 14 degrees of the sun's direction so it never hides the sun.
- Five lanterns on iron posts 5 m from the centre (emissive, strength 15) and a snowman.
- Bulbs have emission strength 10, so they are well above 1.0 in the HDR file and show up as bright points in reflections.

### The sky and the sun

The world shader is a colour ramp from a pink-orange horizon through a lavender band to a deep blue zenith, plus a warm glow around the sun direction (a wide glow, dot(direction, sun)^16 x 1.2, and a tight halo, dot^400 x 6).

The sun has two parts, and they must agree:

1. A sun lamp: strength 3.0 W/m2, colour (1.0, 0.70, 0.42), angle 1 degree. It lights the courtyard and casts the long shadows you see in the map.
2. A sun disc in the world shader, so the camera actually sees a sun. Its angular diameter is 1 degree (the real sun is 0.53; a bigger disc is easier to see in reflections). Its radiance is set so that disc radiance x disc solid angle = the lamp's 3.0 W/m2. The solid angle of a 1 degree disc is 2 pi (1 - cos 0.5 degrees) = 2.39e-4 sr, so the disc radiance is 3.0 / 2.39e-4 = 12540. The disc is multiplied by the Light Path node's "Is Camera Ray", so it only shows in the picture and does not light the scene a second time.

Sun direction: azimuth 60 degrees (measured from +Y, the image centre, towards +X) and elevation 12 degrees, a low December sun between the ochre house and the red house.

### The camera (the probe)

- Add a camera at (0, 0, 1.0), which is where the display stands in Unity.
- Camera type Panoramic, panorama type Equirectangular. In Blender 5.3 this is `camera.data.type = "PANO"` and `camera.data.panorama_type = "EQUIRECTANGULAR"` on the camera data.
- Rotation X = 90 degrees, Y = 0, Z = 0. The camera then looks along +Y and Z stays up, so +Y lands in the middle of the image.
- Resolution 4096 x 2048 (2:1). For quick tests I used 2048 x 1024 with 64 samples.

### Render settings

- Cycles on the GPU (OptiX on the RTX 4070 Super), 128 samples, denoise on.
- No clamping of direct light (`sample_clamp_direct = 0`), so the sun keeps its real value.
- Output: OpenEXR, colour depth 32 (float), codec ZIP, RGB. The EXR is linear and is not affected by the view transform.
- Each 4096 x 2048 render took about 20 seconds. The script renders the map twice: once with the sun disc, once with the disc strength set to 0 for the no-sun version.
- The PNG previews use the AgX view transform.

Run it from the project folder:

```
"D:\SteamLibrary\steamapps\common\Blender\blender.exe" -b -P Docs/Ex08_make_environment.py
"D:\SteamLibrary\steamapps\common\Blender\blender.exe" -b -P Docs/Ex08_make_environment.py -- --res 2048 --samples 64
```

### Finding the sun in the map

The script loads the EXR again, finds the brightest pixel and takes the centre of all pixels brighter than half of it. Result:

- 101 pixels, centre at x = 2730.7, y = 887.5 (from the top) in the 4096 x 2048 image, so u = 0.6667 and v = 0.5667 (from the bottom).
- Azimuth = (u - 0.5) x 360 = 60.00 degrees, elevation = (v - 0.5) x 180 = 12.00 degrees. That matches the lamp exactly, which also tells you how Cycles lays out the image: the centre is the camera's view direction (+Y) and things to the right (+X) are to the right in the image.
- Peak luminance 9468, peak RGB (12722, 8918, 5336). The rest of the map averages about 0.2. The brightest pixel in the no-sun map is 9.1 (a bulb), so the sun is about 1000 times brighter than anything else in the picture.

![the map with the sun ringed](../Captures/Ex08_IBL/step0_map_preview.png)

The source of the code is at the end of these notes.

## 2. Importing the map in Unity

`RtcgEx8Scene.ImportMap` does this for the two big EXRs. By hand: select the EXR and set in the Inspector

1. Texture Type: Default.
2. Texture Shape: Cube.
3. Mapping: Latitude-Longitude Layout (Cylindrical).
4. Convolution Type: None.
5. Fixup Edge Seams: on.
6. sRGB (Color Texture): off. The HDR data is linear.
7. Generate Mipmaps: on, Mipmap Filtering: Kaiser. Streaming Mipmaps: off. Read/Write: off.
8. Wrap Mode Clamp, Filter Mode Trilinear.
9. Max Size 4096, Compression: High Quality. On PC that is BC6H, which keeps HDR values at 1 byte per pixel.

Unity turns the 4096 x 2048 image into a cubemap with 2048 x 2048 faces and 12 mip levels. The two small reflection copies get the same settings with Max Size 512, which gives 256 x 256 faces.

No import settings of other assets were changed.

## 3. The Unity scene

`RtcgEx8Scene.BuildScene` builds all of this. By hand:

### Sky materials

1. Create a material `Ex8_Sky_Full`, shader `Skybox/Cubemap`. Cubemap (HDR): `Ex8_Environment`. Tint: grey 128 (0.5), which means no tint. Exposure 1, Rotation 0.
2. Same again as `Ex8_Sky_NoSun` with `Ex8_Environment_NoSun`.

### Finding the sun in Unity

The builder does what the lecture says: it looks for the brightest region of the sky. A temporary camera with a 90 degree field of view renders only the skybox in six directions (front, back, left, right, up, down) into a 256 x 256 float target. The brightest pixel and the centre of the pixels above half of it give a screen point, and `Camera.ViewportPointToRay` turns it into a world direction.

Result: direction (0.8468, 0.2077, 0.4896), which is yaw 59.97 degrees from +Z towards +X and elevation 11.99 degrees, peak 7894. So Blender's +Y became Unity's +Z and Blender's +X stayed +X. With the Cylindrical import, no extra rotation was needed. The peak is lower than in the EXR because the camera reads a smaller mip where the disc is averaged with the sky around it.

By hand you can do the same check: look through a camera at the skybox, find the sun, and point the light at it.

### The display

- Ground: a flat cylinder 80 m across (scale 80, 0.005, 80), material `Ex8_Snow` colour (0.70, 0.74, 0.82), smoothness 0.15.
- Plinth: a cylinder scaled (3.6, 0.25, 3.6), so 3.6 m across and 0.5 m high, colour (0.42, 0.40, 0.38), smoothness 0.25.
- Everything on the plinth sits under an empty `Display` turned to yaw 10 degrees, so the default camera looks with the sun 50 degrees to its right. Positions below are local to `Display`.
- Front row, 5 dielectric spheres, scale 0.3, at x = -0.9, -0.45, 0, 0.45, 0.9, y = 0.65, z = -0.85. URP/Lit, colour 0.8 grey, metallic 0, smoothness 0, 0.25, 0.5, 0.75, 1.
- Middle row, 5 metal spheres at z = -0.35, colour (0.95, 0.93, 0.88), metallic 1, the same five smoothness values.
- Back row at z = 0.45: a matte white ball (scale 0.6, colour 0.85, smoothness 0, metallic 0) at x = -0.85, y = 0.8; the Nisse at x = 0, standing on the plinth (y = 0.5); a mirror ball (scale 0.6, metallic 1, smoothness 1) at x = 0.85, y = 0.8.
- The Nisse: an empty `Nisse` turned 180 degrees so it faces the camera, with `Nisse.obj` as a child, `Nisse.mat`, scale 0.8 / mesh height (0.970), local position (-centre.x, -min.y, -centre.z) x scale, so it is 0.8 m tall with its feet on the plinth.

### The sun light

- Directional light `Sun (directional light)`, rotation = look along minus the measured sun direction.
- Colour (1.0, 0.70, 0.42), the same as the Blender sun.
- Intensity 3.0 / pi = 0.955. URP leaves out the 1/pi of the diffuse BRDF and the ambient SH is stored as radiance, so a light of E/pi gives the same brightness as a sun of irradiance E in the map's own units. I did not tune it by eye; it came out right first time next to the map's lighting.
- Shadows: Soft, strength 1, pipeline bias.

### The camera

- Main Camera, field of view 50, near 0.05, far 500, HDR on, Post Processing on, SMAA.
- A `Skybox` component with `Ex8_Sky_Full`. This is the background the camera draws, and it always shows the sun. The lighting (ambient and reflections) is set separately through `RenderSettings.skybox`, which can then be the no-sun map without the sun disappearing from the background.
- `OrbitCamera`: target (0, 0.75, 0), yaw 10, pitch 12, distance 3.9.

### Post processing

A global Volume with its own profile `Ex8_VolumeProfile`: Tonemapping ACES, Bloom (threshold 2, intensity 0.35, scatter 0.5), Color Adjustments post exposure +0.5. ACES rolls the HDR values above 1 off instead of clipping them, bloom makes the sun and the bright highlights glow a little, and the half stop makes the scene a bit less gloomy. Without the +0.5 the lit objects looked dull next to the background.

### The reflection probe

`Sky Reflection Probe (custom)` at (0, 1, 0): Type Custom, Cubemap `Ex8_Environment_Reflection`, HDR on, box size 200 x 200 x 200, box projection off, importance 1. The controller swaps its cubemap (full, no-sun, or black for "reflections off"), sets its intensity to the exposure, and turns it by -rotation about Y. URP 17 supports rotated reflection probes, so the reflections turn with the sky. Why a probe and not the normal skybox reflection is explained under "What went wrong".

### Lighting window

The controller sets these at Start and on every key press, so the Lighting window shows whatever the current state is:

- Environment: Skybox Material `Ex8_Sky_Full` (or `Ex8_Sky_NoSun` while the sun light is on), Sun Source: the directional light.
- Environment Lighting: Source Skybox, Intensity Multiplier 1 (or Source Color for the flat and none states).
- Environment Reflections: Source Skybox, Intensity Multiplier 1 (0 when off).

After changing the skybox, `DynamicGI.UpdateEnvironment()` makes Unity project the sky into spherical harmonics again.

### The controller and keys

`IBL Controller` holds `IblController` and `IblHud`.

| Key | What it does |
| --- | --- |
| 1 | background: environment map or plain dark colour |
| 2 | diffuse ambient: from the map (SH), flat colour, none |
| 3 | reflections on or off |
| 4 | sun as a directional light on or off |
| 5 | spin the environment slowly (10 degrees per second) |
| R | rotate 45 degrees (Shift+R back) |
| E | exposure 1, 2, 0.5 |
| C | alignment check: keep the sun in the map while the light is on |
| 0 | reset |
| H | hide the help |
| right mouse drag, arrows | orbit |
| scroll | zoom |

The flat ambient colour is the average of the map's own SH ambient, evaluated in 26 directions: linear (0.534, 0.355, 0.318). So "flat" and "skybox" have about the same total amount of light, and the only difference is direction.

## 4. The ingredients, one at a time

`RtcgEx8Scene.CaptureSteps` opens the scene, calls the controller directly (keys cannot be pressed headless), renders a few throwaway frames after every change, and saves 1280 x 720 pictures.

### Step 0: the map

![map in Unity](../Captures/Ex08_IBL/step0b_map_cubemap_view.png)

The skybox seen through a 75 degree camera at the probe height, looking towards the sun. Up is up, the ochre house and the tree are left of the sun as in the panorama, so the map is neither upside down nor mirrored. The sun shows as a white blob because bloom spreads it.

### Step 1: background

![off](../Captures/Ex08_IBL/step1_background_off.png)
![on](../Captures/Ex08_IBL/step1_background_on.png)

Only the camera's clear mode changes (Skybox or Solid Color). The objects look the same in both, because their ambient light and reflections still come from the map. The background is just a picture behind everything.

### Step 2: diffuse light

![none](../Captures/Ex08_IBL/step2a_no_ambient.png)
![flat](../Captures/Ex08_IBL/step2b_flat_ambient.png)
![skybox](../Captures/Ex08_IBL/step2c_skybox_ambient.png)

Reflections and the sun light are off here, so the metal spheres are black in all three; they have no diffuse colour.

- None: the matte ball and the Nisse are black. Without the map there is no light at all.
- Flat: one colour from every direction. The ball is evenly pink-beige and looks like a flat disc.
- Skybox (SH): the side towards the sun (right) is warm and bright, the side towards the blue sky behind the camera is bluish and darker. The values from the builder: the SH gives (1.54, 0.93, 0.61) for a normal facing the sun and (0.34, 0.25, 0.27) facing away. The Nisse's coat also picks up the warm light on its right side.

### Step 3: reflections

![off](../Captures/Ex08_IBL/step3a_reflections_off.png)
![on](../Captures/Ex08_IBL/step3b_reflections_on.png)

With reflections off the metal spheres are black and the dielectric ones only have diffuse light. With reflections on, the mirror ball and the smooth metal spheres show the courtyard: houses, the window lights and the coloured bulbs. From right (smoothness 1) to left (smoothness 0) the reflection gets blurrier because rougher surfaces read a smaller, blurrier mip of the cubemap. The dielectric spheres get a faint reflection, strongest at the edges (Fresnel).

![alignment check](../Captures/Ex08_IBL/step3c_mirror_ball_sun.png)

The alignment check. Both halves are from the same camera looking at the mirror ball from the right side of the display, with the sun behind and to the right of the camera.

- Left half: the sun light is off, and the sun only exists inside the map. Its reflection is the bright spot on the ball, right of the centre, at the height of the reflected houses.
- Right half: the sun is taken out of the map (no-sun map for the lighting) and added back as the directional light. The bright spot is now the light's own specular highlight, and the ball and the plinth get a shadow.

The spot is in the same place in both halves. That is the check that the light points the same way as the sun in the map: if the direction were off by a few degrees, the highlight would move away from where the map's sun was reflected. A second check is in step 4: the shadows of the spheres run parallel to the shadows of the lamp posts that Blender baked into the map.

### Step 4: the sun

![map only](../Captures/Ex08_IBL/step4a_no_sun_light.png)
![sun as a light](../Captures/Ex08_IBL/step4b_with_sun_light.png)

- Map only (left picture): the sun is in the map, so the SH has a strong warm lobe towards it, but the objects have no shadows at all. The light is soft and the spheres seem to float. This is the lecture's point: SH and blurred mips are low-pass, and the map knows nothing about where objects are.
- Sun as a light: the no-sun map gives the soft sky light, and the directional light gives crisp shadows that fall left and towards the camera. The plinth throws a long shadow on the snow. The shadows are long because the sun is only 12 degrees up.

### Step 5: rotation

![0](../Captures/Ex08_IBL/step5a_rotation_0.png)
![90](../Captures/Ex08_IBL/step5b_rotation_90.png)

The skybox `_Rotation` is set to 90 on both sky materials. The background turns (the red barn is now behind the display), the ambient SH turns with it because Unity makes it from the same material, the probe turns by the same angle, and the controller turns the light too. The shadows now fall to the right. Skybox/Cubemap turns the sky by -rotation about Y in Unity's terms, so the light direction is `Quaternion.Euler(0, -rotation, 0) * sunDirection`. I checked the sign with the SH: the ambient value in the turned sun direction stayed the same (0.420, 0.294, 0.292) after the 90 degree turn, which only happens if the light and the sky turn the same way.

### Step 6: everything on

![final](../Captures/Ex08_IBL/step6_final_overview.png)

## 5. The theory in plain words

**Equirectangular mapping.** Every pixel of the 2:1 image is one direction. The horizontal position is the angle around you (360 degrees across the width), the vertical position is the angle up or down (180 degrees over the height). In this map the centre column is +Y in Blender, which is +Z in Unity, the top row is straight up and the bottom row straight down. The rows near the top and bottom are stretched a lot, because a whole circle of directions near the pole is spread over a full row of pixels. Unity's Cylindrical import turns this into a cubemap: six square images, one per axis, which the GPU can look up quickly with a direction.

**Diffuse light as spherical harmonics.** A matte surface collects light from the whole half of the sky its normal faces, weighted by the cosine. That is a big blur of the map, so all the detail is lost anyway. Unity therefore stores only the lowest frequencies, 9 numbers per colour channel (L2 spherical harmonics), and the shader evaluates them with the surface normal. This is why the matte ball shows a smooth warm-to-blue gradient and never a picture of the houses.

**Reflections as a pre-filtered cubemap.** A mirror needs the sharp map in exactly the reflected direction. A rough surface needs the average over a cone of directions around it, and the cone grows with roughness. Instead of averaging at run time, the cubemap keeps blurrier versions in its mip levels, and the shader picks a mip from the roughness. Unity's Lit shader goes at most 6 mips down from the top.

**Why the sun is separate.** The sun is tiny (a 1 degree disc here, 2.4e-4 sr) and very bright (12540 here, the rest of the sky about 0.2). Three things go wrong if it stays only in the map. An 8-bit map would clip it at 1.0 and lose almost all its energy (the EXR keeps it, so this one is not a problem here). The SH and the blurry mips smear it into a soft warm glow, so you get no crisp highlight from rough surfaces and no clear light direction. And the map has no positions, so nothing can cast a shadow. So the sun is found in the map (brightest region), the disc is removed from the map used for lighting, and it comes back as a directional light pointing the same way, with the same colour and an intensity that matches its energy. The SH numbers show why the removal matters: with the disc in the map, a normal facing the sun gets 1.54 from the SH alone; after removing it, 0.42, and the light adds the rest with a shadow. Keeping both would count the sun twice.

## 6. What was hard or went wrong

1. **The default skybox reflection never changed.** In the first build the mirror ball showed Unity's default grey and blue sky, not the courtyard, even though the ambient SH updated fine. Unity only makes the skybox reflection when lighting is generated, and setting `RenderSettings.skybox` from a script did not redo it in my editor runs. So I used a reflection probe.
2. **Realtime reflection probes are switched off** in this project's quality settings (both quality levels), so a realtime probe rendered nothing. I did not change the quality settings. A probe in Custom mode with a cubemap asset works instead, and URP 17's probe rotation makes it follow the sky rotation.
3. **Convolved cubemaps came out black in the probe.** The textbook import for reflections is Convolution Type "Specular (Glossy Reflection)". With that, every reflection in the probe was black, twice, with two different texture sizes. Also, using the convolved map as the skybox blurred the background and moved the measured sun to 58.4 degrees, because the skybox reads a small mip that the convolution had blurred. So all maps are imported without convolution.
4. **Rough spheres were mirror sharp** with the 2048 face cubemap. URP reads at most mip 6, and mip 6 of a 2048 face is still 32 x 32. Small copies (512 x 256 EXR, 256 faces) fix it: mip 6 is 4 x 4 per face. The blur comes from Kaiser mips, not a proper GGX filter, so rough reflections are a bit blotchy instead of smooth. Compare the left metal sphere in step 3b.
5. **Switching the probe off and on lost it.** URP's probe atlas throws out a probe that was not used in the last frame, and in editor renders it did not come back. "Reflections off" therefore swaps in a 16 x 16 black cubemap instead of disabling the probe.
6. **Windows were white.** The white window frame stuck out further than the glowing glass and hid it. Swapping their depths fixed it.
7. **Camera views.** The first diffuse view was taken from the side and half the matte ball was hidden behind the front spheres. The first mirror ball view looked towards the sun, which puts the sun's reflection on the very rim of the ball where you cannot see it. The final view has the sun behind and to the right of the camera.
8. **Orientation and exposure were not a problem.** Blender's image centre (+Y) landed on Unity's +Z and nothing was mirrored, and the light intensity of E/pi matched without tuning.

## 7. What is not verified

- **Play mode was not run.** All pictures come from edit-mode renders where the script calls the controller's methods directly. The keys (1 to 5, R, E, C, 0, H), the mouse orbit, the scroll zoom and the OnGUI help were not tried.
- Whether `DynamicGI.UpdateEnvironment()` updates the SH in Play mode as fast as it did in the editor captures is not checked.
- The probe swap worked in editor renders. In Play mode the probe atlas behaves per frame and should also work, but that is not tested, and neither is whether the slow spin (key 5) looks smooth.
- Real-time performance was not measured. The two big cubemaps are 2048 x 2048 x 6 BC6H with mips, about 32 MB each in memory.
- The 32-bit EXRs are 74 MB each on disk.
- The probe captures only the sky, so the spheres do not reflect each other, the plinth or the Nisse. A probe that also rendered the scene would need realtime probes switched on in the quality settings or a baked probe.

## Code

### Docs/Ex08_make_environment.py

```python
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
```

### Assets/Scripts/Ex8/IblController.cs

```csharp
// Ex 8: image-based lighting, switched on one ingredient at a time.
// Keys (new Input System, Input.GetKey would throw in this project):
//   1  background: the environment map behind everything, or a plain colour
//   2  diffuse ambient: from the map (spherical harmonics) -> flat colour -> none
//   3  specular reflections from the map on or off
//   4  the sun as a real directional light (the map without the sun disc is used for lighting then)
//   5  spin the environment slowly
//   R  rotate the environment 45 degrees (Shift+R: back)
//   E  exposure 1 -> 2 -> 0.5 -> 1
//   C  alignment check: keep the sun in the map while the light is on, so its reflection and the light's highlight overlap
//   0  reset
// Put it on any object. The editor script RtcgEx8Scene fills in the references and the measured sun direction.
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class IblController : MonoBehaviour
{
    public enum Ambient { Skybox, Flat, None }

    [Header("References")]
    public Camera cam;
    public Skybox cameraSkybox;          // the background the camera draws (always the full map, so the sun is visible)
    public Material skyFull;             // Skybox/Cubemap with the full map (sun disc included)
    public Material skyNoSun;            // the same map with the sun disc removed
    public Light sun;
    public ReflectionProbe skyProbe;     // custom probe holding a small copy of the map, turned with the sky
    public Cubemap reflectionFull;       // small (256 face) copies of the maps; their blurrier mips serve rough surfaces
    public Cubemap reflectionNoSun;
    public Cubemap reflectionBlack;      // "reflections off": the probe gets a black cubemap

    [Header("Measured from the map (by RtcgEx8Scene)")]
    public Vector3 sunDirection = new Vector3(0.5f, 0.2f, 0.8f); // world direction towards the sun at rotation 0
    public float sunIntensity = 0.95f;   // sun irradiance / pi, so it matches the map's own units
    public Color flatAmbient = new Color(0.3f, 0.3f, 0.35f); // linear, the average of the map's ambient light
    public Color backgroundColour = new Color(0.16f, 0.17f, 0.20f);

    [Header("State")]
    public bool background = true;
    public Ambient ambient = Ambient.Skybox;
    public bool reflections = true;
    public bool sunLight = false;
    public bool keepSunInMap = false;
    public bool spin = false;
    [Range(0, 360)] public float rotation = 0f;
    public float exposure = 1f;
    public float spinSpeed = 10f;        // degrees per second

    void Start() { Apply(); }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        bool changed = false;
        if (kb.digit1Key.wasPressedThisFrame) { background = !background; changed = true; }
        if (kb.digit2Key.wasPressedThisFrame) { ambient = (Ambient)(((int)ambient + 1) % 3); changed = true; }
        if (kb.digit3Key.wasPressedThisFrame) { reflections = !reflections; changed = true; }
        if (kb.digit4Key.wasPressedThisFrame) { sunLight = !sunLight; changed = true; }
        if (kb.digit5Key.wasPressedThisFrame) { spin = !spin; }
        if (kb.cKey.wasPressedThisFrame) { keepSunInMap = !keepSunInMap; changed = true; }
        if (kb.rKey.wasPressedThisFrame) { rotation = Mathf.Repeat(rotation + (kb.shiftKey.isPressed ? -45f : 45f), 360f); changed = true; }
        if (kb.eKey.wasPressedThisFrame) { exposure = exposure == 1f ? 2f : exposure == 2f ? 0.5f : 1f; changed = true; }
        if (kb.digit0Key.wasPressedThisFrame) { ResetAll(); changed = true; }
        if (spin) { rotation = Mathf.Repeat(rotation + spinSpeed * Time.deltaTime, 360f); changed = true; }
        if (changed) Apply();
    }

    public void ResetAll()
    {
        background = true; ambient = Ambient.Skybox; reflections = true; sunLight = false;
        keepSunInMap = false; spin = false; rotation = 0f; exposure = 1f;
    }

    // The sun direction after the environment is turned. Skybox/Cubemap turns the sky by -rotation about Y
    // in Unity's left-handed terms, so the light has to turn the same way.
    public Vector3 RotatedSunDirection() => Quaternion.Euler(0f, -rotation, 0f) * sunDirection.normalized;

    public void Apply()
    {
        // the two sky materials always share rotation and exposure
        foreach (var m in new[] { skyFull, skyNoSun })
        {
            if (m == null) continue;
            m.SetFloat("_Rotation", rotation);
            m.SetFloat("_Exposure", exposure);
        }

        // 1. background
        if (cam != null)
        {
            cam.clearFlags = background ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = backgroundColour;
        }
        if (cameraSkybox != null) cameraSkybox.material = skyFull;

        // 4. the sun: either inside the map (no light), or taken out of the map and added back as a light
        bool lightOn = sunLight;
        RenderSettings.skybox = (lightOn && !keepSunInMap) ? skyNoSun : skyFull;
        if (sun != null)
        {
            sun.enabled = lightOn;
            sun.intensity = sunIntensity * exposure;
            sun.transform.rotation = Quaternion.LookRotation(-RotatedSunDirection(), Vector3.up);
        }
        RenderSettings.sun = sun;

        // 2. diffuse ambient
        switch (ambient)
        {
            case Ambient.Skybox:
                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientIntensity = 1f;
                break;
            case Ambient.Flat:
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = (flatAmbient * exposure).gamma; // the colour field is gamma, the value is linear
                break;
            case Ambient.None:
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                break;
        }

        // 3. specular reflections. The probe holds a small copy of the map: its mip 0 is sharp for smooth surfaces,
        // the smaller mips are blurrier and are read by rough surfaces.
        // It uses the same map as the lighting (with or without the sun disc), turns with the sky and follows the exposure.
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = reflections ? 1f : 0f;
        if (skyProbe != null)
        {
            // Swapping the texture is used to switch reflections off. Disabling the probe or setting its intensity to 0
            // made URP stop using it in the editor captures, even after switching it back on.
            skyProbe.customBakedTexture = !reflections ? reflectionBlack
                                        : RenderSettings.skybox == skyNoSun ? reflectionNoSun : reflectionFull;
            skyProbe.intensity = exposure;
            skyProbe.transform.rotation = Quaternion.Euler(0f, -rotation, 0f);
        }

        // re-project the sky into spherical harmonics (the diffuse ambient)
        DynamicGI.UpdateEnvironment();
    }

    public string Describe()
    {
        return $"1 Background: {(background ? "environment map" : "plain colour")}\n" +
               $"2 Diffuse ambient: {(ambient == Ambient.Skybox ? "from the map (SH)" : ambient == Ambient.Flat ? "flat colour" : "none")}\n" +
               $"3 Reflections: {(reflections ? "on" : "off")}\n" +
               $"4 Sun light: {(sunLight ? "on (sun taken out of the map)" : "off (sun only inside the map)")}" +
               (sunLight && keepSunInMap ? "  [C: sun kept in the map]" : "") + "\n" +
               $"5 Spin: {(spin ? "on" : "off")}    R rotation: {rotation:0} deg    E exposure: {exposure:0.0}";
    }
}
```

### Assets/Scripts/Ex8/OrbitCamera.cs

```csharp
// Ex 8: orbit camera. Hold the right mouse button and drag to orbit, scroll to zoom, arrow keys also orbit.
// Uses the new Input System (Mouse.current, Keyboard.current).
using UnityEngine;
using UnityEngine.InputSystem;

public class OrbitCamera : MonoBehaviour
{
    public Vector3 target = new Vector3(0f, 0.75f, 0f);
    public float distance = 4.2f;
    public float yaw = 0f;          // degrees around Y
    public float pitch = 8f;        // degrees, positive looks down
    public float minDistance = 0.8f, maxDistance = 14f;
    public float mouseSpeed = 0.25f; // degrees per pixel
    public float keySpeed = 60f;     // degrees per second

    void LateUpdate()
    {
        var m = Mouse.current;
        var kb = Keyboard.current;
        if (m != null)
        {
            if (m.rightButton.isPressed)
            {
                Vector2 d = m.delta.ReadValue() * mouseSpeed;
                yaw += d.x;
                pitch -= d.y;
            }
            float s = m.scroll.ReadValue().y;
            if (s != 0f) distance *= s > 0f ? 0.9f : 1.1f;   // one step per scroll event, whatever the units
        }
        if (kb != null)
        {
            float dt = Time.deltaTime * keySpeed;
            if (kb.leftArrowKey.isPressed) yaw += dt;
            if (kb.rightArrowKey.isPressed) yaw -= dt;
            if (kb.upArrowKey.isPressed) pitch += dt;
            if (kb.downArrowKey.isPressed) pitch -= dt;
        }
        Apply();
    }

    public void Apply()
    {
        pitch = Mathf.Clamp(pitch, -20f, 85f);
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(target - rot * Vector3.forward * distance, rot);
    }

    // used by the capture script, since the mouse cannot be moved headless
    public void SetView(Vector3 newTarget, float newYaw, float newPitch, float newDistance)
    {
        target = newTarget; yaw = newYaw; pitch = newPitch; distance = newDistance;
        Apply();
    }
}
```

### Assets/Scripts/Ex8/IblHud.cs

```csharp
// Ex 8: on-screen help in Play mode. H hides it.
using UnityEngine;
using UnityEngine.InputSystem;

public class IblHud : MonoBehaviour
{
    public IblController controller;
    public bool show = true;
    GUIStyle style;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.hKey.wasPressedThisFrame) show = !show;
    }

    void OnGUI()
    {
        if (!show || controller == null) return;
        if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = false };
        string text = "IMAGE-BASED LIGHTING (Ex 8)\n" + controller.Describe() +
                      "\nC alignment check    0 reset    H hide help\nRight mouse drag: orbit    scroll: zoom    arrows: orbit";
        var size = style.CalcSize(new GUIContent(text));
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(10, 10, size.x + 20, size.y + 16), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(20, 18, size.x + 10, size.y + 10), text, style);
    }
}
```

### Assets/Editor/RtcgEx8Scene.cs

```csharp
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Ex 8: image-based lighting with an equirectangular HDR map rendered in Blender (Docs/Ex08_make_environment.py).
// Run headless (Unity closed):
//   Unity.exe -batchmode -quit -projectPath <project> -executeMethod RtcgEx8Scene.ImportMap
//   Unity.exe -batchmode -quit -projectPath <project> -executeMethod RtcgEx8Scene.BuildScene
//   Unity.exe -batchmode -quit -projectPath <project> -executeMethod RtcgEx8Scene.CaptureSteps
// ImportMap sets the EXR import settings (cubemap from latitude-longitude, HDR, Kaiser mips).
// BuildScene makes the materials and "Assets/Scenes/Ex 8 IBL.unity", and finds the sun in the map.
// CaptureSteps drives IblController directly (keys cannot be pressed headless) and writes Captures/Ex08_IBL.
public static class RtcgEx8Scene
{
    const string ScenePath = "Assets/Scenes/Ex 8 IBL.unity";
    const string TexDir = "Assets/Textures/Ex8/";
    const string MapFull = TexDir + "Ex8_Environment.exr";
    const string MapNoSun = TexDir + "Ex8_Environment_NoSun.exr";
    const string ReflFull = TexDir + "Ex8_Environment_Reflection.exr";        // 512 x 256 copies for the probe
    const string ReflNoSun = TexDir + "Ex8_Environment_NoSun_Reflection.exr";
    const string MatDir = "Assets/Materials/Ex8/";
    const string OutDir = "Captures/Ex08_IBL";
    const float ViewOffset = 50f;   // the default view looks 50 degrees to the left of the sun

    // from the Blender script: sun lamp irradiance 3.0 and colour (1.0, 0.70, 0.42)
    const float SunIrradiance = 3.0f;
    static readonly Color SunColour = new Color(1.0f, 0.70f, 0.42f);

    // ------------------------------------------------------------------ import
    [MenuItem("RTCG/Ex8 Import Map")]
    public static void ImportMap()
    {
        foreach (var p in new[] { MapFull, MapNoSun, ReflFull, ReflNoSun })
        {
            bool reflection = p.Contains("_Reflection");
            AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(p);
            ti.textureType = TextureImporterType.Default;
            ti.textureShape = TextureImporterShape.TextureCube;
            ti.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;   // latitude-longitude layout
            var st = new TextureImporterSettings(); ti.ReadTextureSettings(st);
            // No convolution. Unity's "Glossy Reflection" (Specular) convolution would be the textbook choice, but a
            // probe holding a convolved cubemap rendered black here (tried twice). The small copies' Kaiser mips are
            // blurry enough: the roughest surface reads mip 6, which is 4 x 4 pixels per face.
            st.cubemapConvolution = TextureImporterCubemapConvolution.None;
            st.seamlessCubemap = true;               // "Fixup Edge Seams": blurry mips do not show the cube edges
            ti.SetTextureSettings(st);
            ti.sRGBTexture = false;                  // HDR data is linear
            ti.mipmapEnabled = true;
            ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            ti.streamingMipmaps = false;
            ti.isReadable = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear;
            ti.maxTextureSize = reflection ? 512 : 4096;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;   // BC6H on PC: HDR, 1 byte per pixel
            ti.SaveAndReimport();
            var t = AssetDatabase.LoadAssetAtPath<Cubemap>(p);
            Debug.Log($"EX8 imported {p}: {(t ? $"cubemap {t.width}x{t.height} {t.format} mips {t.mipmapCount}" : "NOT A CUBEMAP")}");
        }
    }

    // ------------------------------------------------------------------ helpers
    static Material Lit(string name, Color col, float smooth, float metallic)
    {
        Directory.CreateDirectory(MatDir);
        string path = MatDir + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", col);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metallic);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Material Sky(string name, string mapPath)
    {
        string path = MatDir + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("Skybox/Cubemap")) { name = name };
        m.SetTexture("_Tex", AssetDatabase.LoadAssetAtPath<Cubemap>(mapPath));
        m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f));   // 0.5 grey = no tint (the shader doubles it)
        m.SetFloat("_Exposure", 1f);
        m.SetFloat("_Rotation", 0f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos; go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    // Finds the sun in the sky the same way the lecture does it: look for the brightest region.
    // Renders the sky in six 90 degree views into a float target, finds the brightest pixel and turns it into a direction.
    public static Vector3 MeasureSun(Material sky, out float peak)
    {
        var old = RenderSettings.skybox;
        RenderSettings.skybox = sky;
        var go = new GameObject("_sunfinder");
        var c = go.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.Skybox; c.cullingMask = 0; c.fieldOfView = 90f; c.aspect = 1f;
        c.nearClipPlane = 0.1f; c.farClipPlane = 1000f; c.allowHDR = true;
        var acd = go.AddComponent<UniversalAdditionalCameraData>(); acd.renderPostProcessing = false; acd.antialiasing = AntialiasingMode.None;
        const int N = 256;
        var rt = new RenderTexture(N, N, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var tex = new Texture2D(N, N, TextureFormat.RGBAHalf, false, true);
        Vector3[] fwd = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up, Vector3.down };
        peak = -1f; Vector3 best = Vector3.up;
        foreach (var f in fwd)
        {
            go.transform.rotation = Quaternion.LookRotation(f, Mathf.Abs(f.y) > 0.5f ? Vector3.forward : Vector3.up);
            c.targetTexture = rt;
            for (int k = 0; k < 2; k++) c.Render();   // the first render can come out empty
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, N, N), 0, 0); tex.Apply();
            RenderTexture.active = null;
            var px = tex.GetPixels();
            int bi = -1; float bl = -1f;
            for (int i = 0; i < px.Length; i++)
            {
                float l = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;
                if (l > bl) { bl = l; bi = i; }
            }
            if (bl <= peak) continue;
            // centre of the pixels above half the peak in this view
            float sx = 0, sy = 0, sw = 0;
            for (int i = 0; i < px.Length; i++)
            {
                float l = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;
                if (l > 0.5f * bl) { sx += (i % N + 0.5f) * l; sy += (i / N + 0.5f) * l; sw += l; }
            }
            peak = bl;
            best = c.ViewportPointToRay(new Vector3(sx / sw / N, sy / sw / N, 0)).direction.normalized;
        }
        c.targetTexture = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
        RenderSettings.skybox = old;
        return best;
    }

    static string AzEl(Vector3 d)
    {
        float az = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, el = Mathf.Asin(d.y) * Mathf.Rad2Deg;
        return $"yaw {az:0.00} deg (from +Z towards +X), elevation {el:0.00} deg";
    }

    // ------------------------------------------------------------------ scene
    [MenuItem("RTCG/Ex8 Build Scene")]
    public static void BuildScene()
    {
        AssetDatabase.Refresh();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Directory.CreateDirectory(MatDir);

        var skyFull = Sky("Ex8_Sky_Full", MapFull);
        var skyNoSun = Sky("Ex8_Sky_NoSun", MapNoSun);

        // find the sun in the full map (rotation 0)
        Vector3 sunDir = MeasureSun(skyFull, out float peak);
        Vector3 sunDirNo = MeasureSun(skyNoSun, out float peakNo);
        Debug.Log($"EX8 SUN in Unity: dir {sunDir.ToString("F4")}, {AzEl(sunDir)}, peak {peak:0.0} (no-sun map peak {peakNo:0.00} at {AzEl(sunDirNo)})");
        float sunYaw = Mathf.Atan2(sunDir.x, sunDir.z) * Mathf.Rad2Deg;

        // materials
        var ground = Lit("Ex8_Snow", new Color(0.70f, 0.74f, 0.82f), 0.15f, 0f);
        var plinthM = Lit("Ex8_Plinth", new Color(0.42f, 0.40f, 0.38f), 0.25f, 0f);
        var matte = Lit("Ex8_Matte_White", new Color(0.85f, 0.85f, 0.85f), 0f, 0f);
        var mirror = Lit("Ex8_Mirror", new Color(0.95f, 0.95f, 0.95f), 1f, 1f);
        float[] smooth = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        // the display faces the camera; the camera looks with the sun 50 degrees to its right,
        // so the lit sides are visible and the shadows fall across the plinth towards the left
        float viewYaw = sunYaw - ViewOffset;
        var root = new GameObject("Display").transform;
        root.rotation = Quaternion.Euler(0, viewYaw, 0);

        var floor = Prim(PrimitiveType.Cylinder, "Ground", null, new Vector3(0, -0.005f, 0), new Vector3(80f, 0.005f, 80f), ground);
        Prim(PrimitiveType.Cylinder, "Plinth", root, new Vector3(0, 0.25f, 0), new Vector3(3.6f, 0.25f, 3.6f), plinthM);

        for (int i = 0; i < 5; i++)
        {
            float x = -0.9f + 0.45f * i;
            var d = Lit($"Ex8_Dielectric_S{smooth[i]:0.00}", new Color(0.8f, 0.8f, 0.8f), smooth[i], 0f);
            var m = Lit($"Ex8_Metal_S{smooth[i]:0.00}", new Color(0.95f, 0.93f, 0.88f), smooth[i], 1f);
            Prim(PrimitiveType.Sphere, $"Dielectric_smooth_{smooth[i]:0.00}", root, new Vector3(x, 0.65f, -0.85f), Vector3.one * 0.3f, d);
            Prim(PrimitiveType.Sphere, $"Metal_smooth_{smooth[i]:0.00}", root, new Vector3(x, 0.65f, -0.35f), Vector3.one * 0.3f, m);
        }
        Prim(PrimitiveType.Sphere, "Matte_Ball", root, new Vector3(-0.85f, 0.8f, 0.45f), Vector3.one * 0.6f, matte);
        Prim(PrimitiveType.Sphere, "Mirror_Ball", root, new Vector3(0.85f, 0.8f, 0.45f), Vector3.one * 0.6f, mirror);

        // Nisse: wrapper at the base centre, child scaled to 0.8 m tall, facing the camera (local -Z)
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Nisse/Nisse.obj");
        var nisseMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Nisse/Nisse.mat");
        Mesh mesh = model.GetComponentInChildren<MeshFilter>().sharedMesh;
        Bounds b = mesh.bounds;
        float s = 0.8f / b.size.y;
        var wrap = new GameObject("Nisse").transform; wrap.SetParent(root, false);
        wrap.localPosition = new Vector3(0f, 0.5f, 0.45f); wrap.localRotation = Quaternion.Euler(0, 180f, 0);
        var child = (GameObject)PrefabUtility.InstantiatePrefab(model, wrap);
        child.name = "Mesh"; child.transform.localScale = Vector3.one * s;
        child.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z) * s;
        foreach (var r in child.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = nisseMat;

        // the sun as a light
        var sunGO = new GameObject("Sun (directional light)");
        var sun = sunGO.AddComponent<Light>();
        sun.type = LightType.Directional; sun.color = SunColour; sun.intensity = SunIrradiance / Mathf.PI;
        sun.shadows = LightShadows.Soft; sun.shadowStrength = 1f;
        sunGO.AddComponent<UniversalAdditionalLightData>();
        sunGO.transform.rotation = Quaternion.LookRotation(-sunDir, Vector3.up);

        // camera
        var camGO = new GameObject("Main Camera"); camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 50f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 500f; cam.allowHDR = true;
        var acd = camGO.AddComponent<UniversalAdditionalCameraData>(); acd.renderPostProcessing = true; acd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var camSky = camGO.AddComponent<Skybox>(); camSky.material = skyFull;
        var orbit = camGO.AddComponent<OrbitCamera>();
        orbit.SetView(new Vector3(0, 0.75f, 0), viewYaw, 12f, 3.9f);
        camGO.AddComponent<AudioListener>();

        // post: ACES tone mapping and a little bloom, so values above 1 roll off instead of clipping
        string profPath = MatDir + "Ex8_VolumeProfile.asset";
        AssetDatabase.DeleteAsset(profPath);
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(prof, profPath);
        var tm = prof.Add<Tonemapping>(false); tm.mode.Override(TonemappingMode.ACES);
        var bl = prof.Add<Bloom>(false); bl.threshold.Override(2.0f); bl.intensity.Override(0.35f); bl.scatter.Override(0.5f);
        var ca = prof.Add<ColorAdjustments>(false); ca.postExposure.Override(0.5f);   // half a stop brighter overall
        foreach (var c in prof.components) { AssetDatabase.AddObjectToAsset(c, prof); EditorUtility.SetDirty(c); }
        EditorUtility.SetDirty(prof);
        var vol = new GameObject("Post Volume (ACES + bloom)").AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = prof;

        // controller + HUD
        var ctrlGO = new GameObject("IBL Controller");
        var ctrl = ctrlGO.AddComponent<IblController>();
        ctrl.cam = cam; ctrl.cameraSkybox = camSky; ctrl.skyFull = skyFull; ctrl.skyNoSun = skyNoSun; ctrl.sun = sun;
        ctrl.sunDirection = sunDir; ctrl.sunIntensity = SunIrradiance / Mathf.PI;
        var hud = ctrlGO.AddComponent<IblHud>(); hud.controller = ctrl;

        // reflections: the default skybox reflection is not re-made when the sky is changed from a script (it stays
        // Unity's default sky), and realtime probes are switched off in this project's quality settings. So a probe in
        // Custom mode holds a small copy of the map; URP 17 turns the probe with its transform.
        var probeGO = new GameObject("Sky Reflection Probe (custom)");
        probeGO.transform.position = new Vector3(0, 1f, 0);
        var probe = probeGO.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Custom;
        probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Cubemap>(ReflFull);
        ctrl.reflectionFull = AssetDatabase.LoadAssetAtPath<Cubemap>(ReflFull);
        ctrl.reflectionNoSun = AssetDatabase.LoadAssetAtPath<Cubemap>(ReflNoSun);
        string blackPath = MatDir + "Ex8_NoReflection.cubemap";
        AssetDatabase.DeleteAsset(blackPath);
        var black = new Cubemap(16, TextureFormat.RGBAHalf, false);
        var zeros = new Color[16 * 16];
        for (int f = 0; f < 6; f++) black.SetPixels(zeros, (CubemapFace)f);
        black.Apply();
        AssetDatabase.CreateAsset(black, blackPath);
        ctrl.reflectionBlack = black;
        probe.hdr = true;
        probe.size = new Vector3(200f, 200f, 200f); probe.boxProjection = false;
        probe.importance = 1; probe.intensity = 1f;
        ctrl.skyProbe = probe;

        // flat ambient colour = the average of the map's own ambient light (evaluated from its SH in 26 directions)
        ctrl.ResetAll(); ctrl.Apply();
        var sh = RenderSettings.ambientProbe;
        var dirs = new System.Collections.Generic.List<Vector3>();
        for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
            if (x != 0 || y != 0 || z != 0) dirs.Add(new Vector3(x, y, z).normalized);
        var res = new Color[dirs.Count];
        sh.Evaluate(dirs.ToArray(), res);
        Color avg = Color.black; foreach (var c in res) avg += c; avg /= res.Length;
        ctrl.flatAmbient = new Color(avg.r, avg.g, avg.b, 1f);
        Debug.Log($"EX8 ambient SH: up {Eval(sh, Vector3.up)} down {Eval(sh, Vector3.down)} towards sun {Eval(sh, sunDir)} away {Eval(sh, -sunDir)} -> flat {ctrl.flatAmbient}");
        ctrl.Apply();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"EX8 built: nisse scale {s}, view yaw {viewYaw:0.0}, sun yaw {sunYaw:0.0}");
    }

    static string Eval(SphericalHarmonicsL2 sh, Vector3 d)
    {
        var o = new Color[1]; sh.Evaluate(new[] { d }, o);
        return $"({o[0].r:0.000}, {o[0].g:0.000}, {o[0].b:0.000})";
    }

    // ------------------------------------------------------------------ captures
    static Camera cam;
    static IblController ctrl;
    static OrbitCamera orbit;

    static void Shot(string name, int w = 1280, int h = 720)
    {
        ctrl.Apply();
        // throwaway renders: the environment update and the lazy texture loads settle over a couple of frames
        for (int i = 0; i < 3; i++) RtcgTools.WritePng(cam, Path.Combine(OutDir, "_warmup.png"), 320, 180);
        RtcgTools.WritePng(cam, Path.Combine(OutDir, name + ".png"), w, h);
        var sh = RenderSettings.ambientProbe;
        Debug.Log($"EX8 SHOT {name}: skybox {RenderSettings.skybox?.name} ambient {RenderSettings.ambientMode} up {Eval(sh, Vector3.up)} side {Eval(sh, ctrl.RotatedSunDirection())} refl {RenderSettings.reflectionIntensity} probe {ctrl.skyProbe.customBakedTexture.name} sun {ctrl.sun.enabled} rot {ctrl.rotation}");
    }

    // like Shot, but returns the 1280 x 720 picture instead of saving it
    static Texture2D Grab(string label)
    {
        ctrl.Apply();
        for (int i = 0; i < 3; i++) RtcgTools.WritePng(cam, Path.Combine(OutDir, "_warmup.png"), 320, 180);
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.targetTexture = rt; cam.aspect = 1280f / 720f; cam.Render();
        RenderTexture.active = rt;
        var t = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        t.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); t.Apply();
        RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt);
        Debug.Log($"EX8 GRAB {label}: skybox {RenderSettings.skybox.name} sun {ctrl.sun.enabled} probe {ctrl.skyProbe.customBakedTexture.name}");
        return t;
    }

    [MenuItem("RTCG/Ex8 Capture Steps")]
    public static void CaptureSteps()
    {
        EditorSceneManager.OpenScene(ScenePath);
        Directory.CreateDirectory(OutDir);
        cam = Camera.main;
        ctrl = Object.FindAnyObjectByType<IblController>();
        orbit = cam.GetComponent<OrbitCamera>();
        var display = GameObject.Find("Display");
        var ground = GameObject.Find("Ground");
        Vector3 sd = ctrl.sunDirection;
        float sunYaw = Mathf.Atan2(sd.x, sd.z) * Mathf.Rad2Deg;
        float viewYaw = sunYaw - ViewOffset;
        Vector3 centre = new Vector3(0, 0.75f, 0);
        Vector3 L(Vector3 local) => Quaternion.Euler(0, viewYaw, 0) * local;   // display-local to world

        ctrl.ResetAll();
        orbit.SetView(centre, viewYaw, 12f, 3.9f);
        for (int i = 0; i < 4; i++) RtcgTools.WritePng(cam, Path.Combine(OutDir, "_warmup.png"), 320, 180);

        // 0b: just the sky, display hidden, wide view towards the sun
        display.SetActive(false); ground.SetActive(false);
        cam.fieldOfView = 75f;
        orbit.SetView(new Vector3(0, 1.0f, 0), sunYaw - 35f, -8f, 0.01f);
        Shot("step0b_map_cubemap_view");
        display.SetActive(true); ground.SetActive(true);
        cam.fieldOfView = 50f;

        // 1: background off / on (everything else as default: SH ambient, reflections, no sun light)
        orbit.SetView(centre, viewYaw, 12f, 3.9f);
        ctrl.ResetAll(); ctrl.background = false; Shot("step1_background_off");
        ctrl.ResetAll(); Shot("step1_background_on");

        // 2: diffuse only (no reflections, no sun light), close on the matte ball and the Nisse in the back row.
        // The sun is 50 degrees to the right of this view, so with SH ambient the right side of the ball turns warm.
        orbit.SetView(L(new Vector3(-0.42f, 0.85f, 0.45f)), viewYaw, 12f, 1.6f);
        ctrl.ResetAll(); ctrl.reflections = false;
        ctrl.ambient = IblController.Ambient.None; Shot("step2a_no_ambient");
        ctrl.ambient = IblController.Ambient.Flat; Shot("step2b_flat_ambient");
        ctrl.ambient = IblController.Ambient.Skybox; Shot("step2c_skybox_ambient");

        // 3: sphere rows, reflections off / on
        orbit.SetView(L(new Vector3(0f, 0.65f, -0.55f)), viewYaw, 24f, 2.4f);
        ctrl.ResetAll(); ctrl.reflections = false; Shot("step3a_reflections_off");
        ctrl.ResetAll(); Shot("step3b_reflections_on");

        // 3c: the alignment check on the mirror ball, two halves of one picture from the same camera.
        // Left: the sun only inside the map (no light): its reflection is a spot on the ball.
        // Right: the sun taken out of the map and added as the directional light: the light's own highlight.
        // If the light direction was found correctly, the two spots sit in the same place on the ball.
        // The camera looks at the ball from the right side of the display (nothing in between), the sun is behind
        // and to the right of the camera, so its reflection lands well inside the ball, not on the rim.
        Vector3 ball = L(new Vector3(0.85f, 0.8f, 0.45f));
        orbit.SetView(ball, viewYaw - 90f, 4f, 1.25f);
        ctrl.ResetAll(); ctrl.sunLight = false;
        var left = Grab("step3c_left_map_sun");
        ctrl.ResetAll(); ctrl.sunLight = true;
        var right = Grab("step3c_right_light");
        var both = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        both.SetPixels(0, 0, 640, 720, left.GetPixels(320, 0, 640, 720));
        both.SetPixels(640, 0, 640, 720, right.GetPixels(320, 0, 640, 720));
        var line = new Color[2 * 720]; for (int i = 0; i < line.Length; i++) line[i] = Color.white;
        both.SetPixels(639, 0, 2, 720, line);
        both.Apply();
        File.WriteAllBytes(Path.Combine(OutDir, "step3c_mirror_ball_sun.png"), both.EncodeToPNG());
        Object.DestroyImmediate(left); Object.DestroyImmediate(right); Object.DestroyImmediate(both);

        // 4: the sun: only in the map vs. taken out and added as a light
        orbit.SetView(centre, viewYaw, 22f, 4.0f);
        ctrl.ResetAll(); Shot("step4a_no_sun_light");
        ctrl.ResetAll(); ctrl.sunLight = true; Shot("step4b_with_sun_light");

        // 5: rotation 0 and 90, sun light on so the shadows follow
        orbit.SetView(centre, viewYaw, 22f, 4.0f);
        ctrl.ResetAll(); ctrl.sunLight = true; ctrl.rotation = 0f; Shot("step5a_rotation_0");
        ctrl.rotation = 90f; Shot("step5b_rotation_90");

        // 6: final overview, everything on
        orbit.SetView(centre + new Vector3(0, 0.1f, 0), viewYaw - 15f, 16f, 5.4f);
        ctrl.ResetAll(); ctrl.sunLight = true; Shot("step6_final_overview");

        File.Delete(Path.Combine(OutDir, "_warmup.png"));
        ctrl.ResetAll(); ctrl.Apply();
        orbit.SetView(centre, viewYaw, 12f, 3.9f);
        Debug.Log("EX8 captures done");
    }
}
```

Headless runs (Unity closed):

```
"C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe" -batchmode -quit -projectPath <project> -executeMethod RtcgEx8Scene.ImportMap
"C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe" -batchmode -quit -projectPath <project> -executeMethod RtcgEx8Scene.BuildScene
"C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe" -batchmode -quit -projectPath <project> -executeMethod RtcgEx8Scene.CaptureSteps
```
