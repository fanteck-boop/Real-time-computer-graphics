# Exercise 1 notes: animated cube grid with a coloured point light

Built with Claude Code. Scene: `Assets/Scenes/CubeGrid.unity`, rebuilt reproducibly by the menu RTCG > Build Cube Grid Scene (`Assets/Editor/BuildCubeGridScene.cs`). Evidence: `Captures/Ex01/match_sheet.png` (reference gif frames next to renders), `Captures/Ex01/match_numbers.txt` (measurements), `Captures/Ex01/ex01_cubes.gif`, frames in `Captures/Ex01/final`.

## What was built
A 10 x 10 x 10 grid of cubes (prefab scale 0.5, spacing 1) created in `CreateGrid`. `Transformations` animates the scale, `CameraControl` orbits the camera around the grid centre at distance 12, `LightManipulation` moves one point light and changes its colour from red to blue, and `RayCastManipulator` shoots random rays from the centre and hides the cubes they hit (this is what hollows out the middle of the grid). The camera clears to solid black.

## Custom tweaks (stated for the exam)
1. Cos-based pulse: `Transformations.cosPulse`. Scale = start scale x pulseScale x (1 + pulseAmount x cos(t x frequency + index x phase)). With pulseAmount 1 each cube shrinks to zero and regrows, so the whole grid thins out and refills. A small phase per cube (0.004) sends a slow wave through the grid. Final values: pulseScale 0.45, pulseAmount 1, frequency 1.5, phase 0.004. With the flag off the original tutorial formula (sin x 5) is unchanged.
2. Figure-8 light path: `LightManipulation.figureEight`. The light follows a 1:2 Lissajous curve of radius 9 around the grid, so it sweeps across all faces.
3. White cubes: `CreateGrid.colourByPosition`. The tutorial colours each cube from its grid position. With the flag off (default stays on) the cubes are white and the colour comes only from the light, which gives the blue, magenta and red look of the reference.

## Root cause of "no visible shadows and no dark side"
The shadows were never switched off by the pipeline bias values. Four things hid them, and the earlier attempt to dim the ambient had no effect for the first two reasons.
1. The default skybox was both the ambient light (spherical harmonics) and the reflection source. The cube material is metallic 0.7, smoothness 0.8, so the unlit faces mostly mirrored the sky and stayed grey. Lowering `ambientIntensity` barely changed that.
2. With post processing on (default URP volume, pipeline grading mode HDR) the render never exceeded 209 of 255 per channel; with post processing off the same frame reaches 255. The exact component that caps it was not isolated. Lit faces could never reach the fully saturated light colour, so the lit and dark sides did not separate hard enough. The earlier Volume asset with ACES tonemapping had an empty component list, so it never did anything either.
3. The scene design: light inside the grid, huge overlapping cubes, a close camera.
4. The unlit faces in the reference are dark green, which needs a dim green ambient colour, not a skybox.

## Final parameters
- Ambient mode Flat, colour (0, 0.14, 0.03), skybox none, reflection intensity 0 with a custom (empty) reflection source.
- Camera post processing off, background black, distance 12.
- Point light: intensity 3000 (range 50), shadows Soft, strength 1, shadow resolution tier High, light bias 0.02, normal bias 0.1, pipeline settings off for this light. Intensity is large because the metallic material keeps only 30 percent diffuse and the light is 8 to 10 units away, and the aim is to clip lit faces to the pure light colour.
- Pipeline assets (`PC_RPAsset`, `PC_Renderer`) were not changed.

## How close the match is
See `match_numbers.txt`. Measured over the gif and over 44 render frames: near-black pixels 0.946 in the gif and 0.849 in the render; lit pixels fully saturated 65 percent vs 75 percent; mean unlit face luminance 14.4 vs 12.1; unlit faces green-dominant in 91 percent of the render. Not matched: the reference has eleven nearly empty frames out of 21 (grid at zero scale or fully hidden by the raycasts), while the cosine pulse never leaves the render empty, and the render shows more unlit faces and more lit area (0.069 vs 0.034) because cubes stay visible longer. The reference also has a few much larger cubes at some peaks, which the smaller pulse does not reproduce.

## Terminology to be able to explain
Point light and inverse square falloff; per-pixel lighting (the light is evaluated for each pixel with the interpolated normal, here in the deferred path used by PC_Renderer, versus per-vertex); shadow map for a point light (six cube-map faces rendered from the light, resolution tier High means 1024 per face); shadow bias (depth bias stops self shadowing, called shadow acne) and normal bias (offset along the normal, too much gives peter-panning and light leaking on thin cubes); soft shadows (filtered PCF); ambient light (a constant or sky-derived term added to everything, which sets the colour of the unlit side); reflection probe and reflection source (what a metallic, smooth surface mirrors, here set to nothing); Lambert diffuse and the metallic workflow (metallic 0.7 leaves only 30 percent diffuse); HDR and tonemapping (maps unbounded light values into the displayable range, and the default volume here compressed highlights to 209 of 255, so post processing is off); Time.time with sin and cos for animation and a phase offset; local vs world scale.
