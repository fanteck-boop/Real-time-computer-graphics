# Exercise 7: frustum culling, LOD and mipmapping (Kaiser)

Scene: `Assets/Scenes/Ex 7 Culling and Mips.unity`. Screenshots: `Captures/Ex07_Steps/`. Scripts: `Assets/Scripts/Ex7/`.

This exercise follows the lecture of 29 October. The scene is a big open field you can walk around in, with three things to look at:

1. **Frustum culling.** About 370 objects stand in the field. The camera only draws what is inside its view frustum. A counter shows how many objects pass my own frustum test and how many the engine actually drew. Press C and the frustum freezes: it is drawn as yellow lines, a top-down minimap appears, and every object turns green (inside) or red (outside).
2. **LOD (level of detail).** Every Nisse has four meshes, from the full 178 000 triangle scan down to 1 260 triangles. Unity swaps between them by how big the Nisse is on screen. T colours the levels, L forces one level on all of them.
3. **Mipmaps with the Kaiser filter.** A long corridor with a black and white checker floor, and two lanes with the same texture imported twice, once with the Box mip filter and once with Kaiser. M shows which mip level is used where. B changes how the textures are sampled.

## Controls

| Key | What it does |
|---|---|
| W A S D | walk |
| Shift | run |
| Mouse | look around |
| Esc | free the mouse cursor (click in the Game view to catch it again) |
| 1 / 2 / 3 | jump to the Nisse field / the mip corridor / the Box and Kaiser lanes |
| C | freeze or unfreeze the frustum |
| T | LOD tint on or off |
| L | force LOD: automatic, 0, 1, 2, 3, automatic again |
| M | mip level colours on or off |
| B | next sampling mode for the mip textures |
| H | hide or show the HUD |

## What I changed in the texture import settings

The task said to change the mipmap filtering to Kaiser. In the Inspector this is the "Mip Map Filtering" field under "Generate Mip Maps". In code it is `TextureImporter.mipmapFilter`, and in the `.meta` file it is the line `mipMapMode` (0 = Box, 1 = Kaiser).

| Texture | Before | After |
|---|---|---|
| `Assets/Nisse/Nisse_diffuse.jpeg` (shared with the other scenes) | Box | **Kaiser**. Nothing else changed: max size 4096 (2048 for Standalone), compression Normal Quality, mipmaps on, as before. The git diff of the `.meta` file is one line, `mipMapMode: 0` to `mipMapMode: 1`. |
| `Assets/Textures/Ex7/Ex7_Ground.png` | new | Kaiser, compressed, 512 px |
| `Assets/Textures/Ex7/Ex7_Corridor.png` | new | Kaiser, uncompressed, 1024 px |
| `Assets/Textures/Ex7/Ex7_Lane_Kaiser.png` | new | Kaiser, uncompressed, 1024 px, Read/Write on |
| `Assets/Textures/Ex7/Ex7_Lane_Box.png` | new | **Box on purpose** (the comparison copy), uncompressed, 1024 px, Read/Write on |

Because the Nisse texture is shared, the Nisse in Ex 2 to Ex 6 now also use Kaiser mips. To undo it: select the texture, set Mip Map Filtering back to Box, press Apply.

Read/Write is only on for the two lane textures so the builder script can read their mip levels and print how much contrast each level keeps. Rendering does not need it. The TextMesh labels "BOX" and "KAISER" use Unity's built-in font texture, which is made at runtime and has no import settings, so it is the one texture in the scene that is not Kaiser.

## The theory, short

### Frustum and the 6-plane AABB test

A perspective camera sees a cut-off pyramid, the view frustum. It is bounded by 6 planes: left, right, top, bottom, near (0.1 m in front of the camera here) and far (120 m). `GeometryUtility.CalculateFrustumPlanes(camera, planes)` gives these 6 planes. Each plane has a normal that points into the frustum and a distance, so for any point you can compute a signed distance: positive means on the inside of that plane.

Testing every triangle would cost more than drawing it, so the test uses the object's bounding box instead. An AABB (axis aligned bounding box) is the smallest box around the object whose sides are parallel to the world axes, so it is stored as just a centre and a size. `GeometryUtility.TestPlanesAABB(planes, bounds)` checks the box against each plane in turn. For a plane, it only needs the one corner of the box that lies furthest in the direction of the plane's normal. If even that corner is on the outside of the plane, the whole box is outside, and the object can be skipped. Only if the box survives all 6 planes is it counted as inside.

The test is conservative. A box that is outside the frustum but lies diagonally next to one of its corners can pass all 6 plane tests one by one, so it is sometimes called inside when it is not. It never calls a visible object outside. That is the right way round for culling: drawing a few extra objects costs a little time, skipping a visible one would make it disappear.

Unity does the same kind of test itself every frame for every camera, before anything is sent to the GPU. My script just does it again, by hand, so the numbers can be shown and compared.

### LOD by screen height

A model that covers 20 pixels on screen does not need 178 000 triangles. A `LODGroup` holds several versions of the model and a threshold for each, given as a fraction of the screen height. Every frame Unity works out:

`screen height = (object size / (2 * distance * tan(field of view / 2))) * LOD bias`

and picks the first level whose threshold the object still reaches. Below the last threshold the object is not drawn at all. Object size is `LODGroup.size` (the largest side of the bounds, 1.08 m for the Nisse on its plinth), distance is from the camera to the group's reference point, and the LOD bias comes from the quality settings. In this project the PC quality level has LOD bias 2, which makes every switch happen at twice the distance.

`LodDebug.LevelFor` computes this formula itself, so the HUD can count how many Nisse sit at each level.

### Mipmaps and how the GPU picks a level

When a texture is imported with mipmaps, Unity stores a chain of smaller copies: 1024 x 1024, 512, 256 and so on down to 1 x 1, eleven levels for a 1024 texture. Each level is made from the one above it by filtering. The chain costs one third more memory.

When the GPU shades a pixel it looks at how fast the texture coordinates change from that pixel to its neighbours (the screen space derivatives, worked out per 2 x 2 block of pixels). From that it knows how many texels of level 0 fall inside one screen pixel. If one pixel covers about 4 x 4 texels, it reads level 2, where those 16 texels have already been averaged into one. The level is roughly `log2(texels per pixel)`, plus the mip bias.

Without mips, a far away pixel would pick one texel out of many, nearly at random, and the picture flickers and shows moiré (false patterns). With mips the far floor turns into its average colour instead.

- **Trilinear** filtering blends the two nearest levels, so there is no visible line where one level ends.
- **Bilinear** only uses the nearest level, so you see a hard step at each change.
- **Anisotropic** filtering is for surfaces seen at a flat angle, like a floor. There a pixel covers a long thin strip of the texture. Without it, the GPU sizes the level by the long side of the strip and the floor goes blurry quickly. With it, the GPU picks the level by the short side and takes several samples along the long side. In this project the PC quality level has Anisotropic Textures set to "Forced On", so every texture gets it.

### Box vs Kaiser mip filter

This is only about how Unity builds the smaller levels when the texture is imported. The GPU samples them the same way afterwards.

- **Box**: every texel of the next level is the plain average of the 2 x 2 texels above it. It is cheap and simple, but it is a poor low-pass filter. Detail that is too fine for the smaller level is not removed properly, and edges get smeared over 2 texels each step down, so mips tend to look soft and can keep false patterns.
- **Kaiser**: every new texel is a weighted average over a wider area, with weights from a Kaiser window (a smooth bell shape). It removes the detail that the smaller level cannot hold more cleanly, and keeps edges a bit sharper than Box. Unity's own description is a sharpening filter for when textures look too blurry in the distance.

## What the frames show

All images are 1280 x 720 and were rendered by `RtcgEx7Scene.CaptureSteps` in a headless editor. Keys and the mouse cannot be pressed there, so the capture script calls the same methods the keys call (`Freeze`, `SetTint`, `SetForcedLod`, `SetMipColours`, `SetMode`, `SetPose`).

**step0_walk_view.png**: the normal first person view from the spawn point, eye height 1.65 m. Nisse on white plinths in a grid, pillars and the boundary wall behind.

**step1a_frustum_minimap.png**: the frustum frozen while looking across the field (player at (-6, 0, -14), turned 35 degrees). Seen from straight above, with "forward" pointing up. The yellow lines are the edges of the frozen frustum; the far plane is the yellow line along the top (120 m away). 156 objects are green, 216 red. The grey strips on the right are the mip corridor and the lanes.

**step1b_frustum_from_player.png**: what the player sees at that same moment. Everything you can see is green; the red objects are all outside the picture. The thin yellow line along the top and left edge is the frozen frustum's own edge, drawn right where the camera is.

**step2a_lod_tint.png**: LOD tint from the spawn point. Green (LOD0) only for the closest Nisse, then yellow, orange, and red right at the back. The HUD numbers for this view: LOD0 4, LOD1 60, LOD2 280, LOD3 12, culled 0 (all 356 Nisse); in view: 2, 17, 75, 4.

**step2b_lod_forced0.png** and **step2c_lod_forced3.png**: the same view close to one Nisse with every Nisse forced to LOD0, then to LOD3. Far away there is almost no difference, which is the whole point of LOD. On the nearest Nisse LOD3 is clearly smoother: the beard loses its fringes and the hat and boots get rounder. `extra_step2_lod0_left_lod3_right.png` puts the nearest Nisse from both pictures side by side at 2x.

**step3a_mip_floor_plain.png**: the corridor, 6 m wide, 150 m long, 16 px checker. Close up the checker is sharp. Further away it turns into flat grey, with a faint ring-shaped moiré in the middle distance.

**step3b_mip_floor_colours.png**: the same with M on. Each colour is one mip level: red 0 at your feet, orange 1, yellow 2, green 3, cyan 4, blue 5, purple 6, pink 7, white 8. The bands bend into a shallow V towards the walls, because the angle to the floor changes across the picture.

**step3c_box_vs_kaiser.png**: the two lanes from their start, Box on the left, Kaiser on the right. Same texture, same size, same tiling. Up close they look the same. In the middle distance the Box lane shows ring-shaped moiré; the Kaiser lane has finer, straighter hatching with fewer rings.

**step3d_box_vs_kaiser_close.png**: a 320 x 180 pixel window from the middle of step3c, blown up 4 times without smoothing. The rings in the Box lane (left) are clearly stronger than in the Kaiser lane (right).

Extra images for the lanes:
- `extra_step3c_box_vs_kaiser_aniso_off.png` and `extra_step3d_box_vs_kaiser_close_aniso_off.png`: the same with anisotropic filtering off (B once). Now the GPU uses smaller mips earlier. The Kaiser lane fades to an even grey sooner, while the Box lane keeps faint bands further out.
- `extra_step3c_lanes_mip_colours.png` and `extra_step3c_lanes_mip_colours_aniso_off.png`: mip colours on the lanes. Both lanes show exactly the same bands, which shows the GPU picks the same level for both; only the content of the levels differs. Turning anisotropic off moves every band about two levels closer.

The builder also printed how much contrast is left in each mip level of the lane texture (standard deviation of the grey value, 0 = flat grey):

| Level | Size | Box | Kaiser |
|---|---|---|---|
| 0 | 1024 | 0.400 | 0.400 |
| 1 | 512 | 0.347 | 0.360 |
| 2 | 256 | 0.257 | 0.269 |
| 3 | 128 | 0.095 | 0.031 |
| 4 | 64 | 0.029 | 0.004 |

So on levels 1 and 2, where the checker can still be shown, Kaiser keeps a little more contrast (sharper). On levels 3 and 4, where the cells are smaller than a texel and can not be shown, Kaiser has removed almost all of it, while Box keeps leftovers. Those leftovers are what makes the rings in the Box lane. The difference is real but not huge; you have to look at the middle distance of the lanes to see it.

**step4_hud_overview.png**: the HUD with the frustum frozen and the minimap in the top right corner. Objects 372, inside the frustum 156, engine drawn 318 (the minimap camera counts too, see below), inside frozen 156, outside 216, LOD counts, 1.39 M triangles in the drawn meshes, mip settings. OnGUI does not run in a headless editor, so for this picture the capture script writes the HUD's own text (`Ex7Hud.BuildText()`) on a TextMesh in front of the camera and pastes the minimap into the corner. In Play mode OnGUI draws the same text.

## Numbers and why

| What | Value | Why |
|---|---|---|
| Nisse | 19 x 19 grid, 4 m apart, none within 4.5 m of the spawn: 356 | enough to see rings of LOD levels, and the nearest ones are 5.7 m away so only about 4 reach LOD0 |
| Pillars | 16, on a circle of 42 m | big objects for the frustum test |
| LOD thresholds | 30 %, 10 %, 4 %, 2 % of the screen height, culled below 2 % | about as asked (0.30 / 0.10 / 0.02) with a fourth level added for LOD3 |
| LOD switch distances | about 6 m, 19 m, 47 m, 94 m | from the formula above with size 1.08 m, 60 degree field of view and LOD bias 2 |
| LOD meshes | 178 473 / 25 226 / 5 045 / 1 260 triangles | LOD0 is the full scan, LOD1 to LOD3 are the meshes in `Assets/Nisse/LOD/` |
| Camera | field of view 60, near 0.1 m, far 120 m, HDR off, MSAA off, post processing off | far 120 m so the corridor is long enough to reach small mips, and the far plane is a visible part of the frozen frustum |
| Sun | directional, 1.2, rotation (50, -30, 0), **no shadows** | shadows would make `Renderer.isVisible` also count objects that only cast a shadow into view, which would muddy the comparison. It also keeps the frame cheap |
| Ambient | flat colour (0.45, 0.47, 0.52), reflections off | |
| Corridor texture | 1024 px, 16 px checker, 2 m per tile | cells of about 3 cm, so the mips change many times over 150 m |
| Lane texture | 1024 px, checker turned 23 degrees, cells about 16 px, 4.8 m per tile | see "what went wrong" below |
| Minimap | 512 x 512 render texture, orthographic, 80 m across, centred 22 m in front of the frozen camera | |

Triangles with everything forced to LOD0: the 81 Nisse in view in step2b would be 81 x 178 473 = 14.5 million triangles, against about 1 million in automatic mode in a similar view. That is the reason to use LOD.

## Step by step in the editor

You can rebuild everything by hand. The builder `Assets/Editor/RtcgEx7Scene.cs` (methods `BuildScene` and `CaptureSteps`) does exactly these steps in code if you want to compare.

1. **Scene.** File > New Scene > Empty (URP). Save it as `Assets/Scenes/Ex 7 Culling and Mips.unity`.
2. **Textures.** Put the test textures in `Assets/Textures/Ex7/` (any black and white checker works; the builder makes them from code). Select one, and in the Inspector set: Texture Type Default, Wrap Mode Repeat, Filter Mode Trilinear, Advanced > Generate Mip Maps on, **Mip Map Filtering Kaiser**, Max Size 2048, Compression None (so compression artefacts do not hide the mip filter). Press Apply. For the Box copy, duplicate the file (Ctrl+D) and set Mip Map Filtering to Box.
3. **Nisse texture.** Select `Assets/Nisse/Nisse_diffuse.jpeg`, change only Mip Map Filtering from Box to Kaiser, Apply.
4. **Materials.** In `Assets/Materials/Ex7/` make URP Lit materials for the ground (Ex7_Ground texture, Tiling 35 x 42.5), walls, plinth and pillars (plain colours). The corridor and lanes use **URP Unlit** so the light does not change the grey levels: corridor Tiling 3 x 75, lanes Tiling 0.5 x 31.25.
5. **The LOD prefab.**
   - Create an empty GameObject `Ex7_NisseLOD` at (0, 0, 0).
   - Child `Plinth`: a Cylinder at (0, 0.03, 0), scale (1.1, 0.03, 1.1), remove its collider, plinth material.
   - Children `LOD0` to `LOD3`: empty GameObjects with a Mesh Filter (meshes: Nisse.obj, Nisse_LOD1, Nisse_LOD2, Nisse_LOD3) and a Mesh Renderer with `Nisse.mat`, Cast Shadows Off. All four get the same transform: position (-3.3431, 8.7565, -6.6809), scale 1.21252. That makes the full scan 1 m tall and puts its feet on the plinth. The LOD meshes were made from the same scan, so they line up with the same numbers.
   - On the root: Add Component > LOD Group. It starts with LOD 0, 1, 2. Right click a bar and Insert Before to get four. Drag the edges between the bars so the percentages read 30 %, 10 %, 4 % and 2 % (the white area after the last bar is "Culled"). Click each bar and drag the matching LOD child into its Renderers list. Set Fade Mode to None.
   - Add a Capsule Collider: centre (0, 0.5, 0), radius 0.35, height 1, so you bump into the Nisse.
   - Drag the root into `Assets/Prefabs/Ex7/` to make the prefab.
6. **Nisse field.** Empty GameObject `Nisse Field` at (0, 0, 0), add `NisseGrid`, drag the prefab in, right click the component header and choose **Spawn Grid**.
7. **Pillars.** Empty GameObject `Pillars` with 16 cubes (scale 1.2 x 4 x 1.2) on a circle of 42 m. Every direct child of `Nisse Field` and `Pillars` counts as one object for the frustum test.
8. **Ground, walls, mip area.** Plane `Ground` at (20, 0, 37.5), scale (14, 1, 17), keep its Mesh Collider (the CharacterController stands on it). Four boundary cubes 1.5 m high around x -50 to 90, z -47.5 to 122.5. Corridor floor: Plane at (55, 0.005, 39), scale (0.6, 1, 15), no collider, walls at x 51.8 and 58.2, 2.5 m high. Lanes: Planes at x 70.7 (Box) and 73.3 (Kaiser), scale (0.24, 1, 15), and a thin dark divider at x 72. Optional labels: GameObject > 3D Object > Legacy > 3D Text, rotated 90 degrees on X so it lies on the floor.
9. **Light.** Directional Light, rotation (50, -30, 0), intensity 1.2, Shadow Type No Shadows. Window > Rendering > Lighting > Environment: Source Color, Ambient Color (0.45, 0.47, 0.52), Reflection intensity 0.
10. **Player.**
    - Empty GameObject `Player` at (0, 0, 0). Add Component > Character Controller: Height 1.8, Radius 0.35, Center (0, 0.9, 0), Step Offset 0.3, Slope Limit 45. The controller does the collision against the ground and walls, so no Rigidbody is needed.
    - Optional child Capsule `Body` (no collider) at (0, 0.9, 0), scale (0.7, 0.9, 0.7), so the player shows up in the minimap. The camera is inside it, and from inside you only see back faces, which are not drawn, so it does not block the view.
    - Child `Head` at (0, 1.65, 0), and under it the camera `Main Camera` (tag MainCamera): Field of View 60, Clipping Planes 0.1 / 120, Background Type Solid Color, Rendering > Post Processing off, Anti-aliasing None, Output > HDR Off, MSAA Off. Keep its Audio Listener.
    - Add `FirstPersonWalker` to `Player`: drag `Head` and the camera in. Teleport Points (0, 0, 0), (55, 0, -38), (72, 0, -39), all with yaw 0.
11. **Minimap camera.** New Camera `Spectator Camera (minimap)`: Projection Orthographic, Size 40, Clipping Planes 1 / 400, Solid Color background, Post Processing off, no Audio Listener. Untick the component so it is off. `FrustumDebug` makes its render texture, moves it and switches it on when you press C.
12. **Debug object.** Empty GameObject `Ex7 Debug` with `FrustumDebug`, `LodDebug`, `MipDebug` and `Ex7Hud`. References:
    - FrustumDebug: Player Cam = Main Camera, Spectator Cam = the minimap camera, Object Roots = Nisse Field and Pillars, Lod Debug = the LodDebug component.
    - LodDebug: Cam = Main Camera, Groups Root = Nisse Field, Frustum = the FrustumDebug component.
    - MipDebug: Surfaces = corridor floor, Lane Box, Lane Kaiser. Textures = Ex7_Corridor, Ex7_Lane_Box, Ex7_Lane_Kaiser.
    - Ex7Hud: the three components above.
13. Press Play.

## The scripts

### FirstPersonWalker.cs

Walking and looking. The yaw (left/right) turns the whole Player, the pitch (up/down) only turns the Head, so the CharacterController capsule always stands upright. `controller.Move` moves the capsule and stops it at colliders; gravity keeps it pressed to the ground. `SetPose` is there because a CharacterController ignores direct changes of `transform.position` while it is enabled, and the teleports and the capture script both need to move the player.

```csharp
// Ex 7. First person walking: WASD to move, Shift to run, mouse to look, Esc frees the cursor, click locks it again.
// Keys 1, 2 and 3 jump to the three areas (Nisse field, mip corridor, Box vs Kaiser lanes).
// The project only has the new Input System switched on, so this reads Keyboard.current and Mouse.current.
// (Input.GetKey would throw an exception here.)
// Put it on the Player object, which also has a CharacterController. The camera sits on a child object ("Head").
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonWalker : MonoBehaviour
{
    public Transform head;                 // child at eye height, holds the camera, only turns up and down
    public Camera cam;                     // the player camera (on the head)
    public float walkSpeed = 3f;           // metres per second
    public float runSpeed = 8f;            // with Shift
    public float lookSensitivity = 0.1f;   // degrees per pixel of mouse movement
    public float gravity = -9.81f;

    [Header("Teleport points (keys 1, 2, 3): feet position and yaw")]
    public Vector3[] teleportPoints = { Vector3.zero };
    public float[] teleportYaw = { 0f };

    CharacterController controller;
    float yaw, pitch, verticalSpeed;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        yaw = transform.eulerAngles.y;
        pitch = head.localEulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        LockCursor(true);
    }

    void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null) return;

        // cursor: Esc frees it, a click catches it again
        if (kb.escapeKey.wasPressedThisFrame) LockCursor(false);
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);

        // mouse look: yaw turns the whole body, pitch only the head
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 d = mouse.delta.ReadValue() * lookSensitivity;
            yaw += d.x;
            pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
        }
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        head.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        // walking
        Vector2 input = new Vector2(
            (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
            (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
        input = Vector2.ClampMagnitude(input, 1f);
        float speed = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? runSpeed : walkSpeed;
        Vector3 velocity = (transform.right * input.x + transform.forward * input.y) * speed;

        // gravity, so the controller stays on the floor
        if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = -1f;
        verticalSpeed += gravity * Time.deltaTime;
        velocity.y = verticalSpeed;
        controller.Move(velocity * Time.deltaTime);

        // teleports
        Key[] keys = { Key.Digit1, Key.Digit2, Key.Digit3 };
        for (int i = 0; i < keys.Length && i < teleportPoints.Length; i++)
            if (kb[keys[i]].wasPressedThisFrame) SetPose(teleportPoints[i], teleportYaw[i], 0f);
    }

    // Puts the player somewhere. Also used by the capture script, since keys and mouse cannot be simulated headless.
    public void SetPose(Vector3 feet, float newYaw, float newPitch)
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        controller.enabled = false;            // a CharacterController overrides transform changes while enabled
        transform.position = feet;
        controller.enabled = true;
        yaw = newYaw; pitch = newPitch; verticalSpeed = 0f;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }
}
```

### NisseGrid.cs

Places the prefab copies. In edit mode it uses `PrefabUtility.InstantiatePrefab` (inside `#if UNITY_EDITOR`) so the copies stay linked to the prefab: change the LOD thresholds on the prefab and all 356 follow.

```csharp
// Ex 7. Places copies of the LOD Nisse prefab in a grid around the spawn point, with a free circle in the middle
// so the player does not start inside a Nisse. Right click the component header and pick "Spawn Grid".
// The editor builder (RtcgEx7Scene) calls the same method, so the scene is built the same way you would build it by hand.
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class NisseGrid : MonoBehaviour
{
    public GameObject prefab;          // Ex7_NisseLOD prefab (has the LODGroup)
    public int countX = 19, countZ = 19;
    public float spacing = 4f;         // metres between Nisse
    public float clearRadius = 4.5f;   // no Nisse closer than this to the centre
    public int seed = 7;               // random turn of each Nisse, fixed seed so every build looks the same

    [ContextMenu("Spawn Grid")]
    public void Spawn()
    {
        for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);
        var rng = new System.Random(seed);
        for (int ix = 0; ix < countX; ix++)
            for (int iz = 0; iz < countZ; iz++)
            {
                var pos = new Vector3((ix - (countX - 1) * 0.5f) * spacing, 0f, (iz - (countZ - 1) * 0.5f) * spacing);
                float yaw = (float)rng.NextDouble() * 360f;
                if (pos.magnitude < clearRadius) continue;
                GameObject go;
#if UNITY_EDITOR
                if (!Application.isPlaying) go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, transform);
                else
#endif
                go = Instantiate(prefab, transform);
                go.name = $"Nisse_{ix:00}_{iz:00}";
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
    }
}
```

### FrustumDebug.cs

- `Init` collects the objects. For each one it stores a world space AABB around all its renderers (Nisse and plinth together), its original materials, and the renderers that count as "drawn" (only the LOD meshes, not the plinth, so a Nisse that the LODGroup culls does not count as drawn).
- `Recount` runs every frame: `CalculateFrustumPlanes` + `TestPlanesAABB` for every object (my own count), and `Renderer.isVisible` for the engine's count. `isVisible` is true when any camera drew the renderer in the last frame.
- `Freeze` stores the planes once, tests every object against them, draws the 12 edges of the frustum with LineRenderers (corners from `Camera.CalculateFrustumCorners` at the near and far distance), and moves the minimap camera above the frozen camera.
- `ApplyMaterials` is the one place that decides which material every tested renderer gets: frozen tint first, then the LOD tint, otherwise the original. Both tints are plain material swaps (`sharedMaterial`), no MaterialPropertyBlock. The project's render pipeline has the GPU Resident Drawer on, and a MaterialPropertyBlock would take those renderers out of it.

```csharp
// Ex 7. Frustum culling made visible.
// Every frame: the 6 planes of the player camera's frustum are tested against the bounding box (AABB) of every object,
// and the result is counted next to the engine's own number (Renderer.isVisible).
// Key C freezes the frustum: the planes stop following the camera, the frustum is drawn as yellow lines,
// a top-down camera renders a minimap, and every object turns green (inside the frozen frustum) or red (outside).
// You can then keep walking and look at the frozen frustum from outside.
// This script also owns the materials of the tested objects, so the LOD tint (LodDebug) goes through ApplyMaterials too.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class FrustumDebug : MonoBehaviour
{
    [Header("Cameras")]
    public Camera playerCam;
    public Camera spectatorCam;            // orthographic, looks straight down, renders into the minimap texture
    [Header("Objects: every direct child of these roots counts as one object")]
    public Transform[] objectRoots;
    public LodDebug lodDebug;
    [Header("Look")]
    public Color insideColor = new Color(0.15f, 0.8f, 0.2f);
    public Color outsideColor = new Color(0.9f, 0.12f, 0.1f);
    public Color lineColor = new Color(1f, 0.85f, 0.05f);
    public float lineWidth = 0.35f;        // metres
    public float minimapAhead = 22f;       // the minimap is centred this far in front of the frozen camera
    public float minimapHalfSize = 40f;    // orthographic size: half the height of the minimap in metres

    public bool Frozen { get; private set; }
    public int ObjectCount => items.Count;
    public int InsideLive { get; private set; }      // own AABB test against the live player frustum
    public int InsideFrozen { get; private set; }    // own AABB test against the frozen frustum
    public int EngineVisible { get; private set; }   // objects with at least one renderer that has isVisible == true
    public long DrawnTriangles { get; private set; } // triangles of the meshes the engine reports as visible
    public RenderTexture Minimap { get; private set; }

    class Item
    {
        public Bounds bounds;          // world space axis aligned box around the whole object
        public Renderer[] renderers;   // every renderer of the object (gets tinted)
        public Material[] original;
        public Renderer[] counted;     // the renderers that count for "drawn": the LOD meshes (not the plinth)
        public int[] triangles;
        public bool insideFrozen;
    }

    readonly List<Item> items = new List<Item>();
    readonly Plane[] livePlanes = new Plane[6];
    readonly Plane[] frozenPlanes = new Plane[6];
    Material insideMat, outsideMat;
    LineRenderer[] edges;
    bool ready;

    void Start() { Init(); }

    public void Init()
    {
        if (ready) return;
        ready = true;
        insideMat = MakeLit("Frustum inside", insideColor);
        outsideMat = MakeLit("Frustum outside", outsideColor);

        foreach (var root in objectRoots)
            foreach (Transform child in root) items.Add(MakeItem(child));

        // 12 edges of the frustum: near rectangle, far rectangle, 4 side edges
        var lineMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Frustum lines", hideFlags = HideFlags.DontSave };
        lineMat.SetColor("_BaseColor", lineColor);
        var holder = new GameObject("Frozen frustum lines") { hideFlags = HideFlags.DontSave };
        edges = new LineRenderer[12];
        for (int i = 0; i < 12; i++)
        {
            var go = new GameObject("Edge " + i) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(holder.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.positionCount = 2; lr.widthMultiplier = lineWidth;
            lr.sharedMaterial = lineMat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            lr.enabled = false;
            edges[i] = lr;
        }

        Minimap = new RenderTexture(512, 512, 24) { name = "Minimap", hideFlags = HideFlags.DontSave };
        spectatorCam.targetTexture = Minimap;
        spectatorCam.enabled = false;
    }

    static Material MakeLit(string name, Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, hideFlags = HideFlags.DontSave };
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.2f);
        return m;
    }

    Item MakeItem(Transform t)
    {
        var it = new Item();
        it.renderers = t.GetComponentsInChildren<Renderer>(true);
        it.original = new Material[it.renderers.Length];
        it.bounds = it.renderers[0].bounds;
        for (int i = 0; i < it.renderers.Length; i++)
        {
            it.original[i] = it.renderers[i].sharedMaterial;
            it.bounds.Encapsulate(it.renderers[i].bounds);
        }
        var group = t.GetComponent<LODGroup>();
        if (group != null)
        {
            var list = new List<Renderer>();
            foreach (var lod in group.GetLODs()) list.AddRange(lod.renderers);
            it.counted = list.ToArray();
        }
        else it.counted = it.renderers;
        it.triangles = new int[it.counted.Length];
        for (int i = 0; i < it.counted.Length; i++)
        {
            var mf = it.counted[i].GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) it.triangles[i] += (int)(mf.sharedMesh.GetIndexCount(s) / 3);
        }
        return it;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.cKey.wasPressedThisFrame)
        {
            if (Frozen) Unfreeze(); else Freeze();
        }
        Recount();
    }

    // The test itself. CalculateFrustumPlanes gives 6 planes (left, right, bottom, top, near, far) with normals pointing
    // into the frustum. TestPlanesAABB returns false as soon as the box lies completely on the outside of one plane.
    public void Recount()
    {
        GeometryUtility.CalculateFrustumPlanes(playerCam, livePlanes);
        int inside = 0, visible = 0; long tris = 0;
        foreach (var it in items)
        {
            if (GeometryUtility.TestPlanesAABB(livePlanes, it.bounds)) inside++;
            bool any = false;
            for (int i = 0; i < it.counted.Length; i++)
                if (it.counted[i].isVisible) { any = true; tris += it.triangles[i]; }
            if (any) visible++;
        }
        InsideLive = inside; EngineVisible = visible; DrawnTriangles = tris;
    }

    public void Freeze()
    {
        Init();
        Frozen = true;
        GeometryUtility.CalculateFrustumPlanes(playerCam, frozenPlanes);
        int inside = 0;
        foreach (var it in items)
        {
            it.insideFrozen = GeometryUtility.TestPlanesAABB(frozenPlanes, it.bounds);
            if (it.insideFrozen) inside++;
        }
        InsideFrozen = inside;

        // frustum corners in world space
        var n = new Vector3[4]; var f = new Vector3[4];
        var full = new Rect(0, 0, 1, 1);
        playerCam.CalculateFrustumCorners(full, playerCam.nearClipPlane, Camera.MonoOrStereoscopicEye.Mono, n);
        playerCam.CalculateFrustumCorners(full, playerCam.farClipPlane, Camera.MonoOrStereoscopicEye.Mono, f);
        var ct = playerCam.transform;
        for (int i = 0; i < 4; i++) { n[i] = ct.TransformPoint(n[i]); f[i] = ct.TransformPoint(f[i]); }
        for (int i = 0; i < 4; i++)
        {
            SetEdge(i, n[i], n[(i + 1) % 4]);       // near rectangle (tiny, the near plane is only 0.1 m away)
            SetEdge(4 + i, f[i], f[(i + 1) % 4]);   // far rectangle
            SetEdge(8 + i, n[i], f[i]);             // side edges
        }

        // minimap camera: high above, looking down, "up" in the picture is the direction the frozen camera looked
        Vector3 fwd = Vector3.ProjectOnPlane(ct.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        spectatorCam.transform.position = ct.position + fwd * minimapAhead + Vector3.up * 150f;
        spectatorCam.transform.rotation = Quaternion.LookRotation(Vector3.down, fwd);
        spectatorCam.orthographic = true;
        spectatorCam.orthographicSize = minimapHalfSize;
        spectatorCam.enabled = true;
        ApplyMaterials();
    }

    public void Unfreeze()
    {
        Init();
        Frozen = false;
        foreach (var e in edges) e.enabled = false;
        spectatorCam.enabled = false;
        ApplyMaterials();
    }

    void SetEdge(int i, Vector3 a, Vector3 b)
    {
        edges[i].SetPosition(0, a); edges[i].SetPosition(1, b); edges[i].enabled = true;
    }

    // One place decides the material of every tested renderer: frozen tint first, then LOD tint, else the original.
    public void ApplyMaterials()
    {
        foreach (var it in items)
            for (int i = 0; i < it.renderers.Length; i++)
            {
                var r = it.renderers[i];
                Material m = it.original[i];
                if (Frozen) m = it.insideFrozen ? insideMat : outsideMat;
                else if (lodDebug != null)
                {
                    var lm = lodDebug.TintFor(r);
                    if (lm != null) m = lm;
                }
                if (r.sharedMaterial != m) r.sharedMaterial = m;
            }
    }

    void OnDisable()
    {
        // put the real materials back
        foreach (var it in items)
            for (int i = 0; i < it.renderers.Length; i++)
                if (it.renderers[i] != null) it.renderers[i].sharedMaterial = it.original[i];
    }
}
```

### LodDebug.cs

- `SetForcedLod` calls `LODGroup.ForceLOD` on every group. -1 hands control back to Unity.
- `TintFor` returns the colour material for a renderer, looked up by which LOD level it belongs to. Every LOD level has its own renderer, so tinting by level needs no work per frame: the tint follows the switches by itself.
- `LevelFor` is the screen height formula from the theory part. The HUD counts come from it, not from the engine, because Unity does not expose which level a group is showing. I checked it against the pictures: in step2a the green, yellow, orange and red rings fall where the formula says.

```csharp
// Ex 7. LOD (level of detail) made visible.
// Key L cycles a forced LOD level on every LODGroup: automatic, 0, 1, 2, 3 (LODGroup.ForceLOD, -1 means automatic).
// Key T tints each LOD level in its own colour: LOD0 green, LOD1 yellow, LOD2 orange, LOD3 red.
// The counts per level are worked out here with the same rule Unity uses: the object's size divided by the height
// of the view at the object's distance, times QualitySettings.lodBias, compared with the LOD thresholds.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class LodDebug : MonoBehaviour
{
    public Camera cam;
    public Transform groupsRoot;           // parent of all Nisse with a LODGroup
    public FrustumDebug frustum;           // owns the materials, see FrustumDebug.ApplyMaterials
    public Color[] lodColors =
    {
        new Color(0.15f, 0.8f, 0.2f),      // LOD0 green
        new Color(0.95f, 0.85f, 0.1f),     // LOD1 yellow
        new Color(1f, 0.5f, 0.05f),        // LOD2 orange
        new Color(0.9f, 0.12f, 0.1f),      // LOD3 red
    };

    public bool Tint { get; private set; }
    public int ForcedLod { get; private set; } = -1;
    public int[] Counts { get; } = new int[5];       // LOD0..LOD3, and [4] = too small, culled by the LODGroup
    public int[] CountsInView { get; } = new int[5]; // the same, only for Nisse inside the camera frustum

    LODGroup[] groups;
    readonly Dictionary<Renderer, int> levelOf = new Dictionary<Renderer, int>();
    Material[] tintMats;
    readonly Plane[] planes = new Plane[6];
    bool ready;

    void Start() { Init(); }

    public void Init()
    {
        if (ready) return;
        ready = true;
        groups = groupsRoot.GetComponentsInChildren<LODGroup>();
        foreach (var g in groups)
        {
            var lods = g.GetLODs();
            for (int k = 0; k < lods.Length; k++)
                foreach (var r in lods[k].renderers) levelOf[r] = k;
        }
        tintMats = new Material[lodColors.Length];
        for (int k = 0; k < lodColors.Length; k++)
        {
            tintMats[k] = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "LOD tint " + k, hideFlags = HideFlags.DontSave };
            tintMats[k].SetColor("_BaseColor", lodColors[k]);
            tintMats[k].SetFloat("_Smoothness", 0.2f);
        }
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.tKey.wasPressedThisFrame) SetTint(!Tint);
            if (kb.lKey.wasPressedThisFrame) SetForcedLod(ForcedLod >= 3 ? -1 : ForcedLod + 1);
        }
        Recount();
    }

    public void SetTint(bool on)
    {
        Init();
        Tint = on;
        frustum.ApplyMaterials();
    }

    public void SetForcedLod(int level)
    {
        Init();
        ForcedLod = level;
        foreach (var g in groups) g.ForceLOD(level);
    }

    // Material for a renderer when the tint is on: the colour of the LOD level that renderer belongs to.
    public Material TintFor(Renderer r)
    {
        if (!Tint || tintMats == null) return null;
        return levelOf.TryGetValue(r, out int k) ? tintMats[k] : null;
    }

    public void Recount()
    {
        Init();
        System.Array.Clear(Counts, 0, Counts.Length);
        System.Array.Clear(CountsInView, 0, CountsInView.Length);
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        foreach (var g in groups)
        {
            int level = ForcedLod >= 0 ? ForcedLod : LevelFor(g, cam, out _);
            Counts[level]++;
            var b = new Bounds(g.transform.TransformPoint(g.localReferencePoint), Vector3.one * g.size);
            if (GeometryUtility.TestPlanesAABB(planes, b)) CountsInView[level]++;
        }
    }

    // Which LOD the group picks for this camera. Returns lods.Length when the object is too small (culled).
    public static int LevelFor(LODGroup g, Camera cam, out float screenHeight)
    {
        var lods = g.GetLODs();
        Vector3 s = g.transform.lossyScale;
        float size = g.size * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        if (cam.orthographic)
            screenHeight = size / (2f * cam.orthographicSize);
        else
        {
            float distance = Vector3.Distance(cam.transform.position, g.transform.TransformPoint(g.localReferencePoint));
            screenHeight = size / (2f * distance * Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad));
        }
        screenHeight *= QualitySettings.lodBias;
        for (int k = 0; k < lods.Length; k++)
            if (screenHeight >= lods[k].screenRelativeTransitionHeight) return k;
        return lods.Length;
    }

    void OnDisable()
    {
        if (groups != null) foreach (var g in groups) if (g != null) g.ForceLOD(-1);
    }
}
```

### MipDebug.cs

- `MakeMipColourTexture` builds a texture with the same size as the real one and fills each mip level with one colour using `SetPixels(colors, level)`. `Apply(false)` uploads it without letting Unity regenerate the smaller levels from level 0, which would wipe out the colours. Same size means the GPU picks the same level for it, so the colour shows the level the real texture would use.
- `SetMode` changes `filterMode` and `mipMapBias` on the textures, and turns anisotropic filtering off through `QualitySettings.anisotropicFiltering` for the modes that say so. The textures are project assets, so `OnDisable` puts the original values back when you stop Play mode.

```csharp
// Ex 7. Mipmaps made visible.
// Key M swaps the texture on the mip test floors for a "mip level colour" texture: a texture of the same size whose
// mip levels are each filled with one flat colour. The GPU picks mip levels for it exactly as for the real texture,
// so the colour you see is the mip level the real texture is read from at that spot.
// Key B cycles the sampling mode of the test textures (trilinear, no anisotropic, bilinear, point, mip bias +1 / -1).
using UnityEngine;
using UnityEngine.InputSystem;

public class MipDebug : MonoBehaviour
{
    public Renderer[] surfaces;            // corridor floor and the two lanes
    public Texture2D[] textures;           // the textures that B changes (the ones on the surfaces)

    // one colour per mip level, level 0 first (a 1024 texture has 11 levels: 1024, 512, ... 1)
    public static readonly Color[] LevelColors =
    {
        new Color(1f, 0f, 0f),       // 0 red
        new Color(1f, 0.5f, 0f),     // 1 orange
        new Color(1f, 1f, 0f),       // 2 yellow
        new Color(0f, 0.85f, 0f),    // 3 green
        new Color(0f, 0.9f, 0.9f),   // 4 cyan
        new Color(0.1f, 0.3f, 1f),   // 5 blue
        new Color(0.6f, 0.2f, 1f),   // 6 purple
        new Color(1f, 0.2f, 0.8f),   // 7 pink
        new Color(1f, 1f, 1f),       // 8 white
        new Color(0.5f, 0.5f, 0.5f), // 9 grey
        new Color(0.1f, 0.1f, 0.1f), // 10 black
        new Color(0.1f, 0.1f, 0.1f), // 11 (only for textures larger than 1024)
    };

    public static readonly string[] ModeNames =
    {
        "Trilinear + anisotropic (project default)",
        "Trilinear, anisotropic off",
        "Bilinear (hard step between mips), anisotropic off",
        "Point (no filtering), anisotropic off",
        "Trilinear + aniso, mip bias +1 (blurrier)",
        "Trilinear + aniso, mip bias -1 (sharper, more shimmer)",
    };

    public bool ShowMipColours { get; private set; }
    public int Mode { get; private set; }

    Material[] originals, colourMats;
    FilterMode[] origFilter; float[] origBias;
    AnisotropicFiltering origAniso;
    bool ready;

    void Start() { Init(); }

    public void Init()
    {
        if (ready) return;
        ready = true;
        origAniso = QualitySettings.anisotropicFiltering;
        origFilter = new FilterMode[textures.Length]; origBias = new float[textures.Length];
        for (int i = 0; i < textures.Length; i++) { origFilter[i] = textures[i].filterMode; origBias[i] = textures[i].mipMapBias; }

        originals = new Material[surfaces.Length];
        colourMats = new Material[surfaces.Length];
        for (int i = 0; i < surfaces.Length; i++)
        {
            originals[i] = surfaces[i].sharedMaterial;
            var tex = originals[i].GetTexture("_BaseMap");
            colourMats[i] = new Material(originals[i]) { name = originals[i].name + " (mip colours)", hideFlags = HideFlags.DontSave };
            colourMats[i].SetTexture("_BaseMap", MakeMipColourTexture(tex.width, tex.height, tex.anisoLevel));
        }
    }

    // A texture with a full mip chain where every level is one flat colour. Same size as the real texture,
    // so the same level gets picked. SetPixels(colors, level) fills one level; Apply(false) uploads without
    // recalculating the mips (true would overwrite our colours with averages of level 0).
    public static Texture2D MakeMipColourTexture(int width, int height, int aniso)
    {
        var t = new Texture2D(width, height, TextureFormat.RGBA32, true)
        {
            name = "Mip level colours", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear,
            anisoLevel = aniso, hideFlags = HideFlags.DontSave
        };
        for (int level = 0; level < t.mipmapCount; level++)
        {
            int w = Mathf.Max(1, width >> level), h = Mathf.Max(1, height >> level);
            var c = new Color[w * h];
            for (int i = 0; i < c.Length; i++) c[i] = LevelColors[Mathf.Min(level, LevelColors.Length - 1)];
            t.SetPixels(c, level);
        }
        t.Apply(false);
        return t;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.mKey.wasPressedThisFrame) SetMipColours(!ShowMipColours);
        if (kb.bKey.wasPressedThisFrame) SetMode((Mode + 1) % ModeNames.Length);
    }

    public void SetMipColours(bool on)
    {
        Init();
        ShowMipColours = on;
        for (int i = 0; i < surfaces.Length; i++) surfaces[i].sharedMaterial = on ? colourMats[i] : originals[i];
    }

    public void SetMode(int mode)
    {
        Init();
        Mode = mode;
        bool anisoOff = mode == 1 || mode == 2 || mode == 3;
        QualitySettings.anisotropicFiltering = anisoOff ? AnisotropicFiltering.Disable : origAniso;
        var all = new System.Collections.Generic.List<Texture>(textures);
        foreach (var m in colourMats) all.Add(m.GetTexture("_BaseMap"));
        foreach (var t in all)
        {
            t.filterMode = mode == 2 ? FilterMode.Bilinear : mode == 3 ? FilterMode.Point : FilterMode.Trilinear;
            t.mipMapBias = mode == 4 ? 1f : mode == 5 ? -1f : 0f;
        }
    }

    void OnDisable()
    {
        if (!ready) return;
        // the textures are project assets: put their settings back so nothing leaks out of Play mode
        QualitySettings.anisotropicFiltering = origAniso;
        for (int i = 0; i < textures.Length; i++)
            if (textures[i] != null) { textures[i].filterMode = origFilter[i]; textures[i].mipMapBias = origBias[i]; }
        for (int i = 0; i < surfaces.Length; i++) if (surfaces[i] != null) surfaces[i].sharedMaterial = originals[i];
    }
}
```

### Ex7Hud.cs

`BuildText` makes the text, `OnGUI` draws it with rich text colours and draws the minimap texture in the corner while the frustum is frozen. Frame rate is averaged over half a second so you can read it. Triangles, draw calls and SetPass calls come from `ProfilerRecorder` with the counters "Triangles Count", "Draw Calls Count" and "SetPass Calls Count". `UnityStats` would give the same numbers, but it lives in `UnityEditor` and is not there in a build.

```csharp
// Ex 7. On-screen numbers (OnGUI): frustum counts, LOD counts, mip settings, frame rate and the engine's render counters.
// Key H hides and shows it. When the frustum is frozen the minimap is drawn in the top right corner.
// Triangles / draw calls come from ProfilerRecorder (Unity.Profiling). UnityStats would be simpler but only exists in the editor.
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

public class Ex7Hud : MonoBehaviour
{
    public FrustumDebug frustum;
    public LodDebug lod;
    public MipDebug mip;
    public int minimapPixels = 360;

    bool visible = true;
    float fps, fpsTimer; int fpsFrames;
    ProfilerRecorder trisRec, drawRec, setPassRec;
    GUIStyle style;

    void OnEnable()
    {
        trisRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        drawRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        setPassRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
    }

    void OnDisable() { trisRec.Dispose(); drawRec.Dispose(); setPassRec.Dispose(); }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.hKey.wasPressedThisFrame) visible = !visible;
        // frame rate averaged over half a second, so the number can be read
        fpsFrames++; fpsTimer += Time.unscaledDeltaTime;
        if (fpsTimer >= 0.5f) { fps = fpsFrames / fpsTimer; fpsFrames = 0; fpsTimer = 0f; }
    }

    static string Millions(long n) => n >= 1000000 ? (n / 1e6).ToString("F2") + " M" : (n / 1e3).ToString("F0") + " k";

    // The text is built in one place so the capture script can show exactly the same lines.
    public string BuildText()
    {
        var sb = new StringBuilder();
        string fpsText = Application.isPlaying ? $"{fps:F0} fps ({1000f / Mathf.Max(fps, 0.01f):F1} ms)" : "n/a (edit mode capture)";
        sb.AppendLine($"<b>Exercise 7: culling, LOD, mipmaps</b>    {fpsText}");
        sb.AppendLine("<b>Frustum culling</b>");
        sb.AppendLine($"  Objects in scene: {frustum.ObjectCount}");
        sb.AppendLine($"  Inside camera frustum (own 6-plane AABB test): {frustum.InsideLive}");
        sb.AppendLine($"  Drawn by the engine (Renderer.isVisible): {frustum.EngineVisible}" + (frustum.Frozen ? "  (minimap camera counts too)" : ""));
        sb.AppendLine(frustum.Frozen
            ? $"  <color=#ffd21a>C: frustum FROZEN</color>, inside: <color=#33dd44>{frustum.InsideFrozen}</color>, outside: <color=#ff4433>{frustum.ObjectCount - frustum.InsideFrozen}</color>"
            : "  C: freeze frustum (off)");
        string forced = lod.ForcedLod < 0 ? "automatic" : "forced LOD" + lod.ForcedLod;
        sb.AppendLine($"<b>LOD</b>   L: {forced}   T: tint {(lod.Tint ? "on" : "off")}");
        var c = lod.Counts; var v = lod.CountsInView;
        sb.AppendLine($"  all Nisse:  <color=#33dd44>LOD0 {c[0]}</color>  <color=#f2d91a>LOD1 {c[1]}</color>  <color=#ff800d>LOD2 {c[2]}</color>  <color=#ff3322>LOD3 {c[3]}</color>  culled {c[4]}");
        sb.AppendLine($"  in view:    <color=#33dd44>LOD0 {v[0]}</color>  <color=#f2d91a>LOD1 {v[1]}</color>  <color=#ff800d>LOD2 {v[2]}</color>  <color=#ff3322>LOD3 {v[3]}</color>  culled {v[4]}");
        sb.AppendLine($"  triangles of the drawn meshes: {Millions(frustum.DrawnTriangles)}");
        sb.AppendLine("<b>Mipmaps</b>");
        sb.AppendLine($"  M: mip level colours {(mip.ShowMipColours ? "on" : "off")}   B: {MipDebug.ModeNames[mip.Mode]}");
        if (mip.ShowMipColours)
        {
            sb.Append("  levels: ");
            for (int i = 0; i <= 10; i++) sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(MipDebug.LevelColors[i])}>{i}</color> ");
            sb.AppendLine();
        }
        if (trisRec.Valid && Application.isPlaying)
            sb.AppendLine($"<b>GPU work</b> (ProfilerRecorder): {Millions(trisRec.LastValue)} triangles, {drawRec.LastValue} draw calls, {setPassRec.LastValue} SetPass calls");
        else
            sb.AppendLine("<b>GPU work</b> (ProfilerRecorder): only counted in Play mode");
        sb.Append("WASD walk, Shift run, mouse look, Esc free cursor, 1/2/3 jump to area, H hide this");
        return sb.ToString();
    }

    void OnGUI()
    {
        if (!visible) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
            style.normal.textColor = Color.white;
        }
        string text = BuildText();
        var size = style.CalcSize(new GUIContent(text));
        GUI.Box(new Rect(8, 8, size.x + 16, size.y + 12), GUIContent.none);
        GUI.Box(new Rect(8, 8, size.x + 16, size.y + 12), GUIContent.none);   // twice for a darker box
        GUI.Label(new Rect(16, 14, size.x, size.y), text, style);

        if (frustum.Frozen && frustum.Minimap != null)
        {
            var r = new Rect(Screen.width - minimapPixels - 10, 10, minimapPixels, minimapPixels);
            GUI.Box(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), GUIContent.none);
            GUI.DrawTexture(r, frustum.Minimap);
            GUI.Label(new Rect(r.x + 6, r.yMax - 24, r.width, 22), "<b>top view, frozen frustum</b>", style);
        }
    }
}
```

## What was hard or went wrong

- **My own count against the engine's.** With only the player camera rendering, the two numbers matched at the spawn point (103 and 103). At the step4 pose they were 156 against 153. The difference is the plinths: my box includes the plinth, which is wider than the Nisse, while the engine's own culling tests each LOD mesh with its own, tighter box. So three Nisse had their plinth edge inside the frustum but the Nisse itself outside.
- **`Renderer.isVisible` counts every camera.** While the frustum is frozen, the minimap camera sees the whole field, so the engine number jumps (318 in step4). The HUD says so in that line. In the editor the Scene view is a camera too: if it is open next to the Game view, it adds to the number.
- **`isVisible` in the headless capture.** In edit mode no frames pass between the renders of the capture script, and `isVisible` was never reset, so it collected every render since the start. I moved the HUD shot to the start, right after the warm up renders from the same spot, so its numbers are the real ones for that pose. In Play mode it is reset every frame.
- **GPU Resident Drawer.** The pipeline asset has it on (it draws plain MeshRenderers in big batches). I was not sure `ForceLOD` and `isVisible` would still work with it. Both do: the matching counts above, and the LOD3 pictures that clearly differ from LOD0.
- **The LOD meshes do not quite match the current Nisse.obj.** `Nisse.obj` on this branch has been changed since the LOD meshes were made. Their bounds differ: the old LOD meshes are 0.41 m deep and reach 6 cm lower, the new scan is 0.24 m deep. At first I scaled each LOD to 1 m on its own, which made LOD0 about 6 % bigger than the others. Since the files share the same coordinates (same x centre), I gave all four the transform of LOD0 instead. In the LOD0 against LOD3 picture they line up, but the swap could show a small jump at the bottom.
- **The first lane texture showed the opposite of what I expected.** I started with a straight 8 px checker. Its cells line up exactly with the 2 x 2 blocks that Box averages, so Box kept a perfect checker down to level 3 and then went flat grey, while Kaiser faded it out step by step. Box looked "sharper" there, but only because that pattern is a special case for it. I changed to a checker turned by about 23 degrees, which has edges at all positions like real textures do. With that one the numbers and the pictures show what the filters are known for.
- **The HUD in the capture.** OnGUI needs a Game view, which a headless editor does not have. The capture uses a TextMesh in front of the camera with the HUD's own text. My first try placed the dark box wrong, because I read the text size from `renderer.bounds`, which is a box in world axes and gets wider when the camera is turned. `renderer.localBounds` (in the text's own axes) fixed it.
- **Anisotropic filtering is forced on in this project.** That moves the mip levels about two steps further away on the floors (see the extra colour images) and also hides part of the Box and Kaiser difference. That is why there is a B mode with anisotropic off.

## What I did not verify

- **Play mode as a whole.** I never pressed Play. Walking with WASD, Shift, mouse look, Esc and the cursor lock, the 1/2/3 teleports, and all the key toggles (C, T, L, M, B, H) are written but not run. Every capture drives the same methods directly, so the logic behind each key is tested, but not the key handling, the CharacterController against the walls, or the gravity.
- **The OnGUI HUD and minimap layout** in a real Game view. The capture shows the same text, drawn another way.
- **Frame rate.** A headless capture has no frame loop, so I have no fps or ms numbers. The `ProfilerRecorder` lines (triangles, draw calls, SetPass calls) only fill in during Play mode, so I have not seen their values. With 356 Nisse, about 4 at LOD0 and about 1 million triangles in view, I expect it to run fine, but that is a guess.
- **That the texture settings come back after Play.** `MipDebug.OnDisable` restores filter mode, mip bias and the anisotropic setting. In the capture they were reset by hand and the `git status` afterwards shows no change to `ProjectSettings`, but I have not watched it happen in Play mode.

## Files

- Scene: `Assets/Scenes/Ex 7 Culling and Mips.unity`
- Scripts: `Assets/Scripts/Ex7/FirstPersonWalker.cs`, `NisseGrid.cs`, `FrustumDebug.cs`, `LodDebug.cs`, `MipDebug.cs`, `Ex7Hud.cs`
- Builder: `Assets/Editor/RtcgEx7Scene.cs` (`BuildScene`, `CaptureSteps`)
- Prefab: `Assets/Prefabs/Ex7/Ex7_NisseLOD.prefab`
- Textures: `Assets/Textures/Ex7/` (4 PNG files). Materials: `Assets/Materials/Ex7/`
- Changed: `Assets/Nisse/Nisse_diffuse.jpeg.meta` (mip filter Box to Kaiser, nothing else)
- Captures: `Captures/Ex07_Steps/`, logs `Captures/_logs/ex7_build.log` and `ex7_cap.log`

Run headless (Unity must be closed):

```
Unity.exe -batchmode -quit -projectPath <project> -executeMethod RtcgEx7Scene.BuildScene
Unity.exe -batchmode -quit -projectPath <project> -executeMethod RtcgEx7Scene.CaptureSteps
```
