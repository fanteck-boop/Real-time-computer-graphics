# Exercise 6: holographic scanline shader in Shader Graph

Built with Claude Code.

## What was built and why
Effect: a holographic projection, a fragment effect (the object keeps its shape, only its look changes). The graph is `Assets/Shaders/Ex06/NisseHologram.shadergraph`, applied through `Assets/Materials/Part2/Ex06_Hologram.mat` in the scene `Assets/Scenes/Ex06_ShaderGraph.unity` (built by `RtcgPart2.BuildEx06`). Renders are in `Captures/Ex06/`: `hologram_vs_original.png` (hologram left, plain textured Nisse right), `hologram_closeup.png`, two property variants, and the looping `Ex06_loop.gif`.

## Honest status of the graph file
The `.shadergraph` file was written as JSON by a script, not drawn in the editor. Unity's Shader Graph importer accepted it: the asset loads as a Shader, `isSupported` is true and it reports zero shader messages (log `Captures/_logs/ex06d.log`), and it renders correctly on Nisse. It uses the real node types (Position, Split, Time, Property, Multiply, Add, Fraction, Step, Fresnel Effect, Saturate) and the Universal Unlit target. What is not verified is how the nodes look when opened in the Shader Graph window, because the node positions were generated. Open the graph, press Ctrl+A and tidy the layout, then take the screenshot of the graph that the catalogue asks for. If the editor reports a problem, the node recipe below rebuilds it by hand in a few minutes.

## Graph logic
Properties: Hologram Color (cyan 0.1, 0.75, 1.0), Scanline Density (graph default 90, scene material 60), Scroll Speed 0.4, Rim Power (graph default 2.5).
1. Position node (Object space) into Split. Take Y and multiply by Scanline Density. This gives a number that rises with height, so one unit of height holds that many lines.
2. Time node (Time output) multiplied by Scroll Speed. Add the two results. This makes the pattern slide upward over time.
3. Fraction, then Step with Edge 0.5. The output is 1 for half of every cycle and 0 for the rest, which is a hard-edged scanline mask.
4. Fresnel Effect with Power from Rim Power. It is 0 where the surface faces the camera and rises toward 1 at the silhouette.
5. Alpha: Multiply the scanline mask by 0.7, Add Fresnel, Add 0.05, Saturate, connect to Alpha. The surface is transparent, so the lines cut holes and the rim glows.
6. Base Color: Multiply Fresnel by 2, Add 0.5, Multiply by Hologram Color, connect to Base Color. The silhouette gets brighter than the centre.
Graph settings: Universal target, Unlit, Surface Type Transparent, alpha blending, Render Face Both. Bloom in the scene profile (threshold 0.9, intensity 1.0) makes the bright rim glow. The vertex block is left at its defaults, so this is a fragment effect only.

## Click-by-click recipe (if the file needs rebuilding)
Create > Shader Graph > URP > Unlit Shader Graph. In Graph Inspector, Graph Settings: Surface Type Transparent. Add four properties in the Blackboard: Hologram Color (Color, HDR), Scanline Density (Float 90), Scroll Speed (Float 0.4), Rim Power (Float 2.5). Then add the nodes in the order of the numbered list above and connect the ports as written. Save Asset.

## Hardest parts
- Making the scan pattern follow object height rather than the screen: use the Object-space Position, not Screen Position.
- Getting a glow instead of a flat colour: values above 1 only show as glow if the camera is HDR and bloom is on.
- Transparency and the textured base mesh: the hologram replaces the texture, so the Nisse texture does not show through.

## Lecture terms to be able to explain
Vertex versus fragment stage and which of them this graph affects; object space versus world space; the Fresnel effect (reflectance rises at grazing angles, here used as a rim term); alpha blending versus opaque rendering and why transparency needs sorting; HDR values and bloom; `_Time` as the animation driver; Shader Graph as a node front end that generates HLSL for the render pipeline target.
