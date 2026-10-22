# Exercise 7: two code shaders combined (Sine Vertex Displacement + Specular Cell Shade)

Built with Claude Code.

## What was built and why
The catalogue asks for two shaders from ShadersLab or the unity-shaders repository, combined in a scene with the scanned object, written as code and not as Shader Graph. Source: github.com/adrian-miasik/unity-shaders (Adrian Miasik), folder `UnityShaders/Assets/Shaders/HLSL - Unlit/`, licence GNU GPL 3.0 (checked against the cloned LICENSE file). The two shaders used:
1. `Sine Vertex Displacement`: moves each vertex by sin(time + position * frequency) * amplitude per axis, in the vertex stage.
2. `Specular Cell Shade`: stepped Lambert diffuse plus a stepped Phong highlight, in the fragment stage.

They are merged into one shader, `Assets/Shaders/Ex07/NisseWobbleToon.shader` ("RTCG/Ex07 Wobble Toon"). Vertex stage runs shader 1, fragment stage runs shader 2. Because it is derived from GPL code, the file carries an attribution header and is itself GPL-3.0.

The scene is `Assets/Scenes/Ex07_CodeShaders.unity` (built by `RtcgPart2.BuildEx07`). Images in `Captures/Ex07/`: `0_before_urp_lit` (stock Lit), `1_cell_shade_only` (amplitude 0), four `2_combined_phase*` frames, `3_combined_exaggerated_amp0.06`, and `Ex07_loop.gif`. I viewed them: the cell shading shows hard light bands and white highlight blobs, and the combined frames show the mesh rippling and the shadow following it.

## Porting work and quirks
- The originals are built-in render pipeline CG (`UnityCG.cginc`, `fixed4`, `UnityObjectToClipPos`). They do not render in URP, so the port uses `Core.hlsl` and `Lighting.hlsl`, `TransformObjectToWorld`, `GetMainLight` and the `UnityPerMaterial` constant buffer (needed for the SRP Batcher).
- The original cell shade multiplies a world-space light direction with an object-space normal, which is only correct for an unrotated object. The port transforms the normal to world space.
- The original has no shadows. The port reads `shadowAttenuation` from the main light and adds a ShadowCaster pass that applies the same wobble, otherwise the shadow would stay on the undisplaced mesh.
- The original time term is `_Speed * _Time * 200`. With `_Time.x = t/20` that is about 5 rad/s at speed 0.5. The port uses `_Time.y * 8` (4 rad/s), close but not identical. A `_Phase` property was added so still renders can freeze the wave.
- Amplitude is in object units. Nisse is 1 unit tall, so the default 0.012 is a subtle ripple and the 0.06 render is deliberately extreme.
- Compile check: loaded through the editor script, `isSupported` is true with no shader messages (`Captures/_logs/ex07e.log`).
- The cell shading looks patchy because the scan's mesh has fine geometric noise in its normals, and a hard step exposes every one of them.

## Lecture terms to be able to explain
The two programmable stages and what each outputs (clip-space position from the vertex shader, colour from the fragment shader); object, world and clip space; interpolation of vertex outputs; Lambert diffuse as dot(N, L); Phong specular from the reflected view vector and a shininess exponent; `step` and `smoothstep`; why a displaced vertex needs a matching shadow caster pass; built-in pipeline versus URP shaders and the SRP Batcher; multi_compile shader variants for shadows.

Original shaders: Adrian Miasik, unity-shaders, GPL-3.0.
