# Exercise 5: two Shader Graph effects on the Nisse

Two effects from games, rebuilt in Shader Graph (URP 17, Unity 6000.6) on the scanned Nisse:

- A. Skyrim dragon shout (Thu'um): a shockwave ring sweeps across the figure.
- B. "Become a shadow" (Death Eater smoke): the figure turns into dark smoke and leaves a cloud of small glowing specks that hangs in its shape, then it re-forms.

## Files

| What | Where |
|---|---|
| Shout graph (lit, on the Nisse) | `Assets/Shaders/Ex05/NisseShout.shadergraph`, material `NisseShout.mat` |
| Shout shell graph (transparent, bends the background) | `Assets/Shaders/Ex05/NisseShoutShell.shadergraph`, material `NisseShoutShell.mat` |
| Shadow graph | `Assets/Shaders/Ex05/NisseShadow.shadergraph`, material `NisseShadow.mat` |
| Shadow particle graphs, puff texture, emitter mesh | `Assets/Shaders/Ex05/NisseShadowSmoke.shadergraph`, `NisseShadowSpecks.shadergraph` (materials of the same names), `Ex5_SmokePuff.asset`, `Ex5_NisseEmitMesh.asset`, stage materials `Ex5_ShadowFloor.mat`, `Ex5_ShadowBackdrop.mat` |
| Stage materials, grid texture, bloom profile | `Assets/Shaders/Ex05/Ex5_Floor.mat`, `Ex5_Backdrop.mat`, `Ex5_GridTex.asset`, `Ex5_Effects_Profile.asset` |
| Scenes | `Assets/Scenes/Ex 5 Shout.unity`, `Assets/Scenes/Ex 5 Shadow.unity` |
| Runtime scripts | `Assets/Scripts/NisseShoutDriver.cs`, `Assets/Scripts/NisseShadowDriver.cs` |
| Scene builder and capture | `Assets/Editor/RtcgEx5Effects.cs` (`BuildShout`, `CaptureShout`, `BuildShadow`, `CaptureShadow`) |
| Graph generators | `Tools/sg_lib.py`, `Tools/make_shout_graph.py`, `Tools/make_shadow_graph.py` |
| Frames and contact sheets | `Captures/Ex05_Shout/`, `Captures/Ex05_Shadow/` (`*_contact_sheet.png`) |

The graphs open normally in the Shader Graph window. They were written by the Python scripts and
not drawn by hand, but every node in them is a standard Shader Graph node, so they can be edited
in the window like any other graph.

Shout scene (the shadow stage is described in section B): dark floor and back wall with a faint grid, one directional key light with soft
shadows, HDR camera with post processing on, and a global Volume with only Bloom (threshold 1,
intensity 0.9) and ACES tonemapping. The Nisse is `Assets/Nisse/Nisse.obj` placed under an empty
"Nisse" object, scaled to 1 unit tall (scale = 1 / bounds height = 1.2125), feet on the floor,
rotated 180 degrees so it faces the camera at -Z. The texture is the same `_BaseMap` as
`Assets/Nisse/Nisse.mat`.

## A. Shout

### What it is

In Skyrim the shout is a wave of rippling air that leaves the dragonborn and knocks things over.
Here the shout starts at an empty object "Shout origin" to the left of the Nisse at
(-0.9, 0.55, -0.35). A ring (really a sphere surface) grows from it at 1 unit per second. Where
the ring passes through the Nisse the surface bulges out along its normals with a small ripple, and
a gold-white band glows. A Fresnel rim on the figure brightens while the shout is alive. A second,
transparent sphere grows with the ring and bends what is behind it.

`NisseShoutDriver` restarts the shout every 3.2 s (0.4 s quiet, then 2.2 s of shout). It writes
`_ShoutOrigin`, `_ShoutTime`, `_ShoutSpeed` and `_ShoutLife` through a MaterialPropertyBlock and
scales the shell sphere to radius = time x speed.

### Node chain: NisseShout (URP Lit)

Band, used by both the vertex and the fragment stage:

1. Position (Absolute World) and Shout Origin (property) into Distance: how far this point is from the start.
2. Shout Time x Shout Speed (Multiply): the ring radius now.
3. Subtract: distance minus radius. Zero exactly on the ring, negative inside, positive outside.
4. Absolute, then Smoothstep with Edge1 = Ring Width, Edge2 = 0. Edges swapped on purpose, so it gives 1 on the ring and falls to 0 at Ring Width away from it.
5. Fade: Shout Time / Life (Divide), One Minus, Saturate. 1 when the shout starts, 0 after Life seconds.
6. Band x Fade (Multiply).

Vertex stage:

7. Signed distance from step 3 x Ripple Frequency (Multiply), Sine, x 0.4, + 0.6 (Multiply, Add). A ripple between 0.2 and 1.0 across the ring.
8. Ripple x Amplitude x (Band x Fade) (two Multiply).
9. Normal Vector (World) x that amount (Multiply): the offset.
10. Position (Absolute World) + offset (Add).
11. Transform, Absolute World to Object, type Position. Into Vertex Position.

Fragment stage:

12. Base Map (property) into Sample Texture 2D (type Default). RGBA into Base Color.
13. Ring Color x (Band x Fade) (Multiply): the glowing band.
14. Fresnel Effect with Power = Rim Power. Lerp from 0.15 to 1 with T = Fade. Fresnel x Lerp x Rim Strength x Ring Color (three Multiply): the rim.
15. Band glow + rim (Add) into Emission.
16. Smoothness property into Smoothness. Metallic left at 0.

### Node chain: NisseShoutShell (URP Unlit, Transparent, Alpha blend, front faces, no depth write)

1. Fresnel Effect, Power = Shell Power. Only the silhouette of the sphere lights up, so on screen it reads as a ring.
2. Fade as above (Shout Time, Life, Divide, One Minus, Saturate).
3. Ripple: UV + Shout Time x 0.6 (Multiply, Add) into Gradient Noise (Scale = Ripple Scale), then Remap 0..1 to 0.4..1.4.
4. Strength = Fresnel x Fade x Ripple (two Multiply).
5. Normal Vector (View) x Distortion x Strength (two Multiply): a small screen offset.
6. Screen Position (Default) + offset (Add) into the UV of Scene Color: the background, shifted.
7. Scene Color + Shell Tint x Strength (Multiply, Add) into Base Color.
8. Strength x 2.5, Saturate into Alpha.

Scene Color needs the URP Opaque Texture. It was already on in the active pipeline asset
(`PC_RPAsset`, `m_RequireOpaqueTexture: 1`, used by the PC quality level), so no pipeline setting
was changed.

### Interesting nodes

- Distance and Subtract turn "where is the ring" into one number per vertex. Everything else is shaping that number.
- Smoothstep with the edges reversed is a cheap band function. No extra One Minus needed.
- The same band nodes feed the vertex output and the emission. Shader Graph evaluates them once in each stage, so the glow lines up with the bulge.
- Transform (Absolute World to Object) is needed because the offset is worked out in world units, while Vertex Position wants object space. The Nisse mesh is scaled by 1.21, so an object-space offset would come out a different size.
- Scene Color is how the shell bends the background. It is a copy of the opaque frame, so the shell can only bend opaque things, which is fine here.

### Parameter values used

Speed 1.0, Ring Width 0.09, Amplitude 0.05, Ripple Frequency 70, Life 2.2 s, Ring Color HDR
(3.2, 2.4, 1.2), Rim Power 3, Rim Strength 0.8, Smoothness 0.25. Shell: Shell Power 2.5,
Distortion 0.12, Shell Tint HDR (0.35, 0.27, 0.14), Ripple Scale 18. Frames at t = 0.3, 0.7, 0.85,
1.0, 1.15 and 1.6 s.

### What was hard or went wrong

- The first renders showed the Nisse blue and purple. The Sample Texture 2D node copied from the donor graph had Type = Normal, so the colour texture was unpacked as a normal map. Setting Type to Default fixed it. This is worth checking whenever a texture looks "wrong colour" in a graph.
- The first ring colour (intensity about 6) and the first shell tint blew out under bloom and filled the inside of the shell with a beige haze. Both were turned down.
- The normals are not recomputed after the displacement, so the lighting does not show the bulge much. The bulge is mostly visible on the silhouette and in the close-up (`shout_closeup_t0.85.png`), where the hat edge is bent out. The glow carries most of the effect.
- Comparing `shout_t0.85.png` with `shout_t0.85_noshell.png` shows the shell bends the floor grid and the edge of the Nisse, but the bend is subtle over a dark background. It is clearest in the close-up, where the hat is seen doubled through the shell edge.

## B. Shadow

### What it is

The Nisse turns into smoke and leaves a cloud of small particles of himself behind, then comes back.
Three parts work together:

1. The `NisseShadow` Shader Graph eats the body away in a noise pattern (alpha clip). The cut has a
   thin dark violet ember edge, and the body darkens to black-violet as it goes.
2. A smoke Particle System emits soft dark puffs from the Nisse's surface. They grow, rise, turn
   in noise turbulence and fade.
3. A speck Particle System emits thousands of tiny glowing specks (violet, dark red, a few pale
   embers) from the same surface. They barely move, so after the body is gone they still hang in
   the Nisse's shape for about 2 s and then drift up and fade.

`NisseShadowDriver` runs one 9.2 s loop and drives all three from the same dissolve value:

| Time (s) | What happens | Dissolve |
|---|---|---|
| 0 to 1.0 | solid figure | 0 |
| 1.0 to 4.0 | turns to smoke (SmoothStep) | 0 to 1 |
| 4.0 to 6.2 | body gone, specks linger and drift | 1 |
| 6.2 to 7.7 | re-forms | 1 to 0 |
| 7.7 to 9.2 | solid again, then the loop restarts | 0 |

Particle rates are only set while the dissolve is rising. Smoke rate = 380 x clamp(sin(pi x
min(1, 1.15 d)) + 0.15), so it starts at once, peaks halfway and stops when the body is gone.
Speck rate = 2200 x sqrt(d) x (1 - smoothstep over d = 0.85..1), so specks keep coming while the
body vanishes and stop just before it is fully gone. Values go to the material through a
MaterialPropertyBlock and to the particle systems through `emission.rateOverTime`.

Stage: lighter than before so dark smoke reads. Blue-grey lit wall (0.42, 0.44, 0.5) and floor
(0.3, 0.31, 0.36), ambient (0.2, 0.2, 0.24), a warm key light with soft shadows, a cool
directional back light from behind for a rim, and a spot that puts a bright pool on the wall
behind the Nisse. Same Bloom + ACES Volume as before. These stage materials are
`Ex5_ShadowFloor.mat` and `Ex5_ShadowBackdrop.mat`, so the shout scene's materials were not changed.

### Node chain: NisseShadow (URP Lit, Alpha Clipping on)

Dissolve noise:

1. Position (Absolute World), Split.
2. Combine (R, G) and Combine (B, G): two flat projections, front (xy) and side (zy).
3. Gradient Noise on each (Scale = Noise Scale), Add, x 0.5.
4. Simple Noise on the xy projection (Scale 120) for small grain. Lerp from the gradient noise toward it by 0.18.
5. Remap 0.22..0.78 to 0..1, Saturate. Into Alpha.
6. Dissolve x 1.02 (Multiply) into Alpha Clip Threshold. Pixels where noise is below the threshold are discarded. The 1.02 makes sure Dissolve = 1 removes everything.

Edge:

7. Threshold + Edge Width (Add).
8. Smoothstep with Edge1 = threshold + width, Edge2 = threshold, In = noise. Reversed edges, so 1 right at the cut and 0 a little further in.
9. Dissolve x 25, Saturate: switches the edge off when Dissolve is 0.
10. Edge Color x edge x switch (two Multiply). Edge Color is now a dim violet, (0.3, 0.03, 0.45), so it reads as a dark ember, not a neon line.

Rim and colour:

11. Fresnel Effect (Power = Rim Power) x Lerp(0, 1, Dissolve) x Rim Color (two Multiply). No rim on the solid figure, a violet rim while it dissolves.
12. Edge glow + rim (Add) into Emission.
13. Sample Texture 2D (Base Map, type Default). Lerp from the texture to Shadow Color (near black violet) with T = Saturate(Dissolve x 2.5). Into Base Color.
14. Smoothness property (0.08) into Smoothness.

Vertex stage (small drift of the pieces that are about to vanish):

15. Time x Wisp Speed (Multiply).
16. Position x + z (Add) as R, Position y minus scrolled time (Subtract) as G, Combine. Gradient Noise with Scale = Wisp Scale.
17. Power 2 on that noise, x One Minus(dissolve noise), x Wisp Amount, x Dissolve.
18. Wisp Direction (Vector3) x amount, Add to Position (Absolute World), Transform Absolute World to Object (Position), into Vertex Position.

### Node chain: particle graphs (URP Unlit, Transparent, both faces, no depth write)

`NisseShadowSmoke.shadergraph` (Alpha blend) and `NisseShadowSpecks.shadergraph` (Additive) are the
same seven nodes:

1. Main Tex (Texture2D property, `_MainTex`) into Sample Texture 2D.
2. Vertex Color: the particle colour and the colour-over-lifetime fade arrive here.
3. Texture RGBA x Vertex Color x Tint (two Multiply) into Base Color.
4. Split the product, A into Alpha.

The smoke Tint is white. The specks Tint is HDR (1.6, 1.6, 1.6), so the brightest specks go over
1 and get a little bloom. Both use `Ex5_SmokePuff.asset`, a 128 x 128 texture made in C#: white
RGB, alpha = radial falloff (smoothstep, power 1.4) x (0.5 + 0.9 x four octaves of Perlin noise),
so the edge of every puff is soft and uneven.

### Particle settings

Both systems use Shape = Mesh (Triangle), World simulation space, scaling mode Shape (the emitter
shape follows the Nisse scale of 1.21, particle sizes stay in metres).

The Shape module is meant to use the Nisse's Mesh Renderer, but `Nisse.obj` is imported with
Read/Write off and its import settings were not to be changed. So `BuildShadow` copies the mesh
(105,837 vertices, 178,473 triangles) to `Assets/Shaders/Ex05/Ex5_NisseEmitMesh.asset` and the
emitter object copies the mesh object's position, rotation and scale. It emits from the same
surface.

| Setting | Smoke | Specks |
|---|---|---|
| Lifetime | 2 to 3.5 s | 4 to 4.8 s |
| Start speed | 0.02 to 0.1 | 0 to 0.03 |
| Start size | 0.2 to 0.36 m | 0.01 to 0.02 m |
| Size over lifetime | 0.5 to 1.9 (grows to about 0.35 to 0.7 m) | 1 to 0.45 (shrinks) |
| Start colour | random between (0.08, 0.06, 0.11, a 0.5) and (0.2, 0.16, 0.25, a 0.35) | random pick: violet (0.48, 0.22, 0.85) 55 %, dark red (0.75, 0.06, 0.1) 40 %, pale ember (1, 0.78, 0.55) 5 %, alpha 0.75 |
| Velocity over lifetime (world) | x -0.05..0.05, y 0.2..0.42, z -0.05..0.05 | x -0.015..0.015, y 0.02..0.07, z -0.02..0.02 |
| Noise | strength 0.2, frequency 1.3, scroll 0.3, 2 octaves, damping | strength 0.05, frequency 1.5, scroll 0.2, damping |
| Colour over lifetime (alpha) | 0, 1 at 20 %, 0.75 at 50 %, 0 | 0, 1 at 4 %, 0.7 at 45 %, 0 |
| Other | rotation 0..360 deg, spin -0.6..0.6 rad/s, max 3000 | max 14000 |
| Normal offset | 0.02 | 0.004 |
| Peak rate (from the driver) | 380 / s | 2200 / s |

In the capture about 540 smoke puffs and 3,100 specks are alive at the moment the body disappears.

### Interesting parts

- Alpha Clip Threshold does the dissolving. The graph outputs a noise value as Alpha plus the threshold, and URP does the `clip()`.
- Two projections of Gradient Noise from world position instead of mesh UVs. The scan's UV map is a patchwork of islands, so UV noise would show seams.
- The particles do most of the work now. The shader only has to make the body darken and break up. The smoke and specks give the "turned into smoke" read.
- Vertex Color is what makes a Shader Graph work as a particle shader. Without it the colour and fade set on the Particle System would be ignored.
- The specks are additive and very small, so a cloud of them reads as glowing dust, not as a solid shape. Because they barely move, the cloud keeps the Nisse's outline for a while after the body is gone.

### Parameter values used (shader)

Noise Scale 14, Edge Width 0.07, Edge Color HDR (0.3, 0.03, 0.45), Shadow Color (0.025, 0.008,
0.04), Rim Color HDR (0.3, 0.1, 0.55), Rim Power 2.2, Wisp Amount 0.15, Wisp Scale 5, Wisp Speed
0.7, Wisp Direction (0.35, 1, 0.15), Smoothness 0.08.

### Frames

`Captures/Ex05_Shadow/shadow_contact_sheet.png` has 8 frames: 1 solid (t 0.8), 2 dissolve 25 %
(t 2.0), 3 dissolve 50 % (t 2.5), 4 dissolve 75 % (t 3.03), 5 body gone with specks (t 4.3),
6 specks drifting (t 5.6), 7 re-forming (t 6.8), 8 solid again (t 8.7). There are also two
close-ups (`shadow_closeup_4_dissolve75.png`, `shadow_closeup_5_gone_specks.png`).

`CaptureShadow` runs in edit mode. It resets both particle systems with a fixed random seed,
then steps the driver and `ParticleSystem.Simulate(1/30 s)` from t = 0 and renders each frame
when its time comes, so the particles have the right history. Four throwaway renders come first
because the first renders in a session come out untextured.

### What was hard or went wrong

- Round 1 of this version: the speck tint was 3, so the specks overlapped into a bright pink-white glitter. Lowering it to 1.6 and making 40 % of them dark red gave a darker, mixed dust.
- The smoke first looked like separate dark balls. It got softer by lowering the puff alpha (0.85 to 0.5), letting the puffs grow more (to 1.9x), using a lower-frequency noise in the puff texture, and emitting more of them (170 to 380 per second) so they merge into one column.
- With 5.5 s speck lifetimes the specks were still floating around the re-formed Nisse. Shorter lifetimes (4 to 4.8 s) and stopping speck emission just before the body is gone fixed it: frame 8 is clean.
- The first edge colour change did not show up. The generator had not been re-run, so the old graph was still being used. Always run `make_shadow_graph.py` before `BuildShadow`.
- The particle Shape module cannot read `Nisse.obj` directly (Read/Write off), hence the copied emitter mesh.

### My verdict on the frames

Frames 2 to 6 read as "turns into smoke and leaves glowing dust". A dark smoke column rises over
the figure, the body breaks into dark pieces, and a cloud of specks in the Nisse's shape stays
after the body is gone and then thins out. Weak points:

- The smoke is flat. The particles are unlit, so the puffs are not shaded.
- The smoke drifts a bit to the left in this seed.
- At 75 % the last pieces of the body read as small violet shards more than as smoke.
- The re-form (frame 7) is the dissolve played backwards: loose dark shards come back. There is no particle effect pulling the Nisse back together.

## Verification

- All five graphs (shout, shout shell, shadow, smoke, specks) import with no shader errors or messages in the headless logs (`Captures/_logs/ex5_Build*.log`: `hasError=False messages=0`).
- I looked at every frame in `Captures/Ex05_Shout/` and `Captures/Ex05_Shadow/`. They show the ring travelling across the figure, and for the shadow the 8-frame loop from solid through smoke and lingering specks back to solid.
- Not verified: the scenes running in Play mode. The frames were made in edit mode by calling `Apply(t)` on the drivers (and, for the shadow, stepping the particle systems with `Simulate`) and rendering. I did not check the frame rate with 3,000+ particles and the 178k-triangle emitter mesh. The `Update()` loops in the two driver scripts are simple, but I did not watch them run. The bulge is small and hard to judge in still frames.

## Rebuilding the graphs by hand

Shout (Shader Graph > URP > Lit Shader Graph). Blackboard: Base Map (Texture2D, reference
`_BaseMap`), Shout Origin (Vector3, `_ShoutOrigin`), Shout Time (Float, `_ShoutTime`), Shout Speed,
Ring Width, Amplitude, Ripple Frequency, Life (Floats), Ring Color (Color, HDR), Rim Power,
Rim Strength, Smoothness (Floats).

```
Position [Absolute World] -> Distance.A
Shout Origin             -> Distance.B
Shout Time, Shout Speed  -> Multiply                      = radius
Distance - radius        -> Subtract                      = x
x -> Absolute -> Smoothstep(Edge1 = Ring Width, Edge2 = 0) = band
Shout Time / Life -> Divide -> One Minus -> Saturate      = fade
band x fade -> Multiply                                    = bf
x x Ripple Frequency -> Multiply -> Sine -> Multiply 0.4 -> Add 0.6 = ripple
ripple x Amplitude -> Multiply -> Multiply bf             = disp
Normal Vector [World] x disp -> Multiply                  = offset
Position [Absolute World] + offset -> Add
  -> Transform [Absolute World -> Object, Position] -> Vertex: Position
Base Map -> Sample Texture 2D [Type Default] .RGBA -> Fragment: Base Color
Ring Color x bf -> Multiply                               = ringGlow
Fresnel Effect (Power = Rim Power)
  x Lerp(A 0.15, B 1, T fade) x Rim Strength x Ring Color = rim
ringGlow + rim -> Add -> Fragment: Emission
Smoothness -> Fragment: Smoothness
```

Shell (URP > Unlit Shader Graph; Graph Settings: Surface Type Transparent, Blending Alpha,
Render Face Front, Depth Write Disabled, Cast Shadows off). Blackboard: Shout Time, Life,
Shell Power, Distortion, Ripple Scale (Floats), Shell Tint (Color, HDR).

```
Fresnel Effect (Power = Shell Power)                      = f
Shout Time / Life -> Divide -> One Minus -> Saturate      = fade
UV + (Shout Time x 0.6) -> Add -> Gradient Noise(Scale = Ripple Scale)
  -> Remap(In 0..1, Out 0.4..1.4)                         = ripple
f x fade x ripple -> Multiply, Multiply                   = s
Normal Vector [View] x Distortion x s -> Multiply, Multiply = offset
Screen Position [Default] + offset -> Add -> Scene Color.UV
Scene Color + (Shell Tint x s) -> Add -> Base Color
s x 2.5 -> Saturate -> Alpha
```

Put the shell material on a Sphere centred on the shout origin and scale it to 2 x time x speed
(the driver script does this).

Shadow (URP > Lit Shader Graph; Graph Settings: Alpha Clipping on). Blackboard: Base Map
(Texture2D, `_BaseMap`), Dissolve (Float slider 0..1, `_Dissolve`), Noise Scale, Edge Width
(Floats), Edge Color (Color, HDR), Shadow Color (Color), Rim Color (Color, HDR), Rim Power,
Wisp Amount, Wisp Scale, Wisp Speed (Floats), Wisp Direction (Vector3), Smoothness (Float).

```
Position [Absolute World] -> Split
Combine(R, G) -> Gradient Noise(Noise Scale)  = n1
Combine(B, G) -> Gradient Noise(Noise Scale)  = n2
(n1 + n2) x 0.5 -> Add, Multiply              = n
Combine(R, G) -> Simple Noise(Scale 120)      = grain
Lerp(A n, B grain, T 0.18) -> Remap(In 0.22..0.78, Out 0..1) -> Saturate = noise
noise -> Fragment: Alpha
Dissolve x 1.02 -> Multiply                   = thr -> Fragment: Alpha Clip Threshold
thr + Edge Width -> Add                       = thrW
Smoothstep(Edge1 thrW, Edge2 thr, In noise)   = edge
Dissolve x 25 -> Saturate                     = on
Edge Color x edge x on -> Multiply, Multiply  = edgeGlow
Fresnel Effect(Power = Rim Power) x Lerp(A 0, B 1, T Dissolve) x Rim Color = rim
edgeGlow + rim -> Add -> Fragment: Emission
Base Map -> Sample Texture 2D [Type Default]
Lerp(A texture RGBA, B Shadow Color, T Saturate(Dissolve x 2.5)) -> Fragment: Base Color
Smoothness -> Fragment: Smoothness
Time x Wisp Speed -> Multiply                 = ts
Combine(R = Split.R + Split.B, G = Split.G - ts) -> Gradient Noise(Wisp Scale)
  -> Power(B 2) x One Minus(noise) x Wisp Amount x Dissolve = w
Wisp Direction x w -> Multiply                = offset
Position [Absolute World] + offset -> Add
  -> Transform [Absolute World -> Object, Position] -> Vertex: Position
```

Smoke and speck particle shaders (URP > Unlit Shader Graph; Graph Settings: Surface Type
Transparent, Blending Alpha for smoke and Additive for specks, Render Face Both, Depth Write
Disabled, Cast Shadows off). Blackboard: Main Tex (Texture2D, `_MainTex`), Tint (Color, HDR).

```
Main Tex -> Sample Texture 2D [Type Default] .RGBA x Vertex Color x Tint -> Multiply, Multiply = c
c -> Base Color
c -> Split .A -> Alpha
```

Then in the scene: two Particle Systems on the Nisse with Shape = Mesh (a readable copy of the
Nisse mesh, or Mesh Renderer if Read/Write is on), with the settings in the particle table above, and a
material from each particle shader with the puff texture in Main Tex.

## How the graph files were made

`Tools/sg_lib.py` builds the .shadergraph JSON by copying nodes, slots, properties and blocks
from graphs that ship inside the Shader Graph and URP packages (`Library/PackageCache`), the same
way the earlier `Tools/make_hologram_graph.py` does. Only ids, positions, default values and the
links are new. The two make_ scripts list the nodes and links in the order above. Re-run them,
then `RtcgEx5Effects.BuildShout` / `BuildShadow` to rebuild materials and scenes.
