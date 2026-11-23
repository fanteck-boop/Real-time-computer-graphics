# Ex9 notes: skeleton and real-time animation of Nisse

## What was built
Nisse got a skeleton and skin in code, then a scripted wave animation, rendered in Unity at 30 fps.
- Rig builder, weights and clip: `Assets/Editor/RtcgEx09.cs`. Motion function: `Assets/Scripts/Ex09Motion.cs`. Scene `Assets/Scenes/Ex09_Animation.unity`, prefab `Assets/Nisse/Nisse_Rigged.prefab`, mesh `Assets/Nisse/Nisse_skinned.asset`, clip `Assets/Animations/Nisse_Wave.anim`, controller `Assets/Animations/Nisse.controller`.
- Evidence in `Captures/Ex09`: `nisse_wave.mp4` (1280x720, 30 fps, 180 frames, 6 s), `nisse_wave.gif`, `frames/f_000..179.png`, `pose_extreme.png`, `analysis_*.png` (the views used to place joints), test pose renders in `test_*`.

## Skeleton
16 bones, placed by hand from per-height mesh extents and front, side and back renders of the scan (Nisse is 1 unit tall, front faces +Z, character right arm on +X): Root (never moves; in the first version it held the scan's base plate, which the cleaned scan no longer has), Hips, Spine, Head, four hat bones (Hat1, Hat2, Hat3, HatTip) so the long floppy hat tip can swing, Shoulder and Forearm for each arm, Leg and Foot for each leg. Bones have identity rotation at rest, so a bone's Euler angles rotate about world axes.

## Skinning
Unity has no automatic weighting, so the weights are computed in code. For every vertex and every bone: weight = 1 / ((distance to the bone segment / bone radius)^5 + 0.05). The four largest are kept and normalised to sum 1 (Unity's limit of 4 influences). Vertices on the base plate (old scan) got Root with weight 1. Bind poses are each bone's world-to-local matrix at rest. The mesh is the full scan (now 178,672 triangles in the glb; the first version had 252,265), baked into the normalised space; GPU skinning handled it without any need for the 25k LOD.
Seams: the weight depends only on vertex position. Scan vertices duplicated along UV seams share a position, so they get identical weights and move together, which avoids texture tearing at seams.

## Animation
`Ex09Motion.Pose(t)` describes a 6 s loop: right arm rises, forearm waves about 2.2 times per second, head tilts and looks around, a nod near the end, hips sway and bob, hat bones swing with a lag so the tip trails the head, left arm swings slightly. The editor samples it every 0.1 s into keyframes (Euler rotation curves per bone plus hip height) with smooth clamped-auto tangents. Unity interpolates between keys with Hermite-style curves. The last key equals the first, so the clip loops. An Animator with a one-state controller plays it. Capture ran at fixed 30 fps (Time.captureFramerate), 180 frames, mp4 and gif made with ffmpeg. I viewed six frames across the loop and extreme test poses.

## Honest assessment of artefacts
- Raising the arm stretches the sleeve and the torso side near the shoulder (soft blend of Spine and Shoulder weights); visible in frames around 1.4 s to 4 s.
- Weights are smooth blends, tuned only by exponent 3 to 5 and looking at renders; no hand painting. The exponent 5 gives sharper joints than 3, which looked too mushy in the weight heat map.
- Legs and feet are rigged but barely animated, because the boots were fused to the base plate in the first scan and would stretch. The scan was later replaced by a cleaned one without the base plate and the rig was rebuilt; the pose test looked the same as before.
- The scan is not watertight and normals are noisy; skinning does not fix that, so noisy shading moves with the mesh.
- Framing: Nisse is small and left of centre in the shot; not re-framed.
- The real-time rate of the scene was not measured separately. The sequence was captured at a fixed 30 fps timestep, so "above 25 fps" rests on that setting plus the fact that Ex8 showed this GPU renders far heavier scenes at hundreds of fps, not on a dedicated measurement of this scene.

## Blender route
The coordinator suggested trying Blender automatic weights first. I did not run that comparison: the scan has open boundaries, so bone heat on the full mesh was likely to fail, and a decimated or remeshed copy would lose the texture layout and need an FBX round trip. I used the in-Unity code weights, which keep the full textured scan. If the student prefers Blender weights, that remains untested.

## Terms for the oral exam
Skinning: moving mesh vertices by the bones that influence them, v' = sum of weight_i * boneMatrix_i * bindInverse_i * v. Bind pose: the pose in which the mesh was bound; bindposes store each bone's inverse rest transform. Bone weights: per-vertex influence of each bone, up to 4, summing to 1. Keyframes: stored values at chosen times. Interpolation: how values between keys are computed, here smooth tangent curves (versus linear or stepped). Real-time animation versus rendered sequence: real-time gets instant feedback and frame budget limits (here GPU skinning of 146k vertices), a rendered sequence can use slower, higher-quality rendering per frame.

Built with Claude Code
