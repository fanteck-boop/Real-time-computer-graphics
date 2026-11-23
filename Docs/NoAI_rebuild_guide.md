# How to rebuild the parts Claude helped with (No-AI branch)

This lists every step, in order, for the four things I helped with: Ex1 twist, Ex3 new Nisse, Ex4 gallery and Ex5 shadow. If you follow it you get the same result. Where a number matters I give it. Unity 6000.6.0f1, URP, new Input System only.

Used in several places, so read once:
- The Nisse model is `Assets/Nisse/Nisse.obj` (178,643 triangles, base plate gone). Its texture is `Assets/Nisse/Nisse_diffuse.jpeg` on `Assets/Nisse/Nisse.mat`.
- The mesh is in the raw scan coordinates, so its pivot is far from the object origin (about x -2.76, y -6.88, z 5.52). That is why the scenes use a wrapper object and an offset. Never expect it to sit at 0,0,0.
- URP lights use inverse-square falloff, so a spot 3.6 m away needs an intensity of about 50 to look lit. Intensity 1 is almost black.
- When you render a screenshot by script, the first render in a session comes out with white textures. Render a throwaway frame first.

---

## Ex1: the twist (distortion waves and click shockwave)

What it does: a wave starts at a random cube every 1.2 s and pushes the other cubes away from it. A left click starts the same wave, stronger, at the cube nearest the mouse.

Steps:
1. Open `Assets/Scripts/Transformations.cs`. Add at the top: `using System.Collections.Generic;` and `using UnityEngine.InputSystem;` (the project only uses the new Input System, so `Input.GetMouseButtonDown` would throw).
2. Add these fields in the class:
   ```csharp
   [Header("Custom tweak: distortion waves + click shockwave")]
   public bool waves = false;
   public float waveAmplitude = 0.9f;
   public float waveNumber = 1.4f;       // radians per unit of distance
   public float waveSpeed = 5f;          // units per second the front travels
   public float waveLife = 2.5f;         // seconds until the wave has faded
   public float waveBand = 3f;           // thickness of the travelling band
   public float autoWaveInterval = 1.2f; // 0 = clicks only
   public float clickWaveBoost = 1.6f;
   struct Wave { public Vector3 origin; public float start; public float amp; }
   readonly List<Wave> active = new List<Wave>();
   float nextAuto;
   ```
3. Add the click picking. It uses the nearest cube to the ray, not a collider hit, because the cubes shrink to zero scale and a collider test would miss:
   ```csharp
   Vector3 NearestCubeToRay(Ray ray)
   {
       float best = float.MaxValue; Vector3 pos = Vector3.zero;
       for (int i = 0; i < createGrid.grid.Length; i++)
       {
           if (!createGrid.grid[i].gameObject.activeSelf) continue;
           Vector3 p = createGrid.grid[i].position;
           float d = Vector3.Cross(ray.direction, p - ray.origin).sqrMagnitude;
           if (d < best) { best = d; pos = startPos[i]; }
       }
       return pos;
   }
   ```
4. Add the wave list update, called once at the start of `Update()` when `waves` is on:
   ```csharp
   void UpdateWaves()
   {
       if (autoWaveInterval > 0f && Time.time >= nextAuto)
       {
           nextAuto = Time.time + autoWaveInterval;
           active.Add(new Wave { origin = startPos[Random.Range(0, startPos.Length)], start = Time.time, amp = waveAmplitude });
       }
       if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Camera.main != null)
       {
           Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
           active.Add(new Wave { origin = NearestCubeToRay(ray), start = Time.time, amp = waveAmplitude * clickWaveBoost });
       }
       active.RemoveAll(w => Time.time - w.start > waveLife);
   }
   ```
5. Add the offset each cube gets from all live waves:
   ```csharp
   Vector3 WaveOffset(Vector3 pos)
   {
       Vector3 sum = Vector3.zero;
       for (int k = 0; k < active.Count; k++)
       {
           Wave w = active[k];
           float age = Time.time - w.start;
           Vector3 dir = pos - w.origin;
           float d = dir.magnitude;
           dir = d > 0.001f ? dir / d : Vector3.up;
           float phase = (d - age * waveSpeed) * waveNumber;       // sin(distance - time)
           float front = (d - age * waveSpeed) / waveBand;
           float envelope = Mathf.Exp(-front * front) * (1f - age / waveLife);
           sum += dir * (w.amp * Mathf.Sin(phase) * envelope);
       }
       return sum;
   }
   ```
6. At the top of `Update()` put `if (waves) UpdateWaves();`. In the line that sets `localPosition` add the offset:
   ```csharp
   createGrid.grid[i].localPosition = startPos[i] + Vector3.up * Mathf.Sin(Time.time * moveFrequency + moveOffset) * moveAmplitude
       + (waves ? WaveOffset(startPos[i]) : Vector3.zero);
   ```
7. In the scene, tick `Waves` on the `Transformations` component on the Manager. Leave the other values at the defaults above.
8. Press Play. Cubes ripple by themselves, and a left click makes a bigger ripple where you click.

Why it works: `sin(distance - speed * age)` moves the crest outward as age grows. The Gaussian `exp(-front^2)` keeps only a band of cubes moving, and `(1 - age/life)` fades the wave out.

---

## Ex3: the new Nisse on the snowball

Your own scene is `Ex 3 Lights unity and blender.unity`. The sphere is a child of the Nisse object, so everything is placed in the raw mesh coordinates.

Getting the new mesh into Unity:
1. Download the Original GLB from Sketchfab (Download 3D Model, first option). Unity cannot import GLB without a package, so convert it to OBJ. The route I actually tested is the script `Docs/glb_to_obj.py` (it lives on the `main` branch; get it with `git show main:Docs/glb_to_obj.py > glb_to_obj.py`). Run `python glb_to_obj.py model.glb Nisse.obj`. It writes the vertices as stored in the file, one OBJ vertex per glb vertex, and copies the UVs unchanged. I compared it against the first Nisse.obj and about 85 percent of the UVs matched exactly (the rest are duplicated seam vertices), and the texture shows the right way up in Unity.
   I did not test a Blender export (File > Export > Wavefront). Blender flips the UV v coordinate when it imports a glTF, so the texture could come out upside down or the mesh could be rotated. If you use Blender and the texture looks wrong, that is the first thing to check.
2. Put the OBJ at `Assets/Nisse/Nisse.obj`. If you replace the old file, keep its `.meta`, so every scene keeps pointing at it (Unity finds the model by the GUID in the `.meta`).
3. Select `Nisse.obj` in the Project window. Model tab: Index Format 32 bit (the mesh has 100k+ vertices), Mesh Compression Off, Normals = Calculate. Apply.
4. The Nisse in the scene has its material set to `Nisse.mat`.

Placing it on the sphere:
1. The Nisse instance in the scene has these values: root position (1.545, 7.346, 5.06), root rotation Y 180. The mesh child is called "default"; its local Y was 0.203 for the old mesh.
2. The sphere is a child of the root, at local (2.764, -7.683, 5.47), scale 1 (diameter 1 m). Its top ends up at world y 0.163.
3. The new mesh has no base plate, so it stands lower. Set the "default" child's local Y to **-0.0158**. The boots then reach y 0.158, which is 5 mm into the sphere. If you leave 0.203 he floats 0.21 m above it.
4. To find your own number: select the Nisse mesh, read its renderer Bounds min Y, read the sphere's max Y, and move the mesh down by the difference plus 0.005.

Lights, because this was the "only one shadow" question:
- Directional light: Mode Realtime (not Baked), Shadow Type Soft.
- Point light and spot light: Mode Realtime, Shadow Type Soft.
- `PC_RPAsset` > Lighting > Additional Lights must have Cast Shadows on (it already is on in this project).
- Baked lights give no real-time shadow unless you bake lightmaps.

---

## Ex4: the gallery (`Ex 4 Galleri.unity`)

Build it as one empty parent called "Gallery" with everything inside. All sizes are in metres.

Materials (Universal Render Pipeline/Lit, Metallic 0 on all). Make these first:

| Name | Base colour | Smoothness |
|---|---|---|
| Wall | (0.82, 0.80, 0.76) | 0.05 |
| Floor | (0.30, 0.27, 0.25) | 0.45 |
| Ceiling | (0.12, 0.12, 0.13) | 0 |
| Pedestal | (0.92, 0.92, 0.90) | 0.15 |
| Frame | (0.06, 0.05, 0.045) | 0.35 |
| Lamp | white, Emission on, HDR colour (5, 3.8, 2.4) | 0.2 |
| one per picture | white, texture in Base Map | 0.12 |

Room (cubes, remove the colliders if you want a clean scene):
1. Floor: position (0, -0.05, 0), scale (10, 0.1, 16).
2. Ceiling: (0, 4.05, 0), scale (10.2, 0.1, 16.2).
3. Left wall: (-5.05, 2, 0), scale (0.1, 4, 16). Right wall: (5.05, 2, 0), same scale.
4. Back wall: (0, 2, 8.05), scale (10.2, 4, 0.1). Front wall: (0, 2, -8.05), same scale.
The room is closed so only your own lights count.

Pictures: import the four JPGs. Make each one a Quad with its material. Height 2.2, width = 2.2 x image width / image height (cats 2.2, village 1.66, cubist 2.2, robot 2.2). Centre height 1.9.

| Picture | Wall | z | Quad rotation Y |
|---|---|---|---|
| cats and sleigh | left (x = -5) | -1.0 | -90 |
| village | left | 4.5 | -90 |
| cubist Santa | right (x = 5) | -1.0 | 90 |
| robot Santa | right | 4.5 | 90 |

Place the quad 0.062 off the wall into the room. Behind it put a cube for the frame: thickness 0.06, 0.03 off the wall, 0.18 larger than the picture in each direction, with the Frame material. A Quad is only visible from its back-facing side (-Z), which is why the rotation signs matter. If a picture is invisible, flip the sign.

Pedestals and Nisse (three of them):
1. Positions: (-2.3, 0, 0.5), (0, 0, 3.0), (2.3, 0, 0.5).
2. Each: a cube 0.9 x 1.0 x 0.9 centred at y 0.5, plus a cube 1.0 x 0.06 x 1.0 at y 1.03 (top surface y 1.06).
3. Nisse: put an empty at the pedestal top (y 1.06) with Y rotation 170, 180, 190 (left, middle, right). Put the model inside it as a child. Scale the child 1.03 (so he is 0.85 m tall: 0.85 / mesh height 0.8247). Set the child's local position to (-centre.x, -min.y, -centre.z) x 1.03, using the mesh bounds centre and min. Use `Nisse.mat`.

Lights (all placed by you):
1. Picture lights, one per picture: Spot, angle 62, inner angle 37, range 12, intensity 32, colour (1, 0.93, 0.82), no shadows. Position 1.3 m out from the wall, at y 3.9, same z as the picture. Aim at the centre of the picture.
2. Nisse key lights, one per Nisse: Spot, angle 40, inner 24, range 12, intensity 55, colour (1, 0.9, 0.75), Shadow Type Soft. Position 3.6 above the floor and 2.2 in front (z - 2.2) of the pedestal; the left one 1.2 further left, the right one 1.2 further right, the middle one straight. Aim at 1.45 above the pedestal centre.
3. Back fill: Point, intensity 6, colour (0.55, 0.65, 1), range 14, at (0, 3.4, 6.5), no shadows.
4. Lamp for bloom: a sphere of scale 0.3 at (3.6, 2.8, 6.8) with the Lamp material, and a child Point light with intensity 8, colour (1, 0.82, 0.55), range 8.
5. Switch the scene's Directional Light off.

Scene settings:
1. Lighting > Environment: ambient mode Flat (Color), colour (0.05, 0.05, 0.06). Reflection source Custom, intensity 0.
2. Camera: position (0, 1.6, -6.9), look at (0, 1.3, 2.5), field of view 62, HDR on, Background black.
3. No post-processing yet. Depth of field, exposure, bloom and lens flare are the actual Ex4 work.

---

## Ex5: the shadow (turns to smoke, specks stay)

The full node lists with every value are in `Docs/Ex05_shout_shadow_notes.md`, section B and "Rebuilding the graphs by hand". This is the order to build it in.

1. **Stage.** Wall (0.42, 0.44, 0.5), floor (0.3, 0.31, 0.36), ambient (0.2, 0.2, 0.24), a warm key light with soft shadows, a cool directional back light, and a spot that puts a bright pool on the wall behind the Nisse. Smoke is dark, so the backdrop has to be lighter or it disappears. HDR camera, post processing on, a Volume with Bloom (threshold 1, intensity 0.9) and ACES tonemapping.
2. **Nisse.** `Nisse.obj` inside an empty, scaled to 1 unit tall (scale = 1 / mesh height = 1.2125), feet on the floor, rotated 180 so it faces a camera at -Z.
3. **Dissolve graph** (Create > Shader Graph > URP > Lit Shader Graph, Alpha Clipping on). Properties: Base Map, Dissolve (0..1), Noise Scale 14, Edge Width 0.07, Edge Color, Shadow Color, Rim Color, Rim Power 2.2, wisp values, Smoothness 0.08.
   - Noise: Position (Absolute World), then Gradient Noise on the front projection and the side projection, averaged, plus a little Simple Noise for grain. Remap it to 0..1 and feed it to Alpha.
   - Alpha Clip Threshold = Dissolve x 1.02. This is the dissolve itself.
   - Edge: Smoothstep between the threshold and threshold + Edge Width gives a thin band; multiply by Edge Color (dim violet, not bright).
   - Colour: Lerp from the Base Map texture to Shadow Color (near-black violet) over Dissolve x 2.5.
   - Make sure the Sample Texture 2D node is type **Default**. If the Nisse comes out blue or purple, that node is set to Normal.
   - Vertex stage: a small scrolling Gradient Noise offset times Dissolve, added to Position, transformed from Absolute World back to Object.
4. **Particle graphs.** Two Unlit Shader Graphs, transparent, both faces, no depth write: Smoke (Alpha blend) and Specks (Additive). Nodes: Main Tex into Sample Texture 2D, multiplied by Vertex Color and a Tint, into Base Color; Alpha from the same product. The Vertex Color node is what lets the Particle System colour and fade work.
5. **Puff texture.** A 128 x 128 texture, white RGB, alpha = soft radial falloff multiplied by a few octaves of Perlin noise, so the edge of each puff is soft and uneven.
6. **Emitter mesh.** `Nisse.obj` has Read/Write off, so the Particle System cannot use it as a shape. Make a readable copy of the mesh (`Instantiate(mesh)` saved as an asset in an editor script) and use that as the Shape mesh.
7. **Two Particle Systems** on the Nisse, Shape = Mesh (Triangle) using that copy, simulation space World. Settings are in the table at the end of section B in the notes. In short:
   - Smoke: lifetime 2 to 3.5 s, size 0.2 to 0.36 growing to 1.9x, dark colours, rising 0.2 to 0.42, noise 0.2, up to 3,000 particles, peak 380 per second.
   - Specks: lifetime 4 to 4.8 s, size 0.01 to 0.02, violet / dark red / pale ember colours, almost no velocity, up to 14,000, peak 2,200 per second, tint HDR 1.6.
8. **Driver script** (`Assets/Scripts/NisseShadowDriver.cs`). One loop of 9.2 s with one dissolve value: solid 0 to 1.0 s, dissolve 1.0 to 4.0 s (smoothstep), gone 4.0 to 6.2 s, re-form 6.2 to 7.7 s, solid again until 9.2 s. The same value is written to the material and sets both particle rates: smoke rate 380 x clamp(sin(pi x min(1, 1.15 d)) + 0.15), speck rate 2200 x sqrt(d) x (1 - smoothstep over d = 0.85 to 1). The rates only run while the dissolve rises, so specks stop just before the body is gone.
9. **Check it.** At 50 percent you should see dark smoke over a breaking body, at 100 percent a cloud of specks still in his shape, and after the re-form no specks around the solid Nisse. If specks stay around the re-formed Nisse, shorten their lifetime or stop their emission earlier.

---

## The shout

Dropped. The files are still in the project (scene `Ex 5 Shout.unity` and the graphs in `Assets/Shaders/Ex05/`), and its recipe is in section A of the Ex5 notes.
