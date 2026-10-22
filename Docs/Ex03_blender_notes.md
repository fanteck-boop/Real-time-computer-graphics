# Exercise 3, ray-traced half: Nisse product photo in Blender (Cycles)

Built with Claude Code.

## What was done
The scan (photogrammetry export, `model.glb`, 252,265 triangles, embedded diffuse and normal map) was imported into Blender 5.2 LTS and rendered headless with Cycles. The Blender MCP server was not connected, so the scene is built by the script `blender_scene.py` run with `blender -b -P`. The glb is Y-up with the object offset from the origin; Blender's importer converts to Z-up. The mesh was scaled to 0.30 m tall, centred in XY and placed with its lowest point on Z=0. The glb's embedded 4 MB diffuse texture was used, not the 14 MB original jpeg. Material: the imported Principled BSDF with roughness set to 0.55 (satin, scan has no roughness map).

## Setup and numbers
- Backdrop: a cyclorama (floor curving into a wall with a 0.8 m radius), diffuse grey (0.22), roughness 0.7, so there is no visible horizon line.
- Camera: 85 mm, at (0.12, -1.5, 0.2) m, depth of field with f/8 and focus distance 1.5 m. 1920x1080.
- Key: area light, square 0.8 m, 75 W, warm (1.0, 0.95, 0.88), at (-0.9, -0.7, 0.55).
- Fill: area light, square 1.2 m, 9 W, cool (0.9, 0.95, 1.0), at (1.0, -0.8, 0.25). Key to fill power ratio about 8:1, a contrasty look.
- Rim: area light, square 0.6 m, 55 W, at (0.5, 1.0, 0.6), behind the object, separates the silhouette from the backdrop.
- World: near black, strength 0.05, so nearly all light comes from the three area lights.
- Colour management: AgX view transform, look Medium High Contrast, exposure +0.2.
- Cycles: 384 samples, max 8 bounces, OptiX denoiser, GPU (RTX 4070 SUPER, OptiX). Render time 9.2 s.
- Files: `Captures/Ex03_Blender/nisse_blender_product.png` and `.blend`.

## Assessment
Texture is clearly visible, no black or blown-out areas. The contact shadow on the floor is soft but faint, because the backdrop is bright and the key is not very low. Raising the key power or lowering the backdrop albedo would make it read stronger.

## Terminology for the oral exam
- Path tracing: Monte Carlo estimate of the rendering equation. Rays are traced from the camera, bounce off surfaces, and light is gathered at each bounce. Gives global illumination, soft shadows and reflections physically, but is noisy at low sample counts, hence denoising.
- Rasterization (Unity): triangles are projected to the screen and shaded per pixel; shadows, reflections and indirect light are approximations (shadow maps, probes, SSR). Much faster, real time.
- BRDF: bidirectional reflectance distribution function, how much light arriving from direction wi leaves toward wo. The Principled BSDF is a layered, energy-conserving model based on microfacet theory (roughness = microfacet spread).
- Three-point lighting: key (main, defines form), fill (softer, lifts shadows, sets contrast ratio), rim or back light (edge highlight, separates subject from background).
- Area light: finite emitter, gives soft penumbra; larger size means softer shadows. Inverse-square falloff.
- Tone mapping or view transform: maps scene-referred linear HDR values to display range. AgX handles bright colours more gracefully than the older Filmic.
- Denoising: removes Monte Carlo noise using auxiliary buffers (albedo, normal).
- Depth of field: aperture (f-stop) and focus distance control blur.
